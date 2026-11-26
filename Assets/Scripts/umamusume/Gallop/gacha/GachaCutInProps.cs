using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using static Gallop.CutIn.Cutt.CutInTimelineWorkSheet;
using Object = UnityEngine.Object;

internal sealed class GachaCutInProps : IDisposable
{
    private const int DefaultTrainerGender = 1;
    public const int AttachmentIndex = 7000;
    public const int BackgroundAttachmentIndex = 3000;
    private readonly Transform root;
    private readonly Transform camera;
    private readonly Camera gradientCamera;
    private readonly Func<string, string, Transform> findBackground;
    private readonly Func<int, Transform> findActor;
    private readonly bool useNativePlacement;
    private readonly int colorCorrectionExemptLayer;
    private readonly List<Prop> props = new List<Prop>();
    private readonly Dictionary<string, Prop> locators = new Dictionary<string, Prop>(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<GameObject> Instances => props.Select(prop => prop.Instance);
    public bool HasColorCorrectionExemptProps { get; private set; }

    public Transform FindAttachment(string searchId, string path)
    {
        if (string.IsNullOrEmpty(searchId) || !locators.TryGetValue(searchId, out var prop))
            return null;
        var parent = prop.Instance.transform;
        return string.IsNullOrEmpty(path) ? parent : parent.Find(path);
    }

    public GachaCutInProps(Transform root, Transform camera, Func<string, string, Transform> findBackground,
        Func<int, Transform> findActor, IEnumerable<PropGroup> groups, Func<PropTrack, (GameObject Prefab, int RandomEntryIndex)> loadProp,
        Func<int, Transform> findActorPosition, int colorCorrectionExemptLayer)
    {
        this.root = root;
        this.camera = camera;
        gradientCamera = camera.GetComponent<Camera>();
        this.findBackground = findBackground;
        this.findActor = findActor;
        useNativePlacement = findActorPosition != null;
        this.colorCorrectionExemptLayer = colorCorrectionExemptLayer;
        foreach (var group in groups)
        {
            var (prefab, randomEntryIndex) = loadProp(group._keys);
            if (!prefab)
                continue;
            var parent = group._keys.IsUseTemporaryObjectParent
                ? null : findActorPosition?.Invoke(group._keys.AttachCharacterIndex);
            var prop = new Prop(parent ? parent : root, group._keys, prefab, randomEntryIndex, useNativePlacement);
            props.Add(prop);
            if (!string.IsNullOrEmpty(group.SearchId))
                locators[group.SearchId] = prop;
        }
    }

    public void SampleBeforeEffects(int frame, float time, Func<int, Vector3?> actorLight)
    {
        foreach (var prop in props)
            prop.Sample(frame, time, actorLight);
        foreach (var prop in props)
            ApplyPose(prop, beforeEffects: true);
    }

    public void SampleAfterEffects(int frame, float time)
    {
        HasColorCorrectionExemptProps = false;
        foreach (var prop in props)
        {
            ApplyPose(prop, beforeEffects: false);
            HasColorCorrectionExemptProps |= prop.Instance.activeInHierarchy &&
                prop.Instance.layer == colorCorrectionExemptLayer;
            prop.SampleModelGradient(frame, time * 30f, gradientCamera);
        }
    }

    private void ApplyPose(Prop prop, bool beforeEffects)
    {
        if (prop.PoseApplied || (beforeEffects && !prop.ApplyBeforeEffects))
            return;
        prop.PoseApplied = true;
        var attachment = prop.Attachment;
        var defaultParent = useNativePlacement ? prop.Instance.transform.parent : root;
        var parent = attachment != null ? ResolveAttachment(attachment, prop.Track.AttachCharacterIndex, beforeEffects) :
            useNativePlacement ? defaultParent :
            !prop.Track.IsUseTemporaryObjectParent ? findActor(prop.Track.AttachCharacterIndex) : null;
        prop.ApplyAttachment(parent ? parent : defaultParent, colorCorrectionExemptLayer,
            useNativePlacement && parent == camera);
    }

    private Transform ResolveAttachment(PropKey key, int characterIndex, bool beforeEffects)
    {
        Transform parent;
        switch (key.AttachIndex)
        {
            case -1 when useNativePlacement:
                // JP looks up a named node on the creating character. An empty or
                // missing locator leaves the prop in its original parent space;
                // using the actor root here applies its position animation twice.
                parent = findActor(characterIndex);
                return parent && !string.IsNullOrEmpty(key.AttachLocator)
                    ? FindLocator(parent, key.AttachLocator) : null;
            case 1000:
                parent = camera;
                break;
            case BackgroundAttachmentIndex:
                return findBackground(key.AttachLocatorSearchId, key.AttachLocator);
            case AttachmentIndex:
                if (!locators.TryGetValue(key.AttachLocatorSearchId ?? string.Empty, out var prop))
                    return null;
                ApplyPose(prop, beforeEffects);
                parent = prop.Instance.transform;
                break;
            default:
                parent = findActor(key.AttachIndex >= 0 ? key.AttachIndex : characterIndex);
                break;
        }
        if (!parent || string.IsNullOrEmpty(key.AttachLocator))
            return parent;
        return FindLocator(parent, key.AttachLocator) ?? parent;
    }

    private static Transform FindLocator(Transform node, string path)
    {
        if (node.name == path)
            return node;
        var found = node.Find(path);
        if (found)
            return found;
        foreach (Transform child in node)
        {
            found = FindLocator(child, path);
            if (found)
                return found;
        }
        return null;
    }

    public void Dispose()
    {
        foreach (var prop in props)
        {
            prop.Instance.SetActive(false);
            prop.ReleaseMaterials();
            Object.Destroy(prop.Instance);
        }
        props.Clear();
        locators.Clear();
    }

    private sealed class Prop
    {
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissiveId = Shader.PropertyToID("_EmissiveColor");
        private static readonly int EmissiveIntensityId = Shader.PropertyToID("_EmissiveIntensity");
        private static readonly int EmissiveRimPowerId = Shader.PropertyToID("_EmissiveRimPower");
        private static readonly int EmissiveRimIntensityId = Shader.PropertyToID("_EmissiveRimIntensity");
        private static readonly int EmissiveRimCenterId = Shader.PropertyToID("_IsEmissiveRimCenter");
        private static readonly int SilhouetteId = Shader.PropertyToID("_Silhouette");
        private static readonly int NormalizeNormalId = Shader.PropertyToID("_NormalizeNormal");
        private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
        private static readonly int CharaColorId = Shader.PropertyToID("_CharaColor");
        private static readonly int ToonStepId = Shader.PropertyToID("_ToonStep");
        private static readonly int ToonFeatherId = Shader.PropertyToID("_ToonFeather");
        private static readonly int ToonDarkColorId = Shader.PropertyToID("_ToonDarkColor");
        private static readonly int ToonBrightColorId = Shader.PropertyToID("_ToonBrightColor");
        private static readonly int UseLightDirectionId = Shader.PropertyToID("_UseOriginalDirectionalLight");
        private static readonly int LightDirectionId = Shader.PropertyToID("_OriginalDirectionalLightDir");
        private static readonly int RimColorId = Shader.PropertyToID("_RimColor");
        private static readonly int RimStepId = Shader.PropertyToID("_RimStep");
        private static readonly int RimFeatherId = Shader.PropertyToID("_RimFeather");
        private static readonly int RimSpecRateId = Shader.PropertyToID("_RimSpecRate");
        private static readonly int RimShadowId = Shader.PropertyToID("_RimShadow");
        private static readonly int StencilMaskId = Shader.PropertyToID("_StencilMask");
        private static readonly int StencilCompId = Shader.PropertyToID("_StencilComp");
        private static readonly int StencilOpId = Shader.PropertyToID("_StencilOp");
        private static readonly int GradientTopId = Shader.PropertyToID("_TopColor");
        private static readonly int GradientBottomId = Shader.PropertyToID("_BottomColor");
        private static readonly int GradientStartId = Shader.PropertyToID("_GradientStartPos");
        private static readonly int GradientEndId = Shader.PropertyToID("_GradientEndPos");
        private static readonly int GradientScaleId = Shader.PropertyToID("_GradientScale");
        private static readonly int GradientOffsetId = Shader.PropertyToID("_GradientOffset");
        private static readonly int GradientModeId = Shader.PropertyToID("_GradientBlendMode");
        private static readonly int GradientAlphaId = Shader.PropertyToID("_GradientBlendAlpha");
        private static readonly int GradientOutlineId = Shader.PropertyToID("_IsGradientOutline");
        private static readonly int GradientScreenId = Shader.PropertyToID("_IsGradientScreenMode");
        public readonly PropTrack Track;
        public readonly GameObject Instance;
        public PropKey Attachment { get; private set; }
        public bool PoseApplied;
        private readonly Vector3 initialPosition, initialScale;
        private readonly Quaternion initialRotation;
        private readonly Renderer[] renderers;
        private readonly int[] fillColorProperties;
        private readonly bool hasLightTrack;
        private readonly bool hasSilhouette;
        private readonly bool hasNormalizeNormal;
        private readonly bool hasNativeEmission;
        private readonly (int RendererIndex, int OpaqueQueue)[] alphaMeshQueues;
        private int lastSampleFrame = -1;
        private int lastEmissionFrame = -1;
        private int emissionKeyIndex = -1;
        private readonly Renderer[] gradientRenderers;
        private readonly Dictionary<string, Transform> gradientNodes = new Dictionary<string, Transform>(StringComparer.Ordinal);
        private bool gradientApplied;
        private Material[][] ownedMaterials;
        private PropKey previousStencil;
        private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        private bool world, resetDetachPose;
        private bool ignoreColorCorrection;
        public bool ApplyBeforeEffects { get; private set; }
        private Vector3 position, scale;
        private Quaternion rotation;
        private int layer = GachaCutInLoader.PreviewLayer;

        private readonly bool useNativeClock;
        private readonly int randomEntryIndex;

        public Prop(Transform parent, PropTrack track, GameObject prefab, int randomEntryIndex, bool useNativeClock)
        {
            this.useNativeClock = useNativeClock;
            this.randomEntryIndex = useNativeClock ? randomEntryIndex : -1;
            Track = track;
            Instance = Object.Instantiate(prefab, parent, false);
            Instance.SetActive(false);
            GachaCutInLoader.SetLayer(Instance);
            foreach (var animator in Instance.GetComponentsInChildren<Animator>(true))
                animator.enabled = false;
            renderers = Instance.GetComponentsInChildren<Renderer>(true);
            hasNativeEmission = useNativeClock && renderers.Length > 0 && HasNativePropType(prefab, 1, "pfb_chr_prop") &&
                track.thisList.Any(key => key.IsEmissiveIntensity || key.IsEmissiveSoftEdge);
            if (useNativeClock && track.thisList.Any(key => key.IsDepthWriteAlphaMesh?._isValid == true) &&
                HasNativePropType(prefab, 2, "pfb_toon_prop"))
            {
                var queues = new List<(int RendererIndex, int OpaqueQueue)>();
                for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                    if (Gallop.Live.Props.TryGetAlphaMeshQueue(renderers[rendererIndex].name, out int queue))
                        queues.Add((rendererIndex, queue));
                alphaMeshQueues = queues.ToArray();
            }
            hasSilhouette = useNativeClock && renderers.Length > 0 &&
                track.thisList.Any(key => key.IsSilhouette) &&
                renderers[0].sharedMaterials.Any(material => material && material.HasProperty(SilhouetteId));
            hasNormalizeNormal = useNativeClock && renderers.Length > 0 &&
                track.thisList.Any(key => key.IsNormalizeNormal) &&
                renderers[0].sharedMaterials.Any(material => material && material.HasProperty(NormalizeNormalId));
            fillColorProperties = renderers.Select(renderer =>
            {
                var materials = renderer.sharedMaterials;
                if (materials.Any(material => material && material.HasProperty(ColorId)))
                    return ColorId;
                return materials.Any(material => material && material.HasProperty(CharaColorId)) ? CharaColorId : -1;
            }).ToArray();
            hasLightTrack = track.thisList.Any(key => key.IsToonParam &&
                (key.LightDir?._isValid == true || key.IsLightDirByAttachChara));
            gradientRenderers = track.thisList.Any(key => key.IsModelGradient)
                ? renderers.Where(renderer => renderer.sharedMaterials.Any(material => material && material.HasProperty(GradientModeId))).ToArray()
                : Array.Empty<Renderer>();
            initialPosition = Instance.transform.localPosition;
            initialRotation = Instance.transform.localRotation;
            initialScale = Instance.transform.localScale;
        }

        private AnimationClip MotionClip(PropKey key)
        {
            if (randomEntryIndex >= 0 && key._useRandomAnimationClip &&
                key._randomAnimationClipArray is { } clips && randomEntryIndex < clips.Length && clips[randomEntryIndex])
                return clips[randomEntryIndex];
            return key._clip;
        }

        public void Sample(int frame, float time, Func<int, Vector3?> actorLight)
        {
            var keys = Track.thisList;
            PropKey current = null, attach = null, attachmentSpace = null, motion = null;
            PropKey move = null, turn = null, resize = null;
            PropKey playOrigin = null;
            AnimationClip motionClip = null;
            int motionStart = 0;
            float stop = float.PositiveInfinity;
            ignoreColorCorrection = false;
            int index = 0;
            while (index < keys.Length && keys[index]._frame <= frame)
            {
                current = keys[index++];
                if (current.IsAttach || current.IsDetach)
                {
                    attach = current.IsAttach ? current : null;
                    attachmentSpace = current;
                }
                if (current.IsPlay)
                    playOrigin = current;
                if (current.IsStop)
                    stop = current._frame / 30f;
                else if (MotionClip(current) is { } clip && clip)
                {
                    // The native clock uses the last explicit play key, or the current key if none exists.
                    var fallbackOrigin = !useNativeClock && motionClip == clip ? motion : current;
                    motionStart = (playOrigin ?? fallbackOrigin)._frame;
                    motion = current;
                    motionClip = clip;
                    stop = float.PositiveInfinity;
                }
                if (current.IsUsePosition) move = current;
                if (current.IsUseRotation) turn = current;
                if (current.IsUseScale) resize = current;
                if (useNativeClock && current.IsUpdatePostEffect)
                    ignoreColorCorrection = current.IsIgnoreColorCorrectionLayer;
            }
            int trainerGender = useNativeClock ? DefaultTrainerGender : 0;
            Instance.SetActive(current?.IsVisible == true &&
                (!current.IsUseTargetGender || current.TargetGender == trainerGender));
            // Hidden props can still be the parent locator of another prop.
            var transform = Instance.transform;
            ApplyBeforeEffects = !useNativeClock || current?.IsAttachPreLateUpdate == true;
            var previousPosition = transform.localPosition;
            var previousRotation = transform.localRotation;
            var previousScale = transform.localScale;
            transform.SetLocalPositionAndRotation(initialPosition, initialRotation);
            transform.localScale = initialScale;
            if (motion != null)
            {
                var clip = motionClip;
                float elapsed = Mathf.Min(time, stop) - motionStart / 30f;
                float speed = motion._playSpeed?.Or(1f) ?? 1f;
                float headTime = motion._motionHeadTime?.Or(0f) ?? 0f;
                float sample;
                if (useNativeClock)
                {
                    sample = (elapsed - headTime) * speed;
                    // The native Animator receives a normalized time based on the authored duration.
                    if (motion._playLength?._isValid == true)
                    {
                        float duration = motion._playLength._value;
                        sample = duration > 0f ? sample / duration * clip.length : 0f;
                    }
                }
                else
                {
                    sample = elapsed * speed;
                    if (motion._playLength?._isValid == true)
                        sample = Mathf.Min(sample, motion._playLength._value);
                    sample += headTime;
                }
                clip.SampleAnimation(Instance, clip.isLooping && clip.length > 0f
                    ? Mathf.Repeat(sample, clip.length) : Mathf.Clamp(sample, 0f, clip.length));
            }
            var next = index < keys.Length ? keys[index] : null;
            if (useNativeClock)
            {
                // JP UpdatePropTransform clears _isSetPos/_isSetRot every frame. Without
                // interpolation, only the current key can submit these offsets. Keeping
                // an older offset moves 101103's bowl 0.315 units away from the hand.
                move = current?.IsUsePosition == true ? current : null;
                turn = current?.IsUseRotation == true ? current : null;
                bool blendPosition = TryGetTransformInterpolation(keys, frame, 0, out var fromPosition, out var toPosition);
                bool blendRotation = TryGetTransformInterpolation(keys, frame, 1, out var fromRotation, out var toRotation);
                if (blendPosition) move = fromPosition;
                if (blendRotation) turn = fromRotation;
                position = move == null ? Vector3.zero : blendPosition
                    ? Vector3.Lerp(move._pos, toPosition._pos, Weight(move, toPosition, frame, 0, true))
                    : move._pos;
                rotation = turn == null ? Quaternion.identity : blendRotation
                    ? Quaternion.Slerp(Quaternion.Euler(turn._rot), Quaternion.Euler(toRotation._rot),
                        Weight(turn, toRotation, frame, 1, true))
                    : Quaternion.Euler(turn._rot);
            }
            else
            {
                position = move == null ? transform.localPosition : Vector3.Lerp(move._pos, next?._pos ?? move._pos,
                    Weight(move, next, frame, 0, next?.IsUsePosition == true));
                rotation = turn == null ? transform.localRotation : Quaternion.Slerp(Quaternion.Euler(turn._rot),
                    Quaternion.Euler(next?._rot ?? turn._rot), Weight(turn, next, frame, 1, next?.IsUseRotation == true));
            }
            scale = resize == null ? transform.localScale : Vector3.Lerp(resize._scale, next?._scale ?? resize._scale,
                Weight(resize, next, frame, 10, next?.IsUseScale == true));
            if (!ApplyBeforeEffects)
            {
                transform.SetLocalPositionAndRotation(previousPosition, previousRotation);
                transform.localScale = previousScale;
            }
            world = (attachmentSpace ?? move ?? turn ?? resize)?.IsUseWorldSpace == true;
            resetDetachPose = attachmentSpace is { IsDetach: true, IsDetachStayTransform: false };
            Attachment = attach;
            if (alphaMeshQueues != null && current != null && current._frame > lastSampleFrame &&
                current.IsDepthWriteAlphaMesh is { _isValid: true } depthWrite)
                ApplyDepthWriteRenderQueue(depthWrite._value);
            lastSampleFrame = frame;
            if (hasNativeEmission)
                SampleEmissiveIntensity(keys, frame);
            if (Instance.activeSelf)
                ApplyMaterial(keys, current, frame, time * 30f, actorLight);
            PoseApplied = false;
        }

        private static bool TryGetTransformInterpolation(PropKey[] keys, int frame, int channel,
            out PropKey from, out PropKey to)
        {
            from = to = null;
            foreach (var key in keys)
            {
                if (!(channel == 0 ? key.IsUsePosition : key.IsUseRotation))
                    continue;
                if (key._frame > frame)
                {
                    if (from == null || key._params?.ElementAtOrDefault(channel)?._interpolateType is not > 0)
                        return false;
                    to = key;
                    return true;
                }
                from = key;
            }
            return false;
        }

        public void ApplyAttachment(Transform parent, int colorCorrectionExemptLayer, bool cameraAttachment)
        {
            if (Attachment?.IsReferenceAttachCharaVisibleState == true && !parent.gameObject.activeInHierarchy)
                Instance.SetActive(false);
            var transform = Instance.transform;
            if (world || (useNativeClock && resetDetachPose))
                transform.SetPositionAndRotation(position, rotation);
            else
            {
                // The rendering camera has a 180-degree yaw relative to the authored
                // camera anchor, as in effect attachments. Props use that same space.
                var anchorRotation = cameraAttachment ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
                transform.SetPositionAndRotation(parent.TransformPoint(anchorRotation * position),
                    parent.rotation * anchorRotation * rotation);
            }
            transform.localScale = Attachment?.IsUseAttachScale == true ? Vector3.Scale(scale, parent.lossyScale) : scale;
            int parentLayer = parent.gameObject.layer;
            int nextLayer = ignoreColorCorrection ? colorCorrectionExemptLayer :
                (GachaMaskCamera.ActorLayers & (1 << parentLayer)) != 0 ? parentLayer : GachaCutInLoader.PreviewLayer;
            if (layer != nextLayer)
            {
                layer = nextLayer;
                GachaCutInLoader.SetLayer(Instance, layer);
            }
        }

        public void SampleModelGradient(int frame, float sampleFrame, Camera camera)
        {
            if (gradientRenderers.Length == 0 || !Instance.activeSelf)
                return;
            GachaCutInPlayer.GetKeyInterval(Track.thisList, frame, out var current, out var next);
            if (current?.IsModelGradient != true)
            {
                if (!gradientApplied)
                    return;
                foreach (var renderer in gradientRenderers)
                {
                    renderer.GetPropertyBlock(properties);
                    properties.SetFloat(GradientModeId, 0f);
                    properties.SetFloat(GradientAlphaId, 0f);
                    renderer.SetPropertyBlock(properties);
                }
                gradientApplied = false;
                return;
            }

            int inputMode = Value(current.GradientInputMode);
            bool screen = Enabled(current.IsGradientScreenMode);
            float weight = MaterialWeight(current, next, frame, sampleFrame, 14, next?.IsModelGradient == true);
            Vector4 start = Anchor(current.GradientStartNodeName, current.GradientStartNodeOffset, current.GradientStartWorldPosition);
            Vector4 end = Anchor(current.GradientEndNodeName, current.GradientEndNodeOffset, current.GradientEndWorldPosition);
            foreach (var renderer in gradientRenderers)
            {
                renderer.GetPropertyBlock(properties);
                SetColor(GradientTopId, current.GradientTopColor, next?.GradientTopColor, weight);
                SetColor(GradientBottomId, current.GradientBottomColor, next?.GradientBottomColor, weight);
                SetFloat(GradientScaleId, current.GradientScale, next?.GradientScale, weight);
                SetFloat(GradientOffsetId, current.GradientOffset, next?.GradientOffset, weight);
                SetFloat(GradientAlphaId, current.GradientBlendAlpha, next?.GradientBlendAlpha, weight);
                properties.SetVector(GradientStartId, start);
                properties.SetVector(GradientEndId, end);
                properties.SetFloat(GradientModeId, Value(current.GradientBlendMode));
                properties.SetFloat(GradientOutlineId, Enabled(current.IsGradientOutline) ? 1f : 0f);
                properties.SetFloat(GradientScreenId, screen ? 1f : 0f);
                renderer.SetPropertyBlock(properties);
            }
            gradientApplied = true;

            Vector4 Anchor(StringValue nodeName, VectorValue offset, VectorValue worldPosition)
            {
                Vector3 position;
                if (inputMode == 1)
                    position = worldPosition?.OrZero ?? Vector3.zero;
                else
                {
                    if (nodeName?._isValid != true || string.IsNullOrEmpty(nodeName._value))
                        return Vector4.zero;
                    if (!gradientNodes.TryGetValue(nodeName._value, out var node))
                    {
                        node = FindLocator(Instance.transform, nodeName._value);
                        gradientNodes[nodeName._value] = node;
                    }
                    if (!node)
                        return Vector4.zero;
                    Vector3 direction = offset?.OrZero ?? Vector3.zero;
                    position = node.position + (camera ? camera.transform.TransformDirection(direction) : direction);
                }
                if (screen && camera)
                {
                    position = camera.WorldToScreenPoint(position);
                    return new Vector4(position.x, position.y, position.z, 0f);
                }
                return new Vector4(position.x, position.y, position.z, 1f);
            }

            static int Value(EnumValue value) => value?._isValid == true ? value._intValue : 0;
            static bool Enabled(BoolValue value) => value?._isValid == true && value._value;
        }

        private void ApplyMaterial(PropKey[] keys, PropKey current, int frame, float sampleFrame, Func<int, Vector3?> actorLight)
        {
            var fill = Channel(key => key.IsUseFillColor, 3);
            var chara = Channel(key => key.IsCharaColor, 4);
            var toon = Channel(key => key.IsToonParam, 5);
            var toonColor = Channel(key => key.IsToonColor, 5);
            var rim = Channel(key => key.IsRimColor, 6);
            var emission = Channel(key => key.IsEmissiveColor, 8);
            var outline = Channel(key => key.IsUpdateOutline, 9);
            var intensity = hasNativeEmission ? default : Channel(key => key.IsEmissiveIntensity, 11);
            var stencil = Channel(key => key.IsUpdateStencil, -1).From;
            var lightDirection = hasLightTrack ? PropLightDirection(toon, actorLight) : null;
            if (stencil != null && stencil != previousStencil)
                ApplyStencil(stencil);
            if (fill.From == null && chara.From == null && toon.From == null && toonColor.From == null &&
                rim.From == null && emission.From == null && outline.From == null && !hasSilhouette && !hasNormalizeNormal)
                return;
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                var renderer = renderers[rendererIndex];
                renderer.GetPropertyBlock(properties);
                if (rendererIndex == 0 && hasSilhouette)
                    properties.SetFloat(SilhouetteId, current.IsSilhouette ? 1f : 0f);
                if (rendererIndex == 0 && hasNormalizeNormal)
                    properties.SetFloat(NormalizeNormalId, current.IsNormalizeNormal ? 1f : 0f);
                int fillProperty = fillColorProperties[rendererIndex];
                if (fill.From != null && fillProperty != -1)
                    properties.SetColor(fillProperty, Color.LerpUnclamped(fill.From.FillColor, fill.To.FillColor, fill.Weight));
                if (emission.From != null && (!hasNativeEmission || rendererIndex == 0))
                {
                    var color = Color.LerpUnclamped(emission.From.EmissiveColor, emission.To.EmissiveColor, emission.Weight);
                    float power = intensity.From == null ? 1f : Mathf.LerpUnclamped(
                        intensity.From.EmissiveIntensity, intensity.To.EmissiveIntensity, intensity.Weight);
                    properties.SetColor(EmissiveId, color * power);
                }
                if (outline.From != null)
                {
                    properties.SetColor(OutlineColorId, Color.LerpUnclamped(
                        outline.From.OutlineColor, outline.To.OutlineColor, outline.Weight));
                    properties.SetFloat(OutlineWidthId, Mathf.LerpUnclamped(
                        outline.From.OutlineWidth, outline.To.OutlineWidth, outline.Weight));
                }
                if (chara.From != null)
                    SetColor(CharaColorId, chara.From.CharaColor, chara.To.CharaColor, chara.Weight);
                if (toon.From != null)
                {
                    SetFloat(ToonStepId, toon.From.ToonStep, toon.To.ToonStep, toon.Weight);
                    SetFloat(ToonFeatherId, toon.From.ToonFeather, toon.To.ToonFeather, toon.Weight);
                    if (hasLightTrack)
                    {
                        properties.SetInt(UseLightDirectionId, lightDirection.HasValue ? 1 : 0);
                        if (lightDirection is Vector3 direction)
                            properties.SetVector(LightDirectionId, direction);
                    }
                }
                if (toonColor.From != null)
                {
                    SetColor(ToonDarkColorId, toonColor.From.ToonDarkColor, toonColor.To.ToonDarkColor, toonColor.Weight);
                    SetColor(ToonBrightColorId, toonColor.From.ToonBrightColor, toonColor.To.ToonBrightColor, toonColor.Weight);
                }
                if (rim.From != null)
                {
                    SetColor(RimColorId, rim.From.RimColor, rim.To.RimColor, rim.Weight);
                    SetFloat(RimStepId, rim.From.RimStep, rim.To.RimStep, rim.Weight);
                    SetFloat(RimFeatherId, rim.From.RimFeather, rim.To.RimFeather, rim.Weight);
                    SetFloat(RimSpecRateId, rim.From.RimSpecRate, rim.To.RimSpecRate, rim.Weight);
                    SetFloat(RimShadowId, rim.From.RimShadow, rim.To.RimShadow, rim.Weight);
                }
                renderer.SetPropertyBlock(properties);
            }

            (PropKey From, PropKey To, float Weight) Channel(Func<PropKey, bool> enabled, int channel)
            {
                PropKey from = null;
                foreach (var key in keys)
                {
                    if (!enabled(key))
                        continue;
                    if (key._frame > frame)
                        return (from, key, MaterialWeight(from, key, frame, sampleFrame,
                            channel, from != null && channel >= 0));
                    from = key;
                }
                return (from, from, 0f);
            }
        }

        private static bool HasNativePropType(GameObject prefab, int propType, string prefabPrefix)
        {
            var holder = prefab.GetComponent<Gallop.AssetHolder>();
            int type = 0;
            if (holder && holder._assetTableValue?.list != null)
                foreach (var entry in holder._assetTableValue.list)
                    if (entry?.Key == "prop_type")
                    {
                        type = (int)entry.Value;
                        break;
                    }
            return type == 0 ? prefab.name.Contains(prefabPrefix, StringComparison.Ordinal) : type == propType;
        }

        private void SampleEmissiveIntensity(PropKey[] keys, int frame)
        {
            if (frame <= lastEmissionFrame)
                return;
            var renderer = renderers[0];
            bool changed = false;
            renderer.GetPropertyBlock(properties);
            for (int sourceFrame = lastEmissionFrame + 1; sourceFrame <= frame; sourceFrame++)
            {
                while (emissionKeyIndex + 1 < keys.Length && keys[emissionKeyIndex + 1]._frame <= sourceFrame)
                    emissionKeyIndex++;
                if (emissionKeyIndex < 0)
                    continue;
                var current = keys[emissionKeyIndex];
                if (FindEmissiveInterpolation(keys, emissionKeyIndex) is { } interval)
                {
                    var (from, to) = interval;
                    float weight = Interpolation.NativeWeightBetween(from, to, to._params, 11, sourceFrame);
                    properties.SetFloat(EmissiveIntensityId,
                        Mathf.LerpUnclamped(from.EmissiveIntensity, to.EmissiveIntensity, weight));
                }
                else if (current.IsEmissiveIntensity)
                {
                    properties.SetFloat(EmissiveIntensityId, current.EmissiveIntensity);
                    if (current.IsEmissiveSoftEdge)
                    {
                        properties.SetFloat(EmissiveRimPowerId, current.EmissiveRimPower);
                        properties.SetFloat(EmissiveRimIntensityId, current.EmissiveRimIntensity);
                        properties.SetFloat(EmissiveRimCenterId, current.IsEmissiveCenter ? 1f : 0f);
                    }
                }
                else
                    continue;
                changed = true;
            }
            if (changed)
                renderer.SetPropertyBlock(properties);
            lastEmissionFrame = frame;
        }

        private static (PropKey From, PropKey To)? FindEmissiveInterpolation(PropKey[] keys, int keyIndex)
        {
            const int intensityChannel = 11;
            int endIndex = keyIndex + 1;
            while (endIndex < keys.Length && !keys[endIndex].IsEmissiveIntensity)
                endIndex++;
            if (endIndex == keys.Length)
                return null;
            var to = keys[endIndex];
            if (to._params == null || to._params.Length <= intensityChannel ||
                to._params[intensityChannel]?._interpolateType is not > 0)
                return null;
            for (int index = keyIndex; index >= 0; index--)
                if (keys[index].IsEmissiveIntensity)
                    return (keys[index], to);
            return null;
        }

        private Vector3? PropLightDirection((PropKey From, PropKey To, float Weight) toon, Func<int, Vector3?> actorLight)
        {
            if (toon.From == null)
                return null;
            if (toon.From.IsLightDirByAttachChara)
            {
                int slot = Attachment is { AttachIndex: >= 0 and < 1000 } key
                    ? key.AttachIndex : Track.AttachCharacterIndex;
                return actorLight(slot);
            }
            if (toon.From.LightDir?._isValid != true)
                return null;
            var from = toon.From.LightDir._value;
            var to = toon.To.LightDir?._isValid == true ? toon.To.LightDir._value : from;
            return Quaternion.Euler(Vector3.LerpUnclamped(from, to, toon.Weight)) * Vector3.back;
        }

        private void ApplyStencil(PropKey key)
        {
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                foreach (var material in GetOwnedMaterials(rendererIndex))
                {
                    if (!material || !material.HasProperty(StencilCompId))
                        continue;
                    material.SetInt(StencilMaskId, key.StencilValue);
                    material.SetInt(StencilCompId, (int)CompareFunction.Always);
                    material.SetInt(StencilOpId, (int)StencilOp.Replace);
                }
            previousStencil = key;
        }

        private void ApplyDepthWriteRenderQueue(bool depthWrite)
        {
            foreach (var (rendererIndex, opaqueQueue) in alphaMeshQueues)
            {
                var materials = GetOwnedMaterials(rendererIndex);
                if (materials.Length > 0 && materials[0])
                    materials[0].renderQueue = depthWrite ? opaqueQueue : opaqueQueue + 1000;
            }
        }

        private Material[] GetOwnedMaterials(int rendererIndex)
        {
            ownedMaterials ??= new Material[renderers.Length][];
            return ownedMaterials[rendererIndex] ??= renderers[rendererIndex].materials;
        }

        public void ReleaseMaterials()
        {
            if (ownedMaterials == null)
                return;
            foreach (var materials in ownedMaterials)
                if (materials != null)
                    foreach (var material in materials)
                        if (material)
                            Object.Destroy(material);
        }

        private void SetColor(int property, ColorValue from, ColorValue to, float weight)
        {
            if (from?._isValid == true)
                properties.SetColor(property, Color.LerpUnclamped(from._value,
                    to?._isValid == true ? to._value : from._value, weight));
        }

        private void SetFloat(int property, FloatValue from, FloatValue to, float weight)
        {
            if (from?._isValid == true)
                properties.SetFloat(property, Mathf.LerpUnclamped(from._value, to?.Or(from._value) ?? from._value, weight));
        }

        private float MaterialWeight(PropKey key, PropKey next, int frame, float sampleFrame, int channel, bool canBlend)
        {
            if (!canBlend)
                return 0f;
            return useNativeClock
                ? Interpolation.NativeWeightBetween(key, next, next._params, channel, frame)
                : Weight(key, next, sampleFrame, channel, true);
        }

        private static float Weight(PropKey key, PropKey next, float frame, int index, bool canBlend)
        {
            var interpolation = next?._params;
            return canBlend && interpolation != null && index < interpolation.Length &&
                interpolation[index]?._interpolateType > 0
                ? interpolation[index].WeightBetween(key, next, frame) : 0f;
        }
    }
}
