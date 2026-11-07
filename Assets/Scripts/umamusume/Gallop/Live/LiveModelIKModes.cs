using UnityEngine;
using Gallop.Live.Cutt;

namespace Gallop.Live
{
    // Persistent updater state from original LiveIK.SetType and attachment
    // callbacks. Per-frame strategy execution is installed separately.
    public sealed class LiveIKModeState
    {
        public int Kind;
        public Transform MyPosition,CenterPosition,LeftPosition,RightPosition;
        public float MyScale=1f,CenterScale=1f,LeftScale=1f,RightScale=1f;
        public float MyHeightRate=1f,CenterHeightRate=1f,LeftHeightRate=1f,RightHeightRate=1f;
        public Transform LeftAttach,LeftIKTarget,RightAttach,RightIKTarget;
        public bool LeftAttachEnabled,RightAttachEnabled,ControlLeft,ControlRight;
        public LiveModelIKRuntime CenterIK;
        public bool AnimationBlend;
        public float HeightRate,SlideScale;
        public Vector3 SlidePosition,OriginalPosition;
        public bool HasOriginalPosition,HasSlidePosition;
    }

    public sealed partial class LiveModelIKRuntime
    {
        public enum IKMode { None=0,Center=1,Outside=2,Limb=3,Attach=4,Mix=5 }
        public bool Enabled=true;
        public bool Paused;
        public LiveIKModeState Updater { get; private set; }
        public LiveGallopTwoBoneIKRuntime[] ModeLimbs { get; private set; }
        public LiveIKCollisionRuntime ModeCollision;
        public Transform LeftTarget { get; private set; }
        public Transform RightTarget { get; private set; }

        static float ModeF(float value)=>LiveTimelineControl.MonitorFloat32(value);
        Transform FindModeNode(string name)=>NodeCache.TryGetValue(name,out var node)?node:null;

        void MakeHandModes(bool animationBlend)
        {
            LeftTarget=FindModeNode("Wrist_L_Target");RightTarget=FindModeNode("Wrist_R_Target");
            ModeLimbs=new LiveGallopTwoBoneIKRuntime[2];
            string[] names={"Wrist_L","Wrist_R"};
            for(int i=0;i<2;i++)
            {
                var w=ModeLimbs[i]=new LiveGallopTwoBoneIKRuntime();
                w.Initialize(FindModeNode(names[i]),FindModeNode(names[i]+"_Pole"),i==0?LeftTarget:RightTarget);
                w.IsUseAnimationBlendAlpha=animationBlend;
            }
        }

        void ReleaseMode()
        {
            if(Updater==null)return;
            switch(Updater.Kind)
            {
                case 1:
                    ModeCollision?.Release();ModeCollision=null;ModeLimbs=null;break;
                case 4:
                    ModeCollision?.Release();ModeCollision=null;ModeLimbs=null;LeftTarget=null;RightTarget=null;break;
                case 5:
                    if(ModeLimbs!=null){ModeCollision?.Release();ModeCollision=null;ModeLimbs=null;}
                    break;
            }
        }

        public void SetType(int type,LiveModelIKRuntime center=null)
        {
            if(type==Type)return;
            ReleaseMode();Updater=null;Type=type;LeftTarget=null;RightTarget=null;
            switch(type)
            {
                case 0:
                    Enabled=false;Limb.Enabled=false;InitializeModeFromCollision(type);return;
                case 1:
                    Updater=new LiveIKModeState{Kind=type,HeightRate=GetHeightRate(TotalScale)};
                    MakeHandModes(true);break;
                case 2:
                    Updater=new LiveIKModeState{Kind=type,MyPosition=Position,CenterPosition=center.Position,
                        MyScale=TotalScale,CenterScale=center.TotalScale,MyHeightRate=GetHeightRate(TotalScale),CenterHeightRate=GetHeightRate(center.TotalScale)};
                    break;
                case 3:
                    Enabled=false;InitializeModeFromCollision(type);
                    foreach(var w in Limb.Limbs){w.IsUseAnimationBlendAlpha=true;w.BlendValue=0f;}
                    Limb.Enabled=true;return;
                case 4:
                    Updater=new LiveIKModeState{Kind=type,AnimationBlend=false,HeightRate=0f};MakeHandModes(true);break;
                case 5:
                    Updater=new LiveIKModeState{Kind=type};break;
                default:return;
            }
            Enabled=true;Limb.Enabled=false;
        }

        void InitializeModeFromCollision(int type)
        {
            if(!HasIKCollision)return;
            if(type==0)
            {
                foreach(var w in Limb.Limbs)
                {
                    w.IsTargetEndEffector=true;w.Solver.RotationType=LiveTwoBoneIKRuntime.EffectorRotationType.None;
                    w.IsUseAnimationBlendAlpha=true;w.BlendValue=0f;
                }
                Limb.Enabled=true;
            }
            else if(type==3)
                foreach(var w in Limb.Limbs){w.IsTargetEndEffector=false;w.Solver.RotationType=LiveTwoBoneIKRuntime.EffectorRotationType.World;}
        }

        public void SetLeftAttach(Transform attach,Transform centerIKTarget)
        {
            if(Updater==null||Updater.Kind==1)return;
            Updater.LeftAttach=attach;
            if(Updater.Kind==4){Updater.LeftAttachEnabled=attach!=null;return;}
            Updater.LeftIKTarget=centerIKTarget;Updater.LeftAttachEnabled=attach!=null&&centerIKTarget!=null;
        }

        public void SetRightAttach(Transform attach,Transform centerIKTarget)
        {
            if(Updater==null||Updater.Kind==1)return;
            Updater.RightAttach=attach;
            if(Updater.Kind==4){Updater.RightAttachEnabled=attach!=null;return;}
            Updater.RightIKTarget=centerIKTarget;Updater.RightAttachEnabled=attach!=null&&centerIKTarget!=null;
        }

        public void SetRelation(LiveModelIKRuntime center,bool controlLeft,LiveModelIKRuntime left,bool controlRight,LiveModelIKRuntime right)
        {
            var u=Updater;if(u==null||u.Kind!=5)return;
            u.MyPosition=Position;u.MyScale=TotalScale;u.MyHeightRate=GetHeightRate(TotalScale);u.ControlLeft=controlLeft;u.ControlRight=controlRight;
            // Missing owners preserve earlier relations, as in the original.
            if(center!=null)
            {
                u.CenterIK=center;u.CenterPosition=center.Position;u.CenterScale=center.TotalScale;
                float minimum=System.BitConverter.Int32BitsToSingle(unchecked((int)0x3f53b646));
                float my=ModeF(ModeF(ModeF(u.MyScale-minimum)/.325f)*.1625f);
                float other=ModeF(ModeF(ModeF(u.CenterScale-minimum)/.325f)*.1625f);
                u.SlideScale=ModeF(ModeF(other+u.MyScale)-my);
            }
            if(left!=null){u.LeftPosition=left.Position;u.LeftScale=left.TotalScale;u.LeftHeightRate=GetHeightRate(left.TotalScale);}
            if(right!=null){u.RightPosition=right.Position;u.RightScale=right.TotalScale;u.RightHeightRate=GetHeightRate(right.TotalScale);}
            int count=(controlLeft?1:0)+(controlRight?1:0);if(count==0)return;
            ModeLimbs=new LiveGallopTwoBoneIKRuntime[count];int index=0;
            void Make(string name,Transform target)
            {
                var w=ModeLimbs[index++]=new LiveGallopTwoBoneIKRuntime();
                w.Initialize(FindModeNode(name),FindModeNode(name+"_Pole"),target);w.IsUseAnimationBlendAlpha=true;
            }
            if(controlLeft){LeftTarget=FindModeNode("Wrist_L_Target");Make("Wrist_L",LeftTarget);}
            if(controlRight){RightTarget=FindModeNode("Wrist_R_Target");Make("Wrist_R",RightTarget);}
        }

        public void GetAnimRate(ref float anim1,ref float anim2,ref float anim3)
        {
            switch(Type)
            {
                case 1:anim1=Updater.HeightRate;anim2=ModeF(1f-anim1);anim3=0;return;
                case 2:
                    if(Updater.MyHeightRate>=Updater.CenterHeightRate){anim1=1f;anim2=0f;}
                    else if(Updater.CenterHeightRate>Updater.MyHeightRate){anim1=ModeF(ModeF(1f-Updater.CenterHeightRate)+Updater.MyHeightRate);anim2=ModeF(1f-anim1);}
                    anim3=0;return;
                case 3:anim1=InitialHeightRate;anim2=ModeF(1f-anim1);anim3=0;return;
                case 5:
                    anim1=1f;anim2=0f;anim3=0f;float sum=0f;int count=0;
                    for(int i=0;i<2;i++)
                    {
                        if((i==0?Updater.LeftPosition:Updater.RightPosition)==null)continue;
                        float rate=i==0?Updater.LeftHeightRate:Updater.RightHeightRate;
                        float delta=ModeF(rate-Updater.MyHeightRate);
                        float weight=delta>0?ModeF(1f+ModeF(-delta*1.2f)):ModeF(1f+ModeF(-ModeF(1f-Updater.MyHeightRate)*.1f));
                        if(i==0)anim2=weight;else anim3=weight;
                        sum=ModeF(sum+weight);count++;
                    }
                    anim1=ModeF(1f-ModeF(sum/count));return;
                default:anim1=1f;anim2=0f;anim3=0f;return;
            }
        }
    }
}
