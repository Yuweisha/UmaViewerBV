using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live
{
    // GallopFrameBuffer's Mini-only path: copyUI=false, no borrowed backup.
    // The host supplies GraphicSettings' existence and GetVirtualResolution3D
    // result; UI copy, backup surfaces and resolution-change owners are separate.
    public sealed class LiveMiniFrameBuffer
    {
        readonly string _bufferName;
        List<Camera> _cameras;
        RenderTexture _colorBuffer;
        public bool IsInitialized { get; private set; }
        public RenderTexture ColorBuffer => _colorBuffer;
        public bool IsEnable { get; private set; }
        public event Action<LiveMiniFrameBuffer, Camera> OnChange;
#if UNITY_EDITOR
        public event Action<string, UnityEngine.Object> EngineWrite;
#endif
        public LiveMiniFrameBuffer(string bufferName) { _bufferName = bufferName; }

        public void Initialize(bool hasGraphicsSettings, Func<Vector2Int> getVirtualResolution3D)
        {
            if (IsInitialized || !hasGraphicsSettings) return;
            _cameras = new List<Camera>(4);
            var size = getVirtualResolution3D();
            _colorBuffer = new RenderTexture(size.x, size.y, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
#if UNITY_EDITOR
            EngineWrite?.Invoke("RenderTexture.ctor", _colorBuffer);
#endif
            _colorBuffer.useMipMap = false;
            _colorBuffer.autoGenerateMips = false;
            _colorBuffer.filterMode = FilterMode.Bilinear;
            _colorBuffer.Create();
            Shader.SetGlobalTexture(_bufferName, _colorBuffer);
#if UNITY_EDITOR
            EngineWrite?.Invoke("Shader.SetGlobalTexture", _colorBuffer);
#endif
            IsEnable = true;
            IsInitialized = true;
        }

        public void AddCamera(Camera camera)
        {
            foreach (var existing in _cameras) if (existing == camera) return;
            camera.targetTexture = _colorBuffer;
#if UNITY_EDITOR
            EngineWrite?.Invoke("Camera.targetTexture", camera);
#endif
            OnChange?.Invoke(this, camera);
            _cameras.Add(camera);
        }

        public void Resize(int width, int height)
        {
            if (!IsInitialized || (_colorBuffer.width == width && _colorBuffer.height == height)) return;
            var previous = _colorBuffer;
            _colorBuffer = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default)
            {
                useMipMap = false, autoGenerateMips = false, filterMode = FilterMode.Bilinear
            };
            _colorBuffer.Create();
            Shader.SetGlobalTexture(_bufferName, _colorBuffer);
            foreach (var camera in _cameras)
            {
                camera.targetTexture = _colorBuffer;
                OnChange?.Invoke(this, camera);
            }
            previous.Release();
            UnityEngine.Object.Destroy(previous);
        }

        public void Release()
        {
            if (!IsInitialized) return;
            foreach (var camera in _cameras)
            {
                if (camera == null) continue;
                camera.targetTexture = null;
#if UNITY_EDITOR
                EngineWrite?.Invoke("Camera.targetTexture", camera);
#endif
                camera.enabled = false;
#if UNITY_EDITOR
                EngineWrite?.Invoke("Camera.enabled", camera);
#endif
            }
            _cameras.Clear();
            if (_colorBuffer != null)
            {
                _colorBuffer.Release();
#if UNITY_EDITOR
                EngineWrite?.Invoke("RenderTexture.Release", _colorBuffer);
                EngineWrite?.Invoke("Object.Destroy", _colorBuffer);
#endif
                UnityEngine.Object.Destroy(_colorBuffer);
                _colorBuffer = null;
            }
            // Native does not clear the global texture binding or restore the
            // cameras' old targets. CopySurface.IsEnable was false for Mini.
            IsInitialized = false;
        }
    }
}
