using UnityEngine;

namespace Gallop.Live.Cutt
{
    // Layout mirrored from LiveTimelineControl.RayHitEffectUpdateInfo.
    public struct RayHitEffectUpdateInfo
    {
        public int TimelineIndex;
        public string EffectPath;
        public float CurrentTime;
        public bool ResetEffectAll;
        public bool IsEnabled;
        public bool IsRaycastMaskLayerCharaModel;
        public bool IsRaycastMaskLayerBG;
        public bool IsRaycastMaskLayer3D;
        public LiveCharaPositionFlag Character;
        public float RayOriginAngleX;
        public float RayOriginAngleZ;
        public float RayOriginRadius;
        public Gallop.Model.Component.RayHitController.RayTargetNode RayTargetNode;
        public Vector3 RayTargetPositionOffset;
        public float RayMaxDistance;
        public float RaycastInterval;
        public int RayHitEffectMaxCount;
        public float RayHitEffectDuration;
        public float RayHitEffectDirectionBlendRate;
        public bool ResetRayHitEffectColor;
        public Color RayHitEffectColor;
        public float RayHitEffectColorPower;
        public bool IsRayHitEffectColorPowerRGBOnly;
        public bool IsRayHitEffectLayerTransparentFX;
    }

    // Producer reconstruction of AlterUpdate_RayHitEffect 0x4ee2340.
    public static class RayHitEffectProducer
    {
        public static bool Evaluate(LiveTimelineRayHitEffectData group, int timelineIndex,
            int frame, float currentTime, TimelinePlayerMode playMode, float t, out RayHitEffectUpdateInfo info)
        {
            info = default;
            if (group == null || group.keys == null) return false;
            var keys = group.keys;
            if (keys.Count == 0 || keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable)) return false;
            if (!keys.EnablePlayModeTimeline(playMode)) return false;
            LiveTimelineControl.FindTimelineKey(out var current, out var next, keys, frame);
            if (!(current is LiveTimelineKeyRayHitEffectData cur)) return false;
            var nxt = next as LiveTimelineKeyRayHitEffectData;
            info.TimelineIndex = timelineIndex;
            info.CurrentTime = currentTime;
            info.ResetEffectAll = cur.ResetEffectAll;
            info.IsEnabled = cur.IsEnabled;
            info.IsRaycastMaskLayerCharaModel = cur.IsRaycastMaskLayerCharaModel;
            info.IsRaycastMaskLayerBG = cur.IsRaycastMaskLayerBG;
            info.IsRaycastMaskLayer3D = cur.IsRaycastMaskLayer3D;
            info.Character = cur.Character;
            info.RayOriginAngleX = cur.RayOriginAngleX;
            info.RayOriginAngleZ = cur.RayOriginAngleZ;
            info.RayOriginRadius = cur.RayOriginRadius;
            info.RayTargetNode = cur.RayTargetNode;
            info.RayTargetPositionOffset = cur.RayTargetPositionOffset;
            info.RayMaxDistance = cur.RayMaxDistance;
            info.RaycastInterval = cur.RaycastInterval;
            info.RayHitEffectMaxCount = cur.RayHitEffectMaxCount;
            info.RayHitEffectDuration = group.EffectDuration;
            info.RayHitEffectDirectionBlendRate = cur.RayHitEffectDirectionBlendRate;
            info.ResetRayHitEffectColor = cur.ResetRayHitEffectColor;
            info.RayHitEffectColor = cur.RayHitEffectColor;
            info.RayHitEffectColorPower = cur.RayHitEffectColorPower;
            info.IsRayHitEffectColorPowerRGBOnly = cur.IsRayHitEffectColorPowerRGBOnly;
            info.IsRayHitEffectLayerTransparentFX = cur.IsRayHitEffectLayerTransparentFX;
            if (nxt != null && nxt.IsInterpolateKey())
            {
                info.RayOriginAngleX = Lerp(cur.RayOriginAngleX, nxt.RayOriginAngleX, t);
                info.RayOriginAngleZ = Lerp(cur.RayOriginAngleZ, nxt.RayOriginAngleZ, t);
                info.RayOriginRadius = Lerp(cur.RayOriginRadius, nxt.RayOriginRadius, t);
                info.RayTargetPositionOffset = new Vector3(
                    Lerp(cur.RayTargetPositionOffset.x, nxt.RayTargetPositionOffset.x, t),
                    Lerp(cur.RayTargetPositionOffset.y, nxt.RayTargetPositionOffset.y, t),
                    Lerp(cur.RayTargetPositionOffset.z, nxt.RayTargetPositionOffset.z, t));
                info.RayMaxDistance = Lerp(cur.RayMaxDistance, nxt.RayMaxDistance, t);
                info.RaycastInterval = Lerp(cur.RaycastInterval, nxt.RaycastInterval, t);
                info.RayHitEffectDirectionBlendRate = Lerp(cur.RayHitEffectDirectionBlendRate, nxt.RayHitEffectDirectionBlendRate, t);
                info.RayHitEffectColor = new Color(
                    Lerp(cur.RayHitEffectColor.r, nxt.RayHitEffectColor.r, t),
                    Lerp(cur.RayHitEffectColor.g, nxt.RayHitEffectColor.g, t),
                    Lerp(cur.RayHitEffectColor.b, nxt.RayHitEffectColor.b, t),
                    Lerp(cur.RayHitEffectColor.a, nxt.RayHitEffectColor.a, t));
                info.RayHitEffectColorPower = Lerp(cur.RayHitEffectColorPower, nxt.RayHitEffectColorPower, t);
            }
            return true;
        }

        static float Lerp(float a, float b, float t)
        {
            float delta = LiveTimelineControl.MonitorFloat32(b - a);
            float scaled = LiveTimelineControl.MonitorFloat32(delta * t);
            return LiveTimelineControl.MonitorFloat32(a + scaled);
        }
    }
}
