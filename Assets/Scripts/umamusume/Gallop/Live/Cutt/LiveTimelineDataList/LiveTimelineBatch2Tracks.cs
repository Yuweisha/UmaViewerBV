using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Cutt.Util
{
    [Serializable]
    public class TimelineCharaPartsData
    {
        private string _rendererName;
        private int _rendererHash;
        public bool IsVisible;
        public string RendererName => _rendererName;
        public int RendererHash => _rendererHash;
    }
}

namespace Gallop.Live.Cutt
{
    [Serializable]
    public class LiveTimelineKeyCharaParts : LiveTimelineKeyWithInterpolate
    {
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.CharaParts;
        public Gallop.Cutt.Util.TimelineCharaPartsData[] CharaPartsDataArray;
    }

    [Serializable]
    public class LiveTimelineKeyCharaPartsDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyCharaParts> { }

    [Serializable]
    public class LiveTimelineCharaPartsData
    {
        public const int DATA_LIST_SIZE = 20;
        public LiveTimelineKeyCharaPartsDataList centerKeys;
        public LiveTimelineKeyCharaPartsDataList left1Keys;
        public LiveTimelineKeyCharaPartsDataList right1Keys;
        public LiveTimelineKeyCharaPartsDataList left2Keys;
        public LiveTimelineKeyCharaPartsDataList right2Keys;
        public LiveTimelineKeyCharaPartsDataList place06Keys;
        public LiveTimelineKeyCharaPartsDataList place07Keys;
        public LiveTimelineKeyCharaPartsDataList place08Keys;
        public LiveTimelineKeyCharaPartsDataList place09Keys;
        public LiveTimelineKeyCharaPartsDataList place10Keys;
        public LiveTimelineKeyCharaPartsDataList place11Keys;
        public LiveTimelineKeyCharaPartsDataList place12Keys;
        public LiveTimelineKeyCharaPartsDataList place13Keys;
        public LiveTimelineKeyCharaPartsDataList place14Keys;
        public LiveTimelineKeyCharaPartsDataList place15Keys;
        public LiveTimelineKeyCharaPartsDataList place16Keys;
        public LiveTimelineKeyCharaPartsDataList place17Keys;
        public LiveTimelineKeyCharaPartsDataList place18Keys;
        public LiveTimelineKeyCharaPartsDataList place19Keys;
        public LiveTimelineKeyCharaPartsDataList place20Keys;
    }

    [Serializable]
    public class LiveTimelineKeyPreColorCorrectionData : LiveTimelineKeyColorCorrectionData
    {
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.PreColorCorrection;
        public uint excludeColorCorrectionCharacterFlags;
        public uint excludeColorCorrectionShadowFlags;
    }

    [Serializable]
    public class LiveTimelineKeyPreColorCorrectionDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyPreColorCorrectionData> { }

    [Serializable]
    public class LiveTimelinePreColorCorrectionData : ILiveTimelineGroupDataWithName
    {
        public LiveTimelineKeyPreColorCorrectionDataList keys;
    }

    [Serializable]
    public class LiveTimelineKeyTextureAnimationData : LiveTimelineKeyWithInterpolate
    {
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.TextureAnimation;
        public string textureName;
        public bool textureNameEmpty;
        public Vector2 offset;
        public Vector2 tiling;
        public Vector2 scrollSpeed;
        public float scrollInterval;
        public int textureID;
    }

    [Serializable]
    public class LiveTimelineKeyTextureAnimationDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyTextureAnimationData> { }

    [Serializable]
    public class LiveTimelineTextureAnimationData : ILiveTimelineGroupDataWithName
    {
        public LiveTimelineKeyTextureAnimationDataList keys;
    }
}
