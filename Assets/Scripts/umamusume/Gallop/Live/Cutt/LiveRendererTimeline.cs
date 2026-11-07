using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    [Serializable]
    public class LiveTimelineKeyRendererData : LiveTimelineKeyWithInterpolate
    {
        public bool renderEnable=true;
        public override LiveTimelineKeyDataType dataType=>LiveTimelineKeyDataType.Renderer;
    }
    [Serializable] public class LiveTimelineKeyRendererDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyRendererData> { }
    [Serializable]
    public class LiveTimelineRendererData : ILiveTimelineGroupDataWithName
    {
        public LiveTimelineKeyRendererDataList keys=new LiveTimelineKeyRendererDataList();
        public LiveTimelineRendererData():base("Renderer Object") { }
        public override ILiveTimelineKeyDataList GetKeyList()=>keys;
    }
    public struct RendererUpdateInfo
    {
        public int TimelineNameHash;
        public bool renderEnable;
    }
    public delegate void RendererUpdateInfoDelegate(ref RendererUpdateInfo updateInfo);
    public partial class LiveTimelineWorkSheet
    {
        [SerializeField] public List<LiveTimelineRendererData> rendererList;
    }
    public partial class LiveTimelineControl
    {
        public event RendererUpdateInfoDelegate OnUpdateRenderer;
        // Original4ee2950: inherited interpolation fields do not blend this bool.
        public void AlterUpdate_RendererControl(LiveTimelineWorkSheet sheet,int frame)
        {
            if(OnUpdateRenderer==null)return;
            if(sheet==null||sheet.rendererList==null)throw new NullReferenceException("Original Renderer group list");
            foreach(var group in sheet.rendererList)
            {
                if(group==null||group.keys==null)throw new NullReferenceException("Original Renderer group keys");
                var keys=group.keys;
                if(keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable)||!keys.EnablePlayModeTimeline(_playMode))continue;
                FindTimelineKey(out var current,out var next,keys,frame);
                if(current==null)continue;
                if(!(current is LiveTimelineKeyRendererData key))throw new InvalidCastException("Original Renderer current key");
                var info=new RendererUpdateInfo{TimelineNameHash=group.nameHash,renderEnable=key.renderEnable};
                OnUpdateRenderer(ref info);
            }
        }
    }
}
