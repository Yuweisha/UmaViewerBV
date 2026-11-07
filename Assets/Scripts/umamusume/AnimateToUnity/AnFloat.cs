using System;
using System.Runtime.CompilerServices;

namespace AnimateToUnity
{
    internal static class AnFloat
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static float Round(float value)=>BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(value));
    }
}
