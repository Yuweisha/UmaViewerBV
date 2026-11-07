using System;
using System.Collections.Generic;
using Gallop.Live.Cutt;
using UnityEngine;

namespace Gallop.Live
{
    public partial class Director
    {
        readonly Dictionary<UmaContainerCharacter,LiveCharaNodeOffsetModel> _nodeOffsetModels=new Dictionary<UmaContainerCharacter,LiveCharaNodeOffsetModel>();
        public event Action<int,string,Quaternion> OnCharaNodeOffsetRotationWrite;
        public event Action<int,string,Quaternion> OnCharaNodeOffsetRotationRead;
        int _nodeOffsetPhysicalSlot;
        internal void BindCharaNodeOffset()=>_liveTimelineControl.OnUpdateCharaNodeOffset+=OnUpdateCharaNodeOffset;
        internal void UnbindCharaNodeOffset()
        {
            if(_liveTimelineControl!=null)_liveTimelineControl.OnUpdateCharaNodeOffset-=OnUpdateCharaNodeOffset;
            _nodeOffsetModels.Clear();
        }
        void OnUpdateCharaNodeOffset(ref CharaNodeOffsetUpdateInfo info)
        {
            // Original Director4e2db60 walks physical slots and selects each
            // actor's current model. A compressed cast list loses empty slots.
            for(int slot=0;slot<20;slot++)
            {
                if(((int)info.CharacterPos&(1<<slot))==0)continue;
                var actor=ResolvePhysicalFacialCharacter(slot);if(actor==null)continue;
                if(info.NodeData.IsUseParamForPersonal&&info.NodeData.PersonalNodeDataArray.Length!=0)
                    throw new NotSupportedException("CharaNodeOffset personal selection requires original CharacterBuildInfo.RaceRunPersonality; the actual Live corpus uses common data");
                if(!_nodeOffsetModels.TryGetValue(actor,out var model))
                {
                    model=new LiveCharaNodeOffsetModel(actor.LiveLocator.Bones);_nodeOffsetModels.Add(actor,model);
                    model.RotationRead+=(name,q)=>OnCharaNodeOffsetRotationRead?.Invoke(_nodeOffsetPhysicalSlot,name,q);
                    model.RotationWrite+=(name,q)=>OnCharaNodeOffsetRotationWrite?.Invoke(_nodeOffsetPhysicalSlot,name,q);
                }
                _nodeOffsetPhysicalSlot=slot;
                model.Apply(info.NodeData,0); // Common data never reads personality.
            }
        }
    }
}
