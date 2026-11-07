using System;

namespace Gallop.Live
{
    public interface ILiveAnimationModePlayable
    {
        void Rebind();
        bool Enabled{set;}
        void SetRaceMode(Action onSetRaceMode);
        void ResetRaceMode();
    }
    public interface ILiveAnimationModeOwner
    {
        bool HasCanceler{get;}
        bool HasFace{get;}
        int ClothCategory{get;}
        void AddCanceler();
        void ResetCancelParameter();
        void ZeroPositionNode();
        void StopLegacyAnimation();
        ILiveAnimationModePlayable CreatePlayable();
        void SetCullingMode(int mode);
        void ResetCySpring();
        void ReconstructCySpring(int category);
        void StopSystemTextFacial();
        void SetLiveIKEnabled(bool enabled);
        void SetLiveLimbIKEnabled(bool enabled);
    }
    public sealed class LiveSystemTextIKModeState
    {
        public bool Enabled;
        public Action<bool> SetComponentEnabled;
        public Action<float> SetComponentBlend;
        public void Disable()
        {
            Enabled=false;
            if(SetComponentEnabled!=null){SetComponentEnabled(false);if(!Enabled)SetComponentBlend(0f);}
        }
    }
    // Original LiveModelController ChangeRace/ChangeLegacy and complete
    // StopSystemText state control. Owner engine/component operations are
    // supplied separately; SystemText entry and animation loading are separate.
    public sealed class LiveAnimationModeRuntime
    {
        readonly ILiveAnimationModeOwner owner;
        public int CurrentMode;
        public object CurrentMotionSet;
        public ILiveAnimationModePlayable Player;
        public int PreviousSystemTextId;
        public bool UseOverrideFacialMotion;
        public float PreviousSystemTextAnimationMotionSec;
        public string PreviousSystemTextBodyMotionName;
        public LiveSystemTextIKModeState SystemTextIK;
        public bool LiveIKRestore,LiveLimbIKRestore;
        public LiveAnimationModeRuntime(ILiveAnimationModeOwner value){owner=value;}
        // Original ChangeSystemTextAnimation's Begin callback. The subsequent
        // body-motion setup owns mode1; this callback only prepares its peers.
        public void BeginSystemTextAnimation(bool liveIKEnabled,bool limbIKEnabled,Action resetIKTargets)
        {
            owner.SetCullingMode(0);
            if(owner.HasCanceler)owner.ResetCancelParameter();
            if(owner.ClothCategory==2)return;
            owner.ReconstructCySpring(2);
            if(liveIKEnabled)
            {
                LiveIKRestore=liveIKEnabled;LiveLimbIKRestore=limbIKEnabled;
                resetIKTargets();owner.SetLiveIKEnabled(false);owner.SetLiveLimbIKEnabled(false);
            }
        }
        public void StopSystemTextAnimation()
        {
            PreviousSystemTextId=0;UseOverrideFacialMotion=false;PreviousSystemTextAnimationMotionSec=0f;
            if(owner.HasFace)owner.StopSystemTextFacial();
            PreviousSystemTextBodyMotionName=null;SystemTextIK?.Disable();
            owner.SetLiveIKEnabled(LiveIKRestore);owner.SetLiveLimbIKEnabled(LiveLimbIKRestore);
        }
        public void ChangeRaceAnimation()
        {
            if(!owner.HasCanceler)owner.AddCanceler();
            owner.ZeroPositionNode();
            if(CurrentMode==0)owner.StopLegacyAnimation();else if(CurrentMode==1)CurrentMotionSet=null;
            if(Player==null)Player=owner.CreatePlayable();else Player.Rebind();
            Player.Enabled=true;Player.SetRaceMode(()=>owner.SetCullingMode(0));CurrentMode=2;
            if(owner.ClothCategory!=1){owner.ResetCySpring();owner.ReconstructCySpring(1);}
            StopSystemTextAnimation();
        }
        public void ChangeLegacyAnimation()
        {
            if(owner.HasCanceler)owner.ResetCancelParameter();
            if(owner.HasFace)owner.StopSystemTextFacial();
            if(owner.ClothCategory!=0)owner.ReconstructCySpring(0);
            CurrentMotionSet=null;
            if(CurrentMode!=0)
            {
                Player.Rebind();Player.ResetRaceMode();owner.SetCullingMode(1);Player.Enabled=false;
            }
            CurrentMode=0;StopSystemTextAnimation();
        }
    }
}
