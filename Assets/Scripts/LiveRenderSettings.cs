using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

[Serializable]
public sealed class LiveRenderSettings
{
    public static readonly Vector2Int[] Resolutions = {
        new Vector2Int(1280,720),new Vector2Int(1920,1080),new Vector2Int(2560,1440),new Vector2Int(3840,2160),
        new Vector2Int(1920,1200),new Vector2Int(2560,1600),new Vector2Int(3840,2400),
        new Vector2Int(1920,1440),new Vector2Int(2560,1920),new Vector2Int(3840,2880),
        new Vector2Int(2560,1080),new Vector2Int(3440,1440),new Vector2Int(5120,2160),
        new Vector2Int(3840,1080),new Vector2Int(5120,1440),new Vector2Int(7680,2160) };
    public static readonly string[] ResolutionLabels = {
        "16:9  1280 x 720 (720p)","16:9  1920 x 1080 (1K)","16:9  2560 x 1440 (2.5K)","16:9  3840 x 2160 (4K)",
        "16:10  1920 x 1200 (1K)","16:10  2560 x 1600 (2.5K)","16:10  3840 x 2400 (4K)",
        "4:3  1920 x 1440 (1K)","4:3  2560 x 1920 (2.5K)","4:3  3840 x 2880 (4K)",
        "21:9  2560 x 1080 (1K)","21:9  3440 x 1440 (2.5K)","21:9  5120 x 2160 (4K)",
        "32:9  3840 x 1080 (1K)","32:9  5120 x 1440 (2.5K)","32:9  7680 x 2160 (4K)" };
    public static readonly int[] FrameRates = { 24,30,60,120 };
    public static readonly int[] Bitrates = { 8,12,20,30,50,80 };
    public static readonly string[] EncoderLabels = { "H.264 / CPU (libx264)","H.264 / NVIDIA (NVENC)","H.265 / CPU (libx265)","H.265 / NVIDIA (NVENC)" };
    public static readonly string[] Encoders = { "libx264","h264_nvenc","libx265","hevc_nvenc" };
    // Android 只有 MediaCodec（芯片硬编），对应 LiveAndroidVideoEncoder 的 hevc 开关
    public static readonly string[] AndroidEncoderLabels = { "H.264 / MediaCodec (hardware)","H.265 / MediaCodec (hardware)" };
    public int Width=1920,Height=1080,Fps=60,BitrateMbps=20;
    public string Encoder="libx264";

    public void Validate()
    {
        if(!Resolutions.Contains(new Vector2Int(Width,Height))||!FrameRates.Contains(Fps)||!Bitrates.Contains(BitrateMbps)||!Encoders.Contains(Encoder))
            throw new ArgumentException("Unsupported render setting");
    }

    public static string FileName(LiveEntry live,DateTime timestamp)
    {
        var invalid=Path.GetInvalidFileNameChars();
        string title=new string((live.SongName??"Live").Select(c=>invalid.Contains(c)||char.IsControl(c)?'_':c).ToArray()).Trim().TrimEnd('.');
        if(title.Length>100)title=title.Substring(0,100).TrimEnd('.');
        return live.MusicId.ToString(CultureInfo.InvariantCulture)+"_"+title+"_"+timestamp.ToString("yyyyMMdd_HHmmss_fff",CultureInfo.InvariantCulture)+".mp4";
    }

    public const string FfmpegMissingMessage="FFmpeg not deceted";
    static bool ffmpegChecked,ffmpegDetected;
    static string ffmpegExecutable;
    public static bool FfmpegDetected { get { CheckFfmpegOnce();return ffmpegDetected; } }
    public static string RequireFfmpeg()
    {
        if(!FfmpegDetected)throw new InvalidOperationException(FfmpegMissingMessage);
        return ffmpegExecutable;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void CheckFfmpegOnce()
    {
        if(ffmpegChecked)return;
        ffmpegChecked=true;

        string executable;

        try
        {
            executable=FindFfmpeg();
        }
        catch(Exception error)
        {
            UnityEngine.Debug.LogWarning("[FFmpeg] 找不到可执行文件: "+error.Message);
            return;
        }

        UnityEngine.Debug.Log("[FFmpeg] 使用 "+executable+" 做检测");

        // Unity IL2CPP 下 System.Diagnostics.Process.Start 创建子进程会失败
        // （"Native error= Success"，CreateProcess 返回 FALSE 但 GetLastError 为 0），
        // 同样的代码在 .NET 控制台/GUI 程序里都正常，所以这里直接走 kernel32。
        if (Application.platform==RuntimePlatform.WindowsPlayer||Application.platform==RuntimePlatform.WindowsEditor)
        {
            if(!NativeProcessProbe.Run(executable,"-version",8000,out int exitCode,out string stdout,out string error))
            {
                UnityEngine.Debug.LogWarning("[FFmpeg] 探测失败: "+error+"  ("+executable+")");
                return;
            }

            ffmpegDetected=exitCode==0
                &&stdout.IndexOf("ffmpeg",StringComparison.OrdinalIgnoreCase)>=0;

            if(ffmpegDetected)
            {
                ffmpegExecutable=executable;
                UnityEngine.Debug.Log("[FFmpeg] 检测通过: "+executable);
            }
            else
            {
                UnityEngine.Debug.LogWarning("[FFmpeg] -version 结果异常 exit="+exitCode+" output="
                    +stdout.Substring(0,Math.Min(200,stdout.Length)));
            }

            return;
        }

        try
        {
            using(var process=new Process{StartInfo=new ProcessStartInfo(executable,"-version")
                {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}})
            {
                process.Start();

                // 必须先同步读完输出再等退出：管道写满的话进程永远不退出，
                // 原来的 WaitForExit(3000) 会一直超时并把检测结果钉在 false。
                string version=process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();

                if(!process.WaitForExit(8000))
                {
                    try{process.Kill();}catch{}

                    UnityEngine.Debug.LogWarning("[FFmpeg] -version 超时: "+executable);
                    return;
                }

                ffmpegDetected=process.ExitCode==0
                    &&version.IndexOf("ffmpeg",StringComparison.OrdinalIgnoreCase)>=0;

                if(ffmpegDetected)
                    ffmpegExecutable=executable;
                else
                    UnityEngine.Debug.LogWarning("[FFmpeg] -version 结果异常 exit="+process.ExitCode+" output="
                        +version.Substring(0,Math.Min(200,version.Length)));
            }
        }
        catch(Exception error)
        {
            UnityEngine.Debug.LogWarning("[FFmpeg] 启动失败: "+error.Message);
            ffmpegDetected=false;
        }
    }

    public static string FindFfmpeg()
    {
        // Config.json 里的 FfmpegPath 一直没被读取过，配了也白配；先认它
        // 值可以是 ffmpeg.exe 本身，也可以直接填安装目录
        string configured = Config.Instance != null ? Config.Instance.FfmpegPath : null;

        if (!string.IsNullOrWhiteSpace(configured))
        {
            string trimmed = configured.Trim().Trim('"');

            if (File.Exists(trimmed))
                return trimmed;

            foreach (string name in new[] { "ffmpeg.exe", "ffmpeg.cmd", "ffmpeg.bat" })
            {
                string inside = Path.Combine(trimmed, name);

                if (File.Exists(inside))
                    return inside;
            }
        }

        var root=Path.GetFullPath(Path.Combine(Application.dataPath,".."));
        var candidates=new[]{Path.Combine(root,"ffmpeg.exe"),Path.Combine(root,"bin/ffmpeg.exe"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"ffmpeg/bin/ffmpeg.exe")};
        foreach(var path in candidates)if(File.Exists(path))return path;
        foreach(string directory in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator))
        {
            if(string.IsNullOrWhiteSpace(directory))continue;

            // 包管理器常在 PATH 里放 bat/cmd 垫片，只认 .exe 会漏
            string clean=directory.Trim().Trim('"');

            foreach(string name in new[]{"ffmpeg.exe","ffmpeg.cmd","ffmpeg.bat"})
            {
                string found=Path.Combine(clean,name);

                if(File.Exists(found))return found;
            }
        }
        throw new FileNotFoundException("FFmpeg was not found. Install FFmpeg or place ffmpeg.exe beside UmaViewer.exe.");
    }

    public static string Quote(string value) => "\""+value.Replace("\"","\\\"")+"\"";

    /// <summary>
    /// 逐个试打开编码器，返回真正可用的那几项（索引对应 Encoders/EncoderLabels）。
    ///
    /// NVENC 在旧驱动上会 configure 失败（"Driver does not support the required
    /// nvenc API version"），但 UI 照样列着，用户点了才报错。这里启动前先筛掉。
    /// 结果缓存：同一进程内 ffmpeg 和驱动不会中途变化。
    /// </summary>
    public static int[] AvailableEncoders(string ffmpegPath)
    {
        if(_availableEncoders!=null)return _availableEncoders;

        var usable=new System.Collections.Generic.List<int>();

        for(int i=0;i<Encoders.Length;i++)
        {
            string arguments="-hide_banner -loglevel error -f lavfi -i color=s=256x256:r=30"
                +" -frames:v 1 -c:v "+Encoders[i]+" -pix_fmt yuv420p -f null -";

            bool ok;

            if(Application.platform==RuntimePlatform.WindowsPlayer||Application.platform==RuntimePlatform.WindowsEditor)
                ok=NativeProcessProbe.Run(ffmpegPath,arguments,60000,out int code,out _,out _)&&code==0;
            else
                ok=TryRunEncoder(ffmpegPath,arguments,60000);

            UnityEngine.Debug.Log("[FFmpeg] 编码器 "+Encoders[i]+" 可用="+ok);

            if(ok)usable.Add(i);
        }

        // 一个都没有时退回 CPU 软编，至少别让下拉变空
        if(usable.Count==0)usable.Add(0);

        _availableEncoders=usable.ToArray();
        return _availableEncoders;
    }

    static int[] _availableEncoders;

    private static bool TryRunEncoder(string executable,string arguments,int timeoutMs)
    {
        try
        {
            using(var process=new Process{StartInfo=new ProcessStartInfo(executable,arguments)
                {UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true}})
            {
                process.Start();
                process.StandardError.ReadToEnd();
                return process.WaitForExit(timeoutMs)&&process.ExitCode==0;
            }
        }
        catch(Exception) { return false; }
    }
    public string EncoderArguments => "-c:v "+Encoder+" -preset "+(Encoder.EndsWith("nvenc",StringComparison.Ordinal)?"p5":"fast")+
        " -b:v "+BitrateMbps+"M -maxrate "+(BitrateMbps*3/2)+"M -bufsize "+(BitrateMbps*2)+"M -pix_fmt yuv420p";

    public void VerifyEncoder(string executable)
    {
        string arguments="-hide_banner -loglevel error -f lavfi -i color=s="+Width+"x"+Height+":r="+Fps
            +" -frames:v 1 "+EncoderArguments+" -f null -";

        // 同 CheckFfmpegOnce：IL2CPP 下 Process.Start 起不来，Windows 上走 kernel32
        if (Application.platform==RuntimePlatform.WindowsPlayer||Application.platform==RuntimePlatform.WindowsEditor)
        {
            if(!NativeProcessProbe.Run(executable,arguments,120000,out int exitCode,out string output,out string failure))
                throw new InvalidOperationException("Encoder unavailable: "+Encoder+"\n"+failure);

            if(exitCode!=0)
                throw new InvalidOperationException("Encoder unavailable: "+Encoder+"\n"+output.Trim());

            return;
        }

        using(var process=new Process{StartInfo=new ProcessStartInfo(executable,arguments)
            {UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true}})
        {
            process.Start();
            string error=process.StandardError.ReadToEnd();
            process.WaitForExit();
            if(process.ExitCode!=0)throw new InvalidOperationException("Encoder unavailable: "+Encoder+"\n"+error.Trim());
        }
    }
}
