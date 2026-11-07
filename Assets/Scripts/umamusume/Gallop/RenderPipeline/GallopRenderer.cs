using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.Universal.Internal;
using UrpCameraData = UnityEngine.Rendering.Universal.CameraData;

namespace Gallop.RenderPipeline
{
    // Parent control flow: fixed GameAssembly 0x5231130, 0x5230db0,
    // 0x52323e0, 0x5231100, 0x5230900. URP14 still owns Execute, stable
    // event sorting, Cull and DrawShadows. No nested UniversalRenderer executes.
    public sealed class GallopRenderer : ScriptableRenderer
    {
        readonly ForwardLights lights = new ForwardLights();
        readonly GallopMainLightShadowPass mainShadow = new GallopMainLightShadowPass();
        readonly GallopAdditionalLightShadowPass additionalShadow = new GallopAdditionalLightShadowPass();
        readonly GallopDrawPass opaque = new GallopDrawPass(true, 250);
        readonly GallopDrawPass transparent = new GallopDrawPass(false, 450);
        readonly DrawSkyboxPass previewSkybox = new DrawSkyboxPass(RenderPassEvent.BeforeRenderingSkybox);
        readonly GallopDrawPass mirrorOpaque = new GallopDrawPass(true, 250, "SimpleMirrorCaster");
        readonly GallopDrawPass mirrorTransparent = new GallopDrawPass(false, 450, "SimpleMirrorCaster");
        readonly GallopDepthPass depth = new GallopDepthPass();
        readonly DepthNormalOnlyPass normals = new DepthNormalOnlyPass((RenderPassEvent)150, RenderQueueRange.opaque, -1);
        readonly GallopBeforePass before = new GallopBeforePass();
        readonly GallopAfterPass after = new GallopAfterPass();
        readonly GallopInvertPass invertOn = new GallopInvertPass(true, 0), invertOff = new GallopInvertPass(false, 1000);
        readonly GallopTransparentSettingsPass transparentSettings;
        readonly GallopCallbackPass callback = new GallopCallbackPass();
        readonly GallopResolvePass resolve;
        readonly GallopScreenOutputPass screenOutput = new GallopScreenOutputPass();
        readonly GallopOpaqueCopyPass copyOpaque = new GallopOpaqueCopyPass();
        readonly GallopUiPass ui = new GallopUiPass();
        readonly bool simpleMirrorDefault;
        RTHandle targetHandle, colorHandle, depthHandle, normalHandle, depthTexture, screenHandle;
        readonly List<RenderTexture> temporaries = new List<RenderTexture>();
        CameraData current;
        RenderTexture colorSurface, depthSurface, outputSurface, multisampleSurface, sampledDepthSurface;
        RenderTexture screenStackSurface;
        public RenderTexture ColorSurface => colorSurface;
        public RenderTexture DepthSurface => depthSurface;
        public RenderTexture SampledDepthSurface => sampledDepthSurface;
        public RenderTexture OutputSurface => outputSurface;
        public RenderTexture MultisampleSurface => multisampleSurface;

#if UNITY_EDITOR
        public static Action<Camera, GallopRenderer, string[], int, float> SetupObserver;
        // Opt-in API-boundary experiments only. Null in ordinary Live capture.
        public delegate void CullingBoundaryProbe(Camera camera, ref ScriptableCullingParameters parameters);
        public static CullingBoundaryProbe CullingObserver;
#endif
        public GallopRenderer(GallopRendererData data) : base(data)
        {
            simpleMirrorDefault = data.SimpleMirror;
            transparentSettings = new GallopTransparentSettingsPass(data.ShadowTransparentReceive);
            resolve = new GallopResolvePass(this);
            supportedRenderingFeatures = new RenderingFeatures { msaa = true };
        }

        public override int SupportedCameraStackingTypes() => (1 << (int)CameraRenderType.Base) | (1 << (int)CameraRenderType.Overlay);

        // Ownership bridge: Viewer creates ordinary Unity cameras rather than
        // deserializing the game's CameraData prefab. Install before feature/Cull
        // dispatch, never replace an already configured original owner.
        void EnsureCameraOwner(in UrpCameraData data)
        {
            var cam = data.camera;
            if (!cam.TryGetComponent<CameraData>(out var owner))
            {
                owner = cam.gameObject.AddComponent<CameraData>();
                owner.IsClearDepth = data.clearDepth;
                owner.UseCameraOpaqueTexture = data.requiresOpaqueTexture;
            }
            if (simpleMirrorDefault) { owner.IsMirrorRendering = true; owner.MirrorCasterQuality = 1; }
            // The settings source is the current host quality owner; the camera
            // gating/math is the recovered CameraData.UpdateAntiAliasParameter.
            var request = !cam.allowMSAA && owner.RequestAntiAliasing == CameraData.AntiAliasLevel.Auto
                ? CameraData.AntiAliasLevel.None : owner.RequestAntiAliasing;
            var aa = NativeCameraAntiAlias.Evaluate((int)request, false, false, 1, cam.allowMSAA,
                cam.targetTexture != null, UniversalRenderPipeline.asset.msaaSampleCount);
            owner.RenderingAntiAliasing = aa.Level; owner.IsCreateAntialiasTexture = aa.CreateTexture;
        }

        public override void SetupCullingParameters(ref ScriptableCullingParameters parameters, ref UrpCameraData data)
        {
            EnsureCameraOwner(in data);
            var cam = data.camera;
            cam.TryGetComponent<CameraData>(out var owner);
            // Native UIManager.OnInitialize installs CameraData's begin-camera
            // callback (0x4c19b40). Its manager Setup must precede Cull, since an
            // original panel SetupCanvas can change sorting/canvas ownership.
            // The Viewer bridge only runs it for the corresponding UI camera.
            if (owner != null && owner.IsUIRendering && GraphicSettings.BlurPanels.PanelNum > 0)
                GraphicSettings.BlurPanels.SetupRendering();
            var additional = cam.GetComponent<UniversalAdditionalCameraData>();
            float distance = CameraData.ResolveShadowDistance(CameraData.GlobalMaxShadowDistance,
                owner != null ? owner.MaxShadowDistance : 0f, owner != null,
                additional == null || additional.renderShadows);
            data.maxShadowDistance = distance;
            parameters.cullingOptions = ApplyCullingOptions(parameters.cullingOptions, owner,
                UniversalRenderPipeline.asset.supportsMainLightShadows || UniversalRenderPipeline.asset.supportsAdditionalLightShadows,
                distance);
            parameters.maximumVisibleLights = UniversalRenderPipeline.maxVisibleAdditionalLights + 1;
            parameters.shadowDistance = distance;
#if UNITY_EDITOR
            CullingObserver?.Invoke(cam, ref parameters);
#endif
            // Native intentionally preserves the engine's enclosing-sphere,
            // iteration count and near-plane offset; no stock renderer call here.
        }

        public static CullingOptions ApplyCullingOptions(CullingOptions options, CameraData owner, bool supportsShadows, float distance)
        {
            if (owner != null)
            {
                options &= ~CullingOptions.OcclusionCull;
                options &= ~CullingOptions.NeedsReflectionProbes;
                // Preserve native order, including this UI branch's unusual mask.
                if (owner.IsUIRendering) { options |= CullingOptions.DisablePerObjectCulling; options &= CullingOptions.NeedsLighting; }
                if (owner.IsDisableObjectCulling) options |= CullingOptions.DisablePerObjectCulling;
                if (owner.IsDisableLightCulling) options &= ~CullingOptions.NeedsLighting;
            }
            if (!supportsShadows || Mathf.Approximately(distance, 0f)) options &= ~CullingOptions.ShadowCasters;
            return options;
        }

        public override void SetupLights(ScriptableRenderContext context, ref RenderingData data) => lights.Setup(context, ref data);

        public static void UpdateShadowSettings(ref RenderingData data)
        {
            if (data.shadowData.supportsAdditionalLightShadows || !(data.cameraData.maxShadowDistance > 0f)) return;
            var visible = data.cullResults.visibleLights;
            for (int i = 0; i < visible.Length; i++)
            {
                if (i == data.lightData.mainLightIndex || visible[i].lightType == LightType.Directional) continue;
                var light = visible[i].light;
                if (light == null || light.shadows == LightShadows.None || Mathf.Approximately(light.shadowStrength, 0f)) continue;
                data.shadowData.supportsAdditionalLightShadows = SystemInfo.supportsShadows;
                data.shadowData.supportsSoftShadows = true;
                break;
            }
        }

        public override void Setup(ScriptableRenderContext context, ref RenderingData data)
        {
            var camera = data.cameraData.camera;
            if (!camera.TryGetComponent<CameraData>(out current)) return;
            data.cameraData.clearDepth = current.IsClearDepth;
            current.ViewMatrix = data.cameraData.GetViewMatrix();
            current.ProjectionMatrix = data.cameraData.GetProjectionMatrix();
            var saved = data.cameraData.cameraTargetDescriptor;
            ReleaseHandles();
            // URP overlays inherit their base camera's target. A StandardRequest
            // temporarily binds only the base Camera, not each overlay Camera.
            RenderTexture outputTexture = data.cameraData.targetTexture;
            bool screenCamera = outputTexture == null;
            targetHandle = outputTexture != null ? RTHandles.Alloc((Texture)outputTexture)
                : RTHandles.Alloc(new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget));
            if (screenCamera)
            {
                // The game supplies a screen RenderTexture before this renderer.
                // Viewer cameras render directly to a swapchain instead, which
                // post shaders cannot sample. Keep a readable color owner until
                // all post passes finish, then present it to the backbuffer.
                // Overlay cameras must continue the base camera's completed
                // color, just as the game's cameras share one framebuffer.
                screenHandle = targetHandle;
                if (data.cameraData.renderType == CameraRenderType.Base || screenStackSurface == null)
                {
                    var screenDescriptor = saved;
                    screenDescriptor.msaaSamples = 1;
                    screenDescriptor.bindMS = false;
                    screenDescriptor.depthBufferBits = 24;
                    screenStackSurface = RenderTexture.GetTemporary(screenDescriptor);
                    temporaries.Add(screenStackSurface);
                }
                outputTexture = screenStackSurface;
                targetHandle = RTHandles.Alloc((Texture)screenStackSurface);
                screenOutput.Setup(targetHandle, screenHandle);
                EnqueuePass(screenOutput);
            }
            colorHandle = depthHandle = targetHandle;
            colorSurface = depthSurface = outputSurface = outputTexture;

            if (data.cameraData.targetTexture != null && data.cameraData.targetTexture.format == RenderTextureFormat.Depth)
            {
                ConfigureCameraTarget(targetHandle, targetHandle);
                EnqueueObjects(current, true);
                Observe(camera, ref data);
                return;
            }
            UpdateShadowSettings(ref data);
            bool needDepth = data.cameraData.requiresDepthTexture || data.cameraData.isSceneViewCamera || data.cameraData.isPreviewCamera;
            bool needNormals = false;
            foreach (var pass in activeRenderPassQueue)
            {
                needDepth |= (pass.input & (ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal)) != 0;
                needNormals |= (pass.input & ScriptableRenderPassInput.Normal) != 0;
            }
            bool simpleMirror = current.IsMirrorRendering && current.MirrorCasterQuality == 1;
            needDepth &= !simpleMirror;
            if (current.IsOverrideShadowResolution)
                data.shadowData.mainLightShadowmapWidth = data.shadowData.mainLightShadowmapHeight = current.OverrideShadowResolution;
            bool main = mainShadow.Setup(ref data);
            // The game pass consumes the dynamic support flag directly; no
            // URP14 internal additionalLightShadowsEnabled gate intervenes.
            bool extra = additionalShadow.Setup(ref data);
            var descriptor = outputTexture != null ? outputTexture.descriptor : saved;
            bool depthOnly = false;
            RenderTexture internalTexture = null;
            if (current.IsCreateAntialiasTexture || (screenCamera && current.RenderingAntiAliasing > 1))
            {
                descriptor.msaaSamples = current.RenderingAntiAliasing;
                descriptor.autoGenerateMips = false;
                RenderUtils.SetMSAARenderTextureDescriptor(ref descriptor);
                if (descriptor.depthBufferBits == 0) descriptor.depthBufferBits = 24;
                internalTexture = RenderTexture.GetTemporary(descriptor);
                colorHandle = depthHandle = RTHandles.Alloc((Texture)internalTexture);
                colorSurface = depthSurface = internalTexture;
                multisampleSurface = internalTexture;
                current.InternalRenderingTexture = internalTexture;
            }
            else
            {
                current.InternalRenderingTexture = null;
                if (descriptor.depthBufferBits == 0)
                {
                    depthOnly = true;
                    var depthDesc = descriptor;
                    depthDesc.colorFormat = RenderTextureFormat.Depth; depthDesc.depthBufferBits = 24;
                    internalTexture = RenderTexture.GetTemporary(depthDesc);
                    depthHandle = RTHandles.Alloc((Texture)internalTexture);
                    depthSurface = internalTexture;
                    current.InternalRenderingDepthTexture = internalTexture;
                }
            }
            if (internalTexture != null) temporaries.Add(internalTexture);
            ConfigureCameraTarget(colorHandle, depthHandle);
            if (main) EnqueuePass(mainShadow);
            if (extra) EnqueuePass(additionalShadow);
            if (before.Setup(current, colorHandle, depthHandle, descriptor, ref data)) EnqueuePass(before);
            after.Setup(saved.width, saved.height); EnqueuePass(after);
            data.cameraData.cameraTargetDescriptor.width = descriptor.width;
            data.cameraData.cameraTargetDescriptor.height = descriptor.height;
            if (current.IsInvertCulling || current.IsMirrorRendering) { EnqueuePass(invertOn); EnqueuePass(invertOff); }
            if (needDepth)
            {
                var dd = saved;
                dd.colorFormat = RenderTextureFormat.Depth; dd.depthBufferBits = 32; dd.msaaSamples = 1; dd.bindMS = false;
                RenderingUtils.ReAllocateIfNeeded(ref depthTexture, dd, FilterMode.Point, TextureWrapMode.Clamp, name: "_CameraDepthTexture");
                sampledDepthSurface = depthTexture.rt;
                if (needNormals)
                {
                    var nd = saved; nd.graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm; nd.depthBufferBits = 0; nd.msaaSamples = 1;
                    RenderingUtils.ReAllocateIfNeeded(ref normalHandle, nd, name: "_CameraNormalsTexture");
                    normals.Setup(depthTexture, normalHandle); EnqueuePass(normals);
                }
                else
                {
                    depth.Setup(depthTexture, current.ShadowCasterRenderingLayerMask, depthOnly ? 499 : 150);
                    EnqueuePass(depth);
                }
            }
            else if (data.cameraData.renderType == CameraRenderType.Base)
                Shader.SetGlobalTexture("_CameraDepthTexture", Texture2D.grayTexture);
            if (simpleMirror)
            {
                mirrorOpaque.Setup(current.RenderingLayerMask); mirrorTransparent.Setup(current.RenderingLayerMask);
                EnqueuePass(mirrorOpaque); EnqueuePass(mirrorTransparent);
            }
            else EnqueueObjects(current, false);
            // The preview scene retains Unity's authored default skybox. Live
            // owns its clear/background state and must not receive an extra sky.
            if (Live.Director.instance == null && !simpleMirror && !current.IsUIRendering &&
                camera.clearFlags == CameraClearFlags.Skybox && RenderSettings.skybox != null)
                EnqueuePass(previewSkybox);
            EnqueuePass(callback);
            if (internalTexture != null)
            {
                // Depth consumers run after color resolve. Retain the physical draw
                // surface until FinishRendering, including its MSAA depth.
                resolve.Setup(internalTexture, targetHandle, outputTexture, depthOnly, true);
                EnqueuePass(resolve);
            }
            Observe(camera, ref data);
        }

        void EnqueueObjects(CameraData owner, bool depthTarget)
        {
            if (owner.IsUIRendering) { if (ui.Setup(owner)) EnqueuePass(ui); return; }
            // Skip is checked independently by each original DrawObjects.Setup.
            // It does not suppress OpaqueBlit or TransparentSettings scheduling.
            if (!owner.IsSkipDrawObjectsPass)
            { opaque.Setup(owner.RenderingLayerMask); EnqueuePass(opaque); }
            if (!depthTarget && owner.UseCameraOpaqueTexture)
            { copyOpaque.Setup(colorHandle, owner.CameraOpaqueTextureSizeRate); EnqueuePass(copyOpaque); }
            if (!depthTarget && transparentSettings.Setup()) EnqueuePass(transparentSettings);
            if (!owner.IsSkipDrawObjectsPass)
            { transparent.Setup(owner.RenderingLayerMask); EnqueuePass(transparent); }
        }

        void Observe(Camera camera, ref RenderingData data)
        {
#if UNITY_EDITOR
            if (SetupObserver != null)
            {
                var queue = new List<string>();
                foreach (var p in activeRenderPassQueue) queue.Add(((int)p.renderPassEvent) + ":" + p.GetType().Name);
                SetupObserver(camera, this, queue.ToArray(), data.shadowData.mainLightShadowmapWidth, data.cameraData.maxShadowDistance);
            }
#endif
        }

        internal void ReleaseTemporary(RenderTexture texture)
        {
            if (temporaries.Remove(texture)) RenderTexture.ReleaseTemporary(texture);
            if (current != null)
            {
                if (current.InternalRenderingTexture == texture) current.InternalRenderingTexture = null;
                if (current.InternalRenderingDepthTexture == texture) current.InternalRenderingDepthTexture = null;
            }
        }

        internal void ResolveToOutput()
        {
            ConfigureCameraTarget(targetHandle, targetHandle);
            colorSurface = outputSurface;
        }

        public override void FinishRendering(CommandBuffer cmd)
        {
            // URP calls this after the last camera in the stack, not after Base.
            foreach (var texture in temporaries) RenderTexture.ReleaseTemporary(texture);
            temporaries.Clear();
            screenStackSurface = null;
            if (current != null) { current.InternalRenderingTexture = null; current.InternalRenderingDepthTexture = null; }
            ReleaseHandles();
            // These attachments are reused by ReAllocateIfNeeded and released on Dispose.
        }

        void ReleaseHandles()
        {
            if (colorHandle != null && colorHandle != targetHandle) colorHandle.Release();
            if (depthHandle != null && depthHandle != targetHandle && depthHandle != colorHandle) depthHandle.Release();
            targetHandle?.Release(); targetHandle = colorHandle = depthHandle = null;
            screenHandle?.Release(); screenHandle = null;
            colorSurface = depthSurface = multisampleSurface = sampledDepthSurface = null;
        }
        protected override void Dispose(bool disposing)
        {
            ReleaseHandles(); depthTexture?.Release(); normalHandle?.Release();
            mainShadow.Dispose(); additionalShadow.Dispose(); copyOpaque.Dispose(); ui.Dispose();
            foreach (var texture in temporaries) RenderTexture.ReleaseTemporary(texture);
            temporaries.Clear();
            screenStackSurface = null;
            // Public ForwardLights has an internal disposal API in URP14.
            typeof(ForwardLights).GetMethod("Cleanup", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(lights, null);
            base.Dispose(disposing);
        }
    }
}
