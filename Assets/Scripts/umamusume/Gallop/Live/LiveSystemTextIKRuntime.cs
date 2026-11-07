using System;
using Gallop.Live.Cutt;

namespace Gallop.Live
{
    // SimpleModelLimbIK's FK buffers and wrist blend. The independent
    // EventTimelineLimbIK owns its four solvers and LateUpdate scheduling.
    public sealed class LiveSystemTextIKRuntime
    {
        public readonly bool[][] MotionFK={new[]{true,true},new[]{true,true}};
        public readonly float[] BlendWeights=new float[2];
        public bool IsBodyIK=true;
        public float PreviousMotionElapsed;
        readonly EventCharaFKAnimationGroup.Runtime group;
        public EventCharaFKAnimationGroup.Runtime Group=>group;
        readonly Action<int,float> setBlend;
        readonly LiveSystemTextIKModeState mode;
        public LiveSystemTextIKRuntime(EventCharaFKAnimationGroup.Runtime group,LiveSystemTextIKModeState mode,Action<int,float> setBlend)
        {this.group=group;this.mode=mode;this.setBlend=setBlend;}
        public void SetFKMotionState(bool add,string current,string previous,float weight=1f)
        {
            int layer=add?1:0;MotionFK[0][layer]=group!=null&&group.IsFKMotion(current);
            MotionFK[1][layer]=previous!=null&&group!=null&&group.IsFKMotion(previous);
            BlendWeights[layer]=previous!=null?weight:1f;
        }
        public float GetIKBlendWeight(bool add)
        {
            int layer=add?1:0;bool current=MotionFK[0][layer];
            return current==MotionFK[1][layer]?(current?0f:1f):current?LiveTimelineControl.MonitorFloat32(1f-BlendWeights[layer]):BlendWeights[layer];
        }
        public void UpdateMotion(string current,ref string previous,float effectiveDelta)
        {
            float weight=1f;
            if(!string.IsNullOrEmpty(previous))
            {
                PreviousMotionElapsed=LiveTimelineControl.MonitorFloat32(PreviousMotionElapsed+effectiveDelta);
                if(PreviousMotionElapsed<.23f)weight=LiveTimelineControl.MonitorFloat32(PreviousMotionElapsed/.23f);
                else previous=null;
            }
            SetFKMotionState(false,current,previous,weight);
        }
        public void UpdateWristIKBlend()
        {if(mode.Enabled){float weight=IsBodyIK?GetIKBlendWeight(false):0f;setBlend(0,weight);setBlend(1,weight);}}
    }
}
