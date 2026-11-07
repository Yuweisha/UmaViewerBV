# UmaSpringNative

Windows x64 CySpring 数值内核。`src/kernel.cpp` 实现普通链条、裙摆联动与碰撞更新，`src/batch_job.h` 定义批量调用布局。C# 层负责输入缓冲、调度和姿态回写。

在项目根目录执行：

```powershell
./Native/UmaSpringNative/build.ps1 -Compiler g++.exe
```

使用 MinGW-w64 GCC，输出为 `Assets/Plugins/UmaSpringNative.dll`。构建保留浮点运算顺序，使用 Windows UCRT 数学函数；修改内核后先重建 DLL，再构建 Unity Player。
