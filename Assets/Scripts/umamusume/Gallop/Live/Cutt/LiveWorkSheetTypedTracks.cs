using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    public partial class LiveTimelineWorkSheet
    {
        [SerializeField] public LiveTimelineKeyCameraMotionDataList cameraMotionKeys;
        [SerializeField] public LiveTimelineKeyTailMotionDataList tailMotionDataList;
        [SerializeField] public LiveTimelineHdrBloomData hdrBloomKeys;
        [SerializeField] public LiveTimelineKeyTitleDataList titleKeys;
        [SerializeField] public LiveTimelineKeyFlashPlayerDataList flashPlayerKeys;
        [SerializeField] public LiveTimelineKeyVoiceDataList VoiceKeys;
        [SerializeField] public LiveTimelineKeyEventDataList eventKeys;
        [SerializeField] public LiveTimelineKeyScreenCaptureDataList ScreenCaptureDataList;
        [SerializeField] public LiveTimelineCharaPartsData CharaPartsKeys;
        [SerializeField] public List<LiveTimelinePreColorCorrectionData> preColorCorrectionDataLists;
        [SerializeField] public List<LiveTimelineTextureAnimationData> textureAnimationList;
        [SerializeField] public List<LiveTimelineEyeCameraPositionData> EyeCameraPosList;
        [SerializeField] public List<LiveTimelineEyeCameraLookAtData> EyeCameraLookAtList;
        [SerializeField] public List<LiveTimelineRayHitEffectData> RayHitEffectDataList;
        [SerializeField] public List<LiveTimelineContactEffectData> ContactEffectDataList;
    }
}
