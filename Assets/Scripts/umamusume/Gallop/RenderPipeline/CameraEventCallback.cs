using System;
using UnityEngine;
using UnityEngine.Rendering;
using Gallop.RenderPipeline;

namespace Gallop
{
    // Original 0x4a4c030 and five forwarding bodies. Subscription is to the
    // CameraData owner; RenderingManager/pipeline dispatch is a separate layer.
    public sealed class CameraEventCallback : MonoBehaviour
    {
        Camera _targetCamera;
        bool _isInitialize;
        public Action<ScriptableRenderContext, Camera> BeginFrameRenderingCallack;
        public Action<ScriptableRenderContext, Camera> EndFrameRenderingCallback;
        public Action<ScriptableRenderContext, Camera> BeginCameraRenderingCallback;
        public Action<ScriptableRenderContext, Camera> AfterBeginCameraRenderingCallback;
        public Action<ScriptableRenderContext, Camera> EndCameraRenderingCallback;

        public void Initialize(Camera targetCamera) => Initialize(targetCamera, false);
        void Initialize(Camera targetCamera, bool isAwake)
        {
            var previous = _targetCamera;
            if (targetCamera != null) _targetCamera = targetCamera;
            else if (!TryGetComponent(out _targetCamera))
            {
                if (!isAwake) enabled = false;
                return;
            }
            if (_isInitialize && previous != null) Unbind(previous.GetCameraData());
            var owner = _targetCamera.GetCameraData();
            owner.BeginCameraRenderingCallback += OnCameraRendering;
            owner.AfterBeginCameraRenderingCallback += OnAfterBeginCameraRendering;
            owner.EndCameraRenderingCallback += OnEndCameraRendering;
            owner.BeginFrameRenderingCallback += OnBeginFrameRendering;
            owner.EndFrameRenderingCallback += OnEndFrameRendering;
            _isInitialize = true;
        }

        void Unbind(CameraData owner)
        {
            owner.BeginCameraRenderingCallback -= OnCameraRendering;
            owner.AfterBeginCameraRenderingCallback -= OnAfterBeginCameraRendering;
            owner.EndCameraRenderingCallback -= OnEndCameraRendering;
            owner.BeginFrameRenderingCallback -= OnBeginFrameRendering;
            owner.EndFrameRenderingCallback -= OnEndFrameRendering;
        }
        void Awake() => Initialize(null, true);
        void OnDestroy()
        {
            if (_targetCamera != null) Unbind(_targetCamera.GetCameraData());
            _targetCamera = null;
            BeginFrameRenderingCallack = null;
            EndFrameRenderingCallback = null;
            BeginCameraRenderingCallback = null;
            AfterBeginCameraRenderingCallback = null;
            EndCameraRenderingCallback = null;
        }
        void OnBeginFrameRendering(ScriptableRenderContext context, Camera camera)
        {
            if (enabled && BeginFrameRenderingCallack != null && camera == _targetCamera)
                BeginFrameRenderingCallack(context, _targetCamera);
        }
        void OnEndFrameRendering(ScriptableRenderContext context, Camera camera)
        {
            if (enabled && EndFrameRenderingCallback != null && camera == _targetCamera)
                EndFrameRenderingCallback(context, _targetCamera);
        }
        void OnCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (enabled && BeginCameraRenderingCallback != null && camera == _targetCamera)
                BeginCameraRenderingCallback(context, _targetCamera);
        }
        void OnAfterBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (enabled && AfterBeginCameraRenderingCallback != null && camera == _targetCamera)
                AfterBeginCameraRenderingCallback(context, _targetCamera);
        }
        void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (enabled && EndCameraRenderingCallback != null && camera == _targetCamera)
                EndCameraRenderingCallback(context, _targetCamera);
        }
    }
}
