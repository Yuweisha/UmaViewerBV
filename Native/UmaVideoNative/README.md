# UmaVideoNative

Live 渲染的视频编码桥，Windows 和 Android 各一套实现，C# 侧分别由
`LiveGpuVideoEncoder` 和 `LiveAndroidVideoEncoder` 调用。

## Windows：`src/encoder.cpp`

D3D11 / NVENC。Unity 纹理不进 CPU 内存，直接交给 NVIDIA 编码器，写出带时间戳的视频流，
音视频封装由 FFmpeg 完成。

在项目根目录执行：

```powershell
./Native/UmaVideoNative/build.ps1 -Compiler g++.exe
```

使用 MinGW-w64 GCC，输出为 `Assets/Plugins/UmaVideoNative.dll`。`include/nvEncodeAPI.h` 来自 FFmpeg nv-codec-headers n12.1.14.0，授权声明保留在头文件中。

## Android：`android/src/encoder_android.cpp`

MediaCodec 硬编 + MediaMuxer 直接写 MP4。像素经 `AsyncGPUReadback` 到 CPU（一次拷贝），
YUV 转换和 H.264/H.265 编码都在芯片 VPU，因此 1080p60 能跑满，APK 体积不增加
（`libmediandk` 是 API 21+ 系统库）。

不用 FFmpeg 的原因：`libx264` 在手机上是纯软编，1080p 只有 15–25 fps；且 IL2CPP 下
`System.Diagnostics.Process` 不可用，管道方案本来也跑不通。

```bash
python Native/UmaVideoNative/android/build_android.py --ndk <ndk-root>
```

输出 `Assets/Plugins/Android/libs/arm64-v8a/libUmaVideoAndroid.so`，依赖：

```
libmediandk.so  libandroid.so  liblog.so  libjnigraphics.so  libm.so  libdl.so  libc.so
```

CI 的 Android job 会自动编译这个库（`.github/workflows/build.yml`）。

### 导出 API

```c
void* UVVideoAndroid_Create(int width, int height, int fps, int bitrateMbps, int hevc,
                            const char* path, int sampleRate, int channels);
int   UVVideoAndroid_SubmitRGBA(void* handle, const void* rgba, int width, int height);
int   UVVideoAndroid_SubmitAudio(void* handle, const float* interleaved,
                                 int sampleFrames, int64_t timestampUs);
int   UVVideoAndroid_Finish(void* handle);
int   UVVideoAndroid_State(void* handle, int* submitted, int* written);  // 1=完成 0=进行中 -1=失败
const char* UVVideoAndroid_Error(void* handle);
int   UVVideoAndroid_Abort(void* handle);
void  UVVideoAndroid_Destroy(void* handle);
```

线程模型：`Submit*` 只入队，编码/复用全在内部 worker 线程；视频队列上限 4 帧，
满了会阻塞形成背压，避免丢帧或吃爆内存。

`SubmitRGBA` 的缓冲区按 Unity 的左下原点排布，原生侧翻转成视频的左上原点。

### 注意

- 只支持 ARM64（`arm64-v8a`）。Unity Android 默认就是这个架构。
- 编码器颜色格式按 NV12 → I420 → flexible 依次尝试，全失败才报错。
- `AMediaCodec_getInputImage` 在 NDK 的 C API 里不存在，所以输入必须由 CPU 送 YUV，
  这一点省不掉；但编码本身仍是硬件。

