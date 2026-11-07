using Gallop.Live.Cutt;
using UnityEngine;

namespace Gallop.Live
{
    // Normal nonrandom LiveFaceController parent and SetEar equality gate.
    // Random animation scheduling/locator weights are a separate owner.
    public sealed class LiveEarControllerState
    {
        public LiveEarWeightTransition Weights { get; } = new LiveEarWeightTransition();
        public int Left { get; private set; }
        public int Right { get; private set; }
        public float Weight { get; private set; }
        public float Duration { get; private set; }
        public DrivenKeyComponent.InterpolateType Interpolate { get; private set; }
        public int Submissions { get; private set; }
        public bool RandomMotion { get; private set; }
        bool previousRandomMotion;
        int previousLeft, previousRight;
        float previousWeight, previousDuration;
        bool forceSubmission;
        public void Invalidate()=>forceSubmission=true;
        DrivenKeyComponent.InterpolateType previousInterpolate;
        public void SetRandomMotion(bool enabled) { RandomMotion = enabled; }

        public bool Update(LiveTimelineKeyFacialFaceData face, LiveTimelineKeyFacialEarData ear)
        {
            bool activeFace = LiveFacialPartsResolver.IsActive(face);
            bool random = ear != null && ear.useEarRandomMotion;
            if (activeFace && !random && LiveFacialIdTable.TryGet(face.facialId, out var faceEntry))
            {
                Left = Right = faceEntry[0]; Weight = Mathf.Clamp01(face.weight * 0.01f);
                float duration = 0f; // Native face branch uses a fresh local duration.
                LiveFacialInterpolation.UpdateDuration(face.speed, face.time, ref duration); Duration = duration;
                Interpolate = face.interpolateType;
            }
            if (LiveFacialPartsResolver.IsActive(ear) && !random && (!activeFace || ear.frame >= face.frame))
            {
                bool resolved;
                if (((int)ear.attribute & 0x20000) != 0)
                { Left = ear.facialEarIdL; Right = ear.facialEarIdR; resolved = true; }
                else if (face != null && LiveFacialIdTable.TryGet(face.facialId, out var entry))
                { Left = Right = entry[0]; resolved = true; }
                else resolved = false;
                if (resolved)
                {
                    Weight = Mathf.Clamp01(ear.weight * 0.01f);
                    float duration = Duration;
                    LiveFacialInterpolation.UpdateDuration(ear.speed, ear.time, ref duration); Duration = duration;
                    // The ear branch does not assign _earInterpolateType.
                }
            }
            if (!forceSubmission&&Left == previousLeft && Right == previousRight && Weight == previousWeight &&
                Duration == previousDuration && Interpolate == previousInterpolate && RandomMotion == previousRandomMotion) return false;
            forceSubmission=false;
            // Native SetEar submits both sides, left first, even when only one
            // differs, and passes force=true to the original snapshot override.
            if (!RandomMotion) { Weights.Set(Left, 1, Weight, Duration); Weights.Set(Right, 0, Weight, Duration); Submissions++; }
            previousLeft = Left; previousRight = Right; previousWeight = Weight;
            previousDuration = Duration; previousInterpolate = Interpolate; previousRandomMotion = RandomMotion;
            return !RandomMotion;
        }

        public void Advance(float delta) { Weights.Advance(delta, Interpolate); }
    }
}
