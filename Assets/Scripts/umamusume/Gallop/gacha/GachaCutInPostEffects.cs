using Gallop;
using Gallop.RenderPipeline;
using UnityEngine;
using static Gallop.CutIn.Cutt.CutInTimelineWorkSheet;

internal sealed partial class GachaCutInPlayer
{
    private float supportPostEffectTime;

    private void ResetPostEffects()
    {
        supportPostEffectTime = 0f;
        post.DofDiffuionBloomOverlay = DofDiffusionBloomOverlayPass.Parameter.Default();
        post.DofDiffuionBloomOverlay.TargetCamera = camera;
        if (supportOptions == null)
            post.DofDiffuionBloomOverlay.DiffusionReferenceShortEdge = 1080;
        post.SunShafts = SunShaftsPass.Parameter.Default();
        post.GlobalFog = GlobalFogPass.Parameter.Default();
        post.TiltShift = TiltShiftPass.Parameter.Default();
        post.ChromaticAberration = ChromaticAberrationPass.Parameter.Default();
        post.LensDistortion = LensDistortionPass.Parameter.Default();
        post.Hatching = HatchingPass.Parameter.Default();
        post.Fluctuation = FluctuationPass.Parameter.Default();
        post.TransmittedLight.IsEnable = false;
        post.FilmRoll = FilmRollPass.Parameter.Default();
    }

    private void SampleScenePostEffect(string name, PostEffectKey a, PostEffectKey b, float weight)
    {
        switch (name)
        {
            case "DOF":
                SampleDof(a, b, weight);
                break;
            case "SunShaft":
                SampleSunShafts(a, b, weight);
                break;
            case "GlobalFog":
                SampleGlobalFog(a, b, weight);
                break;
            case "TiltShift":
                SampleTiltShift(a.TiltShiftParam, b.TiltShiftParam, weight);
                break;
            case "ChromaticAberration":
                SampleChromaticAberration(a.ChromaticAberrationParam, b.ChromaticAberrationParam, weight);
                break;
            case "LensDistortion":
                SampleLensDistortion(a.LensDistortionParam, b.LensDistortionParam, weight);
                break;
            case "Hatching":
                SampleHatching(a.HatchingParam, b.HatchingParam, weight);
                break;
            case "Flucturation":
                SampleFluctuation(a.FluctuationParam, b.FluctuationParam, weight);
                break;
            case "TransmittedLight":
                SampleTransmittedLight(a.TransmittedLightParam, b.TransmittedLightParam, weight);
                break;
            case "FilmRoll":
                SampleFilmRoll(a.FilmRollParam, b.FilmRollParam, weight);
                break;
        }
    }

    private void SampleDof(PostEffectKey a, PostEffectKey b, float weight)
    {
        ref var p = ref post.DofDiffuionBloomOverlay;
        p.IsEnableDof = a._dofEnable;
        p.IsEnableOldDof = a._oldDof;
        if (!p.IsEnableDof)
            return;
        p.DofQualityType = (DepthBlurAndBloom.DofQuality)a._dofQuality;
        p.DofFocalType = (DepthBlurAndBloom.DofFocalType)a._dofFocalType;
        switch (p.DofFocalType)
        {
            case DepthBlurAndBloom.DofFocalType.Transform:
                if (supportOptions == null)
                {
                    p.DofFocalTransfrom = (uint)a._dofFocalTargetType < 5u &&
                        cast.TryGetValue(a._dofFocalTargetType, out var target) ? target.Actor.transform : null;
                }
                else
                {
                    p.DofFocalTransfrom = cast.TryGetValue(a._dofFocalTargetType, out var support)
                        ? support.PositionAnimator.transform : null;
                    p.IsEnableDof = p.DofFocalTransfrom != null;
                }
                break;
            case DepthBlurAndBloom.DofFocalType.Position:
                p.DofFocalPosition = Vector3.LerpUnclamped(a._dofFocalPosition, b._dofFocalPosition, weight);
                break;
            case DepthBlurAndBloom.DofFocalType.Point:
                p.DofFocalPoint = Mix(a._dofFocalTarget, b._dofFocalTarget, weight);
                break;
        }
        p.DofFocalSize = Mix(a._dofFocalSize, b._dofFocalSize, weight);
        p.DofMaxFocalSize = Current.Data.maxForcalSize;
        p.DofForegroundSize = Mix(a._dofForegroundSize, b._dofForegroundSize, weight);
        p.DofMaxBlurSpread = Mix(a._dofMaxBlurSpread, b._dofMaxBlurSpread, weight);
        p.DofSmoothness = Mix(a._dofSmoothness, b._dofSmoothness, weight);
        p.DofBlurType = (DepthBlurAndBloom.DofBlur)a.DofBlurType;
        p.IsPointBallBlur = a.IsPointBallBlur;
        p.BallBlurPowerFactor = Mix(a.BallBlurPowerFactor, b.BallBlurPowerFactor, weight);
        p.BallBlurBrightnessThreshhold = Mix(a.BallBlurBrightnessThreshhold, b.BallBlurBrightnessThreshhold, weight);
        p.BallBlurBrightnessIntensity = Mix(a.BallBlurBrightnessIntensity, b.BallBlurBrightnessIntensity, weight);
        p.BallBlurSpread = Mix(a.BallBlurSpread, b.BallBlurSpread, weight);
    }

    private void SampleSunShafts(PostEffectKey a, PostEffectKey b, float weight)
    {
        ref var p = ref post.SunShafts;
        p.IsEnable = a._sunShaftEnable && GraphicSettings.IsSunShaft;
        p.UseNativeCutInBlur = supportOptions == null && Current.Unique;
        p.TargetCamera = camera;
        p.SunPosition = Vector3.LerpUnclamped(a._sunShaftsPosition, b._sunShaftsPosition, weight);
        p.SunColor = Color.LerpUnclamped(a._sunShaftsColor, b._sunShaftsColor, weight);
        p.SunPower = Mix(a._sunShaftsPower, b._sunShaftsPower, weight);
        p.BlurRadius = Mix(a._sunShaftsBlurRadius, b._sunShaftsBlurRadius, weight);
        p.BlackLevel = Mix(a._sunShaftsBlackLevel, b._sunShaftsBlackLevel, weight);
        p.CenterBrightness = Mix(a._sunShaftsCenterBrightness, b._sunShaftsCenterBrightness, weight);
        p.CenterMultiplex = Mix(a._sunShaftsCenterMultiplex, b._sunShaftsCenterMultiplex, weight);
        p.KomorebiRate = Mix(a._sunShaftsKomorebiRate, b._sunShaftsKomorebiRate, weight);
        p.ColorLerpRate = Mix(a._sunShaftsColorLerpRate, b._sunShaftsColorLerpRate, weight);
        p.EffectColorPower = Mix(a._sunShaftsEffectColorPower, b._sunShaftsEffectColorPower, weight);
        p.ScreenColorPower = Mix(a._sunShaftsScreenColorPower, b._sunShaftsScreenColorPower, weight);
        p.IsEnabledBorderClear = a._sunShaftsBoarderClear;
        p.ScreenBlendMode = a._sunShaftsScreenBlendMode;
        p.Resolution = a.SunShaftsResolution;
    }

    private void SampleGlobalFog(PostEffectKey a, PostEffectKey b, float weight)
    {
        ref var p = ref post.GlobalFog;
        p.TargetCamera = camera;
        p.IsDistanceFog = a.DistanceFog;
        p.IsHeightFog = a.HeightFog;
        p.IsUseRadialDistance = a.UseRadialDistance;
        p.SceneFogMode = (FogMode)a.SceneFogMode;
        p.FogColor = Color.LerpUnclamped(a.FogColor, b.FogColor, weight);
        p.FogHeight = Mix(a.FogHeight, b.FogHeight, weight);
        p.FogHeightDensity = Mix(a.FogHeightDensity, b.FogHeightDensity, weight);
        p.StartDistance = Mix(a.StartDistance, b.StartDistance, weight);
        p.SceneFogDensity = Mix(a.SceneFogDensity, b.SceneFogDensity, weight);
        p.SceneFogStart = Mix(a.SceneFogStart, b.SceneFogStart, weight);
        p.SceneFogEnd = Mix(a.SceneFogEnd, b.SceneFogEnd, weight);
    }

    private void SampleTiltShift(TiltShiftData a, TiltShiftData b, float weight)
    {
        if (a == null)
            return;
        b ??= a;
        ref var p = ref post.TiltShift;
        p.Mode = a.Mode;
        p.Quality = a.Quality;
        p.Downsample = a.Downsample;
        p.BlurArea = Mix(a.BlurArea, b.BlurArea, weight);
        p.MaxBlurSize = Mix(a.MaxBlurSize, b.MaxBlurSize, weight);
        p.Offset = Vector2.LerpUnclamped(a.Offset, b.Offset, weight);
        p.Roll = Mix(a.Roll, b.Roll, weight);
    }

    private void SampleChromaticAberration(ChromaticData a, ChromaticData b, float weight)
    {
        if (a == null)
            return;
        b ??= a;
        ref var p = ref post.ChromaticAberration;
        p.IsEnable = a.IsEnable;
        p.Type = a.Type;
        var red = Vector2.LerpUnclamped(a.RedOffset, b.RedOffset, weight) * 0.01f;
        var green = Vector2.LerpUnclamped(a.GreenOffset, b.GreenOffset, weight) * 0.01f;
        var blue = Vector2.LerpUnclamped(a.BlueOffset, b.BlueOffset, weight) * 0.01f;
        p.ColorOffsetX = new Vector4(red.x, green.x, blue.x, 0f);
        p.ColorOffsetY = new Vector4(red.y, green.y, blue.y, 0f);
        p.MixParameter.z = Mix(a.Power, b.Power, weight);
        p.MixParameter.w = Mix(a.Clip, b.Clip, weight);
    }

    private void SampleLensDistortion(LensDistortionData a, LensDistortionData b, float weight)
    {
        if (a == null)
            return;
        b ??= a;
        ref var p = ref post.LensDistortion;
        p.Intensity = Mix(a.Intensity, b.Intensity, weight);
        p.IntensityX = Mix(a.IntensityX, b.IntensityX, weight);
        p.IntensityY = Mix(a.IntensityY, b.IntensityY, weight);
        p.CenterX = Mix(a.CenterX, b.CenterX, weight);
        p.CenterY = Mix(a.CenterY, b.CenterY, weight);
        p.Scale = Mix(a.Scale, b.Scale, weight);
    }

    private void SampleHatching(HatchingData a, HatchingData b, float weight)
    {
        if (a == null)
            return;
        b ??= a;
        ref var p = ref post.Hatching;
        p.BlendMode = a.BlendMode;
        p.NoiseTexture = a.NoiseTexture;
        p.BlendAlpha = Mix(a.BlendAlpha, b.BlendAlpha, weight);
        p.StrokeTiling = Mix(a.StrokeTiling, b.StrokeTiling, weight);
        p.StrokeHeight = Mix(a.StrokeHeight, b.StrokeHeight, weight);
        p.StrokeAngle = Mix(a.StrokeAngle, b.StrokeAngle, weight);
        p.StrokeLength = Mix(a.StrokeLength, b.StrokeLength, weight);
        p.CircleMaskOffset = Vector2.LerpUnclamped(a.CircleMaskOffset, b.CircleMaskOffset, weight);
        p.CircleMaskStretchRatio = Vector2.LerpUnclamped(a.CircleMaskStretchRatio, b.CircleMaskStretchRatio, weight);
        p.CircleMaskSize = Mix(a.CircleMaskSize, b.CircleMaskSize, weight);
        p.CircleMaskAngle = Mix(a.CircleMaskAngle, b.CircleMaskAngle, weight);
        p.CircleMaskInnerRatio = Mix(a.CircleMaskInnerRatio, b.CircleMaskInnerRatio, weight);
        p.CircleMaskInnerEffect = Mix(a.CircleMaskInnerEffect, b.CircleMaskInnerEffect, weight);
    }

    private void SampleFluctuation(FluctuationData a, FluctuationData b, float weight)
    {
        if (a == null)
            return;
        b ??= a;
        ref var p = ref post.Fluctuation;
        p.IsEnable = a.IsEnable;
        p.Power = Mix(a.Power, b.Power, weight);
        p.DepthClip = Mix(a.DepthClip, b.DepthClip, weight);

        var direction = Vector2.LerpUnclamped(a.MoveDirection, b.MoveDirection, weight);
        float speed = Mix(a.MovePower, b.MovePower, weight);
        if (supportOptions == null)
        {
            p.MoveDirection = direction;
            p.MovePower = 0f;
            if (p.IsEnable)
                p.MovePosition = direction * (lastFrame * speed);
            return;
        }

        if (a.IsEnable)
        {
            var position = p.MovePosition + direction * (speed * (time - supportPostEffectTime) * FramesPerSecond);
            p.MovePosition = new Vector2(position.x % 1f, position.y % 1f);
        }
        supportPostEffectTime = time;
    }

    private void SampleTransmittedLight(TransmittedLightData a, TransmittedLightData b, float weight)
    {
        if (a == null)
            return;
        b ??= a;
        ref var p = ref post.TransmittedLight;
        p.IsEnable = a.IsEnable;
        p.Iterations = a.Iterations;
        p.BlendMode = a.BloomBlendMode;
        p.Intensity = Mix(a.Intensity, b.Intensity, weight);
        p.Threshold = Mix(a.Threshold, b.Threshold, weight);
        p.BlurSpread = Mix(a.BlurSpread, b.BlurSpread, weight);
    }

    private void SampleFilmRoll(FilmRollData a, FilmRollData b, float weight)
    {
        if (a == null)
            return;
        b ??= a;
        ref var p = ref post.FilmRoll;
        p.IsEnable = a.IsEnable;
        p.IsNoiseRandom = a.IsNoiseRandom;
        p.EnqueueTiming = (FilmRollPass.Parameter.EnqueueTimingType)a.EnqueueTimiing;
        p.RollPosition = Vector2.LerpUnclamped(a.RollPosition, b.RollPosition, weight);
        p.RollMaskSize = Vector2.LerpUnclamped(a.RollMaskSize, b.RollMaskSize, weight);
        p.RollMaskFadeSize = Vector2.LerpUnclamped(a.RollMaskFadeSize, b.RollMaskFadeSize, weight);
        p.RollMaskColor = Color.LerpUnclamped(a.RollMaskColor, b.RollMaskColor, weight);
        p.NoiseColor = Color.LerpUnclamped(a.NoiseColor, b.NoiseColor, weight);
        p.NoiseRange = Vector2.LerpUnclamped(a.NoiseRange, b.NoiseRange, weight);
        p.NoisePower = Mix(a.NoisePower, b.NoisePower, weight);
        p.ScanLineSpeed = Mix(a.ScanLineSpeed, b.ScanLineSpeed, weight);
        p.ScanLineSize = Mix(a.ScanLineSize, b.ScanLineSize, weight);
        p.ScanlineBlurSize = Vector2.LerpUnclamped(a.ScanlineBlurSize, b.ScanlineBlurSize, weight);
        p.ScanLinePower = Mix(a.ScanLinePower, b.ScanLinePower, weight);
        p.Time = time;
    }

    private static float Mix(float a, float b, float weight) => Mathf.LerpUnclamped(a, b, weight);
}
