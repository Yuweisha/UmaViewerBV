using System;

namespace Gallop.Live
{
    // Service boundaries in original Director.AlterUpdateTimeline. Implementors
    // must supply the original subsystem; absent installers are not no-ops.
    public interface ILiveDirectorTimelineSheet
    {
        bool IsSequencialPlay {get;}
        int GetCurrentSheetType();
        bool IsSheet(int index);
        bool SetCurrentSheet(int index);
        bool IsEnablePostEffect {set;}
        void AlterUpdate(float time);
        float Timescale {get;}
    }
    public interface ILiveDirectorTimelineServices
    {
        void GetAudioManager();
        void UpdateRandomTable();
        void UpdateFade(float time);
        ILiveDirectorTimelineSheet Timeline {get;}
        bool IsTimelineAlive {get;}
        bool IsEndCurrentSheet();
        void FinishLiveState();
        void SetupLiveStart();
        void UpdateMainCamera();
        bool HasCameraLookAt {get;}
        bool IsTimelineControlled {get;}
        void ActivationUpdate();
    }
    public sealed class LiveDirectorTimelineDispatch
    {
        public LiveTimeController Clock;
        public float CurrentTime,PreviousTime;
        public int State;
        public bool IsBloom,IsDof;
        readonly ILiveDirectorTimelineServices _source;
        public LiveDirectorTimelineDispatch(ILiveDirectorTimelineServices source){_source=source??throw new ArgumentNullException(nameof(source));}
        // Preserve the parent field read after Update; callbacks may clear it.
        public static void AdvanceClock(ref LiveTimeController clock,ref float current,ref float previous,float deltaTime)
        {
            previous=current;
            clock.Update(deltaTime);
            current=clock.CurrentTime;
        }
        public void AlterUpdateTimeline(float deltaTime,out float timescale)
        {
            timescale=1f;
            _source.GetAudioManager();
            AdvanceClock(ref Clock,ref CurrentTime,ref PreviousTime,deltaTime);
            _source.UpdateRandomTable();
            _source.UpdateFade(CurrentTime);
            var timeline=_source.Timeline;
            if(timeline==null)throw new NullReferenceException();
            if(timeline.IsSequencialPlay)
            {
                int sheet=timeline.GetCurrentSheetType();
                if(_source.IsEndCurrentSheet())
                {
                    int next=unchecked(sheet+1);
                    if(next!=3&&_source.Timeline.IsSheet(next))
                    {
                        if(_source.Timeline.SetCurrentSheet(next))CurrentTime=0f;
                        State=3;
                        _source.SetupLiveStart();
                    }
                    else _source.FinishLiveState();
                }
            }
            else if(_source.IsEndCurrentSheet())_source.FinishLiveState();
            timeline=_source.Timeline;
            if(_source.IsTimelineAlive)
            {
                timeline.IsEnablePostEffect=IsBloom||IsDof;
                // Native re-reads the field following callbacks.
                _source.Timeline.AlterUpdate(CurrentTime);
                timescale=_source.Timeline.Timescale;
            }
            _source.UpdateMainCamera();
            if(_source.HasCameraLookAt&&_source.IsTimelineControlled)_source.ActivationUpdate();
        }
    }
}
