using System;
using System.Collections.Generic;
using Gallop.Live.Cutt;
using UnityEngine;

namespace Gallop.Live
{
    public partial class StageController
    {
        readonly Dictionary<int,TransmittedLightMaskController> _transmittedLightMaskControllers=new Dictionary<int,TransmittedLightMaskController>();
        public TransmittedLightMaskController GetTransmittedLightMaskController(int nameHash)
        {_transmittedLightMaskControllers.TryGetValue(nameHash,out var value);return value;}
        bool ContainsTransmittedLightMaskTimeline(int nameHash,LiveTimelineControl control)
        {
            foreach(var sheet in control.data.worksheetList)
                foreach(var group in sheet.transmittedLightMaskDataList)if(group.nameHash==nameHash)return true;
            return false;
        }
        public void FindAndAddTransmittedLightMaskController(GameObject instance,int nameHash,LiveTimelineControl control)
        {
            if(!ContainsTransmittedLightMaskTimeline(nameHash,control))return;
            var controller=new TransmittedLightMaskController(instance);
            _transmittedLightMaskControllers.Add(nameHash,controller);
            control.AddTransmittedLightMaskController(nameHash,controller);
        }
        void UpdateTransmittedLightMask(ref TransmittedLightMaskUpdateInfo info,int nameHash)
        {if(_transmittedLightMaskControllers.TryGetValue(nameHash,out var value))value.UpdateInfo(ref info);}
        public void AlterUpdateTransmittedLightMaskControllers()
        {foreach(var value in _transmittedLightMaskControllers.Values)value.Update();}
        void InitializeTransmittedLightMaskControllers(LiveTimelineControl control)
        {
            if(control.data==null)return;
            foreach(var sheet in control.data.worksheetList)
                foreach(var group in sheet.transmittedLightMaskDataList)
                {
                    group.UpdateStatus();foreach(var key in group.keys.thisList)key?.OnLoad(control);
                    if(_transmittedLightMaskControllers.TryGetValue(group.nameHash,out var existing))
                    {control.AddTransmittedLightMaskController(group.nameHash,existing);continue;}
                    // StageObjectMap contains cloned scene instances, whereas
                    // worksheet groups retain original root names.
                    if(StageObjectMap.TryGetValue(group.name,out var instance))FindAndAddTransmittedLightMaskController(instance,group.nameHash,control);
                }
        }
        void ReleaseTransmittedLightMaskControllers()
        {foreach(var value in _transmittedLightMaskControllers.Values)value.Release();}
    }
}
