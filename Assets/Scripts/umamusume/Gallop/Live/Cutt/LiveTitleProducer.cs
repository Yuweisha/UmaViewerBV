using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    public interface ILiveTitleTarget
    {
        bool IsDisappear { get; }
        void FadeIn(float duration, float currentTime);
        void FadeOut(float duration, float currentTime);
        void Disappear();
    }
    // Reconstructed LiveTitleController state surface. The original forwards to
    // FadeGui; the state machine (IsDisappear) is what the timeline producer drives.
    public sealed class LiveTitleControllerState : ILiveTitleTarget
    {
        public bool IsDisappear { get; set; }
        public LiveFadeGui FadeGui;
        public readonly List<string> Calls = new List<string>();

        public void FadeIn(float duration, float currentTime)
        {
            Calls.Add("FadeIn(" + duration.ToString("R") + "," + currentTime.ToString("R") + ")");
            IsDisappear = false;
            FadeGui?.FadeIn(duration, currentTime);
        }

        public void FadeOut(float duration, float currentTime)
        {
            Calls.Add("FadeOut(" + duration.ToString("R") + "," + currentTime.ToString("R") + ")");
            IsDisappear = false;
            FadeGui?.FadeOut(duration, currentTime);
        }

        public void Disappear()
        {
            if (IsDisappear) return;
            Calls.Add("Disappear");
            IsDisappear = true;
            FadeGui?.Disappear();
        }
    }

    // Producer reconstruction of AlterUpdate_Title 0x4ee4d60.
    // Native dispatch on key._actionType raw value: 0 -> Disappear (only when not
    // already disappeared), 1 -> FadeGui.FadeIn, 2 -> FadeGui.FadeOut, matching the enum. When no current
    // key exists the native body falls through to the Disappear branch.
    public static class LiveTitleProducer
    {
        public static bool Evaluate(LiveTimelineKeyTitleDataList keys, int frame,
            TimelinePlayerMode playMode, ILiveTitleTarget controller)
        {
            if (keys == null || controller == null) return false;
            if (keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable)) return false;
            if (!keys.EnablePlayModeTimeline(playMode)) return false;
            LiveTimelineControl.FindTimelineKey(out var current, out _, keys, frame);
            if (!(current is LiveTimelineKeyTitleData key))
            {
                // Native jumps to the Disappear branch when no current key exists.
                if (!controller.IsDisappear) controller.Disappear();
                return false;
            }
            float duration = key._actionFrame / 60f;
            float elapsed = (frame - key.frame) / 60f;
            switch ((int)key._actionType)
            {
                case 0:
                    if (!controller.IsDisappear) controller.Disappear();
                    break;
                case 1:
                    controller.FadeIn(duration, elapsed);
                    break;
                case 2:
                    controller.FadeOut(duration, elapsed);
                    break;
            }
            return true;
        }
    }
}
