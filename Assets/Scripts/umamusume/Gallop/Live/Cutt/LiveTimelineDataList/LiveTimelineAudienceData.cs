using System;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    [Serializable]
    public class LiveTimelineKeyAudienceData : LiveTimelineKeyWithInterpolate
    {
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.Audience;
        public Vector3 position;
        public Vector3 rotate;
        public Vector3 scale;
        public Color cyalumeColor;
        public Color cyalumeGlowColor;
        public float cyalumeGlowColorPower;
        public float cyalumeMaskRadius;
        public int animationSetting;
        public int animationRootIndex;
        public int animationBodyRegion;
        public int animationCategory;
        public int animationIndex;
        public WrapMode animationWrapMode;
        public float animationSpeed;
        public float animationOffsetTime;
        public float AnimationTime;
        public bool UseAnimationTime;
        public Color AudienceColor;
        public float AudienceColorPower;
        public Color AudienceToonBrightColor;
        public Color AudienceToonDarkColor;
        public float AudienceVertexColorToonPower;
        public float AudienceOutlineWidthPower;
        public Color AudienceOutlineColor;
        public int AudienceOutlineColorBlend;
        public Color AudienceRimColor;
        public float AudienceRimStep;
        public float AudienceRimFeather;
        public float AudienceRimSpecRate;
        public float AudienceRimShadowRate;
        public float AudienceRimHorizonOffset;
        public float AudienceRimVerticalOffset;
        public Color AudienceRimColor2;
        public float AudienceRimStep2;
        public float AudienceRimFeather2;
        public float AudienceRimSpecRate2;
        public float AudienceRimShadowRate2;
        public float AudienceRimHorizonOffset2;
        public float AudienceRimVerticalOffset2;
        public Vector3 AudienceDirectionalLightAngle;
        [NonSerialized] public Quaternion Rotation = Quaternion.identity;
        [NonSerialized] public Quaternion AudienceDirectionalLightRotation = Quaternion.identity;
        public bool IsEnabledRender => ((int)attribute & 65536) != 0;
        public bool AudienceUseDirectionalLight => ((int)attribute & 131072) != 0;
        public bool AudienceDirectionalLightCameraFollow => ((int)attribute & 262144) != 0;
        public override void OnLoad(LiveTimelineControl control)
        {
            Rotation = Quaternion.Euler(rotate);
            AudienceDirectionalLightRotation = Quaternion.Euler(AudienceDirectionalLightAngle);
        }
    }
    [Serializable] public class LiveTimelineKeyAudienceDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyAudienceData> { }
    [Serializable]
    public class LiveTimelineAudienceData : ILiveTimelineGroupDataWithName
    {
        public LiveTimelineKeyAudienceDataList keys = new LiveTimelineKeyAudienceDataList();
        [SerializeField] private int _objectIndex;
        public int ObjectIndex { get => _objectIndex; set => _objectIndex = value; }
        public override ILiveTimelineKeyDataList GetKeyList() => keys;
        public LiveTimelineAudienceData() : base("Audience Object") { }
    }
}
