using System.Collections.Generic;
using Gallop.Live;
using Gallop.Live.Cutt;

namespace Gallop
{
    public partial class FaceDrivenKeyTarget
    {
        bool _deferLiveFacialWrites;
        int _pendingLiveFacialWrites;
        public void BeginLiveFacialSubmissions() => _deferLiveFacialWrites = true;
        public void EndLiveFacialSubmissions() => _deferLiveFacialWrites = false;
        bool DeferLiveFacialWrite(int group)
        {
            if (!_deferLiveFacialWrites)
                return false;
            _pendingLiveFacialWrites |= group;
            return true;
        }

        public float LiveEyePupilScale { get; private set; } = 1f;
        public LiveEarControllerState LiveEarState { get; private set; } = new LiveEarControllerState();

        void ApplyLiveEarWeights()
        {
            // Ordinary SetEar writes identical weights to each of the three
            // bones. The complete banks remain available for native I/O audit;
            // random/animation locator weights require their separate owner.
            for (int i = 0; i < leftEarMorph.Count; i++)
                leftEarMorph[i].weight = LiveEarState.Weights.ActiveTypes.Contains(i + 1) ? LiveEarState.Weights.Weights[(i + 1) * 6 + 3] : 0f;
            for (int i = 0; i < rightEarMorph.Count; i++)
                rightEarMorph[i].weight = LiveEarState.Weights.ActiveTypes.Contains(i + 1) ? LiveEarState.Weights.Weights[(i + 1) * 6] : 0f;
            if (BaseLEarMorph != null && BaseREarMorph != null)
                ChangeMorphEar();
        }

        sealed class LivePartState
        {
            public LiveFacialWeightTransition Weights;
            public float[] Target;
            public float LastTime;
        }

        readonly Dictionary<List<FacialMorph>, LivePartState> livePartStates = new Dictionary<List<FacialMorph>, LivePartState>();
        sealed class LiveGroupSubmission
        {
            public FaceParts[] First, Second;
            public float Duration, SubmittedDuration, LastTime;
            public DrivenKeyComponent.InterpolateType Interpolate;
        }

        readonly Dictionary<LiveFacialPartGroup, LiveGroupSubmission> liveGroupSubmissions = new Dictionary<LiveFacialPartGroup, LiveGroupSubmission>();
        static bool SameLiveParts(FaceParts[] cached, LiveFacialParts parts)
        {
            if (cached == null || cached.Length != parts.Count)
                return false;
            for (int i = 0; i < cached.Length; i++)
                if (cached[i]._faceParts != parts[i]._faceParts || !Gallop.Math.IsFloatEqualLight(cached[i]._weight, parts[i]._weight))
                    return false;
            return true;
        }

        static FaceParts[] CopyLiveParts(LiveFacialParts parts)
        {
            var result = new FaceParts[parts.Count];
            for (int i = 0; i < result.Length; i++)
                result[i] = parts[i];
            return result;
        }

        bool ApplyLiveFacialGroupWeights(LiveTimelineKey current, LiveFacialPartGroup group, float liveTime)
        {
            if (group == LiveFacialPartGroup.Eye && Container.LiveEyeBlink?.IsBlinking == true)
                return false;
            if (!LiveFacialPartsResolver.TryResolve(current, group, true, out var first))
                return false;
            LiveFacialParts second = default;
            if (group != LiveFacialPartGroup.Mouth && !LiveFacialPartsResolver.TryResolve(current, group, false, out second))
                return false;
            if (!liveGroupSubmissions.TryGetValue(group, out var submission) || liveTime < submission.LastTime)
            {
                submission = new LiveGroupSubmission();
                liveGroupSubmissions[group] = submission;
            }

            int speed, time;
            DrivenKeyComponent.InterpolateType interpolate;
            if (current is LiveTimelineKeyFacialFaceData face)
            {
                speed = face.speed;
                time = face.time;
                interpolate = face.interpolateType;
            }
            else if (current is LiveTimelineKeyFacialEyeData eye)
            {
                speed = eye.speed;
                time = eye.time;
                interpolate = eye.interpolateType;
            }
            else if (current is LiveTimelineKeyFacialEyebrowData brow)
            {
                speed = brow.speed;
                time = brow.time;
                interpolate = brow.interpolateType;
            }
            else if (current is LiveTimelineKeyLipSyncData lip)
            {
                speed = lip.speed;
                time = lip.time;
                interpolate = lip.interpolateType;
            }
            else
            {
                var mouth = (LiveTimelineKeyFacialMouthData)current;
                speed = mouth.speed;
                time = mouth.time;
                interpolate = mouth.interpolateType;
            }

            // Ordinary Director callback initializes its duration local to0
            // before original UpdateDurationTime, unlike persistent facial groups.
            if (current is LiveTimelineKeyLipSyncData)
                submission.Duration = 0f;
            LiveFacialInterpolation.UpdateDuration(speed, time, ref submission.Duration);
            if (group == LiveFacialPartGroup.Mouth)
            {
                SubmitSharedLiveMouth(CopyLiveParts(first), submission.Duration, interpolate, false);
                submission.LastTime = liveTime;
                return true;
            }

            // Original SetEye/SetEyebrow compares both ordered arrays. A change
            // on either side resubmits both, including an unchanged side which
            // must snapshot its current in-flight weight and restart its clock.
            bool sameDuration = group == LiveFacialPartGroup.Eye ? Gallop.Math.IsFloatEqualLight(submission.Duration, submission.SubmittedDuration) : submission.Duration == submission.SubmittedDuration;
            bool submit = !SameLiveParts(submission.First, first) || !SameLiveParts(submission.Second, second) || !sameDuration || submission.Interpolate != interpolate;
            if (submit)
            {
                submission.First = CopyLiveParts(first);
                submission.Second = CopyLiveParts(second);
                submission.SubmittedDuration = submission.Duration;
                submission.Interpolate = interpolate;
                if (group == LiveFacialPartGroup.Eye && Container.LiveEyeBlink != null)
                {
                    Container.LiveEyeBlink.Owner.StoredLeft = submission.First;
                    Container.LiveEyeBlink.Owner.StoredRight = submission.Second;
                }
            }

            submission.LastTime = liveTime;
            if (group == LiveFacialPartGroup.Mouth)
                ApplyLiveFacialPartWeights(MouthMorphs, first, current, liveTime, submit, submission.Duration, interpolate);
            else
            {
                ApplyLiveFacialPartWeights(group == LiveFacialPartGroup.Eye ? leftEyeMorph : leftEyebrowMorph, first, current, liveTime, submit, submission.Duration, interpolate);
                ApplyLiveFacialPartWeights(group == LiveFacialPartGroup.Eye ? rightEyeMorph : rightEyebrowMorph, second, current, liveTime, submit, submission.Duration, interpolate);
            }

            return true;
        }

#if UNITY_EDITOR
        // Read-only capture attribution: the actual callback input, not a
        // second guessed selector based on capture ordinal or actor ordering.
        public FacialDataUpdateInfo LastLiveFacialInput { get; private set; }
        public float LastLiveFacialTime { get; private set; }

        void ObserveLiveFacialInput(in FacialDataUpdateInfo input, float time)
        {
            LastLiveFacialInput = input;
            LastLiveFacialTime = time;
        }

#endif
        // LiveFaceController selects explicit parts (eye 0x40000, other 0x20000), or
        // converts facialId through the executed native static table. A NOP
        // key must not clear a pose owned by the face key/LipSync.
        void ApplyLiveFacialPartWeights(List<FacialMorph> targets, LiveFacialParts cur, LiveTimelineKey current, float liveTime, bool submit, float duration, DrivenKeyComponent.InterpolateType interpolate)
        {
            if (!livePartStates.TryGetValue(targets, out var state) || liveTime < state.LastTime)
            {
                state = new LivePartState
                {
                    Weights = new LiveFacialWeightTransition(targets.Count),
                    Target = new float[targets.Count],
                    LastTime = (float)current.FrameSecond
                };
                livePartStates[targets] = state;
            }

            System.Array.Clear(state.Target, 0, state.Target.Length);
            for (int i = 0; i < cur.Count; i++)
            {
                var part = cur[i];
                if (part._faceParts == 0)
                    continue;
                state.Target[part._faceParts - 1] = part._weight;
            }

            for (int i = 0; i < targets.Count; i++)
                state.Weights.Current[i] = targets[i].weight;
            if (submit)
                state.Weights.SetTarget(state.Target, duration, interpolate, true);
            state.LastTime = liveTime;
            for (int i = 0; i < targets.Count; i++)
                targets[i].weight = state.Weights.Current[i];
        }

        // Original LiveFaceController parent handles faceCur eyebrows, and
        // UpdateEye selects faceCur unless an active eye key is at least as new.
        // Mouth is a separate source; a Face key must not overwrite AutoLip.
        void ApplyLiveFullFace(FacialDataUpdateInfo info, float liveTime)
        {
            var key = info.faceCur;
            if (!LiveFacialPartsResolver.IsActive(key))
                return;
            if (!LivePartOwnsGroup(info.eyeCur, key))
            {
                LiveEyePupilScale = ((int)key.attribute & 0x100000) == 0 ? 1f : 0f;
                bool changed = ApplyLiveFacialGroupWeights(key, LiveFacialPartGroup.Eye, liveTime);
                if (changed)
                    ChangeMorphEye();
            }

            if (!LivePartOwnsGroup(info.eyebrowCur, key))
            {
                bool changed = ApplyLiveFacialGroupWeights(key, LiveFacialPartGroup.Eyebrow, liveTime);
                if (changed)
                    ChangeMorphEyebrow();
            }
        }

        static bool LivePartOwnsGroup(LiveTimelineKey part, LiveTimelineKey face) => LiveFacialPartsResolver.IsActive(part) && (!LiveFacialPartsResolver.IsActive(face) || part.frame >= face.frame);
        // FacialUpdater.PostJob forwards the model delta to DrivenKey.PostJob
        // after timeline submissions, including the frame receiving a new key.
        // Manual Evaluate forwards zero. All groups advance once here; authored
        // absolute key time must not add a second, independent interpolation step.
        public void AdvanceLiveFacialWeights(float elapsedTime)
        {
            {
                if (Container.LiveModelComponentsPaused)
                    return;
                _deferLiveFacialWrites = true;
                Container.LiveEyeBlink?.BeforeApplyDrivenKey(elapsedTime);
                Container.LiveSystemTextDrivenKey?.Tick(elapsedTime, this);
                bool eye = false, brow = false, mouth = false;
                if (livePatternMouth.HasTarget)
                {
                    livePatternMouth.Advance(elapsedTime);
                    CopySharedLiveMouth();
                    mouth = true;
                }

                foreach (var pair in livePartStates)
                {
                    var state = pair.Value;
                    state.Weights.Advance(elapsedTime);
                    for (int i = 0; i < pair.Key.Count; i++)
                        pair.Key[i].weight = state.Weights.Current[i];
                    eye |= pair.Key == leftEyeMorph || pair.Key == rightEyeMorph;
                    brow |= pair.Key == leftEyebrowMorph || pair.Key == rightEyebrowMorph;
                    mouth |= pair.Key == MouthMorphs;
                }

                _deferLiveFacialWrites = false;
                eye |= (_pendingLiveFacialWrites & 1) != 0;
                brow |= (_pendingLiveFacialWrites & 2) != 0;
                mouth |= (_pendingLiveFacialWrites & 4) != 0;
                _pendingLiveFacialWrites = 0;
                if (eye)
                {
                    ChangeMorphEye();
                    ObserveSystemTextPose("eye");
                }

                if (brow)
                {
                    ChangeMorphEyebrow();
                    ObserveSystemTextPose("eyebrow");
                }

                if (mouth)
                {
                    ChangeMorphMouth();
                    ObserveSystemTextPose("mouth");
                }

                if (Container.LiveSystemTextFace?.Active == true)
                    Container.LiveSystemTextEar?.Tick(elapsedTime, LiveEarState.Weights);
                else
                    Container.LiveRandomEar?.Tick(elapsedTime);
                LiveEarState.Advance(elapsedTime);
                ApplyLiveEarWeights();
                ObserveSystemTextPose("ear");
                if (Container.LiveFacePartsScale != null)
                {
                    liveGroupSubmissions.TryGetValue(LiveFacialPartGroup.Eye, out var eyeSubmission);
                    bool BaseEye(FaceParts[] parts)
                    {
                        if (parts != null)
                            foreach (var part in parts)
                                if (part._faceParts != 0)
                                    return false;
                        return true;
                    }

                    Container.LiveFacePartsScale.AfterApplyDrivenKey(BaseEye(eyeSubmission?.First), BaseEye(eyeSubmission?.Second));
                }

#if UNITY_EDITOR
                if (Container.LiveSystemTextFace?.Active == true)
                    ;
#endif
            }
        }

        public void ApplyLiveBlinkStep(int step, float duration)
        {
            var owner = Container.LiveEyeBlink?.Owner;
            if (owner == null)
                return;
            if (step != 0 && step != 1 && step != 3 && step != 4)
                return;
            liveGroupSubmissions.TryGetValue(LiveFacialPartGroup.Eye, out var submission);
            var interpolate = submission?.Interpolate ?? DrivenKeyComponent.InterpolateType.Linear;
            FaceParts[] blink = new[]
            {
                new FaceParts
                {
                    _faceParts = step == 1 ? 2 : 1,
                    _weight = 1
                }
            };
            void Apply(List<FacialMorph> targets, FaceParts[] parts)
            {
                if (parts == null)
                    return;
                if (!livePartStates.TryGetValue(targets, out var state))
                {
                    state = new LivePartState
                    {
                        Weights = new LiveFacialWeightTransition(targets.Count),
                        Target = new float[targets.Count]
                    };
                    livePartStates.Add(targets, state);
                }

                System.Array.Clear(state.Target, 0, state.Target.Length);
                foreach (var part in parts)
                    if (part._faceParts > 0)
                        state.Target[part._faceParts - 1] = part._weight;
                for (int i = 0; i < targets.Count; i++)
                    state.Weights.Current[i] = targets[i].weight;
                state.Weights.SetTarget(state.Target, duration, interpolate, true);
            }

            Apply(rightEyeMorph, step == 4 ? owner.StoredRight : blink);
            Apply(leftEyeMorph, step == 4 ? owner.StoredLeft : blink);
        }
    }
}
