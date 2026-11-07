using UnityEngine;

namespace Gallop.Live.Cutt
{
    public struct VoiceSample
    {
        public bool HasCue;
        public int CueId;
        public float StartTime;
        public int KeyFrame;
    }

    // Original AlterUpdate_Voice 0x4ee7050 sampling part: list/play-mode gate,
    // key fetch, LiveVariationId override selection and start offset. The frame
    // dedupe state stays in the caller.
    // Frame-dedupe/stop state of AlterUpdate_Voice 0x4ee7050. A null key resets
    // the last frame to -1 and stops voice only once; repeating the same key frame
    // does not replay; a new key frame plays.
    public sealed class LiveVoiceScheduler
    {
        public enum VoiceAction { None = 0, Play = 1, Stop = 2 }
        private int _lastKeyFrame = -1;
        public int LastKeyFrame => _lastKeyFrame;

        public VoiceAction Update(in VoiceSample sample, out int cueId, out float startTime)
        {
            cueId = 0;
            startTime = 0f;
            if (!sample.HasCue)
            {
                if (_lastKeyFrame == -1) return VoiceAction.None;
                _lastKeyFrame = -1;
                return VoiceAction.Stop;
            }
            if (sample.KeyFrame == _lastKeyFrame) return VoiceAction.None;
            _lastKeyFrame = sample.KeyFrame;
            cueId = sample.CueId;
            startTime = sample.StartTime;
            return VoiceAction.Play;
        }

        public void Reset() => _lastKeyFrame = -1;
    }

    public static class LiveVoiceTimeline
    {
        public static VoiceSample Evaluate(ILiveTimelineKeyDataList keys, float frame, int variationId, float currentTime)
        {
            var none = new VoiceSample { HasCue = false, KeyFrame = -1 };
            if (keys == null || keys.Count == 0) return none;
            if (keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable)) return none;
            if (float.IsNaN(frame) || float.IsInfinity(frame)) return none;
            if (frame < keys.At(0).frame) return none;
            LiveTimelineControl.FindTimelineKey(out var current, out _, keys, frame);
            if (!(current is LiveTimelineKeyVoiceData key)) return none;
            int cueId = key.CueId;
            if (key.OverrideCueIdDataList != null)
            {
                for (int i = 0; i < key.OverrideCueIdDataList.Count; i++)
                {
                    var over = key.OverrideCueIdDataList[i];
                    if (over == null || over.Type != LiveTimelineKeyVoiceData.OverrideType.LiveVariationId) continue;
                    if (variationId == over.TargetId)
                    {
                        cueId = over.OverrideCueIdId;
                        break;
                    }
                }
            }
            return new VoiceSample
            {
                HasCue = true,
                CueId = cueId,
                StartTime = Mathf.Max(0f, currentTime - key.frame / 60f),
                KeyFrame = key.frame,
            };
        }
    }
}
