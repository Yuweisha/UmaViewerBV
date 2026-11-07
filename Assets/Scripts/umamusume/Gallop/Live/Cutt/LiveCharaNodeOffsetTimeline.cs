using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop
{
    [Serializable] public class CourseCharacterNodeParam
    {
        [Serializable] public class NameNodeParam
        {
            public string Name;public int NameHash;
            public Vector3 Position;public Quaternion Rotation;public Vector3 Scale;
            public bool AddTransformPosition,AddTransformRotation,AddTransformScale;
        }
    }
}
namespace Gallop.CutIn.Cutt
{
    [Serializable] public sealed class KeyVector3
    {
        [SerializeField] public bool _isValid;
        [SerializeField] public Vector3 _value;
        public KeyVector3() { }
        public KeyVector3(Vector3 value){_value=value;}
        public void Interpolate(KeyVector3 previous,KeyVector3 next,float t)
        {
            _isValid=next._isValid;t=Mathf.Clamp01(t);
            Vector3 a=previous._isValid?previous._value:Vector3.zero,b=next._isValid?next._value:Vector3.zero;
            _value=new Vector3(Lerp(a.x,b.x,t),Lerp(a.y,b.y,t),Lerp(a.z,b.z,t));
        }
        static float F(float value)=>Gallop.Live.Cutt.LiveTimelineControl.MonitorFloat32(value);
        static float Lerp(float a,float b,float t)=>F(F(F(b-a)*t)+a);
    }
    [Serializable] public class CharacterNodeData
    {
        public KeyVector3 HeadRotateOffset=new KeyVector3(),NeckRotateOffset=new KeyVector3(),ChestRotateOffset=new KeyVector3(),SpineRotateOffset=new KeyVector3(),WaistRotateOffset=new KeyVector3();
        public CourseCharacterNodeParam.NameNodeParam[] NameNodeParamArray=Array.Empty<CourseCharacterNodeParam.NameNodeParam>();
    }
    [Serializable] public class TimelineKeyCharacterNodeData : CharacterNodeData
    {
        public bool IsUseParamForPersonal;
        public CharacterNodeData[] PersonalNodeDataArray=Array.Empty<CharacterNodeData>();
        public void SetReferences(CharacterNodeData data,bool personal,CharacterNodeData[] personalities)
        {
            HeadRotateOffset=data.HeadRotateOffset;NeckRotateOffset=data.NeckRotateOffset;ChestRotateOffset=data.ChestRotateOffset;SpineRotateOffset=data.SpineRotateOffset;WaistRotateOffset=data.WaistRotateOffset;NameNodeParamArray=data.NameNodeParamArray;
            IsUseParamForPersonal=personal;PersonalNodeDataArray=personalities;
        }
        static void Blend(CharacterNodeData output,CharacterNodeData previous,CharacterNodeData next,float t)
        {
            output.HeadRotateOffset.Interpolate(previous.HeadRotateOffset,next.HeadRotateOffset,t);output.NeckRotateOffset.Interpolate(previous.NeckRotateOffset,next.NeckRotateOffset,t);output.ChestRotateOffset.Interpolate(previous.ChestRotateOffset,next.ChestRotateOffset,t);output.SpineRotateOffset.Interpolate(previous.SpineRotateOffset,next.SpineRotateOffset,t);output.WaistRotateOffset.Interpolate(previous.WaistRotateOffset,next.WaistRotateOffset,t);
        }
        public void Interpolate(TimelineKeyCharacterNodeData previous,TimelineKeyCharacterNodeData next,float t)
        {
            IsUseParamForPersonal=previous.IsUseParamForPersonal&&next.IsUseParamForPersonal;
            if(!IsUseParamForPersonal){Blend(this,previous,next,t);return;}
            int count=System.Math.Min(4,System.Math.Min(previous.PersonalNodeDataArray.Length,next.PersonalNodeDataArray.Length));
            if(PersonalNodeDataArray.Length!=4){Array.Resize(ref PersonalNodeDataArray,4);for(int i=0;i<4;i++)if(PersonalNodeDataArray[i]==null)PersonalNodeDataArray[i]=new CharacterNodeData();}
            for(int i=0;i<count;i++)Blend(PersonalNodeDataArray[i],previous.PersonalNodeDataArray[i],next.PersonalNodeDataArray[i],t);
        }
    }
}
namespace Gallop.Live.Cutt
{
    using Gallop.CutIn.Cutt;
    [Serializable] public class LiveTimelineKeyCharaNodeOffsetData : LiveTimelineKeyWithInterpolate
    {
        public CharacterNodeData _commonNodeData=new CharacterNodeData();
        public bool _isUsePersonalityParam;
        public CharacterNodeData[] _personalityNodeDataArray=Array.Empty<CharacterNodeData>();
        [NonSerialized] readonly TimelineKeyCharacterNodeData runtimeData=new TimelineKeyCharacterNodeData();
        public override LiveTimelineKeyDataType dataType=>LiveTimelineKeyDataType.CharaNodeOffset;
        public TimelineKeyCharacterNodeData GetNodeData(){runtimeData.SetReferences(_commonNodeData,_isUsePersonalityParam,_personalityNodeDataArray);return runtimeData;}
    }
    [Serializable] public class LiveTimelineKeyCharaNodeOffsetDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyCharaNodeOffsetData> { }
    [Serializable] public class LiveTimelineCharaNodeOffsetData : ILiveTimelineGroupDataWithName
    {
        public LiveTimelineKeyCharaNodeOffsetDataList keys=new LiveTimelineKeyCharaNodeOffsetDataList();
        [SerializeField] public int _characterIndex=-1;
        public LiveTimelineCharaNodeOffsetData():base("CharaNodeOffset") { }
        public override ILiveTimelineKeyDataList GetKeyList()=>keys;
    }
    public struct CharaNodeOffsetUpdateInfo {public LiveCharaPositionFlag CharacterPos;public TimelineKeyCharacterNodeData NodeData;}
    public delegate void CharaNodeOffsetUpdateInfoDelegate(ref CharaNodeOffsetUpdateInfo info);
    public partial class LiveTimelineWorkSheet {[SerializeField] public List<LiveTimelineCharaNodeOffsetData> CharaNodeOffsetDataList;}
    public partial class LiveTimelineControl
    {
        readonly TimelineKeyCharacterNodeData _blendedCharaNodeOffset=new TimelineKeyCharacterNodeData();
        public event CharaNodeOffsetUpdateInfoDelegate OnUpdateCharaNodeOffset;
        public void AlterUpdate_CharaNodeOffset(LiveTimelineWorkSheet sheet,int frame)
        {
            if(OnUpdateCharaNodeOffset==null)return;
            foreach(var group in sheet.CharaNodeOffsetDataList)
            {
                if(group._characterIndex<0)continue;var keys=group.keys;
                if(keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable)||!keys.EnablePlayModeTimeline(_playMode))continue;
                var found=keys.FindKeyCached(frame,true);if(found.key==null)return;
                var current=(LiveTimelineKeyCharaNodeOffsetData)found.key;var next=keys.At(found.index+1)as LiveTimelineKeyCharaNodeOffsetData;
                var info=new CharaNodeOffsetUpdateInfo{CharacterPos=(LiveCharaPositionFlag)(1<<group._characterIndex)};
                if(next!=null&&next.IsInterpolateKey()){_blendedCharaNodeOffset.Interpolate(current.GetNodeData(),next.GetNodeData(),CalculateInterpolationValue(current,next,frame));info.NodeData=_blendedCharaNodeOffset;}
                else info.NodeData=current.GetNodeData();
                OnUpdateCharaNodeOffset(ref info);
            }
        }
    }
}
