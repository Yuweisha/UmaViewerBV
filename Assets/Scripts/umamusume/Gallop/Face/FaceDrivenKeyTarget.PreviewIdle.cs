using Gallop.Live;
using UnityEngine;

namespace Gallop
{
    public partial class FaceDrivenKeyTarget
    {
        float[] previewBlinkWeights;

        // The UI remains the owner of morph weights. Only the evaluated pose is
        // overlaid, so a panel edit during a blink is never restored to stale data.
        internal void ApplyPreviewIdleBlink(int eyeType)
        {
            if (eyeType < 0) { ChangeMorphEye(); return; }
            if (previewBlinkWeights == null || previewBlinkWeights.Length != EyeMorphs.Count)
                previewBlinkWeights = new float[EyeMorphs.Count];
            for (int i = 0; i < EyeMorphs.Count; i++)
                previewBlinkWeights[i] = EyeMorphs[i].weight;
            try
            {
                foreach (var morph in EyeMorphs)
                {
                    // Keep gaze, pupil size and eye visibility independent.
                    if (morph.index > 0 && morph.index < 20)
                        morph.weight = morph.index == eyeType ? 1f : 0f;
                }
                ChangeMorphEye();
            }
            finally
            {
                for (int i = 0; i < EyeMorphs.Count; i++)
                    EyeMorphs[i].weight = previewBlinkWeights[i];
            }
        }

        internal bool HasPreviewManualEar => EarMorphs.Exists(morph => Mathf.Abs(morph.weight) > 0.0001f);

        internal void ApplyPreviewIdleEar(LiveEarWeightTransition weights)
        {
            FacialResetEar();
            foreach (var morph in EarMorphs)
            {
                if (!weights.ActiveTypes.Contains(morph.index)) continue;
                for (int bone = 0; bone < Mathf.Min(morph.trsArray.Count, 3); bone++)
                {
                    var trs = morph.trsArray[bone];
                    if (trs.transform == null) continue;
                    // The authored ear animation has independent weights for
                    // each of the three bones, unlike the panel's scalar morph.
                    float weight = weights.Weights[morph.index * 6 + (morph.direction ? 3 : 0) + bone];
                    if (trs._isValidScaleTransform) trs.transform.localScale += trs._scale * weight;
                    trs.transform.localPosition += trs._position * weight;
                    RotationRecorder[trs.transform] += trs._rotation * weight;
                }
            }
            ApplyRotationEar();
        }
    }
}
