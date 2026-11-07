using System;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    [Serializable]
    public class LiveTimelineKeyTiltShiftData : LiveTimelineKeyWithInterpolate
    {
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.TiltShift;
        public int mode;
        public int quality=1;
        public float blurArea=1;
        public float maxBlurSize=5;
        public int downsample;
        public Vector2 offset;
        public float roll;
    }

    [Serializable]
    public class LiveTimelineKeyTiltShiftDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyTiltShiftData> { }
}
