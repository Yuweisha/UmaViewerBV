using UnityEngine;

namespace Gallop.Live.Cutt
{
    // Update info layout mirrored from LiveTimelineControl.ContactEffectUpdateInfo
    // and producer AlterUpdate_ContactEffect 0x4ecc820.
    public struct ContactEffectUpdateInfo
    {
        public int TimelineIndex;
        public string EffectPath;
        public Gallop.CharaFootGroundData CharaFootGroundData;
        public float CurrentTime;
        public bool ResetEffectAll;
        public bool IsEnabled;
        public LiveCharaPositionFlag CharacterFlag;
        public int EffectMaxCount;
        public Color EffectColor;
        public float EffectColorPower;
        public bool ResetEffectColor;
        public bool IsEffectColorPowerRGBOnly;
        public Vector3 EffectOffset;
        public Vector3 EffectRotate;
        public float ContactPlaneHeight;
    }

    // Producer reconstruction of AlterUpdate_ContactEffect 0x4ecc820. The caller
    // supplies t (originally CalculateInterpolationValueAtTime, 0x4eeb970); the
    // producer copies the current key, then lerps the interpolatable fields when
    // the next key is interpolating.
    public static class ContactEffectProducer
    {
        public static bool Evaluate(LiveTimelineContactEffectData group, int timelineIndex,
            int frame, float currentTime, TimelinePlayerMode playMode, float t, out ContactEffectUpdateInfo info)
        {
            info = default;
            if (group == null || group.keys == null) return false;
            var keys = group.keys;
            if (keys.Count == 0 || keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable)) return false;
            if (!keys.EnablePlayModeTimeline(playMode)) return false;
            LiveTimelineControl.FindTimelineKey(out var current, out var next, keys, frame);
            if (!(current is LiveTimelineKeyContactEffectData cur)) return false;
            var nxt = next as LiveTimelineKeyContactEffectData;
            info.TimelineIndex = timelineIndex;
            info.CharaFootGroundData = group.CharaFootGroundData;
            info.CurrentTime = currentTime;
            info.ResetEffectAll = cur.ResetEffectAll;
            info.IsEnabled = cur.IsEnabled;
            info.CharacterFlag = cur.CharacterFlag;
            info.EffectMaxCount = cur.EffectMaxCount;
            info.EffectColor = cur.EffectColor;
            info.EffectColorPower = cur.EffectColorPower;
            info.ResetEffectColor = cur.ResetEffectColor;
            info.IsEffectColorPowerRGBOnly = cur.IsEffectColorPowerRGBOnly;
            info.EffectOffset = cur.EffectOffset;
            info.EffectRotate = cur.EffectRotate;
            info.ContactPlaneHeight = cur.ContactPlaneHeight;
            if (nxt != null && nxt.IsInterpolateKey())
            {
                info.EffectColor = new Color(
                    Lerp(cur.EffectColor.r, nxt.EffectColor.r, t),
                    Lerp(cur.EffectColor.g, nxt.EffectColor.g, t),
                    Lerp(cur.EffectColor.b, nxt.EffectColor.b, t),
                    Lerp(cur.EffectColor.a, nxt.EffectColor.a, t));
                info.EffectColorPower = Lerp(cur.EffectColorPower, nxt.EffectColorPower, t);
                info.EffectOffset = new Vector3(
                    Lerp(cur.EffectOffset.x, nxt.EffectOffset.x, t),
                    Lerp(cur.EffectOffset.y, nxt.EffectOffset.y, t),
                    Lerp(cur.EffectOffset.z, nxt.EffectOffset.z, t));
                info.EffectRotate = new Vector3(
                    Lerp(cur.EffectRotate.x, nxt.EffectRotate.x, t),
                    Lerp(cur.EffectRotate.y, nxt.EffectRotate.y, t),
                    Lerp(cur.EffectRotate.z, nxt.EffectRotate.z, t));
                info.ContactPlaneHeight = Lerp(cur.ContactPlaneHeight, nxt.ContactPlaneHeight, t);
            }
            return true;
        }

        // Native uses subss/mulss/addss in sequence.
        static float Lerp(float a, float b, float t)
        {
            float delta = LiveTimelineControl.MonitorFloat32(b - a);
            float scaled = LiveTimelineControl.MonitorFloat32(delta * t);
            return LiveTimelineControl.MonitorFloat32(a + scaled);
        }
    }
}
