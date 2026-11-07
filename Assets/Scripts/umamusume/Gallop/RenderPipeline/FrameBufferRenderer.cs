using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UrpCameraData = UnityEngine.Rendering.Universal.CameraData;

namespace Gallop.RenderPipeline
{
    // Native 0x52303c0, ordinary and split-window output.
    public sealed class FrameBufferRenderer : ScriptableRenderer
    {
        public static Func<bool> IsLandscapeMode = () => UnityEngine.Screen.width > UnityEngine.Screen.height;
        // The Viewer has no vertical game-window manager. Its future owner must
        // publish Screen.IsVertical explicitly, independently of camera aspect.
        public static Func<bool> IsGameScreenVertical = () => false;
        public static Func<int> WindowWidth = () => UnityEngine.Screen.width;
        readonly FrameBufferPass pass;
        RTHandle target;
        public FrameBufferRenderer(FrameBufferRendererData data) : base(data)
        {
            pass = new FrameBufferPass(data.BlitShader);
            supportedRenderingFeatures = new RenderingFeatures { msaa = true };
        }
        public override int SupportedCameraStackingTypes() => 1 << (int)CameraRenderType.Base;
        public override void SetupCullingParameters(ref ScriptableCullingParameters p, ref UrpCameraData data)
        {
            // Native 0x52302f0 does not change distance/max-visible-lights.
            p.cullingOptions &= ~(CullingOptions.OcclusionCull | CullingOptions.NeedsLighting |
                CullingOptions.NeedsReflectionProbes | CullingOptions.ShadowCasters);
            p.cullingOptions |= CullingOptions.DisablePerObjectCulling;
        }
        public override void Setup(ScriptableRenderContext context, ref RenderingData data)
        {
            if (!data.cameraData.camera.TryGetComponent<FrameBufferRenderCameraData>(out var owner)) return;
            // Native ordinary-window output is the backbuffer. An offline SRP
            // request redirects that output to its supplied camera texture;
            // unlike UniversalRenderer this parent must bind it explicitly.
            target?.Release(); target = data.cameraData.targetTexture != null
                ? RTHandles.Alloc((Texture)data.cameraData.targetTexture)
                : RTHandles.Alloc(new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget));
            ConfigureCameraTarget(target, target);
            pass.Setup(owner, IsLandscapeMode()); EnqueuePass(pass);
        }
        public override void FinishRendering(CommandBuffer cmd) { target?.Release(); target = null; }
        protected override void Dispose(bool disposing) { target?.Release(); pass.Dispose(); base.Dispose(disposing); }

        sealed class FrameBufferPass : ScriptableRenderPass
        {
            readonly Shader shader;
            Material material, transparent;
            RenderTexture source, right, left;
            bool landscape;
            public FrameBufferPass(Shader value) { shader = value; renderPassEvent = (RenderPassEvent)600; }
            public void Setup(FrameBufferRenderCameraData owner, bool isLandscape)
            {
                source = owner.ScreenTexture; landscape = isLandscape;
                if (landscape) { right = owner.ScreenTextureForRightUI; left = owner.ScreenTextureForLeftUI; }
                // SetupForGamepadSoftwareCursor is the native shared empty RET.
                // Do not bind owner.ScreenTextureForGamepadSoftwareCursor here.
            }
            public override void Execute(ScriptableRenderContext context, ref RenderingData data)
            {
                bool split = landscape && IsGameScreenVertical();
                // Native 0x5212580 returns before allocating a command buffer.
                if (split && (source == null || right == null || left == null)) return;
                if (material == null)
                {
                    // Original player asset path17131 references Shader path10,
                    // verified name Gallop/UI/SimpleCopy, also in shader.a.
                    var original = shader != null ? shader : ShaderManager.GetShader(ShaderManager.ShaderKinds.UISimpleCopy);
                    if (original == null) throw new InvalidOperationException("Original Gallop/UI/SimpleCopy shader unavailable");
                    material = CoreUtils.CreateEngineMaterial(original); transparent = CoreUtils.CreateEngineMaterial(original);
                    transparent.SetFloat("_IsDisableFixedAlpha", 1f);
                }
                var cmd = CommandBufferPool.Get("Gallop frame buffer");
                try
                {
                    if (split)
                    {
                        float scale = FrameBufferWindowLayout.WindowScaleRate(WindowWidth(), landscape, true);
                        cmd.SetViewport(FrameBufferWindowLayout.Left(scale));
                        cmd.Blit(left, BuiltinRenderTextureType.CurrentActive, material, 0);
                        cmd.SetViewport(FrameBufferWindowLayout.Center(scale));
                        cmd.Blit(source, BuiltinRenderTextureType.CurrentActive, material, 0);
                        cmd.SetViewport(FrameBufferWindowLayout.Right(scale));
                        cmd.Blit(right, BuiltinRenderTextureType.CurrentActive, material, 0);
                        // Cursor setup is an empty native RET; its private field
                        // is never assigned through the recovered owner API.
                    }
                    else
                    {
                        if (landscape) cmd.SetViewport(data.cameraData.camera.pixelRect);
                        if (source != null) cmd.Blit(source, BuiltinRenderTextureType.CurrentActive, material, 0);
                        else cmd.ClearRenderTarget(true, true, Color.black, 0f);
                    }
                    context.ExecuteCommandBuffer(cmd);
                }
                finally { CommandBufferPool.Release(cmd); }
            }
            public void Dispose() { CoreUtils.Destroy(material); CoreUtils.Destroy(transparent); }
        }
    }
}
