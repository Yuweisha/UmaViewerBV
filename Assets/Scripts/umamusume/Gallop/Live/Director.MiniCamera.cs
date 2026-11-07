using UnityEngine;

namespace Gallop.Live
{
    public partial class Director
    {
        // Original camera fields170/194; model root is the independent field1d0.
        public Camera MiniCharaCamera {get;internal set;}
        public bool IsMiniCharaCameraInitialized {get;internal set;}
        public Transform MiniCharaRootTransform {get;internal set;}
        LiveMiniCameraOwner _miniCameraOwner;
        internal void PrepareMiniCharaOutput() => _miniCameraOwner?.PrepareOutput(MainRenderCamera);
        internal void SetMiniCharaOrthographicSize(float size) => _miniCameraOwner?.SetOrthographicSize(size);
        internal void CreateMiniCharaCamera(RenderTexture mainColorBuffer)
        {
            if (IsMiniCharaCameraInitialized) return;
            _miniCameraOwner = new LiveMiniCameraOwner(transform) { MainColorBuffer = mainColorBuffer };
            _miniCameraOwner.Initialize(true, LiveMonitorResolution.CurrentVirtualResolution);
            MiniCharaCamera = _miniCameraOwner.Camera;
            IsMiniCharaCameraInitialized = _miniCameraOwner.IsInitialized;
        }
        internal void DestroyMiniCharaCamera()
        {
            if (!IsMiniCharaCameraInitialized) return;
            _miniCameraOwner.Release();
            IsMiniCharaCameraInitialized = false;
        }
    }
}
