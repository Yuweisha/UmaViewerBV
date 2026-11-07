using Gallop.Live.Cutt;
using Gallop.RenderPipeline;

namespace Gallop.Live
{
    public partial class Director
    {
        private void SubscribeNativePostTimeline(LiveTimelineControl control)
        {
            control.OnUpdateVortex+=OnUpdateVortex;
            control.OnUpdateFluctuation+=OnUpdateFluctuation;
            control.OnUpdateLensDistortion+=OnUpdateLensDistortion;
            control.OnUpdateTiltShift+=OnUpdateTiltShift;
            control.OnUpdateHatching+=OnUpdateHatching;
            control.OnUpdateTiltShiftMultiCamera+=OnUpdateTiltShiftMultiCamera;
            control.OnUpdateRadialBlurMultiCamera+=OnUpdateRadialBlurMultiCamera;
            control.OnUpdatePostEffect_DOFMultiCamera+=OnUpdatePostEffect_DOFMultiCamera;
            control.OnUpdateCharaFootLight+=OnUpdateCharaFootLight;
        }
        private void UnsubscribeNativePostTimeline(LiveTimelineControl control)
        {
            control.OnUpdateVortex-=OnUpdateVortex;
            control.OnUpdateFluctuation-=OnUpdateFluctuation;
            control.OnUpdateLensDistortion-=OnUpdateLensDistortion;
            control.OnUpdateTiltShift-=OnUpdateTiltShift;
            control.OnUpdateHatching-=OnUpdateHatching;
            control.OnUpdateTiltShiftMultiCamera-=OnUpdateTiltShiftMultiCamera;
            control.OnUpdateRadialBlurMultiCamera-=OnUpdateRadialBlurMultiCamera;
            control.OnUpdatePostEffect_DOFMultiCamera-=OnUpdatePostEffect_DOFMultiCamera;
            control.OnUpdateCharaFootLight-=OnUpdateCharaFootLight;
        }
        // Native uses activeIndex-motionIndex and bounds checks, never a Camera.main fallback.
        private GallopImageEffect NativePostEffect()
            =>_cameraImageEffects!=null&&_activeCameraIndex>=0&&_activeCameraIndex<_cameraImageEffects.Length?_cameraImageEffects[_activeCameraIndex]:null;
        private void OnUpdateVortex(VortexPass.Parameter info)
        {var effect=NativePostEffect();if(effect!=null)LiveNativePostTimeline.Apply(ref effect.RenderParameter.Vortex,info);}
        private void OnUpdateFluctuation(FluctuationPass.Parameter info)
        {var effect=NativePostEffect();if(effect!=null)LiveNativePostTimeline.Apply(ref effect.RenderParameter.Fluctuation,info,_liveTimeController!=null&&_liveTimeController.IsCalcElapsedTime);}
        private void OnUpdateLensDistortion(LensDistortionPass.Parameter info)
        {var effect=NativePostEffect();if(effect!=null)LiveNativePostTimeline.Apply(ref effect.RenderParameter.LensDistortion,info);}
        private void OnUpdateTiltShift(TiltShiftPass.Parameter info)
        {var effect=NativePostEffect();if(effect!=null)LiveNativePostTimeline.Apply(ref effect.RenderParameter.TiltShift,info);}
        private void OnUpdateHatching(HatchingPass.Parameter info)
        {var effect=NativePostEffect();if(effect!=null)LiveNativePostTimeline.Apply(ref effect.RenderParameter.Hatching,info);}
        private void OnUpdateTiltShiftMultiCamera(TiltShiftPass.Parameter info,int index)
        {if(_multiCameras==null||index<0||index>=_multiCameras.Length)return;LiveNativePostTimeline.Apply(ref _multiCameras[index].PostEffectParameter.TiltShift,info);}
        private void OnUpdateRadialBlurMultiCamera(RadialBlurPass.Parameter info,int index)
        {if(_multiCameras==null||index<0||index>=_multiCameras.Length)return;LiveRadialBlurTimeline.Apply(ref _multiCameras[index].PostEffectParameter.RadialBlur,info);}
        private void OnUpdatePostEffect_DOFMultiCamera(PostEffectUpdateInfo_DOF info,int index)
        {if(_multiCameras==null||index<0||index>=_multiCameras.Length||_multiCameras[index]==null)return;LiveMultiCameraDofTimeline.Apply(ref _multiCameras[index].PostEffectParameter.DofDiffuionBloomOverlay,info);}
    }
}
