using System;
using Gallop;

namespace Gallop.Live
{
    // Step -> face application mapping decoded from the original:
    //   UpdateFaceEye 0x518e8f0 switches on [this+0x38]
    //   step 0/3 -> SetFaceEyeBlink(EyeHalfA, EyeHalfA, duration)
    //   step 1   -> SetFaceEyeBlink(EyeClose, EyeClose, duration)
    //   step 2   -> nothing
    //   step 4   -> stored face parts (SetFaceEyeType) or SetLastAnimEyeFace
    // The FaceType instances are reduced to stable ids here: 1 = EyeHalfA,
    // 2 = EyeClose (labels from Gallop.FaceType statics +0x18 / +0x20).
    public sealed class LiveBlinkDriver
    {
        // Facial ids from LiveTimelineDefine.FacialPartsSet: 1004 = eye parts 1/1
        // (half close), 1005 = eye parts 2/2 (full close).
        public const int EyeHalfA = 1004;
        public const int EyeClose = 1005;

        public EyeBlinkStateMachine Machine = new EyeBlinkStateMachine();
        public LiveDrivenKeyComponent Component;
        public LiveFaceWeightGroup FaceWeight;
        public int[] EyeRightGroups = { 2 };
        public int[] EyeLeftGroups = { 3 };
        // step 4 stored face parts branch (original [this+0x70] switch)
        public FaceParts[] StoredEyeR;
        public FaceParts[] StoredEyeL;
        public int EyeFaceState;

        // Timing port of the executed state machine (blink-state-001):
        // each entered step applies its face and waits GetWaitTime(step).
        public float Remaining = 0f;
        public bool ForceBlink;
        public bool HasSetting = true;
        public float ForceEyeCloseTime = 0.1f;
        public float BaseDuration = 0.8f;
        public float IntervalMin = 3f;
        public float IntervalMax = 5f;
        public Func<float> Random01 = () => 0f;

        public void ResetToIdle()
        {
            Machine.Step = 5;
            Remaining = Machine.WaitTime(5, ForceBlink, HasSetting, ForceEyeCloseTime,
                                         IntervalMin, IntervalMax, BaseDuration, Random01());
        }

        public void Advance(float deltaTime, bool isDoubleBlink = false)
        {
            Remaining -= deltaTime;
            while (Remaining <= 0f)
            {
                Machine.Step = Machine.NextStep(isDoubleBlink);
                ApplyStep(Machine.Step, ForceEyeCloseTime);
                Remaining += Machine.WaitTime(Machine.Step, ForceBlink, HasSetting, ForceEyeCloseTime,
                                              IntervalMin, IntervalMax, BaseDuration, Random01());
            }
        }

        public void ApplyStep(int step, float durationTime)
        {
            switch (step)
            {
                case 0:
                case 3:
                    ApplyFace(EyeHalfA, durationTime);
                    break;
                case 1:
                    ApplyFace(EyeClose, durationTime);
                    break;
                case 2:
                    break;
                case 4:
                    if (EyeFaceState == 0)
                    {
                        Component?.SetFaceEyeType(StoredEyeR, 2, durationTime, true, false, 1);
                        Component?.SetFaceEyeType(StoredEyeL, 3, durationTime, true, false, 1);
                    }
                    else
                    {
                        Component?.SetLastAnimEyeFace(FaceWeight, durationTime);
                    }
                    break;
                case 5:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(step));
            }
        }

        void ApplyFace(int faceType, float durationTime)
        {
            if (Component == null)
            {
                return;
            }
            Component.SetFaceEyeBlink(faceType, 1f, EyeRightGroups, durationTime);
            Component.SetFaceEyeBlink(faceType, 1f, EyeLeftGroups, durationTime);
        }
    }
}
