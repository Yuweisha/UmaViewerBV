using System;
using System.Collections.Generic;
using Gallop.Live.Cutt;
using UnityEngine;

namespace Gallop.RenderPipeline
{
    // Stage adapter. Registration/gates and same-input camera/color arithmetic
    // were rechecked against game 2.32 AddLensFlareControllerArray @ 4ff76f0
    // and UnityLensFlareController.AlterUpdate @ 50798e0. This does not establish
    // equivalence of full-Live inputs or rendered output.
    // Historical AddLensFlareControllerArray 0x1a7f8b0 / UpdateLensFlare 0x1a91360.
    // This adapter consumes authored CustomLensFlare sources, never creates substitute light effects.
    public sealed class RecoveredLensFlareController
    {
#if UNITY_EDITOR
        public static Action<string> SamplingObserver;
#endif
        private sealed class Target
        {
            public CustomLensFlare Flare;
            public Gallop.Live.UnityLensFlareController AngleOwner;
            public Transform Transform;
            public Vector3 InitialPosition;
            public int NameHash;
            public Material Material;
            public MeshRenderer Renderer;
            public bool HasColorPower, HasMulColor0;
            public Color ColorFactor = Color.white;
            public float BrightnessFactor = 1f;
            public bool AutoBrightness = true;
            public bool ScreenFit;
            public bool HasBlinkColor;
            public Color BlinkColor;
#if UNITY_EDITOR
            public Vector3 ObservedUp, ObservedCameraForward;
            public int ObservedUnityFrame;
#endif
        }

        private readonly Dictionary<int, List<Target>> _groups = new Dictionary<int, List<Target>>();
        private readonly List<Target> _targets = new List<Target>();
        private readonly Dictionary<CustomLensFlare, Target> _bySource = new Dictionary<CustomLensFlare, Target>();
        private readonly Plane[] _frustumPlanes = new Plane[6];
        private readonly MaterialPropertyBlock _lightProperties = new MaterialPropertyBlock();
        private readonly float _standard;
        private readonly float _under;
        private readonly int _colorPowerId;
        private readonly int _mulColor0Id;

        public RecoveredLensFlareController(Transform root, float standard, float under,
            string nameSuffixToken = "")
        {
            _standard = standard;
            _under = under;
            _colorPowerId = ShaderManager.GetPropertyId(ShaderManager.PropertyId._ColorPower);
            _mulColor0Id = ShaderManager.GetPropertyId(ShaderManager.PropertyId._MulColor0);
            AddSources(root, nameSuffixToken);
        }

        public void AddSources(Transform root, string nameSuffixToken)
        {
            if (root == null) return;
            // Stage.AddLensFlareControllerArray registers active controller hosts.
            // A standalone CustomLensFlare stays under its authored owner.
            foreach (var angleOwner in root.GetComponentsInChildren<Gallop.Live.UnityLensFlareController>())
            {
                var flare = angleOwner.GetComponent<CustomLensFlare>();
                if (flare == null) continue;
                var sourceTransform = flare.transform;
                angleOwner.CalcEnableAngleCos();
                angleOwner.UseLensFlare(Gallop.Live.LiveQualitySettings.IsLensflare);
                var renderer = flare.GetComponent<MeshRenderer>();
                Material material = RenderUtils.GetMaterial(renderer);
                var target = new Target
                {
                    Flare = flare,
                    AngleOwner = angleOwner,
                    Transform = sourceTransform,
                    InitialPosition = sourceTransform.localPosition,
                    NameHash = FNVHash.Generate(flare.name + nameSuffixToken),
                    Material = material,
                    Renderer = renderer,
                    HasColorPower = material != null && material.HasProperty(_colorPowerId),
                    HasMulColor0 = material != null && material.HasProperty(_mulColor0Id)
                };
                _targets.Add(target);
                _bySource.Add(flare, target);
                if (sourceTransform.parent == null) continue;
                // Native strips every literal Clone token from the parent only.
                int parentHash = FNVHash.Generate(sourceTransform.parent.name.Replace("(Clone)", string.Empty) + nameSuffixToken);
                if (!_groups.TryGetValue(parentHash, out var group))
                {
                    group = new List<Target>();
                    _groups.Add(parentHash, group);
                }
                group.Add(target);
            }
        }

        public void RemoveSources(Transform root, string nameSuffixToken)
        {
            for (int i = _targets.Count - 1; i >= 0; i--)
            {
                var target = _targets[i];
                if (target.Transform == null || !target.Transform.IsChildOf(root)) continue;
                var parent = target.Transform.parent;
                if (parent != null)
                {
                    int hash = FNVHash.Generate(parent.name.Replace("(Clone)", string.Empty) + nameSuffixToken);
                    if (_groups.TryGetValue(hash, out var group))
                    {
                        group.Remove(target);
                        if (group.Count == 0) _groups.Remove(hash);
                    }
                }
                _targets.RemoveAt(i);
                _bySource.Remove(target.Flare);
            }
        }

        internal void SetBlinkColor(CustomLensFlare flare, Color color)
        {
            if (flare == null || !_bySource.TryGetValue(flare, out var target)) return;
            target.HasBlinkColor = true;
            target.BlinkColor = color;
            target.AngleOwner.Color = color;
        }

        public void Apply(ref LensFlareUpdateInfo info)
        {
            if (_groups.TryGetValue(info.TimelineNameHash, out var group))
            {
                foreach (var target in group) Apply(target, ref info);
                return;
            }
            foreach (var target in _targets)
            {
                if (target.Flare == null || target.NameHash != info.TimelineNameHash) continue;
                Apply(target, ref info);
                return; // Native individual fallback stops at the first matching controller.
            }
        }

        // UnityLensFlareController.UpdateInfo 0x1aaaf20.
        private static void Apply(Target target, ref LensFlareUpdateInfo info)
        {
            if (target.Flare == null) return;
            if (info.enableParameter)
            {
                target.Transform.localPosition = info.IsOverridePosition
                    ? info.offset : target.InitialPosition + info.offset;
                target.Flare.FadeSpeed = info.fadeSpeed;
                target.ColorFactor = info.color;
                target.BrightnessFactor = info.brightness;
                target.ScreenFit = info.IsScreenFit;
            }
            target.AutoBrightness = info.IsAutoBrightness;
            target.AngleOwner.IsAutoBrightness = info.IsAutoBrightness;
            if (target.Flare.gameObject.activeSelf != info.enable)
                target.Flare.gameObject.SetActive(info.enable);
        }

        // Native AlterUpdate 0x1aaa410. Call after timeline/material/light updates, once per Live frame.
        public void AlterUpdate(Camera camera)
        {
#if UNITY_EDITOR
            SamplingObserver?.Invoke("flare.before");
#endif
            foreach (var target in _targets)
            {
                if (target.Flare == null || !target.Flare.gameObject.activeInHierarchy) continue;
                Color color = target.ColorFactor;
                if (target.Material != null)
                {
                    // Stage lighting uses property blocks instead of changing the material.
                    // Read the same first-slot values that the authored source mesh draws.
                    _lightProperties.Clear();
                    if (target.Renderer != null)
                    {
                        target.Renderer.GetPropertyBlock(_lightProperties, 0);
                        if (_lightProperties.isEmpty)
                            target.Renderer.GetPropertyBlock(_lightProperties);
                    }
                    // Native reads _MulColor0 only when shader-color control is
                    // enabled; otherwise it retains Color (e.g. Blink output).
                    if (target.AngleOwner.IsEnabledShaderColor && target.HasMulColor0)
                        target.AngleOwner.Color = _lightProperties.HasColor(_mulColor0Id)
                            ? _lightProperties.GetColor(_mulColor0Id) : target.Material.GetColor(_mulColor0Id);
                    Color materialColor = target.AngleOwner.Color;
                    float colorPower = target.HasColorPower
                        ? _lightProperties.HasFloat(_colorPowerId)
                            ? _lightProperties.GetFloat(_colorPowerId) : target.Material.GetFloat(_colorPowerId)
                        : 1f;
                    color *= materialColor * (colorPower * 2f);
                }
                bool visibleColor = color.r > 0.000001f || color.g > 0.000001f || color.b > 0.000001f;
                // Original disable-only branches preserve the preceding numeric
                // Brightness; disabling the component owns visibility history.
                float brightness = target.Flare.Brightness;
                bool enabled = visibleColor && target.AngleOwner.IsUseLensFlare && camera != null;
                if (enabled)
                {
#if UNITY_EDITOR
                    target.ObservedUp=target.Transform.up;
                    target.ObservedCameraForward=camera.transform.forward;
                    target.ObservedUnityFrame=Time.frameCount;
#endif
                    brightness = target.BrightnessFactor;
                    if (target.AngleOwner.IsAutoBrightness)
                    {
                        float dot = -Vector3.Dot(camera.transform.forward, target.Transform.up);
                        float anglePower = Mathf.Clamp01(dot);
                        if (_standard > brightness && _standard > _under)
                            brightness = Mathf.LerpUnclamped(0f, _standard,
                                Mathf.Clamp01((brightness - _under) / (_standard - _under)));
                        brightness *= anglePower;
                        if (target.AngleOwner != null)
                            brightness = target.AngleOwner.ApplyEnableAngle(dot, brightness);
                    }
                    else if (target.ScreenFit)
                    {
                        FitOnScreen(target.Transform, camera);
                    }
                }
                // Source positioning is on the authored host above, not on an arbitrary child offset.
                target.Flare.ApplyTimeline(enabled && brightness > 0f, false, Vector3.zero,
                    color, brightness, target.Flare.FadeSpeed);
            }
#if UNITY_EDITOR
            SamplingObserver?.Invoke("flare.after");
#endif
        }

        // FitOnScreen 0x1aaa870 uses localPosition directly against world frustum planes.
        // Retain that native coordinate convention rather than substituting viewport clamping.
        private void FitOnScreen(Transform source, Camera camera)
        {
            GeometryUtility.CalculateFrustumPlanes(camera, _frustumPlanes);
            Vector3 position = source.localPosition;
            if (_frustumPlanes[4].GetDistanceToPoint(position) < 0f) return;
            Vector3 correction = Vector3.zero;
            for (int i = 0; i < 4; i++)
            {
                float distance = _frustumPlanes[i].GetDistanceToPoint(position);
                if (distance < 0f)
                    correction -= _frustumPlanes[i].normal * (distance - 0.01f);
            }
            source.localPosition = position + correction;
        }
    }
}
