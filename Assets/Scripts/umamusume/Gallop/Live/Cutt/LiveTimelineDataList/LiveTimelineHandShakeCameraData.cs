using System;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    [Serializable]
    public class LiveTimelineKeyHandShakeCameraData : LiveTimelineKeyWithInterpolate
    {
        public float power, frequency, Rate;
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.HandShakeCamera;
    }

    [Serializable]
    public class LiveTimelineKeyHandShakeCameraDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyHandShakeCameraData>
    {
        [SerializeField] private bool _useFixedShakePattern;
        public bool UseFixedShakePattern { get => _useFixedShakePattern; set => _useFixedShakePattern = value; }
    }

    public static class LiveHandShakeCameraTimeline
    {
        // AlterUpdate_HandShakeCamera 4ed2ba0 resets parameters before its gates.
        public static void Evaluate(LiveTimelineKeyHandShakeCameraDataList keys, int frame,
            TimelinePlayerMode playMode, HandShakeCamera target)
        {
            target.Power = target.Frequency = target.Rate = 0f;
            if (keys == null || keys.Count == 0 || keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) ||
                !keys.EnablePlayModeTimeline(playMode) || !LiveQualitySettings.IsCameraShake) return;
            LiveTimelineControl.FindTimelineKey(out var current, out var next, keys, frame);
            var a = current as LiveTimelineKeyHandShakeCameraData;
            var b = next as LiveTimelineKeyHandShakeCameraData;
            if (a == null) return;
            if (b != null && b.IsInterpolateKey())
            {
                float t = LiveTimelineControl.CalculateInterpolationValue(a, b, frame);
                target.Power = a.power + (b.power - a.power) * t;
                target.Frequency = a.frequency + (b.frequency - a.frequency) * t;
                target.Rate = a.Rate + (b.Rate - a.Rate) * t;
            }
            else { target.Power = a.power; target.Frequency = a.frequency; target.Rate = a.Rate; }
            target.UseFixedShakePattern = keys.UseFixedShakePattern;
        }
    }
}
