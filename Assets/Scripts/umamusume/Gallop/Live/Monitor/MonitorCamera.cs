using UnityEngine;

namespace Gallop.Live
{
    // Restored component identity of the original Resources/Prefabs/Live/MonitorCamera.
    // RT lifecycle/borrowed target follows the version-pinned MonitorCamera bodies.
    // The existing StageMonitorCameraDriver supplies URP's pre-submit copy adapter;
    // this does not replace the pending original CameraData callback installation.
    [DisallowMultipleComponent]
    public sealed class MonitorCamera : MonoBehaviour
    {
        private Camera _targetCamera;
        private bool _isEnabledTargetTexture;
        private RenderTexture _targetTexture;
        private RenderTexture _monitorTexture;

        public Camera targetCamera => _targetCamera;
        public RenderTexture monitorTexture => _monitorTexture;
        public RenderTexture targetTexture => _targetTexture;

        public void Initialize(int width, int height)
        {
            if (_targetCamera == null) _targetCamera = GetComponent<Camera>();
            ReleaseMonitorTexture();
            if (_targetCamera == null) return;
            _monitorTexture = new RenderTexture(width, height, 0)
            {
                name = "LiveMonitorCameraRT", filterMode = FilterMode.Bilinear
            };
            _monitorTexture.Create();
            _targetCamera.targetTexture = _monitorTexture;
            _targetCamera.depth = 0f;
            // Host CameraData/URP adapter for the native Initialize parameter gates.
            var data = GetComponent<Gallop.RenderPipeline.CameraData>();
            if (data == null) data = gameObject.AddComponent<Gallop.RenderPipeline.CameraData>();
            data.Parameter.IsEnable = false;
            data.Parameter.IsRequestDepthPass = false;
        }

        public void SetupCaptureTexture(RenderTexture texture)
        {
            if (_targetCamera == null) return;
            _targetTexture = texture;
            _isEnabledTargetTexture = texture != null;
        }

        public void ReleaseMonitorTexture()
        {
            if (_monitorTexture == null) return;
            _monitorTexture.Release();
            if (Application.isPlaying) Destroy(_monitorTexture); else DestroyImmediate(_monitorTexture);
            _monitorTexture = null;
        }

        private void OnDestroy()
        {
            if (_targetCamera != null) _targetCamera.targetTexture = null;
            ReleaseMonitorTexture();
        }
    }
}
