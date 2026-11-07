using System;
using Gallop.RenderPipeline;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gallop.Live
{
    // Original Director.CreateMiniCharaCamera 0x4e24170, release 0x4e257e0,
    // after-pass 0x4e2d850. The host supplies the original main-buffer and
    // GraphicSettings owners; model depth ordering is wired by its own owner.
    public sealed class LiveMiniCameraOwner
    {
        readonly Transform _directorRoot;
        LiveMiniFrameBuffer _frameBuffer;
        Material _material;
        RenderPipeline.CameraData _cameraData;
        int _customPassReserveId = -1;
        int _referenceHeight;
        float _authoredOrthographicSize = 1f;
        UniversalAdditionalCameraData _urpCamera;
        Camera _presentationOwner;
        public bool IsInitialized { get; private set; }
        public GameObject CameraObject { get; private set; }
        public Camera Camera { get; private set; }
        public Transform CameraTransform { get; private set; }
        public LiveMiniFrameBuffer FrameBuffer => _frameBuffer;
        public RenderTexture MainColorBuffer { get; set; }
        public event Action<Camera> BeginFrameRendering;
#if UNITY_EDITOR
        public event Action<string, Texture, RenderTexture, Material> CommandWrite;
#endif
        public LiveMiniCameraOwner(Transform directorRoot) { _directorRoot = directorRoot; }

        public void Initialize(bool hasGraphicsSettings, Func<Vector2Int> getVirtualResolution3D)
        {
            if (IsInitialized) return;
            CameraObject = new GameObject("MiniCharaCamera");
            Camera = CameraObject.AddComponent<Camera>();
            _urpCamera = CameraObject.AddComponent<UniversalAdditionalCameraData>();
            _urpCamera.renderPostProcessing = false;
            _urpCamera.renderShadows = false;
            Camera.cullingMask = GraphicSettings.GetCullingLayer(GraphicSettings.LayerIndex.LayerCharaMini);
            Camera.allowMSAA = true;
            Camera.allowHDR = false;
            Camera.depth = 16f;
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = Color.clear;
            Camera.targetTexture = null;
            _frameBuffer = new LiveMiniFrameBuffer("MiniCharaCameraColorBuffer");
            _frameBuffer.Initialize(hasGraphicsSettings, getVirtualResolution3D);
            _referenceHeight = _frameBuffer.ColorBuffer.height;
            _frameBuffer.AddCamera(Camera);
            CameraTransform = Camera.transform;
            CameraTransform.parent = _directorRoot;
            Camera.orthographic = true;
            Camera.orthographicSize = 1f;
            CameraTransform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 180f, 0f));
            if (!CameraObject.TryGetComponent<CameraEventCallback>(out var callback))
                callback = CameraObject.AddComponent<CameraEventCallback>();
            callback.BeginFrameRenderingCallack += OnBeginFrameRendering;
            _material = new Material(ShaderManager.GetShader(ShaderManager.ShaderKinds.MultiCameraFinalComposite));
            _cameraData = Camera.GetCameraData();
            ref var parameter = ref _cameraData.GetCustomRenderParameter(out _customPassReserveId);
            parameter.IsEnable = true;
            parameter.RenderPassEvent = (RenderPassEvent)1000;
            parameter.OnExecute += OnAfterRendering;
            IsInitialized = true;
        }

        internal void PrepareOutput(Camera presentation)
        {
            if (!IsInitialized || presentation == null) return;
            // Keep the quality-selected vertical resolution for window playback;
            // exports use their actual requested dimensions. Projection and RT
            // must share the output aspect before ViewportToWorldPoint runs.
            var output = MainColorBuffer != null ? MainColorBuffer : presentation.targetTexture;
            float aspect = (float)presentation.pixelWidth / Mathf.Max(1, presentation.pixelHeight);
            int height = output != null ? output.height : _referenceHeight;
            int width = output != null ? output.width : Mathf.Max(1, Mathf.RoundToInt(height * aspect));
            _frameBuffer.Resize(width, height);
            Camera.aspect = (float)width / height;
            SetOrthographicSize(_authoredOrthographicSize);
            PreparePresentation(presentation);
        }

        void PreparePresentation(Camera presentation)
        {
            if (_urpCamera == null) return;
            bool overlay = MainColorBuffer == null;
            if (_presentationOwner != null && (!overlay || _presentationOwner != presentation))
            {
                _presentationOwner.GetUniversalAdditionalCameraData().cameraStack.Remove(Camera);
                _presentationOwner = null;
            }
            _urpCamera.renderType = overlay ? CameraRenderType.Overlay : CameraRenderType.Base;
            // A screen overlay must retain the completed scene color. Its own
            // MSAA intermediate would start without that color; the private
            // export target retains the native antialiasing path.
            Camera.allowMSAA = !overlay;
            Camera.clearFlags = overlay ? CameraClearFlags.Depth : CameraClearFlags.SolidColor;
            Camera.targetTexture = overlay ? null : _frameBuffer.ColorBuffer;
            if (!overlay) return;
            _presentationOwner = presentation;
            var stack = presentation.GetUniversalAdditionalCameraData().cameraStack;
            stack.Remove(Camera);
            int index = stack.FindIndex(c => c != null && c.depth > Camera.depth);
            stack.Insert(index < 0 ? stack.Count : index, Camera);
        }

        internal void SetOrthographicSize(float size)
        {
            _authoredOrthographicSize = size;
            // The authored 1920x1080 layout fits by height on wide outputs and
            // by width on narrower outputs, retaining the Mini model's shape.
            Camera.orthographicSize = size * Mathf.Max(1f, (16f / 9f) / Camera.aspect);
        }

        void OnBeginFrameRendering(ScriptableRenderContext context, Camera camera) => BeginFrameRendering?.Invoke(camera);

        void OnAfterRendering(CustomRenderPass pass, ScriptableRenderContext context, ref RenderingData data)
        {
            // Playback overlays draw directly into the presentation stack.
            // CameraTarget from a private Mini RT refers to that RT, not the
            // window backbuffer. Only offline export needs the explicit blit.
            if (_urpCamera != null && _urpCamera.renderType == CameraRenderType.Overlay) return;
            var cmd = CommandBufferPool.Get("MiniCharaCameraAfterRenderingPass");
#if UNITY_EDITOR
            CommandWrite?.Invoke("CommandBufferPool.Get", null, null, null);
#endif
            // Native calls the three-argument material overload (all passes).
            cmd.Blit(_frameBuffer.ColorBuffer, MainColorBuffer!=null?new RenderTargetIdentifier(MainColorBuffer):new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget), _material);
#if UNITY_EDITOR
            CommandWrite?.Invoke("Blit", _frameBuffer.ColorBuffer, MainColorBuffer, _material);
#endif
            context.ExecuteCommandBuffer(cmd);
#if UNITY_EDITOR
            CommandWrite?.Invoke("ExecuteCommandBuffer", null, null, null);
#endif
            CommandBufferPool.Release(cmd);
#if UNITY_EDITOR
            CommandWrite?.Invoke("CommandBufferPool.Release", null, null, null);
#endif
        }

        public void Release()
        {
            if (!IsInitialized) return;
            if (_presentationOwner != null)
                _presentationOwner.GetUniversalAdditionalCameraData().cameraStack.Remove(Camera);
            _presentationOwner = null;
            _frameBuffer.Release();
            _frameBuffer = null;
            RenderUtils.Destroy(ref _material);
            _cameraData.ReleaseCustomRenderParameter(ref _customPassReserveId);
            IsInitialized = false;
            // Native retains the GameObject/Camera/Transform and callback here.
            // Their parent/root lifecycle owns destruction.
        }
    }
}

