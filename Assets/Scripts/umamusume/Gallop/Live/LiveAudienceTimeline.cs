using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using Gallop.Live.Cutt;

namespace Gallop.Live
{
    public static class LiveAudienceTimeline
    {
        public sealed class State
        {
            public LiveTimelineKeyAudienceData Values;
            public Quaternion Rotation, DirectionalLightRotation;
            public float ProgressTime;
            public bool RenderEnabled, UseDirectionalLight;
        }
        // Native producer 4ec4ee0. Non-interpolated flags come from the current
        // key; numeric fields interpolate only when the NEXT key enables it.
        public static State Sample(LiveTimelineKeyAudienceData a, LiveTimelineKeyAudienceData b,
            float ratio, int frame, Quaternion cameraRotation)
        {
            if (a == null) return null;
            bool blend = b != null && b.IsInterpolateKey();
            b = b ?? a;
            var values = new LiveTimelineKeyAudienceData
            {
                position = blend ? Lerp(a.position, b.position, ratio) : a.position,
                scale = blend ? Lerp(a.scale, b.scale, ratio) : a.scale,
                cyalumeColor = blend ? Lerp(a.cyalumeColor, b.cyalumeColor, ratio) : a.cyalumeColor,
                cyalumeGlowColor = blend ? Lerp(a.cyalumeGlowColor, b.cyalumeGlowColor, ratio) : a.cyalumeGlowColor,
                cyalumeGlowColorPower = blend ? Lerp(a.cyalumeGlowColorPower, b.cyalumeGlowColorPower, ratio) : a.cyalumeGlowColorPower,
                cyalumeMaskRadius = blend ? Lerp(a.cyalumeMaskRadius, b.cyalumeMaskRadius, ratio) : a.cyalumeMaskRadius,
                animationSetting = a.animationSetting,
                animationRootIndex = a.animationRootIndex,
                animationBodyRegion = a.animationBodyRegion,
                animationCategory = a.animationCategory,
                animationIndex = a.animationIndex,
                animationWrapMode = a.animationWrapMode,
                animationSpeed = blend ? Lerp(a.animationSpeed, b.animationSpeed, ratio) : a.animationSpeed,
                animationOffsetTime = a.animationOffsetTime,
                AnimationTime = blend ? Lerp(a.AnimationTime, b.AnimationTime, ratio) : a.AnimationTime,
                UseAnimationTime = a.UseAnimationTime,
                AudienceColor = blend ? Lerp(a.AudienceColor, b.AudienceColor, ratio) : a.AudienceColor,
                AudienceColorPower = blend ? Lerp(a.AudienceColorPower, b.AudienceColorPower, ratio) : a.AudienceColorPower,
                AudienceToonBrightColor = blend ? Lerp(a.AudienceToonBrightColor, b.AudienceToonBrightColor, ratio) : a.AudienceToonBrightColor,
                AudienceToonDarkColor = blend ? Lerp(a.AudienceToonDarkColor, b.AudienceToonDarkColor, ratio) : a.AudienceToonDarkColor,
                AudienceVertexColorToonPower = blend ? Lerp(a.AudienceVertexColorToonPower, b.AudienceVertexColorToonPower, ratio) : a.AudienceVertexColorToonPower,
                AudienceOutlineWidthPower = blend ? Lerp(a.AudienceOutlineWidthPower, b.AudienceOutlineWidthPower, ratio) : a.AudienceOutlineWidthPower,
                AudienceOutlineColor = blend ? Lerp(a.AudienceOutlineColor, b.AudienceOutlineColor, ratio) : a.AudienceOutlineColor,
                AudienceOutlineColorBlend = a.AudienceOutlineColorBlend,
                AudienceRimColor = blend ? Lerp(a.AudienceRimColor, b.AudienceRimColor, ratio) : a.AudienceRimColor,
                AudienceRimStep = blend ? Lerp(a.AudienceRimStep, b.AudienceRimStep, ratio) : a.AudienceRimStep,
                AudienceRimFeather = blend ? Lerp(a.AudienceRimFeather, b.AudienceRimFeather, ratio) : a.AudienceRimFeather,
                AudienceRimSpecRate = blend ? Lerp(a.AudienceRimSpecRate, b.AudienceRimSpecRate, ratio) : a.AudienceRimSpecRate,
                AudienceRimShadowRate = blend ? Lerp(a.AudienceRimShadowRate, b.AudienceRimShadowRate, ratio) : a.AudienceRimShadowRate,
                AudienceRimHorizonOffset = blend ? Lerp(a.AudienceRimHorizonOffset, b.AudienceRimHorizonOffset, ratio) : a.AudienceRimHorizonOffset,
                AudienceRimVerticalOffset = blend ? Lerp(a.AudienceRimVerticalOffset, b.AudienceRimVerticalOffset, ratio) : a.AudienceRimVerticalOffset,
                AudienceRimColor2 = blend ? Lerp(a.AudienceRimColor2, b.AudienceRimColor2, ratio) : a.AudienceRimColor2,
                AudienceRimStep2 = blend ? Lerp(a.AudienceRimStep2, b.AudienceRimStep2, ratio) : a.AudienceRimStep2,
                AudienceRimFeather2 = blend ? Lerp(a.AudienceRimFeather2, b.AudienceRimFeather2, ratio) : a.AudienceRimFeather2,
                AudienceRimSpecRate2 = blend ? Lerp(a.AudienceRimSpecRate2, b.AudienceRimSpecRate2, ratio) : a.AudienceRimSpecRate2,
                AudienceRimShadowRate2 = blend ? Lerp(a.AudienceRimShadowRate2, b.AudienceRimShadowRate2, ratio) : a.AudienceRimShadowRate2,
                AudienceRimHorizonOffset2 = blend ? Lerp(a.AudienceRimHorizonOffset2, b.AudienceRimHorizonOffset2, ratio) : a.AudienceRimHorizonOffset2,
                AudienceRimVerticalOffset2 = blend ? Lerp(a.AudienceRimVerticalOffset2, b.AudienceRimVerticalOffset2, ratio) : a.AudienceRimVerticalOffset2,
            };
            return new State
            {
                Values = values,
                Rotation = blend ? Quaternion.Lerp(a.Rotation, b.Rotation, ratio) : a.Rotation,
                DirectionalLightRotation = (blend ? Quaternion.Lerp(a.AudienceDirectionalLightRotation,
                    b.AudienceDirectionalLightRotation, ratio) : a.AudienceDirectionalLightRotation) *
                    (a.AudienceDirectionalLightCameraFollow ? cameraRotation : Quaternion.identity),
                ProgressTime = Divide(frame - a.frame, 60f),
                RenderEnabled = a.IsEnabledRender,
                UseDirectionalLight = a.AudienceUseDirectionalLight
            };
        }
        [MethodImpl(MethodImplOptions.NoInlining)] public static float Multiply(float a, float b) => BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(a * b));
        [MethodImpl(MethodImplOptions.NoInlining)] public static float Divide(float a, float b) => BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(a / b));
        [MethodImpl(MethodImplOptions.NoInlining)] public static float Add(float a, float b) => BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(a + b));
        [MethodImpl(MethodImplOptions.NoInlining)] public static float Subtract(float a, float b) => BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(a - b));
        public static float Lerp(float a, float b, float t) => Add(Multiply(Subtract(b, a), t), a);
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => new Vector3(Lerp(a.x,b.x,t),Lerp(a.y,b.y,t),Lerp(a.z,b.z,t));
        public static Color Lerp(Color a, Color b, float t) => new Color(Lerp(a.r,b.r,t),Lerp(a.g,b.g,t),Lerp(a.b,b.b,t),Lerp(a.a,b.a,t));
    }
}
