using System;
using UnityEngine;

namespace Gallop.Live
{
    // Consumption boundary for native GraphicSettings +0x20, not its full installer.
    public sealed class LiveBlackBoardLayerSettings { public int[] Layers; }
    public static class LiveBlackBoardLayerControl
    {
        public static int GetLayer(LiveBlackBoardLayerSettings settings, int index) => settings.Layers[index];
        public static int GetCullingLayer(LiveBlackBoardLayerSettings settings, int index) => 1 << settings.Layers[index];
    }
    public interface ILiveBlackBoardResourceServices<T> where T : class
    {
        T NewGameObject(string name);
        T GetTransform(T obj, bool component);
        T GetUIManager();
        void SetParent(T transform, T parent);
        void SetLocalPosition(T transform, Vector3 value);
        void SetLocalScale(T transform, Vector3 value);
        Vector3 GetLocalScale(T transform);
        T AddComponent(T obj, string kind);
        void SetInt(T obj, string property, int value);
        void SetBool(T obj, string property, bool value);
        void SetFloat(T obj, string property, float value);
        void SetColor(T obj, Color value);
        void SetReference(T obj, string property, T value);
        LiveBlackBoardLayerSettings GetLayerSettings();
        T GetSystemCanvas(T manager);
        T NewRenderTexture(int width, int height, int depth, int format);
        void SetName(T obj, string name);
        bool CreateRenderTexture(T obj);
        T NewTargetList();
        T GetAtlasReference(T obj);
        void Release(T obj);
        void Destroy(T obj, bool immediate);
        void ClearTargets(T targets);
    }
    // Full Initialize / SetAtlas / UnloadNameTextureResource parent bodies.
    // UIManager, atlas resolution and engine operations are supplied bindings.
    public sealed class LiveBlackBoardResources<T> where T : class
    {
        public T CanvasObject, Canvas, CameraObject, Camera, Atlas, RenderTexture, Targets;
        public T[] Images;
        readonly ILiveBlackBoardResourceServices<T> source;
        public LiveBlackBoardResources(ILiveBlackBoardResourceServices<T> source) { this.source = source; }
        static T Require(T value) => value ?? throw new NullReferenceException();
        public void Initialize()
        {
            CameraObject = source.NewGameObject("TextRenderCamera");
            T cameraTransform = source.GetTransform(Require(CameraObject), false);
            T manager = Require(source.GetUIManager());
            T parent = source.GetTransform(manager, true);
            source.SetParent(Require(cameraTransform), parent);
            cameraTransform = source.GetTransform(Require(CameraObject), false);
            source.SetLocalPosition(Require(cameraTransform), new Vector3(0, 0, -1));
            Camera = source.AddComponent(Require(CameraObject), "Camera");
            source.SetInt(Require(Camera), "clearFlags", 2);
            source.SetColor(Require(Camera), new Color(1, 1, 1, 0));
            T cameraForMask = Camera;
            int mask = LiveBlackBoardLayerControl.GetCullingLayer(source.GetLayerSettings(), 11);
            source.SetInt(Require(cameraForMask), "cullingMask", mask);
            source.SetBool(Require(Camera), "orthographic", true);
            source.SetBool(Require(Camera), "allowMSAA", false);
            source.SetBool(Require(Camera), "allowHDR", false);
            source.SetBool(Require(Camera), "useOcclusionCulling", false);
            CanvasObject = source.NewGameObject("TextRenderCanvas");
            T canvasTransform = source.GetTransform(Require(CanvasObject), false);
            manager = Require(source.GetUIManager());
            parent = source.GetTransform(manager, true);
            source.SetParent(Require(canvasTransform), parent);
            T canvasForLayer = CanvasObject;
            int layer = LiveBlackBoardLayerControl.GetLayer(source.GetLayerSettings(), 11);
            source.SetInt(Require(canvasForLayer), "layer", layer);
            Canvas = source.AddComponent(Require(CanvasObject), "Canvas");
            canvasTransform = source.GetTransform(Require(Canvas), true);
            manager = Require(source.GetUIManager());
            T systemCanvas = Require(source.GetSystemCanvas(manager));
            Vector3 scale = source.GetLocalScale(systemCanvas);
            source.SetLocalScale(Require(canvasTransform), scale);
            source.SetInt(Require(Canvas), "renderMode", 1);
            source.SetReference(Require(Canvas), "worldCamera", Camera);
            source.SetFloat(Require(Canvas), "planeDistance", 10);
            RenderTexture = source.NewRenderTexture(32, 256, 0, 0);
            source.SetName(Require(RenderTexture), "FontTexture");
            source.SetBool(Require(RenderTexture), "isPowerOfTwo", true);
            source.CreateRenderTexture(Require(RenderTexture)); // Native ignores the BOOL.
            source.SetReference(Require(Camera), "targetTexture", RenderTexture);
            Targets = source.NewTargetList();
        }
        public void SetAtlas(T obj) { Atlas = source.GetAtlasReference(Require(obj)); }
        public void UnloadNameTextureResource()
        {
            source.Release(Require(RenderTexture));
            source.Destroy(RenderTexture, false);
            source.Destroy(CanvasObject, true);
            source.Destroy(Camera, true);
            RenderTexture = null; Atlas = null; Camera = null; CanvasObject = null; Canvas = null;
            source.Destroy(CameraObject, false);
            CameraObject = null;
            source.ClearTargets(Require(Targets));
            Targets = null;
        }
    }
}
