using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gallop.RenderPipeline
{
    // Preserve the completed game image while the UI-only base camera draws
    // the same title/lyric geometry used by the playback screen canvas.
    internal sealed class LiveSubtitleBackdropPass : ScriptableRenderPass
    {
        private RenderTexture source;
        public LiveSubtitleBackdropPass() {renderPassEvent=RenderPassEvent.AfterRenderingSkybox;}
        public void Setup(RenderTexture texture) {source=texture;}
        public override void Execute(ScriptableRenderContext context,ref RenderingData data)
        {
            if(source==null)return;
            var command=CommandBufferPool.Get("Live subtitle scene backdrop");
            command.Blit(source,data.cameraData.renderer.cameraColorTargetHandle.nameID);
            context.ExecuteCommandBuffer(command);CommandBufferPool.Release(command);
        }
    }
}
