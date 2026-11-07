using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gallop.Live
{
    public partial class Director
    {
        private OfflineRenderClock _offlineRenderClock;
        private UnityEngine.RenderTexture _previousMiniOutput;
        public bool IsOfflineRendering => _offlineRenderClock != null;
        public bool IsPresentationOutputCamera(UnityEngine.Camera camera) => IsPresentationCamera(camera) ||
            (_transparentCamera != null && camera == _transparentCamera.Camera);

        // Hidden workers do not necessarily receive Unity's automatic camera
        // rendering. Preparation captures and exported frames use the same
        // complete camera stacks, after the engine has updated skinned meshes.
        public void RenderOfflineCameraStacks(RenderTexture output)
        {
            MultiCameraFinalComposite?.PrepareManualRenderTargets();
            foreach(var camera in Camera.allCameras.Where(c=>c.isActiveAndEnabled).OrderBy(c=>c.depth))
            {
                var additional=camera.GetComponent<UniversalAdditionalCameraData>();
                if(additional!=null&&additional.renderType==CameraRenderType.Overlay)continue;
                var destination=IsPresentationOutputCamera(camera)?output:camera.targetTexture;
                if(destination==null)continue;
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,
                    new UnityEngine.Rendering.RenderPipeline.StandardRequest{destination=destination});
            }
        }

        public void BeginOfflineRender(UnityEngine.RenderTexture output = null)
        {
            // Mini cameras are initialized before the exporter creates its RT.
            // Their final blit must use the shared export surface, not their own
            // CameraTarget fallback. Keep the playback binding for cleanup.
            _previousMiniOutput = _miniCameraOwner != null ? _miniCameraOwner.MainColorBuffer : null;
            if (output != null && _miniCameraOwner != null) _miniCameraOwner.MainColorBuffer = output;
            PrepareMiniCharaOutput();
            _offlineRenderClock = new OfflineRenderClock(this);
            _liveTimeController = new LiveTimeController(_offlineRenderClock);
            _liveTimeController.Initialize(false);
            _liveTimeController.StartCalcElapsedTime();
            SongAudioTimeOverride = () => _offlineRenderClock.Time;
            IsRecordVMD = false;
            _syncTime = true;
            sliderControl.is_Touched = sliderControl.is_Outed = false;
            _liveCurrentTime = _livePrevTime = 0f;
        }

        public void SetOfflineRenderFrame(int frame, int fps)
        {
            if (_offlineRenderClock == null) throw new InvalidOperationException("Offline renderer is not attached");
            _offlineRenderClock.Time = OfflineRenderFrameTime(frame, fps);
        }

        // Match the native motion clock's frame * float32 seconds-per-frame.
        // Rounding a double division to float can fall below the authored
        // frame boundary: floor(time * 60) then repeats/skips formation keys
        // while animation sampling continues at the fractional time.
        public static float OfflineRenderFrameTime(int frame, int fps)
        {
            float secondsPerFrame = Cutt.LiveTimelineControl.MonitorFloat32(1f / fps);
            return Cutt.LiveTimelineControl.MonitorFloat32(frame * secondsPerFrame);
        }

        public void EndOfflineRender()
        {
            if (_offlineRenderClock == null) return;
            if (_miniCameraOwner != null) _miniCameraOwner.MainColorBuffer = _previousMiniOutput;
            _previousMiniOutput = null;
            PrepareMiniCharaOutput();
            _offlineRenderClock = null;
            SongAudioTimeOverride = null;
            PreparePlaybackClock();
            _liveTimeController.StartCalcElapsedTime();
        }
        public void FinishOfflineRender() { EndOfflineRender(); ExitLive(); }

        private sealed class OfflineRenderClock : ILiveTimeControllerSource
        {
            private readonly Director owner;
            public float Time;
            public OfflineRenderClock(Director director) { owner = director; }
            public bool GetDirector() => owner != null;
            public bool HasDirector() => owner != null;
            public bool GetAudioManager() => true;
            public float LiveTotalTime => owner.totalTime;
            public int SheetIndex => 1;
            public bool IsPlayingSong => true;
            public float SongPlaybackTime => Time;
            public bool IsBgmPlaying => false;
            public long BgmMilliseconds => 0;
            public void SelectSkitCue(bool opening) => throw new NotSupportedException();
            public bool PauseSong() => false;
            public bool ResumeSong() => false;
            public void NotifyDirectorPause(bool paused) { }
        }
    }
}
