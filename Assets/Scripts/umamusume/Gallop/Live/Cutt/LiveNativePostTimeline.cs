using System;
using System.Runtime.CompilerServices;
using Gallop.RenderPipeline;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    public static class LiveNativePostTimeline
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        static float F(float x) => BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(x));
        static float L(float a, float b, float t) => F(a + F(F(b - a) * t));
        static float T(LiveTimelineKeyWithInterpolate a, LiveTimelineKeyWithInterpolate b, int frame) => b != null && b.IsInterpolateKey() ? LiveTimelineControl.CalculateInterpolationValue(a, b, frame) : 0;
        internal static bool TryKeys<TK>(LiveTimelineKeyDataListTemplate<TK> keys, int frame, TimelinePlayerMode mode, out TK a, out TK b)
            where TK : LiveTimelineKey
        {
            a = b = null;
            if (keys == null || keys.Count == 0 || keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) || !keys.EnablePlayModeTimeline(mode))
                return false;
            LiveTimelineControl.FindTimelineKey(out var current, out var next, keys, frame);
            a = current as TK;
            b = next as TK;
            return a != null;
        }

        public static VortexPass.Parameter Sample(LiveTimelineKeyVortexData a, LiveTimelineKeyVortexData b, int frame)
        {
            float t = T(a, b, frame);
            b = b ?? a;
            return new VortexPass.Parameter
            {
                IsEnable = a.IsEnable,
                VortexArea = Vector4.Lerp(a.Area, b.Area, t),
                RotVolume = L(a.RotVolume, b.RotVolume, t),
                DepthClip = L(a.DepthClip, b.DepthClip, t)
            };
        }

        public static FluctuationPass.Parameter Sample(LiveTimelineKeyFluctuationData a, LiveTimelineKeyFluctuationData b, int frame)
        {
            float t = T(a, b, frame);
            b = b ?? a;
            return new FluctuationPass.Parameter
            {
                IsEnable = a.IsEnable,
                DisableGraphicSettings = a.DisableGraphicSettings,
                MoveDirection = Vector2.Lerp(a.MoveDirection, b.MoveDirection, t),
                MovePower = L(a.MovePower, b.MovePower, t),
                Power = L(a.Power, b.Power, t),
                DepthClip = L(a.DepthClip, b.DepthClip, t)
            };
        }

        public static LensDistortionPass.Parameter Sample(LiveTimelineKeyLensDistortionData a, LiveTimelineKeyLensDistortionData b, int frame)
        {
            float t = T(a, b, frame);
            b = b ?? a;
            return new LensDistortionPass.Parameter
            {
                Intensity = L(a.Intensity, b.Intensity, t),
                IntensityX = L(a.IntensityX, b.IntensityX, t),
                IntensityY = L(a.IntensityY, b.IntensityY, t),
                CenterX = L(a.CenterX, b.CenterX, t),
                CenterY = L(a.CenterY, b.CenterY, t),
                Scale = L(a.Scale, b.Scale, t)
            };
        }

        public static TiltShiftPass.Parameter Sample(LiveTimelineKeyTiltShiftData a, LiveTimelineKeyTiltShiftData b, int frame)
        {
            float t = T(a, b, frame);
            b = b ?? a;
            return new TiltShiftPass.Parameter
            {
                Mode = a.mode,
                Quality = a.quality,
                Downsample = a.downsample,
                BlurArea = L(a.blurArea, b.blurArea, t),
                MaxBlurSize = L(a.maxBlurSize, b.maxBlurSize, t),
                Offset = new Vector2(L(a.offset.x, b.offset.x, t), L(a.offset.y, b.offset.y, t)),
                Roll = L(a.roll, b.roll, t)
            };
        }

        public static HatchingPass.Parameter Sample(LiveTimelineKeyHatchingData a, LiveTimelineKeyHatchingData b, int frame)
        {
            bool interpolate = b != null && b.IsInterpolateKey();
            float t = T(a, b, frame);
            b = b ?? a;
            return new HatchingPass.Parameter
            {
                BlendMode = a.BlendMode,
                BlendAlpha = L(a.BlendAlpha, b.BlendAlpha, t),
                NoiseTexture = a.NoiseTexture,
                StrokeTiling = L(a.StrokeTiling, b.StrokeTiling, t),
                StrokeHeight = L(a.StrokeHeight, b.StrokeHeight, t),
                StrokeAngle = L(a.StrokeAngle, b.StrokeAngle, t),
                StrokeLength = L(a.StrokeLength, b.StrokeLength, t),
                CircleMaskOffset = new Vector2(L(a.CircleMaskOffset.x, b.CircleMaskOffset.x, t), L(a.CircleMaskOffset.y, b.CircleMaskOffset.y, t)),
                CircleMaskSize = L(a.CircleMaskSize, b.CircleMaskSize, t),
                CircleMaskStretchRatio = new Vector2(L(a.CircleMaskStretchRatio.x, b.CircleMaskStretchRatio.x, t), L(a.CircleMaskStretchRatio.y, b.CircleMaskStretchRatio.y, t)),
                // Native0x4ef6070 uses current Size(+58), not current Angle(+64), on this branch.
                CircleMaskAngle = interpolate ? L(a.CircleMaskSize, b.CircleMaskAngle, t) : a.CircleMaskAngle,
                CircleMaskInnerRatio = L(a.CircleMaskInnerRatio, b.CircleMaskInnerRatio, t),
                CircleMaskInnerEffect = L(a.CircleMaskInnerEffect, b.CircleMaskInnerEffect, t)
            };
        }

        public static void Apply(ref VortexPass.Parameter p, VortexPass.Parameter v)
        {
            p.IsEnable = v.IsEnable;
            if (!p.IsEnable)
                return;
            p.VortexArea = v.VortexArea;
            p.RotVolume = v.RotVolume;
            p.DepthClip = v.DepthClip;
        }

        public static void Apply(ref FluctuationPass.Parameter p, FluctuationPass.Parameter v, bool calcElapsedTime)
        {
            p.IsEnable = v.IsEnable;
            p.DisableGraphicSettings = v.DisableGraphicSettings;
            if (!p.IsDepthTexture)
                return;
            p.MoveDirection = v.MoveDirection;
            p.MovePower = calcElapsedTime ? v.MovePower : 0;
            p.Power = v.Power;
            p.DepthClip = v.DepthClip;
        }

        public static void Apply(ref LensDistortionPass.Parameter p, LensDistortionPass.Parameter v)
        {
            p.Intensity = v.Intensity;
            p.IntensityX = v.IntensityX;
            p.IntensityY = v.IntensityY;
            p.CenterX = v.CenterX;
            p.CenterY = v.CenterY;
            p.Scale = v.Scale;
        }

        public static void Apply(ref TiltShiftPass.Parameter p, TiltShiftPass.Parameter v)
        {
            if (p.UnlinkCutt)
                return;
            p.Mode = v.Mode;
            p.Quality = v.Quality;
            p.Downsample = v.Downsample;
            p.BlurArea = v.BlurArea;
            p.MaxBlurSize = v.MaxBlurSize;
            p.Offset = v.Offset;
            p.Roll = v.Roll;
        }

        public static void Apply(ref HatchingPass.Parameter p, HatchingPass.Parameter v)
        {
            var order = p.RenderPassEvent;
            p = v;
            p.RenderPassEvent = order;
        }
    }

    public partial class LiveTimelineControl
    {
        public event Action<VortexPass.Parameter> OnUpdateVortex;
        public event Action<FluctuationPass.Parameter> OnUpdateFluctuation;
        public event Action<LensDistortionPass.Parameter> OnUpdateLensDistortion;
        public event Action<TiltShiftPass.Parameter> OnUpdateTiltShift;
        public event Action<HatchingPass.Parameter> OnUpdateHatching;
        public event Action<TiltShiftPass.Parameter, int> OnUpdateTiltShiftMultiCamera;
        public event Action<RadialBlurPass.Parameter, int> OnUpdateRadialBlurMultiCamera;
        internal void AlterUpdate_RadialBlur(LiveTimelineWorkSheet sheet, int frame)
        {
            if (OnUpdateRadialBlur != null && LiveRadialBlurTimeline.TryEvaluate(sheet.radialBlurKeys, frame, _playMode, out var info))
                OnUpdateRadialBlur(info);
        }

        internal void AlterUpdate_Vortex(LiveTimelineWorkSheet sheet, int frame)
        {
            if (OnUpdateVortex != null && LiveNativePostTimeline.TryKeys(sheet.VortexKeys, frame, _playMode, out var a, out var b))
                OnUpdateVortex(LiveNativePostTimeline.Sample(a, b, frame));
        }

        internal void AlterUpdate_Fluctuation(LiveTimelineWorkSheet sheet, int frame)
        {
            if (OnUpdateFluctuation != null && LiveNativePostTimeline.TryKeys(sheet.FluctuationKeys, frame, _playMode, out var a, out var b))
                OnUpdateFluctuation(LiveNativePostTimeline.Sample(a, b, frame));
        }

        internal void AlterUpdate_LensDistortion(LiveTimelineWorkSheet sheet, int frame)
        {
            if (OnUpdateLensDistortion != null && LiveNativePostTimeline.TryKeys(sheet.LensDistortionKeys, frame, _playMode, out var a, out var b))
                OnUpdateLensDistortion(LiveNativePostTimeline.Sample(a, b, frame));
        }

        internal void AlterUpdate_TiltShift(LiveTimelineWorkSheet sheet, int frame)
        {
            if (OnUpdateTiltShift != null && LiveNativePostTimeline.TryKeys(sheet.tiltShiftKeys, frame, _playMode, out var a, out var b))
                OnUpdateTiltShift(LiveNativePostTimeline.Sample(a, b, frame));
        }

        internal void AlterUpdate_Hatching(LiveTimelineWorkSheet sheet, int frame)
        {
            if (OnUpdateHatching != null && LiveNativePostTimeline.TryKeys(sheet.hatchingKeys, frame, _playMode, out var a, out var b))
                OnUpdateHatching(LiveNativePostTimeline.Sample(a, b, frame));
        }

        internal void AlterUpdate_MultiCameraTiltShift(LiveTimelineWorkSheet sheet, int frame)
        {
            if (_multiCameraCache == null || OnUpdateTiltShiftMultiCamera == null || sheet.multiCameraTiltShiftDataLists == null)
                return;
            foreach (var group in sheet.multiCameraTiltShiftDataLists)
                if (group != null && LiveNativePostTimeline.TryKeys(group.keys, frame, _playMode, out var a, out var b))
                    OnUpdateTiltShiftMultiCamera(LiveNativePostTimeline.Sample(a, b, frame), group.MultiCameraNo);
        }

        internal void AlterUpdate_MultiCameraRadialBlur(LiveTimelineWorkSheet sheet, int frame)
        {
            if (_multiCameraCache == null || OnUpdateRadialBlurMultiCamera == null || sheet.multiCameraRadialBlurDataLists == null)
                return;
            foreach (var group in sheet.multiCameraRadialBlurDataLists)
                if (group != null && LiveRadialBlurTimeline.TryEvaluate(group.keys, frame, _playMode, out var info))
                    OnUpdateRadialBlurMultiCamera(info, group.MultiCameraNo);
        }
    }
}
