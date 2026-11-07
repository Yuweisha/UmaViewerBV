using System;
using System.Collections.Generic;
using Gallop.Live;

namespace Gallop
{
    public partial class FaceDrivenKeyTarget
    {
        public bool OrdinaryLiveLipSyncEnabled = true;
#if UNITY_EDITOR
        public IReadOnlyList<FacialMorph> EditorLiveSystemTextGroup(int group) => group == 2 ? rightEyeMorph : group == 3 ? leftEyeMorph : group == 4 ? rightEyebrowMorph : group == 5 ? leftEyebrowMorph : MouthMorphs;
#endif
        void ObserveSystemTextPose(string group)
        {
#if UNITY_EDITOR
            if (Container.LiveSystemTextFace?.Active == true)
                ;
#endif
        }

        public void ApplyLiveSystemTextAnimation(DrivenKeyAnimation animation, IReadOnlyDictionary<string, UnityEngine.Transform> nodes)
        {
            var groups = new[]
            {
                rightEyeMorph,
                leftEyeMorph,
                rightEyebrowMorph,
                leftEyebrowMorph,
                MouthMorphs
            };
            for (int group = 0; group < groups.Length; group++)
            {
                var morphs = groups[group];
                foreach (var morph in morphs)
                    morph.weight = 0f;
                var types = animation.animationFaceType[group];
                for (int i = 0; i < types.faceType.Length; i++)
                {
                    int id = types.faceType[i];
                    if (id == 0)
                        continue;
                    var morph = morphs.Find(m => m.index == id);
                    if (morph?.locator != null && nodes.TryGetValue(morph.locator.name, out var node))
                        morph.weight = -100f * node.localPosition.x;
                }
            }

            ChangeMorphEye();
            ChangeMorphEyebrow();
            ChangeMorphMouth();
        }

        public void SubmitLiveSystemTextFace(int group, FaceParts[] parts, float duration)
        {
            if (group == 6)
            {
                SubmitLiveSystemTextMouth(parts, 1f, duration);
                return;
            }

            var targets = group == 2 ? rightEyeMorph : group == 3 ? leftEyeMorph : group == 4 ? rightEyebrowMorph : leftEyebrowMorph;
            if (!livePartStates.TryGetValue(targets, out var state))
            {
                state = new LivePartState
                {
                    Weights = new LiveFacialWeightTransition(targets.Count),
                    Target = new float[targets.Count]
                };
                livePartStates.Add(targets, state);
            }

            bool comic = false;
            if (group == 2 || group == 3)
            {
                foreach (var part in parts)
                    if ((part._faceParts == 16 || part._faceParts == 22) && part._weight > 0f)
                        comic = true;
                if (targets.Count >= 22 && (targets[15].weight > 0f || targets[21].weight > 0f))
                    comic = true;
            }

            if (comic)
                duration = 0f;
            Array.Clear(state.Target, 0, state.Target.Length);
            foreach (var part in parts)
                if (part._faceParts > 0)
                    state.Target[part._faceParts - 1] = part._weight;
            for (int i = 0; i < targets.Count; i++)
                state.Weights.Current[i] = targets[i].weight;
            state.Weights.SetTarget(state.Target, duration, state.Weights.Interpolate, true);
            for (int i = 0; i < targets.Count; i++)
                targets[i].weight = state.Weights.Current[i];
            var blink = Container.LiveEyeBlink?.Owner;
            if (blink != null)
            {
                if (group == 2)
                    blink.StoredRight = parts;
                else if (group == 3)
                    blink.StoredLeft = parts;
            }
        }

        public void SetLiveSystemTextCheek(bool enabled, int type)
        {
            if (CheekMorph == null)
                return;
            for (int i = 0; i < CheekMorph.BindProperties.Count; i++)
                CheekMorph.BindProperties[i].Value = enabled && i == type - 1 ? 1f : 0f;
            ChangeMorphEffect();
        }

        public void RestoreLiveSystemTextSources()
        {
            // StopSystemText sets each native force flag so unchanged ordinary
            // keys resubmit on the following callback.
            liveGroupSubmissions.Clear();
            livePatternMouth.Invalidate();
            LiveEarState.Invalidate();
        }
    }
}
