using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Gallop.Live
{
    // Original Director.InitializeCamera 0x4e28d10 / UpdateTransparentCamera
    // 0x4e41e40. The Viewer uses a URP stack for the game's shared framebuffer.
    // This camera has no LiveImageEffect: its TransparentFX draw follows the
    // processed scene, with independent depth and no scene postprocessing.
    public sealed class LiveTransparentCamera
    {
        readonly Camera camera;
        Camera owner;
        public Camera Camera => camera;
        public bool Active => camera != null && camera.enabled;
        public LiveTransparentCamera(Transform parent)
        {
            var go = new GameObject("TransparentCamera");
            go.transform.SetParent(parent, false);
            camera = go.AddComponent<Camera>();
            camera.cullingMask = GraphicSettings.GetCullingLayer(
                GraphicSettings.LayerIndex.LayerTransparentFX);
            camera.allowMSAA = false;
            camera.allowHDR = false;
            camera.depth = 15f;
            // Host URP Overlay continues the processed scene surface. A Color
            // request enters Gallop's native private-target clear path, which
            // must not clear the shared Viewer presentation framebuffer.
            camera.clearFlags = CameraClearFlags.Depth;
            camera.backgroundColor = Color.clear;
            camera.enabled = false;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderType = CameraRenderType.Overlay;
            // URP Overlay m_ClearDepth defaults to true; Gallop owner matches it.
            data.renderPostProcessing = false;
            data.renderShadows = false;
            var gallop = go.AddComponent<Gallop.RenderPipeline.CameraData>();
            gallop.RequestAntiAliasing = Gallop.RenderPipeline.CameraData.AntiAliasLevel.None;
        }
        public void Update(Camera main, bool enabled, bool orthographic, float size, bool defaultTransform)
        {
            if (camera == null || main == null) return;
            if (owner != main)
            {
                RemoveFromOwner();
                owner = main;
                var data = main.GetComponent<UniversalAdditionalCameraData>();
                if (data == null) data = main.gameObject.AddComponent<UniversalAdditionalCameraData>();
                if (!data.cameraStack.Contains(camera)) data.cameraStack.Add(camera);
            }
            camera.enabled = enabled;
            // Native AddCamera assigns the shared framebuffer, which also
            // supplies its aspect. URP overlays inherit the target indirectly.
            camera.targetTexture = main.targetTexture;
            camera.aspect = main.aspect;
            camera.orthographic = orthographic;
            camera.orthographicSize = size;
            // Native StaticVariableDefine.Live.Director .cctor 0x5880300
            // stores Quaternion.Internal_FromEulerRad(0, PI, 0), not identity.
            camera.transform.SetPositionAndRotation(defaultTransform ? Vector3.zero : main.transform.position,
                defaultTransform ? Quaternion.Euler(0f, 180f, 0f) : main.transform.rotation);
            camera.fieldOfView = main.fieldOfView;
            camera.nearClipPlane = main.nearClipPlane;
            camera.farClipPlane = main.farClipPlane;
        }
        void RemoveFromOwner()
        {
            if (owner != null && owner.TryGetComponent<UniversalAdditionalCameraData>(out var data))
                data.cameraStack.Remove(camera);
            owner = null;
        }
        public void Dispose()
        {
            RemoveFromOwner();
            if (camera != null) Object.Destroy(camera.gameObject);
        }
    }
}


