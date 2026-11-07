using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using UnityObject = UnityEngine.Object;
using Gallop.Live.Cutt;

namespace Gallop.Live
{
    public interface ILiveFlashController
    {
        bool IsInitialized { get; }

        void SetParent(Transform parent, bool worldPositionStays);
        void Destroy();
        void AlterUpdate();
        void PlayLabel(string label, float currentTime, object curData = null);
        void UpdateOrientationLandscape();
        void UpdateOrientationPortrait();
        void Reset();
    }

    public class LiveFlashController : MonoBehaviour, ILiveFlashController
    {
        Canvas canvas;
        Camera camera, cameraOwner;
        GameObject flashRoot;
        Gallop.FlashPlayer player;
        Gallop.FlashActionPlayer actionPlayer;
        object previousFlashKey;
#if UNITY_EDITOR
        public Action<string, string, string, string> FlashSeObserver;
#endif
        protected Gallop.FlashPlayer FlashPlayer => player;
        protected Gallop.FlashActionPlayer FlashActionPlayer => actionPlayer;
        protected GameObject FlashRoot => flashRoot;
        protected Font CurrentFlashFont { get; private set; }
        public bool IsInitialized { get; private set; }
        public Camera RenderingCamera => camera;

        public virtual void Initialize(string resourcePath)
        {
            if (IsInitialized)
                Destroy();
            transform.position = new Vector3(0f, 0f, 100f);
            var cameraObject = new GameObject("FlashCamera", typeof(Camera), typeof(UniversalAdditionalCameraData));
            cameraObject.transform.SetParent(transform, false);
            camera = cameraObject.GetComponent<Camera>();
            // Native CreateFlashCamera keeps Camera's default orthographic size.
            // Canvas maps pixel-space A2U geometry into this camera. Local-mode
            // particles ignore the Canvas ancestor scale, so a 540-unit camera
            // incorrectly reduces authored background dust to subpixel specks.
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.clearFlags = CameraClearFlags.Depth;
            camera.nearClipPlane = -20f;
            camera.farClipPlane = 70f;
            camera.depth = 18f;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.allowDynamicResolution = false;
            camera.useOcclusionCulling = false;
            camera.cullingMask = 1 << LayerMask.NameToLayer("UI");
            var data = cameraObject.GetComponent<UniversalAdditionalCameraData>();
            data.renderType = CameraRenderType.Overlay;
            data.renderPostProcessing = false;
            data.renderShadows = false;
            var gallopData = cameraObject.AddComponent<Gallop.RenderPipeline.CameraData>();
            gallopData.IsUIRendering = true;
            gallopData.RequestAntiAliasing = Gallop.RenderPipeline.CameraData.AntiAliasLevel.None;
            var canvasObject = new GameObject("FlashCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasObject.layer = LayerMask.NameToLayer("UI");
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 10f;
            canvas.sortingLayerName = "UI";
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var prefab = LoadOnView<GameObject>(resourcePath);
            if (prefab == null)
                throw new InvalidOperationException("Live Flash prefab missing: " + resourcePath);
            flashRoot = Instantiate(prefab, canvas.transform, false);
            flashRoot.name = prefab.name;
            actionPlayer = flashRoot.GetComponent<Gallop.FlashActionPlayer>();
            if (actionPlayer != null)
            {
                actionPlayer.LoadFlashPlayer();
                player = actionPlayer.FlashPlayer;
                actionPlayer.SetKeepInstanceAll(true);
                actionPlayer.OnInstantiatedParticles = AdjustParticleAspect;
            }
            else
                player = new Gallop.FlashPlayer(flashRoot);
            foreach (var node in player.Root.Objects)
                node.Parameter._placeAnchorScaleType = AnimateToUnity.AnPlaceAnchorScaleTypes.Fix;
            player.Root._ignoreMargin = true;
            player.Pause(true);
            IsInitialized = true;
            LiveSubtitleSettings.Changed += ApplySubtitleVisibility;
            ApplySubtitleVisibility(LiveSubtitleSettings.Enabled);
            UpdateCamera();
        }

        void AdjustParticleAspect(ParticleSystem[] particles)
        {
            float scale = Mathf.Min(camera.aspect / (16f / 9f), 1f);
            foreach (var particle in particles)
            {
                if (particle.name.Contains("root"))
                    continue;
                var previous = particle.transform.localScale;
                particle.transform.localScale = new Vector3(scale, scale, previous.z);
            }
        }

        public void SetParent(Transform parent, bool worldPositionStays) => transform.SetParent(parent, worldPositionStays);
        void OnDestroy() => Destroy();
        public void Destroy()
        {
            LiveSubtitleSettings.Changed -= ApplySubtitleVisibility;
            previousFlashKey = null;
            DestroySub();
            if (cameraOwner != null)
                cameraOwner.GetUniversalAdditionalCameraData().cameraStack.Remove(camera);
            cameraOwner = null;
            if (flashRoot != null)
                UnityObject.Destroy(flashRoot);
            if (canvas != null)
                UnityObject.Destroy(canvas.gameObject);
            if (camera != null)
                UnityObject.Destroy(camera.gameObject);
            flashRoot = null;
            canvas = null;
            camera = null;
            player = null;
            actionPlayer = null;
            IsInitialized = false;
        }

        protected virtual void DestroySub()
        {
        }

        void ApplySubtitleVisibility(bool enabled)
        {
            // Keep sampling the authored timeline while hidden, so re-enabling
            // subtitles resumes at the current card instead of restarting it.
            if (flashRoot != null)
                flashRoot.SetActive(enabled);
            if (camera != null)
                camera.enabled = enabled;
            if (!enabled)
                Director.instance?.FlashSeSource?.Stop();
        }

        void UpdateCamera()
        {
            var owner = Director.instance != null ? Director.instance.MainRenderCamera : null;
            if (owner != null)
            {
                if (cameraOwner != owner)
                {
                    if (cameraOwner != null)
                        cameraOwner.GetUniversalAdditionalCameraData().cameraStack.Remove(camera);
                    cameraOwner = owner;
                }

                var stack = owner.GetUniversalAdditionalCameraData().cameraStack;
                // Flash UI is the last scene overlay.
                if (stack.Count == 0 || stack[stack.Count - 1] != camera)
                {
                    stack.Remove(camera);
                    stack.Add(camera);
                }

                camera.targetTexture = owner.targetTexture;
                camera.aspect = owner.aspect;
            }

            float aspect = camera.aspect;
            player.Root.SetViewportSize(aspect >= 16f / 9f ? new Vector2(1080f * aspect, 1080f) : new Vector2(1920f, 1920f / aspect));
        }

        public void AlterUpdate()
        {
            if (IsInitialized)
            {
                UpdateCamera();
                AlterUpdatePlayer();
                actionPlayer?.UpdateParticle();
            }
        }

        // Native LiveChampionsTextController.AlterUpdate only updates action particles;
        // its root clock stays frozen and is re-seeked from the Live timeline.
        protected virtual void AlterUpdatePlayer()
        {
            player.AlterUpdate();
        }

        public virtual void PlayLabel(string label, float currentTime, object curData = null)
        {
            if (!IsInitialized || string.IsNullOrEmpty(label))
                return;

            if (actionPlayer != null)
            {
                UpdateFlashSe(label, curData);
                actionPlayer.PlaySeek(label, currentTime);
            }
            else
            {
                var motion = player.Root.RootMotion;
                motion.Play(label);
                player.SetCurrentTimeAllMotion(currentTime);
            }
        }

        protected void UpdateFlashSe(string label, object keyData)
        {
            bool changed = !ReferenceEquals(previousFlashKey, keyData);
            previousFlashKey = keyData;
            var key = keyData as LiveTimelineKeyFlashPlayerData;
            if (!LiveSubtitleSettings.Enabled || !Director.IsLiveCall || !changed || key == null || string.IsNullOrEmpty(key.CueSheetName) || string.IsNullOrEmpty(key.CueName))
                return;
            var motion = player.Root.RootMotion;
            motion.SetAction(label, 0, () =>
            {
                var director = Director.instance;
                if (director == null || !director.IsStarted)
                    return;
                director.TryPlayFlashSe(key.CueSheetName, key.CueName);
#if UNITY_EDITOR
                FlashSeObserver?.Invoke("play", label, key.CueSheetName, key.CueName);
#endif
                motion.RemoveAction(label, 0);
#if UNITY_EDITOR
                FlashSeObserver?.Invoke("remove", label, key.CueSheetName, key.CueName);
#endif
            });
#if UNITY_EDITOR
            FlashSeObserver?.Invoke("setup", label, key.CueSheetName, key.CueName);
#endif
        }

        public void UpdateOrientationLandscape()
        {
        }

        public void UpdateOrientationPortrait()
        {
        }

        public void Reset()
        {
            if (actionPlayer != null)
                actionPlayer.Reset();
            else
                player?.Reset();
        }

        protected T LoadOnView<T>(string path)
            where T : UnityObject => LiveFlashResourceUtility.LoadOnView<T>(path);
        protected void SetCurrentFlashFont(Font font) => CurrentFlashFont = font;
        protected bool ReplaceFlashFont(string name, Font font)
        {
            SetCurrentFlashFont(font);
            Gallop.FlashPlayer.RegisterFont(name, font);
            player?.ReplaceFont(name, font);
            return true;
        }

        protected string GetFlashRootObjectName(string name) => player?.GetObj(name)?.name;
        protected bool SetFlashText(string text, string target, bool recursive, string root) => player != null && player.SetText(text, target, recursive, root);
        protected bool SetFlashTexture(string target, Texture texture, int index, bool flag0, bool flag1, string root) => player != null && player.SetTexture(target, texture, index, flag0, flag1, root);
    }

    internal static class LiveFlashResourceUtility
    {
        public static T LoadOnView<T>(string path)
            where T : UnityObject
        {
            if (string.IsNullOrEmpty(path))
                return null;
            T res = Resources.Load<T>(path);
            if (res != null)
                return res;
            UmaDatabaseEntry entry = FindEntry(path);
            if (entry == null)
                return null;
            AssetBundle bundle = null;
            try
            {
                bundle = UmaAssetManager.LoadAssetBundle(entry, neverUnload: true, isRecursive: true);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LiveFlashResourceUtility] LoadAssetBundle failed: " + path + "\n" + ex.Message);
            }

            if (bundle == null)
                return null;
            return LoadAssetFromBundle<T>(bundle, path);
        }

        private static UmaDatabaseEntry FindEntry(string path)
        {
            var main = UmaViewerMain.Instance;
            if (main == null || main.AbList == null)
                return null;
            UmaDatabaseEntry entry;
            if (main.AbList.TryGetValue(path, out entry) && entry != null)
                return entry;
            string normalizedPath = Normalize(path);
            string fileName = Normalize(Path.GetFileName(path));
            foreach (KeyValuePair<string, UmaDatabaseEntry> kv in main.AbList)
            {
                if (kv.Value == null)
                    continue;
                string key = Normalize(kv.Key);
                string name = Normalize(kv.Value.Name);
                string nameFile = Normalize(Path.GetFileName(kv.Value.Name ?? string.Empty));
                if (key == normalizedPath || name == normalizedPath)
                    return kv.Value;
                if (key.EndsWith("/" + normalizedPath, StringComparison.OrdinalIgnoreCase) || name.EndsWith("/" + normalizedPath, StringComparison.OrdinalIgnoreCase))
                    return kv.Value;
                if (!string.IsNullOrEmpty(fileName) && (nameFile == fileName || key.EndsWith("/" + fileName, StringComparison.OrdinalIgnoreCase) || name.EndsWith("/" + fileName, StringComparison.OrdinalIgnoreCase)))
                    return kv.Value;
            }

            return null;
        }

        private static T LoadAssetFromBundle<T>(AssetBundle bundle, string requestedPath)
            where T : UnityObject
        {
            string requested = Normalize(requestedPath);
            string requestedFile = Normalize(Path.GetFileNameWithoutExtension(requestedPath));
            string[] names = null;
            try
            {
                names = bundle.GetAllAssetNames();
            }
            catch
            {
            }

            if (names != null)
            {
                string best = null;
                for (int i = 0; i < names.Length; i++)
                {
                    string n = Normalize(names[i]);
                    string nf = Normalize(Path.GetFileNameWithoutExtension(n));
                    if (n == requested || n.EndsWith("/" + requested, StringComparison.OrdinalIgnoreCase) || nf == requestedFile)
                    {
                        best = names[i];
                        break;
                    }
                }

                if (string.IsNullOrEmpty(best))
                {
                    for (int i = 0; i < names.Length; i++)
                    {
                        string n = Normalize(names[i]);
                        string nf = Normalize(Path.GetFileNameWithoutExtension(n));
                        if ((!string.IsNullOrEmpty(requestedFile) && nf.Contains(requestedFile)) || n.Contains(requested))
                        {
                            best = names[i];
                            break;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(best))
                {
                    try
                    {
                        T asset = bundle.LoadAsset<T>(best);
                        if (asset != null)
                            return asset;
                    }
                    catch
                    {
                    }
                }
            }

            try
            {
                T[] all = bundle.LoadAllAssets<T>();
                if (all != null && all.Length > 0)
                    return all[0];
            }
            catch
            {
            }

            return null;
        }

        private static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;
            return s.Replace('\\', '/').Trim().TrimStart('/').ToLowerInvariant();
        }
    }
}
