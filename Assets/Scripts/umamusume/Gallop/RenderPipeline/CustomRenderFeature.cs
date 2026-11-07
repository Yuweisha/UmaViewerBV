using UnityEngine.Rendering.Universal;

namespace Gallop.RenderPipeline
{
    // Original 0x51ccaa0/0x51cc970/0x51ccbc0. Each renderer owns four passes.
    public sealed class CustomRenderFeature : ScriptableRendererFeature
    {
        public const int CUSTOM_PARAMETER_NUM = 4;
        CustomRenderPass[] _customRenderPassArray;
        public override void Create()
        {
            Dispose();
            _customRenderPassArray = new CustomRenderPass[CUSTOM_PARAMETER_NUM];
            for (int i = 0; i < _customRenderPassArray.Length; i++)
                _customRenderPassArray[i] = new CustomRenderPass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
        {
            if (data.cameraData.camera.TryGetComponent<CameraData>(out var owner))
                owner.AddRenderPassesCustomRenderPass(renderer, _customRenderPassArray);
        }

        protected override void Dispose(bool disposing)
        {
            if (_customRenderPassArray != null)
                foreach (var pass in _customRenderPassArray) pass.Dispose();
        }
    }
}
