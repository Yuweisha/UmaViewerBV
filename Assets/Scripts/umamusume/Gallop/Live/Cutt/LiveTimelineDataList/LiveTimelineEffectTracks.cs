using System;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;

namespace Gallop.Model.Component
{
    public static class RayHitController
    {
        public enum RayTargetNode
        {
            Position = 0, Head = 1, Neck = 2, Chest = 3, Spine = 4, Waist = 5, Hip = 6, Max = 7
        }
    }
}

namespace Gallop.Live.Cutt
{
    [Serializable]
    public class LiveTimelineKeyRayHitEffectData : LiveTimelineKeyWithInterpolate
    {
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.RayHitEffect;
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
        public float RayHitEffectDirectionBlendRate;
        public bool ResetRayHitEffectColor;
        public Color RayHitEffectColor;
        public float RayHitEffectColorPower;
        public bool IsRayHitEffectColorPowerRGBOnly;
        public bool IsRayHitEffectLayerTransparentFX;
    }

    [Serializable]
    public class LiveTimelineKeyRayHitEffectDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyRayHitEffectData> { }

    [Serializable]
    public class LiveTimelineRayHitEffectData : ILiveTimelineGroupDataWithName
    {
        public LiveTimelineKeyRayHitEffectDataList keys;
        [SerializeField, JsonProperty("_folder")] private string _folder;
        [SerializeField, JsonProperty("_effectDuration")] private float _effectDuration;
        public string Folder => _folder;
        public float EffectDuration => _effectDuration;
    }

    [Serializable]
    public class LiveTimelineKeyContactEffectData : LiveTimelineKeyWithInterpolate
    {
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.ContactEffect;
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

    [Serializable]
    public class LiveTimelineKeyContactEffectDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyContactEffectData> { }

    [Serializable]
    public class LiveTimelineContactEffectData : ILiveTimelineGroupDataWithName
    {
        public LiveTimelineKeyContactEffectDataList keys;
        [SerializeField, JsonProperty("_folder")] private string _folder;
        [SerializeField, JsonProperty("_charaFootGroundData")] private Gallop.CharaFootGroundData _charaFootGroundData;
        public string Folder => _folder;
        public Gallop.CharaFootGroundData CharaFootGroundData => _charaFootGroundData;
    }
}
