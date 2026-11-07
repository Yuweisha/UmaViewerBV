using UnityEngine;
using UnityEngine.Rendering;

namespace Gallop.RenderPipeline
{
    public partial class CameraData
    {
        public delegate void BeginFrameRendering(ScriptableRenderContext context, Camera camera);
        public delegate void EndFrameRendering(ScriptableRenderContext context, Camera camera);
        public delegate void BeginCameraRendering(ScriptableRenderContext context, Camera camera);
        public delegate void AfterBeginCameraRendering(ScriptableRenderContext context, Camera camera);
        public delegate void EndCameraRendering(ScriptableRenderContext context, Camera camera);
        public BeginFrameRendering BeginFrameRenderingCallback { get; set; }
        public EndFrameRendering EndFrameRenderingCallback { get; set; }
        public BeginCameraRendering BeginCameraRenderingCallback { get; set; }
        public AfterBeginCameraRendering AfterBeginCameraRenderingCallback { get; set; }
        public EndCameraRendering EndCameraRenderingCallback { get; set; }
    }

    public static class CameraExtension
    {
        // Original 0x51ca390: preserve existing owner; initialize newly added one.
        public static CameraData GetCameraData(this Camera camera)
        {
            if (camera.TryGetComponent<CameraData>(out var owner)) return owner;
            owner = camera.gameObject.AddComponent<CameraData>();
            owner.Initialize(false);
            return owner;
        }
    }
}
