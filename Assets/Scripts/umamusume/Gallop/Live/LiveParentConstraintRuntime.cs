using System;
using System.Collections.Generic;
using Gallop.Live.Cutt;
using UnityEngine;

namespace Gallop.Live
{
    public interface ILiveParentConstraintBindings
    {
        bool TryGetCurrentCharacter(int physicalSlot,out int dressIndex);
        bool TryGetCharacterBone(int physicalSlot,string nodeName,out Transform node);
        bool TryGetPropsNode(int physicalSlot,int dressIndex,int propIndex,int nodeHash,out Transform node);
        void ApplyProps(int physicalSlot,int dressIndex,int propIndex,bool enable,float weight,bool influence,IReadOnlyList<ConstraintUtil.Source> sources);
        ParentConstraintComponent GetMiniConstraint(int index);
        void ApplyStage(int objectHash,bool enable,float weight,bool influence,IReadOnlyList<ConstraintUtil.Source> sources);
    }
    // Original Director4e3ec30, separated from the host's loaded-model layout.
    public sealed class LiveParentConstraintRuntime
    {
        readonly ILiveParentConstraintBindings bindings;
        readonly List<ConstraintUtil.Source> sources=new List<ConstraintUtil.Source>();
        readonly List<ConstraintUtil.Source> cache=new List<ConstraintUtil.Source>();
        public LiveParentConstraintRuntime(ILiveParentConstraintBindings bindings){this.bindings=bindings;}
        public void Apply(in ParentConstraintUpdateInfo info)
        {
            sources.Clear();
            int count=System.Math.Min(info.SourceDataArray.Length,info.SourceDataArrayEnableLength);
            for(int i=0;i<count;i++)
            {
                var data=info.SourceDataArray[i];if(string.IsNullOrEmpty(data._nodeName))continue;
                if(!bindings.TryGetCurrentCharacter(data._targetIndex,out int dress))continue;
                Transform node=null;
                if(data._sourceType==LiveTimelineKeyParentConstraintData.SourceType.CharacterBone)
                {if(!bindings.TryGetCharacterBone(data._targetIndex,data._nodeName,out node))continue;}
                else if(data._sourceType==LiveTimelineKeyParentConstraintData.SourceType.Props)
                {if(!bindings.TryGetPropsNode(data._targetIndex,dress,data._propId,FNVHash.Generate(data._nodeName),out node))continue;}
                if(node==null)continue;
                // Native cache uses the input ordinal, including skipped inputs;
                // it appends once rather than silently compacting source slots.
                if(i>=cache.Count)cache.Add(new ConstraintUtil.Source());
                var source=cache[i];source.Transform=node;source.Weight=data._weight;sources.Add(source);
            }
            switch(info.ObjectType)
            {
                case LiveDefine.ConstraintObjectType.Props:
                    bindings.ApplyProps(info.CharaIndex,info.DressIndex,info.PropsIndex,info.Enable,info.Weight,info.InfluenceOfSourceScale,sources);break;
                case LiveDefine.ConstraintObjectType.MiniChara:
                    var component=bindings.GetMiniConstraint(info.CharaIndex);
                    if(component!=null){component.SetEnable(info.Enable);component.SetWeight(info.Weight);component.SetSource(sources);component.SetInfluenceOfSourceScale(info.InfluenceOfSourceScale);}break;
                case LiveDefine.ConstraintObjectType.StageObject:
                    bindings.ApplyStage(info.StageObjectNameHash,info.Enable,info.Weight,info.InfluenceOfSourceScale,sources);break;
            }
        }
    }
}
