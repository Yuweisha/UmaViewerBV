using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gallop.RenderPipeline
{
    // Independent original ColorGrading slot; this is not the RGB curve LUT.
    public sealed class ColorGradingPass : ScriptableRenderPass, IDisposable
    {
        public struct Parameter
        {
            public bool IsEnable;
            public Texture Lut;
            public float Rate;
            public RenderPassEvent Event;
            public static Parameter Default() => new Parameter
            {
                Rate = 1f,
                Event = (RenderPassEvent)550
            };
        }

        // Optional fixture override; production reads the recovered original
        // static flag and original SetGameQualityImageEffect updates.
        public static bool? OriginalGlobalEnabled { get; set; }

        readonly int output = Shader.PropertyToID("ColorGradingPass_RT0");
        Material material;
        PostImageEffectFeature feature;
        CameraData camera;
        public ColorGradingPass()
        {
            renderPassEvent = (RenderPassEvent)550;
        }

        public bool Setup(CameraData camera, PostImageEffectFeature feature)
        {
            this.camera = null;
            this.feature = null;
            if (camera == null)
                throw new NullReferenceException();
            var p = camera.Parameter.ColorGrading;
            if (!p.IsEnable)
                return false;
            if (!(OriginalGlobalEnabled ?? Gallop.GraphicSettings.IsColorGrading) || p.Lut == null)
                return false;
            if (material == null)
            {
                Shader shader = ShaderManager.GetShader(ShaderManager.ShaderKinds.ColorGrading);
                if (shader == null)
                    return false;
                material = new Material(shader);
            }

            renderPassEvent = p.Event;
            this.feature = feature;
            this.camera = camera;
            return true;
        }

        public static void Render(CommandBuffer cmd, Material material, Texture lut, float rate, RenderTargetIdentifier source, RenderTargetIdentifier destination)
        {
            int width = lut.width, height = lut.height;
            var uniforms = GetUniforms(width, height, lut.height, rate);
            cmd.SetGlobalVector("_Lut2D_Params", uniforms[0].Vector);
            cmd.SetGlobalTexture("_Lut2D", lut);
            cmd.SetGlobalFloat("_Lut2D_Rate", uniforms[1].Scalar);
            cmd.Blit(source, destination, material);
        }

        public static NativeEffectUniform[] GetUniforms(int width, int height, int secondHeight, float rate) => new[]
        {
            NativeEffectUniform.V(0x10e, new Vector4(1f / width, 1f / height, secondHeight - 1f, 0)),
            NativeEffectUniform.F(0x10f, rate)
        };
        public override void Execute(ScriptableRenderContext context, ref RenderingData data)
        {
            Shader shader = ShaderManager.GetShader(ShaderManager.ShaderKinds.ColorGrading);
            if (ReferenceEquals(material, null) || ReferenceEquals(material.shader, null))
                throw new NullReferenceException();
            if (!material.shader.Equals(shader))
                material.shader = shader;
            var p = camera.Parameter.ColorGrading;
            var cmd = CommandBufferPool.Get("ColorGradingPass");
            try
            {
                var source = feature.SourceRT;
                var target = RenderTextureHandle.Make(output, source.Width, source.Height);
                target.GetTemporaryRT(cmd, FilterMode.Bilinear);
                Render(cmd, material, p.Lut, p.Rate, source.RtId, target.RtId);
                if ((int)renderPassEvent <= 550)
                    feature.SetSourceRT(target, cmd, ref data);
                else
                {
                    cmd.Blit(target.RtId, data.cameraData.renderer.cameraColorTargetHandle.nameID);
                    target.ReleaseTemporaryRT(cmd);
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
            CoreUtils.Destroy(material);
            material = null;
            camera = null;
            feature = null;
        }
    }
}
