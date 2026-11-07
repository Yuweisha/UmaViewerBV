using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    [Serializable] public class LiveTimelineKeyParentConstraintData : LiveTimelineKeyWithInterpolate
    {
        public enum SourceType { CharacterBone=0,Props=1 }
        [Serializable] public class SourceData
        {
            [SerializeField] public SourceType _sourceType;
            [SerializeField] public int _targetIndex;
            [SerializeField] public string _nodeName=string.Empty;
            [SerializeField] public float _weight=1f;
            [SerializeField] public int _propId;
            public void CopyFrom(SourceData other){_sourceType=other._sourceType;_targetIndex=other._targetIndex;_nodeName=other._nodeName;_weight=other._weight;_propId=other._propId;}
        }
        [SerializeField] public bool _enable;
        public float _weight;
        [SerializeField] public SourceData[] _sourceDataArray=Array.Empty<SourceData>();
        [SerializeField] public bool _influenceOfSourceScale;
        public override LiveTimelineKeyDataType dataType=>LiveTimelineKeyDataType.ParentConstraint;
    }
    [Serializable] public class LiveTimelineKeyParentConstraintDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyParentConstraintData> { }
    [Serializable] public class LiveTimelineParentConstraintData : ILiveTimelineGroupData
    {
        public LiveTimelineKeyParentConstraintDataList keys=new LiveTimelineKeyParentConstraintDataList();
        [SerializeField] public LiveDefine.ConstraintObjectType _objectType;
        [SerializeField] public int _charaIndex,_dressIndex,_propsIndex;
        [SerializeField] public string _stageObjectName=string.Empty;
        [SerializeField] public int _stageObjectNameHash;
        public override ILiveTimelineKeyDataList GetKeyList()=>keys;
        public void SetUpStageObjectNameHash()=>_stageObjectNameHash=FNVHash.Generate(_stageObjectName);
    }
    public struct ParentConstraintUpdateInfo
    {
        public LiveDefine.ConstraintObjectType ObjectType;
        public int CharaIndex,DressIndex,PropsIndex,StageObjectNameHash;
        public bool Enable;
        public float Weight;
        public bool InfluenceOfSourceScale;
        public LiveTimelineKeyParentConstraintData.SourceData[] SourceDataArray;
        public int SourceDataArrayEnableLength;
    }
    public delegate void ParentConstraintUpdateInfoDelegate(in ParentConstraintUpdateInfo info);
    public partial class LiveTimelineWorkSheet {[SerializeField] public List<LiveTimelineParentConstraintData> ParentConstraintDataList;}
    public partial class LiveTimelineControl
    {
        ParentConstraintUpdateInfo _parentConstraintInfo=new ParentConstraintUpdateInfo{SourceDataArray=Array.Empty<LiveTimelineKeyParentConstraintData.SourceData>()};
        public event ParentConstraintUpdateInfoDelegate OnUpdateParentConstraint;
        static float ParentConstraintLerp(float a,float b,float t)=>MonitorFloat32(MonitorFloat32(MonitorFloat32(b-a)*t)+a);
        // Original4edee60. Selection is integer-frame based; interpolation uses
        // the independently supplied float-seconds clock, including subframes.
        // Viewer dispatch preserves the native position before Particle;
        // complete Mini model ownership remains a separate consumer gap.
        public void AlterUpdate_ParentConstraint(LiveTimelineWorkSheet sheet,int frame,float currentTime)
        {
            if(OnUpdateParentConstraint==null)return;
            foreach(var group in sheet.ParentConstraintDataList)
            {
                var keys=group.keys;if(keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable)||!keys.EnablePlayModeTimeline(_playMode))continue;
                var found=keys.FindKeyCached(frame,true);if(found.key==null)continue;
                var previous=(LiveTimelineKeyParentConstraintData)found.key;var next=keys.At(found.index+1)as LiveTimelineKeyParentConstraintData;
                _parentConstraintInfo.ObjectType=group._objectType;_parentConstraintInfo.CharaIndex=group._charaIndex;_parentConstraintInfo.DressIndex=group._dressIndex;_parentConstraintInfo.PropsIndex=group._propsIndex;_parentConstraintInfo.StageObjectNameHash=group._stageObjectNameHash;
                _parentConstraintInfo.Enable=previous._enable;_parentConstraintInfo.Weight=previous._weight;_parentConstraintInfo.InfluenceOfSourceScale=previous._influenceOfSourceScale;
                int count=previous._sourceDataArray.Length;
                if(count>_parentConstraintInfo.SourceDataArray.Length){_parentConstraintInfo.SourceDataArray=new LiveTimelineKeyParentConstraintData.SourceData[count];for(int i=0;i<count;i++)_parentConstraintInfo.SourceDataArray[i]=new LiveTimelineKeyParentConstraintData.SourceData();}
                for(int i=0;i<count;i++)_parentConstraintInfo.SourceDataArray[i].CopyFrom(previous._sourceDataArray[i]);_parentConstraintInfo.SourceDataArrayEnableLength=count;
                if(next!=null&&next.IsInterpolateKey())
                {
                    float t=CalculateInterpolationValueAtTime(previous,next,currentTime);_parentConstraintInfo.Weight=ParentConstraintLerp(previous._weight,next._weight,t);
                    for(int i=0;i<count;i++)foreach(var candidate in next._sourceDataArray)
                        if(previous._sourceDataArray[i]._targetIndex==candidate._targetIndex&&previous._sourceDataArray[i]._nodeName==candidate._nodeName)
                        {_parentConstraintInfo.SourceDataArray[i]._weight=ParentConstraintLerp(previous._sourceDataArray[i]._weight,candidate._weight,t);break;}
                }
                OnUpdateParentConstraint(in _parentConstraintInfo);
            }
        }
    }
}
