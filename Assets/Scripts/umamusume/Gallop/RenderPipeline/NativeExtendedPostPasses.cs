using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gallop.RenderPipeline
{
    // A replayable command stream, shared by the Unity backend and native-body
    // comparisons. Identifiers describe original RT roles, not replacement assets.
    public sealed class NativePostCommand
    {
        public string Call, Source, Target, Scope;
        public int Property, Width, Height, PassIndex = -1, Value;
        public float Scalar;
        public Vector4 Vector;
        public Texture Texture;
        public bool Material;
        // Scene consumers retain renderer/material identity and color/depth pairs.
        public string MaterialRole, Depth;
        public int Format;
        public bool ClearDepth, ClearColor;
        public Renderer Renderer;
        public Matrix4x4 View, Projection;
        public Action<Material> MaterialApplier;
        public static NativePostCommand Op(string call, string target = null) => new NativePostCommand
        {
            Call = call,
            Target = target
        };
        public static void Allocate(List<NativePostCommand> c, string target, int w, int h)
        {
            c.Add(new NativePostCommand { Call = "makeRT", Target = target, Width = w, Height = h });
            c.Add(new NativePostCommand { Call = "allocateRT", Target = target, Value = 1 });
        }

        public static void Float(List<NativePostCommand> c, int id, float f, string scope = "global") => c.Add(new NativePostCommand { Call = "float", Property = id, Scalar = f, Scope = scope });
        public static void Vec(List<NativePostCommand> c, int id, Vector4 v, string scope = "global") => c.Add(new NativePostCommand { Call = "vector", Property = id, Vector = v, Scope = scope });
        public static void Tex(List<NativePostCommand> c, int id, Texture t, string scope = "global") => c.Add(new NativePostCommand { Call = "texture", Property = id, Texture = t, Scope = scope });
        public static void Blit(List<NativePostCommand> c, string from, string to, bool material = false, int pass = -1) => c.Add(new NativePostCommand { Call = "blit", Source = from, Target = to, Material = material, PassIndex = pass });
        public static void Resolve(List<NativePostCommand> c, int evt)
        {
            if (evt <= 550)
                c.Add(Op("handoff"));
            else
            {
                Blit(c, "target", "camera");
                c.Add(Op("releaseRT", "target"));
            }
        }
    }

    public abstract class NativeExtendedPostPass : ScriptableRenderPass, IDisposable
    {
        protected Material material;
        protected CameraData camera;
        protected PostImageEffectFeature feature;
        protected abstract int Kind { get; }
        protected abstract string PoolName { get; }
        protected abstract string TargetName { get; }
        protected virtual string DownsampleName => null;

        protected abstract bool Enabled(PostImageEffectFeature.Parameter p);
        protected abstract RenderPassEvent Event(PostImageEffectFeature.Parameter p);
        public abstract List<NativePostCommand> Build(PostImageEffectFeature.Parameter p, int width, int height);
        protected virtual bool PrepareResources() => true;
        protected virtual Shader FindShader() => ShaderManager.GetShader((ShaderManager.ShaderKinds)Kind);
        protected virtual bool RestoreShader => true;

        protected virtual RenderTargetIdentifier CameraTarget(ref RenderingData data) => data.cameraData.renderer.cameraColorTargetHandle.nameID;
        protected virtual void CompleteHostTarget(CommandBuffer cmd, ref RenderingData data)
        {
        }

        protected virtual void OriginalService(NativePostCommand command) => throw new InvalidOperationException("Original post service is not installed: " + command.Call);
        protected int NativeEvent(RenderPassEvent parameterEvent) => camera == null ? (int)parameterEvent : (int)renderPassEvent;
        public bool Setup(CameraData c, PostImageEffectFeature f)
        {
            camera = null;
            feature = null;
            if (c == null)
                throw new NullReferenceException();
            var p = c.Parameter;
            if (!Enabled(p) || !PrepareResources())
                return false;
            if (material == null)
            {
                var shader = FindShader();
                if (shader == null)
                    return false;
                material = new Material(shader);
            }

            renderPassEvent = Event(p);
            camera = c;
            feature = f;
            return true;
        }

        protected virtual Dictionary<string, int> Names() => new Dictionary<string, int>
        {
            ["target"] = Shader.PropertyToID(TargetName),
            ["downsample"] = DownsampleName == null ? 0 : Shader.PropertyToID(DownsampleName)
        };
        public static void Emit(CommandBuffer cmd, Material material, IEnumerable<NativePostCommand> commands, Dictionary<string, RenderTextureHandle> targets, Dictionary<string, int> names, Action<RenderTextureHandle> handoff, Action<CommandBuffer> submit = null, Action<NativePostCommand> originalService = null)
        {
            foreach (var c in commands)
            {
                int id = (c.Call == "float" || c.Call == "vector" || c.Call == "int" || c.Call == "texture") ? ShaderManager.GetPropertyId((ShaderManager.PropertyId)c.Property) : 0;
                switch (c.Call)
                {
                    case "makeRT":
                        targets[c.Target] = RenderTextureHandle.Make(names[c.Target], c.Width, c.Height);
                        break;
                    case "allocateRT":
                        targets[c.Target].GetTemporaryRT(cmd, (FilterMode)c.Value);
                        break;
                    case "releaseRT":
                        targets[c.Target].ReleaseTemporaryRT(cmd);
                        break;
                    case "float":
                        if (c.Scope == "global")
                            cmd.SetGlobalFloat(id, c.Scalar);
                        else
                            material.SetFloat(id, c.Scalar);
                        break;
                    case "int":
                        material.SetInt(id, c.Value);
                        break;
                    case "vector":
                        if (c.Scope == "global")
                            cmd.SetGlobalVector(id, c.Vector);
                        else if (c.Scope == "materialColor")
                            material.SetColor(id, c.Vector);
                        else
                            material.SetVector(id, c.Vector);
                        break;
                    case "texture":
                        if (c.Scope == "global")
                            cmd.SetGlobalTexture(id, c.Texture);
                        else
                            material.SetTexture(id, c.Texture);
                        break;
                    case "targetTexture":
                        cmd.SetGlobalTexture(id = ShaderManager.GetPropertyId((ShaderManager.PropertyId)c.Property), targets[c.Target].RtId);
                        break;
                    case "blit":
                        if (!c.Material)
                            cmd.Blit(targets[c.Source].RtId, targets[c.Target].RtId);
                        else if (c.PassIndex == -1)
                            cmd.Blit(targets[c.Source].RtId, targets[c.Target].RtId, material);
                        else
                            cmd.Blit(targets[c.Source].RtId, targets[c.Target].RtId, material, c.PassIndex);
                        break;
                    case "handoff":
                        handoff(targets["target"]);
                        break;
                    case "bgResolve":
                        if (targets["source"].RtId != targets["camera"].RtId)
                            cmd.Blit(targets["source"].RtId, targets["camera"].RtId);
                        break;
                    case "executeCommand":
                        if (submit == null)
                            throw new InvalidOperationException("Original intermediate submit is not bound.");
                        submit(cmd);
                        break;
                    case "clearCommand":
                        cmd.Clear();
                        break;
                    case "scrollReset":
                    case "scrollWrite":
                        if (originalService == null)
                            throw new InvalidOperationException("Original scroll owner is not bound.");
                        originalService(c);
                        break;
                    default:
                        throw new InvalidOperationException("Unknown native post command: " + c.Call);
                }
            }
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData data)
        {
            if (RestoreShader)
            {
                var shader = FindShader();
                if (ReferenceEquals(material, null) || ReferenceEquals(material.shader, null))
                    throw new NullReferenceException();
                if (!material.shader.Equals(shader))
                    material.shader = shader;
            }

            var p = camera.Parameter;
            var cmd = CommandBufferPool.Get(PoolName);
            var source = feature.SourceRT;
            try
            {
                var targets = new Dictionary<string, RenderTextureHandle>
                {
                    ["source"] = source,
                    ["camera"] = RenderTextureHandle.Make(CameraTarget(ref data))
                };
                // RenderingData dimensions, rather than source dimensions, are used
                // by the original TiltShift full-size target allocation.
                var commands = Build(p, data.cameraData.cameraTargetDescriptor.width, data.cameraData.cameraTargetDescriptor.height);
                RenderTextureHandle result = default;
                bool handed = false;
                Emit(cmd, material, commands, targets, Names(), t =>
                {
                    result = t;
                    handed = true;
                }, c => context.ExecuteCommandBuffer(c), OriginalService);
                if (handed)
                    feature.SetSourceRT(result, cmd, ref data);
                CompleteHostTarget(cmd, ref data);
                context.ExecuteCommandBuffer(cmd);
            }
            finally
            {
                CommandBufferPool.Release(cmd);
            }
        }

        public virtual void Dispose()
        {
            CoreUtils.Destroy(material);
            material = null;
            camera = null;
            feature = null;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        protected static float F(float f) => BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(f));
    }

    public sealed class HatchingPass : NativeExtendedPostPass
    {
        public struct Parameter
        {
            public int BlendMode;
            public RenderPassEvent RenderPassEvent;
            public float BlendAlpha;
            public Texture2D NoiseTexture;
            public float StrokeTiling, StrokeHeight, StrokeAngle, StrokeLength;
            public Vector2 CircleMaskOffset;
            public float CircleMaskSize;
            public Vector2 CircleMaskStretchRatio;
            public float CircleMaskAngle, CircleMaskInnerRatio, CircleMaskInnerEffect;
            public bool IsValidity => unchecked((uint)BlendMode) > 0;

            public static Parameter Default() => new Parameter
            {
                RenderPassEvent = (RenderPassEvent)550,
                BlendAlpha = .5f,
                StrokeTiling = 4,
                StrokeHeight = .1f,
                StrokeAngle = 60,
                StrokeLength = .1f,
                CircleMaskStretchRatio = Vector2.one,
                CircleMaskInnerRatio = .8f
            };
        }

        protected override int Kind => 0xb2;
        protected override string PoolName => "HatchingPass";
        protected override string TargetName => "HatchingPass_RT";

        protected override bool Enabled(PostImageEffectFeature.Parameter p) => p.Hatching.IsValidity;
        protected override RenderPassEvent Event(PostImageEffectFeature.Parameter p) => p.Hatching.RenderPassEvent;
        public override List<NativePostCommand> Build(PostImageEffectFeature.Parameter p, int width, int height)
        {
            var c = new List<NativePostCommand>();
            NativePostCommand.Allocate(c, "target", feature == null ? width : feature.SourceRT.Width, feature == null ? height : feature.SourceRT.Height);
            ref var a = ref p.Hatching;
            if (a.BlendMode > 5)
                a.BlendMode = 1;
            c.Add(new NativePostCommand { Call = "int", Scope = "material", Property = 0x1d7, Value = a.BlendMode });
            NativePostCommand.Float(c, 0x1d8, a.BlendAlpha, "material");
            NativePostCommand.Tex(c, 0xe3, a.NoiseTexture, "material");
            float angle = F(a.StrokeAngle * .01745329238474369f), s = Mathf.Sin(angle), cos = Mathf.Cos(angle);
            NativePostCommand.Vec(c, 0x1d9, new Vector4(cos, -s, s, cos), "material");
            NativePostCommand.Vec(c, 0x1da, new Vector4(a.StrokeTiling, a.StrokeHeight, 0, 0), "material");
            float length = F(a.StrokeLength * .1f);
            NativePostCommand.Vec(c, 0x1db, new Vector4(F(length * s), F(length * cos), 0, 0), "material");
            NativePostCommand.Vec(c, 0x1dc, new Vector4(F(a.CircleMaskOffset.x + .5f), F(a.CircleMaskOffset.y + .5f), 0, 0), "material");
            var size = OriginalScreenSize == null ? throw new InvalidOperationException("Original Screen width/height owner is not installed.") : OriginalScreenSize();
            NativePostCommand.Vec(c, 0x1dd, new Vector4(a.CircleMaskSize, F((float)size.x / (float)size.y), 0, 0), "material");
            NativePostCommand.Vec(c, 0x1de, new Vector4(a.CircleMaskStretchRatio.x, a.CircleMaskStretchRatio.y, 0, 0), "material");
            angle = F(a.CircleMaskAngle * .01745329238474369f);
            s = Mathf.Sin(angle);
            cos = Mathf.Cos(angle);
            NativePostCommand.Vec(c, 0x1df, new Vector4(cos, -s, s, cos), "material");
            NativePostCommand.Float(c, 0x1e0, a.CircleMaskInnerRatio, "material");
            NativePostCommand.Float(c, 0x1e1, a.CircleMaskInnerEffect, "material");
            NativePostCommand.Blit(c, "source", "target", true);
            NativePostCommand.Resolve(c, NativeEvent(a.RenderPassEvent));
            return c;
        }

        // Bind Gallop's virtual resolution calculation, not device pixel size.
        // A host with the original landscape UI owner can supply that branch.
        public static Func<Vector2Int> OriginalScreenSize { get; set; } = NativeVirtualScreenSize.ForCurrentDevice;
    }

    public sealed class TiltShiftPass : NativeExtendedPostPass
    {
        public struct Parameter
        {
            public bool UnlinkCutt;
            public int Mode, Quality;
            public float BlurArea, MaxBlurSize;
            public int Downsample;
            public Vector2 Offset;
            public float Roll;
            public RenderPassEvent RenderPassEvent;
            public static Parameter Default() => new Parameter
            {
                Quality = 1,
                BlurArea = 1,
                MaxBlurSize = 5,
                RenderPassEvent = (RenderPassEvent)550
            };
        }

        public static bool? OriginalGlobalEnabled { get; set; }
        protected override int Kind => 0xa6;
        protected override string PoolName => "TiltShiftPass";
        protected override string TargetName => "TiltShiftPass_RT";
        protected override string DownsampleName => "TiltShiftPass_DownsampleRT";

        protected override bool Enabled(PostImageEffectFeature.Parameter p)
        {
            return (OriginalGlobalEnabled ?? Gallop.GraphicSettings.IsTiltShift) && unchecked((uint)p.TiltShift.Mode) > 0;
        }

        protected override RenderPassEvent Event(PostImageEffectFeature.Parameter p) => p.TiltShift.RenderPassEvent;
        public override List<NativePostCommand> Build(PostImageEffectFeature.Parameter p, int width, int height)
        {
            var c = new List<NativePostCommand>();
            var a = p.TiltShift;
            NativePostCommand.Allocate(c, "target", width, height);
            NativePostCommand.Float(c, 0xa8, a.MaxBlurSize < 0 ? 0 : a.MaxBlurSize);
            NativePostCommand.Float(c, 0xa9, a.BlurArea);
            float angle = F(a.Roll * .01745329238474369f);
            NativePostCommand.Vec(c, 0xaa, new Vector4(a.Offset.x, a.Offset.y, Mathf.Sin(angle), Mathf.Cos(angle)));
            string dest = "target";
            if (a.Downsample > 0)
            {
                dest = "downsample";
                NativePostCommand.Allocate(c, dest, (feature == null ? width : feature.SourceRT.Width) >> a.Downsample, (feature == null ? height : feature.SourceRT.Height) >> a.Downsample);
            }

            NativePostCommand.Blit(c, "source", dest, true, unchecked(a.Quality * 2 + (a.Mode == 1 ? 0 : 1)));
            if (a.Downsample > 0)
            {
                c.Add(new NativePostCommand { Call = "targetTexture", Property = 0xab, Target = dest });
                NativePostCommand.Blit(c, "source", "target", true, 6);
                c.Add(NativePostCommand.Op("releaseRT", dest));
            }

            NativePostCommand.Resolve(c, NativeEvent(a.RenderPassEvent));
            return c;
        }
    }

    public sealed class FluctuationPass : NativeExtendedPostPass
    {
        public struct Parameter
        {
            public bool IsEnable;
            public Vector2 MoveDirection;
            public float MovePower;
            public Vector2 MovePosition;
            public float Power, DepthClip;
            public RenderTexture CameraDepthTexture;
            public bool DisableGraphicSettings;
            public RenderPassEvent RenderPassEvent;
            public static Parameter Default() => new Parameter
            {
                MoveDirection = Vector2.up,
                MovePower = .05f,
                Power = .01f,
                DepthClip = .3f,
                RenderPassEvent = (RenderPassEvent)550
            };
            public bool IsDepthTexture => IsEnable && (DisableGraphicSettings || RequireQuality());
        }

        const string NoiseResource = "imageeffect/fluctuation/noise";
        Texture2D noise;
        UmaDatabaseEntry noiseEntry;
        public static bool? OriginalGlobalEnabled { get; set; }
        public static Func<string, Texture2D> OriginalTextureLoader { get; set; }
        // Boundary represents original ResourceManager.HasInstance followed by
        // Unload(path, 0x10001), not destruction of the Texture2D asset itself.
        public static Action<string, int> OriginalReleaseTexture { get; set; }

        static bool RequireQuality() => OriginalGlobalEnabled ?? Gallop.GraphicSettings.IsFluctuation;
        protected override int Kind => 0xa9;
        protected override string PoolName => "FluctuationPass";
        protected override string TargetName => "FluctuationPass";

        protected override bool Enabled(PostImageEffectFeature.Parameter p)
        {
            var a = p.Fluctuation;
            if (!a.IsEnable)
                return false;
            return a.DisableGraphicSettings || RequireQuality();
        }

        protected override bool PrepareResources()
        {
            if (noise != null)
                return true;
            if (OriginalTextureLoader != null)
            {
                noise = OriginalTextureLoader("ImageEffect/Fluctuation/noise");
                return noise != null;
            }

            var main = UmaViewerMain.Instance;
            if (main == null || main.AbList == null || UmaAssetManager.instance == null || !main.AbList.TryGetValue(NoiseResource, out var entry) || entry == null)
                return false;
            // Each pass owns one ordinary bundle reference, including its
            // dependencies. Never mark this transient noise bundle NeverUnload.
            noiseEntry = entry;
            try
            {
                var bundle = UmaAssetManager.LoadAssetBundle(entry);
                if (bundle != null)
                    noise = bundle.LoadAsset<Texture2D>("assets/_gallopresources/bundle/resources/imageeffect/fluctuation/noise.png");
                return noise != null;
            }
            finally
            {
                if (noise == null)
                {
                    UmaAssetManager.UnloadAssetBundle(noiseEntry, false);
                    noiseEntry = null;
                }
            }
        }

        public override void Dispose()
        {
            base.Dispose();
            try
            {
                if (noiseEntry != null)
                    UmaAssetManager.UnloadAssetBundle(noiseEntry, false);
                else if (OriginalReleaseTexture != null)
                    OriginalReleaseTexture("ImageEffect/Fluctuation/noise", 0x10001);
                else if (noise != null)
                    throw new InvalidOperationException("Original Fluctuation resource release owner is not installed.");
            }
            finally
            {
                noise = null;
                noiseEntry = null;
            }
        }

        protected override RenderPassEvent Event(PostImageEffectFeature.Parameter p) => p.Fluctuation.RenderPassEvent;
        public override List<NativePostCommand> Build(PostImageEffectFeature.Parameter p, int width, int height)
        {
            var c = new List<NativePostCommand>();
            NativePostCommand.Allocate(c, "target", feature == null ? width : feature.SourceRT.Width, feature == null ? height : feature.SourceRT.Height);
            ref var a = ref p.Fluctuation;
            // Native fmodf preserves a negative remainder. Do not use Repeat,
            // elapsed time or delta time: this update occurs once per Execute.
            a.MovePosition = new Vector2(F(F(a.MovePower * a.MoveDirection.x) + a.MovePosition.x) % 1f, F(F(a.MovePower * a.MoveDirection.y) + a.MovePosition.y) % 1f);
            NativePostCommand.Tex(c, 0xe3, noise);
            if (a.CameraDepthTexture != null)
                NativePostCommand.Tex(c, 0xdb, a.CameraDepthTexture);
            NativePostCommand.Float(c, 0x101, a.MovePosition.x);
            NativePostCommand.Float(c, 0x102, a.MovePosition.y);
            NativePostCommand.Float(c, 0xa1, a.Power);
            NativePostCommand.Float(c, 0x107, a.DepthClip);
            NativePostCommand.Blit(c, "source", "target", true);
            NativePostCommand.Resolve(c, NativeEvent(a.RenderPassEvent));
            return c;
        }
    }

    public sealed class BgBlurPass : NativeExtendedPostPass
    {
        public struct Parameter
        {
            public bool IsEnable;
            public float BlurStrength;
            public Color Color;
            public RenderPassEvent RenderPassEvent;
            public static Parameter Default() => new Parameter
            {
                Color = DefaultBlurColor,
                RenderPassEvent = (RenderPassEvent)550
            };
        }

        // Original UICameraImageEffect .cctor 0x4c1c070 and Parameter.Default:
        // packed 0x00ffffff is white RGB with zero alpha.
        public static readonly Color DefaultBlurColor = new Color(1, 1, 1, 0);
        public static Func<Vector2> OriginalCanvasSize { get; set; }
        public static Func<string, Shader> OriginalShaderFinder { get; set; }
        protected override int Kind => -1;
        protected override string PoolName => "BgBlurPass";
        protected override string TargetName => "BgBlurPass_RT0";
        protected override bool RestoreShader => false;

        protected override Shader FindShader() => OriginalShaderFinder == null ? Shader.Find("Gallop/UI/ImageEffect") : OriginalShaderFinder("Gallop/UI/ImageEffect");
        protected override bool Enabled(PostImageEffectFeature.Parameter p) => p.BgBlur.IsEnable && !(p.BgBlur.BlurStrength <= 0);
        protected override RenderPassEvent Event(PostImageEffectFeature.Parameter p) => p.BgBlur.RenderPassEvent;
        protected override Dictionary<string, int> Names() => new Dictionary<string, int>
        {
            ["rt0"] = Shader.PropertyToID("BgBlurPass_RT0"),
            ["rt1"] = Shader.PropertyToID("BgBlurPass_RT1")
        };
        public static List<NativePostCommand> BuildCommands(Parameter a, Vector2 canvas)
        {
            var c = new List<NativePostCommand>();
            NativePostCommand.Float(c, 0x45, F(a.BlurStrength * .01f), "material");
            NativePostCommand.Vec(c, 0x4b, a.Color, "materialColor");
            int w = Mathf.RoundToInt(F((int)canvas.x * .5f)), h = Mathf.RoundToInt(F((int)canvas.y * .5f));
            NativePostCommand.Allocate(c, "rt0", w, h);
            NativePostCommand.Allocate(c, "rt1", w, h);
            NativePostCommand.Blit(c, "source", "rt0");
            NativePostCommand.Blit(c, "rt0", "rt1", true, 0);
            NativePostCommand.Blit(c, "rt1", "rt0", true, 1);
            NativePostCommand.Blit(c, "rt0", "source");
            c.Add(NativePostCommand.Op("releaseRT", "rt0"));
            c.Add(NativePostCommand.Op("releaseRT", "rt1"));
            return c;
        }

        public override List<NativePostCommand> Build(PostImageEffectFeature.Parameter p, int width, int height)
        {
            if (OriginalCanvasSize == null)
                throw new InvalidOperationException("Original UIManager canvas size owner is not installed.");
            var c = BuildCommands(p.BgBlur, OriginalCanvasSize());
            if (NativeEvent(p.BgBlur.RenderPassEvent) > 550)
                c.Add(new NativePostCommand { Call = "bgResolve" });
            return c;
        }

        public static void Execute(CommandBuffer cmd, RenderTargetIdentifier source, Vector2 canvasSize, Material blurMaterial, ref Parameter p)
        {
            var names = new Dictionary<string, int>
            {
                ["rt0"] = Shader.PropertyToID("BgBlurPass_RT0"),
                ["rt1"] = Shader.PropertyToID("BgBlurPass_RT1")
            };
            Emit(cmd, blurMaterial, BuildCommands(p, canvasSize), new Dictionary<string, RenderTextureHandle> { ["source"] = RenderTextureHandle.Make(source) }, names, t =>
            {
            });
        }
    }
}
