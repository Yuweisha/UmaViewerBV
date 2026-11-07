using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gallop.RenderPipeline
{
    internal sealed class GallopDrawPass : ScriptableRenderPass
    {
        readonly bool opaque;
        readonly List<ShaderTagId> tags;
        readonly List<ShaderTagId> forwardTags;
        FilteringSettings filter;
        public GallopDrawPass(bool isOpaque, int evt, string tag = null)
        {
            opaque = isOpaque; renderPassEvent = (RenderPassEvent)evt;
            tags = new List<ShaderTagId> { new ShaderTagId(tag ?? "SRPDefaultUnlit") };
            // RenderUtils.cctor 0x51dc170 +0x40/+0x48 and DrawObjects.Execute
            // 0x5211420 perform TWO DrawRenderers calls, not one tag fallback.
            if (tag == null) forwardTags = new List<ShaderTagId> { new ShaderTagId("PreUniversalForward"),
                new ShaderTagId("UniversalForward"), new ShaderTagId("AfterUniversalForward"),
                new ShaderTagId("UniversalForwardOnly"), new ShaderTagId("Outline") };
            filter = new FilteringSettings(opaque ? RenderQueueRange.opaque : RenderQueueRange.transparent, -1);
        }
        public void Setup(uint mask) => filter.renderingLayerMask = mask;
        public override void Execute(ScriptableRenderContext context, ref RenderingData data)
        {
            var cmd = CommandBufferPool.Get(opaque ? "Gallop opaque" : "Gallop transparent");
            bool flip = data.cameraData.IsCameraProjectionMatrixFlipped();
            cmd.SetGlobalVector("_DrawObjectPassData", new Vector4(0, 0, 0, opaque ? 1 : 0));
            cmd.SetGlobalVector("_ScaleBiasRt", flip ? new Vector4(-1, 1, -1, 1) : new Vector4(1, 0, 1, 1));
            context.ExecuteCommandBuffer(cmd); CommandBufferPool.Release(cmd);
            var drawing = CreateDrawingSettings(tags, ref data, opaque ? data.cameraData.defaultOpaqueSortFlags : SortingCriteria.CommonTransparent);
            context.DrawRenderers(data.cullResults, ref drawing, ref filter);
            if (forwardTags != null)
            {
                drawing = CreateDrawingSettings(forwardTags, ref data, opaque ? data.cameraData.defaultOpaqueSortFlags : SortingCriteria.CommonTransparent);
                context.DrawRenderers(data.cullResults, ref drawing, ref filter);
            }
        }
    }

    internal sealed class GallopDepthPass : ScriptableRenderPass
    {
        RTHandle target;
        FilteringSettings filter = new FilteringSettings(RenderQueueRange.opaque, -1);
        public void Setup(RTHandle texture, uint mask, int evt) { target = texture; filter.renderingLayerMask = mask; renderPassEvent = (RenderPassEvent)evt; }
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData data)
        {
            ConfigureTarget(target); ConfigureClear(ClearFlag.All, Color.black);
            cmd.SetGlobalTexture("_CameraDepthTexture", target.nameID);
        }
        public override void Execute(ScriptableRenderContext context, ref RenderingData data)
        {
            var draw = CreateDrawingSettings(new ShaderTagId("ShadowCaster"), ref data, data.cameraData.defaultOpaqueSortFlags);
            draw.perObjectData = PerObjectData.None;
            context.DrawRenderers(data.cullResults, ref draw, ref filter);
            var cmd = CommandBufferPool.Get("Gallop publish camera depth");
            cmd.SetGlobalTexture("_CameraDepthTexture", target.nameID);
            context.ExecuteCommandBuffer(cmd); CommandBufferPool.Release(cmd);
        }
    }

    internal sealed class GallopBeforePass : ScriptableRenderPass
    {
        CameraData owner;
        bool resize, clearColorFlag, clearDepth, blit;
        RTHandle color, depth;
        Rect rect;
        public GallopBeforePass() { renderPassEvent = (RenderPassEvent)200; }
        public bool Setup(CameraData data, RTHandle c, RTHandle d, RenderTextureDescriptor descriptor, ref RenderingData rd)
        {
            owner = null; clearColorFlag = clearDepth = blit = false;
            var cam = rd.cameraData.camera;
            bool overlay = rd.cameraData.renderType == CameraRenderType.Overlay;
            var viewport = overlay && rd.cameraData.baseCamera != null ? rd.cameraData.baseCamera.pixelRect : cam.pixelRect;
            resize = viewport.width != descriptor.width || viewport.height != descriptor.height;
            if (resize) { clearDepth = rd.cameraData.clearDepth; rd.cameraData.clearDepth = false; }
            if (overlay) clearColorFlag = cam.clearFlags == CameraClearFlags.Color;
            bool clear = clearColorFlag || clearDepth;
            if (overlay) blit = (data.IsCreateAntialiasTexture && !clear) || resize;
            if (!(blit || clear || resize || data.ForceViewProjectionMatrix)) return false;
            owner = data; color = c; depth = d;
            // Native SetBeforeRenderingPass.Setup 0x5392de7 reads pixelRect.
            // SetViewport consumes pixels; camera.rect would restrict this to 1px.
            rect = viewport;
            if(!overlay){data.ViewMatrix = cam.worldToCameraMatrix; data.ProjectionMatrix = cam.projectionMatrix;}
            return true;
        }
        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor descriptor)
        {
            ConfigureTarget(color, depth);
            ConfigureClear(!resize && clearColorFlag ? ClearFlag.Depth : ClearFlag.None, Color.black);
        }
        public override void Execute(ScriptableRenderContext context, ref RenderingData data)
        {
            var cmd = CommandBufferPool.Get("Gallop before rendering");
            try
            {
                if (blit) { cmd.Blit(data.cameraData.targetTexture, color.nameID); context.ExecuteCommandBuffer(cmd); cmd.Clear(); }
                if (resize || owner.ForceViewProjectionMatrix)
                {
                    cmd.SetViewport(rect);
                    if (clearColorFlag || clearDepth) cmd.ClearRenderTarget(clearDepth, clearColorFlag, data.cameraData.camera.backgroundColor);
                    cmd.SetViewProjectionMatrices(owner.ViewMatrix, owner.ProjectionMatrix);
                    bool texture = data.cameraData.targetTexture != null || color.rt != null;
                    var gpu = GL.GetGPUProjectionMatrix(owner.ProjectionMatrix, SystemInfo.graphicsUVStartsAtTop && texture);
                    var iv = owner.ViewMatrix.inverse; var ip = gpu.inverse;
                    var wc = Matrix4x4.Scale(new Vector3(1, 1, -1)) * owner.ViewMatrix;
                    cmd.SetGlobalMatrix("unity_WorldToCamera", wc); cmd.SetGlobalMatrix("unity_CameraToWorld", wc.inverse);
                    cmd.SetGlobalMatrix("unity_MatrixInvV", iv); cmd.SetGlobalMatrix("unity_MatrixInvP", ip);
                    cmd.SetGlobalMatrix("unity_MatrixInvVP", iv * ip);
                }
                context.ExecuteCommandBuffer(cmd);
            }
            finally { CommandBufferPool.Release(cmd); }
        }
    }

    internal sealed class GallopAfterPass : ScriptableRenderPass
    {
        int width, height;
        public GallopAfterPass() { renderPassEvent = (RenderPassEvent)1000; }
        public void Setup(int w, int h) { width = w; height = h; }
        public override void Execute(ScriptableRenderContext context, ref RenderingData data)
        { data.cameraData.cameraTargetDescriptor.width = width; data.cameraData.cameraTargetDescriptor.height = height; }
    }

    internal sealed class GallopInvertPass : ScriptableRenderPass
    {
        readonly bool invert;
        public GallopInvertPass(bool value, int evt) { invert = value; renderPassEvent = (RenderPassEvent)evt; }
        public override void Execute(ScriptableRenderContext context, ref RenderingData data)
        {
            var cmd = CommandBufferPool.Get("Gallop invert culling"); cmd.SetInvertCulling(invert);
            context.ExecuteCommandBuffer(cmd); CommandBufferPool.Release(cmd);
        }
    }

    internal sealed class GallopTransparentSettingsPass : ScriptableRenderPass
    {
        readonly bool shouldReceiveShadows;
        public GallopTransparentSettingsPass(bool shadowReceiveSupported)
        {
            renderPassEvent = (RenderPassEvent)450;
            shouldReceiveShadows = shadowReceiveSupported; // native ctor +0xE0
        }
        // Native 0x523f440: enqueue only when transparent receive is disabled.
        public bool Setup() => !shouldReceiveShadows;
        public override void Execute(ScriptableRenderContext context, ref RenderingData data)
        {
            var cmd = CommandBufferPool.Get("Gallop transparent settings");
            CoreUtils.SetKeyword(cmd, "_MAIN_LIGHT_SHADOWS", shouldReceiveShadows);
            CoreUtils.SetKeyword(cmd, "_MAIN_LIGHT_SHADOWS_CASCADE", shouldReceiveShadows);
            CoreUtils.SetKeyword(cmd, "_ADDITIONAL_LIGHT_SHADOWS", shouldReceiveShadows);
            context.ExecuteCommandBuffer(cmd); CommandBufferPool.Release(cmd);
        }
    }

    internal sealed class GallopCallbackPass : ScriptableRenderPass
    {
        public GallopCallbackPass() { renderPassEvent = (RenderPassEvent)550; }
        public override void Execute(ScriptableRenderContext context, ref RenderingData data) => context.InvokeOnRenderObjectCallback();
    }

    internal sealed class GallopOpaqueCopyPass : ScriptableRenderPass
    {
        RTHandle source, texture;
        float sizeRate = 1f;
        public GallopOpaqueCopyPass() { renderPassEvent = (RenderPassEvent)450; }
        public void Setup(RTHandle handle, float rate) { source = handle; sizeRate = Mathf.Clamp(rate, 0.1f, 0.5f); }
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData data)
        {
            var desc = data.cameraData.cameraTargetDescriptor; desc.depthBufferBits = 0; desc.msaaSamples = 1; desc.bindMS = false;
            desc.width = (int)(desc.width * sizeRate); desc.height = (int)(desc.height * sizeRate);
            RenderingUtils.ReAllocateIfNeeded(ref texture, desc, FilterMode.Point, TextureWrapMode.Clamp, name: "_CameraOpaqueTexture");
        }
        public override void Execute(ScriptableRenderContext context, ref RenderingData data)
        {
            var cmd = CommandBufferPool.Get("Gallop opaque copy");
            RenderTargetIdentifier copySource = source.nameID;
            var renderer = data.cameraData.renderer as GallopRenderer;
            var surface = renderer?.ColorSurface;
            var destination = renderer?.OutputSurface;
            if (surface != null && surface.bindTextureMS && destination != null)
            { cmd.ResolveAntiAliasedSurface(surface, destination); copySource = destination; }
            cmd.Blit(copySource, texture.nameID); cmd.SetGlobalTexture("_CameraOpaqueTexture", texture.nameID);
            context.ExecuteCommandBuffer(cmd); CommandBufferPool.Release(cmd);
        }
        public void Dispose() => texture?.Release();
    }

    // Native UI layer dispatch, including BlurPanelManager consumers.
    internal sealed class GallopUiPass : ScriptableRenderPass
    {
        readonly List<ShaderTagId> tags = new List<ShaderTagId> { new ShaderTagId("SRPDefaultUnlit"),
            new ShaderTagId("PreUniversalForward"), new ShaderTagId("UniversalForward"),
            new ShaderTagId("AfterUniversalForward"), new ShaderTagId("UniversalForwardOnly") };
        CameraData owner;
        Material blur;
        Dictionary<int, short> sortingLayers;
        public GallopUiPass() { renderPassEvent = (RenderPassEvent)450; }
        public bool Setup(CameraData value)
        {
            owner = null;
            if (blur == null)
            {
                var shader = ShaderManager.GetShader(ShaderManager.ShaderKinds.UIBlur);
                if (shader == null) return false;
                blur = CoreUtils.CreateEngineMaterial(shader);
            }
            if (sortingLayers == null)
            {
                sortingLayers = new Dictionary<int, short>(SortingLayer.layers.Length);
                foreach (var layer in SortingLayer.layers) sortingLayers.Add(layer.id, unchecked((short)layer.value));
            }
            owner = value; return true;
        }
        public override void Execute(ScriptableRenderContext context, ref RenderingData data)
        {
            var cmd = CommandBufferPool.Get("Gallop UI rendering");
            try
            {
                var manager = GraphicSettings.BlurPanels;
                if (manager.PanelNum <= 0)
                {
                    if (owner.IsOpaqueRenderingForUICamera) OnRendering(context, ref data, true, SortingLayerRange.all);
                    OnRendering(context, ref data, false, SortingLayerRange.all);
                }
                else foreach (var layer in sortingLayers)
                {
                    if (manager.OnBlurRendering(context, ref data, cmd, layer.Key, layer.Value, this, blur)) continue;
                    var range = new SortingLayerRange(layer.Value, layer.Value);
                    if (owner.IsOpaqueRenderingForUICamera) OnRendering(context, ref data, true, range);
                    OnRendering(context, ref data, false, range);
                }
                context.ExecuteCommandBuffer(cmd);
            }
            finally { CommandBufferPool.Release(cmd); }
        }
        internal void OnRendering(ScriptableRenderContext context, ref RenderingData data, bool opaque, SortingLayerRange range)
        {
            var draw = CreateDrawingSettings(tags, ref data, opaque ? data.cameraData.defaultOpaqueSortFlags : SortingCriteria.CommonTransparent);
            var filter = new FilteringSettings(opaque ? RenderQueueRange.opaque : RenderQueueRange.transparent,
                data.cameraData.camera.cullingMask, uint.MaxValue);
            filter.sortingLayerRange = range;
            context.DrawRenderers(data.cullResults, ref draw, ref filter);
        }
        public void Dispose() => CoreUtils.Destroy(blur);
    }

    internal sealed class GallopScreenOutputPass : ScriptableRenderPass
    {
        RTHandle source, output;
        // Composite, monitor copy and Live fade finish at 1003..1005.
        public GallopScreenOutputPass() { renderPassEvent = (RenderPassEvent)1006; }
        public void Setup(RTHandle color, RTHandle screen) { source = color; output = screen; }
        public override void Execute(ScriptableRenderContext context, ref RenderingData data)
        {
            var cmd = CommandBufferPool.Get("Gallop screen output");
            try
            {
                cmd.Blit(source.nameID, output.nameID);
                cmd.SetRenderTarget(output.nameID);
                context.ExecuteCommandBuffer(cmd);
                if (data.cameraData.rendersOverlayUI) context.DrawUIOverlay(data.cameraData.camera);
            }
            finally { CommandBufferPool.Release(cmd); }
        }
    }

    internal sealed class GallopResolvePass : ScriptableRenderPass
    {
        readonly GallopRenderer renderer;
        RenderTexture texture, outputTexture;
        RTHandle target;
        bool depthOnly, delay;
        public GallopResolvePass(GallopRenderer owner) { renderer = owner; renderPassEvent = (RenderPassEvent)549; }
        public void Setup(RenderTexture t, RTHandle output, RenderTexture surface, bool depth, bool delayed) { texture = t; target = output; outputTexture = surface; depthOnly = depth; delay = delayed; }
        public override void Execute(ScriptableRenderContext context, ref RenderingData data)
        {
            if (!depthOnly)
            {
                var cmd = CommandBufferPool.Get("Gallop renderer resolve");
                if (texture.bindTextureMS && outputTexture != null) cmd.ResolveAntiAliasedSurface(texture, outputTexture);
                else cmd.Blit(texture, target.nameID);
                // URP14 attachment cache and immediate Texture.active ownership
                // must leave the transient draw surface before it is returned.
                cmd.SetRenderTarget(target.nameID);
                context.ExecuteCommandBuffer(cmd); CommandBufferPool.Release(cmd);
            }
            renderer.ResolveToOutput();
            if (!delay) renderer.ReleaseTemporary(texture);
        }
    }
}
