// 真机自检程序：直接在设备上跑一遍 MediaCodec 硬编 + MediaMuxer 封装，
// 产出一个可播放的 MP4，用 24 帧渐变色验证管线是否端到端可用。
//
// 与 encoder_android.cpp 走同一套 API 调用序列（NV12 协商、AAC、muxer 起停），
// 只是把 Unity 的帧提交换成本地的生成帧。

#include <media/NdkMediaCodec.h>
#include <media/NdkMediaFormat.h>
#include <media/NdkMediaMuxer.h>
#include <media/NdkMediaError.h>

#include <fcntl.h>
#include <unistd.h>

#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <vector>

namespace {

constexpr int kWidth = 640;
constexpr int kHeight = 360;
constexpr int kFps = 30;
constexpr int kFrames = 30;
constexpr int kBitrate = 4000000;
constexpr int kSampleRate = 48000;
constexpr int kChannels = 2;
constexpr int kSamplesPerBuffer = 1024;

const char* StatusText(media_status_t status) {
    switch (status) {
        case AMEDIA_OK: return "ok";
        case AMEDIA_ERROR_UNKNOWN: return "unknown";
        case AMEDIA_ERROR_MALFORMED: return "malformed";
        case AMEDIA_ERROR_UNSUPPORTED: return "unsupported";
        case AMEDIA_ERROR_INVALID_OBJECT: return "invalid object";
        case AMEDIA_ERROR_INVALID_PARAMETER: return "invalid parameter";
        case AMEDIA_ERROR_INVALID_OPERATION: return "invalid operation";
        case AMEDIA_ERROR_END_OF_STREAM: return "end of stream";
        case AMEDIA_ERROR_IO: return "io";
        case AMEDIA_ERROR_WOULD_BLOCK: return "would block";
        default: return "other";
    }
}

void RgbaToNv12(const uint8_t* rgba, uint8_t* out, int width, int height) {
    const int stride = width;
    uint8_t* y = out;
    uint8_t* uv = out + static_cast<size_t>(stride) * height;

    for (int row = 0; row < height; ++row) {
        const uint8_t* src = rgba + static_cast<size_t>(height - 1 - row) * width * 4;
        uint8_t* dst = y + static_cast<size_t>(row) * stride;

        for (int col = 0; col < width; ++col) {
            const int r = src[col * 4 + 0];
            const int g = src[col * 4 + 1];
            const int b = src[col * 4 + 2];
            int luma = ((66 * r + 129 * g + 25 * b + 128) >> 8) + 16;
            dst[col] = static_cast<uint8_t>(luma < 0 ? 0 : (luma > 255 ? 255 : luma));
        }
    }

    for (int row = 0; row < height; row += 2) {
        const uint8_t* src0 = rgba + static_cast<size_t>(height - 1 - row) * width * 4;
        const uint8_t* src1 = rgba + static_cast<size_t>(height - 2 - row) * width * 4;
        const int uvRow = row / 2;

        for (int col = 0; col < width; col += 2) {
            int sumR = 0, sumG = 0, sumB = 0;

            for (int dy = 0; dy < 2; ++dy) {
                const uint8_t* src = dy == 0 ? src0 : src1;
                const int base = col * 4;
                sumR += src[base + 0] + src[base + 4 + 0];
                sumG += src[base + 1] + src[base + 4 + 1];
                sumB += src[base + 2] + src[base + 4 + 2];
            }

            const int r = sumR / 4, g = sumG / 4, b = sumB / 4;
            int cb = ((-38 * r - 74 * g + 112 * b + 128) >> 8) + 128;
            int cr = ((112 * r - 94 * g - 18 * b + 128) >> 8) + 128;

            const size_t idx = static_cast<size_t>(uvRow) * stride + (col / 2) * 2;
            uv[idx] = static_cast<uint8_t>(cb < 0 ? 0 : (cb > 255 ? 255 : cb));
            uv[idx + 1] = static_cast<uint8_t>(cr < 0 ? 0 : (cr > 255 ? 255 : cr));
        }
    }
}

}  // namespace

int main(int argc, char** argv) {
    const char* path = argc > 1 ? argv[1] : "/data/local/tmp/test_hw.mp4";

    printf("[test] output: %s\n", path);
    printf("[test] %dx%d @%dfps, %d frames, %d bps\n", kWidth, kHeight, kFps, kFrames, kBitrate);

    // ---------------------------------------------------------------- 视频编码器

    AMediaCodec* video = AMediaCodec_createEncoderByType("video/avc");

    if (!video) {
        printf("[test] FAIL: no H.264 encoder\n");
        return 1;
    }

    int32_t chosenFormat = 21;
    media_status_t status = AMEDIA_ERROR_UNKNOWN;

    for (int32_t candidate : {21, 19, 0x7F420888}) {
        AMediaFormat* format = AMediaFormat_new();
        AMediaFormat_setString(format, AMEDIAFORMAT_KEY_MIME, "video/avc");
        AMediaFormat_setInt32(format, AMEDIAFORMAT_KEY_WIDTH, kWidth);
        AMediaFormat_setInt32(format, AMEDIAFORMAT_KEY_HEIGHT, kHeight);
        AMediaFormat_setInt32(format, AMEDIAFORMAT_KEY_COLOR_FORMAT, candidate);
        AMediaFormat_setInt32(format, AMEDIAFORMAT_KEY_BIT_RATE, kBitrate);
        AMediaFormat_setInt32(format, AMEDIAFORMAT_KEY_FRAME_RATE, kFps);
        AMediaFormat_setInt32(format, AMEDIAFORMAT_KEY_I_FRAME_INTERVAL, 1);

        status = AMediaCodec_configure(video, format, nullptr, nullptr,
                                      AMEDIACODEC_CONFIGURE_FLAG_ENCODE);
        AMediaFormat_delete(format);

        printf("[test] color format %d -> %s\n", candidate, StatusText(status));

        if (status == AMEDIA_OK) {
            chosenFormat = candidate;
            break;
        }
    }

    if (status != AMEDIA_OK) {
        printf("[test] FAIL: configure video\n");
        return 1;
    }

    if (AMediaCodec_start(video) != AMEDIA_OK) {
        printf("[test] FAIL: start video\n");
        return 1;
    }

    // ---------------------------------------------------------------- 音频编码器

    AMediaCodec* audio = AMediaCodec_createEncoderByType("audio/mp4a-latm");
    bool audioReady = false;

    if (audio) {
        AMediaFormat* format = AMediaFormat_new();
        AMediaFormat_setString(format, AMEDIAFORMAT_KEY_MIME, "audio/mp4a-latm");
        AMediaFormat_setInt32(format, AMEDIAFORMAT_KEY_SAMPLE_RATE, kSampleRate);
        AMediaFormat_setInt32(format, AMEDIAFORMAT_KEY_CHANNEL_COUNT, kChannels);
        AMediaFormat_setInt32(format, AMEDIAFORMAT_KEY_BIT_RATE, 128000);
        AMediaFormat_setInt32(format, AMEDIAFORMAT_KEY_AAC_PROFILE, 2);
        AMediaFormat_setInt32(format, AMEDIAFORMAT_KEY_MAX_INPUT_SIZE,
                              kSamplesPerBuffer * kChannels * 2);

        status = AMediaCodec_configure(audio, format, nullptr, nullptr,
                                       AMEDIACODEC_CONFIGURE_FLAG_ENCODE);
        AMediaFormat_delete(format);

        printf("[test] AAC configure -> %s\n", StatusText(status));

        if (status == AMEDIA_OK && AMediaCodec_start(audio) == AMEDIA_OK) {
            audioReady = true;
        } else {
            AMediaCodec_delete(audio);
            audio = nullptr;
        }
    }

    // ---------------------------------------------------------------- 复用器

    int fd = ::open(path, O_CREAT | O_TRUNC | O_RDWR, 0644);

    if (fd < 0) {
        printf("[test] FAIL: cannot open %s\n", path);
        return 1;
    }

    AMediaMuxer* muxer = AMediaMuxer_new(fd, AMEDIAMUXER_OUTPUT_FORMAT_MPEG_4);

    if (!muxer) {
        printf("[test] FAIL: muxer\n");
        return 1;
    }

    int32_t videoTrack = -1;
    int32_t audioTrack = -1;
    bool muxerStarted = false;
    int encoded = 0;

    // ---------------------------------------------------------------- 主循环

    std::vector<uint8_t> rgba(static_cast<size_t>(kWidth) * kHeight * 4);
    std::vector<uint8_t> yuv(static_cast<size_t>(kWidth) * kHeight * 3 / 2);
    int64_t audioSampleCursor = 0;

    for (int frame = 0; frame < kFrames; ++frame) {
        // 生成渐变帧：整段从蓝到红，便于肉眼确认帧序正确
        const int phase = frame * 8;

        for (int y = 0; y < kHeight; ++y) {
            for (int x = 0; x < kWidth; ++x) {
                uint8_t* pixel = &rgba[(static_cast<size_t>(y) * kWidth + x) * 4];
                pixel[0] = static_cast<uint8_t>((x * 255 / kWidth + phase) & 0xFF);
                pixel[1] = static_cast<uint8_t>((y * 255 / kHeight) & 0xFF);
                pixel[2] = static_cast<uint8_t>((255 - (frame * 255 / kFrames)) & 0xFF);
                pixel[3] = 255;
            }
        }

        // 送视频（可能因为没有空输入缓冲而重试）
        bool submitted = false;
        int retries = 0;

        while (!submitted && retries < 200) {
            ssize_t index = AMediaCodec_dequeueInputBuffer(video, 10000);

            if (index >= 0) {
                size_t capacity = 0;
                uint8_t* buffer = AMediaCodec_getInputBuffer(video, static_cast<size_t>(index),
                                                            &capacity);

                if (!buffer) {
                    printf("[test] FAIL: video input buffer null\n");
                    return 1;
                }

                RgbaToNv12(rgba.data(), yuv.data(), kWidth, kHeight);

                const size_t needed = yuv.size();

                if (capacity < needed) {
                    printf("[test] FAIL: video buffer %zu < %zu\n", capacity, needed);
                    return 1;
                }

                std::memcpy(buffer, yuv.data(), needed);

                const int64_t pts = frame * 1000000LL / kFps;

                if (AMediaCodec_queueInputBuffer(video, static_cast<size_t>(index), 0, needed,
                                                 pts, 0) != AMEDIA_OK) {
                    printf("[test] FAIL: queue video\n");
                    return 1;
                }

                submitted = true;
            } else {
                retries++;
            }
        }

        if (!submitted) {
            printf("[test] FAIL: could not submit frame %d\n", frame);
            return 1;
        }

        // 送音频：每帧对应 sampleRate/fps 个采样
        if (audioReady) {
            int wanted = (frame + 1) * kSampleRate / kFps;

            while (audioSampleCursor < wanted) {
                int count = kSamplesPerBuffer;

                if (audioSampleCursor + count > wanted) count = static_cast<int>(wanted - audioSampleCursor);

                ssize_t index = AMediaCodec_dequeueInputBuffer(audio, 10000);

                if (index < 0) break;

                size_t capacity = 0;
                uint8_t* buffer = AMediaCodec_getInputBuffer(audio, static_cast<size_t>(index),
                                                            &capacity);

                if (!buffer) break;

                const size_t bytes = static_cast<size_t>(count) * kChannels * 2;

                if (bytes > capacity) break;

                auto* pcm = reinterpret_cast<int16_t*>(buffer);

                for (int i = 0; i < count; ++i) {
                    // 440 Hz 正弦，验证音频轨有内容
                    const double t = static_cast<double>(audioSampleCursor + i) / kSampleRate;
                    const int16_t sample = static_cast<int16_t>(std::sin(t * 2 * M_PI * 440.0) * 8000);
                    pcm[i * 2 + 0] = sample;
                    pcm[i * 2 + 1] = sample;
                }

                const int64_t pts = audioSampleCursor * 1000000LL / kSampleRate;
                AMediaCodec_queueInputBuffer(audio, static_cast<size_t>(index), 0, bytes, pts, 0);
                audioSampleCursor += count;
            }
        }

        // 排空两个编码器的输出
        for (int pass = 0; pass < 2; ++pass) {
            AMediaCodec* codec = pass == 0 ? video : audio;

            if (!codec) continue;

            AMediaCodecBufferInfo info;
            ssize_t index = AMediaCodec_dequeueOutputBuffer(codec, &info, 0);

            while (index >= 0 || index == AMEDIACODEC_INFO_OUTPUT_FORMAT_CHANGED) {
                if (index == AMEDIACODEC_INFO_OUTPUT_FORMAT_CHANGED) {
                    AMediaFormat* format = AMediaCodec_getOutputFormat(codec);

                    if (pass == 0 && videoTrack < 0) {
                        videoTrack = static_cast<int32_t>(AMediaMuxer_addTrack(muxer, format));
                        printf("[test] video track = %d\n", videoTrack);
                    } else if (pass == 1 && audioTrack < 0) {
                        audioTrack = static_cast<int32_t>(AMediaMuxer_addTrack(muxer, format));
                        printf("[test] audio track = %d\n", audioTrack);
                    }

                    AMediaFormat_delete(format);

                    if (videoTrack >= 0 && (!audioReady || audioTrack >= 0) && !muxerStarted) {
                        if (AMediaMuxer_start(muxer) == AMEDIA_OK) {
                            muxerStarted = true;
                            printf("[test] muxer started\n");
                        }
                    }

                    index = AMediaCodec_dequeueOutputBuffer(codec, &info, 0);
                    continue;
                }

                if (info.size > 0 && muxerStarted) {
                    size_t capacity = 0;
                    uint8_t* buffer = AMediaCodec_getOutputBuffer(codec, static_cast<size_t>(index),
                                                                 &capacity);

                    if (buffer) {
                        const int32_t track = pass == 0 ? videoTrack : audioTrack;

                        if (track >= 0) {
                            AMediaMuxer_writeSampleData(muxer, static_cast<size_t>(track), buffer, &info);

                            if (pass == 0 && !(info.flags & AMEDIACODEC_BUFFER_FLAG_CODEC_CONFIG)) {
                                encoded++;
                            }
                        }
                    }
                }

                AMediaCodec_releaseOutputBuffer(codec, static_cast<size_t>(index), false);
                index = AMediaCodec_dequeueOutputBuffer(codec, &info, 0);
            }
        }
    }

    // ---------------------------------------------------------------- 收尾

    printf("[test] submitted %d frames, flushing encoders\n", kFrames);

    // 两个编码器送 EOS，然后排空到 EOF
    for (int pass = 0; pass < 2; ++pass) {
        AMediaCodec* codec = pass == 0 ? video : audio;

        if (!codec) continue;

        ssize_t index = AMediaCodec_dequeueInputBuffer(codec, 1000000);

        if (index >= 0) {
            AMediaCodec_queueInputBuffer(codec, static_cast<size_t>(index), 0, 0, 0,
                                         AMEDIACODEC_BUFFER_FLAG_END_OF_STREAM);
        }

        const int32_t track = pass == 0 ? videoTrack : audioTrack;
        bool eos = false;
        int guard = 0;

        while (!eos && guard++ < 5000) {
            AMediaCodecBufferInfo info;
            ssize_t out = AMediaCodec_dequeueOutputBuffer(codec, &info, 20000);

            if (out == AMEDIACODEC_INFO_TRY_AGAIN_LATER) continue;

            if (out == AMEDIACODEC_INFO_OUTPUT_FORMAT_CHANGED) {
                AMediaFormat* format = AMediaCodec_getOutputFormat(codec);
                AMediaFormat_delete(format);
                continue;
            }

            if (out < 0) break;

            if (info.size > 0 && muxerStarted && track >= 0) {
                size_t capacity = 0;
                uint8_t* buffer = AMediaCodec_getOutputBuffer(codec, static_cast<size_t>(out),
                                                             &capacity);

                if (buffer) {
                    AMediaMuxer_writeSampleData(muxer, static_cast<size_t>(track), buffer, &info);

                    if (pass == 0 && !(info.flags & AMEDIACODEC_BUFFER_FLAG_CODEC_CONFIG)) {
                        encoded++;
                    }
                }
            }

            if (info.flags & AMEDIACODEC_BUFFER_FLAG_END_OF_STREAM) eos = true;
            AMediaCodec_releaseOutputBuffer(codec, static_cast<size_t>(out), false);
        }
    }

    if (muxerStarted) AMediaMuxer_stop(muxer);

    AMediaMuxer_delete(muxer);
    AMediaCodec_stop(video);
    AMediaCodec_delete(video);

    if (audio) {
        AMediaCodec_stop(audio);
        AMediaCodec_delete(audio);
    }

    ::close(fd);

    printf("[test] DONE: encoded=%d frames, colorFormat=%d, audio=%s\n",
           encoded, chosenFormat, audioReady ? "yes" : "no");

    return encoded == kFrames ? 0 : 1;
}
