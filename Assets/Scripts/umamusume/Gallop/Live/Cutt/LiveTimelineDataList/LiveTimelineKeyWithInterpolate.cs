using System;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    [Serializable]
    public abstract class LiveTimelineKeyWithInterpolate : LiveTimelineKey
    {
        public LiveCameraInterpolateType interpolateType;
        [Newtonsoft.Json.JsonConverter(typeof(UnityAnimationCurveJsonConverter))]
        public AnimationCurve curve;
        public LiveTimelineEasing.Type easingType;

        public override bool IsInterpolateKey()
        {
            return interpolateType != LiveCameraInterpolateType.None;
        }

        protected LiveTimelineKeyWithInterpolate()
        {
        }
    }
}