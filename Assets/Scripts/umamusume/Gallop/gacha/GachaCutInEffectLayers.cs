using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using static Gallop.CutIn.Cutt.CutInTimelineWorkSheet;
using static GachaCutInLoader;

internal sealed partial class GachaCutInPlayer
{
    private const int EffectOverlayLayer = 17;
    private const int EffectUiLayer = 18;
    private const int ColorCorrectionExemptLayer = 19;
    private const int EffectScreenLayer = 20;
    private const float ScreenEffectOrthoSize = 5f;
    private Camera effectOverlayCamera;
    private Camera effectUiCamera;
    private Camera effectScreenCamera;
    private Camera colorCorrectionExemptCamera;
    private bool splitColorCorrection;
    private uint colorCorrectionExemptActors;
    private void SampleActorColorLayers()
    {
        if (supportOptions != null && maskCamera == null)
            return;
        foreach (var (slot, binding) in cast)
        {
            if (!binding.Actor.gameObject.activeSelf)
                continue;
            var key = binding.PreviousCharacter;
            bool maskActor = maskCamera != null && key.IsChangeLayer && (key.Layer == 15 || key.Layer == 16);
            var layer = maskActor ? GachaMaskCamera.ActorLayer + key.Layer - 15 :
                slot >= 0 && slot < 32 && (colorCorrectionExemptActors & (1u << slot)) != 0 ? ColorCorrectionExemptLayer :
                PreviewLayer;
            binding.SetLayer(layer);
        }
    }

    private void SampleEffectCameras(bool hasWorldOverlay, bool hasUiOverlay, bool hasScreenEffect, bool hasColorExempt)
    {
        SampleEffectCamera(ref colorCorrectionExemptCamera, ColorCorrectionExemptLayer, splitColorCorrection && hasColorExempt,
            false, "Gacha color correction exclusions", 0);
        SampleEffectCamera(ref effectOverlayCamera, EffectOverlayLayer, hasWorldOverlay,
            false, "Gacha unprocessed effects", colorCorrectionExemptCamera ? 1 : 0);
        int uiIndex = (colorCorrectionExemptCamera ? 1 : 0) + (effectOverlayCamera ? 1 : 0);
        SampleEffectCamera(ref effectUiCamera, EffectUiLayer, hasUiOverlay, true, "Gacha 3D UI effects", uiIndex);
        SampleEffectCamera(ref effectScreenCamera, EffectScreenLayer, hasScreenEffect, true,
            "Gacha screen effects", uiIndex + (effectUiCamera ? 1 : 0));
    }

    private void SampleEffectCamera(ref Camera overlay, int layer, bool visible, bool clearDepth, string name, int stackIndex)
    {
        if (!overlay && visible)
        {
            overlay = new GameObject(name).AddComponent<Camera>();
            bool screen = layer == EffectScreenLayer;
            overlay.transform.SetParent(screen ? root : camera.transform, false);
            if (screen)
            {
                overlay.transform.localPosition = new Vector3(0f, 0f, -50f);
                overlay.orthographic = true;
                overlay.orthographicSize = ScreenEffectOrthoSize;
                overlay.nearClipPlane = -20f;
                overlay.farClipPlane = 70f;
            }
            overlay.clearFlags = CameraClearFlags.Depth;
            overlay.cullingMask = 1 << layer;
            overlay.allowHDR = false;
            overlay.allowMSAA = false;
            overlay.useOcclusionCulling = false;
            var data = overlay.GetUniversalAdditionalCameraData();
            data.renderType = CameraRenderType.Overlay;
            data.renderPostProcessing = false;
            data.renderShadows = false;
            var renderData = overlay.gameObject.AddComponent<Gallop.RenderPipeline.CameraData>();
            renderData.IsClearDepth = clearDepth;
            var stack = camera.GetUniversalAdditionalCameraData().cameraStack;
            stack.Insert(stackIndex, overlay);
        }
        if (!overlay)
            return;
        overlay.enabled = visible;
        if (!visible)
            return;
        overlay.aspect = camera.aspect;
        if (layer == EffectScreenLayer)
            return;
        overlay.orthographic = camera.orthographic;
        overlay.orthographicSize = camera.orthographicSize;
        overlay.fieldOfView = camera.fieldOfView;
        overlay.nearClipPlane = camera.nearClipPlane;
        overlay.farClipPlane = camera.farClipPlane;
        overlay.lensShift = camera.lensShift;
    }

    private int EffectRenderLayer(EffectKey key)
    {
        return key._useNonPostprocessLayer ? EffectOverlayLayer :
            key._useUiLayer ? (supportOptions == null ? EffectScreenLayer : EffectUiLayer) :
            supportOptions == null && key._isOrtho ? OrthoBackgroundLayer :
            key._useUi3DLayer ? EffectUiLayer :
            supportOptions == null && key._useColorCorrectionLayer ? ColorCorrectionExemptLayer :
            supportOptions == null && key.IsUseMaskLayer && maskCamera != null ? GachaMaskCamera.EffectLayer : PreviewLayer;
    }

    private void ApplyEffectRenderRouting(VisualTrack<EffectKey> track, EffectKey key)
    {
        int layer = EffectRenderLayer(key);
        if (track.Instance.layer != layer)
            SetLayer(track.Instance, layer);

        bool useNativeSorting = supportOptions == null && Current.Unique;
        if (useNativeSorting && !key.IsApplyGameObjectLayerToEffect && key.IsOrderInLayerOverride)
            return;

        bool applyOrderOffset = useNativeSorting
            ? key.IsApplyGameObjectLayerToEffect && key.EffectRendererOrderInLayerOffset != 0
            : key.IsOrderInLayerOverride;
        if (applyOrderOffset)
            track.OriginalSortingOrders ??= track.Renderers.Select(renderer => renderer.sortingOrder).ToArray();
        if (supportOptions == null && key.IsApplyGameObjectLayerToEffect)
            track.OriginalSortingLayers ??= track.Renderers.Select(renderer => renderer.sortingLayerID).ToArray();
        if (track.OriginalSortingOrders != null && (!useNativeSorting || !key.IsApplyGameObjectLayerToEffect || applyOrderOffset))
            for (int i = 0; i < track.Renderers.Length; i++)
                track.Renderers[i].sortingOrder = track.OriginalSortingOrders[i] +
                    (applyOrderOffset ? key.EffectRendererOrderInLayerOffset : 0);
        if (track.OriginalSortingLayers != null)
        {
            string sortingLayer = key.IsApplyGameObjectLayerToEffect
                ? key._useUiLayer && (layer == EffectScreenLayer || layer == EffectUiLayer) ? "UI" : LayerMask.LayerToName(layer)
                : null;
            int? matchingLayer = string.IsNullOrEmpty(sortingLayer) ? null :
                SortingLayer.layers.Where(candidate => candidate.name == sortingLayer)
                    .Select(candidate => (int?)candidate.id).FirstOrDefault();
            if (!useNativeSorting || !key.IsApplyGameObjectLayerToEffect || matchingLayer.HasValue)
                for (int i = 0; i < track.Renderers.Length; i++)
                    track.Renderers[i].sortingLayerID = matchingLayer ?? track.OriginalSortingLayers[i];
        }
    }

    private void ApplyEffectMaterialState(VisualTrack<EffectKey> track, EffectKey key, bool useNativeProperties)
    {
        bool depth = supportOptions == null && key.IsUpdateZIgnore;
        bool cull = supportOptions == null && key.IsUpdateCulling;
        if (!key.IsUpdateStencil && !depth && !cull)
            return;
        bool keepConfettiStencil = useNativeProperties && key.IsUpdateStencil &&
            Current.Name == "crd104603_001" && track.Instance.name == "pfb_eff_chr1046_03_13(Clone)";
        for (int index = 0; index < track.Renderers.Length; index++)
            foreach (var material in GetEffectMaterials(track, index))
            {
                if (!material)
                    continue;
                if (cull && material.HasProperty("_Cull"))
                {
                    material.SetFloat("_Cull", key.CullingMode);
                    if (!useNativeProperties && material.HasProperty("_CullMode"))
                        material.SetFloat("_CullMode", key.CullingMode);
                }
                else if (cull && material.HasProperty("_CullMode"))
                    material.SetFloat("_CullMode", key.CullingMode);
                if (depth && material.HasProperty("_ZTestMode"))
                    material.SetFloat("_ZTestMode", key.ZIgnoreParam);
                if (!key.IsUpdateStencil)
                    continue;
                if (!useNativeProperties && !material.HasProperty("_StencilComp"))
                    continue;
                if (material.HasProperty("_StencilComp"))
                    material.SetInt("_StencilComp", key.StencilCompare);
                if (!useNativeProperties || material.HasProperty("_StencilOp"))
                    material.SetInt("_StencilOp", key.StencilOperation);
                if (!useNativeProperties || material.HasProperty("_StencilMask"))
                    material.SetInt("_StencilMask", key.StencilValue);

                // The 104603 timeline writes Replace on the confetti from frame 127.
                if (keepConfettiStencil)
                    PreserveEffectStencil(material, "mtl_eff_chr1046_03_03_01");
            }
    }

    // Visually supported change: Keep restores the gold shaft in 100501 and has
    // little visible impact in the 104603 confetti comparison. There is no direct
    // segment-level proof that the JP client uses Keep for either effect.
    private static void PreserveEffectStencil(Material material, string name)
    {
        if (material && (material.name == name || material.name == name + " (Instance)") &&
            material.HasProperty("_StencilOp") && material.GetInt("_StencilOp") == (int)StencilOp.Replace)
            material.SetInt("_StencilOp", (int)StencilOp.Keep);
    }

    private Vector3 EffectScale(EffectKey key, Vector3 scale)
    {
        if (supportOptions != null || !key._useUiLayer || !key.UIIsScaleByAspectRatio)
            return scale;
        float widening = camera.aspect <= 1f ? (9f / 16f) / camera.aspect : camera.aspect / (16f / 9f);
        return scale / Mathf.Max(1f, widening);
    }

    private Vector3 AlignScreenEffect(Transform parent, Vector3 position, int alignment)
    {
        if (alignment < 1 || alignment > 9 || supportOptions != null)
            return position;
        var offset = new Vector3(((alignment - 1) % 3 - 1) * ScreenEffectOrthoSize * camera.aspect,
            (1 - (alignment - 1) / 3) * ScreenEffectOrthoSize, 0f);
        return position + parent.InverseTransformVector(root.TransformVector(offset));
    }

    private void SampleEffectOutline(VisualTrack<EffectKey> track, EffectKey current)
    {
        if (supportOptions != null)
            return;
        EffectKey from, to;
        float weight = 0f;
        if (Current.Unique)
        {
            if (TryGetEffectInterpolation(track.Keys, key => key.IsUpdateOutline, 9, out from, out to))
                weight = to._params[9].WeightBetween(from, to, lastFrame);
            else
            {
                if (!current.IsUpdateOutline)
                    return;
                from = current;
            }
        }
        else
        {
            from = to = null;
            foreach (var key in track.Keys)
            {
                if (!key.IsUpdateOutline)
                    continue;
                if (key._frame > lastFrame)
                {
                    to = key;
                    break;
                }
                from = key;
            }
            if (from == null)
                return;
            var interpolation = to?._params?.ElementAtOrDefault(9);
            if (interpolation?._interpolateType > 0)
                weight = interpolation.WeightBetween(from, to, time * FramesPerSecond);
        }
        float width = Mathf.LerpUnclamped(from.OutlineWidth, to?.OutlineWidth ?? from.OutlineWidth, weight);
        var color = Color.LerpUnclamped(from.OutlineColor, to?.OutlineColor ?? from.OutlineColor, weight);
        for (int index = 0; index < track.Renderers.Length; index++)
            foreach (var material in GetEffectMaterials(track, index))
            {
                if (!material)
                    continue;
                if (material.HasProperty("_OutlineWidth"))
                    material.SetFloat("_OutlineWidth", width);
                if (material.HasProperty("_OutlineColor"))
                    material.SetColor("_OutlineColor", color);
            }
    }

    private static void ApplyBackdropDepth(VisualTrack<EffectKey> track, string prefabName)
    {
        // These two authored backdrop planes have depth testing disabled in the prefab.
        // In the viewer's shared camera they must be occluded by the opaque character.
        string backdrop = prefabName switch
        {
            "pfb_eff_chr1006_03_18" => "bg",
            "pfb_eff_chr1006_03_22" => "yellowplane",
            _ => null
        };
        if (backdrop == null)
            return;
        for (int index = 0; index < track.Renderers.Length; index++)
            if (track.Renderers[index].name == backdrop)
                foreach (var material in GetEffectMaterials(track, index))
                    if (material && material.HasProperty("_ZTestMode"))
                        material.SetInt("_ZTestMode", (int)CompareFunction.LessEqual);
    }

    private static Material[] GetEffectMaterials(VisualTrack<EffectKey> track, int index)
    {
        track.EffectMaterials ??= new Material[track.Renderers.Length][];
        return track.EffectMaterials[index] ??= track.Renderers[index].materials;
    }

    private void ClearEffectMaterials()
    {
        foreach (var track in effects)
            if (track.EffectMaterials != null)
                foreach (var materials in track.EffectMaterials)
                    if (materials != null)
                        foreach (var material in materials)
                            if (material)
                                Object.Destroy(material);
    }
}
