using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    public enum LiveTitleActionType { None = 0, FadeIn = 1, FadeOut = 2, Max = 3 }

    public enum LiveFlashPlayerActionType { PlayIn = 0, None = 1, PlayIn00 = 2, PlayOut = 3, PlayEnd = 4, Max = 5 }

    public enum LiveTimelineEventID { NOT_AVAILABLE = 0, SwitchCamera = 100, CameraHandShake = 101, SwitchMonitor = 102, ChangeEyeTrackTarget = 103 }

    [Serializable]
    public class LiveTimelineKeyTitleData : LiveTimelineKey
    {
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.Title;
        public LiveTitleActionType _actionType;
        public int _actionFrame;
    }

    [Serializable]
    public class LiveTimelineKeyTitleDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyTitleData> { }

    [Serializable]
    public class LiveTimelineKeyFlashPlayerData : LiveTimelineKey
    {
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.FlashPlayer;
        public LiveFlashPlayerActionType _actionType;
        public bool UseActionLabel;
        public string ActionLabel;
        public string CueSheetName;
        public string CueName;
    }

    [Serializable]
    public class LiveTimelineKeyFlashPlayerDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyFlashPlayerData> { }

    [Serializable]
    public class LiveTimelineFlashPlayerData
    {
        public LiveTimelineKeyFlashPlayerDataList keys;
    }

    [Serializable]
    public class LiveTimelineKeyVoiceData : LiveTimelineKey
    {
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.Voice;

        public enum OverrideType { None = 0, LiveVariationId = 1 }

        [Serializable]
        public class OverrideCueIdData
        {
            public OverrideType Type;
            public int TargetId;
            public int OverrideCueIdId;
        }

        public int CueId;
        public List<OverrideCueIdData> OverrideCueIdDataList;
    }

    [Serializable]
    public class LiveTimelineKeyVoiceDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyVoiceData> { }

    [Serializable]
    public class LiveTimelineKeyEventData : LiveTimelineKey
    {
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.Event;

        [Serializable]
        public class EventData
        {
            public LiveTimelineEventID eventId;
            public string serializedParamter;
        }

        public List<EventData> eventList;
    }

    [Serializable]
    public class LiveTimelineKeyEventDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyEventData> { }

    [Serializable]
    public class LiveTimelineKeyScreenCaptureData : LiveTimelineKey
    {
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.ScreenCapture;
        public int CaptureId;
        public float CaptureScale;
    }

    [Serializable]
    public class LiveTimelineKeyScreenCaptureDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyScreenCaptureData> { }
}
