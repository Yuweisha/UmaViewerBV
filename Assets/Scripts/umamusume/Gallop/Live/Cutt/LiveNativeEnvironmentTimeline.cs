using UnityEngine;
namespace Gallop.Live.Cutt
{
    public struct EnvironmentShadowUpdateInfo { public bool isValid; public LiveCharaPositionFlag charaPositionFlag; public bool isSoftShadow; }
    public delegate void EnvironmentShadowDelegate(ref EnvironmentShadowUpdateInfo info);
    public struct EnvironmentWaterUpdateInfo
    {
        public bool isValid;
        public float waterReflection,waveScale,waterDistortion,waterUCross,waterVCross,waterUSpeed,waterVSpeed,waterNormalPower,waveDistortionPower,waveClearly,waveDiffusion,waveDecline;
        public Color waterColor;public float waterVOffset;
    }
    public delegate void EnvironmentWaterDelegate(ref EnvironmentWaterUpdateInfo info);
    public struct EnvironmentStageFovShiftUpdateInfo { public bool isValid; public float BaseFov,ShiftPower;public bool IsShiftY; }
    public delegate void EnvironmentStageFovShiftDelegate(ref EnvironmentStageFovShiftUpdateInfo info);
    public partial class LiveTimelineControl
    {
        public event EnvironmentShadowDelegate OnEnvironmentShadow;
        public event EnvironmentWaterDelegate OnEnvironmentWater;
        public event EnvironmentStageFovShiftDelegate OnEnvironmentStageFovShift;
        LiveCharaPositionFlag _nativeEnvironmentShadowFlags;
        bool _nativeEnvironmentSoftShadow;
        // Original complete parent 4ecdee0. Shadow cache changes only while its callback is bound.
        private void EvaluateNativeEnvironment(LiveTimelineWorkSheet sheet,float frame)
        {
            if(sheet?.environmentDataLists==null)return;
            foreach(var group in sheet.environmentDataLists)
            {
                var keys=group?.keys;
                if(keys==null || keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) || !keys.EnablePlayModeTimeline(_playMode))continue;
                FindTimelineKeyCurrent(out LiveTimelineKey key,keys,frame);
                if(!(key is LiveTimelineKeyStageEnvironmentData current))continue;
                if(OnEnvironmentShadow!=null && (_nativeEnvironmentShadowFlags!=current.characterShadow || _nativeEnvironmentSoftShadow!=current.isSoftShadow))
                {
                    _nativeEnvironmentShadowFlags=current.characterShadow;_nativeEnvironmentSoftShadow=current.isSoftShadow;
                    var info=new EnvironmentShadowUpdateInfo {isValid=current.isValidShadow,charaPositionFlag=current.characterShadow,isSoftShadow=current.isSoftShadow};
                    OnEnvironmentShadow(ref info);
                }
                if(OnEnvironmentMirror!=null)
                {
                    var info=new EnvironmentMirrorUpdateInfo {isValid=current.isValidMirror,mirror=current.isMirror,mirrorReflectionRate=current.mirrorReflectionRate,
                        bgMirror=current.isBgMirror,IsMirrorBg3d=current.IsMirrorBg3d,charaPositionFlag=current.characterMirror,
                        VisibleHeadFlag=current.CharacterMirrorHead,IsToonMirror=current.IsToonMirror,
                        IsEnabledCharacterMirrorHead=current.IsEnabledMirrorCharacterHead,EnableCharacterMirrorExpandFaceBounds=current.EnableCharacterMirrorExpandFaceBounds,
                        CharacterMirrorExpandFaceBounds=current.CharacterMirrorExpandFaceBounds};
                    OnEnvironmentMirror(ref info);
                }
                if(OnEnvironmentWater!=null)
                {
                    var info=new EnvironmentWaterUpdateInfo {isValid=current.isValidWaterReflection,waterReflection=current.waterReflection,
                        waveScale=current.waveScale,waterDistortion=current.waterDistortion,waterUCross=current.waterUCross,waterVCross=current.waterVCross,
                        waterUSpeed=current.waterUSpeed,waterVSpeed=current.waterVSpeed,waterNormalPower=current.waterNormalPower,
                        waveDistortionPower=current.waveDistortionPower,waveClearly=current.waveClearly,waveDiffusion=current.waveDiffusion,waveDecline=current.waveDecline,
                        waterColor=current.waterColor,waterVOffset=current.waterVOffset};
                    OnEnvironmentWater(ref info);
                }
                if(OnEnvironmentStageFovShift!=null)
                {
                    var info=new EnvironmentStageFovShiftUpdateInfo {isValid=current.IsValidStageFovShift,BaseFov=current.BaseFov,ShiftPower=current.ShiftPower,IsShiftY=current.IsShiftY};
                    OnEnvironmentStageFovShift(ref info);
                }
            }
        }
    }
}
