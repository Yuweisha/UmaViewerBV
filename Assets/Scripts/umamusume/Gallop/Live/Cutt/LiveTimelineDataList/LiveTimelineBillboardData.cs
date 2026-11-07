using System;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    [Serializable]
    public class LiveTimelineKeyBillboardData : LiveTimelineKeyWithInterpolate
    {
        public bool manualAngle;
        public Vector3 angle;
        [NonSerialized] public Quaternion manualRotation;
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.Billboard;
        public override void OnLoad(LiveTimelineControl control) { manualRotation = Quaternion.Euler(angle); }
    }
    [Serializable]
    public class LiveTimelineKeyBillboardDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyBillboardData> { }
    [Serializable]
    public class LiveTimelineBillboardData : ILiveTimelineGroupDataWithName
    {
        public LiveTimelineKeyBillboardDataList keys = new LiveTimelineKeyBillboardDataList();
        public LiveTimelineBillboardData() : base("Billboard") { }
        public override ILiveTimelineKeyDataList GetKeyList() => keys;
    }
    public struct BillboardUpdateInfo
    {
        public int nodeHash;
        public bool enableManualAngle;
        public Quaternion manualRotation;
    }
    public delegate void BillboardUpdateInfoDelegate(ref BillboardUpdateInfo info);

    public static class LiveBillboardTimeline
    {
        // Installed AlterUpdate_Billboard 0x4ec6e30. Rotation interpolation
        // depends on the destination key; manual/automatic mode uses the current.
        public static bool TryEvaluate(LiveTimelineBillboardData group, int frame,
            TimelinePlayerMode playMode, out BillboardUpdateInfo info)
        {
            info = default;
            var keys = group?.keys;
            if (keys == null || keys.Count == 0 || keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) ||
                !keys.EnablePlayModeTimeline(playMode)) return false;
            LiveTimelineControl.FindTimelineKey(out var current, out var next, keys, frame);
            var a = current as LiveTimelineKeyBillboardData;
            var b = next as LiveTimelineKeyBillboardData;
            if (a == null) return false;
            info.nodeHash = group.nameHash;
            info.enableManualAngle = a.manualAngle;
            info.manualRotation = b != null && b.IsInterpolateKey()
                ? Quaternion.Lerp(a.manualRotation, b.manualRotation,
                    LiveTimelineControl.CalculateInterpolationValue(a, b, frame)) : a.manualRotation;
            return true;
        }
    }
}
