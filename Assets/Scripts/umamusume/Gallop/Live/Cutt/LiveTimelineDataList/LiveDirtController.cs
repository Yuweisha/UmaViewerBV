using System.Collections.Generic;
using UnityEngine;
using Gallop;

namespace Gallop.Live.Cutt
{
    // Managed counterpart of DirtController.ApplyMaterial (0x518a230):
    // [_DirtRate] = Vector4(upper, lower, foot, 0) through the shared model
    // MaterialPropertyBlock, then applied to every renderer of the dirt holder
    // (RendererHolder.ApplyMaterialPropertyBlock 0x51b8e80).
    //
    // Component order comes from DirtController.SetDirtRate 0x518a600: it stores
    // foot at rates[2], lower at rates[1] and upper at rates[0] (0x518a613/27/3e),
    // and ApplyMaterial copies rates[0],rates[1],rates[2] into the vector.
    // DirtController.OnInitialize 0x518a4cd points its block at ModelController
    // +0xd8, so dirt is written into the same block as colour/toon/emissive.
    public sealed class LiveDirtController
    {
        public bool Enabled;
        public readonly float[] Rates = new float[3];
        public readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();
        public readonly List<Renderer> Renderers = new List<Renderer>();

        public void ApplyMaterial(bool applyToRenderers)
        {
            if (!Enabled) return;
            int id = ShaderManager.GetPropertyId(ShaderManager.PropertyId._DirtRate);
            Block.SetVector(id, new Vector4(Rates[2], Rates[1], Rates[0], 0f));
            if (!applyToRenderers) return;
            for (int i = 0; i < Renderers.Count; i++)
            {
                if (Renderers[i] != null) Renderers[i].SetPropertyBlock(Block);
            }
        }
    }
}
