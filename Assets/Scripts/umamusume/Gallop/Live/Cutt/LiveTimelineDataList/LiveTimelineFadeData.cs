using System;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    [Serializable]
    public class LiveTimelineKeyFadeData : LiveTimelineKeyWithInterpolate
    {
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.Fade;
        public Color fadeColor;
    }
    [Serializable]
    public class LiveTimelineKeyFadeDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyFadeData> { }
}
