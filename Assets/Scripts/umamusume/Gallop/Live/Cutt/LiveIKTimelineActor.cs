using System;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    // Loaded-model endpoints for the original FormationOffset IK producer.
    public sealed class LiveIKTimelineActor : ILiveIKTimelineActor
    {
        public readonly LiveModelIKRuntime Runtime;
        public readonly LiveMicStandRuntime Mic;
        readonly Func<ILiveTimelineCharactorLocator> locator;
        public LiveIKTimelineActor(LiveModelIKRuntime runtime, LiveMicStandRuntime mic,
            Func<ILiveTimelineCharactorLocator> locatorProvider)
        { Runtime=runtime; Mic=mic; locator=locatorProvider; }
        public int Type=>Runtime.Type;
        public bool HasLocator=>locator()!=null;
        public Transform LeftTarget=>Runtime.LeftTarget;
        public Transform RightTarget=>Runtime.RightTarget;
        public Transform MicLeft=>Mic.Left.Node;
        public Transform MicRight=>Mic.Right.Node;
        static LiveModelIKRuntime Model(ILiveIKTimelineActor actor)=>actor==null?null:((LiveIKTimelineActor)actor).Runtime;
        public Transform FindHandAttach(int selector)
        {
            string name=selector==1?"Hand_Attach_L":selector==2?"Hand_Attach_R":null;
            return name!=null&&Runtime.NodeCache.TryGetValue(name,out var node)?node:null;
        }
        public void ClearMicOffsets()=>Mic.ResetOffsets();
        public void SetVisible(bool visible)=>locator().liveCharaVisible=visible;
        public void SetType(int type,ILiveIKTimelineActor center)=>Runtime.SetType(type,Model(center));
        public void SetLeftAttach(Transform attach,Transform target)=>Runtime.SetLeftAttach(attach,target);
        public void SetRightAttach(Transform attach,Transform target)=>Runtime.SetRightAttach(attach,target);
        public void SetRelation(ILiveIKTimelineActor center,bool controlLeft,ILiveIKTimelineActor left,bool controlRight,ILiveIKTimelineActor right)
            =>Runtime.SetRelation(Model(center),controlLeft,Model(left),controlRight,Model(right));
        public void UpdateLimb(LiveTimelineKeyFormationOffsetData key)
        {
            if(Runtime.Type!=3)return;
            int[] values={key.IKSystemParam1,key.IKSystemParam2,key.IKSystemParam3,key.IKSystemParam4};
            for(int i=0;i<4;i++)
            {
                var limb=Runtime.Limb.Limbs[i];limb.IsUseAnimationBlendAlpha=values[i]>0;
                if(values[i]<=0)limb.BlendValue=0f;
            }
        }
        public void SetMicOffsets(bool left,Vector3 leftHigh,Vector3 leftLow,bool right,Vector3 rightHigh,Vector3 rightLow)
            =>Mic.SetOffsets(left,leftHigh,leftLow,right,rightHigh,rightLow);
    }
}
