using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// Android 硬件视频编码桥：RenderTexture → MediaCodec(H.264/H.265) → MediaMuxer(MP4)。
///
/// 与 Windows 的 LiveGpuVideoEncoder 对应，但走的是 Android NDK 的 MediaCodec，
/// 不是 NVENC。像素在这里必须经过 CPU（AsyncGPUReadback → RGBA），编码在 VPU，
/// 因此 CPU 只承担一次内存拷贝 + YUV 转换，1080p60 可以跑满。
///
/// 音频用同一个 muxer 封装：调用方按帧提交 float PCM，内部转 S16 后交给 AAC 编码器。
/// </summary>
internal sealed class LiveAndroidVideoEncoder : IDisposable
{
    private const string Library = "UmaVideoAndroid";

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr UVVideoAndroid_Create(
        int width, int height, int fps, int bitrateMbps, int hevc,
        string path, int sampleRate, int channels);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int UVVideoAndroid_SubmitRGBA(IntPtr handle, IntPtr rgba,
        int width, int height);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int UVVideoAndroid_SubmitAudio(IntPtr handle, IntPtr interleaved,
        int sampleFrames, long timestampUs);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int UVVideoAndroid_Finish(IntPtr handle);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int UVVideoAndroid_State(IntPtr handle, out int submitted, out int written);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr UVVideoAndroid_Error(IntPtr handle);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern int UVVideoAndroid_Abort(IntPtr handle);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern void UVVideoAndroid_Destroy(IntPtr handle);

    private IntPtr handle;
    private int width, height, fps, sampleRate, channels;
    private bool ending;

    public int WrittenFrames { get; private set; }
    public int SubmittedFrames { get; private set; }
    public string Path { get; private set; }

    /// <summary>Android 上永远可用；失败时返回 false 并给出原因，调用方回落到其它路径。</summary>
    public static bool TryCreate(LiveRenderSettings settings, string path,
        out LiveAndroidVideoEncoder encoder, out string reason)
    {
        encoder = null;
        reason = null;

        if (Application.platform != RuntimePlatform.Android)
        {
            reason = "not running on Android";
            return false;
        }

        var candidate = new LiveAndroidVideoEncoder();

        try
        {
            const int rate = 48000;
            candidate.handle = UVVideoAndroid_Create(
                settings.Width, settings.Height, settings.Fps, settings.BitrateMbps,
                settings.Encoder.IndexOf("265", StringComparison.Ordinal) >= 0 ? 1 : 0,
                path, rate, 2);

            if (candidate.handle == IntPtr.Zero)
            {
                reason = "MediaCodec encoder could not be created";
                candidate.Dispose();
                return false;
            }

            candidate.width = settings.Width;
            candidate.height = settings.Height;
            candidate.fps = settings.Fps;
            candidate.sampleRate = rate;
            candidate.channels = 2;
            candidate.Path = path;
            encoder = candidate;
            return true;
        }
        catch (Exception failure) when (failure is DllNotFoundException
            || failure is EntryPointNotFoundException || failure is BadImageFormatException)
        {
            reason = failure.Message;
            candidate.Dispose();
            return false;
        }
    }

    /// <summary>
    /// 提交一帧 RGBA。buffer 必须正好是 width*height*4 字节，且已按 Unity 的
    /// 左下原点排布（内部会翻转）。
    /// </summary>
    public void Submit(byte[] rgba)
    {
        if (rgba == null || rgba.Length != width * height * 4)
            throw new ArgumentException("frame buffer size mismatch", nameof(rgba));

        ThrowIfFailed();

        // 工程未开 allowUnsafeCode，用 GCHandle 固定数组取指针
        GCHandle pin = GCHandle.Alloc(rgba, GCHandleType.Pinned);

        try
        {
            if (UVVideoAndroid_SubmitRGBA(handle, pin.AddrOfPinnedObject(), width, height) != 0)
                ThrowIfFailed();
        }
        finally
        {
            pin.Free();
        }

        SubmittedFrames++;
    }

    public void SubmitAudio(float[] interleaved, int sampleFrames, long timestampUs)
    {
        if (interleaved == null || sampleFrames <= 0) return;
        if (interleaved.Length < sampleFrames * channels)
            throw new ArgumentException("audio buffer too small", nameof(interleaved));

        ThrowIfFailed();

        GCHandle pin = GCHandle.Alloc(interleaved, GCHandleType.Pinned);

        try
        {
            if (UVVideoAndroid_SubmitAudio(handle, pin.AddrOfPinnedObject(), sampleFrames, timestampUs) != 0)
                ThrowIfFailed();
        }
        finally
        {
            pin.Free();
        }
    }

    public void Finish()
    {
        if (ending) return;

        UVVideoAndroid_Finish(handle);
        ending = true;
    }

    public bool IsDone
    {
        get
        {
            int state = UVVideoAndroid_State(handle, out int submitted, out int written);
            SubmittedFrames = submitted;
            WrittenFrames = written;

            if (state < 0)
                throw new InvalidOperationException(Marshal.PtrToStringAnsi(UVVideoAndroid_Error(handle)));

            return state == 1;
        }
    }

    public void ThrowIfFailed()
    {
        if (handle == IntPtr.Zero) return;

        if (UVVideoAndroid_State(handle, out int submitted, out int written) < 0)
            throw new InvalidOperationException(Marshal.PtrToStringAnsi(UVVideoAndroid_Error(handle)));

        SubmittedFrames = submitted;
        WrittenFrames = written;
    }

    public void Dispose()
    {
        if (handle == IntPtr.Zero) return;

        // 未正常收尾时先中止，避免 worker 还在等队列空间
        int state = UVVideoAndroid_State(handle, out _, out _);

        if (state == 0)
        {
            UVVideoAndroid_Abort(handle);
            UVVideoAndroid_Finish(handle);
        }

        UVVideoAndroid_Destroy(handle);
        handle = IntPtr.Zero;
    }
}
