using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live
{
    // Port of DrivenKeyComponent.FaceWeightGroup.SetFace (original 0x4aca9e0) and
    // SetPrevWeight (original 0x4ad3bb0 / 0x4ad3d50). The weight arrays and group
    // layout mirror the original; GetFaceTypes, GetOverrideFaceParts and the
    // AddBlendFace sink are injected so the component side can wire them later.
    public sealed class LiveFaceWeightGroup
    {
        public struct WeightGroup
        {
            public int StartIndex;
            public int Length;
        }

        public float[] CommonWeight = Array.Empty<float>();
        public WeightGroup[] WeightGroups = Array.Empty<WeightGroup>();
        public WeightGroup[] TargetWeightGroups = Array.Empty<WeightGroup>();
        // original +0x40 PrevWeightArray: SetPrevWeight copies into these slots,
        // not into TargetWeightGroups.
        public WeightGroup[] PrevWeightGroups = Array.Empty<WeightGroup>();
        public float[] DurationTime = Array.Empty<float>();
        public float[] CurrentTime = Array.Empty<float>();
        public int GroupOffset;
        public int GroupCount;
        public bool DirtyFlag;
        // AddBlendFace sink (original 0x4ad0e60): a fixed array whose last entry is
        // the -1 sentinel, plus the running face-type count at [this+0x70].
        public int[] BlendFaces = Array.Empty<int>();
        public int FaceTypeCount;

        // FaceTypeSet mirrors the original class: +0x10 int[] types, +0x18 count.
        public sealed class FaceTypeSet
        {
            public int[] Types = Array.Empty<int>();
            public int Count;
        }

        // Original GetFaceTypes 0x4ad1160: copies the blend-face list (or its tail
        // when removeBase is true) into the FaceTypeSet and stores the count.
        public void GetFaceTypes(FaceTypeSet faceTypeSet, bool removeBase = false)
        {
            if (faceTypeSet == null)
            {
                return;
            }
            int count = removeBase ? FaceTypeCount - 1 : FaceTypeCount;
            faceTypeSet.Count = count;
            if (faceTypeSet.Types == null)
            {
                return;
            }
            int sourceIndex = removeBase ? 1 : 0;
            for (int i = 0; i < count && sourceIndex + i < BlendFaces.Length && i < faceTypeSet.Types.Length; i++)
            {
                faceTypeSet.Types[i] = BlendFaces[sourceIndex + i];
            }
        }

        public void AddBlendFace(int face)
        {
            if (BlendFaces == null || BlendFaces.Length < 2)
            {
                return;
            }
            int capacity = BlendFaces.Length - 1;
            for (int i = 0; i < capacity; i++)
            {
                if (BlendFaces[i] == face)
                {
                    return;
                }
                if (BlendFaces[i] == -1)
                {
                    BlendFaces[i] = face;
                    BlendFaces[i + 1] = -1;
                    FaceTypeCount++;
                    return;
                }
            }
        }

        // Original SetPrevWeight(int, bool): copy WeightGroup-indexed values to the
        // PrevWeightGroup positions when forced, at time zero, or at the duration.
        public void SetPrevWeight(int group, bool force)
        {
            if (group < 0 || group >= CurrentTime.Length || group >= DurationTime.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(group));
            }
            bool due = CurrentTime[group] == 0f || CurrentTime[group] >= DurationTime[group];
            if (!(force || due))
            {
                return;
            }
            for (int k = 0; k < WeightGroups.Length && k < TargetWeightGroups.Length && k < PrevWeightGroups.Length; k++)
            {
                int source = WeightGroups[k].StartIndex + group;
                int destination = PrevWeightGroups[k].StartIndex + group;
                if (destination >= 0 && destination < CommonWeight.Length && source >= 0 && source < CommonWeight.Length)
                {
                    CommonWeight[destination] = CommonWeight[source];
                }
            }
        }

        // Original SetFace. groupTypeArray holds the active FaceGroupType values;
        // AddBlendFace receives the FaceParts index when a weight is positive.
        public void SetFace(FaceParts[] faceParts, float weight, int[] groupTypeArray, float durationTime,
                            bool isBlend, bool dontClearFaces, int targetGroup,
                            Action<int, int> addBlendFace, Func<int, FaceParts[]> getFaceTypes = null)
        {
            if (groupTypeArray == null)
            {
                return;
            }
            int group = targetGroup - GroupOffset;
            bool isInGroup = Array.IndexOf(groupTypeArray, targetGroup) >= 0;
            if (isInGroup && group >= 0 && group < DurationTime.Length && group < CurrentTime.Length)
            {
                DurationTime[group] = durationTime;
                CurrentTime[group] = 0f;
            }
            bool shouldClear = durationTime <= 0f && !isBlend;
            if (DirtyFlag && !dontClearFaces)
            {
                DirtyFlag = false;
            }
            if (shouldClear)
            {
                for (int k = 1; k < GroupCount; k++)
                {
                    if (k >= WeightGroups.Length)
                    {
                        break;
                    }
                    int index = WeightGroups[k].StartIndex + group;
                    if (index >= 0 && index < CommonWeight.Length)
                    {
                        CommonWeight[index] = 0f;
                    }
                }
                if (faceParts != null)
                {
                    for (int i = 0; i < faceParts.Length; i++)
                    {
                        int start = faceParts[i]._faceParts;
                        if (start <= 0)
                        {
                            continue;
                        }
                        int target = start < WeightGroups.Length ? WeightGroups[start].StartIndex + group : -1;
                        if (target >= 0 && target < CommonWeight.Length)
                        {
                            CommonWeight[target] = faceParts[i]._weight * weight;
                        }
                        if (weight > 0f)
                        {
                            addBlendFace?.Invoke(start, 0);
                        }
                    }
                }
            }
            else
            {
                if (faceParts != null)
                {
                    for (int i = 0; i < faceParts.Length; i++)
                    {
                        int start = faceParts[i]._faceParts;
                        if (start <= 0)
                        {
                            continue;
                        }
                        if (weight > 0f)
                        {
                            addBlendFace?.Invoke(start, 0);
                        }
                        if (isInGroup && start < TargetWeightGroups.Length)
                        {
                            int target = TargetWeightGroups[start].StartIndex + group;
                            if (target >= 0 && target < CommonWeight.Length)
                            {
                                CommonWeight[target] = faceParts[i]._weight * weight;
                            }
                        }
                    }
                }
            }
            getFaceTypes?.Invoke(targetGroup);
        }
    }
}
