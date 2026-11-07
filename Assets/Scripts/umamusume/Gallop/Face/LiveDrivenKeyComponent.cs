using System;
using System.Collections.Generic;
using Gallop;

namespace Gallop.Live
{
    // Port of the DrivenKeyComponent blink entry (original SetFaceEyeBlink 0x4ac9f80).
    // The original applies five SetFace calls in this order:
    //   faceWeight[0] with eyeR parts  (targetGroup 2)
    //   faceWeight[1] with eyeL parts  (targetGroup 3)
    //   faceWeight[1] with eyebrowR    (targetGroup 4)
    //   faceWeight[1] with eyebrowL    (targetGroup 5)
    //   faceWeight[2] with mouth       (targetGroup 6)
    // all with isBlend=true, dontClearFaces=false, and the shared weight/duration.
    public sealed class LiveDrivenKeyComponent
    {
        public bool Enabled = true;
        public LiveFaceWeightGroup[] FaceWeights = Array.Empty<LiveFaceWeightGroup>();
        public Func<int, FacePartsSet> GetFacePartsSet;
        public int[] GroupTypes = Array.Empty<int>();
        public List<string> Trace = new List<string>();

        public void SetFaceEyeBlink(int faceType, float weight, int[] groupTypeArray, float durationTime)
        {
            if (!Enabled || groupTypeArray == null)
            {
                return;
            }
            var parts = GetFacePartsSet?.Invoke(faceType);
            if (parts == null)
            {
                return;
            }
            Trace.Add("blink " + faceType + " [" + string.Join(",", groupTypeArray) + "]");
            Apply(0, parts._eyeR, 2, weight, groupTypeArray, durationTime);
            Apply(1, parts._eyeL, 3, weight, groupTypeArray, durationTime);
            Apply(1, parts._eyebrowR, 4, weight, groupTypeArray, durationTime);
            Apply(1, parts._eyebrowL, 5, weight, groupTypeArray, durationTime);
            Apply(2, parts._mouth, 6, weight, groupTypeArray, durationTime);
        }

        void Apply(int faceWeightIndex, FaceParts[] parts, int targetGroup, float weight,
                   int[] groupTypeArray, float durationTime)
        {
            if (faceWeightIndex < 0 || faceWeightIndex >= FaceWeights.Length || FaceWeights[faceWeightIndex] == null)
            {
                return;
            }
            var faceWeightGroup = FaceWeights[faceWeightIndex];
            faceWeightGroup.SetFace(parts, weight, groupTypeArray, durationTime,
                                    true, false, targetGroup,
                                    (start, flag) => faceWeightGroup.AddBlendFace(start));
        }

        // Original SetLastAnimEyeFace 0x4acb0e0: resets the face timing arrays and
        // commits the previous weights for groups 2 and 3 when a duration is given.
        public void SetLastAnimEyeFace(LiveFaceWeightGroup faceWeight, float durationTime)
        {
            Trace.Add("lastAnim " + durationTime);
            if (faceWeight == null)
            {
                return;
            }
            for (int i = 0; i < faceWeight.CurrentTime.Length && i < faceWeight.DurationTime.Length; i++)
            {
                faceWeight.CurrentTime[i] = 0f;
                faceWeight.DurationTime[i] = durationTime;
            }
            if (durationTime > 0f)
            {
                faceWeight.SetPrevWeight(2 - faceWeight.GroupOffset, false);
                faceWeight.SetPrevWeight(3 - faceWeight.GroupOffset, false);
            }
        }

        // Original SetFaceEyeType 0x4aca180: SetFace(facePartsArray, targetGroup, ...)
        // then clears the face data flag at +0xd0. The face-data side is not part of
        // this port yet, so the flag reset is left to the caller.
        public void SetFaceEyeType(FaceParts[] facePartsArray, int targetGroup, float durationTime,
                                   bool isBlend, bool dontClearFaces, int faceWeightIndex)
        {
            if (!Enabled || faceWeightIndex < 0 || faceWeightIndex >= FaceWeights.Length)
            {
                return;
            }
            Trace.Add("eyeType [" + targetGroup + "]");
            var faceWeightGroup = FaceWeights[faceWeightIndex];
            faceWeightGroup.SetFace(facePartsArray, 1f, GroupTypes, durationTime,
                                    isBlend, dontClearFaces, targetGroup,
                                    (start, flag) => faceWeightGroup.AddBlendFace(start));
        }
    }
}
