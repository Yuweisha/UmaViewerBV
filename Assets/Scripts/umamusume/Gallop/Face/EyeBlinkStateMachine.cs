using System;

namespace Gallop
{
    // Managed timing state machine for auto blink. Step values and wait times are
    // taken from the executed original (live61-io/blink-state-001):
    //   0 -> 1 -> 2 -> 3 -> 4 -> (double ? 0 : 5), 5 -> 0
    //   wait(step2) = 0.03 + (force && setting ? ForceEyeCloseTime : 0)
    //   wait(step5) = Random.Range(intervalMin, intervalMax)
    //   other steps = 0
    // Every wait is finally increased by the caller supplied base duration.
    // BlinkStep / DrivenKeyComponent consumers are not wired by this class.
    public sealed class EyeBlinkStateMachine
    {
        public const float DefaultWaitTime = 0.03f;

        public EyeBlinkStateMachine()
        {
            Step = 5;
        }

        public int Step { get; set; }

        public int NextStep(bool isDoubleBlink)
        {
            switch (Step)
            {
                case 0: return 1;
                case 1: return 2;
                case 2: return 3;
                case 3: return 4;
                case 4: return isDoubleBlink ? 0 : 5;
                case 5: return 0;
                default: throw new ArgumentOutOfRangeException(nameof(Step));
            }
        }

        public float WaitTime(int step, bool forceBlink, bool hasSetting, float forceEyeCloseTime,
                              float intervalMin, float intervalMax, float baseDuration, float random01)
        {
            float wait;
            switch (step)
            {
                case 2:
                    wait = DefaultWaitTime;
                    if (forceBlink && hasSetting) wait += forceEyeCloseTime;
                    break;
                case 5:
                    wait = intervalMin + (intervalMax - intervalMin) * random01;
                    break;
                default:
                    wait = 0f;
                    break;
            }
            return wait + baseDuration;
        }
    }
}
