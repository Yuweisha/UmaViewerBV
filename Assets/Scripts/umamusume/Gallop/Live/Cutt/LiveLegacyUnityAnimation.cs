using UnityEngine;

namespace Gallop.Live.Cutt
{
    public sealed class LiveLegacyUnityAnimation : ILiveLegacyAnimation
    {
        public sealed class State : ILiveLegacyMotionState
        {
            public readonly AnimationState AnimationState;
            public State(AnimationState value){AnimationState=value;}
            public void SetWrapMode(int mode)=>AnimationState.wrapMode=(WrapMode)mode;
            public void SetWeight(float value)=>AnimationState.weight=value;
            public void SetTime(float value)=>AnimationState.time=value;
            public float GetTime()=>AnimationState.time;
            public void SetSpeed(float value)=>AnimationState.speed=value;
        }
        readonly Animation animation;
        public LiveLegacyUnityAnimation(Animation value){animation=value;}
        public bool Exists=>animation!=null;
        public ILiveLegacyMotionState Find(string name)
        {var state=animation[name];return state!=null?new State(state):null;}
        public void Blend(string name,float weight,float fade)=>animation.Blend(name,weight,fade);
        public void Stop()=>animation.Stop();
    }
    public sealed class LiveLegacyUnityModel : ILiveLegacyMotionModel
    {
        readonly UmaContainerCharacter owner;
        public LiveLegacyUnityModel(UmaContainerCharacter value){owner=value;}
        public bool Exists=>owner!=null;
        public bool IsUsingPlayable=>owner.LiveAnimationMode!=null&&owner.LiveAnimationMode.CurrentMode!=0;
        public void ChangeLegacy()=>owner.ChangeLiveLegacyAnimation();
        public void ResetIKTargets()=>owner.LiveModelIK?.Limb.ResetTargets();
    }
}
