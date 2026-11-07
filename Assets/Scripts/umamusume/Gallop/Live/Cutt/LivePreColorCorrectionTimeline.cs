using Gallop.RenderPipeline;

namespace Gallop.Live.Cutt
{
    // Native SetupPreColorCorrectionUpdateInfo 0x4ef6c50: current key fills
    // enable/saturation/mode, seven curves, selective, two colors and the two
    // exclude flag words; a missing key yields disabled defaults (saturation 1,
    // linear curves, zero flags).
    public struct PreColorCorrectionUpdateInfo
    {
        public SimpleColorCorrectionPass.Parameter Parameter;
        public uint CharacterFlags;
        public uint ShadowFlags;
    }

    public static class LivePreColorCorrectionTimeline
    {
        public static PreColorCorrectionUpdateInfo Evaluate(
            System.Collections.Generic.List<LiveTimelinePreColorCorrectionData> groups,
            float frame, TimelinePlayerMode playMode)
        {
            if (groups == null || groups.Count != 1 || float.IsNaN(frame) || float.IsInfinity(frame))
                return Default();
            var keys = groups[0]?.keys;
            if (keys == null || keys.Count == 0 ||
                keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) ||
                !keys.EnablePlayModeTimeline(playMode) || frame < keys.At(0).frame)
                return Default();
            LiveTimelineControl.FindTimelineKey(out var current, out _, keys, frame);
            if (!(current is LiveTimelineKeyPreColorCorrectionData key)) return Default();
            return new PreColorCorrectionUpdateInfo
            {
                Parameter = new SimpleColorCorrectionPass.Parameter
                {
                    Enabled = key.enable != 0,
                    Red = key.redCurve,
                    Green = key.greenCurve,
                    Blue = key.blueCurve,
                    Saturation = key.saturation,
                    // Original main/pre-color correction callback event 604 (0x25c).
                    RenderPassEvent = (UnityEngine.Rendering.Universal.RenderPassEvent)604,
                },
                CharacterFlags = key.excludeColorCorrectionCharacterFlags,
                ShadowFlags = key.excludeColorCorrectionShadowFlags,
            };
        }

        static PreColorCorrectionUpdateInfo Default() => new PreColorCorrectionUpdateInfo
        {
            Parameter = new SimpleColorCorrectionPass.Parameter { Enabled = false, Saturation = 1f },
        };
    }
}
