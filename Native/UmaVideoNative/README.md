# UmaVideoNative

Windows x64 D3D11/NVENC 编码桥。`src/encoder.cpp` 将 Unity 纹理交给 NVIDIA 编码器，写出带时间戳的视频流，最终音视频封装由 FFmpeg 完成。

在项目根目录执行：

```powershell
./Native/UmaVideoNative/build.ps1 -Compiler g++.exe
```

使用 MinGW-w64 GCC，输出为 `Assets/Plugins/UmaVideoNative.dll`。`include/nvEncodeAPI.h` 来自 FFmpeg nv-codec-headers n12.1.14.0，授权声明保留在头文件中。
