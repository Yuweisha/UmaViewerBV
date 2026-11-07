using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

// D3D11 textures stay on the GPU. Only timestamped compressed packets reach CPU/disk.
internal sealed class LiveGpuVideoEncoder : IDisposable
{
    const string Library="UmaVideoNative";
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl,CharSet=CharSet.Unicode)]
    static extern IntPtr UVVideo_Create(IntPtr source,int fps,int bitrate,int hevc,string path);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] static extern IntPtr UVVideo_GetRenderEvent();
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] static extern IntPtr UVVideo_Error(IntPtr handle);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] static extern int UVVideo_State(IntPtr handle,out int submitted,out int written);
    [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] static extern void UVVideo_Destroy(IntPtr handle);
    IntPtr handle,callback;
    RenderTexture input;
    bool ending;
    public int WrittenFrames {get;private set;}
    public static bool TryCreate(RenderTexture source,LiveRenderSettings settings,string path,out LiveGpuVideoEncoder encoder,out string reason)
    {
        encoder=null;reason=null;
        if(SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Direct3D11||!settings.Encoder.EndsWith("_nvenc",StringComparison.Ordinal))return false;
        if(Array.IndexOf(Environment.GetCommandLineArgs(),"-renderGpuDirectOff")>=0)return false;
        var candidate=new LiveGpuVideoEncoder();
        try
        {
            var descriptor=source.descriptor;descriptor.depthBufferBits=0;descriptor.msaaSamples=1;
            candidate.input=new RenderTexture(descriptor){name="NVENC GPU input"};candidate.input.Create();
            candidate.handle=UVVideo_Create(candidate.input.GetNativeTexturePtr(),settings.Fps,settings.BitrateMbps,settings.Encoder=="hevc_nvenc"?1:0,path);
            if(candidate.handle==IntPtr.Zero)throw new InvalidOperationException(Marshal.PtrToStringAnsi(UVVideo_Error(IntPtr.Zero)));
            candidate.callback=UVVideo_GetRenderEvent();encoder=candidate;return true;
        }
        catch(Exception error) when(error is DllNotFoundException||error is EntryPointNotFoundException||error is InvalidOperationException||error is BadImageFormatException)
        {reason=error.Message;candidate.Dispose();return false;}
    }
    void Issue(int operation)
    {
        var cmd=CommandBufferPool.Get("NVENC GPU encode");
        try {cmd.IssuePluginEventAndData(callback,operation,handle);Graphics.ExecuteCommandBuffer(cmd);}
        finally {CommandBufferPool.Release(cmd);}
    }
    public void Submit(RenderTexture source)
    {
        ThrowIfFailed();
        // Match the existing FFmpeg vflip without reading pixels back to CPU.
        Graphics.Blit(source,input,new Vector2(1,-1),new Vector2(0,1));
        Issue(1);
    }
    public void Finish(){if(!ending){Issue(2);ending=true;}}
    public bool IsDone
    {
        get {int state=UVVideo_State(handle,out _,out int written);WrittenFrames=written;if(state<0)throw new InvalidOperationException(Marshal.PtrToStringAnsi(UVVideo_Error(handle)));return state==1;}
    }
    public void ThrowIfFailed()
    {
        if(UVVideo_State(handle,out _,out int written)<0)throw new InvalidOperationException(Marshal.PtrToStringAnsi(UVVideo_Error(handle)));
        WrittenFrames=written;
    }
    public void Dispose()
    {
        if(handle!=IntPtr.Zero)
        {
            // Normal completion has already drained every render event. Abort
            // requires a one-off barrier before freeing a queued callback's data.
            int state=UVVideo_State(handle,out _,out _);
            if(state!=1&&callback!=IntPtr.Zero)
            {
                Finish();
                AsyncGPUReadback.Request(input).WaitForCompletion();
            }
            UVVideo_Destroy(handle);handle=IntPtr.Zero;
        }
        if(input!=null){input.Release();UnityEngine.Object.Destroy(input);input=null;}
    }
}
