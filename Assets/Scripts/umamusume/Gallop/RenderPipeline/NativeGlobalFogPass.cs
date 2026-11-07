using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gallop.RenderPipeline
{
    // Original RVA 5218b60 uses an indexed quad, not CommandBuffer.Blit.
    public sealed class GlobalFogPass : ScriptableRenderPass, IDisposable
    {
        public struct Parameter
        {
            public bool IsDistanceFog, IsHeightFog, IsUseRadialDistance;
            public float FogHeight, FogHeightDensity, StartDistance;
            public Color FogColor;
            public FogMode SceneFogMode;
            public float SceneFogDensity, SceneFogStart, SceneFogEnd, CameraFovFactor;
            public Camera TargetCamera;
            public RenderPassEvent RenderPassEvent;
            public bool IsValidity => GraphicSettings.IsGlobalFog && (IsDistanceFog || IsHeightFog);
            public bool IsUseDepthTexture => IsValidity;

            public static Parameter Default() => new Parameter
            {
                FogHeight = 1,
                FogHeightDensity = 2,
                FogColor = Color.black,
                SceneFogMode = FogMode.ExponentialSquared,
                SceneFogDensity = .01f,
                SceneFogEnd = 300,
                CameraFovFactor = 1,
                RenderPassEvent = (RenderPassEvent)550
            };
        }

        public struct CameraSample
        {
            public float Near, Far, FieldOfView, Aspect;
            public Vector3 Right, Up, Forward, Position;
            public static CameraSample Read(Camera c) => new CameraSample
            {
                Near = c.nearClipPlane,
                Far = c.farClipPlane,
                FieldOfView = c.fieldOfView,
                Aspect = c.aspect,
                Right = c.transform.right,
                Up = c.transform.up,
                Forward = c.transform.forward,
                Position = c.transform.position
            };
        }

        public sealed class Command
        {
            public string Call, Target;
            public int Property, PassIndex;
            public Vector4 Vector;
            public Matrix4x4 Matrix, View;
        }

        Material material;
        Mesh quad;
        CameraData cameraData;
        PostImageEffectFeature feature;
        Camera targetCamera;
        static readonly int TargetId = Shader.PropertyToID("GlobalFogPass_RT0");
        public static Action<Camera> HostTargetBridgeObserver;
        public static Action<CameraSample, IReadOnlyList<Command>> CommandObserver;
        public bool Setup(CameraData c, PostImageEffectFeature f)
        {
            cameraData = null;
            feature = null;
            targetCamera = null;
            if (c == null)
                throw new NullReferenceException();
            var p = c.Parameter.GlobalFog;
            if (!p.IsValidity)
                return false;
            if (quad == null)
                quad = CreateOriginalQuad();
            if (material == null)
            {
                var s = ShaderManager.GetShader((ShaderManager.ShaderKinds)0xbd);
                if (s == null)
                    return false;
                material = new Material(s);
            }

            targetCamera = p.TargetCamera;
            if (targetCamera == null)
                targetCamera = c.GetComponent<Camera>();
            renderPassEvent = p.RenderPassEvent;
            if (targetCamera == null)
                throw new NullReferenceException();
            // Preserve the original transform binding at Setup, rather than
            // silently switching to the rendering camera's view for the rays.
            cameraData = c;
            feature = f;
            return true;
        }

        public static Mesh CreateOriginalQuad()
        {
            var m = new Mesh();
            m.SetVertices(new[] { new Vector3(0, 0, 3), new Vector3(1, 0, 2), new Vector3(1, 1, 1), new Vector3(0, 1, 0) });
            m.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            m.SetIndices(new[] { 0, 1, 2, 3 }, MeshTopology.Quads, 0);
            m.MarkDynamic();
            return m;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static float F(float x) => BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(x));
        static Vector3 Add(Vector3 a, Vector3 b) => new Vector3(F(a.x + b.x), F(a.y + b.y), F(a.z + b.z));
        static Vector3 Sub(Vector3 a, Vector3 b) => new Vector3(F(a.x - b.x), F(a.y - b.y), F(a.z - b.z));
        static Vector3 Mul(Vector3 a, float b) => new Vector3(F(a.x * b), F(a.y * b), F(a.z * b));
        static float Magnitude(Vector3 v) => F((float)System.Math.Sqrt(F(F(F(v.y * v.y) + F(v.x * v.x)) + F(v.z * v.z))));
        static Vector3 Normalized(Vector3 v)
        {
            float n = Magnitude(v);
            return n > 1e-5f ? new Vector3(F(v.x / n), F(v.y / n), F(v.z / n)) : Vector3.zero;
        }

        public static List<Command> Build(Parameter p, CameraSample s, int evt, Matrix4x4 view, Matrix4x4 orthoGPU, Matrix4x4 cameraGPU)
        {
            float angle = F(F(F(p.CameraFovFactor * s.FieldOfView) * .5f) * .01745329238474369f);
            float tangent = Mathf.Tan(angle), vertical = F(tangent * s.Near), horizontal = F(vertical * s.Aspect);
            var right = Mul(s.Right, horizontal);
            var up = Mul(s.Up, vertical);
            var forward = Mul(s.Forward, s.Near);
            var tl = Add(Sub(forward, right), up);
            float distance = F(F(Magnitude(tl) * s.Far) / s.Near);
            var rays = new Matrix4x4();
            rays.SetRow(0, Mul(Normalized(tl), distance));
            rays.SetRow(1, Mul(Normalized(Add(Add(right, forward), up)), distance));
            rays.SetRow(2, Mul(Normalized(Sub(Add(right, forward), up)), distance));
            rays.SetRow(3, Mul(Normalized(Sub(Sub(forward, right), up)), distance));
            float delta = F(s.Position.y - p.FogHeight), range = p.SceneFogMode == FogMode.Linear ? F(p.SceneFogEnd - p.SceneFogStart) : 0;
            float inv = Mathf.Abs(range) > 1e-4f ? F(1f / range) : 0, linear = p.SceneFogMode == FogMode.Linear ? -inv : 0, end = p.SceneFogMode == FogMode.Linear ? F(inv * p.SceneFogEnd) : 0;
            var c = new List<Command>();
            c.Add(new Command { Call = "vector", Property = 0xf4, Vector = new Vector4(p.FogHeight, delta, delta <= 0 ? 1 : 0, F(p.FogHeightDensity * .5f)) });
            c.Add(new Command { Call = "vector", Property = 0xf5, Vector = new Vector4(-(p.StartDistance > 0 ? p.StartDistance : 0), 0, 0, 0) });
            c.Add(new Command { Call = "matrix", Property = 0xf2, Matrix = rays });
            c.Add(new Command { Call = "vector", Property = 0xf3, Vector = new Vector4(s.Position.x, s.Position.y, s.Position.z, 0) });
            c.Add(new Command { Call = "vector", Property = 0xf6, Vector = p.FogColor });
            c.Add(new Command { Call = "vector", Property = 0xf7, Vector = new Vector4(F(p.SceneFogDensity * 1.2011224031448364f), F(p.SceneFogDensity * 1.4426950216293335f), linear, end) });
            c.Add(new Command { Call = "vector", Property = 0xf8, Vector = new Vector4((int)p.SceneFogMode, p.IsUseRadialDistance ? 1 : 0, linear, end) });
            c.Add(new Command { Call = "viewProjection", View = Matrix4x4.identity, Matrix = orthoGPU });
            c.Add(new Command { Call = "targetTexture", Property = 0x2a, Target = "source" });
            c.Add(new Command { Call = "drawMesh", Matrix = Matrix4x4.identity, PassIndex = p.IsDistanceFog ? (p.IsHeightFog ? 0 : 1) : 2 });
            c.Add(new Command { Call = "viewProjection", View = view, Matrix = cameraGPU });
            if (evt <= 550)
                c.Add(new Command { Call = "handoff" });
            else
            {
                c.Add(new Command { Call = "blit", Target = "camera" });
                c.Add(new Command { Call = "releaseRT", Target = "target" });
            }

            return c;
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor descriptor)
        {
            cmd.GetTemporaryRT(TargetId, descriptor.width, descriptor.height, 0, FilterMode.Bilinear);
            ConfigureTarget(new RenderTargetIdentifier(TargetId));
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData data)
        {
            var shader = ShaderManager.GetShader((ShaderManager.ShaderKinds)0xbd);
            if (material == null || ReferenceEquals(material.shader, null))
                throw new NullReferenceException();
            if (material.shader != shader)
                material.shader = shader;
            var cmd = CommandBufferPool.Get("GlobalFogPass");
            var source = feature.SourceRT;
            try
            {
                bool flipped = data.cameraData.IsCameraProjectionMatrixFlipped();
                var ortho = GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(0, 1, 0, 1, -1, 100), flipped);
                var projection = GL.GetGPUProjectionMatrix(data.cameraData.camera.projectionMatrix, flipped);
                var sample = CameraSample.Read(targetCamera);
                var commands = Build(cameraData.Parameter.GlobalFog, sample, (int)renderPassEvent, data.cameraData.GetViewMatrix(), ortho, projection);
                CommandObserver?.Invoke(sample, commands);
                foreach (var c in commands)
                {
                    switch (c.Call)
                    {
                        case "vector":
                            cmd.SetGlobalVector(ShaderManager.GetPropertyId((ShaderManager.PropertyId)c.Property), c.Vector);
                            break;
                        case "matrix":
                            cmd.SetGlobalMatrix(ShaderManager.GetPropertyId((ShaderManager.PropertyId)c.Property), c.Matrix);
                            break;
                        case "viewProjection":
                            RenderingUtils.SetViewAndProjectionMatrices(cmd, c.View, c.Matrix, false);
                            break;
                        case "targetTexture":
                            cmd.SetGlobalTexture(ShaderManager.GetPropertyId((ShaderManager.PropertyId)c.Property), source.RtId);
                            break;
                        case "drawMesh":
                            cmd.DrawMesh(quad, c.Matrix, material, 0, c.PassIndex);
                            break;
                        case "handoff":
                            feature.SetSourceRT(RenderTextureHandle.Make(TargetId, data.cameraData.cameraTargetDescriptor.width, data.cameraData.cameraTargetDescriptor.height), cmd, ref data);
                            break;
                        case "blit":
                            if (data.cameraData.camera.targetTexture == null)
                                throw new InvalidOperationException("Original fog camera targetTexture is not bound.");
                            cmd.Blit(TargetId, data.cameraData.camera.targetTexture);
                            break;
                        case "releaseRT":
                            cmd.ReleaseTemporaryRT(TargetId);
                            break;
                    }
                }

                if ((int)renderPassEvent > 550)
                {
                    // URP14's final blit reads its own color attachment; retain
                    // the native targetTexture route and explicitly bridge it.
                    var target = new RenderTargetIdentifier(data.cameraData.camera.targetTexture);
                    var host = data.cameraData.renderer.cameraColorTargetHandle.nameID;
                    if (target != host)
                    {
                        cmd.Blit(target, host);
                        HostTargetBridgeObserver?.Invoke(data.cameraData.camera);
                    }
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
            CoreUtils.Destroy(quad);
            quad = null;
            CoreUtils.Destroy(material);
            material = null;
            cameraData = null;
            feature = null;
            targetCamera = null;
        }
    }
}
