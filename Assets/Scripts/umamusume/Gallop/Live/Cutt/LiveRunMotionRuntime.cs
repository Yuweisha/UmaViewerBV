using System;

namespace Gallop.Live.Cutt
{
    public interface ILiveRunPlayableEngine
    {
        bool SameClip(object first,object second);
        bool HasClip(object clip);
        bool IsValid(object playable);
        object Create(object clip);
        void Destroy(object playable);
        void Disconnect(int mixer,int port);
        void Connect(object playable,int mixer,int port);
        void SetWeight(int mixer,int port,float value);
        float Length(object clip);
        void SetTime(object playable,double value);
    }

    // Original LiveModelController.UpdateRunModeAnimation, LiveCharacterAnimation
    // blend/clip selection, and LivePlayableAnimator race-mixer control bodies.
    // Cast/resource installation, mode transitions, cancellation and evaluation
    // phase belong to the owner adapter; no substitute source clips are created.
    public sealed class LiveRunMotionRuntime
    {
        public sealed class ClipState
        {
            public object Clip,Playable;
            public bool Changed;
        }
        readonly ILiveRunPlayableEngine engine;
        static readonly float[] Thresholds={-1f,0f,1.5f};
        public readonly ClipState[] States={new ClipState(),new ClipState(),new ClipState(),new ClipState()};
        public object[] RaceClips;
        public int FirstMotionIndex{get;private set;}
        public int SecondMotionIndex{get;private set;}
        public float BlendWeight{get;private set;}
        public float CurrentTime{get;private set;}
        public float MixerWeight{get;private set;}
        public LiveRunMotionRuntime(ILiveRunPlayableEngine engine){this.engine=engine;}
        static float F(float value)=>LiveTimelineControl.MonitorFloat32(value);
        public void SetClip(int index,object clip)
        {
            if(index==-1)return;
            var state=States[index];if(engine.SameClip(state.Clip,clip))return;
            if(engine.IsValid(state.Playable))engine.Destroy(state.Playable);
            state.Clip=clip;
            if(engine.HasClip(clip))state.Playable=engine.Create(clip);
            state.Changed=true;
        }
        public void ResetRaceClips()
        {
            foreach(var state in States)
            {
                if(engine.IsValid(state.Playable)){engine.Destroy(state.Playable);state.Playable=null;}
                state.Clip=null;
                // 0x33a8070 retains Changed and already invalid stored handles.
            }
        }
        public static void GetBlendMotionIndex(float rate,out int first,out int second,out float weight)
        {
            first=0;second=0;weight=0f;
            // Original race STATIC state1 has exactly these three thresholds;
            // its ordered running prefix array is run05/run02/run03.
            var thresholds=Thresholds;
            if(thresholds[0]>rate)rate=thresholds[0];else if(rate>thresholds[2])rate=thresholds[2];
            for(int i=0;i<2;i++)
            {
                first=i;
                if(thresholds[i+1]>=rate){second=i+1;weight=F(F(rate-thresholds[i])/F(thresholds[i+1]-thresholds[i]));return;}
            }
        }
        void SetRunMotionClip(int first,int second,float rawRate)
        {
            if(first>RaceClips.Length-1||second>RaceClips.Length-1)return;
            if(!engine.HasClip(RaceClips[first])||!engine.HasClip(RaceClips[second]))return;
            if(!engine.SameClip(States[0].Clip,RaceClips[first]))SetClip(0,RaceClips[first]);
            if(rawRate>0f&&second>=0&&second<RaceClips.Length&&first!=second)
            {
                if(!engine.SameClip(States[1].Clip,RaceClips[second]))SetClip(1,RaceClips[second]);
            }
            else if(engine.HasClip(States[1].Clip))SetClip(1,null);
        }
        void UpdateMixer(int mixer,int first,int second,float weight)
        {
            var a=States[first];var b=second!=-1?States[second]:null;bool hasSecond=b!=null&&engine.HasClip(b.Clip);
            if(!engine.HasClip(a.Clip)||!engine.IsValid(a.Playable))return;
            if(a.Changed||(b!=null&&b.Changed))
            {
                engine.Disconnect(mixer,0);engine.Disconnect(mixer,1);engine.Connect(a.Playable,mixer,0);
                if(hasSecond){engine.Connect(b.Playable,mixer,1);b.Changed=false;}
                a.Changed=false;
            }
            engine.SetWeight(mixer,0,F(1f-weight));engine.SetWeight(mixer,1,weight);
            engine.SetTime(a.Playable,F(CurrentTime%engine.Length(a.Clip)));
            if(hasSecond)engine.SetTime(b.Playable,F(CurrentTime%engine.Length(b.Clip)));
        }
        public void Update(int runningType,float keyMotionSpeed,float rawBlendRate,float motionSec)
        {
            GetBlendMotionIndex(rawBlendRate,out int first,out int second,out float weight);
            FirstMotionIndex=first;SecondMotionIndex=second;BlendWeight=weight;
            SetRunMotionClip(first,second,rawBlendRate);
            CurrentTime=F(F(keyMotionSpeed*motionSec)*(runningType==3?.8f:1f));
            MixerWeight=engine.HasClip(States[1].Clip)?weight:0f;
            UpdateMixer(0,0,1,MixerWeight);UpdateMixer(2,2,3,MixerWeight);
        }
    }
}
