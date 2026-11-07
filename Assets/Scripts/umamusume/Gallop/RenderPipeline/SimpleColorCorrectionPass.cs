using System;
using Gallop.Live;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gallop.RenderPipeline
{
    // Recovered simple/nonselective/unmasked subset. Generic default550;
    // installed Director overrides its main-camera color parameter to604.
    public sealed class SimpleColorCorrectionPass : ScriptableRenderPass, IDisposable
    {
        public struct Parameter
        {
            public bool Enabled;
            public AnimationCurve Red, Green, Blue;
            public float Saturation;
            public RenderPassEvent RenderPassEvent;
            // Existing/default-valued callers retain the original generic550 path.
            public RenderPassEvent EffectiveEvent => RenderPassEvent == 0 ? UnityEngine.Rendering.Universal.RenderPassEvent.BeforeRenderingPostProcessing : RenderPassEvent;
            public bool IsValid => Enabled && Red != null && Green != null && Blue != null && Red.length > 0 && Green.length > 0 && Blue.length > 0 && !float.IsNaN(Saturation) && !float.IsInfinity(Saturation);
        }

        static readonly int Output = Shader.PropertyToID("_RecoveredColorCorrectionOutput");
        readonly ColorCorrectionCurveLut _lut = new ColorCorrectionCurveLut();
        AnimationCurve _red, _green, _blue;
        Parameter _parameter;
        Material _material;
        PostImageEffectFeature _feature;
        public Texture2D LutTexture => _lut.Texture;
        public int LutUploadCount { get; private set; }

        public SimpleColorCorrectionPass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        }

        // Authored curves are immutable during playback; cuts/seek replace their references.
        // Saturation-only changes do not require rebuilding the LUT.
        public bool Prepare(Parameter parameter)
        {
            _parameter = parameter;
            renderPassEvent = parameter.EffectiveEvent;
            if (!parameter.IsValid)
            {
                _red = _green = _blue = null;
                return false;
            }

            if (_lut.Texture == null || _red != parameter.Red || _green != parameter.Green || _blue != parameter.Blue)
            {
                _lut.Update(parameter.Red, parameter.Green, parameter.Blue);
                _red = parameter.Red;
                _green = parameter.Green;
                _blue = parameter.Blue;
                LutUploadCount++;
            }

            return true;
        }

        public bool Setup(Parameter parameter, PostImageEffectFeature feature)
        {
            _feature = feature;
            // Owner application occurs in the feature before this native gate.
            // Disabling the global flag must not allocate or upload a new LUT.
            if (!Gallop.GraphicSettings.IsColorCorrection)
                return false;
            if (!Prepare(parameter))
                return false;
            var shader = ShaderManager.GetShader(ShaderManager.ShaderKinds.ColorCorrectionCurvesSimple);
            if (shader == null || !shader.isSupported)
                return false;
            if (_material == null)
                _material = CoreUtils.CreateEngineMaterial(shader);
            else if (_material.shader != shader)
                _material.shader = shader;
            return _material.passCount > 0;
        }

        // Shared by the renderer and isolated command-buffer GPU tests.
        public static void Render(CommandBuffer cmd, Material material, Texture lut, float saturation, RenderTargetIdentifier source, RenderTargetIdentifier destination)
        {
            // The recovered shader uses global uniforms. Every draw explicitly sets them;
            // no dependency on whichever DOF/diffusion draw previously bound _RgbTex.
            CoreUtils.SetKeyword(material, "MASK_RECTANGLE", false);
            CoreUtils.SetKeyword(material, "MASK_VIGNETTE", false);
            material.SetInt("_StencilComp", (int)CompareFunction.Always);
            material.SetInt("_StencilOp", (int)StencilOp.Keep);
            material.SetInt("_StencilMask", 0);
            cmd.SetGlobalTexture("_RgbTex", lut);
            cmd.SetGlobalFloat("_Saturation", saturation);
            cmd.SetGlobalFloat("_PostFilmPower", 0f); // zero mask power = full correction
            cmd.Blit(source, destination, material, 0);
            cmd.SetGlobalTexture("_RgbTex", Texture2D.blackTexture);
        }

        // Shared late-camera draw, including temporary ownership and safe in-place
        // camera writes. Isolated GPU tests exercise this exact production function.
        public static void RenderLate(CommandBuffer cmd, Material material, Texture lut, float saturation, RenderTargetIdentifier source, RenderTargetIdentifier cameraAttachment, RenderTextureDescriptor descriptor)
        {
            cmd.GetTemporaryRT(Output, descriptor, FilterMode.Bilinear);
            Render(cmd, material, lut, saturation, source, Output);
            cmd.Blit(Output, cameraAttachment);
            cmd.ReleaseTemporaryRT(Output);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (_feature == null || _material == null || !_parameter.IsValid || _lut.Texture == null)
                return;
            bool late = (int)renderPassEvent > 550;
            var target = renderingData.cameraData.renderer.cameraColorTargetHandle;
            if (late && target == null)
                return;
            var cmd = CommandBufferPool.Get("Recovered.SimpleColorCorrection");
            try
            {
                var descriptor = renderingData.cameraData.cameraTargetDescriptor;
                descriptor.depthBufferBits = 0;
                descriptor.msaaSamples = 1;
                descriptor.bindMS = false;
                descriptor.useMipMap = descriptor.autoGenerateMips = false;
                // Do NOT reset SourceRT: it may hold the immediately preceding radial result.
                var inputHandle = _feature.SourceRT;
                var inputRT = inputHandle.RtId;

                if (late)
                {
                    // Native Color.Execute0x5204d40 resolves late color directly to
                    // the camera attachment; SourceRT must already include flare600.
                    RenderLate(cmd, _material, _lut.Texture, _parameter.Saturation, inputRT, target.nameID, descriptor);
                    _feature.ResetSourceRT(cmd, ref renderingData);
                }
                else
                {
                    cmd.GetTemporaryRT(Output, descriptor, FilterMode.Bilinear);
                    Render(cmd, _material, _lut.Texture, _parameter.Saturation, inputRT, Output);
                    _feature.SetSourceRT(RenderTextureHandle.Make(Output, descriptor.width, descriptor.height), cmd, ref renderingData);
                }

                context.ExecuteCommandBuffer(cmd);

            }
            finally
            {
                CommandBufferPool.Release(cmd);
            }
        }

        public void Dispose()
        {
            _lut.Dispose();
            CoreUtils.Destroy(_material);
            _material = null;
            _red = _green = _blue = null;
            _feature = null;
            _parameter = default;
        }
    }
}
