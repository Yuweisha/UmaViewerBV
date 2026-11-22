using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

/// <summary>
/// 用 Win32 CreateProcess 启动外部程序。
///
/// 为什么不用 System.Diagnostics.Process：Unity IL2CPP 构建里 Process.Start
/// 创建子进程会抛 "Native error= Success"（CreateProcess 返回 FALSE 但
/// GetLastError() 为 0）。同一段代码在 .NET 控制台程序、GUI 程序、PowerShell
/// 里都正常，只在 IL2CPP 下失败 —— 这是 Win32 调用被 IL2CPP 的 BCL 实现弄坏了。
/// 所以这里直接调 kernel32，完全绕开 BCL。
/// </summary>
internal sealed class NativeProcess : IDisposable
{
    private const uint CREATE_NO_WINDOW = 0x08000000;
    private const int STARTF_USESTDHANDLES = 0x00000100;
    private const uint INFINITE = 0xFFFFFFFF;

    private const uint HANDLE_FLAG_INHERIT = 0x00000001;
    private const uint PIPE_ACCESS_INBOUND = 0x00000001;
    private const uint PIPE_ACCESS_OUTBOUND = 0x00000002;

    private const int STD_INPUT_HANDLE = -10;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string lpReserved;
        public string lpDesktop;
        public string lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        public int bInheritHandle;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcess(
        string lpApplicationName, string lpCommandLine,
        IntPtr lpProcessAttributes, IntPtr lpThreadAttributes,
        bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment,
        string lpCurrentDirectory, ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(out IntPtr hReadPipe, out IntPtr hWritePipe,
        ref SECURITY_ATTRIBUTES lpPipeAttributes, uint nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetHandleInformation(IntPtr hObject, uint dwMask, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    private PROCESS_INFORMATION info;
    private FileStream stdin;
    private FileStream stderrStream;
    private bool disposed;

    public int ProcessId => info.dwProcessId;
    public Stream StandardInput => stdin;
    public Stream StandardError => stderrStream;

    private NativeProcess() { }

    /// <summary>
    /// 启动进程。attachStdin 为 true 时创建可写管道接子进程的 stdin（ffmpeg pipe:0 用来收裸帧）。
    /// 返回 null 表示失败，reason 给出 Win32 错误码和系统消息。
    /// </summary>
    public static NativeProcess Start(string executable, string arguments,
        bool attachStdin, out string reason)
    {
        reason = null;
        var process = new NativeProcess();

        IntPtr stdinRead = IntPtr.Zero, stdinWrite = IntPtr.Zero;
        IntPtr errRead = IntPtr.Zero, errWrite = IntPtr.Zero;

        var inheritable = new SECURITY_ATTRIBUTES
        {
            nLength = Marshal.SizeOf(typeof(SECURITY_ATTRIBUTES)),
            lpSecurityDescriptor = IntPtr.Zero,
            bInheritHandle = 1
        };

        try
        {
            var startup = new STARTUPINFO();
            startup.cb = Marshal.SizeOf(typeof(STARTUPINFO));
            startup.dwFlags = STARTF_USESTDHANDLES;

            if (attachStdin)
            {
                if (!CreatePipe(out stdinRead, out stdinWrite, ref inheritable, 0))
                {
                    reason = "CreatePipe(stdin) failed: " + Marshal.GetLastWin32Error();
                    return null;
                }

                // 父进程持有写端：写端必须不继承，读端继承给子进程
                SetHandleInformation(stdinWrite, HANDLE_FLAG_INHERIT, 0);
                startup.hStdInput = stdinRead;
            }
            else
            {
                startup.hStdInput = GetStdHandle(STD_INPUT_HANDLE);
            }

            if (!CreatePipe(out errRead, out errWrite, ref inheritable, 0))
            {
                reason = "CreatePipe(stderr) failed: " + Marshal.GetLastWin32Error();
                return null;
            }

            SetHandleInformation(errRead, HANDLE_FLAG_INHERIT, 0);
            startup.hStdOutput = errWrite;
            startup.hStdError = errWrite;

            var commandLine = "\"" + executable + "\" " + arguments;

            if (!CreateProcess(executable, commandLine, IntPtr.Zero, IntPtr.Zero, true,
                    CREATE_NO_WINDOW, IntPtr.Zero, null, ref startup, out process.info))
            {
                int code = Marshal.GetLastWin32Error();
                reason = "CreateProcess failed: " + code + " ("
                         + new System.ComponentModel.Win32Exception(code).Message + ")";
                return null;
            }

            // 子进程已经拿到自己的副本，父进程立刻关掉这些端
            CloseHandle(stdinRead);
            stdinRead = IntPtr.Zero;
            CloseHandle(errWrite);
            errWrite = IntPtr.Zero;

            if (attachStdin)
            {
                var safe = new SafeFileHandle(stdinWrite, true);
                stdinWrite = IntPtr.Zero;
                process.stdin = new FileStream(safe, FileAccess.Write, 65536, false);
            }

            var errSafe = new SafeFileHandle(errRead, true);
            errRead = IntPtr.Zero;
            process.stderrStream = new FileStream(errSafe, FileAccess.Read, 4096, false);

            return process;
        }
        catch (Exception failure)
        {
            reason = failure.GetType().Name + ": " + failure.Message;
            process.Dispose();
            return null;
        }
        finally
        {
            if (stdinRead != IntPtr.Zero) CloseHandle(stdinRead);
            if (stdinWrite != IntPtr.Zero) CloseHandle(stdinWrite);
            if (errRead != IntPtr.Zero) CloseHandle(errRead);
            if (errWrite != IntPtr.Zero) CloseHandle(errWrite);
        }
    }

    /// <summary>等待退出，返回 false 表示超时。</summary>
    public bool WaitForExit(int timeoutMs)
    {
        return WaitForSingleObject(info.hProcess, (uint)timeoutMs) == 0;
    }

    public int ExitCode
    {
        get
        {
            uint code;
            GetExitCodeProcess(info.hProcess, out code);
            return (int)code;
        }
    }

    public void Kill()
    {
        TerminateProcess(info.hProcess, 1);
        WaitForSingleObject(info.hProcess, 5000);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        try { stdin?.Dispose(); } catch { }
        try { stderrStream?.Dispose(); } catch { }
        stdin = null;
        stderrStream = null;

        if (info.hThread != IntPtr.Zero) CloseHandle(info.hThread);
        if (info.hProcess != IntPtr.Zero) CloseHandle(info.hProcess);
        info.hThread = IntPtr.Zero;
        info.hProcess = IntPtr.Zero;
    }
}

/// <summary>
/// 一次性探测：跑完、收输出、拿退出码。给 FFmpeg 检测和编码器可用性筛选用。
/// </summary>
internal static class NativeProcessProbe
{
    public static bool Run(string executable, string arguments, int timeoutMs,
        out int exitCode, out string output, out string error)
    {
        exitCode = -1;
        output = string.Empty;
        error = null;

        NativeProcess process = NativeProcess.Start(executable, arguments, false, out string reason);

        if (process == null)
        {
            error = reason;
            return false;
        }

        try
        {
            // 必须边读边等：输出量大时管道写满会阻塞子进程，等退出就永远等不到
            var builder = new StringBuilder();
            var buffer = new byte[8192];

            while (true)
            {
                int read = process.StandardError.Read(buffer, 0, buffer.Length);

                if (read <= 0) break;

                builder.Append(Encoding.UTF8.GetString(buffer, 0, read));

                if (builder.Length > 200000) break;
            }

            if (!process.WaitForExit(timeoutMs))
            {
                process.Kill();
                error = "process did not exit within " + timeoutMs + " ms";
                return false;
            }

            output = builder.ToString();
            exitCode = process.ExitCode;
            return true;
        }
        catch (Exception failure)
        {
            error = failure.GetType().Name + ": " + failure.Message;
            return false;
        }
        finally
        {
            process.Dispose();
        }
    }
}
