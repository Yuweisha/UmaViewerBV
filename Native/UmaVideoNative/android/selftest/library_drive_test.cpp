// 通过 dlopen 加载真实的 libUmaVideoAndroid.so，按 Unity 侧的调用顺序驱动它：
// Create -> 逐帧 SubmitRGBA + SubmitAudio -> Finish -> 轮询 State -> Destroy。
//
// 这一步验证的是自检程序覆盖不到的路径：worker 线程、4 帧背压、增量入队。

#include <dlfcn.h>
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
constexpr int kBitrateMbps = 4;
constexpr int kSampleRate = 48000;

using CreateFn = void* (*)(int, int, int, int, int, const char*, int, int);
using SubmitRgbaFn = int (*)(void*, const void*, int, int);
using SubmitAudioFn = int (*)(void*, const float*, int, int64_t);
using FinishFn = int (*)(void*);
using StateFn = int (*)(void*, int*, int*);
using ErrorFn = const char* (*)(void*);
using AbortFn = int (*)(void*);
using DestroyFn = void (*)(void*);

}  // namespace

int main(int argc, char** argv) {
    const char* libPath = argc > 1 ? argv[1] : "/data/local/tmp/libUmaVideoAndroid.so";
    const char* outPath = argc > 2 ? argv[2] : "/data/local/tmp/test_lib.mp4";

    void* lib = dlopen(libPath, RTLD_NOW);

    if (!lib) {
        printf("[lib-test] FAIL dlopen: %s\n", dlerror());
        return 1;
    }

    printf("[lib-test] loaded %s\n", libPath);

    auto create = reinterpret_cast<CreateFn>(dlsym(lib, "UVVideoAndroid_Create"));
    auto submitRgba = reinterpret_cast<SubmitRgbaFn>(dlsym(lib, "UVVideoAndroid_SubmitRGBA"));
    auto submitAudio = reinterpret_cast<SubmitAudioFn>(dlsym(lib, "UVVideoAndroid_SubmitAudio"));
    auto finish = reinterpret_cast<FinishFn>(dlsym(lib, "UVVideoAndroid_Finish"));
    auto state = reinterpret_cast<StateFn>(dlsym(lib, "UVVideoAndroid_State"));
    auto error = reinterpret_cast<ErrorFn>(dlsym(lib, "UVVideoAndroid_Error"));
    auto abort = reinterpret_cast<AbortFn>(dlsym(lib, "UVVideoAndroid_Abort"));
    auto destroy = reinterpret_cast<DestroyFn>(dlsym(lib, "UVVideoAndroid_Destroy"));

    if (!create || !submitRgba || !submitAudio || !finish || !state || !error || !abort
        || !destroy) {
        printf("[lib-test] FAIL: missing symbols\n");
        return 1;
    }

    printf("[lib-test] all 8 symbols resolved\n");

    void* handle = create(kWidth, kHeight, kFps, kBitrateMbps, 0, outPath, kSampleRate, 2);

    if (!handle) {
        printf("[lib-test] FAIL: Create returned null\n");
        return 1;
    }

    printf("[lib-test] created; submitting %d frames\n", kFrames);

    std::vector<uint8_t> rgba(static_cast<size_t>(kWidth) * kHeight * 4);
    std::vector<float> pcm(4800 * 2);
    int64_t audioCursor = 0;

    for (int frame = 0; frame < kFrames; ++frame) {
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

        if (submitRgba(handle, rgba.data(), kWidth, kHeight) != 0) {
            printf("[lib-test] FAIL submit frame %d: %s\n", frame, error(handle));
            destroy(handle);
            return 1;
        }

        // 每帧对应 sampleRate/fps 个采样，与 Unity 侧一致
        const int wanted = (frame + 1) * kSampleRate / kFps;
        const int count = wanted - static_cast<int>(audioCursor);

        if (count > 0) {
            for (int i = 0; i < count; ++i) {
                const double t = static_cast<double>(audioCursor + i) / kSampleRate;
                const float value = static_cast<float>(std::sin(t * 2 * M_PI * 440.0) * 0.25);
                pcm[i * 2 + 0] = value;
                pcm[i * 2 + 1] = value;
            }

            const int64_t timestampUs = audioCursor * 1000000LL / kSampleRate;

            if (submitAudio(handle, pcm.data(), count, timestampUs) != 0) {
                printf("[lib-test] FAIL submit audio: %s\n", error(handle));
                destroy(handle);
                return 1;
            }

            audioCursor += count;
        }
    }

    printf("[lib-test] submitted all; calling Finish\n");
    finish(handle);

    int submitted = 0, written = 0;
    int polls = 0;

    while (polls++ < 3000) {
        const int status = state(handle, &submitted, &written);

        if (status < 0) {
            printf("[lib-test] FAIL encode: %s\n", error(handle));
            destroy(handle);
            return 1;
        }

        if (status == 1) break;
        usleep(10000);
    }

    printf("[lib-test] state: submitted=%d written=%d polls=%d\n", submitted, written, polls);
    destroy(handle);

    if (written != kFrames) {
        printf("[lib-test] FAIL: expected %d frames, got %d\n", kFrames, written);
        return 1;
    }

    printf("[lib-test] DONE: %d frames encoded\n", written);
    return 0;
}
