using UnityEngine;

namespace Gallop.Live
{
    // LiveFaceController.UpdateDurationTime 0x4e91860 and
    // DrivenKeyComponent.GetInterpolateRate 0x4ac4870.
    public static class LiveFacialInterpolation
    {
        const float FrameSeconds = 0.016666668f;
        const float HalfPi = 1.5707963705062866f;

        // Original CRT SSE2 sinf/cosf (450de0/4505c0), restricted to the
        // [0, HalfPi] arguments produced below. Mathf uses the host math
        // library; its last bit can differ at real saved transition times.
        // Preserve the original double polynomial operation order, then
        // round once to float. This is local to facial interpolation.
        static double SinPolynomial(double x)
        {
            double square = x * x;
            double fourth = square * square;
            double cubic = x * square;
            double high = 2.7557319223985893e-06 * square;
            high = high + -0.0001984126984126984;
            high = high * fourth;
            double low = 0.008333333333333333 * square;
            low = low + -0.16666666666666666;
            high = high + low;
            high = high * cubic;
            return x + high;
        }

        static double CosPolynomial(double x)
        {
            double square = x * x;
            double fourth = square * square;
            double high = -2.755731922398589e-07 * square;
            high = high + 2.4801587301587298e-05;
            high = high * fourth;
            double low = -0.0013888888888888887 * square;
            low = low + 0.041666666666666664;
            high = high + low;
            high = high * fourth;
            double first = -0.5 * square;
            first = first + 1.0;
            return first + high;
        }

        static double ReduceHalfPi(double x)
        {
            double head = x - 1.5707963267341256;
            double reduced = head - 6.077100506506192e-11;
            if (System.Math.Abs(reduced) < 0.000030517578125)
            {
                double next = head - 6.077100506303966e-11;
                double error = (head - next) - 6.077100506303966e-11;
                reduced = next - (2.0222662487959506e-21 - error);
            }
            return reduced;
        }

        static float NativeSin(float angle)
        {
            if (angle > 0.7853981852531433f) return (float)CosPolynomial(ReduceHalfPi(angle));
            if (angle < 0.0001220703125f) return angle;
            if (angle < 0.0078125f)
            {
                double x = angle;
                return (float)(x - ((x * x) * x) * 0.16666666666666666);
            }
            return (float)SinPolynomial(angle);
        }

        static float NativeCos(float angle)
        {
            return angle > 0.7853981852531433f ?
                (float)-SinPolynomial(ReduceHalfPi(angle)) : (float)CosPolynomial(angle);
        }

        public static bool UpdateDuration(int speed, int frame, ref float duration)
        {
            int frames;
            switch (speed)
            {
                case 0: frames = frame; break;
                case 1: frames = 3; break;
                case 10: frames = 2; break;
                case 20: frames = 6; break;
                case 21: frames = 9; break;
                default: return false; // Original TryGetValue failure preserves prior duration.
            }
            duration = frames * FrameSeconds;
            return true;
        }

        public static float Rate(DrivenKeyComponent.InterpolateType type, float currentTime, float duration)
        {
            if (duration <= 0f || currentTime >= duration) return 1f;
            float ratio = Mathf.Clamp01(currentTime / duration);
            switch (type)
            {
                case DrivenKeyComponent.InterpolateType.EaseIn:
                    return 1f - NativeCos(ratio * HalfPi);
                case DrivenKeyComponent.InterpolateType.EaseOut:
                    return NativeSin(ratio * HalfPi);
                case DrivenKeyComponent.InterpolateType.EaseInOut:
                    float doubled = ratio + ratio;
                    return doubled < 1f ? (1f - NativeCos(doubled * HalfPi)) * 0.5f :
                        NativeSin((doubled - 1f) * HalfPi) * 0.5f + 0.5f;
                default: return ratio;
            }
        }
    }
}
