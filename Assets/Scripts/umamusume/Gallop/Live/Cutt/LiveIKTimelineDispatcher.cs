using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    public interface ILiveIKTimelineActor
    {
        int Type {get;}
        bool HasLocator {get;}
        Transform LeftTarget {get;}
        Transform RightTarget {get;}
        Transform MicLeft {get;}
        Transform MicRight {get;}
        Transform FindHandAttach(int selector);
        void ClearMicOffsets();
        void SetVisible(bool visible);
        void SetType(int type,ILiveIKTimelineActor center);
        void SetLeftAttach(Transform attach,Transform target);
        void SetRightAttach(Transform attach,Transform target);
        void SetRelation(ILiveIKTimelineActor center,bool controlLeft,ILiveIKTimelineActor left,bool controlRight,ILiveIKTimelineActor right);
        void UpdateLimb(LiveTimelineKeyFormationOffsetData key);
        void SetMicOffsets(bool left,Vector3 leftHigh,Vector3 leftLow,bool right,Vector3 rightHigh,Vector3 rightLow);
    }

    // Original AlterUpdate_IKController 0x4ed3620. Model operations remain
    // separate so the producer's list gates/order can be compared independently.
    public sealed class LiveIKTimelineDispatcher
    {
        sealed class MixData
        {
            public int Priority;public ILiveIKTimelineActor Self,Left,Right;
            public Transform LeftAttach,RightAttach;public bool ControlLeft,ControlRight;public MixData Center;
        }
        readonly List<MixData> mixed=new List<MixData>();
        static float F(float x)=>LiveTimelineControl.MonitorFloat32(x);
        static Vector3 Lerp(Vector3 a,Vector3 b,float t)=>new Vector3(F(F(F(b.x-a.x)*t)+a.x),F(F(F(b.y-a.y)*t)+a.y),F(F(F(b.z-a.z)*t)+a.z));

        public void Update(IList<LiveTimelineKeyFormationOffsetDataList> groups,int frame,int playMode,Func<int,ILiveIKTimelineActor> actor)
        {
            mixed.Clear();
            for(int index=0;index<groups.Count;index++)
            {
                var self=actor(index);if(self==null)continue;
                self.ClearMicOffsets();var keys=groups[index];
                if(keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable)||!keys.EnablePlayModeTimeline((TimelinePlayerMode)playMode)||keys.Count<=0)continue;
                var selected=keys.FindKeyCached(frame,true);var current=selected.key as LiveTimelineKeyFormationOffsetData;
                var next=current!=null?keys.At(selected.index+1)as LiveTimelineKeyFormationOffsetData:null;
                if(current==null)continue;
                if(self.HasLocator)self.SetVisible(current.visible);
                if(!current.visible)continue;
                switch((int)current.IKSystem)
                {
                    case 0:self.SetType(0,null);break;
                    case 1:
                        if(self.Type!=0)break;
                        if(current.IKSystemParam1!=current.IKSystemParam2)
                        {
                            var left=current.IKSystemParam1!=index?actor(current.IKSystemParam1):null;
                            var right=current.IKSystemParam2!=index?actor(current.IKSystemParam2):null;
                            var attachL=left?.FindHandAttach(current.IKSystemParam3);var attachR=right?.FindHandAttach(current.IKSystemParam4);
                            if(attachL==null&&attachR==null)break;
                            self.SetType(1,null);
                            if(attachL!=null){left.SetType(2,self);left.SetLeftAttach(attachL,self.LeftTarget);}
                            if(attachR!=null){right.SetType(2,self);right.SetRightAttach(attachR,self.RightTarget);}
                        }
                        else
                        {
                            var outside=current.IKSystemParam1!=index?actor(current.IKSystemParam1):null;
                            var attachL=outside?.FindHandAttach(current.IKSystemParam3);var attachR=outside?.FindHandAttach(current.IKSystemParam4);
                            if(attachL==null&&attachR==null)break;
                            self.SetType(1,null);outside.SetType(2,self);outside.SetLeftAttach(attachL,self.LeftTarget);outside.SetRightAttach(attachR,self.RightTarget);
                        }
                        break;
                    case 2:break;
                    case 3:
                        if(self.Type!=0)break;
                        self.SetType(3,null);self.UpdateLimb(current);break;
                    case 4:
                        if(self.Type==0)
                        {
                            self.SetType(4,null);
                            if(self.Type==4)
                            {
                                self.SetLeftAttach(current.IKSystemParam1>0?self.MicLeft:null,null);
                                self.SetRightAttach(current.IKSystemParam2>0?self.MicRight:null,null);
                            }
                        }
                        bool enabledL=current.IKSystemParam1>0&&current.IsEnabledIKMicStandLOffset,enabledR=current.IKSystemParam2>0&&current.IsEnabledIKMicStandROffset;
                        Vector3 highL=current.IKMicStandLOffsetHigh,lowL=current.IKMicStandLOffsetLow,highR=current.IKMicStandROffsetHigh,lowR=current.IKMicStandROffsetLow;
                        if(next!=null&&next.IsInterpolateKey()&&next.IKSystem==LiveTimelineKeyFormationOffsetData.IKSystemType.MicStand)
                        {
                            float rate=LiveTimelineControl.CalculateInterpolationValue(current,next,frame);
                            if(next.IKSystemParam1>0&&next.IsEnabledIKMicStandLOffset){highL=Lerp(highL,next.IKMicStandLOffsetHigh,rate);lowL=Lerp(lowL,next.IKMicStandLOffsetLow,rate);}
                            if(next.IKSystemParam2>0&&next.IsEnabledIKMicStandROffset){highR=Lerp(highR,next.IKMicStandROffsetHigh,rate);lowR=Lerp(lowR,next.IKMicStandROffsetLow,rate);}
                        }
                        self.SetMicOffsets(enabledL,highL,lowL,enabledR,highR,lowR);break;
                    case 5:
                        if(self.Type!=0)break;
                        var ml=current.IKSystemParam1!=index?actor(current.IKSystemParam1):null;
                        var mr=current.IKSystemParam2!=index?actor(current.IKSystemParam2):null;
                        if(ml==self)ml=null;if(mr==self)mr=null;
                        var al=ml?.FindHandAttach(current.IKSystemParam3);var ar=mr?.FindHandAttach(current.IKSystemParam4);
                        if(al==null&&ar==null)break;
                        self.SetType(5,null);mixed.Add(new MixData{Priority=current.PositionPriority,Self=self,Left=al!=null?ml:null,LeftAttach=al,Right=ar!=null?mr:null,RightAttach=ar});break;
                }
            }
            mixed.Sort((a,b)=>unchecked(a.Priority-b.Priority));
            for(int i=0;i<mixed.Count;i++)
            {
                var data=mixed[i];
                if(data.LeftAttach!=null&&(data.Center==null||data.Center.Self!=data.Left))data.ControlLeft=true;
                if(data.RightAttach!=null&&(data.Center==null||data.Center.Self!=data.Right))data.ControlRight=true;
                data.Self.SetRelation(data.Center?.Self,data.ControlLeft,data.Left,data.ControlRight,data.Right);
                if(data.ControlLeft)
                {
                    data.Left.SetLeftAttach(data.LeftAttach,data.Self.LeftTarget);
                    for(int j=i+1;j<mixed.Count;j++)if(mixed[j].Self==data.Left){mixed[j].Center=data;break;}
                }
                if(data.ControlRight)
                {
                    data.Right.SetRightAttach(data.RightAttach,data.Self.RightTarget);
                    for(int j=i+1;j<mixed.Count;j++)if(mixed[j].Self==data.Right){mixed[j].Center=data;break;}
                }
            }
        }
    }
}
