using System;
using System.Collections.Generic;
using Gallop.Live.Cutt;
using Gallop.RenderPipeline;
using UnityEngine;

namespace Gallop.Live
{
    // StageController.SetupLightProjection/UpdateLightProjection/CopyLightProjectionControllerParam
    // 0x1a8c480 / 0x1a91990 / 0x1a82aa0; LightProjectionController.Update 0x19ae8f0.
    public sealed class LightProjectionRuntimeController : IDisposable
    {
        public struct MovieFrame
        {
            public Texture texture;
            public Vector2 scale, offset;
        }
        // Resolve the currently evaluated Monitor frame (native TextureId is a one-based slot).
        public delegate bool MovieFrameResolver(int slotId, out MovieFrame frame);

        private sealed class Binding
        {
            public LightProjection projection;
            public CustomProjector projector;
            public Vector3 position, scale;
            public Quaternion rotation;
            public bool enabled, orthographic, overrideIgnoreLayer;
            public LayerMask overrideLayerMask;
            public float size, fieldOfView, nearClipPlane, farClipPlane;
            public readonly HashSet<string> errors = new HashSet<string>();
        }

        private readonly Dictionary<int, Binding> _bindings = new Dictionary<int, Binding>();
        private readonly List<CustomProjector> _projectors = new List<CustomProjector>();
        private readonly MirrorBallProjectionRuntime _mirrorRuntime = new MirrorBallProjectionRuntime();
        private Func<int, Texture> _mirrorTextureResolver;
        private Func<int, Texture> _textureResolver;
        private Func<int, Transform> _characterResolver;
        private Func<int, Vector3> _characterPositionResolver;
        private MovieFrameResolver _movieResolver;
        public IReadOnlyList<CustomProjector> Projectors => _projectors;
        public IReadOnlyList<MirrorBallProjector> MirrorBallProjectors => _mirrorRuntime.Projectors;

        public void Initialize(Transform stageRoot, Func<int, Texture> textureResolver,
            Func<int, Transform> characterResolver)
        {
            Dispose();
            _textureResolver = textureResolver;
            _characterResolver = characterResolver;
            if (stageRoot == null) throw new ArgumentNullException(nameof(stageRoot));
            _mirrorRuntime.Initialize(stageRoot, characterResolver);
            foreach (LightProjection projection in stageRoot.GetComponentsInChildren<LightProjection>(true))
            {
                int hash = FNVHash.Generate(projection.name);
                if (_bindings.ContainsKey(hash))
                    throw new InvalidOperationException("Duplicate authored LightProjection name: " + projection.name);
                if (!projection.Initialize())
                {
                    Debug.LogError("LightProjection '" + projection.name + "' has no authored CustomProjector/material/shader.", projection);
                    continue;
                }
                CustomProjector projector = projection.Projector;
                Transform transform = projector.transform;
                var binding = new Binding
                {
                    projection = projection, projector = projector,
                    position = transform.localPosition, rotation = transform.localRotation, scale = transform.localScale,
                    enabled = projector.enabled, orthographic = projector.OrthoGraphic,
                    size = projector.OrthoGraphicSize, fieldOfView = projector.FieldOfView,
                    nearClipPlane = projector.NearClipPlane, farClipPlane = projector.FarClipPlane,
                    overrideIgnoreLayer = projector.OverrideIgnoreLayer, overrideLayerMask = projector.OverrideLayerMask
                };
                _bindings.Add(hash, binding);
                _projectors.Add(projector);
                projector.enabled = false;
            }
        }

        public void ConfigureMovieFrames(MovieFrameResolver resolver) { _movieResolver = resolver; }
        public void ConfigureCharacterPosition(Func<int, Vector3> resolver) { _characterPositionResolver = resolver; }
        public void ConfigureMirrorBallTextures(Func<int, Texture> resolver) { _mirrorTextureResolver = resolver; }

        public void Apply(ref LiveLightProjectionTimeline.UpdateInfo info)
        {
            if (info.group == null) return;
            if (_mirrorRuntime.Apply(ref info, _textureResolver, _mirrorTextureResolver)) return;
            int hash = info.group.nameHash;
            if (!_bindings.TryGetValue(hash, out Binding binding))
            {
                // Original4eb99f0 returns on a missing authored controller.
                return;
            }
            CustomProjector projector = binding.projector;
            var a = info.current;
            if (a == null) { projector.enabled = false; return; }
            var b = info.next ?? a;
            float t = info.progress;
            Transform transform = projector.transform;
            Vector3 position = Vector3.LerpUnclamped(a.Position, b.Position, t);
            if (a.CharacterAttach)
            {
                if (_characterPositionResolver != null)
                    position += _characterPositionResolver(a.CharacterAttachPosition);
                else
                {
                    Transform character = _characterResolver?.Invoke(a.CharacterAttachPosition);
                    if (character != null) position += character.position;
                    else
                    {
                        projector.enabled = false;
                        Report(binding, "attachment", "Attached character position is unavailable.");
                        return;
                    }
                }
            }
            transform.localPosition = position;
            transform.localRotation = Quaternion.Lerp(a.Rotation, b.Rotation, t);
            transform.localScale = Vector3.LerpUnclamped(a.Scale, b.Scale, t);
            projector.OrthoGraphic = a.Orthographic;
            projector.OrthoGraphicSize = Mathf.LerpUnclamped(a.OrthographicSize, b.OrthographicSize, t);
            projector.FieldOfView = Mathf.LerpUnclamped(a.FieldOfView, b.FieldOfView, t);
            projector.NearClipPlane = Mathf.LerpUnclamped(a.NearClipPlane, b.NearClipPlane, t);
            projector.FarClipPlane = Mathf.LerpUnclamped(a.FarClipPlane, b.FarClipPlane, t);
            // Native retains authored AspectRatio, but evaluates clipping from the timeline.
            projector.OverrideIgnoreLayer = a.OverrideIgnoreLayer;
            projector.OverrideLayerMask = a.OverrideLayerMask;
            Material material = projector.Material;
            material.SetColor(ShaderManager.GetPropertyId(ShaderManager.PropertyId._ProjectorMulColor0), info.color);
            material.SetFloat(ShaderManager.GetPropertyId(ShaderManager.PropertyId._ProjectorColorPower),
                Mathf.LerpUnclamped(a.ColorPower, b.ColorPower, t));
            LiveDefine.TrySetLightBlendModeMaterialProperty((LiveDefine.LightBlendMode)a.LightBlendMode, material);
            // Native backside/character-only/radius/falloff fields are consumed only by MirrorBallProjector,
            // not by this planar CustomProjector. Do not invent stage shader properties for them.
            if (info.group.ContentType != 0 && info.group.ContentType != 1)
            {
                projector.enabled = false;
                Report(binding, "content:" + info.group.ContentType, "Unknown authored ContentType " + info.group.ContentType + ".");
                return;
            }
            if (a.IsEnable)
            {
                Texture texture = (info.group.ContentType == 0 ? _textureResolver : _mirrorTextureResolver)?.Invoke(a.TextureId);
                // Original StageController.UpdateLightProjection 0x4eb99f0
                // only writes a texture when TryGetValue succeeds. An absent
                // ID (e.g. SHAΚEROCK projector_02/id0) retains the prefab's
                // texture; it does not disable an otherwise enabled projector.
                if (texture != null)
                    material.SetTexture(ShaderManager.GetPropertyId(ShaderManager.PropertyId._LightTex), texture);
            }
            if (binding.projection.IsAnimationProjection && !ApplyAnimation(binding, a, b, t, info.time))
            {
                projector.enabled = false;
                return;
            }
            projector.enabled = a.IsEnable;
        }

        private bool ApplyAnimation(Binding binding, LiveTimelineKeyLightProjectionData a,
            LiveTimelineKeyLightProjectionData b, float progress, float elapsed)
        {
            var param = a.AnimationParam;
            if (param == null) return true;
            if (param.TextureId == -1)
            {
                binding.projector.Material.SetTexture(ShaderManager.GetPropertyId(ShaderManager.PropertyId._AnimationTex),null);
                return true;
            }
            Texture texture;
            Vector2 scale, offset;
            if (a.UseMonitorMovie())
            {
                if (_movieResolver == null || !_movieResolver(param.TextureId, out MovieFrame frame) || frame.texture == null)
                {
                    // Original4eb9560 keeps the material and enabled state for
                    // an absent/inactive one-based UV movie slot (including0).
                    return true;
                }
                texture = frame.texture;
                scale = frame.scale;
                offset = frame.offset;
            }
            else
            {
                texture = _textureResolver?.Invoke(param.TextureId);
                if (param.MaxCut >= 1)
                {
                    scale = Vector2.one;
                    offset = Vector2.zero;
                    if (param.DivisionNumberX >= 1 && param.DivisionNumberY >= 1)
                    {
                        float time = elapsed;
                        if (param.AnimationTime > 0f && time > param.AnimationTime) time %= param.AnimationTime;
                        int cut = param.AnimationTime > 0f ? Mathf.FloorToInt(time / param.AnimationTime * param.MaxCut) : 0;
                        scale = new Vector2(1f / param.DivisionNumberX, 1f / param.DivisionNumberY);
                        // Preserve the original row calculation (division by Y, not a conventional X atlas stride).
                        offset = new Vector2((float)(cut % param.DivisionNumberX) / param.DivisionNumberX,
                            1f - (float)(cut / param.DivisionNumberY) / param.DivisionNumberY);
                    }
                }
                else
                {
                    var next = b.AnimationParam ?? param;
                    scale = Vector2.LerpUnclamped(param.ScaleUV, next.ScaleUV, progress);
                    offset = Vector2.LerpUnclamped(param.OffsetUV, next.OffsetUV, progress);
                }
            }
            Material material = binding.projector.Material;
            int animationTex = ShaderManager.GetPropertyId(ShaderManager.PropertyId._AnimationTex);
            if(texture!=null)material.SetTexture(animationTex, texture);
            material.SetVector(ShaderManager.GetPropertyId(ShaderManager.PropertyId._UVAdjust),
                new Vector4(scale.x,scale.y,offset.x,offset.y));
            return true;
        }

        private static void Report(Binding binding, string key, string message)
        {
            if (binding.errors.Add(key))
                Debug.LogError("LightProjection '" + binding.projection.name + "': " + message, binding.projection);
        }

        public void Dispose()
        {
            foreach (Binding binding in _bindings.Values)
            {
                if (binding.projector == null) continue;
                CustomProjector projector = binding.projector;
                Transform transform = projector.transform;
                transform.localPosition = binding.position;
                transform.localRotation = binding.rotation;
                transform.localScale = binding.scale;
                projector.enabled = binding.enabled;
                projector.OrthoGraphic = binding.orthographic;
                projector.OrthoGraphicSize = binding.size;
                projector.FieldOfView = binding.fieldOfView;
                projector.NearClipPlane = binding.nearClipPlane;
                projector.FarClipPlane = binding.farClipPlane;
                projector.OverrideIgnoreLayer = binding.overrideIgnoreLayer;
                projector.OverrideLayerMask = binding.overrideLayerMask;
                if (binding.projection != null) binding.projection.Release();
            }
            _bindings.Clear();
            _projectors.Clear();
            _mirrorRuntime.Dispose();
            _mirrorTextureResolver = null;
            _textureResolver = null;
            _characterResolver = null;
            _characterPositionResolver = null;
            _movieResolver = null;
        }
    }
}
