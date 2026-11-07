using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gallop.RenderPipeline
{
    [DisallowMultipleComponent]
    public class CustomLensFlare : MonoBehaviour
    {
        // Original resources now feed the recovered manager/draw, not two renderers.
        // Legacy mode is retained solely for explicit offline regression comparisons.
        public static bool UseNativeRenderer = true;
        private static readonly HashSet<CustomLensFlare> sNativeSources = new HashSet<CustomLensFlare>();
        public static IReadOnlyCollection<CustomLensFlare> NativeSources => sNativeSources;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetNativeSources()
        {
            sNativeSources.Clear();
            UseNativeRenderer = true;
        }

        public static float RuntimeStrength = 1f;
        public static float RuntimeScale = 1f;
        public static float RuntimeAttenuationDistance = 500f;
        // to SRP depth occlusion, which can reject a source placed inside its lamp mesh.
        public static bool RuntimeOcclusion = true;
        // Legacy Flare sizes are authored in a different screen-space convention
        // than URP LensFlareDataSRP. A direct 1.0 mapping produces a near-full
        // frame white disc on several live stages.
        private const float kMaxElementScale = 1f;
        private const float kMaxIntensity = 1f;
        private const string kRuntimeSourceName = "__RuntimeLensFlare";
        public LensFlareData Flare;
        public float Brightness = 1f;
        public float FadeSpeed = 1f;
        public Color Color = Color.white;
        public LayerMask IgnoreLayers;
        public bool IsDirectional;
        private LensFlareComponentSRP _component;
        private LensFlareDataSRP _runtimeData;
        private float _lastRuntimeStrength = -1f;
        private float _lastRuntimeScale = -1f;
        private float _lastRuntimeAttenuationDistance = -1f;
        private bool _lastRuntimeOcclusion;
        private LensFlareData _cachedAtlasData;
        private sealed class AtlasCacheEntry
        {
            public Texture[] textures;
            public int references;
        }

        private static readonly Dictionary<LensFlareData, AtlasCacheEntry> sAtlasCache = new Dictionary<LensFlareData, AtlasCacheEntry>();
        private static readonly HashSet<Texture> sLoggedTextureSamples = new HashSet<Texture>();
        private void OnEnable()
        {
            sNativeSources.Add(this);
            if (UseNativeRenderer)
            {
                if (_component != null)
                    _component.enabled = false;
                return;
            }

            BuildRuntimeFlare();
            ApplyRuntimeParameters();
            if (_component != null)
                _component.enabled = true;
        }

        private void Update()
        {
            if (UseNativeRenderer)
            {
                if (_component != null)
                    _component.enabled = false;
                return;
            }

            if (_runtimeData == null || _component == null)
                return;
            if (!Mathf.Approximately(_lastRuntimeStrength, RuntimeStrength) || !Mathf.Approximately(_lastRuntimeScale, RuntimeScale) || !Mathf.Approximately(_lastRuntimeAttenuationDistance, RuntimeAttenuationDistance) || _lastRuntimeOcclusion != RuntimeOcclusion)
                ApplyRuntimeParameters();
        }

        private void OnDisable()
        {
            sNativeSources.Remove(this);
            if (_component != null)
                _component.enabled = false;
        }

        private void OnDestroy()
        {
            sNativeSources.Remove(this);
            if (_component != null)
            {
                _component.enabled = false;
                _component.lensFlareData = null;
                Destroy(_component.gameObject);
            }

            if (_runtimeData != null)
                Destroy(_runtimeData);
            ReleaseAtlas();
        }

        public void ApplyTimeline(bool enabled, bool overridePosition, Vector3 offset, Color color, float brightness, float fadeSpeed)
        {
            if (!UseNativeRenderer && (_component == null || _runtimeData == null))
                BuildRuntimeFlare();
            if (_component != null)
                _component.transform.localPosition = overridePosition ? offset : Vector3.zero;
            Color = color;
            Brightness = Mathf.Max(0f, brightness);
            FadeSpeed = Mathf.Max(0f, fadeSpeed);
            // Original UnityLensFlareController.AlterUpdate gates the CustomLensFlare
            // component itself. Disabled sources must lose visibility history too.
            if (UseNativeRenderer)
                this.enabled = enabled;
            if (!UseNativeRenderer)
                ApplyRuntimeParameters();
            if (_component != null)
                _component.enabled = !UseNativeRenderer && enabled;
        }

        private void BuildRuntimeFlare()
        {
            // Missing authored resources must remain missing, never become a procedural flare.
            if (Flare == null || Flare.Texture == null)
                return;
            Transform runtimeSource = transform.Find(kRuntimeSourceName);
            if (runtimeSource == null)
            {
                var sourceObject = new GameObject(kRuntimeSourceName);
                runtimeSource = sourceObject.transform;
                runtimeSource.SetParent(transform, false);
            }

            runtimeSource.gameObject.layer = gameObject.layer;
            _component = runtimeSource.GetComponent<LensFlareComponentSRP>();
            if (_component == null)
                _component = runtimeSource.gameObject.AddComponent<LensFlareComponentSRP>();
            // AddComponent invokes LensFlareComponentSRP.OnEnable immediately. At that
            // point lensFlareData is still null, so URP removes this component from
            // LensFlareCommonSRP. Disable it before assigning data; the caller enables
            // it after setup, making OnEnable register the completed component.
            _component.enabled = false;
            if (_runtimeData != null)
                Destroy(_runtimeData);
            _runtimeData = ScriptableObject.CreateInstance<LensFlareDataSRP>();
            var source = Flare.ElementArray ?? Array.Empty<LensFlareElement>();
            var elements = new LensFlareDataElementSRP[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                LensFlareElement item = source[i] ?? new LensFlareElement();
                var element = new LensFlareDataElementSRP
                {
                    flareType = SRPLensFlareType.Image,
                    lensFlareTexture = GetElementTexture(item.ImageIndex),
                    position = item.Position,
                    uniformScale = Mathf.Clamp(item.Size, 0.001f, kMaxElementScale),
                    sizeXY = Vector2.one,
                    tint = ComposeTint(item.Color),
                    autoRotate = item.Rotate,
                    modulateByLightColor = item.UseLightColor,
                    blendMode = SRPLensFlareBlendMode.Additive,
                    preserveAspectRatio = true
                };
                elements[i] = element;
            }

            _runtimeData.elements = elements;
            _component.lensFlareData = _runtimeData;
            _component.useOcclusion = RuntimeOcclusion;
            _component.allowOffScreen = false;
            _component.attenuationByLightShape = false;
            // Do not shrink the flare with distance. The previous double fade
            // attenuated both area and brightness and made the authored starburst
            // disappear into the lamp mesh. Retain intensity falloff only.
            _component.distanceAttenuationCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            _component.scaleByDistanceCurve = AnimationCurve.Constant(0f, 1f, 1f);
        }

        private void ApplyRuntimeParameters()
        {
            if (_component == null || _runtimeData == null)
                return;
            _component.intensity = Mathf.Min(kMaxIntensity, Mathf.Max(0f, Brightness)) * Mathf.Clamp(RuntimeStrength, 0f, 2f);
            _lastRuntimeStrength = RuntimeStrength;
            _component.maxAttenuationDistance = Mathf.Clamp(RuntimeAttenuationDistance, 10f, 1000f);
            _lastRuntimeAttenuationDistance = RuntimeAttenuationDistance;
            _component.useOcclusion = RuntimeOcclusion;
            _lastRuntimeOcclusion = RuntimeOcclusion;
            for (int i = 0; i < _runtimeData.elements.Length; i++)
            {
                LensFlareElement source = Flare != null && Flare.ElementArray != null && i < Flare.ElementArray.Length ? Flare.ElementArray[i] : null;
                Color sourceColor = source != null ? source.Color : UnityEngine.Color.white;
                _runtimeData.elements[i].tint = ComposeTint(sourceColor);
                float sourceScale = source != null ? source.Size : 1f;
                _runtimeData.elements[i].uniformScale = Mathf.Clamp(sourceScale, 0.001f, kMaxElementScale) * Mathf.Clamp(RuntimeScale, 0.1f, 3f);
            }

            _lastRuntimeScale = RuntimeScale;
        }

        private static string FormatImageIndexCounts(Dictionary<int, int> counts)
        {
            if (counts == null || counts.Count == 0)
                return "<none>";
            var values = new List<string>();
            foreach (var pair in counts)
                values.Add($"{pair.Key}:{pair.Value}");
            return string.Join(",", values);
        }

        private static void LogTextureSample(Texture texture)
        {
            try
            {
                var rt = RenderTexture.GetTemporary(8, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                Graphics.Blit(texture, rt);
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = rt;
                var probe = new Texture2D(8, 8, TextureFormat.RGBA32, false, true);
                probe.ReadPixels(new Rect(0, 0, 8, 8), 0, 0, false);
                probe.Apply(false, false);
                Color32[] pixels = probe.GetPixels32();
                byte minAlpha = 255, maxAlpha = 0;
                float maxLuma = 0f;
                foreach (Color32 pixel in pixels)
                {
                    minAlpha = System.Math.Min(minAlpha, pixel.a);
                    maxAlpha = System.Math.Max(maxAlpha, pixel.a);
                    maxLuma = Mathf.Max(maxLuma, (pixel.r + pixel.g + pixel.b) / (255f * 3f));
                }

                Debug.Log($"[LensFlareTexture] {texture.name} {texture.width}x{texture.height} " + $"alpha={minAlpha}-{maxAlpha} maxLuma={maxLuma:0.###}");
                UnityEngine.Object.Destroy(probe);
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
            catch (Exception ex)
            {
                Debug.Log($"[LensFlareTexture] {texture.name} sample failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private Color ComposeTint(Color elementColor)
        {
            // Legacy flare assets use alpha=0 for their single-image layout.
            // It is not an opacity channel there: live10102's 163 flare
            // components and both referenced flare assets all carry zero alpha.
            // URP LensFlareDataSRP does use tint alpha for compositing, so retain
            // the authored RGB modulation while supplying the required opacity.
            return new Color(elementColor.r * Color.r, elementColor.g * Color.g, elementColor.b * Color.b, 1f);
        }

        private Texture GetElementTexture(int imageIndex)
        {
            if (Flare == null || Flare.Texture == null || Flare.TextureLayout != 0)
                return Flare != null ? Flare.Texture : null;
            if (_cachedAtlasData != Flare)
            {
                ReleaseAtlas();
                if (!sAtlasCache.TryGetValue(Flare, out var entry))
                {
                    entry = new AtlasCacheEntry
                    {
                        textures = SliceOneLargeFourSmall(Flare.Texture)
                    };
                    sAtlasCache.Add(Flare, entry);
                }

                entry.references++;
                _cachedAtlasData = Flare;
            }

            AtlasCacheEntry cached = sAtlasCache[Flare];
            return imageIndex >= 0 && imageIndex < cached.textures.Length ? cached.textures[imageIndex] : Flare.Texture;
        }

        private static Texture[] SliceOneLargeFourSmall(Texture source)
        {
            int largeWidth = Mathf.Max(1, source.width);
            int largeHeight = Mathf.Max(1, source.height / 2);
            int smallWidth = Mathf.Max(1, source.width / 2);
            int smallHeight = Mathf.Max(1, source.height / 4);
            return new Texture[]
            {
                CreateSlice(source, largeWidth, largeHeight, new Vector2(1f, 0.5f), new Vector2(0f, 0.5f), 0),
                CreateSlice(source, smallWidth, smallHeight, new Vector2(0.5f, 0.25f), new Vector2(0f, 0.25f), 1),
                CreateSlice(source, smallWidth, smallHeight, new Vector2(0.5f, 0.25f), new Vector2(0.5f, 0.25f), 2),
                CreateSlice(source, smallWidth, smallHeight, new Vector2(0.5f, 0.25f), Vector2.zero, 3),
                CreateSlice(source, smallWidth, smallHeight, new Vector2(0.5f, 0.25f), new Vector2(0.5f, 0f), 4)
            };
        }

        private static RenderTexture CreateSlice(Texture source, int width, int height, Vector2 scale, Vector2 offset, int imageIndex)
        {
            var texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default)
            {
                name = source.name + "_FlareImage" + imageIndex,
                filterMode = source.filterMode,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            texture.Create();
            Graphics.Blit(source, texture, scale, offset);
            return texture;
        }

        private void ReleaseAtlas()
        {
            if (_cachedAtlasData == null || !sAtlasCache.TryGetValue(_cachedAtlasData, out var entry))
            {
                _cachedAtlasData = null;
                return;
            }

            entry.references--;
            if (entry.references <= 0)
            {
                foreach (Texture texture in entry.textures)
                    if (texture != null)
                        Destroy(texture);
                sAtlasCache.Remove(_cachedAtlasData);
            }

            _cachedAtlasData = null;
        }
    }
}
