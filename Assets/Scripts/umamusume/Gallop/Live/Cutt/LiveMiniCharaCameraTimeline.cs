using System;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    [Serializable] public sealed class LiveTimelineKeyMiniCharaCameraData : LiveTimelineKeyWithInterpolate
    {
        public bool Enable;
        public float OrthographicSize=1f;
        public override LiveTimelineKeyDataType dataType=>LiveTimelineKeyDataType.MiniCharaCamera;
    }
    [Serializable] public sealed class LiveTimelineKeyMiniCharaCameraDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyMiniCharaCameraData> { }
    public partial class LiveTimelineWorkSheet {[SerializeField] public LiveTimelineKeyMiniCharaCameraDataList MiniCharaCameraKeys;}
    public partial class LiveTimelineControl
    {
#if UNITY_EDITOR
        public event Action<bool,float> OnMiniCharaCameraApplied;
#endif
        // Original4ed6b40 uses integer-frame interpolation; its seconds
        // argument does not feed this camera channel's blend calculation.
        public void AlterUpdate_MiniCharaCamera(LiveTimelineWorkSheet sheet,int frame,float currentTime)
        {
            var director=Director.instance;if(!director.IsMiniCharaCameraInitialized)return;
            director.PrepareMiniCharaOutput();
            var keys=sheet.MiniCharaCameraKeys;if(keys==null||keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable)||!keys.EnablePlayModeTimeline(_playMode))return;
            var found=keys.FindKeyCached(frame,true);if(found.key==null)return;var previous=(LiveTimelineKeyMiniCharaCameraData)found.key;var next=keys.At(found.index+1)as LiveTimelineKeyMiniCharaCameraData;
            float size=previous.OrthographicSize;
            if(next!=null&&next.IsInterpolateKey())
            {float t=CalculateInterpolationValue(previous,next,frame);size=MonitorFloat32(MonitorFloat32(MonitorFloat32(next.OrthographicSize-previous.OrthographicSize)*t)+previous.OrthographicSize);}
            var camera=director.MiniCharaCamera;camera.enabled=previous.Enable;director.SetMiniCharaOrthographicSize(size);
#if UNITY_EDITOR
            OnMiniCharaCameraApplied?.Invoke(previous.Enable,size);
#endif
        }
    }
}
