// Android 硬件编码桥：Unity RenderTexture(RGBA) -> MediaCodec(H.264/H.265 硬编) -> MediaMuxer(MP4)
//
// 为什么不用 FFmpeg：libx264 在手机上是纯软编，1080p60 达不到实时；MediaCodec 走
// 芯片 VPU，CPU 几乎不占。libmediandk 从 API 21 起是系统库，不增加 APK 体积。
//
// 线程模型：Unity 主线程只做 SubmitRGBA（入队即返回，队列满时阻塞以形成背压），
// 编码/复用全在内部 worker 线程，避免卡渲染。

#include <media/NdkMediaCodec.h>
#include <media/NdkMediaFormat.h>
#include <media/NdkMediaMuxer.h>
#include <media/NdkMediaError.h>

#include <android/log.h>
#include <fcntl.h>
#include <unistd.h>

#include <atomic>
#include <condition_variable>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <deque>
#include <memory>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

#define LOG_TAG "UmaVideoAndroid"
#define LOGI(...) __android_log_print(ANDROID_LOG_INFO, LOG_TAG, __VA_ARGS__)
#define LOGE(...) __android_log_print(ANDROID_LOG_ERROR, LOG_TAG, __VA_ARGS__)

#define API extern "C" __attribute__((visibility("default")))

namespace {

// MediaCodec 颜色格式：21 = NV12(semi-planar)，19 = I420(planar)，0x7F420888 = flexible
constexpr int32_t kColorFormatSemiPlanar = 21;
constexpr int32_t kColorFormatPlanar = 19;
constexpr int32_t kColorFormatFlexible = 0x7F420888;

constexpr int32_t kAacObjectLC = 2;
constexpr size_t kMaxQueuedFrames = 4;
constexpr size_t kAudioSamplesPerBuffer = 1024;
constexpr int64_t kDequeueTimeoutUs = 10000;

struct VideoFrame {
    std::vector<uint8_t> rgba;
};

struct AudioChunk {
    std::vector<int16_t> pcm;  // interleaved stereo
    int64_t timestampUs = 0;
};

struct Encoder {
    int width = 0;
    int height = 0;
    int fps = 30;
    int bitrateBps = 20 * 1000 * 1000;
    bool hevc = false;
    int sampleRate = 48000;
    int channels = 2;

    std::string path;
    int fd = -1;

    AMediaCodec* videoCodec = nullptr;
    AMediaCodec* audioCodec = nullptr;
    AMediaMuxer* muxer = nullptr;
    int32_t videoTrack = -1;
    int32_t audioTrack = -1;
    bool muxerStarted = false;
    int32_t colorFormat = kColorFormatSemiPlanar;

    std::thread worker;

    std::mutex lock;
    std::condition_variable videoReady;
    std::condition_variable videoSpace;
    std::condition_variable audioReady;

    std::deque<VideoFrame> videoQueue;
    std::deque<AudioChunk> audioQueue;

    bool audioEnabled = false;
    bool stopping = false;   // 收到 Finish：不再接受新帧
    bool aborting = false;   // 收到 Cancel
    bool finished = false;   // worker 已退出
    bool failed = false;

    int64_t submittedFrames = 0;
    int64_t encodedFrames = 0;
    int64_t pushedAudioSamples = 0;

    std::string error;

    void Fail(const std::string& message) {
        {
            std::lock_guard<std::mutex> guard(lock);
            if (!failed) {
                failed = true;
                error = message;
            }
            stopping = true;
        }
        videoReady.notify_all();
        videoSpace.notify_all();
        audioReady.notify_all();
    }
};

std::string StatusText(media_status_t status) {
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
        default: return "status " + std::to_string(static_cast<int>(status));
    }
}

bool Check(Encoder* enc, media_status_t status, const char* what) {
    if (status == AMEDIA_OK) return true;
    enc->Fail(std::string(what) + " failed: " + StatusText(status));
    return false;
}

// ---------------------------------------------------------------- 视频编码器

bool ConfigureVideoCodec(Encoder* enc) {
    const char* mime = enc->hevc ? "video/hevc" : "video/avc";
    enc->videoCodec = AMediaCodec_createEncoderByType(mime);
    if (!enc->videoCodec) {
        enc->Fail(std::string("no hardware encoder for ") + mime);
        return false;
    }

    // 优先 NV12，不行再退 I420；两者都不支持就报错，避免写出花屏。
    const int32_t candidates[] = {kColorFormatSemiPlanar, kColorFormatPlanar};
    media_status_t last = AMEDIA_ERROR_UNKNOWN;

    for (int32_t candidate : candidates) {
        AMediaFormat* format = AMediaFormat_new();
        AMediaFormat_setString(format, AMEDIAFORMAT_KEY_MIME, mime);
        AMediaFormat_setInt32(format, AMEDIAFORMAT_KEY_WIDTH, enc->width);
        AMediaFormat_setInt32(format, AMEDIAFORMAT_KEY_HEIGHT, enc->height);
        AMediaFormat_setInt32(format, AMEDIAFORMAT_KEY_COLOR_FORMAT, candidate);
        AMediaFormat_setInt32(format, AMEDIAFORMAT_KEY_BIT_RATE, enc->bitrateBps);
        AMediaFormat_setInt32(format, AMEDIAFORMAT_KEY_FRAME_RATE, enc->fps);
        AMediaFormat_setInt32(format, AMEDIAFORMAT_KEY_I_FRAME_INTERVAL, 1);

        last = AMediaCodec_configure(enc->videoCodec, format, nullptr, nullptr,
                                     AMEDIACODEC_CONFIGURE_FLAG_ENCODE);
        AMediaFormat_delete(format);

        if (last == AMEDIA_OK) {
            enc->colorFormat = candidate;
            break;
        }
    }

    if (last != AMEDIA_OK) {
        // 极少数设备只报 flexible，退回它并依赖厂商默认布局。
        AMediaFormat* format = AMediaFormat_new();
        AMediaFormat_setString(format, AMEDIAFORMAT_KEY_MIME, mime);
        AMediaFormat_setInt32(format, AMEDIAFORMAT_KEY_WIDTH, enc->width);
        AMediaFormat_setInt32(format, AMEDIAFORMAT_KEY_HEIGHT, enc->height);
        AMediaFormat_setInt32(format, AMEDIAFORMAT_KEY_COLOR_FORMAT, kColorFormatFlexible);
        AMediaFormat_setInt32(format, AMEDIAFORMAT_KEY_BIT_RATE, enc->bitrateBps);
        AMediaFormat_setInt32(format, AMEDIAFORMAT_KEY_FRAME_RATE, enc->fps);
        AMediaFormat_setInt32(format, AMEDIAFORMAT_KEY_I_FRAME_INTERVAL, 1);
        last = AMediaCodec_configure(enc->videoCodec, format, nullptr, nullptr,
                                     AMEDIACODEC_CONFIGURE_FLAG_ENCODE);
        AMediaFormat_delete(format);

        if (last != AMEDIA_OK) {
            enc->Fail("hardware encoder rejected every color format: " + StatusText(last));
            return false;
        }

        enc->colorFormat = kColorFormatFlexible;
    }

    if (!Check(enc, AMediaCodec_start(enc->videoCodec), "AMediaCodec_start(video)")) return false;

    LOGI("video encoder ready: %dx%d @%dfps, %d bps, %s, color=%d",
         enc->width, enc->height, enc->fps, enc->bitrateBps,
         enc->hevc ? "hevc" : "h264", enc->colorFormat);
    return true;
}

bool ConfigureAudioCodec(Encoder* enc) {
    enc->audioCodec = AMediaCodec_createEncoderByType("audio/mp4a-latm");
    if (!enc->audioCodec) {
        LOGI("no AAC encoder; exporting video without audio");
        return true;
    }

    AMediaFormat* format = AMediaFormat_new();
    AMediaFormat_setString(format, AMEDIAFORMAT_KEY_MIME, "audio/mp4a-latm");
    AMediaFormat_setInt32(format, AMEDIAFORMAT_KEY_SAMPLE_RATE, enc->sampleRate);
    AMediaFormat_setInt32(format, AMEDIAFORMAT_KEY_CHANNEL_COUNT, enc->channels);
    AMediaFormat_setInt32(format, AMEDIAFORMAT_KEY_BIT_RATE, 192000);
    AMediaFormat_setInt32(format, AMEDIAFORMAT_KEY_AAC_PROFILE, kAacObjectLC);
    AMediaFormat_setInt32(format, AMEDIAFORMAT_KEY_MAX_INPUT_SIZE,
                          static_cast<int32_t>(kAudioSamplesPerBuffer * enc->channels * 2));

    media_status_t status = AMediaCodec_configure(enc->audioCodec, format, nullptr, nullptr,
                                                  AMEDIACODEC_CONFIGURE_FLAG_ENCODE);
    AMediaFormat_delete(format);

    if (status != AMEDIA_OK) {
        LOGE("AAC configure failed (%s); continuing video-only", StatusText(status).c_str());
        AMediaCodec_delete(enc->audioCodec);
        enc->audioCodec = nullptr;
        return true;
    }

    if (AMediaCodec_start(enc->audioCodec) != AMEDIA_OK) {
        LOGE("AAC start failed; continuing video-only");
        AMediaCodec_delete(enc->audioCodec);
        enc->audioCodec = nullptr;
        return true;
    }

    enc->audioEnabled = true;
    LOGI("audio encoder ready: AAC-LC %d Hz x%d", enc->sampleRate, enc->channels);
    return true;
}

// ---------------------------------------------------------------- YUV 转换

void RgbaToYuv(Encoder* enc, const uint8_t* rgba, uint8_t* out) {
    const int width = enc->width;
    const int height = enc->height;
    const int stride = width;

    uint8_t* y = out;
    uint8_t* u = nullptr;
    uint8_t* v = nullptr;

    const bool semiPlanar = enc->colorFormat != kColorFormatPlanar;
    const int ySize = stride * height;

    if (semiPlanar) {
        u = out + ySize;
        v = u + 1;
    } else {
        u = out + ySize;
        v = u + (stride / 2) * (height / 2);
    }

    // Unity 的 RenderTexture 原点在左下，视频原点在左上：这里按行倒序读取。
    for (int row = 0; row < height; ++row) {
        const uint8_t* src = rgba + static_cast<size_t>(height - 1 - row) * width * 4;
        uint8_t* dst = y + static_cast<size_t>(row) * stride;

        for (int col = 0; col < width; ++col) {
            const int r = src[col * 4 + 0];
            const int g = src[col * 4 + 1];
            const int b = src[col * 4 + 2];

            // BT.601 有限范围，与 Android 编码器默认一致
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

            const int r = sumR / 4;
            const int g = sumG / 4;
            const int b = sumB / 4;

            int cb = ((-38 * r - 74 * g + 112 * b + 128) >> 8) + 128;
            int cr = ((112 * r - 94 * g - 18 * b + 128) >> 8) + 128;

            cb = cb < 0 ? 0 : (cb > 255 ? 255 : cb);
            cr = cr < 0 ? 0 : (cr > 255 ? 255 : cr);

            const size_t uvIndex = semiPlanar
                ? static_cast<size_t>(uvRow) * stride + (col / 2) * 2
                : static_cast<size_t>(uvRow) * (stride / 2) + (col / 2);

            if (semiPlanar) {
                u[uvIndex] = static_cast<uint8_t>(cb);
                v[uvIndex] = static_cast<uint8_t>(cr);
            } else {
                u[uvIndex] = static_cast<uint8_t>(cb);
                v[uvIndex] = static_cast<uint8_t>(cr);
            }
        }
    }
}

// ---------------------------------------------------------------- 编码送数据

bool FeedOneVideoFrame(Encoder* enc, const VideoFrame& frame, int64_t frameIndex) {
    ssize_t index = AMediaCodec_dequeueInputBuffer(enc->videoCodec, kDequeueTimeoutUs);
    if (index == AMEDIACODEC_INFO_TRY_AGAIN_LATER) return false;
    if (index < 0) {
        enc->Fail("video dequeueInputBuffer: " + std::to_string(index));
        return false;
    }

    size_t capacity = 0;
    uint8_t* buffer = AMediaCodec_getInputBuffer(enc->videoCodec,
                                                static_cast<size_t>(index), &capacity);
    if (!buffer) {
        enc->Fail("video getInputBuffer returned null");
        return false;
    }

    const size_t needed = static_cast<size_t>(enc->width) * enc->height * 3 / 2;
    if (capacity < needed) {
        enc->Fail("video input buffer too small: " + std::to_string(capacity) + " < "
                  + std::to_string(needed));
        return false;
    }

    RgbaToYuv(enc, frame.rgba.data(), buffer);

    const int64_t pts = frameIndex * 1000000LL / enc->fps;
    media_status_t status = AMediaCodec_queueInputBuffer(enc->videoCodec,
                                                        static_cast<size_t>(index), 0, needed,
                                                        static_cast<uint64_t>(pts), 0);
    return Check(enc, status, "video queueInputBuffer");
}

bool FeedAudio(Encoder* enc) {
    if (!enc->audioEnabled) {
        // 没有音频编码器就直接丢弃，避免队列无限增长
        AudioChunk chunk;
        {
            std::unique_lock<std::mutex> guard(enc->lock);
            if (enc->audioQueue.empty()) return false;
            chunk = std::move(enc->audioQueue.front());
            enc->audioQueue.pop_front();
        }
        return true;
    }

    ssize_t index = AMediaCodec_dequeueInputBuffer(enc->audioCodec, 0);
    if (index == AMEDIACODEC_INFO_TRY_AGAIN_LATER) return false;
    if (index < 0) {
        enc->Fail("audio dequeueInputBuffer: " + std::to_string(index));
        return false;
    }

    AudioChunk chunk;
    {
        std::unique_lock<std::mutex> guard(enc->lock);
        if (enc->audioQueue.empty()) {
            AMediaCodec_queueInputBuffer(enc->audioCodec, static_cast<size_t>(index), 0, 0, 0, 0);
            return false;
        }
        chunk = std::move(enc->audioQueue.front());
        enc->audioQueue.pop_front();
    }

    size_t capacity = 0;
    uint8_t* buffer = AMediaCodec_getInputBuffer(enc->audioCodec,
                                                static_cast<size_t>(index), &capacity);
    if (!buffer) {
        enc->Fail("audio getInputBuffer returned null");
        return false;
    }

    const size_t bytes = chunk.pcm.size() * sizeof(int16_t);
    if (bytes > capacity) {
        enc->Fail("audio input buffer too small");
        return false;
    }

    std::memcpy(buffer, chunk.pcm.data(), bytes);
    return Check(enc, AMediaCodec_queueInputBuffer(enc->audioCodec, static_cast<size_t>(index), 0,
                                                   bytes,
                                                   static_cast<uint64_t>(chunk.timestampUs), 0),
                 "audio queueInputBuffer");
}

// ---------------------------------------------------------------- 输出与复用

bool DrainCodec(Encoder* enc, AMediaCodec* codec, int32_t* track, bool isVideo,
                bool draining) {
    AMediaCodecBufferInfo info;
    ssize_t index = AMediaCodec_dequeueOutputBuffer(codec, &info, draining ? 10000 : 0);

    while (index >= 0 || index == AMEDIACODEC_INFO_OUTPUT_FORMAT_CHANGED
           || index == AMEDIACODEC_INFO_OUTPUT_BUFFERS_CHANGED) {
        if (index == AMEDIACODEC_INFO_OUTPUT_FORMAT_CHANGED) {
            AMediaFormat* format = AMediaCodec_getOutputFormat(codec);

            if (*track < 0) {
                *track = static_cast<int32_t>(AMediaMuxer_addTrack(enc->muxer, format));
                if (*track < 0) {
                    enc->Fail(std::string("AMediaMuxer_addTrack failed for ")
                              + (isVideo ? "video" : "audio"));
                    AMediaFormat_delete(format);
                    return false;
                }
            }

            if (enc->videoTrack >= 0 && (!enc->audioEnabled || enc->audioTrack >= 0)
                && !enc->muxerStarted) {
                if (!Check(enc, AMediaMuxer_start(enc->muxer), "AMediaMuxer_start")) {
                    AMediaFormat_delete(format);
                    return false;
                }
                enc->muxerStarted = true;
                LOGI("muxer started (video=%d audio=%d)", enc->videoTrack, enc->audioTrack);
            }

            AMediaFormat_delete(format);
            index = AMediaCodec_dequeueOutputBuffer(codec, &info, 0);
            continue;
        }

        if (index == AMEDIACODEC_INFO_OUTPUT_BUFFERS_CHANGED) {
            index = AMediaCodec_dequeueOutputBuffer(codec, &info, 0);
            continue;
        }

        if (info.size > 0 && enc->muxerStarted && *track >= 0) {
            size_t capacity = 0;
            uint8_t* buffer = AMediaCodec_getOutputBuffer(codec, static_cast<size_t>(index),
                                                          &capacity);

            if (!buffer) {
                enc->Fail("getOutputBuffer returned null");
                return false;
            }

            AMediaMuxer_writeSampleData(enc->muxer, static_cast<size_t>(*track), buffer, &info);

            if (isVideo && !(info.flags & AMEDIACODEC_BUFFER_FLAG_CODEC_CONFIG)) {
                enc->encodedFrames++;
            }
        }

        const bool eos = (info.flags & AMEDIACODEC_BUFFER_FLAG_END_OF_STREAM) != 0;
        AMediaCodec_releaseOutputBuffer(codec, static_cast<size_t>(index), false);

        if (eos) return true;
        index = AMediaCodec_dequeueOutputBuffer(codec, &info, draining ? 10000 : 0);
    }

    if (index < 0 && index != AMEDIACODEC_INFO_OUTPUT_FORMAT_CHANGED
        && index != AMEDIACODEC_INFO_OUTPUT_BUFFERS_CHANGED
        && index != AMEDIACODEC_INFO_TRY_AGAIN_LATER) {
        enc->Fail(std::string(isVideo ? "video" : "audio")
                  + " dequeueOutputBuffer: " + std::to_string(index));
        return false;
    }

    return false;
}

void SignalEndOfStream(Encoder* enc, AMediaCodec* codec, bool isVideo) {
    ssize_t index = AMediaCodec_dequeueInputBuffer(codec, 1000000);

    if (index >= 0) {
        AMediaCodec_queueInputBuffer(codec, static_cast<size_t>(index), 0, 0, 0,
                                     AMEDIACODEC_BUFFER_FLAG_END_OF_STREAM);
    } else {
        LOGE("%s: could not queue EOS buffer", isVideo ? "video" : "audio");
    }
}

void WorkerMain(Encoder* enc) {
    if (!ConfigureVideoCodec(enc)) return;
    if (!ConfigureAudioCodec(enc)) return;

    bool videoEos = false;
    bool audioEos = !enc->audioEnabled;

    while (true) {
        {
            std::unique_lock<std::mutex> guard(enc->lock);

            if (enc->aborting) break;

            if (enc->stopping && enc->videoQueue.empty() && enc->audioQueue.empty()) {
                guard.unlock();

                if (!videoEos) {
                    SignalEndOfStream(enc, enc->videoCodec, true);
                    videoEos = true;
                }

                if (!audioEos && enc->audioCodec) {
                    SignalEndOfStream(enc, enc->audioCodec, false);
                    audioEos = true;
                }

                guard.lock();
            } else if (enc->videoQueue.empty() && enc->audioQueue.empty() && !enc->stopping) {
                guard.unlock();
                // 无数据时空转一小会儿，等主线程提交
                std::this_thread::sleep_for(std::chrono::milliseconds(2));
                continue;
            }
        }

        // 送视频
        while (true) {
            VideoFrame frame;
            int64_t index;

            {
                std::unique_lock<std::mutex> guard(enc->lock);
                if (enc->videoQueue.empty() || enc->aborting) break;
                frame = std::move(enc->videoQueue.front());
                enc->videoQueue.pop_front();
                index = enc->submittedFrames - static_cast<int64_t>(enc->videoQueue.size()) - 1;
            }

            enc->videoSpace.notify_one();

            if (!FeedOneVideoFrame(enc, frame, index)) {
                if (enc->failed) return;
                // 输入缓冲暂时没有，帧已丢弃会掉帧 —— 退回去重试一次
                std::unique_lock<std::mutex> guard(enc->lock);
                enc->videoQueue.push_front(std::move(frame));
                break;
            }

            break;
        }

        // 送音频
        if (!audioEos) FeedAudio(enc);

        // 收输出
        bool videoDone = DrainCodec(enc, enc->videoCodec, &enc->videoTrack, true, false);
        if (enc->failed) return;

        if (!audioEos && enc->audioCodec) {
            bool audioDone = DrainCodec(enc, enc->audioCodec, &enc->audioTrack, false, false);
            if (enc->failed) return;
            if (audioDone) audioEos = true;
        }

        if (videoDone) {
            // 视频 EOF 后继续把音频排空
            while (!audioEos && enc->audioCodec) {
                FeedAudio(enc);
                bool audioDone = DrainCodec(enc, enc->audioCodec, &enc->audioTrack, false, true);
                if (enc->failed) return;
                if (audioDone) break;
            }
            break;
        }

        if (videoEos && audioEos) break;
    }

    if (enc->muxer && enc->muxerStarted) AMediaMuxer_stop(enc->muxer);

    LOGI("worker done: submitted=%lld encoded=%lld", static_cast<long long>(enc->submittedFrames),
         static_cast<long long>(enc->encodedFrames));

    std::lock_guard<std::mutex> guard(enc->lock);
    enc->finished = true;
    enc->videoReady.notify_all();
    enc->videoSpace.notify_all();
    enc->audioReady.notify_all();
}

}  // namespace

// ---------------------------------------------------------------- 对外 C API

API void* UVVideoAndroid_Create(int width, int height, int fps, int bitrateMbps, int hevc,
                                const char* path, int sampleRate, int channels) {
    if (width <= 0 || height <= 0 || fps <= 0 || !path) {
        LOGE("UVVideoAndroid_Create: invalid arguments");
        return nullptr;
    }

    auto* enc = new Encoder();
    enc->width = width;
    enc->height = height;
    enc->fps = fps;
    enc->bitrateBps = (bitrateMbps > 0 ? bitrateMbps : 20) * 1000 * 1000;
    enc->hevc = hevc != 0;
    enc->path = path;
    enc->sampleRate = sampleRate > 0 ? sampleRate : 48000;
    enc->channels = channels > 0 ? channels : 2;

    enc->fd = ::open(path, O_CREAT | O_TRUNC | O_RDWR, 0644);

    if (enc->fd < 0) {
        LOGE("cannot open output file: %s", path);
        delete enc;
        return nullptr;
    }

    enc->muxer = AMediaMuxer_new(enc->fd, AMEDIAMUXER_OUTPUT_FORMAT_MPEG_4);

    if (!enc->muxer) {
        LOGE("AMediaMuxer_new failed");
        ::close(enc->fd);
        delete enc;
        return nullptr;
    }

    enc->worker = std::thread(WorkerMain, enc);
    return enc;
}

API int UVVideoAndroid_SubmitRGBA(void* handle, const void* rgba, int width, int height) {
    auto* enc = static_cast<Encoder*>(handle);

    if (!enc || !rgba) return -1;
    if (width != enc->width || height != enc->height) {
        enc->Fail("frame size mismatch");
        return -1;
    }

    const size_t bytes = static_cast<size_t>(width) * height * 4;

    {
        std::unique_lock<std::mutex> guard(enc->lock);
        enc->videoSpace.wait(guard, [enc] {
            return enc->failed || enc->aborting || enc->videoQueue.size() < kMaxQueuedFrames;
        });

        if (enc->failed || enc->aborting) return -1;

        VideoFrame frame;
        frame.rgba.resize(bytes);
        std::memcpy(frame.rgba.data(), rgba, bytes);
        enc->videoQueue.push_back(std::move(frame));
        enc->submittedFrames++;
    }

    enc->videoReady.notify_one();
    return 0;
}

API int UVVideoAndroid_SubmitAudio(void* handle, const float* interleaved, int sampleFrames,
                                   int64_t timestampUs) {
    auto* enc = static_cast<Encoder*>(handle);

    if (!enc || !interleaved || sampleFrames <= 0) return -1;
    if (!enc->audioEnabled) return 0;

    const int channels = enc->channels;
    AudioChunk chunk;
    chunk.pcm.resize(static_cast<size_t>(sampleFrames) * channels);
    chunk.timestampUs = timestampUs;

    for (int i = 0; i < sampleFrames * channels; ++i) {
        float value = interleaved[i];
        if (value > 1.0f) value = 1.0f;
        else if (value < -1.0f) value = -1.0f;
        chunk.pcm[i] = static_cast<int16_t>(value * 32767.0f);
    }

    {
        std::lock_guard<std::mutex> guard(enc->lock);
        if (enc->failed || enc->aborting) return -1;
        enc->audioQueue.push_back(std::move(chunk));
    }

    enc->audioReady.notify_one();
    return 0;
}

API int UVVideoAndroid_Finish(void* handle) {
    auto* enc = static_cast<Encoder*>(handle);

    if (!enc) return -1;

    {
        std::lock_guard<std::mutex> guard(enc->lock);
        enc->stopping = true;
    }

    enc->videoReady.notify_all();
    enc->audioReady.notify_all();
    return 0;
}

API int UVVideoAndroid_State(void* handle, int* submitted, int* written) {
    auto* enc = static_cast<Encoder*>(handle);

    if (!enc) return -1;

    std::lock_guard<std::mutex> guard(enc->lock);
    if (submitted) *submitted = static_cast<int>(enc->submittedFrames);
    if (written) *written = static_cast<int>(enc->encodedFrames);

    if (enc->failed) return -1;
    if (enc->finished) return 1;
    return 0;
}

API const char* UVVideoAndroid_Error(void* handle) {
    auto* enc = static_cast<Encoder*>(handle);

    if (!enc) return "null handle";

    std::lock_guard<std::mutex> guard(enc->lock);
    return enc->error.c_str();
}

API int UVVideoAndroid_Abort(void* handle) {
    auto* enc = static_cast<Encoder*>(handle);

    if (!enc) return -1;

    {
        std::lock_guard<std::mutex> guard(enc->lock);
        enc->aborting = true;
    }

    enc->videoReady.notify_all();
    enc->videoSpace.notify_all();
    enc->audioReady.notify_all();
    return 0;
}

API void UVVideoAndroid_Destroy(void* handle) {
    auto* enc = static_cast<Encoder*>(handle);

    if (!enc) return;

    {
        std::lock_guard<std::mutex> guard(enc->lock);
        enc->aborting = true;
    }

    enc->videoReady.notify_all();
    enc->videoSpace.notify_all();
    enc->audioReady.notify_all();

    if (enc->worker.joinable()) enc->worker.join();

    if (enc->videoCodec) {
        AMediaCodec_stop(enc->videoCodec);
        AMediaCodec_delete(enc->videoCodec);
    }

    if (enc->audioCodec) {
        AMediaCodec_stop(enc->audioCodec);
        AMediaCodec_delete(enc->audioCodec);
    }

    if (enc->muxer) AMediaMuxer_delete(enc->muxer);
    if (enc->fd >= 0) ::close(enc->fd);

    delete enc;
}
