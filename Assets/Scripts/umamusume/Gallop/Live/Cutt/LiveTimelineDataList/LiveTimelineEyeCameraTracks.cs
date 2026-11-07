using System;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    [Serializable]
    public class LiveTimelineKeyEyeCameraPositionData : LiveTimelineKeyCameraPositionData
    {
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.EyeCameraPos;
        public bool IsEnabled;
        public float Power;
        public float Roll;
        public float Fov;
        public LiveCameraCullingLayer CullingMask;
        [Newtonsoft.Json.JsonConverter(typeof(UnityPptrNullConverter))]
        public Texture2D MaskTexture;
        // Original override 0x4f0b0e0 uses the eye key's +0xb8 mask,
        // then the common authored-layer -> Unity-layer conversion 0x4f052b0.
        public override int GetCullingMask() => ResolveCullingMask(CullingMask);
    }

    [Serializable]
    public class LiveTimelineKeyEyeCameraPositionDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyEyeCameraPositionData> { }

    [Serializable]
    public class LiveTimelineEyeCameraPositionData : ILiveTimelineGroupDataWithName
    {
        public LiveTimelineKeyEyeCameraPositionDataList keys;
        [SerializeField, Newtonsoft.Json.JsonProperty("_characterIndex")]
        private int _characterIndex;
        public int CharacterIndex => _characterIndex;
    }

    [Serializable]
    public class LiveTimelineKeyEyeCameraLookAtData : LiveTimelineKeyCameraLookAtData
    {
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.EyeCameraLookAt;
    }

    [Serializable]
    public class LiveTimelineKeyEyeCameraLookAtDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyEyeCameraLookAtData> { }

    [Serializable]
    public class LiveTimelineEyeCameraLookAtData : ILiveTimelineGroupDataWithName
    {
        public LiveTimelineKeyEyeCameraLookAtDataList keys;
    }
}
