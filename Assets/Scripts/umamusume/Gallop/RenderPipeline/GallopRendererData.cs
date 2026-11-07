using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Gallop.RenderPipeline
{
    // Native factories 0x52307a0 / 0x522ffe0 create independent renderers.
    [CreateAssetMenu(menuName = "Rendering/Gallop Renderer")]
    public sealed class GallopRendererData : ScriptableRendererData
    {
        public Shader CopyColorShader;
        public bool SimpleMirror;
        // Native UniversalRendererData m_ShadowTransparentReceive. 1154 renderer asset = 1.
        [SerializeField] private bool m_ShadowTransparentReceive = true;
        public bool ShadowTransparentReceive => m_ShadowTransparentReceive;
        protected override ScriptableRenderer Create()
        {
            debugShaders ??= new DebugShaderResources();
            return new GallopRenderer(this);
        }
    }
}
