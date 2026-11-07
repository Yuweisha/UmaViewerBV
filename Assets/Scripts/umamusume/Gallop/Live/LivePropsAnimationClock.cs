using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Gallop.Live
{
    public static class LivePropsAnimationClock
    {
        // Original producer 0x4ee1e26..0x4ee1f59. StartAnimationTime is
        // serialized editor data; the runtime clock starts at AnimationHeadFrame.
        public static float Calculate(int keyFrame, int headFrame, int currentFrame,
            float currentTime, Func<int, float> timescale = null)
        {
            float remainder = Mathf.Max(0f, Sub(currentTime, Divide(currentFrame, 60f)));
            if (timescale == null)
                return Add(Divide(unchecked(headFrame - keyFrame + currentFrame), 60f), remainder);
            float frames = headFrame;
            for (int frame = keyFrame; frame < currentFrame; frame++) frames = Add(frames, timescale(frame));
            return Add(Divide(frames, 60f), Multiply(remainder, timescale(keyFrame)));
        }

        // Mono may retain excess precision or fold division by a constant into
        // a reciprocal multiply. Native uses a rounded divss/subss/addss chain.
        [MethodImpl(MethodImplOptions.NoInlining)] private static float Divide(float a, float b) => BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(a / b));
        [MethodImpl(MethodImplOptions.NoInlining)] private static float Sub(float a, float b) => BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(a - b));
        [MethodImpl(MethodImplOptions.NoInlining)] private static float Add(float a, float b) => BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(a + b));
        [MethodImpl(MethodImplOptions.NoInlining)] private static float Multiply(float a, float b) => BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(a * b));
    }
}
