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
        try
        {
            string executable=FindFfmpeg();
            using(var process=new Process{StartInfo=new ProcessStartInfo(executable,"-version")
                {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}})
            {
                process.Start();
                var output=process.StandardOutput.ReadToEndAsync();
                var error=process.StandardError.ReadToEndAsync();
                if(!process.WaitForExit(3000)){process.Kill();return;}
                string version=output.GetAwaiter().GetResult();
                error.GetAwaiter().GetResult();
                ffmpegDetected=process.ExitCode==0&&version.StartsWith("ffmpeg version",StringComparison.OrdinalIgnoreCase);
                if(ffmpegDetected)ffmpegExecutable=executable;
            }
        }
        catch(Exception){ffmpegDetected=false;}
    }

    public static string FindFfmpeg()
    {
        var root=Path.GetFullPath(Path.Combine(Application.dataPath,".."));
        var candidates=new[]{Path.Combine(root,"ffmpeg.exe"),Path.Combine(root,"bin/ffmpeg.exe"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"ffmpeg/bin/ffmpeg.exe")};
        foreach(var path in candidates)if(File.Exists(path))return path;
        foreach(string directory in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator))
        {
            if(string.IsNullOrWhiteSpace(directory))continue;
            string path=Path.Combine(directory.Trim('"'),"ffmpeg.exe");
            if(File.Exists(path))return path;
        }
        throw new FileNotFoundException("FFmpeg was not found. Install FFmpeg or place ffmpeg.exe beside UmaViewer.exe.");
    }

    public static string Quote(string value) => "\""+value.Replace("\"","\\\"")+"\"";
    public string EncoderArguments => "-c:v "+Encoder+" -preset "+(Encoder.EndsWith("nvenc",StringComparison.Ordinal)?"p5":"fast")+
        " -b:v "+BitrateMbps+"M -maxrate "+(BitrateMbps*3/2)+"M -bufsize "+(BitrateMbps*2)+"M -pix_fmt yuv420p";

    public void VerifyEncoder(string executable)
    {
        using(var process=new Process{StartInfo=new ProcessStartInfo(executable,"-hide_banner -loglevel error -f lavfi -i color=s="+Width+"x"+Height+":r="+Fps+" -frames:v 1 "+EncoderArguments+" -f null -")
            {UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true}})
        {
            process.Start();
            string error=process.StandardError.ReadToEnd();
            process.WaitForExit();
            if(process.ExitCode!=0)throw new InvalidOperationException("Encoder unavailable: "+Encoder+"\n"+error.Trim());
        }
    }
}
