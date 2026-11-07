using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gallop.RenderPipeline
{
    // Fixed native Create installs PrepareScreenMakeup at550. RegisterPass
    // places it after shaft effects and before DOF/Bloom at the same event.
    public sealed class ImageEffectCallbackPass : ScriptableRenderPass
    {
        public delegate void ImageEffectCallback(ScriptableRenderContext context, ref RenderingData renderingData, RenderTargetIdentifier sourceRT, RenderTargetIdentifier destinationRT);
        private PostImageEffectFeature _feature;
        private ImageEffectCallback _callback;
        readonly string _traceStep;
        public ImageEffectCallbackPass(RenderPassEvent evt, string traceStep = "PrepareScreenMakeup")
        {
            renderPassEvent = evt;
            _traceStep = traceStep;
        }

        public bool Setup(PostImageEffectFeature feature, ImageEffectCallback callback)
        {
            _feature = callback != null ? feature : null;
            _callback = callback;
            return callback != null;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var renderer = renderingData.cameraData.renderer;
            if (renderer == null)
                return;
            // Native Execute snapshots SourceRT and camera target by VALUE.
            // The callback does not transfer ownership or resolve the source.
            var source = _feature.SourceRT;
            _callback(context, ref renderingData, source.RtId, renderer.cameraColorTargetHandle.nameID);
        }

        public void Dispose()
        {
            _feature = null;
            _callback = null;
        }
    }
}
