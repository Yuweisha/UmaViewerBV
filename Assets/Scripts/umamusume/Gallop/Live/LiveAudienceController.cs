using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Gallop.Live.Cutt;
using Gallop.RenderPipeline;

namespace Gallop.Live
{
    // Asset-backed port of Gallop.Cyalume.AudienceController. No replacement crowd,
    // phone or motion geometry is synthesized; prefab identity and clip order survive.
    public sealed class LiveAudienceController : IDisposable
    {
        public GameObject Instance { get; private set; }
        public Vector4 WorldPosition { get; private set; }
        public string PlayingClipName { get; private set; }
        public float PlayingClipTime { get; private set; }
        private Animation animation;
        private Material audienceMaterial, cyalumeBodyMaterial;
        private readonly List<Material> glowMaterials = new List<Material>();
        private readonly List<Material> ownedMaterials = new List<Material>();
        private readonly List<string> clipNames = new List<string>();
        private int playIndex = -1;
        private float playTime = -1f;

        public LiveAudienceController(GameObject prefab, Transform parent, AnimationClip[] clips, bool setLayer = true)
        {
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));
            Instance = UnityEngine.Object.Instantiate(prefab, parent, false);
            foreach (var renderer in Instance.GetComponentsInChildren<Renderer>(true))
                foreach (var material in renderer.materials)
                {
                    if (material == null) continue;
                    ownedMaterials.Add(material);
                    string name = material.name;
                    if (audienceMaterial == null && name.Contains("_audience")) audienceMaterial = material;
                    if (name.Contains("_cyalume_glow")) glowMaterials.Add(material);
                    else if (cyalumeBodyMaterial == null && name.Contains("_cyalume")) cyalumeBodyMaterial = material;
                }
            animation = Instance.GetComponent<Animation>();
            if (clips != null)
            {
                // Native removes the prefab Animation when a replacement array was supplied.
                if (animation != null) UnityEngine.Object.DestroyImmediate(animation);
                animation = Instance.AddComponent<Animation>();
                foreach (var clip in clips)
                {
                    if (clip == null) continue;
                    if (!clip.legacy) throw new InvalidOperationException("Audience source clip must be legacy: " + clip.name);
                    animation.AddClip(clip, clip.name);
                }
            }
            if (animation != null)
            {
                foreach (AnimationState state in animation) clipNames.Add(state.clip.name);
                animation.playAutomatically = false;
                animation.Stop();
            }
            if (setLayer)
            {
                // Original ChangeLayer maps base Default -> live background layer.
                int layer = LayerMask.NameToLayer("BG");
                if (layer >= 0) foreach (var t in Instance.GetComponentsInChildren<Transform>(true))
                    if (t.gameObject.layer == 0) t.gameObject.layer = layer;
            }
        }

        public static float OutlineAlpha(float alpha, int blend)
            => blend == 0 ? LiveAudienceTimeline.Multiply(LiveAudienceTimeline.Subtract(1f, alpha), .5f)
                : blend == 1 ? LiveAudienceTimeline.Add(LiveAudienceTimeline.Multiply(alpha, .5f), .5f) : .5f;

        private static int Property(int index) => ShaderManager.GetPropertyId((ShaderManager.PropertyId)index);
        private static void Color(Material material, int index, UnityEngine.Color value) { material.SetColor(Property(index), value); }
        private static void Float(Material material, int index, float value) { material.SetFloat(Property(index), value); }

        public void UpdateInfo(LiveAudienceTimeline.State state, Func<int?> syncChoreography = null)
        {
            if (Instance == null || state == null) return;
            var v = state.Values;
            Instance.SetActive(state.RenderEnabled);
            Instance.transform.localPosition = v.position;
            Instance.transform.localRotation = state.Rotation;
            Instance.transform.localScale = v.scale;
            // Native 4f42f30 uses x>y ? x : z; preserve that branch exactly.
            float radius = LiveAudienceTimeline.Multiply(v.cyalumeMaskRadius, v.scale.x > v.scale.y ? v.scale.x : v.scale.z);
            var world = Instance.transform.position;
            WorldPosition = new Vector4(world.x, world.y, world.z, radius > 0f ? LiveAudienceTimeline.Divide(1f, radius) : 0f);
            if (cyalumeBodyMaterial != null) Color(cyalumeBodyMaterial, 0x99, v.cyalumeColor);
            foreach (var material in glowMaterials)
            {
                Color(material, 0x94, v.cyalumeGlowColor);
                Float(material, 0x97, v.cyalumeGlowColorPower);
            }
            if (audienceMaterial != null)
            {
                Color(audienceMaterial, 0x11, new UnityEngine.Color(LiveAudienceTimeline.Multiply(v.AudienceColor.r, v.AudienceColorPower),
                    LiveAudienceTimeline.Multiply(v.AudienceColor.g, v.AudienceColorPower), LiveAudienceTimeline.Multiply(v.AudienceColor.b, v.AudienceColorPower), 1f));
                Color(audienceMaterial, 0x13, v.AudienceToonBrightColor);
                Color(audienceMaterial, 0x12, v.AudienceToonDarkColor);
                Float(audienceMaterial, 0x14, v.AudienceVertexColorToonPower);
                Float(audienceMaterial, 0x35, LiveAudienceTimeline.Multiply(v.AudienceOutlineWidthPower, .325f));
                var outline = v.AudienceOutlineColor;
                outline.a = OutlineAlpha(outline.a, v.AudienceOutlineColorBlend);
                Color(audienceMaterial, 0x36, outline);
                Color(audienceMaterial, 0x15, v.AudienceRimColor);
                Float(audienceMaterial, 0x16, v.AudienceRimStep);
                Float(audienceMaterial, 0x17, v.AudienceRimFeather);
                Float(audienceMaterial, 0x18, v.AudienceRimSpecRate);
                Float(audienceMaterial, 0x1a, v.AudienceRimShadowRate);
                Float(audienceMaterial, 0x21, v.AudienceRimHorizonOffset);
                Float(audienceMaterial, 0x22, v.AudienceRimVerticalOffset);
                Color(audienceMaterial, 0x23, v.AudienceRimColor2);
                Float(audienceMaterial, 0x24, v.AudienceRimStep2);
                Float(audienceMaterial, 0x25, v.AudienceRimFeather2);
                Float(audienceMaterial, 0x26, v.AudienceRimSpecRate2);
                Float(audienceMaterial, 0x1b, v.AudienceRimShadowRate2);
                Float(audienceMaterial, 0x27, v.AudienceRimHorizonOffset2);
                Float(audienceMaterial, 0x28, v.AudienceRimVerticalOffset2);
                audienceMaterial.SetInt(Property(0x8a), state.UseDirectionalLight ? 1 : 0);
                audienceMaterial.SetVector(Property(0x8b), state.DirectionalLightRotation * Vector3.back);
            }
            UpdateAnimation(state, syncChoreography);
        }

        private void UpdateAnimation(LiveAudienceTimeline.State state, Func<int?> syncChoreography)
        {
            if (animation == null) return;
            var v = state.Values;
            if (v.animationSetting < 0 || v.animationSetting > 2) return;
            int index = v.animationSetting == 2 ? v.animationIndex : -1;
            if (v.animationSetting == 0 || v.animationSetting == 1)
            {
                string[] regions = { "_", "_r", "_l", "_t" };
                string[] categories = { "_", "_common", "_unique" };
                int category = v.animationCategory, animationIndex = v.animationIndex;
                if (v.animationSetting == 1)
                {
                    int? choreography = syncChoreography?.Invoke();
                    if (!choreography.HasValue || choreography.Value > 7) return;
                    animationIndex = choreography.Value;
                    category = 1;
                }
                string region = v.animationBodyRegion >= 0 && v.animationBodyRegion < regions.Length ? regions[v.animationBodyRegion] : string.Empty;
                string token = category >= 0 && category < categories.Length ? categories[category] : string.Empty;
                string name = "anm_env_live_cmn_audience" + v.animationRootIndex.ToString("D3", CultureInfo.InvariantCulture) +
                    region + token + "_" + animationIndex.ToString("D3", CultureInfo.InvariantCulture);
                index = clipNames.IndexOf(name);
                if (index < 0) return;
            }
            bool changed = index != playIndex;
            if (changed && playIndex >= 0 && playIndex < clipNames.Count) animation.Stop(clipNames[playIndex]);
            playIndex = index;
            if (changed) playTime = 0f;
            if (index < 0 || index >= clipNames.Count) return;
            string selected = clipNames[index];
            var clipState = animation[selected];
            if (clipState == null) return;
            float time = v.UseAnimationTime ? v.AnimationTime : LiveAudienceTimeline.Add(LiveAudienceTimeline.Multiply(v.animationSpeed, state.ProgressTime), v.animationOffsetTime);
            if (!changed && Mathf.Approximately(playTime, time)) return;
            playTime = time;
            clipState.time = time;
            animation.Play(selected);
            clipState.speed = 1f;
            animation.wrapMode = v.animationWrapMode;
            animation.Sample();
            clipState.speed = 0f;
            PlayingClipName = selected; PlayingClipTime = time;
        }

        public void Dispose()
        {
            if (Instance != null) UnityEngine.Object.Destroy(Instance);
            foreach (var material in ownedMaterials) if (material != null) UnityEngine.Object.Destroy(material);
            ownedMaterials.Clear(); glowMaterials.Clear(); clipNames.Clear(); Instance = null;
        }
    }
}
