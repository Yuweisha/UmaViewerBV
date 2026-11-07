using UnityEngine;

namespace Gallop.Live.Cutt
{
    //贝塞尔曲线计算工具类
    //任意阶贝塞尔曲线的递归计算
    //二次、三次、四次和五次贝塞尔曲线的公式直接计算
    public static class BezierUtil
    {
        private static readonly Vector3[] _subPointArray = new Vector3[17];

        // Original SSE rounds each operation. Mono can keep wider intermediates.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static float F(float v) => System.BitConverter.Int32BitsToSingle(System.BitConverter.SingleToInt32Bits(v));
        private static float M(float a, float b) => F(a * b);
        private static float A(float a, float b) => F(a + b);
        private static float S3(float a, float b, float c) => A(A(a, b), c);
        private static float S4(float a, float b, float c, float d) => A(S3(a, b, c), d);
        private static float S5(float a, float b, float c, float d, float e) => A(S4(a, b, c, d), e);
        private static float S6(float a, float b, float c, float d, float e, float f) => A(S5(a, b, c, d, e), f);
        internal static Vector3 LerpUnclamped(Vector3 a, Vector3 b, float t) => new Vector3(
            A(M(F(b.x - a.x), t), a.x), A(M(F(b.y - a.y), t), a.y), A(M(F(b.z - a.z), t), a.z));

        public static void Calc(Vector3[] points, int pointNum, float t, out Vector3 outPos)
        {
            outPos = CalcRecurse(points, pointNum, t);
        }

        private static Vector3 CalcRecurse(Vector3[] points, int pointNum, float t)
        {
            if (pointNum == 2)
            {
                return LerpUnclamped(points[0], points[1], t);
            }

            for (int i = 0; i < pointNum - 1; i++)
            {
                _subPointArray[i] = LerpUnclamped(points[i], points[i + 1], t);
            }

            return CalcRecurse(_subPointArray, pointNum - 1, t);
        }

        public static void Calc(ref Vector3 start, ref Vector3 end, ref Vector3 cp, float t, out Vector3 v3)
        {
            float num = F(1f - t);
            float num2 = M(t, t);
            float num3 = M(A(t, t), num);
            float num4 = M(num, num);

            v3.x = S3(M(num2, end.x), M(num3, cp.x), M(num4, start.x));
            v3.y = S3(M(num3, cp.y), M(num2, end.y), M(num4, start.y));
            v3.z = S3(M(num3, cp.z), M(num2, end.z), M(num4, start.z));
        }

        public static void Calc(ref Vector3 start, ref Vector3 end, ref Vector3 cp1, ref Vector3 cp2, float t, out Vector3 v3)
        {
            float num = F(1f - t);
            float num2 = M(M(t, t), t);
            float num3 = M(M(M(t, 3f), t), num);
            float num4 = M(M(M(t, 3f), num), num);
            float num5 = M(M(num, num), num);

            v3.x = S4(M(num2, end.x), M(num3, cp2.x), M(num4, cp1.x), M(num5, start.x));
            v3.y = S4(M(num3, cp2.y), M(num2, end.y), M(num4, cp1.y), M(num5, start.y));
            v3.z = S4(M(num3, cp2.z), M(num2, end.z), M(num4, cp1.z), M(num5, start.z));
        }

        public static void Calc(ref Vector3 start, ref Vector3 end, ref Vector3 cp1, ref Vector3 cp2, ref Vector3 cp3, float t, out Vector3 v3)
        {
            float num = F(1f - t);
            float num2 = M(M(M(t, t), t), t);
            float num3 = M(M(M(M(t, 4f), t), t), num);
            // Pinned original 0x4f03740 uses 4 here, including the middle term.
            float num4 = M(M(M(M(t, 4f), t), num), num);
            float num5 = M(M(M(M(t, 4f), num), num), num);
            float num6 = M(M(M(num, num), num), num);

            v3.x = S5(M(num2, end.x), M(num3, cp3.x), M(num4, cp2.x), M(num5, cp1.x), M(num6, start.x));
            v3.y = S5(M(num3, cp3.y), M(num2, end.y), M(num4, cp2.y), M(num5, cp1.y), M(num6, start.y));
            v3.z = S5(M(num3, cp3.z), M(num2, end.z), M(num4, cp2.z), M(num5, cp1.z), M(num6, start.z));
        }

        public static void Calc(ref Vector3 start, ref Vector3 end, ref Vector3 cp1, ref Vector3 cp2, ref Vector3 cp3, ref Vector3 cp4, float t, out Vector3 v3)
        {
            float num = F(1f - t);
            float num2 = M(M(M(M(t, t), t), t), t);
            float num3 = M(M(M(M(M(t, 5f), t), t), t), num);
            // Pinned original 0x4f03a00 uses 5 for both middle terms.
            float num4 = M(M(M(M(M(t, 5f), t), t), num), num);
            float num5 = M(M(M(M(M(t, 5f), t), num), num), num);
            float num6 = M(M(M(M(M(t, 5f), num), num), num), num);
            float num7 = M(M(M(M(num, num), num), num), num);

            v3.x = S6(M(num3, cp4.x), M(num2, end.x), M(num4, cp3.x), M(num5, cp2.x), M(num6, cp1.x), M(num7, start.x));
            v3.y = S6(M(num3, cp4.y), M(num2, end.y), M(num4, cp3.y), M(num5, cp2.y), M(num6, cp1.y), M(num7, start.y));
            v3.z = S6(M(num3, cp4.z), M(num2, end.z), M(num4, cp3.z), M(num5, cp2.z), M(num6, cp1.z), M(num7, start.z));
        }
    }
}
