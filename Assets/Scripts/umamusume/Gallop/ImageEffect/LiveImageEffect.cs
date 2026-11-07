using Gallop.RenderPipeline;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gallop
{
    // Serialized Live camera subtype. The base image effect is also used outside Live.
    public class LiveImageEffect : GallopImageEffect
    {
        readonly EdgeStyleController _edgeStyleController=new EdgeStyleController();
        public EdgeStyleMode CurrentEdgeStyle=>_edgeStyleController.CurrentMode;
        public void SetUpEdgeStyleController(EdgeStyleMode mode)=>_edgeStyleController.Initialize(this,mode);
        public void SetUpEdgeStyleRenderList(int index,UnityEngine.Object model,
            System.Func<UnityEngine.Object,EdgeStylePartsData> parts)=>_edgeStyleController.SetupRendererList(index,model,parts);
        public void CleanUpEdgeStyleController()=>_edgeStyleController.CleanUp();
        void OnDestroy()=>CleanUpEdgeStyleController();
        public RenderTexture[] MonitorTextureArray { get; set; }

        public override void InitializeVolume()
        {
            base.InitializeVolume();
            // Fixed DLL0x3ee1a60 repeats these derived assignments even when
            // the base initializer has already run.
            RenderParameter.DofDiffuionBloomOverlay.IsEnableDofAutoDisable = true;
            RenderParameter.OnPrepareScreenMakeup = OnRenderImagePrepareScreenMakeup;
            RenderParameter.LensDistortion.RenderPassEvent=(RenderPassEvent)602;
            RenderParameter.Vortex.RenderPassEvent=(RenderPassEvent)603;
        }

        private void OnRenderImagePrepareScreenMakeup(ScriptableRenderContext context,
            ref RenderingData renderingData, RenderTargetIdentifier source, RenderTargetIdentifier destination)
        {
            var director = Live.Director.instance;
            if (director == null || MonitorTextureArray == null) return;
            var cmd = CommandBufferPool.Get("Live.PrepareScreenMakeup");
            try
            {
                var virtualResolution = director.MonitorVirtualResolution;
                for (int i = 0; i < MonitorTextureArray.Length; i++)
                    if (!director.IsActiveMonitorCamera(i) && director.IsUseRenderTextureForMonitor(i))
                        CopyMonitorTexture(cmd, source, MonitorTextureArray[i], virtualResolution);
                context.ExecuteCommandBuffer(cmd);
            }
            finally { CommandBufferPool.Release(cmd); }
        }

        public static void CopyMonitorTexture(CommandBuffer cmd, RenderTargetIdentifier source,
            RenderTexture target, Vector2Int virtualResolution)
        {
            float width = virtualResolution.x, height = virtualResolution.y;
            float targetAspect = (float)target.width / target.height;
            float aspect = width / height;
            if (Mathf.Abs(aspect - targetAspect) < 0.001f)
                cmd.Blit(source, target);
            else if (targetAspect > aspect)
            {
                float cropHeight = width / targetAspect;
                cmd.Blit(source, target, new Vector2(1f, cropHeight / height),
                    new Vector2(0f, ((height - cropHeight) * 0.5f) / height));
            }
            else
            {
                float cropWidth = targetAspect * height;
                cmd.Blit(source, target, new Vector2(cropWidth / width, 1f),
                    new Vector2(((width - cropWidth) * 0.5f) / width, 0f));
            }
        }
    }
}
