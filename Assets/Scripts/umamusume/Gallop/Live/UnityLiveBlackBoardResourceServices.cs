using System;
using System.Collections.Generic;
using UnityEngine;
using TargetEntry = Gallop.Live.LiveBlackBoardTargetControl<UnityEngine.MeshRenderer, UnityEngine.Material>.TargetMesh;

namespace Gallop.Live
{
    public sealed class UnityLiveBlackBoardResourceServices : ILiveBlackBoardResourceServices<object>
    {
        readonly Func<Component> uiManager;
        readonly Func<Component, RectTransform> systemCanvas;
        readonly Func<LiveBlackBoardLayerSettings> layers;
        readonly Func<GameObject, object> atlas;
        public UnityLiveBlackBoardResourceServices(Func<Component> uiManager,
            Func<Component, RectTransform> systemCanvas, Func<LiveBlackBoardLayerSettings> layers,
            Func<GameObject, object> atlas)
        {
            this.uiManager = uiManager ?? throw new ArgumentNullException(nameof(uiManager));
            this.systemCanvas = systemCanvas ?? throw new ArgumentNullException(nameof(systemCanvas));
            this.layers = layers ?? throw new ArgumentNullException(nameof(layers));
            this.atlas = atlas ?? throw new ArgumentNullException(nameof(atlas));
        }
        public object NewGameObject(string name) => new GameObject(name);
        public object GetTransform(object obj, bool component) => component ? ((Component)obj).transform : ((GameObject)obj).transform;
        public object GetUIManager() => uiManager();
        public void SetParent(object transform, object parent) => ((Transform)transform).SetParent((Transform)parent);
        public void SetLocalPosition(object transform, Vector3 value) => ((Transform)transform).localPosition = value;
        public void SetLocalScale(object transform, Vector3 value) => ((Transform)transform).localScale = value;
        public Vector3 GetLocalScale(object transform) => ((Transform)transform).localScale;
        public object AddComponent(object obj, string kind)
        {
            if (kind == "Camera") return ((GameObject)obj).AddComponent<Camera>();
            if (kind == "Canvas") return ((GameObject)obj).AddComponent<Canvas>();
            throw new NotSupportedException(kind);
        }
        public void SetInt(object obj, string property, int value)
        {
            switch (property)
            {
                case "clearFlags": ((Camera)obj).clearFlags = (CameraClearFlags)value; break;
                case "cullingMask": ((Camera)obj).cullingMask = value; break;
                case "layer": ((GameObject)obj).layer = value; break;
                case "renderMode": ((Canvas)obj).renderMode = (RenderMode)value; break;
                default: throw new NotSupportedException(property);
            }
        }
        public void SetBool(object obj, string property, bool value)
        {
            switch (property)
            {
                case "orthographic": ((Camera)obj).orthographic = value; break;
                case "allowMSAA": ((Camera)obj).allowMSAA = value; break;
                case "allowHDR": ((Camera)obj).allowHDR = value; break;
                case "useOcclusionCulling": ((Camera)obj).useOcclusionCulling = value; break;
                // Pinned game's deprecated setter is a verified no-op.
                case "isPowerOfTwo": break;
                default: throw new NotSupportedException(property);
            }
        }
        public void SetFloat(object obj, string property, float value)
        {
            if (property != "planeDistance") throw new NotSupportedException(property);
            ((Canvas)obj).planeDistance = value;
        }
        public void SetColor(object obj, Color value) => ((Camera)obj).backgroundColor = value;
        public void SetReference(object obj, string property, object value)
        {
            if (property == "worldCamera") ((Canvas)obj).worldCamera = (Camera)value;
            else if (property == "targetTexture") ((Camera)obj).targetTexture = (RenderTexture)value;
            else throw new NotSupportedException(property);
        }
        public LiveBlackBoardLayerSettings GetLayerSettings() => layers();
        public object GetSystemCanvas(object manager) => systemCanvas((Component)manager);
        public object NewRenderTexture(int width, int height, int depth, int format) => new RenderTexture(width, height, depth, (RenderTextureFormat)format);
        public void SetName(object obj, string name) => ((UnityEngine.Object)obj).name = name;
        public bool CreateRenderTexture(object obj) => ((RenderTexture)obj).Create();
        public object NewTargetList() => new List<TargetEntry>();
        public object GetAtlasReference(object obj) => atlas((GameObject)obj);
        public void Release(object obj) => ((RenderTexture)obj).Release();
        public void Destroy(object obj, bool immediate)
        {
            if (immediate) UnityEngine.Object.DestroyImmediate((UnityEngine.Object)obj);
            else UnityEngine.Object.Destroy((UnityEngine.Object)obj);
        }
        public void ClearTargets(object targets) => ((List<TargetEntry>)targets).Clear();
    }
}
