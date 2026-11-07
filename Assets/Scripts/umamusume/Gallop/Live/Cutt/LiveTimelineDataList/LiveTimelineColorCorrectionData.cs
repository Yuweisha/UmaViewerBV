using System;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    // Shipped TypeTree has no interpolateType/curve/easingType in these keys.
    // Unlike master5's placeholder, this is a discrete key, not WithInterpolate.
    [Serializable]
    public class LiveTimelineKeyColorCorrectionData : LiveTimelineKey
    {
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.ColorCorrection;
        public int enable;
        public float saturation = 1f;
        public int mode;
        public AnimationCurve redCurve;
        public AnimationCurve greenCurve;
        public AnimationCurve blueCurve;
        public AnimationCurve depthRedCurve;
        public AnimationCurve depthGreenCurve;
        public AnimationCurve depthBlueCurve;
        public AnimationCurve blendCurve;
        public int selective;
        public Color keyColor;
        public Color targetColor;
    }

    [Serializable]
    public class LiveTimelineKeyColorCorrectionDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyColorCorrectionData> { }

    [Serializable]
    public class LiveTimelineColorCorrectionData : ILiveTimelineGroupDataWithName
    {
        public LiveTimelineKeyColorCorrectionDataList keys;
    }

    [Serializable]
    public class LiveTimelineKeyMultiCameraColorCorrectionData : LiveTimelineKeyColorCorrectionData
    {
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.MultiCameraColorCorrection;
    }
    [Serializable]
    public class LiveTimelineKeyMultiCameraColorCorrectionDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyMultiCameraColorCorrectionData> { }
    [Serializable]
    public class LiveTimelineMultiCameraColorCorrectionData : LiveTimelineMultiCameraPostEffect
    {
        public LiveTimelineKeyMultiCameraColorCorrectionDataList keys;
        public LiveTimelineMultiCameraColorCorrectionData() : base("ColorCorrection") { }
        public override ILiveTimelineKeyDataList GetKeyList() => keys;
    }
}
