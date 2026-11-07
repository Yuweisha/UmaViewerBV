using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

namespace Gallop.Live
{
    // UIGradation 0x4f0e140..0x4f0e6a0.
    // SetAlpha/SetGradationColor are native-verified; ModifyMesh applies the same
    // top/bottom index rule (i % 6 in _topVertices) to the generated UI mesh.
    [AddComponentMenu("Live/UIGradation")]
    public class UIGradation : BaseMeshEffect
    {
        public Color ColorTop = Color.white;
        public Color ColorBottom = Color.white;
        public int[] TopVertices = { 0, 1, 2 };
        public readonly UIGradationState State = new UIGradationState();

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive()) return;
            // Original UpdateVertexColor indexes the expanded TRIANGLE stream,
            // not the four shared vertices of a quad. Authored [1,2,3] selects
            // its three top-corner occurrences in each six-vertex glyph.
            var vertices = new List<UIVertex>();
            vh.GetUIVertexStream(vertices);
            int count = vertices.Count;
            var top = UIGradationState.ChooseTopVertices(count, TopVertices);
            UIVertex vertex = default;
            for (int i = 0; i < count; i++)
            {
                vertex = vertices[i];
                vertex.color = top[i] ? ColorTop : ColorBottom;
                vertices[i] = vertex;
            }
            vh.Clear();
            vh.AddUIVertexTriangleStream(vertices);
        }

        public void SetAlpha(float alpha)
        {
            State.ColorTop = ColorTop;
            State.ColorBottom = ColorBottom;
            State.SetAlpha(alpha);
            ColorTop = State.ColorTop;
            ColorBottom = State.ColorBottom;
            if (graphic != null) graphic.SetVerticesDirty();
        }

        public void SetGradationColor(Color top, Color bottom)
        {
            ColorTop = top;
            ColorBottom = bottom;
            State.SetGradationColor(top, bottom);
            if (graphic != null) graphic.SetVerticesDirty();
        }
    }
}
