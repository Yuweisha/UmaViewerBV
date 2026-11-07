using System;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    [Serializable]
    public class LiveTimelineKeyBgColor2Data : LiveTimelineKeyWithInterpolate
    {
        public override LiveTimelineKeyDataType dataType
        {
            get{return LiveTimelineKeyDataType.BgColor2;}
        }
        public Color color1;
        public Color color2;
        public float power;
        public string BlinkLightName;
        public int BlinkLightNameHash;
        public int BlinkLightContainerIndex;
        public float BlinkLightBrightnessPower;
        public bool IsSyncBlinkLightToColor1;
        public bool IsSyncBlinkLightToColor2;
        public bool IsAdjustedBlinkLightColor;
        public LiveTimelineKeyLoopType loopType;
        public int loopCount;
        public int loopExecutedCount;
        public int loopIntervalFrame;
        public bool isPasteLoopUnit;
        public bool isChangeLoopInterpolate;
        public float f32;
        public LiveTimelineKeyLoopType _loopType;
        public int _loopCount;
        public int _loopExecutedCount;
        public int _loopIntervalFrame;
        public bool _isChangeLoopInterpolate;

        public virtual int RandomTableIndex()
        {
            // Native 4f068c0 sign-extends the high half of attribute (+16h).
            return unchecked((short)((uint)attribute >> 16));
        }

        public override void OnLoad(LiveTimelineControl timelineControl)
        {
            // Native 4f06850 migrates the legacy saved fields once. It does not
            // resolve or rewrite BlinkLightNameHash.
            if (f32 > 0f)
            {
                power=f32;
                f32=-1f;
            }
            if ((int)_loopType != 4)
            {
                loopType=_loopType;
                loopCount=_loopCount;
                loopExecutedCount=_loopExecutedCount;
                loopIntervalFrame=_loopIntervalFrame;
                isChangeLoopInterpolate=_isChangeLoopInterpolate;
                _loopType=(LiveTimelineKeyLoopType)4;
            }
        }
    }

    [Serializable]
    public class LiveTimelineKeyBgColor2DataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyBgColor2Data>
    {
    }

    [Serializable]
    public class LiveTimelineBgColor2Data : ILiveTimelineGroupDataWithName
    {
        private const string DEFAULT_NAME = "BgColor2";
        public LiveTimelineKeyBgColor2DataList keys = new LiveTimelineKeyBgColor2DataList();

        public LiveTimelineBgColor2Data()
        {
            if (string.IsNullOrEmpty(name))
                name = DEFAULT_NAME;
        }
    }
}
