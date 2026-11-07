using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Gallop.Live;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Debug=UnityEngine.Debug;

[DefaultExecutionOrder(-31000)]
public sealed class LiveVideoExporter : MonoBehaviour
{
    public static LiveVideoExporter Instance { get; private set; }
    public static bool IsActive => Instance != null && !Instance.finished;
    public int RenderedFrames => frame;
    public int TotalFrames => totalFrames;
    public string CurrentPhase { get; private set; } = "starting";
    public string FailureMessage => error;
    public int CapturedAudioSamples => audioSamples;
    public bool WasCancelled => cancelled;
    public void Cancel() { cancelled=true; }
    public static event Action<string,bool> Completed;
    private LiveRenderSettings settings;
    private LiveEntry live;
    private UmaViewerUI entryUI;
    private Director director;
    private string executable,output,tempFolder,videoFile,audioFile,error;
    private Process encoder;
    private readonly StringBuilder encoderErrors=new StringBuilder();
    private BinaryWriter audio;
    private int sampleRate,frame,totalFrames,audioSamples;
    private int previousFps,previousVsync;
    private float previousCaptureDelta,previousFixedDelta;
    private bool previousBackground,previousVmd,audioRecording,capturing,armed,cancelled,finished,cleaned,settingsSaved;
    private AudioConfiguration previousAudio;
    private RenderTexture target;
    private Texture2D readback;
    private bool asyncReadback;
    private readonly Queue<AsyncGPUReadbackRequest> readbacks=new Queue<AsyncGPUReadbackRequest>();
    private LiveVideoFrameWriter frameWriter;
    private LiveGpuVideoEncoder gpuEncoder;
    public bool UsesGpuDirect {get;private set;}
    public int GpuWrittenFrames {get;private set;}
    public double GpuSubmissionMs {get;private set;}
    public string VideoBackend {get;private set;}="FFmpeg rawvideo";
    public int PrePlayImageCount {get;private set;}
    public int PrePlayManualFrames {get;private set;}
    private readonly Stopwatch captureTimer=new Stopwatch();
    private int submittedFrames;
    public double CaptureSeconds => captureTimer.Elapsed.TotalSeconds;
    public bool UsesAsyncReadback => asyncReadback;
    public double MainThreadReadbackMs {get;private set;}
    public double PipeWriteMs {get;private set;}
    private readonly Dictionary<Camera,RenderTexture> targets=new Dictionary<Camera,RenderTexture>();
    private Text status;
    private Button closeButton;
    private Task finalization;
    private bool frameReady,manualRenderRequested;
    // Explicit batch jobs can render while their player window is hidden.
    public void RenderBatchFrame()=>RequestManualFrame();
    void RequestManualFrame()
    {
        if(!capturing||!armed||director==null)return;
        // Callers run in LateUpdate, before Unity's UpdateAllSkinnedMeshes.
        // Defer the request to CaptureFrames so cameras, props and the skinned
        // vertex buffers all describe the same frame (1092 cut at frame1283).
        manualRenderRequested=true;
    }
    void RenderManualCameras()
    {
        director.RenderOfflineCameraStacks(target);
    }

    void RenderPendingManualCameras()
    {
        if(!manualRenderRequested)return;
        manualRenderRequested=false;
        // A visible worker may already have rendered the complete camera stack.
        // Repeating it accumulates partial multi-camera fades a second time and
        // advances one-shot capture state within the same export frame.
        if(!frameReady)RenderManualCameras();
    }

    public static void Begin(UmaViewerUI ui,LiveEntry song,LiveRenderSettings options,string directory)
    {
        if(IsActive)throw new InvalidOperationException("A Live render is already running");
        LiveRenderSettings.RequireFfmpeg();
        options.Validate();
        var owner=new GameObject("Live video export");
        DontDestroyOnLoad(owner);
        var exporter=owner.AddComponent<LiveVideoExporter>();
        Instance=exporter;exporter.entryUI=ui;exporter.live=song;exporter.settings=options;
        exporter.output=Path.Combine(directory,LiveRenderSettings.FileName(song,DateTime.Now));
        exporter.CreateProgress();
        exporter.StartCoroutine(exporter.RunSafely());
    }

    public static void AttachPending(Director owner)
    {
        var exporter=Instance;
        if(exporter==null||exporter.finished||exporter.director!=null)return;
        try { exporter.Attach(owner); }
        catch(Exception failure) { exporter.error=failure.Message; }
    }

    private void Attach(Director owner)
    {
        director=owner;
        PrePlayImageCount=owner.ScreenCaptures?.Images.Count??0;
        PrePlayManualFrames=owner.PrePlayManualFrames;
        Time.captureDeltaTime=1f/settings.Fps;
        Time.fixedDeltaTime=1f/settings.Fps;
        Application.targetFrameRate=-1;QualitySettings.vSyncCount=0;
        target=new RenderTexture(settings.Width,settings.Height,24,RenderTextureFormat.ARGB32){name="Live video output",antiAliasing=1};
        target.Create();
        director.BeginOfflineRender(target);
        director.BeginSubtitleCapture(target);
        asyncReadback=SystemInfo.supportsAsyncGPUReadback;
        if(!asyncReadback)readback=new Texture2D(settings.Width,settings.Height,TextureFormat.RGB24,false);
        foreach(var camera in FindObjectsOfType<Camera>(true))Bind(camera);
        RenderPipelineManager.beginCameraRendering+=BeforeCamera;
        RenderPipelineManager.endCameraRendering+=AfterCamera;
        totalFrames=Mathf.CeilToInt(director.totalTime*settings.Fps);
        sampleRate=AudioSettings.outputSampleRate;
        if(AudioSettings.speakerMode!=AudioSpeakerMode.Stereo)throw new InvalidOperationException("Stereo audio output was not initialized");
        if(!AudioRenderer.Start())throw new InvalidOperationException("Could not start offline audio recording");
        audioRecording=true;
        audio=new BinaryWriter(File.Create(audioFile));
        WriteWaveHeader(0);
        LiveSongAudioControl.Start(director,0f,0);
        string nativeFile=Path.ChangeExtension(videoFile,"ts");
        if(LiveGpuVideoEncoder.TryCreate(target,settings,nativeFile,out gpuEncoder,out string nativeReason))
        {
            videoFile=nativeFile;UsesGpuDirect=true;asyncReadback=false;
            VideoBackend="NVENC GPU-direct (D3D11)";
            Debug.Log("[LiveRender] "+VideoBackend+"; no per-frame pixel readback/rawvideo pipe");
        }
        else
        {
            if(!string.IsNullOrEmpty(nativeReason))Debug.LogWarning("[LiveRender] GPU-direct unavailable, using FFmpeg: "+nativeReason);
            StartEncoder();
        }
        bool parallel=true;
        if(!UsesGpuDirect)frameWriter=new LiveVideoFrameWriter(encoder.StandardInput.BaseStream,settings.Width*settings.Height*(asyncReadback?4:3),parallel);
        captureTimer.Start();
        capturing=true;
        CurrentPhase="rendering";
        StartCoroutine(CaptureFrames());
    }

    private IEnumerator RunSafely()
    {
        var routine=Run();
        while(true)
        {
            object current;
            try { if(!routine.MoveNext())break;current=routine.Current; }
            catch(Exception failure) { error=failure.Message;break; }
            yield return current;
        }
        (routine as IDisposable)?.Dispose();
        capturing=false;
        if(director!=null)
        {
            director.SetPlaybackPaused(true);
            // A quick cancellation can precede the entry transition's end.
            // Finish that transition before ExitLive starts another one.
            while(UmaSceneController.instance!=null&&UmaSceneController.instance.CavansInstance!=null)yield return null;
        }
        Cleanup();
        finished=true;
        CurrentPhase=cancelled?"cancelled":error==null?"completed":"failed";
        status.text=cancelled?"Render cancelled.":error==null?"Saved\n"+output:"Render failed\n"+error;
        closeButton.GetComponentInChildren<Text>().text="Close";
        closeButton.onClick.RemoveAllListeners();
        closeButton.onClick.AddListener(()=>Destroy(gameObject));
        closeButton.interactable=true;
        if(error!=null)Debug.LogError("[LiveRender] "+error);
        else Debug.Log("[LiveRender] "+(cancelled?"Cancelled":"Saved "+output+" ("+frame+" frames)"));
        Completed?.Invoke(output,!cancelled&&error==null);
    }

    private IEnumerator Run()
    {
        status.text="Checking encoder...";
        CurrentPhase="checking-encoder";
        executable=LiveRenderSettings.RequireFfmpeg();
        var check=Task.Run(()=>settings.VerifyEncoder(executable));
        while(!check.IsCompleted)yield return null;
        if(check.IsFaulted)throw check.Exception.GetBaseException();
        if(cancelled)yield break;
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        tempFolder=Path.Combine(Path.GetDirectoryName(output),".umaviewer-render-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);
        videoFile=Path.Combine(tempFolder,"video.mp4");audioFile=Path.Combine(tempFolder,"audio.wav");
        previousFps=Application.targetFrameRate;previousVsync=QualitySettings.vSyncCount;
        previousCaptureDelta=Time.captureDeltaTime;previousFixedDelta=Time.fixedDeltaTime;
        previousBackground=Application.runInBackground;previousAudio=AudioSettings.GetConfiguration();
        previousVmd=entryUI.isRecordVMD;settingsSaved=true;
        Application.runInBackground=true;
        var configuration=previousAudio;
        configuration.speakerMode=AudioSpeakerMode.Stereo;configuration.sampleRate=48000;
        if((configuration.speakerMode!=previousAudio.speakerMode||configuration.sampleRate!=previousAudio.sampleRate)&&!AudioSettings.Reset(configuration))
            throw new InvalidOperationException("Could not initialize 48 kHz stereo audio");
        entryUI.isRecordVMD=false;
        status.text="Loading "+live.SongName+"...";
        CurrentPhase="loading";
        entryUI.PlayLive();
        double deadline=Time.realtimeSinceStartupAsDouble+600;
        long completedDownloads=UmaViewerDownload.CompletedAssetDownloads;
        while(director==null&&error==null)
        {
            // A cold Standalone cache can take longer than scene setup itself.
            // Downloads have their own request timeouts and retry limits.
            long currentDownloads=UmaViewerDownload.CompletedAssetDownloads;
            if(UmaViewerDownload.IsDownloadingAssets||currentDownloads!=completedDownloads)
                deadline=Time.realtimeSinceStartupAsDouble+600;
            else if(Time.realtimeSinceStartupAsDouble>=deadline)break;
            completedDownloads=currentDownloads;
            yield return null;
        }
        if(error!=null)throw new InvalidOperationException(error);
        if(director==null)throw new TimeoutException("Live setup did not complete");
        while(capturing&&!cancelled&&error==null&&director!=null)yield return null;
        if(error!=null)throw new InvalidOperationException(error);
        if(cancelled)yield break;
        if(frame!=totalFrames)throw new InvalidOperationException("Live ended before the last export frame");
        while(readbacks.Count>0)
        {
            DrainReadbacks();
            if(readbacks.Count>0)yield return null;
        }
        if(submittedFrames!=frame)throw new InvalidOperationException("Video readback frame count mismatch");
        if(UsesGpuDirect)
        {
            gpuEncoder.Finish();
            while(!gpuEncoder.IsDone)yield return null;
            GpuWrittenFrames=gpuEncoder.WrittenFrames;
            if(GpuWrittenFrames!=frame)throw new InvalidOperationException("GPU video encoder frame count mismatch");
            gpuEncoder.Dispose();gpuEncoder=null;
        }
        else
        {
            frameWriter.Complete();
            while(!frameWriter.Completion.IsCompleted)yield return null;
            frameWriter.ThrowIfFailed();
            if(frameWriter.WrittenFrames!=frame)throw new InvalidOperationException("Video encoder frame count mismatch");
            PipeWriteMs=frameWriter.WriteMilliseconds;
        }
        captureTimer.Stop();
        int wanted=(int)System.Math.Round((double)frame*sampleRate/settings.Fps);
        while(audioSamples<wanted)
        {
            CaptureAudio(wanted);
            if(audioSamples<wanted)yield return null;
        }
        AudioRenderer.Stop();audioRecording=false;
        WriteWaveHeader(audioSamples);audio.Dispose();audio=null;
        foreach(var bank in LiveSongAudioControl.Banks(director))UmaViewerAudio.Stop(bank);
        director.SetPlaybackPaused(true);
        status.text="Finalizing MP4...";
        CurrentPhase="finalizing";
        if(encoder!=null)encoder.StandardInput.Close();
        finalization=Task.Run(()=>
        {
            if(encoder!=null)
            {
                if(!encoder.WaitForExit(120000))throw new TimeoutException("Encoder did not finish");
                if(encoder.ExitCode!=0)throw new InvalidOperationException("Video encoding failed: "+encoderErrors);
            }
            string destination=Path.Combine(tempFolder,"complete.mp4");
            string arguments="-hide_banner -loglevel error -nostdin -n -i "+LiveRenderSettings.Quote(videoFile)+" -i "+LiveRenderSettings.Quote(audioFile)+
                " -map 0:v:0 -map 1:a:0 -c:v copy -c:a aac -b:a 320k -t "+((double)frame/settings.Fps).ToString("R",System.Globalization.CultureInfo.InvariantCulture)+
                " -movflags +faststart "+LiveRenderSettings.Quote(destination);
            using(var mux=new Process{StartInfo=new ProcessStartInfo(executable,arguments){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true}})
            {
                mux.Start();string message=mux.StandardError.ReadToEnd();mux.WaitForExit();
                if(mux.ExitCode!=0)throw new InvalidOperationException("Audio/video mux failed: "+message);
            }
            if(!cancelled)File.Move(destination,output);
        });
        while(!finalization.IsCompleted)yield return null;
        if(finalization.IsFaulted)throw finalization.Exception.GetBaseException();
    }

    private void Update()
    {
        if(finished)return;
        if(Input.GetKeyDown(KeyCode.Escape))Cancel();
        if(gpuEncoder!=null&&error==null&&!cancelled)
        {
            try {gpuEncoder.ThrowIfFailed();}
            catch(Exception failure){error=failure.Message;capturing=false;}
        }
        if(frameWriter!=null&&error==null&&!cancelled)
        {
            try {DrainReadbacks();frameWriter.ThrowIfFailed();}
            catch(Exception failure) {error=failure.Message;capturing=false;}
        }
        if(!capturing||director==null||cancelled)return;
        director.SetOfflineRenderFrame(frame,settings.Fps);
        armed=true;
        status.text="Rendering "+live.SongName+"\n"+frame+" / "+totalFrames+" frames  ("+(100f*frame/totalFrames).ToString("F1")+"%)\n"+
            settings.Width+" x "+settings.Height+"  |  "+settings.Fps+" fps  |  "+settings.BitrateMbps+" Mbps";
        UnityEngine.Cursor.lockState=CursorLockMode.None;UnityEngine.Cursor.visible=true;
    }

    private void Bind(Camera camera)
    {
        if(director==null||camera==null||!director.IsPresentationOutputCamera(camera))return;
        if(!targets.ContainsKey(camera))targets[camera]=camera.targetTexture;
        camera.targetTexture=target;
        camera.ResetAspect();
    }
    private void BeforeCamera(ScriptableRenderContext context,Camera camera) { if(capturing)Bind(camera); }
    private void AfterCamera(ScriptableRenderContext context,Camera camera)
    {
        if(!capturing||!armed||cancelled||director==null||camera!=director.FinalRenderCamera)return;
        context.Submit();frameReady=true;
    }
    private IEnumerator CaptureFrames()
    {
        var end=new WaitForEndOfFrame();
        while(capturing)
        {
            yield return end;
            if(!capturing||!armed||cancelled||director==null)continue;
            try {RenderPendingManualCameras();}
            catch(Exception failure){error=failure.Message;capturing=false;continue;}
            if(!frameReady)continue;
            frameReady=false;
            CaptureFrame();
        }
    }
    private void CaptureFrame()
    {
        var previous=RenderTexture.active;
        try
        {
            if(Mathf.Abs(director._liveCurrentTime-Director.OfflineRenderFrameTime(frame,settings.Fps))>0.0001f)
                throw new InvalidOperationException("Export timeline missed frame "+frame);
            director.RenderSubtitlesTo(target);
            long start=Stopwatch.GetTimestamp();double priorReadback=MainThreadReadbackMs;
            if(gpuEncoder!=null)
            {
                gpuEncoder.Submit(target);submittedFrames++;
                GpuSubmissionMs+=(Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency;
            }
            else if(asyncReadback)
            {
                if(readbacks.Count>=3)
                {
                    readbacks.Peek().WaitForCompletion();
                    DrainReadbacks();
                }
                readbacks.Enqueue(AsyncGPUReadback.Request(target,0,TextureFormat.RGBA32));
            }
            else
            {
                RenderTexture.active=target;
                readback.ReadPixels(new Rect(0,0,settings.Width,settings.Height),0,0);
                var data=frameWriter.Rent();readback.GetRawTextureData<byte>().CopyTo(data);
                frameWriter.Submit(data);submittedFrames++;
            }
            if(!UsesGpuDirect)MainThreadReadbackMs=priorReadback+(Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency;
            CaptureAudio((int)System.Math.Round((double)(frame+1)*sampleRate/settings.Fps));
            frame++;armed=false;
            if(frame>=totalFrames)capturing=false;
        }
        catch(Exception failure) { error=failure.Message;capturing=false; }
        finally { RenderTexture.active=previous; }
    }

    private void DrainReadbacks()
    {
        long start=Stopwatch.GetTimestamp();
        while(readbacks.Count>0&&readbacks.Peek().done)
        {
            var request=readbacks.Dequeue();
            if(request.hasError)throw new InvalidOperationException("GPU video readback failed");
            var data=frameWriter.Rent();request.GetData<byte>().CopyTo(data);
            frameWriter.Submit(data);submittedFrames++;
        }
        MainThreadReadbackMs+=(Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency;
    }

    private void CaptureAudio(int maximum)
    {
        int count=AudioRenderer.GetSampleCountForCaptureFrame();
        using(var buffer=new NativeArray<float>(count*2,Allocator.Temp))
        {
            if(!AudioRenderer.Render(buffer))throw new InvalidOperationException("Offline audio rendering failed");
            int write=System.Math.Min(count,maximum-audioSamples);
            if(write<=0)return;
            var samples=buffer.ToArray();var bytes=new byte[write*8];Buffer.BlockCopy(samples,0,bytes,0,bytes.Length);
            audio.Write(bytes);audioSamples+=write;
        }
    }
    private void WriteWaveHeader(int samples)
    {
        long previous=audio.BaseStream.Position;audio.BaseStream.Position=0;
        audio.Write(Encoding.ASCII.GetBytes("RIFF"));audio.Write(36+samples*8);audio.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        audio.Write(16);audio.Write((short)3);audio.Write((short)2);audio.Write(sampleRate);audio.Write(sampleRate*8);audio.Write((short)8);audio.Write((short)32);
        audio.Write(Encoding.ASCII.GetBytes("data"));audio.Write(samples*8);
        audio.BaseStream.Position=System.Math.Max(44,previous);
    }
    private void StartEncoder()
    {
        int filterThreads=0,encoderThreads=0;
        string arguments="-hide_banner -loglevel warning -nostats -n -f rawvideo -pixel_format "+(asyncReadback?"rgba":"rgb24")+" -video_size "+settings.Width+"x"+settings.Height+
            " -framerate "+settings.Fps+" -i pipe:0 -filter_threads "+filterThreads+" -vf vflip -an "+settings.EncoderArguments+
            " -threads "+encoderThreads+" -movflags +faststart "+LiveRenderSettings.Quote(videoFile);
        encoder=new Process{StartInfo=new ProcessStartInfo(executable,arguments){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardError=true}};
        encoder.ErrorDataReceived+=(sender,args)=>{if(args.Data!=null)lock(encoderErrors)encoderErrors.AppendLine(args.Data);};
        encoder.Start();encoder.BeginErrorReadLine();
    }

    private void Cleanup()
    {
        if(cleaned)return;cleaned=true;capturing=false;
        RenderPipelineManager.beginCameraRendering-=BeforeCamera;RenderPipelineManager.endCameraRendering-=AfterCamera;
        foreach(var pair in targets)if(pair.Key!=null){pair.Key.targetTexture=pair.Value;pair.Key.ResetAspect();}
        targets.Clear();
        foreach(var camera in FindObjectsOfType<Camera>(true))if(camera.targetTexture==target&&target!=null)camera.targetTexture=null;
        if(audioRecording){AudioRenderer.Stop();audioRecording=false;}
        audio?.Dispose();audio=null;
        gpuEncoder?.Dispose();gpuEncoder=null;
        frameWriter?.Abort();
        if(encoder!=null)
        {
            try { if(!encoder.HasExited){encoder.Kill();encoder.WaitForExit();} }
            catch(InvalidOperationException) { }
            encoder.Dispose();encoder=null;
        }
        if(frameWriter!=null)
        {
            PipeWriteMs=frameWriter.WriteMilliseconds;
            frameWriter.Dispose();frameWriter=null;
        }
        while(readbacks.Count>0)readbacks.Dequeue().WaitForCompletion();
        captureTimer.Stop();
        if(target!=null){target.Release();Destroy(target);}
        if(readback!=null)Destroy(readback);
        if(settingsSaved)
        {
            Time.captureDeltaTime=previousCaptureDelta;Time.fixedDeltaTime=previousFixedDelta;
            Application.targetFrameRate=previousFps;QualitySettings.vSyncCount=previousVsync;Application.runInBackground=previousBackground;
            if(entryUI!=null)entryUI.isRecordVMD=previousVmd;
            if(AudioSettings.speakerMode!=previousAudio.speakerMode||AudioSettings.outputSampleRate!=previousAudio.sampleRate)AudioSettings.Reset(previousAudio);
        }
        if(director!=null){director.EndSubtitleCapture();director.FinishOfflineRender();}
        if(tempFolder!=null&&Directory.Exists(tempFolder)&&(finalization==null||finalization.IsCompleted))Directory.Delete(tempFolder,true);
        UnityEngine.Cursor.lockState=CursorLockMode.None;UnityEngine.Cursor.visible=true;
    }
    private void OnDestroy(){Cleanup();if(Instance==this)Instance=null;}

    private void CreateProgress()
    {
        var canvas=gameObject.AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=32000;
        gameObject.AddComponent<GraphicRaycaster>();
        var scaler=gameObject.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1280,720);
        var blocker=new GameObject("Render input blocker",typeof(RectTransform),typeof(Image));
        blocker.transform.SetParent(transform,false);
        var rect=(RectTransform)blocker.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
        blocker.GetComponent<Image>().color=new Color(0,0,0,.45f);
        var panel=LiveRenderUI.Panel("Render progress",transform,new Vector2(680,250));
        status=LiveRenderUI.Text("Progress",panel,new Vector2(0,25),new Vector2(630,150),22);
        closeButton=LiveRenderUI.Button("Cancel",panel,new Vector2(0,-82),()=>{cancelled=true;closeButton.interactable=false;status.text="Cancelling...";});
    }
}
