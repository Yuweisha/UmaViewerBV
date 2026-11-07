using UnityEngine;

namespace Gallop.Live
{
    // UIGradation.SetAlpha 0x4f0e420:
    // writes alpha into _colorTop.a (+0x34) and _colorBottom.a (+0x44).
    // UIGradation.SetGradationColor 0x4f0e430:
    // copies two 16-byte colors into +0x28 and +0x38.
    public sealed class UIGradationState
    {
        public Color ColorTop;
        public Color ColorBottom;

        public void SetAlpha(float alpha)
        {
            ColorTop.a = alpha;
            ColorBottom.a = alpha;
        }


        // UIGradation.UpdateVertexColor 0x4f0e440:
        // vertex i uses colorTop when (i % 6) equals one of _topVertices[0..2],
        // otherwise colorBottom.
        public static bool[] ChooseTopVertices(int vertexCount, int[] topVertices)
        {
            var result = new bool[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                int m = i % 6;
                bool top = false;
                for (int j = 0; j < topVertices.Length; j++)
                    if (m == topVertices[j]) { top = true; break; }
                result[i] = top;
            }
            return result;
        }
        public void SetGradationColor(Color top, Color bottom)
        {
            ColorTop = top;
            ColorBottom = bottom;
        }
    }
}
