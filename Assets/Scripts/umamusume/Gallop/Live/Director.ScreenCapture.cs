using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gallop.Live
{
    public partial class Director
    {
        public LiveScreenCaptureController ScreenCaptures {get;private set;}
        public Live2dBackGroundController BackgroundImages {get;private set;}
        public int PrePlayManualFrames {get;private set;}

        void InitializeObjectImages()
        {
            if(ScreenCaptures==null)ScreenCaptures=new LiveScreenCaptureController();
            BackgroundImages=new Live2dBackGroundController();
            BackgroundImages.LoadAllBackgroundTexture(_liveTimelineControl);
            _liveTimelineControl.AddExecuteScreenCapture(ExecuteScreenCapture);
        }
        void ExecuteScreenCapture(int id,float scale)=>ScreenCaptures.ExecuteScreenCapture(this,MainRenderCamera,id,scale);

        // Native Live3dData.PrePlayMusicId / LiveViewController.CreateSubDirector:
        // hide the main director, play the preparation Live with the selected
        // cast, retain its captured images, then destroy it before main playback.
        // Both ordinary entry and pending offline export pass this same gate.
        public IEnumerator PreparePrePlayImages(List<LiveCharacterLoadData> characters,int mode)
        {
            int id=LiveSettingsResourceMap.Load(live.MusicId).PrePlayMusicId;
            if(id<=0)yield break;
            var settings=LiveSettingsResourceMap.Load(id);
            if(string.IsNullOrEmpty(settings.SourceCsv))throw new InvalidOperationException("Missing pre-play settings: "+id);
            var entry=new LiveEntry(settings.SourceCsv){MusicId=id,MemberCount=live.MemberCount,SongName=live.SongName};
            var roots=GetLivePreloadEntries(entry,characters,RequireStage);
            bool loaded=false;UmaAssetManager.PreLoadAndRun(roots,()=>loaded=true);
            while(!loaded)yield return null;
            Director sub=null;RenderTexture target=null;
            bool rendered=false;
            Action<ScriptableRenderContext,Camera> afterCamera=(context,camera)=>
            {
                if(sub!=null&&camera==sub.FinalRenderCamera)rendered=true;
            };
            float previousCaptureDelta=Time.captureDeltaTime;
            bool active=gameObject.activeSelf;
            try
            {
                gameObject.SetActive(false);
                var prefab=Builder.LiveControllerPrefab.GetComponentInChildren<Director>(true);
                sub=Instantiate(prefab.gameObject,transform.parent).GetComponent<Director>();sub.name="PrePlayDirector";
                sub.live=entry;sub.RequireStage=RequireStage;sub.SongPartVariationId=SongPartVariationId;
                sub.Initialize();
                var backgroundEntries=Live2dBackGroundController.ResourcePaths(sub._liveTimelineControl)
                    .Select(path=>UmaViewerMain.Instance.AbList.TryGetValue(path,out var resource)?resource:
                        throw new InvalidOperationException("Missing background resource: "+path)).ToList();
                loaded=false;UmaAssetManager.PreLoadAndRun(backgroundEntries,()=>loaded=true);
                while(!loaded)yield return null;
                var actors=characters.Take(sub.charaObjs.Count).ToList();
                Builder.LoadLiveUma(actors);
                yield return LiveCharacterFlareCollisionRuntime.WaitForReady(sub.CharaContainerScript);
                sub.UI=UI;sub.sliderControl=sliderControl;
                if(ScreenCaptures==null)ScreenCaptures=new LiveScreenCaptureController();
                sub.ScreenCaptures=ScreenCaptures;
                sub.InitializeTimeline(actors,mode);sub._soloMode=true;
                var size=LiveMonitorResolution.CurrentVirtualResolution();
                target=new RenderTexture(size.x,size.y,24,RenderTextureFormat.ARGB32){name="PrePlaySharedColor"};target.Create();
                foreach(var camera in sub._cameraObjects)if(camera!=null)camera.targetTexture=target;
                sub.BeginOfflineRender(target);sub._isLiveSetup=true;Time.captureDeltaTime=1f/60;
                int last=sub._liveTimelineControl.data.worksheetList.Where(sheet=>sheet?.ScreenCaptureDataList?.thisList!=null)
                    .SelectMany(sheet=>sheet.ScreenCaptureDataList.thisList).Select(key=>key.frame).DefaultIfEmpty(0).Max();
                RenderPipelineManager.endCameraRendering+=afterCamera;
                PrePlayManualFrames=0;
                for(int frame=0;frame<=last+3;frame++)
                {
                    rendered=false;
                    sub.SetOfflineRenderFrame(frame,60);
                    yield return new WaitForEndOfFrame();
                    // Hidden batch windows may advance Update/LateUpdate without
                    // rendering any camera. Submit here, after skinning, so the
                    // event600 capture executes before next-frame registration.
                    if(!rendered){sub.RenderOfflineCameraStacks(target);PrePlayManualFrames++;}
                }
                var expected=sub._liveTimelineControl.data.worksheetList.Where(sheet=>sheet?.ScreenCaptureDataList?.thisList!=null)
                    .SelectMany(sheet=>sheet.ScreenCaptureDataList.thisList).Select(key=>key.CaptureId).Distinct();
                foreach(int captureId in expected)
                    if(!ScreenCaptures.Images.ContainsKey(captureId))
                        throw new InvalidOperationException($"Pre-play {id} did not produce capture {captureId}");
                Debug.Log($"[LivePrePlay] {live.MusicId} <- {id}: {ScreenCaptures.Images.Count} images");
                foreach(var background in backgroundEntries)UmaAssetManager.UnloadAssetBundle(background,false);
            }
            finally
            {
                RenderPipelineManager.endCameraRendering-=afterCamera;
                Time.captureDeltaTime=previousCaptureDelta;
                if(sub!=null)
                {
                    sub._isLiveSetup=false;sub.ScreenCaptures=null;
                    sub.gameObject.SetActive(false);Destroy(sub.gameObject);
                }
                if(target!=null){target.Release();Destroy(target);}
                _instance=this;gameObject.SetActive(active);
                Gallop.RenderPipeline.PostImageEffectFeature.ResetRuntimeParameter();
                foreach(var resource in roots)UmaAssetManager.UnloadAssetBundle(resource,false);
            }
            // Finish destruction/Playable cleanup before initializing main tracks.
            yield return null;
        }
    }
}
