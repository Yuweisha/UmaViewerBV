using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gallop.RenderPipeline
{
    public struct NativeEffectUniform
    {
        public int Property;
        public string Scope;
        public Vector4 Vector;
        public float Scalar;
        public bool IsFloat;
        public static NativeEffectUniform V(int p, Vector4 v, string scope = "global") => new NativeEffectUniform
        {
            Property = p,
            Vector = v,
            Scope = scope
        };
        public static NativeEffectUniform F(int p, float v, string scope = "global") => new NativeEffectUniform
        {
            Property = p,
            Scalar = v,
            Scope = scope,
            IsFloat = true
        };
    }

    // Common native single-temporary route, including the event>550 direct resolve.
    public abstract class NativeSingleBlitPass : ScriptableRenderPass, IDisposable
    {
        readonly int shaderKind, output;
        readonly string trace;
        readonly FilterMode filter;
        protected Material material;
        protected CameraData camera;
        PostImageEffectFeature feature;
        protected NativeSingleBlitPass(int kind, string rt, string trace, FilterMode filter = FilterMode.Bilinear)
        {
            shaderKind = kind;
            output = Shader.PropertyToID(rt);
            this.trace = trace;
            this.filter = filter;
            renderPassEvent = (RenderPassEvent)550;
        }

        protected abstract bool IsEnabled(PostImageEffectFeature.Parameter parameter);
        protected abstract RenderPassEvent Event(PostImageEffectFeature.Parameter parameter);
        public abstract NativeEffectUniform[] GetUniforms(PostImageEffectFeature.Parameter parameter);
        void SetParameters(CommandBuffer cmd, PostImageEffectFeature.Parameter parameter)
        {
            foreach (var u in GetUniforms(parameter))
            {
                int id = ShaderManager.GetPropertyId((ShaderManager.PropertyId)u.Property);
                if (u.Scope == "global")
                {
                    if (u.IsFloat)
                        cmd.SetGlobalFloat(id, u.Scalar);
                    else
                        cmd.SetGlobalVector(id, u.Vector);
                }
                else if (u.IsFloat)
                    material.SetFloat(id, u.Scalar);
                else if (u.Scope == "materialColor")
                    material.SetColor(id, u.Vector);
                else
                    material.SetVector(id, u.Vector);
            }
        }

        protected virtual int ShaderPass(PostImageEffectFeature.Parameter parameter) => -1;
        protected virtual bool ParametersBeforeAllocation => false;

        public bool Setup(CameraData c, PostImageEffectFeature f)
        {
            camera = null;
            feature = null;
            if (c == null)
                throw new NullReferenceException();
            var p = c.Parameter;
            if (!IsEnabled(p))
                return false;
            if (material == null)
            {
                var shader = ShaderManager.GetShader((ShaderManager.ShaderKinds)shaderKind);
                if (shader == null)
                    return false;
                material = new Material(shader);
            }

            renderPassEvent = Event(p);
            camera = c;
            feature = f;
            return true;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData data)
        {
            var shader = ShaderManager.GetShader((ShaderManager.ShaderKinds)shaderKind);
            if (ReferenceEquals(material, null) || ReferenceEquals(material.shader, null))
                throw new NullReferenceException();
            if (!material.shader.Equals(shader))
                material.shader = shader;
            var p = camera.Parameter;
            var cmd = CommandBufferPool.Get(trace);
            try
            {
                if (ParametersBeforeAllocation)
                    SetParameters(cmd, p);
                var source = feature.SourceRT;
                var target = RenderTextureHandle.Make(output, source.Width, source.Height);
                target.GetTemporaryRT(cmd, filter);
                if (!ParametersBeforeAllocation)
                    SetParameters(cmd, p);
                int pass = ShaderPass(p);
                if (pass == -1)
                    cmd.Blit(source.RtId, target.RtId, material);
                else
                    cmd.Blit(source.RtId, target.RtId, material, pass);
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

    public sealed class ChromaticAberrationPass : NativeSingleBlitPass
    {
        public struct Parameter
        {
            public bool IsEnable;
            public Vector4 ColorOffsetX, ColorOffsetY, MixParameter;
            public int Type;
            public RenderPassEvent RenderPassEvent;
            public static Parameter Default() => new Parameter
            {
                RenderPassEvent = (RenderPassEvent)550
            };
        }

        public ChromaticAberrationPass() : base(0xaa, "ChromaticAberrationPass_RT0", "ChromaticAberrationPass")
        {
        }

        protected override bool IsEnabled(PostImageEffectFeature.Parameter p) => p.ChromaticAberration.IsEnable;
        protected override RenderPassEvent Event(PostImageEffectFeature.Parameter p) => p.ChromaticAberration.RenderPassEvent;
        protected override int ShaderPass(PostImageEffectFeature.Parameter p) => p.ChromaticAberration.Type;
        public override NativeEffectUniform[] GetUniforms(PostImageEffectFeature.Parameter p) => new[]
        {
            NativeEffectUniform.V(0x10c, p.ChromaticAberration.ColorOffsetX),
            NativeEffectUniform.V(0x10d, p.ChromaticAberration.ColorOffsetY),
            NativeEffectUniform.V(0xb8, p.ChromaticAberration.MixParameter)
        };
    }

    public sealed class VortexPass : NativeSingleBlitPass
    {
        public struct Parameter
        {
            public bool IsEnable;
            public Vector4 VortexArea;
            public float RotVolume, DepthClip;
            public RenderPassEvent RenderPassEvent;
            public static Parameter Default() => new Parameter
            {
                VortexArea = new Vector4(.5f, .5f, .5f, .5f),
                DepthClip = 1,
                RenderPassEvent = (RenderPassEvent)550
            };
        }

        public VortexPass() : base(0xaf, "VortexPass_RT0", "VortexPass", FilterMode.Point)
        {
        }

        protected override bool IsEnabled(PostImageEffectFeature.Parameter p) => p.Vortex.IsEnable;
        protected override RenderPassEvent Event(PostImageEffectFeature.Parameter p) => p.Vortex.RenderPassEvent;
        protected override bool ParametersBeforeAllocation => true;

        public static Vector4 ClampArea(Vector4 v)
        {
            v.z = v.z > .001f ? v.z : .001f;
            v.w = v.w > .001f ? v.w : .001f;
            return v;
        }

        public static void SetMinVortexArea(ref Vector4 v)
        {
            v = ClampArea(v);
        }

        public override NativeEffectUniform[] GetUniforms(PostImageEffectFeature.Parameter p) => new[]
        {
            NativeEffectUniform.V(0x108, ClampArea(p.Vortex.VortexArea)),
            NativeEffectUniform.F(0x109, p.Vortex.RotVolume),
            NativeEffectUniform.F(0x107, p.Vortex.DepthClip)
        };
    }

    public sealed class LetterBoxPass : NativeSingleBlitPass
    {
        public struct Parameter
        {
            public bool IsEnable;
            public float HeightRatio;
            public Color Color;
            public static Parameter Default() => new Parameter
            {
                Color = UnityEngine.Color.black
            };
        }

        public LetterBoxPass() : base(0xb4, "LetterBoxPass_RT", "LetterBoxPass")
        {
        }

        protected override bool IsEnabled(PostImageEffectFeature.Parameter p) => p.LetterBox.IsEnable;
        protected override RenderPassEvent Event(PostImageEffectFeature.Parameter p) => (RenderPassEvent)600;
        public override NativeEffectUniform[] GetUniforms(PostImageEffectFeature.Parameter p) => new[]
        {
            NativeEffectUniform.F(0x1e5, p.LetterBox.HeightRatio, "material"),
            NativeEffectUniform.V(0x1e6, p.LetterBox.Color, "materialColor")
        };
    }

    public sealed class LensDistortionPass : NativeSingleBlitPass
    {
        public struct Parameter
        {
            public float Intensity, IntensityX, IntensityY, CenterX, CenterY, Scale;
            public RenderPassEvent RenderPassEvent;
            public static Parameter Default() => new Parameter
            {
                IntensityX = 1,
                IntensityY = 1,
                Scale = 1,
                RenderPassEvent = (RenderPassEvent)550
            };
        }

        public static bool? OriginalGlobalEnabled { get; set; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static float F(float v) => BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(v));
        public LensDistortionPass() : base(0xa8, "LensDistortionPass_RT0", "LensDistortionPass")
        {
        }

        public static bool LocalValidity(Parameter p) => !(p.Intensity >= -.000001f && p.Intensity <= .000001f) && (p.IntensityX > 0 || p.IntensityY > 0);
        protected override bool IsEnabled(PostImageEffectFeature.Parameter p)
        {
            if (!LocalValidity(p.LensDistortion))
                return false;
            return OriginalGlobalEnabled ?? Gallop.GraphicSettings.IsLensDistortion;
        }

        protected override RenderPassEvent Event(PostImageEffectFeature.Parameter p) => p.LensDistortion.RenderPassEvent;
        public static NativeEffectUniform[] BuildUniforms(Parameter p, Func<float, float> tan)
        {
            float value = Mathf.Abs(p.Intensity);
            if (!(value > 1f))
                value = 1f;
            float theta = F(value * 1.6f);
            if (theta > 160f)
                theta = 160f;
            theta = F(theta * .01745329238474369f);
            float sigma = F(tan(F(theta * .5f)) * 2f);
            float x = p.IntensityX > .0001f ? p.IntensityX : .0001f, y = p.IntensityY > .0001f ? p.IntensityY : .0001f;
            if (!(p.Intensity >= 0f))
                theta = F(1f / theta);
            return new[]
            {
                NativeEffectUniform.V(0x105, new Vector4(p.CenterX, p.CenterY, x, y)),
                NativeEffectUniform.V(0x106, new Vector4(theta, sigma, F(1f / p.Scale), p.Intensity))
            };
        }

        public override NativeEffectUniform[] GetUniforms(PostImageEffectFeature.Parameter p) => BuildUniforms(p.LensDistortion, Mathf.Tan);
    }
}
