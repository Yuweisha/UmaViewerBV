using System;
using System.Runtime.CompilerServices;
using Gallop.ImageEffect;
using Gallop.RenderPipeline;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    // Native names retain the source's spelling. Original parents and sinks are
    [Serializable]
    public struct PostEffectUpdateInfo_DOF
    {
        public bool IsEnableDOF;
        public float forcalSize, blurSpread;
        public Vector3 forcalPosition;
        public DepthBlurAndBloom.DofQuality dofQuality;
        public DepthBlurAndBloom.DofBlur dofBlurType;
        public float dofForegroundSize, dofFocalPoint, dofSoomthness;
        public bool isUseFocalPoint;
        public float BallBlurCurveFactor, BallBlurBrightnessThreshhold, BallBlurBrightnessIntensity, BallBlurSpread;
        public bool IsPointBallBlur;
    }

    public static class LiveMultiCameraDofTimeline
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        static float F(float v) => BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(v));
        static float L(float a, float b, float t) => F(a + F(F(b - a) * t));
        static bool Flag(LiveTimelineKeyPostEffectDOFData k, int bit) => ((int)k.attribute & (1 << bit)) != 0;
        public static PostEffectUpdateInfo_DOF Sample(LiveTimelineKeyPostEffectDOFData a, LiveTimelineKeyPostEffectDOFData b, int frame, Vector3 lookAt, Func<LiveCharaPositionFlag, Vector3> characterPosition)
        {
            bool interpolate = b != null && b.IsInterpolateKey();
            float t = interpolate ? LiveTimelineControl.CalculateInterpolationValue(a, b, frame) : 0;
            // Non-interpolating keys copy their current fields, including NaNs.
            if (!interpolate)
                b = a;
            Vector3 start = Flag(a, 16) ? lookAt : characterPosition(a.FocusCharacters);
            Vector3 end = ReferenceEquals(a, b) ? start : Flag(b, 16) ? lookAt : characterPosition(b.FocusCharacters);
            return new PostEffectUpdateInfo_DOF
            {
                IsEnableDOF = Flag(a, 18),
                isUseFocalPoint = Flag(a, 17),
                IsPointBallBlur = Flag(a, 19),
                dofQuality = (DepthBlurAndBloom.DofQuality)a.dofQuality,
                dofBlurType = (DepthBlurAndBloom.DofBlur)a.dofBlurType,
                forcalSize = interpolate ? L(a.forcalSize, b.forcalSize, t) : a.forcalSize,
                blurSpread = interpolate ? L(a.blurSpread, b.blurSpread, t) : a.blurSpread,
                forcalPosition = interpolate ? new Vector3(L(start.x, end.x, t), L(start.y, end.y, t), L(start.z, end.z, t)) : start,
                dofForegroundSize = interpolate ? L(a.dofForegroundSize, b.dofForegroundSize, t) : a.dofForegroundSize,
                dofFocalPoint = interpolate ? L(a.dofFocalPoint, b.dofFocalPoint, t) : a.dofFocalPoint,
                dofSoomthness = interpolate ? L(a.dofSmoothness, b.dofSmoothness, t) : a.dofSmoothness,
                BallBlurCurveFactor = interpolate ? L(a.BallBlurPowerFactor, b.BallBlurPowerFactor, t) : a.BallBlurPowerFactor,
                BallBlurBrightnessThreshhold = interpolate ? L(a.BallBlurBrightnessThreshhold, b.BallBlurBrightnessThreshhold, t) : a.BallBlurBrightnessThreshhold,
                BallBlurBrightnessIntensity = interpolate ? L(a.BallBlurBrightnessIntensity, b.BallBlurBrightnessIntensity, t) : a.BallBlurBrightnessIntensity,
                BallBlurSpread = interpolate ? L(a.BallBlurSpread, b.BallBlurSpread, t) : a.BallBlurSpread
            };
        }

        public static void Apply(ref DofDiffusionBloomOverlayPass.Parameter p, PostEffectUpdateInfo_DOF v)
        {
            p.IsEnableDof = v.IsEnableDOF;
            p.DofFocalSize = v.forcalSize;
            p.DofMaxBlurSpread = v.blurSpread;
            p.DofQualityType = v.dofQuality;
            p.DofBlurType = v.dofBlurType;
            p.DofForegroundSize = v.dofForegroundSize;
            p.DofSmoothness = v.dofSoomthness;
            // The reconstructed render struct's setters don't set the mode.
            if (v.isUseFocalPoint)
            {
                p.DofFocalPoint = v.dofFocalPoint;
                p.DofFocalType = (DepthBlurAndBloom.DofFocalType)2;
            }
            else
            {
                p.DofFocalPosition = v.forcalPosition;
                p.DofFocalType = (DepthBlurAndBloom.DofFocalType)1;
            }

            p.BallBlurPowerFactor = v.BallBlurCurveFactor;
            p.BallBlurBrightnessThreshhold = v.BallBlurBrightnessThreshhold;
            p.BallBlurBrightnessIntensity = v.BallBlurBrightnessIntensity;
            p.BallBlurSpread = v.BallBlurSpread;
            p.IsPointBallBlur = v.IsPointBallBlur;
        }

        // Film/Bloom still use the older class adapter. Publish only their owned
        // fields: its Setup copies the DOF block too, overwriting the native sink.
        public static void PublishFilmAndBloom(ref DofDiffusionBloomOverlayPass.Parameter p, DofDiffusionBloomOverlayParam source)
        {
            var incoming = default(DofDiffusionBloomOverlayPass.Parameter);
            incoming.Setup(source);
            p.IsEnableBloom = incoming.IsEnableBloom;
            p.IsEnableDiffusion = incoming.IsEnableDiffusion;
            p.BloomDofWeight = incoming.BloomDofWeight;
            p.BloomThreshold = incoming.BloomThreshold;
            p.BloomIntensity = incoming.BloomIntensity;
            p.BloomBlurSize = incoming.BloomBlurSize;
            p.BloomBlendMode = incoming.BloomBlendMode;
            p.DiffusionBlurSize = incoming.DiffusionBlurSize;
            p.DiffusionBright = incoming.DiffusionBright;
            p.DiffusionThreshold = incoming.DiffusionThreshold;
            p.DiffusionSaturation = incoming.DiffusionSaturation;
            p.DiffusionContrast = incoming.DiffusionContrast;
            p.Overlay1 = incoming.Overlay1;
            p.Overlay2 = incoming.Overlay2;
            p.Overlay3 = incoming.Overlay3;
            p.TargetCamera = source.TargetCamera;
            p.IsDisableDofTemporary = p.IsEnableDofAutoDisable && !(p.DofFocalSize < p.DofMaxFocalSize);
            p.DecideDrawType(p.IsEnableDof && !p.IsDisableDofTemporary && GallopImageEffect.UserDofEnabled);
        }
    }

    public partial class LiveTimelineControl
    {
        public event Action<PostEffectUpdateInfo_DOF, int> OnUpdatePostEffect_DOFMultiCamera;
        // Source +B48 caches the final look-at AFTER camera layer adjustment,
        // indexed by group ordinal; the sink is indexed by group.MultiCameraNo.
        private Vector3[] _multiCameraDofLookAt;
        private Func<LiveCharaPositionFlag, Vector3> _multiDofCharacterPosition;
        private Vector3 MultiDofCharacterPosition(LiveCharaPositionFlag flags) => GetPositionWithCharacters(flags, LiveCameraCharaParts.Face);
        internal void AlterUpdate_MultiCameraPostEffect_DOF(LiveTimelineWorkSheet sheet, int frame)
        {
            if (_multiCameraCache == null || OnUpdatePostEffect_DOFMultiCamera == null || sheet.postEffectDOFMultiCameraKeys == null || _multiCameraDofLookAt == null)
                return;
            if (_multiDofCharacterPosition == null)
                _multiDofCharacterPosition = MultiDofCharacterPosition;
            for (int i = 0; i < sheet.postEffectDOFMultiCameraKeys.Count; i++)
            {
                var group = sheet.postEffectDOFMultiCameraKeys[i];
                if (i >= _multiCameraDofLookAt.Length || group == null || group.keys == null)
                    continue;
                if (group.keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) || !group.keys.EnablePlayModeTimeline(_playMode))
                    continue;
                LiveTimelineControl.FindTimelineKey(out var current, out var next, group.keys, frame);
                var a = current as LiveTimelineKeyMultiCameraPostEffectDOFData;
                var b = next as LiveTimelineKeyMultiCameraPostEffectDOFData;
                // Native 0x4edccbf exits the ENTIRE parent, unlike disabled lists
                // which skip only their group. Covered by two-group fixtures.
                if (a == null)
                    return;
                OnUpdatePostEffect_DOFMultiCamera(LiveMultiCameraDofTimeline.Sample(a, b, frame, _multiCameraDofLookAt[i], _multiDofCharacterPosition), group.MultiCameraNo);
            }
        }
    }
}
