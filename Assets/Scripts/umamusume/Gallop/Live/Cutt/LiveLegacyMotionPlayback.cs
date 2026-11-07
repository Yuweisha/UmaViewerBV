using System;

namespace Gallop.Live.Cutt
{
    public interface ILiveLegacyMotionState
    {
        void SetWrapMode(int mode);
        void SetWeight(float value);
        void SetTime(float value);
        float GetTime();
        void SetSpeed(float value);
    }
    public interface ILiveLegacyAnimation
    {
        bool Exists{get;}
        ILiveLegacyMotionState Find(string name);
        void Blend(string name,float weight,float fade);
        void Stop();
    }
    public interface ILiveLegacyMotionModel
    {
        bool Exists{get;}
        bool IsUsingPlayable{get;}
        void ChangeLegacy();
        void ResetIKTargets();
    }
    public delegate void LiveMotionRates(ref float first,ref float second,ref float third);

    // Original LegacyAnimationUpdate/PlayAnim/_PlayAnim control bodies. Clock,
    // model/resource ownership and engine operations are supplied by the caller.
    public sealed class LiveLegacyMotionPlayback
    {
        public ILiveLegacyMotionModel[] Models;
        public ILiveLegacyAnimation[] Animations;
        public float HeightRate;
        public LiveMotionRates GetIKRates;
        public Action<ILiveLegacyMotionState> SetCurrentState;
        public Action<float> SetCurrentStartTime;
        public Func<float> CalculateAnimationTime;

        public sealed class Key
        {
            public int Frame;
            public bool Loop;
            public string MotionName;
            public object Clip,Clip2,Clip3;
            public string StateName,StateName2,StateName3;
            public Swap[] Swaps;
        }
        public sealed class Swap
        {
            public int TargetMotion;
            public bool Active;
            public string StateName;
        }
        static float F(float value)=>LiveTimelineControl.MonitorFloat32(value);
        void FindStates(Key key,int dress,ref ILiveLegacyMotionState first,ref ILiveLegacyMotionState second,ref ILiveLegacyMotionState third)
        {
            var animation=Animations[dress];
            if(animation==null||!animation.Exists||key.Clip==null||string.IsNullOrEmpty(key.MotionName))return;
            var state=animation.Find(key.StateName);if(state==null)return;
            first=state;
            if(key.Clip2!=null)second=animation.Find(key.StateName2);
            if(key.Clip3!=null)third=animation.Find(key.StateName3);
        }
        void PlayState(Key key,string name,ILiveLegacyMotionState state,float rate,float time)
        {
            foreach(var animation in Animations)if(animation!=null&&animation.Exists)animation.Blend(name,rate,0f);
            state.SetWrapMode(key.Loop?2:8);
            state.SetWeight(rate);state.SetTime(time);
            if(!key.Loop&&state.GetTime()<0f)state.SetWrapMode(2);
            state.SetSpeed(0f);
        }
        void Play(Key key,int dress,int motion,float rate,float time)
        {
            string name=motion==0?key.StateName:motion==1?key.StateName2:key.StateName3;
            if(key.Swaps!=null)foreach(var swap in key.Swaps)
                if(swap.Active&&swap.TargetMotion==motion+1){name=swap.StateName;break;}
            PlayState(key,name,Animations[dress].Find(name),rate,time);
        }
        public void Update(Key key,float secondsPerFrame,bool changeAnimation,bool keyChanged)
        {
            ILiveLegacyMotionState first=null,second=null,third=null;
            for(int dress=0;dress<Models.Length;dress++)
            {
                var model=Models[dress];if(model==null||!model.Exists)continue;
                if(model.IsUsingPlayable)model.ChangeLegacy();
                if(keyChanged)model.ResetIKTargets();
                FindStates(key,dress,ref first,ref second,ref third);
                SetCurrentState?.Invoke(first);SetCurrentStartTime?.Invoke(F(key.Frame*secondsPerFrame));
                if(first==null)return;
                if(changeAnimation)Animations[dress].Stop();
                float time=CalculateAnimationTime();
                if(second==null&&third==null){Play(key,dress,0,1f,time);continue;}
                float one=HeightRate,two=F(1f-HeightRate),three=0f;
                GetIKRates?.Invoke(ref one,ref two,ref three);
                Play(key,dress,0,one,time);
                if(second!=null)Play(key,dress,1,two,time);
                if(third!=null)Play(key,dress,2,three,time);
            }
        }
    }
}
