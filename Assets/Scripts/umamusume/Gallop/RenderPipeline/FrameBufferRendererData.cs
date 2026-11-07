using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Gallop.RenderPipeline
{
    [CreateAssetMenu(menuName = "Rendering/Gallop FrameBuffer Renderer")]
    public sealed class FrameBufferRendererData : ScriptableRendererData
    {
        public Shader BlitShader;
        protected override ScriptableRenderer Create()
        {
            // URP14's base constructor dereferences this editor resource owner,
            // including for runtime-created data and original assets without it.
            debugShaders ??= new DebugShaderResources();
            return new FrameBufferRenderer(this);
        }
    }
}
