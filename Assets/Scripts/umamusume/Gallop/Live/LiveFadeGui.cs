using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live
{
    public interface IFadeGuiWidget { Color Color { get; set; } }
    public interface IFadeGuiGradation { void SetAlpha(float alpha); }

    // State reconstruction of FadeGui entry points used by LiveTitleController:
    // FadeIn 0x4e7e8e0, FadeOut 0x4e7e9e0, Disappear 0x4e7e890.
    public sealed class LiveFadeGui
    {
        public float DefaultDuration;
        public bool LetsFadeIn;
        public bool LetsFadeOut;
        public float MinAlpha;
        public float MaxAlpha = 1f;
        public float Duration;
        public bool DisableWhenFinish;
        public float CurTime;
        public bool IsFade;
        public bool IsPositive;
        public bool IsUpdateCurrentTime;
        public bool IsInitialized;
        public readonly List<float> SetAlphaCalls = new List<float>();
        public readonly List<IFadeGuiWidget> Widgets = new List<IFadeGuiWidget>();
        public readonly List<IFadeGuiGradation> Gradations = new List<IFadeGuiGradation>();

        public void FadeIn(float duration, float currentTime)
        {
            CurTime = currentTime;
            Duration = Mathf.Max(0f, duration);
            IsFade = true;
            IsPositive = true;
            IsUpdateCurrentTime = false;
        }

        public void FadeOut(float duration, float currentTime)
        {
            DisableWhenFinish = false;
            if (currentTime < duration)
            {
                Duration = Mathf.Max(0f, duration);
                CurTime = currentTime;
                IsPositive = false;
                IsUpdateCurrentTime = false;
                IsFade = true;
                return;
            }
            SetAlpha(0f);
            IsFade = false;
        }

        public void Disappear()
        {
            SetAlpha(0f);
            IsFade = false;
        }

        public bool DisabledByFade;

        public void AlterUpdate(float deltaTime)
        {
            if (!IsInitialized) return;
            if (MinAlpha > MaxAlpha) MinAlpha = MaxAlpha;
            if (MinAlpha < 0f) MinAlpha = 0f;
            if (MaxAlpha > 1f) MaxAlpha = 1f;
            if (IsFade)
            {
                if (IsUpdateCurrentTime) CurTime = MonitorFloat32(CurTime + deltaTime);
                float alpha;
                if (!IsPositive)
                {
                    if (Duration <= 0f) alpha = 0f;
                    else
                    {
                        float t = MonitorFloat32(CurTime / Duration);
                        float span = MonitorFloat32(MaxAlpha - MinAlpha);
                        if (t < 0f) t = 0f;
                        else if (t > span) t = span;
                        alpha = MonitorFloat32(MaxAlpha - t);
                    }
                }
                else
                {
                    if (Duration <= 0f) alpha = 0f;
                    else
                    {
                        float t = MonitorFloat32(CurTime / Duration);
                        if (t < MinAlpha) t = MinAlpha;
                        if (t > MaxAlpha) t = MaxAlpha;
                        alpha = t;
                    }
                }
                SetAlpha(alpha);
                bool finished = IsPositive ? !(alpha < MaxAlpha) : !(alpha > MinAlpha);
                if (finished)
                {
                    IsFade = false;
                    if (DisableWhenFinish) DisabledByFade = true;
                    LetsFadeIn = false;
                    LetsFadeOut = false;
                }
            }
            if (LetsFadeIn && !IsFade)
            {
                CurTime = 0f;
                Duration = Mathf.Max(0f, DefaultDuration);
                IsFade = true;
                IsPositive = true;
                IsUpdateCurrentTime = true;
                LetsFadeIn = false;
            }
            if (LetsFadeOut && !IsFade)
            {
                DisableWhenFinish = false;
                CurTime = 0f;
                Duration = Mathf.Max(0f, DefaultDuration);
                IsFade = true;
                IsPositive = false;
                IsUpdateCurrentTime = true;
                LetsFadeOut = false;
            }
        }

        static float MonitorFloat32(float value)
        {
            return BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(value));
        }

        public void SetAlpha(float alpha)
        {
            if (!IsInitialized) return;
            for (int i = 0; i < Widgets.Count; i++)
            {
                var c = Widgets[i].Color;
                Widgets[i].Color = new Color(c.r, c.g, c.b, alpha);
            }
            for (int i = 0; i < Gradations.Count; i++) Gradations[i].SetAlpha(alpha);
            SetAlphaCalls.Add(alpha);
        }
    }
}
