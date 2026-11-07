using UnityEngine;

namespace Gallop.Live
{
    public partial class Director
    {
        public bool IsPlaybackPaused => _liveTimeController != null && _liveTimeController.IsPause;

        // User-facing clock entry. The clock owns audio pause/resume and calls
        // PauseLive only after it has changed its own state.
        public void SetPlaybackPaused(bool paused)
        {
            if (_liveTimeController == null) InitializePlaybackClock();
            if (paused) _liveTimeController.PauseLive(); else _liveTimeController.ResumeLive();
        }

        // Native Director.PauseLive/PauseModel fanout. Physical slot roots may
        // contain inactive dress owners; formation may reparent the cached one.
        public void PauseLive(bool paused)
        {
            if(FlashSeSource!=null)
            {
                if(paused)FlashSeSource.Pause();else FlashSeSource.UnPause();
            }
            for (int slot=0;slot<20;slot++)
            {
                var current=ResolvePhysicalFacialCharacter(slot);
                var root=charaObjs!=null && slot<charaObjs.Count ? charaObjs[slot] : null;
                var models=root!=null ? root.GetComponentsInChildren<UmaContainerCharacter>(true) : System.Array.Empty<UmaContainerCharacter>();
                bool found=false;
                foreach(var model in models)
                {
                    model.SetLiveComponentsPaused(paused);
                    if(model==current)found=true;
                }
                if(current!=null&&!found)current.SetLiveComponentsPaused(paused);
            }
            foreach(var mini in _miniCharacters)mini.SetPaused(paused);
            _livePropsController?.Pause(paused);
            if(IsEnabledStageController && _stageController!=null)
                _stageController.Pause(paused);
            _stageParticleController?.Pause(paused);
            _effectController?.Pause(paused);
        }
    }
}
