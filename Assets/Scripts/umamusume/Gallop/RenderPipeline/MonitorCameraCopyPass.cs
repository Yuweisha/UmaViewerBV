using Gallop.Live;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gallop.RenderPipeline
{
    // Host scheduling adapter for native MonitorCamera.OnEndRendering. Resolve
    // URP14's final attachment first; enqueue the copy before context.Submit.
    public sealed class MonitorCameraCopyPass : ScriptableRenderPass
    {
        private StageMonitorCameraDriver _driver;
        public MonitorCameraCopyPass()
        {
            renderPassEvent = (RenderPassEvent)((int)RenderPassEvent.AfterRendering + 4);
        }
        public bool Setup(StageMonitorCameraDriver driver, Camera camera)
        {
            _driver = driver;
            return driver != null && driver.isActiveAndEnabled && driver.IsCaptureCamera(camera);
        }
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            _driver?.CopyCapture(context, renderingData.cameraData.camera);
        }
    }
}
