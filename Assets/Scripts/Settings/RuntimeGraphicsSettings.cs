using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;
using NativeCameraData = Gallop.RenderPipeline.CameraData;

// Quality changes affect an in-memory copy, never the authored pipeline asset.
public static class RuntimeGraphicsSettings
{
    private static UniversalRenderPipelineAsset source;
    private static UniversalRenderPipelineAsset runtime;
    private static readonly int[] Samples = { 1, 2, 4, 8 };
    private struct ShadowScope { public Camera Camera; public UniversalRenderPipelineAsset Asset; public float Previous; }
    private static readonly List<ShadowScope> shadowScopes = new List<ShadowScope>();
    private static readonly Dictionary<Camera, float> lastNativeShadowDistances = new Dictionary<Camera, float>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void InstallNativeShadowScope()
    {
        shadowScopes.Clear(); lastNativeShadowDistances.Clear();
        RenderPipelineManager.beginCameraRendering -= BeginNativeShadow;
        RenderPipelineManager.endCameraRendering -= EndNativeShadow;
        RenderPipelineManager.beginCameraRendering += BeginNativeShadow;
        RenderPipelineManager.endCameraRendering += EndNativeShadow;
    }

    public static bool TryGetNativeShadowDistance(Camera camera, out float distance)
        => lastNativeShadowDistances.TryGetValue(camera, out distance);

    private static void BeginNativeShadow(ScriptableRenderContext context, Camera camera)
    {
        var data = camera.GetComponent<NativeCameraData>();
        if (data == null && Gallop.Live.Director.instance == null) return;
        var active = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        // RuntimeGraphicsSettings.Apply and Editor diagnostics supply in-memory
        // copies. Never mutate an authored pipeline asset from a render callback.
        if (active == null || (active.hideFlags & HideFlags.DontSave) == 0) return;
        var additional = camera.GetComponent<UniversalAdditionalCameraData>();
        bool renderShadows = additional == null || additional.renderShadows;
        float distance = NativeCameraData.ResolveShadowDistance(NativeCameraData.GlobalMaxShadowDistance,
            data != null ? data.MaxShadowDistance : 0f, data != null, renderShadows);

        shadowScopes.Add(new ShadowScope { Camera = camera, Asset = active, Previous = active.shadowDistance });
        lastNativeShadowDistances[camera] = distance;
        // Stock URP raises beginCameraRendering BEFORE InitializeCameraData and
        // shadow culling. Supply the recovered distance at this host boundary.
        active.shadowDistance = distance;
    }

    private static void EndNativeShadow(ScriptableRenderContext context, Camera camera)
    {
        for (int i = shadowScopes.Count - 1; i >= 0; --i)
        {
            var scope = shadowScopes[i];
            if (scope.Camera != camera) continue;
            if (scope.Asset != null) scope.Asset.shadowDistance = scope.Previous;
            shadowScopes.RemoveAt(i);
            break;
        }
    }

    public static void Apply()
    {
        var config = Config.Instance;
        if (config == null) return;
        int samples = Samples[Mathf.Clamp(config.AntiAliasing, 0, 3)];
        QualitySettings.antiAliasing = samples == 1 ? 0 : samples;
        QualitySettings.anisotropicFiltering = (AnisotropicFiltering)Mathf.Clamp(config.AnisotropicFiltering, 0, 2);
        QualitySettings.globalTextureMipmapLimit = Mathf.Clamp(config.TextureMipmapLimit, 0, 3);
        QualitySettings.lodBias = Mathf.Clamp(config.LodBias, 0.3f, 2f);

        var active = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (active == null) return;
        if (active != runtime)
        {
            if (active != source || runtime == null)
            {
                if (runtime != null) Object.Destroy(runtime);
                source = active;
                runtime = Object.Instantiate(active);
                runtime.name = active.name + " (Runtime Graphics)";
                runtime.hideFlags = HideFlags.DontSave;
            }
            // A quality-level override takes precedence over the default pipeline.
            if (QualitySettings.renderPipeline != null) QualitySettings.renderPipeline = runtime;
            else GraphicsSettings.renderPipelineAsset = runtime;
        }
        runtime.msaaSampleCount = samples;
        runtime.renderScale = Mathf.Clamp(config.RenderScalePercent / 100f, 0.5f, 2f);
        runtime.shadowDistance = Mathf.Clamp(config.ShadowDistance, 0f, 150f);
    }
}
