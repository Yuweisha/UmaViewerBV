using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gallop.RenderPipeline
{
    // Original scene-dependent consumers. The stream includes the intermediate
    // context submits: material state must not be deferred across those boundaries.
    public abstract class NativeScenePostPass : ScriptableRenderPass, IDisposable
    {
        protected CameraData camera;
        protected PostImageEffectFeature feature;
        protected readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        public static Action<string, IReadOnlyList<NativePostCommand>> CommandObserver { get; set; }
        public static Action<Camera, RenderTargetIdentifier, RenderTargetIdentifier> HostTargetBridgeObserver { get; set; }
        protected abstract string PoolName { get; }

        protected bool Material(string role, int kind)
        {
            if (materials.TryGetValue(role, out var m) && m != null)
                return true;
            var s = ShaderManager.GetShader(HostShaderKind(kind));
            if (s == null)
                return false;
            materials[role] = new Material(s);
            return true;
        }

        // The restored DLL contains enum insertions absent from the older
        // viewer's tables. Translate at the boundary, retaining DLL IDs in traces.
        public static ShaderManager.ShaderKinds HostShaderKind(int n) => n == 0x179 ? ShaderManager.ShaderKinds.EyeHiglightOnly : (ShaderManager.ShaderKinds)n;
        public static int PropertyId(int n) => ShaderManager.GetPropertyId((ShaderManager.PropertyId)(n >= 0x1f8 && n <= 0x20f ? n - 3 : n));
        protected static int Id(int n) => PropertyId(n);
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        protected static float F(float v) => BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(v));
        protected static void Alloc(List<NativePostCommand> c, string name, int w, int h, int filter = 1, int format = 0)
        {
            c.Add(new NativePostCommand { Call = "makeRT", Target = name, Width = w, Height = h });
            c.Add(new NativePostCommand { Call = "allocateRT", Target = name, Width = w, Height = h, Value = filter, Format = format });
        }

        protected static void Float(List<NativePostCommand> c, int n, float v, string role = null) => c.Add(new NativePostCommand { Call = "float", Property = n, Scalar = v, MaterialRole = role });
        protected static void Vector(List<NativePostCommand> c, int n, Vector4 v, string role = null) => c.Add(new NativePostCommand { Call = "vector", Property = n, Vector = v, MaterialRole = role });
        protected static void Texture(List<NativePostCommand> c, int n, Texture v, string role = null) => c.Add(new NativePostCommand { Call = "texture", Property = n, Texture = v, MaterialRole = role });
        protected static void Int(List<NativePostCommand> c, int n, int v, string role) => c.Add(new NativePostCommand { Call = "int", Property = n, Value = v, MaterialRole = role });
        protected static void Blit(List<NativePostCommand> c, string from, string to, string role = null, int pass = -1) => c.Add(new NativePostCommand { Call = "blit", Source = from, Target = to, MaterialRole = role, PassIndex = pass });
        protected static void Release(List<NativePostCommand> c, string name) => c.Add(NativePostCommand.Op("releaseRT", name));
        protected static void Submit(List<NativePostCommand> c)
        {
            c.Add(NativePostCommand.Op("executeCommand"));
            c.Add(NativePostCommand.Op("clearCommand"));
        }

        protected static void Bind(List<NativePostCommand> c, string color, string depth) => c.Add(new NativePostCommand { Call = "bind", Target = color, Depth = depth });
        protected static void Clear(List<NativePostCommand> c, bool depth, Color color) => c.Add(new NativePostCommand { Call = "clear", ClearDepth = depth, ClearColor = true, Vector = color });
        protected static void Draw(List<NativePostCommand> c, Renderer r, string role, int pass) => c.Add(new NativePostCommand { Call = "drawRenderer", Renderer = r, MaterialRole = role, PassIndex = pass });
        protected static void Matrices(List<NativePostCommand> c, Camera cam) => c.Add(new NativePostCommand { Call = "matrices", View = cam.worldToCameraMatrix, Projection = cam.projectionMatrix });
        protected static void TargetTexture(List<NativePostCommand> c, int n, string target) => c.Add(new NativePostCommand { Call = "targetTexture", Property = n, Target = target });
        protected virtual void Service(NativePostCommand c, ScriptableRenderContext context, ref RenderingData data) => throw new InvalidOperationException("Original scene service missing: " + c.Call);
        protected virtual void PrepareExecute()
        {
        }

        protected virtual void BindExternal(Dictionary<string, RenderTargetIdentifier> rt, ref RenderingData data)
        {
        }

        protected virtual bool BridgeLate => false;

        public abstract List<NativePostCommand> Build(PostImageEffectFeature.Parameter p, int width, int height, int sourceWidth, int sourceHeight);
        protected static RenderTexture Required(RenderTexture rt, string owner)
        {
            if (rt == null)
                throw new InvalidOperationException("Original camera resource owner is not installed: " + owner);
            return rt;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData data)
        {
            PrepareExecute();
            var cmd = CommandBufferPool.Get(PoolName);
            var source = feature.SourceRT;
            var rt = new Dictionary<string, RenderTargetIdentifier>
            {
                ["source"] = source.RtId,
                ["camera"] = new RenderTargetIdentifier(data.cameraData.camera.targetTexture),
                ["attachment"] = colorAttachment
            };
            var size = new Dictionary<string, Vector2Int>();
            try
            {
                using (var surfaces = new NativeCameraSurfaceScope(camera, ref data, context))
                {
                    BindExternal(rt, ref data);
                    var commands = Build(camera.Parameter, data.cameraData.cameraTargetDescriptor.width, data.cameraData.cameraTargetDescriptor.height, source.Width, source.Height);
                    CommandObserver?.Invoke(PoolName, commands);
                    foreach (var c in commands)
                    {
                        var m = c.MaterialRole == null ? null : materials[c.MaterialRole];
                        int id = 0;
                        if (c.Call == "float" || c.Call == "int" || c.Call == "vector" || c.Call == "color" || c.Call == "texture" || c.Call == "targetTexture")
                            id = Id(c.Property);
                        switch (c.Call)
                        {
                            case "makeRT":
                                rt[c.Target] = Shader.PropertyToID(c.Target);
                                size[c.Target] = new Vector2Int(c.Width, c.Height);
                                break;
                            case "allocateRT":
                                var s = size[c.Target];
                                if (c.Format != 0)
                                    cmd.GetTemporaryRT(Shader.PropertyToID(c.Target), s.x, s.y, 0, (FilterMode)c.Value, (GraphicsFormat)c.Format);
                                else
                                    cmd.GetTemporaryRT(Shader.PropertyToID(c.Target), s.x, s.y, 0, (FilterMode)c.Value);
                                break;
                            case "releaseRT":
                                cmd.ReleaseTemporaryRT(Shader.PropertyToID(c.Target));
                                break;
                            case "float":
                                if (m == null)
                                    cmd.SetGlobalFloat(id, c.Scalar);
                                else
                                    m.SetFloat(id, c.Scalar);
                                break;
                            case "int":
                                m.SetInt(id, c.Value);
                                break;
                            case "vector":
                                if (m == null)
                                    cmd.SetGlobalVector(id, c.Vector);
                                else
                                    m.SetVector(id, c.Vector);
                                break;
                            case "color":
                                if (m == null)
                                    cmd.SetGlobalColor(id, c.Vector);
                                else
                                    m.SetColor(id, c.Vector);
                                break;
                            case "texture":
                                if (m == null)
                                    cmd.SetGlobalTexture(id, c.Texture);
                                else
                                    m.SetTexture(id, c.Texture);
                                break;
                            case "targetTexture":
                                cmd.SetGlobalTexture(id, rt[c.Target]);
                                break;
                            case "blit":
                                if (m == null)
                                    cmd.Blit(rt[c.Source], rt[c.Target]);
                                else
                                    cmd.Blit(rt[c.Source], rt[c.Target], m, c.PassIndex);
                                break;
                            case "copyTexture":
                                cmd.CopyTexture(rt[c.Source], rt[c.Target]);
                                break;
                            case "bind":
                                cmd.SetRenderTarget(rt[c.Target], rt[c.Depth]);
                                break;
                            case "clear":
                                cmd.ClearRenderTarget(c.ClearDepth, c.ClearColor, c.Vector);
                                break;
                            case "matrices":
                                cmd.SetViewProjectionMatrices(c.View, c.Projection);
                                break;
                            case "gpuMatrices":
                                RenderingUtils.SetViewAndProjectionMatrices(cmd, c.View, c.Projection, true);
                                break;
                            case "cameraPosition":
                                cmd.SetGlobalVector("_WorldSpaceCameraPos", c.Vector);
                                break;
                            case "screenParams":
                                cmd.SetGlobalVector("_ScreenParams", c.Vector);
                                break;
                            case "maskTexture":
                                cmd.SetGlobalTexture("_TransmittedMaskTex", rt[c.Target]);
                                break;
                            case "drawRenderer":
                                cmd.DrawRenderer(c.Renderer, m, c.Value, c.PassIndex);
                                break;
                            case "applyEyeMaterial":
                                c.MaterialApplier(m);
                                break;
                            case "resolveAA":
                                cmd.ResolveAntiAliasedSurface(camera.Parameter.TargetCamera.GetComponent<CameraData>().ColorTextureMSAA, camera.Parameter.TargetCamera.targetTexture);
                                break;
                            case "executeCommand":
                                context.ExecuteCommandBuffer(cmd);
                                break;
                            case "clearCommand":
                                cmd.Clear();
                                break;
                            case "handoff":
                                var d = size[c.Target];
                                feature.SetSourceRT(RenderTextureHandle.Make(Shader.PropertyToID(c.Target), d.x, d.y), cmd, ref data);
                                break;
                            case "attachmentResolve":
                                if (source.RtId != colorAttachment)
                                    cmd.Blit(source.RtId, colorAttachment);
                                break;
                            case "auraSourceBridge":
                                // Native initial SourceRT and targetTexture refer to
                                // the same camera surface. URP14 inserts a host color
                                // attachment; preserve that alias only for the initial
                                // external source, never for an earlier effect's RT.
                                if (source.NameId == -2 && source.RtId == data.cameraData.renderer.cameraColorTargetHandle.nameID && rt["camera"] != source.RtId)
                                {
                                    cmd.Blit(rt["camera"], source.RtId);
                                    HostTargetBridgeObserver?.Invoke(data.cameraData.camera, rt["camera"], source.RtId);
                                }

                                break;
                            default:
                                Service(c, context, ref data);
                                break;
                        }
                    }

                    if (BridgeLate && (int)renderPassEvent > 550)
                    {
                        var original = new RenderTargetIdentifier(Required(data.cameraData.camera.targetTexture, PoolName + " targetTexture"));
                        var host = data.cameraData.renderer.cameraColorTargetHandle.nameID;
                        if (original != host)
                        {
                            cmd.Blit(original, host);
                            HostTargetBridgeObserver?.Invoke(data.cameraData.camera, original, host);
                        }
                    }

                    context.ExecuteCommandBuffer(cmd);
                }
            }
            finally
            {
                CommandBufferPool.Release(cmd);
            }
        }

        public virtual void Dispose()
        {
            foreach (var m in materials.Values)
                CoreUtils.Destroy(m);
            materials.Clear();
            camera = null;
            feature = null;
        }
    }

    public sealed class RainSplashPass : NativeScenePostPass
    {
        public sealed class EdgeStyleRendererInfo
        {
            public bool IsVisible = true;
            public List<Renderer> RendererList = new List<Renderer>();
        }

        public struct Parameter
        {
            public Camera TargetCamera;
            public CameraData TargetCameraData;
            public bool IsEnable;
            public RenderPassEvent RenderPassEvent;
            public int DownscaleFactor;
            public Vector2 OffsetDirectionBase;
            public float OffsetScaleBase;
            public Vector2 OffsetDirectionExpand;
            public float OffsetScaleExpand, MinDepthValue, MaxDepthValue;
            public Texture2D MistNoiseTexture;
            public Color MistColor;
            public float MistIntensity, MistDensity;
            public Vector2 MistScrollSpeed;
            public float MistBlurSize, MistPower;
            public Texture2D SplashNoiseTexture;
            public Vector2 SplashScrollSpeed;
            public Color SplashColor;
            public float SplashThreshold, SplashBaseRatio, SplashDensity, SplashIntensity, SplashPower;
            public bool IsSplashOuter, IsSplashInner, IsUseRendererList;
            public Dictionary<int, EdgeStyleRendererInfo> RendererInfoDictionary;
            public float AppTime;
            public static Parameter Default() => new Parameter
            {
                RenderPassEvent = (RenderPassEvent)550,
                DownscaleFactor = 4,
                OffsetScaleBase = 1,
                OffsetScaleExpand = 1,
                MaxDepthValue = BitConverter.Int32BitsToSingle(0x3f7ff972),
                MistColor = Color.white,
                MistIntensity = 1,
                MistDensity = 1,
                MistBlurSize = 1,
                MistPower = 1,
                SplashColor = Color.white,
                SplashThreshold = .005f,
                SplashBaseRatio = .001f,
                SplashDensity = 300,
                SplashIntensity = 1,
                SplashPower = .25f
            };
        }

        const string RT0 = "RainSplashPass_RT0", RT1 = "RainSplashPass_RT1", RT2 = "RainSplashPass_RT2";
        protected override string PoolName => "RainSplashPass";
        protected override bool BridgeLate => true;

        public bool Setup(CameraData c, PostImageEffectFeature f)
        {
            camera = null;
            feature = null;
            if (c == null)
                throw new NullReferenceException();
            var a = c.Parameter.RainSplash;
            if (!GraphicSettings.IsRainSplash || !a.IsEnable || !Material("rain", 0xbe))
                return false;
            renderPassEvent = a.RenderPassEvent;
            camera = c;
            feature = f;
            return true;
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor d)
        {
            cmd.GetTemporaryRT(Shader.PropertyToID(RT0), d.width, d.height, 0);
            ConfigureTarget(new RenderTargetIdentifier(Shader.PropertyToID(RT0)));
        }

        protected override void BindExternal(Dictionary<string, RenderTargetIdentifier> rt, ref RenderingData data)
        {
            rt[RT0] = Shader.PropertyToID(RT0);
            var a = camera.Parameter.RainSplash;
            if (a.IsUseRendererList)
            {
                var d = a.TargetCameraData;
                if (d == null)
                    throw new NullReferenceException();
                rt["rainDepth"] = Required(d.IsMSAA ? d.ColorTextureMSAA : d.DepthTexture, "RainSplash depth");
            }
        }

        public static Vector2Int ReducedSize(int w, int h, int down)
        {
            float ratio = (float)h / w;
            if (w <= 1080)
            {
                if (h > 1920)
                {
                    h = 1920;
                    w = Mathf.RoundToInt(1920f / ratio);
                }
            }
            else
            {
                w = 1080;
                h = Mathf.RoundToInt(ratio * 1080f);
            }

            return new Vector2Int(w / down, h / down);
        }

        public override List<NativePostCommand> Build(PostImageEffectFeature.Parameter p, int width, int height, int sw, int sh)
        {
            var a = p.RainSplash;
            var c = new List<NativePostCommand>();
            var size = ReducedSize(width, height, a.DownscaleFactor);
            // RT0 is allocated by Configure; retained through handoff at <=550.
            c.Add(new NativePostCommand { Call = "makeRT", Target = RT0, Width = width, Height = height });
            Alloc(c, RT1, size.x, size.y);
            Alloc(c, RT2, size.x, size.y);
            float q = (float)width / 1080f, inv = 1f / q, r = ((float)height / width) / .5625f * q;
            Vector(c, 0x1fa, a.OffsetDirectionBase, "rain");
            Float(c, 0x1fb, a.OffsetScaleBase, "rain");
            Vector(c, 0x1f8, a.OffsetDirectionExpand, "rain");
            Float(c, 0x1f9, a.OffsetScaleExpand, "rain");
            Float(c, 0x1fc, a.MinDepthValue, "rain");
            Float(c, 0x1fd, a.MaxDepthValue, "rain");
            Texture(c, 0x1fe, a.MistNoiseTexture, "rain");
            Vector(c, 0x1ff, a.MistColor, "rain");
            Float(c, 0x200, a.MistIntensity, "rain");
            Float(c, 0x201, a.MistDensity * inv, "rain");
            Vector(c, 0x202, a.MistScrollSpeed, "rain");
            Float(c, 0x203, a.MistBlurSize, "rain");
            Float(c, 0x204, a.MistPower * inv, "rain");
            Texture(c, 0x205, a.SplashNoiseTexture, "rain");
            Vector(c, 0x207, a.SplashColor, "rain");
            Vector(c, 0x206, a.SplashScrollSpeed, "rain");
            Float(c, 0x208, a.SplashThreshold, "rain");
            Float(c, 0x209, a.SplashBaseRatio, "rain");
            Float(c, 0x20a, a.SplashIntensity, "rain");
            Float(c, 0x20b, a.SplashDensity * inv, "rain");
            Float(c, 0x20c, a.SplashPower * inv, "rain");
            Int(c, 0x20d, a.IsSplashOuter ? 1 : 0, "rain");
            Int(c, 0x20e, a.IsSplashInner ? 1 : 0, "rain");
            Vector(c, 0x20f, new Vector4(q, r, 0, 0), "rain");
            Float(c, 0x44, a.AppTime, "rain");
            if (a.IsUseRendererList)
            {
                Matrices(c, a.TargetCamera);
                Bind(c, RT0, "rainDepth");
                Clear(c, true, Color.clear);
                foreach (var info in a.RendererInfoDictionary.Values)
                    if (info.IsVisible)
                        foreach (var renderer in info.RendererList)
                            Draw(c, renderer, "rain", 5);
            }
            else
                Blit(c, "source", RT1, "rain", 0);
            Blit(c, a.IsUseRendererList ? RT0 : RT1, RT2, "rain", 1);
            Blit(c, RT2, RT1, "rain", 2);
            Blit(c, RT1, RT2, "rain", 3);
            TargetTexture(c, 0xa3, RT2);
            Blit(c, "source", RT0, "rain", 4);
            if ((camera == null ? (int)a.RenderPassEvent : (int)renderPassEvent) <= 550)
                c.Add(NativePostCommand.Op("handoff", RT0));
            else
            {
                Blit(c, RT0, "camera");
                Release(c, RT0);
            }

            Release(c, RT1);
            Release(c, RT2);
            return c;
        }
    }

    public sealed class AuraPass : NativeScenePostPass
    {
        public enum AuraMode
        {
            Normal = 0,
            EmissiveOnly = 1,
            EyeHighlight = 2
        }

        public sealed class AuraParam
        {
            public bool IsEnable;
            public List<Renderer> RendererList;
            public AuraMode CurrentAuraMode;
            public Camera CharacterCamera;
            public float Fresnel = .055f;
            public Vector4 LuminousParam = new Vector4(.1f, 5, 2, 0), DistortionParam = new Vector4(0, .1f, .2f, .15f);
            public Color AuraColor = new Color(0, 0, 1, 1);
            public Vector3 OffsetViewDir;
            public float AuraWidth = 1;
            public Texture EyeHighlightTexture0, EyeHighlightTexture1, EyeHighlightTexture2;
            public Action<Material> EyeHighlightMaterialApplier;
            public int StencilId;
            public bool IsEdgeOnly;
            public bool IsVisible()
            {
                if (!IsEnable || RendererList == null || RendererList.Count == 0)
                    return false;
                foreach (var r in RendererList)
                    if (r.isVisible)
                        return true;
                return false;
            }
        }

        public struct Parameter
        {
            public Dictionary<int, AuraParam> AuraParamDic;
            public RenderPassEvent RenderPassEvent;
            public static Parameter Default() => new Parameter
            {
                AuraParamDic = new Dictionary<int, AuraParam>(),
                RenderPassEvent = (RenderPassEvent)550
            };
            public bool IsValidity
            {
                get
                {
                    if (AuraParamDic == null || AuraParamDic.Count == 0)
                        return false;
                    foreach (var a in AuraParamDic.Values)
                        if (a.IsEnable)
                            return true;
                    return false;
                }
            }

            public bool IsEyeHighlight
            {
                get
                {
                    if (!IsValidity)
                        return false;
                    foreach (var a in AuraParamDic.Values)
                        if (a.CurrentAuraMode == AuraMode.EyeHighlight)
                            return true;
                    return false;
                }
            }
        }

        public static Func<string, int, Texture2D> OriginalTextureLoader { get; set; }

        const string RT0 = "AuraPass_RT0", RT1 = "AuraPass_RT1", Down = "Aura.Bloom.downSampleTex", Vertical = "Aura.Bloom.tmpTexBlurV";
        protected override string PoolName => "AuraPass";

        public bool Setup(CameraData c, PostImageEffectFeature f)
        {
            camera = null;
            feature = null;
            if (c == null)
                throw new NullReferenceException();
            if (!c.Parameter.Aura.IsValidity)
                return false;
            bool existing = materials.TryGetValue("aura", out var primary) && primary != null;
            if (!Material("aura", 0xb0))
                return false;
            if (!existing)
            {
                if (OriginalTextureLoader == null)
                    throw new InvalidOperationException("Original Aura ResourceManager noise owner is not installed.");
                var noise = OriginalTextureLoader("ImageEffect/Fluctuation/noise", 0x10002);
                if (noise == null)
                    return false;
                materials["aura"].SetTexture(Id(0xe4), noise);
            }

            if (!Material("chara", 0x98) || !Material("bloom", 0xa3) || !Material("eye", 0x179))
                return false;
            if (!c.Parameter.TargetCamera.isActiveAndEnabled)
                return false;
            renderPassEvent = c.Parameter.Aura.RenderPassEvent;
            camera = c;
            feature = f;
            return true;
        }

        protected override void PrepareExecute()
        {
            foreach (var entry in new[]
            {
                ("aura", 0xb0),
                ("chara", 0x98),
                ("eye", 0x179),
                ("bloom", 0xa3)
            }

            )
            {
                var s = ShaderManager.GetShader(HostShaderKind(entry.Item2));
                var m = materials[entry.Item1];
                if (m == null || m.shader == null)
                    throw new NullReferenceException();
                if (m.shader != s)
                    m.shader = s;
            }
        }

        protected override void BindExternal(Dictionary<string, RenderTargetIdentifier> rt, ref RenderingData data)
        {
            var cam = camera.Parameter.TargetCamera;
            var d = cam.GetComponent<CameraData>();
            if (d == null)
                throw new NullReferenceException();
            rt["auraColor"] = Required(d.IsMSAA ? d.ColorTextureMSAA : cam.targetTexture, "Aura color");
            rt["auraDepth"] = Required(d.IsMSAA ? d.ColorTextureMSAA : d.DepthTexture, "Aura depth");
        }

        static void Stencil(List<NativePostCommand> c, string role, int id, int comp)
        {
            Int(c, 0x50, id, role);
            Int(c, 0x51, comp, role);
            Int(c, 0x52, 0, role);
        }

        public static Vector3 ViewDirection(Vector3 offset)
        {
            float x = F(offset.x + 0f), y = F(offset.y + 0f), z = F(offset.z + 1f);
            float length = F((float)System.Math.Sqrt(F(F(F(y * y) + F(x * x)) + F(z * z))));
            return length > 1e-5f ? new Vector3(F(x / length), F(y / length), F(z / length)) : Vector3.zero;
        }

        static void Resolve(List<NativePostCommand> c, CameraData d)
        {
            if (!d.IsMSAA)
                return;
            c.Add(new NativePostCommand { Call = d.ColorTextureMSAA.descriptor.bindMS ? "resolveAA" : "blit", Source = "auraColor", Target = "camera" });
        }

        public override List<NativePostCommand> Build(PostImageEffectFeature.Parameter p, int width, int height, int sw, int sh) => BuildCommands(p, width, height, sw, sh, a => a.IsVisible());
        public List<NativePostCommand> BuildCommands(PostImageEffectFeature.Parameter p, int width, int height, int sw, int sh, Func<AuraParam, bool> visible)
        {
            var c = new List<NativePostCommand>();
            var cam = p.TargetCamera;
            var d = cam.GetComponent<CameraData>();
            if (d == null)
                throw new NullReferenceException();
            Alloc(c, RT0, sw, sh);
            Alloc(c, RT1, sw / 2, sw / 2);
            foreach (var a in p.Aura.AuraParamDic.Values)
            {
                if (!visible(a))
                    continue;
                Blit(c, "source", RT0);
                Float(c, 0x17b, a.Fresnel);
                Vector(c, 0x17c, ViewDirection(a.OffsetViewDir));
                Float(c, 0x1c2, a.AuraWidth);
                Matrices(c, cam);
                Bind(c, "auraColor", d.IsMSAA ? "auraColor" : "auraDepth");
                Clear(c, false, new Color(.5f, .5f, .5f, 0));
                foreach (var renderer in a.RendererList)
                {
                    switch (a.CurrentAuraMode)
                    {
                        case AuraMode.Normal:
                            Stencil(c, "chara", Mathf.Max(a.StencilId, 0), a.StencilId > 0 ? 3 : 8);
                            Draw(c, renderer, "chara", 0);
                            break;
                        case AuraMode.EmissiveOnly:
                            Stencil(c, "chara", 170, a.IsEdgeOnly ? 6 : 8);
                            Draw(c, renderer, "chara", 1);
                            break;
                        case AuraMode.EyeHighlight:
                            Stencil(c, "eye", Mathf.Max(a.StencilId, 0), a.StencilId > 0 ? 3 : 8);
                            Texture(c, 0x142, a.EyeHighlightTexture0, "eye");
                            Texture(c, 0x143, a.EyeHighlightTexture1, "eye");
                            Texture(c, 0x144, a.EyeHighlightTexture2, "eye");
                            if (a.EyeHighlightMaterialApplier != null)
                                c.Add(new NativePostCommand { Call = "applyEyeMaterial", MaterialRole = "eye", MaterialApplier = a.EyeHighlightMaterialApplier });
                            Draw(c, renderer, "eye", 0);
                            c[c.Count - 1].Value = 1;
                            break;
                    }
                }

                Resolve(c, d);
                c.Add(NativePostCommand.Op("auraSourceBridge"));
                if (a.CurrentAuraMode == AuraMode.Normal)
                {
                    Vector(c, 0xb8, a.LuminousParam);
                    Blit(c, "source", RT1, "aura", 0);
                }
                else if (a.CurrentAuraMode == AuraMode.EmissiveOnly || a.CurrentAuraMode == AuraMode.EyeHighlight)
                {
                    float divider = a.CurrentAuraMode == AuraMode.EyeHighlight ? .5f : .25f;
                    int w = (int)(sw * divider), h = (int)(sh * divider);
                    Alloc(c, Down, w, h);
                    Blit(c, "source", Down, "bloom", 1);
                    float spread = a.LuminousParam.z;
                    Vector(c, 0xb8, new Vector4((spread / ((float)sw / sh)) * .001953125f, spread * .001953125f, a.LuminousParam.x, a.LuminousParam.y), "bloom");
                    Alloc(c, Vertical, w, h);
                    Blit(c, Down, Vertical, "bloom", 2);
                    Blit(c, Vertical, RT1, "bloom", 3);
                    Release(c, Vertical);
                    Release(c, Down);
                    Submit(c);
                }

                TargetTexture(c, 0xe3, RT1);
                Vector(c, 0x4f, a.DistortionParam);
                c.Add(new NativePostCommand { Call = "color", Property = 0x4b, Vector = a.AuraColor });
                Blit(c, RT0, "auraColor", "aura", 1);
                Resolve(c, d);
                c.Add(NativePostCommand.Op("auraSourceBridge"));
                Submit(c);
            }

            Release(c, RT1);
            Release(c, RT0);
            if ((int)renderPassEvent > 550)
                c.Add(NativePostCommand.Op("attachmentResolve"));
            return c;
        }
    }

    public sealed class TransmittedLightPass : NativeScenePostPass
    {
        public struct Parameter
        {
            public Camera TargetCamera;
            public CameraData TargetCameraData;
            public bool IsEnable;
            public int Iterations;
            public float Intensity, Threshold, BlurSpread;
            public int BlendMode;
            public static Parameter Default() => new Parameter
            {
                Iterations = 1,
                Intensity = 1,
                BlendMode = 1
            };
        }

        const string Mask = "_TransmittedMaskTex", Dest = "TransmittedLight.destTexture", Temp = "TransmittedLight.tempTexture";
        protected override string PoolName => "TransmittedLightPass";

        public TransmittedLightPass()
        {
            renderPassEvent = (RenderPassEvent)550;
        }

        public bool Setup(CameraData c, PostImageEffectFeature f)
        {
            if (c == null)
                throw new NullReferenceException();
            // Unlike Aura/Rain the native gate preserves the previous binding.
            if (!GraphicSettings.IsTransmittedLight || !c.Parameter.TransmittedLight.IsEnable)
                return false;
            camera = null;
            feature = null;
            if (!Material("bloom", 0xa3))
                return false;
            camera = c;
            feature = f;
            return true;
        }

        static string Down(int i) => "TransmittedLight.Bloom.downSampleTex" + i;
        static string Vertical(int i) => "TransmittedLight.Bloom.tmpTexBlurV" + i;
        static string Horizontal(int i) => "TransmittedLight.Bloom.tmpTexBlurH" + i;
        protected override void BindExternal(Dictionary<string, RenderTargetIdentifier> rt, ref RenderingData data)
        {
            // A global texture binding does not create a temporary RT with that
            // property's ID in URP14. Resolve the original depth surface through
            // its camera owner rather than binding a nonexistent temporary ID.
            var owner = camera.Parameter.TransmittedLight.TargetCameraData;
            if (owner == null)
                throw new NullReferenceException();
            // The native mask binds the _CameraDepthTexture shader resource,
            // not CameraData.DepthTexture (the geometry depth/stencil attachment
            // used by Aura/Rain). Its R8 mask has one sample even when geometry
            // uses MSAA, so it needs the renderer's single-sample depth prepass.
            var depth = data.cameraData.renderer is GallopRenderer gallop ? gallop.SampledDepthSurface : owner.DepthTexture;
            rt["depthTexture"] = Required(depth, "TransmittedLight _CameraDepthTexture");
            rt["camera"] = new RenderTargetIdentifier(camera.Parameter.TargetCamera.targetTexture);
        }

        protected override void Service(NativePostCommand c, ScriptableRenderContext context, ref RenderingData data)
        {
            var cam = camera.Parameter.TargetCamera;
            if (c.Call != "drawTransmittedMask")
                throw new InvalidOperationException(c.Call);
            if (!cam.TryGetCullingParameters(out var cp))
                return;
            cp.cullingOptions &= ~(CullingOptions)10;
            var culled = context.Cull(ref cp);
            var local = data;
            local.cameraData.camera = cam;
            var drawing = CreateDrawingSettings(new ShaderTagId("TransmittedLightMask"), ref local, (SortingCriteria)0x3b);
            var owner = camera.Parameter.TransmittedLight.TargetCameraData;
            if (owner == null)
                throw new NullReferenceException();
            var filtering = new FilteringSettings(RenderQueueRange.opaque, cam.cullingMask, owner.RenderingLayerMask);
            context.DrawRenderers(culled, ref drawing, ref filtering);
            drawing.sortingSettings = new SortingSettings(cam)
            {
                criteria = (SortingCriteria)0x17
            };
            filtering.renderQueueRange = RenderQueueRange.transparent;
            context.DrawRenderers(culled, ref drawing, ref filtering);
        }

        public override List<NativePostCommand> Build(PostImageEffectFeature.Parameter p, int width, int height, int sw, int sh)
        {
            var a = p.TransmittedLight;
            var cam = p.TargetCamera;
            var c = new List<NativePostCommand>();
            Alloc(c, Mask, sw, sh, 1, 5);
            Bind(c, Mask, "depthTexture");
            Clear(c, false, Color.black);
            var pos = cam.transform.position;
            Vector(c, 0, new Vector4(pos.x, pos.y, pos.z, 0)); // camera globals use explicit Unity names below
            c[c.Count - 1].Call = "cameraPosition";
            var rect = cam.pixelRect;
            c.Add(new NativePostCommand { Call = "screenParams", Vector = new Vector4(rect.width, rect.height, 1 + 1 / rect.width, 1 + 1 / rect.height) });
            c.Add(new NativePostCommand { Call = "gpuMatrices", View = cam.worldToCameraMatrix, Projection = GL.GetGPUProjectionMatrix(cam.projectionMatrix, true) });
            Submit(c);
            c.Add(NativePostCommand.Op("drawTransmittedMask"));
            c.Add(new NativePostCommand { Call = "maskTexture", Target = Mask });
            Bind(c, "camera", "camera");
            Submit(c);
            Alloc(c, Dest, sw, sh, 0);
            int n = Mathf.Clamp(a.Iterations, 1, 5);
            Vector(c, 0xb8, new Vector4(0, 0, 0, 1), "bloom");
            var sizes = new Vector2Int[n];
            for (int i = 0; i < n; i++)
            {
                int div = 1 << (i + 2);
                sizes[i] = new Vector2Int(sw / div, sh / div);
                Alloc(c, Down(i), sizes[i].x, sizes[i].y);
                Blit(c, i == 0 ? "source" : Down(i - 1), Down(i), "bloom", 4);
            }

            for (int i = 0; i < n; i++)
            {
                Vector(c, 0xb8, new Vector4((a.BlurSpread / ((float)sw / sh)) * .001953125f, a.BlurSpread * .001953125f, a.Threshold, a.Intensity), "bloom");
                Alloc(c, Vertical(i), sizes[i].x, sizes[i].y);
                Blit(c, Down(i), Vertical(i), "bloom", 2);
                Alloc(c, Horizontal(i), sizes[i].x, sizes[i].y);
                Blit(c, Vertical(i), Horizontal(i), "bloom", 3);
                Release(c, Vertical(i));
                Submit(c);
                Release(c, Down(i));
            }

            if (n > 1)
                Float(c, 0xe1, 1, "bloom");
            for (int i = n - 1; i > 0; i--)
            {
                TargetTexture(c, 0xb7, Horizontal(i));
                Alloc(c, Temp, sizes[i - 1].x, sizes[i - 1].y, 0);
                Blit(c, Horizontal(i - 1), Temp, "bloom", 0);
                c.Add(new NativePostCommand { Call = "copyTexture", Source = Temp, Target = Horizontal(i - 1) });
                Release(c, Temp);
            }

            Int(c, 0xe1, a.BlendMode, "bloom");
            TargetTexture(c, 0xb7, Horizontal(0));
            Blit(c, "source", Dest, "bloom", 0);
            Blit(c, Dest, "source");
            Release(c, Dest);
            for (int i = 0; i < n; i++)
                Release(c, Horizontal(i));
            Release(c, Mask);
            return c;
        }
    }
}
