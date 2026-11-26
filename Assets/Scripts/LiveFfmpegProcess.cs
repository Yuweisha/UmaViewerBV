using System;
using System.IO;
using System.Threading.Tasks;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR && ENABLE_IL2CPP
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
#else
using System.Diagnostics;
#endif

internal sealed class LiveFfmpegProcess : IDisposable
{
    public Stream Input { get; private set; }
    private Task<string> output;

    public string Output => output.GetAwaiter().GetResult();
    public bool HasExited => WaitForExit(0);

    public void CloseInput()
    {
        try { Input?.Dispose(); }
        finally
        {
            Input = null;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR && ENABLE_IL2CPP
            Release(inputHandle);
            inputHandle = IntPtr.Zero;
#endif
        }
    }

    public static void Run(string executable, string arguments, int timeout, string failure, bool expectVersion = false)
    {
        using var process = new LiveFfmpegProcess(executable, arguments);
        if (!process.WaitForExit(timeout))
            throw new TimeoutException(failure + " (timed out)");
        string output = process.Output;
        if (process.ExitCode != 0 || (expectVersion && output.IndexOf("ffmpeg version", StringComparison.OrdinalIgnoreCase) < 0))
            throw new InvalidOperationException(failure + ": " + output.Trim());
    }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR && ENABLE_IL2CPP
    private IntPtr process, inputHandle;

    public LiveFfmpegProcess(string executable, string arguments, bool pipeInput = false)
    {
        string fullPath = Path.GetFullPath(executable);
        IntPtr childInput = IntPtr.Zero, parentInput = IntPtr.Zero;
        IntPtr parentOutput = IntPtr.Zero, childOutput = IntPtr.Zero;
        try
        {
            var attributes = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Inherit = 1 };
            Check(CreatePipe(out childInput, out parentInput, ref attributes, 0));
            Check(CreatePipe(out parentOutput, out childOutput, ref attributes, 0));
            Check(SetHandleInformation(parentInput, 1, 0));
            Check(SetHandleInformation(parentOutput, 1, 0));
            var startup = new StartupInfo
            {
                Size = Marshal.SizeOf<StartupInfo>(),
                Flags = 0x100,
                Input = childInput,
                Output = childOutput,
                Error = childOutput
            };
            var command = new StringBuilder(LiveRenderSettings.Quote(fullPath) + " " + arguments);
            Check(CreateProcessW(fullPath, command, IntPtr.Zero, IntPtr.Zero, true, 0x08000000,
                IntPtr.Zero, Path.GetDirectoryName(fullPath), ref startup, out var info));
            process = info.Process;
            CloseHandle(info.Thread);

            if (pipeInput)
            {
                inputHandle = parentInput;
                parentInput = IntPtr.Zero;
                Input = new FileStream(new SafeFileHandle(inputHandle, false), FileAccess.Write);
            }
            var reader = new StreamReader(OpenPipe(ref parentOutput, FileAccess.Read));
            output = Task.Run(() =>
            {
                using (reader) return reader.ReadToEnd();
            });
        }
        catch
        {
            Dispose();
            throw;
        }
        finally
        {
            Release(childInput);
            Release(childOutput);
            Release(parentInput);
            Release(parentOutput);
        }
    }

    private static FileStream OpenPipe(ref IntPtr handle, FileAccess access)
    {
        var owner = new SafeFileHandle(handle, true);
        handle = IntPtr.Zero;
        try { return new FileStream(owner, access); }
        catch { owner.Dispose(); throw; }
    }

    public bool WaitForExit(int milliseconds)
    {
        uint result = WaitForSingleObject(process, unchecked((uint)milliseconds));
        if (result == 258) return false;
        Check(result == 0);
        return true;
    }

    public int ExitCode
    {
        get
        {
            Check(GetExitCodeProcess(process, out uint code));
            return unchecked((int)code);
        }
    }

    public void Kill()
    {
        if (!TerminateProcess(process, 1) && !HasExited)
            Check(false);
    }

    public void Dispose()
    {
        try
        {
            if (process != IntPtr.Zero && !HasExited)
            {
                Kill();
                WaitForExit(5000);
            }
        }
        finally
        {
            Release(process);
            process = IntPtr.Zero;
            CloseInput();
        }
    }

    private static void Check(bool success)
    {
        if (!success) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private static void Release(IntPtr handle)
    {
        if (handle != IntPtr.Zero) CloseHandle(handle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public IntPtr Descriptor;
        public int Inherit;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public int Size;
        public IntPtr Reserved, Desktop, Title;
        public int X, Y, XSize, YSize, XCount, YCount, Fill, Flags;
        public short ShowWindow, ReservedSize;
        public IntPtr Reserved2, Input, Output, Error;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInfo
    {
        public IntPtr Process, Thread;
        public int ProcessId, ThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(out IntPtr read, out IntPtr write, ref SecurityAttributes attributes, uint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern bool CreateProcessW(string application, StringBuilder command, IntPtr processAttributes,
        IntPtr threadAttributes, bool inheritHandles, uint flags, IntPtr environment, string directory,
        ref StartupInfo startup, out ProcessInfo info);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
#else
    private readonly Process process;

    public LiveFfmpegProcess(string executable, string arguments, bool pipeInput = false)
    {
        string fullPath = Path.GetFullPath(executable);
        process = new Process
        {
            StartInfo = new ProcessStartInfo(fullPath, arguments)
            {
                WorkingDirectory = Path.GetDirectoryName(fullPath),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        try
        {
            process.Start();
            if (pipeInput) Input = process.StandardInput.BaseStream;
            else process.StandardInput.Close();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            output = Task.Run(async () => (await stdout.ConfigureAwait(false)) + (await stderr.ConfigureAwait(false)));
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    public bool WaitForExit(int milliseconds) => process.WaitForExit(milliseconds);
    public int ExitCode => process.ExitCode;
    public void Kill()
    {
        if (!HasExited) process.Kill();
    }

    public void Dispose()
    {
        try
        {
            Kill();
            process.WaitForExit(5000);
        }
        finally
        {
            CloseInput();
            process.Dispose();
        }
    }
#endif
}
