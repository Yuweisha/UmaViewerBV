using System;
using UnityEngine;
namespace Gallop.Live.Cutt
{
    [Serializable]
    public class LiveTimelineKeyStageEnvironmentData : LiveTimelineKey
    {
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.Environment;
        public bool isValidMirror = true;
        public bool isMirror = false;
        public bool isBgMirror = true;
        public bool IsMirrorBg3d = false;
        public bool EnableCharacterMirrorExpandFaceBounds = false;
        public LiveCharaPositionFlag characterMirror = (LiveCharaPositionFlag)1048575;
        public LiveCharaPositionFlag CharacterMirrorHead = (LiveCharaPositionFlag)1048575;
        public LiveCharaPositionFlag CharacterMirrorExpandFaceBounds = (LiveCharaPositionFlag)0;
        public float mirrorReflectionRate = 1.0f;
        public bool isValidShadow = false;
        public LiveCharaPositionFlag characterShadow = (LiveCharaPositionFlag)1048575;
        public bool isSoftShadow = false;
        public bool IsToonMirror = false;
        public bool isValidWaterReflection = false;
        public float waterReflection = 1.0f;
        public float waveScale = 0.009999999776482582f;
        public float waterDistortion = 0.019999999552965164f;
        public float waterUCross = 0.30000001192092896f;
        public float waterVCross = 0.30000001192092896f;
        public float waterUSpeed = 1.0f;
        public float waterVSpeed = 2.0f;
        public float waterNormalPower = 0.0f;
        public float waveDistortionPower = 0.03999999910593033f;
        public float waveClearly = 10.0f;
        public float waveDiffusion = 0.15000000596046448f;
        public float waveDecline = 0.9800000190734863f;
        public Color waterColor = new Color(0.8899999856948853f,0.8980000019073486f,0.9290000200271606f,1.0f);
        public float waterVOffset = 0.004999999888241291f;
        public bool IsValidStageFovShift = false;
        public float BaseFov = 35.0f;
        public float ShiftPower = 0.0f;
        public bool IsShiftY = false;
        public bool _isValidMirror = true;
        public bool _mirror = false;
        public bool _bgMirror = true;
        public LiveCharaPositionFlag _characterMirror = (LiveCharaPositionFlag)1048575;
        public float _mirrorReflectionRate = -1.0f;
        public bool IsEnabledMirrorCharacterHead => ((uint)attribute & 65536u) != 0;
        public override void OnLoad(LiveTimelineControl control)
        {
            base.OnLoad(control);
            if(_mirrorReflectionRate>=0f)
            {
                isValidMirror=_isValidMirror;isMirror=_mirror;isBgMirror=_bgMirror;
                characterMirror=_characterMirror;mirrorReflectionRate=_mirrorReflectionRate;_mirrorReflectionRate=-1f;
            }
        }
    }
    [Serializable]
    public class LiveTimelineKeyStageEnvironmentDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyStageEnvironmentData> { }
    [Serializable]
    public class LiveTimelineMirrorReflectionData : ILiveTimelineGroupDataWithName
    {
        public LiveTimelineKeyMirrorReflectionDataList keys = new LiveTimelineKeyMirrorReflectionDataList();
        [SerializeField] LiveTimelineDefine.MirrorReflectionBaseCameraType _baseCameraType;
        [SerializeField] int _baseCameraIndex;
        public LiveTimelineDefine.MirrorReflectionBaseCameraType BaseCameraType { get=>_baseCameraType;set=>_baseCameraType=value; }
        public int BaseCameraIndex { get=>_baseCameraIndex;set=>_baseCameraIndex=value; }
        public override ILiveTimelineKeyDataList GetKeyList()=>keys;
    }
    public partial class LiveTimelineWorkSheet
    {
        [SerializeField] public System.Collections.Generic.List<LiveTimelineMirrorReflectionData> MirrorReflectionDataList;
    }
}
