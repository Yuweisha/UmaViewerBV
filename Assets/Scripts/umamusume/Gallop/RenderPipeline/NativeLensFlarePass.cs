using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gallop.RenderPipeline
{
    // Installed-game Feature 0x51cd220, Setup 0x521ca80, Render 0x521c780.
    // Same tested geometry/UV and original shader as NativeFlareDrawValidation.
    // Synchronous Physics.Raycast replaces job scheduling, not its mask/ray/fade rules.
    public sealed class NativeLensFlarePass : ScriptableRenderPass, IDisposable
    {
        public sealed class SourceState
        {
            public CustomLensFlare Source;
            public float Visibility;
            public bool InView, Blocked;
            public int Mask;
            public string ColliderName;
            public Vector3 Viewport;
#if UNITY_EDITOR
            public float PreviousVisibility, RayDistance, HitDistance;
            public Vector3 RayOrigin, RayDirection;
            public int HitColliderId, HitLayer;
            public string HitPath;
#endif
        }

        sealed class CameraState
        {
            public int Frame = -1;
            public readonly Dictionary<CustomLensFlare, SourceState> Sources = new Dictionary<CustomLensFlare, SourceState>();
        }

        sealed class SetupPass : ScriptableRenderPass
        {
            readonly NativeLensFlarePass owner;
            public SetupPass(NativeLensFlarePass owner)
            {
                this.owner = owner;
                renderPassEvent = RenderPassEvent.BeforeRendering;
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData data) => owner.UpdateVisibility(data.cameraData.camera);
        }

        readonly Dictionary<Camera, CameraState> cameras = new Dictionary<Camera, CameraState>();
        readonly List<CustomLensFlare> removed = new List<CustomLensFlare>();
        readonly List<Camera> removedCameras = new List<Camera>();
        readonly SetupPass setup;
        readonly PostImageEffectFeature feature;
        readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        Material material;
        Mesh quad;
        int syncFrame = -1;
        static readonly int MainTex = Shader.PropertyToID("_MainTex");
        static readonly int MainTexST = Shader.PropertyToID("_MainTex_ST");
        static readonly int Size = Shader.PropertyToID("_Size");
        static readonly int UV = Shader.PropertyToID("_UV");
        static readonly int Tint = Shader.PropertyToID("_Color");
        public NativeLensFlarePass(PostImageEffectFeature feature)
        {
            this.feature = feature;
            setup = new SetupPass(this);
            renderPassEvent = (RenderPassEvent)600;
        }

        public bool Enqueue(ScriptableRenderer renderer, CameraData owner)
        {
            if (!CustomLensFlare.UseNativeRenderer || owner == null || owner.IsUIRendering || CustomLensFlare.NativeSources.Count == 0)
                return false;
            var shader = ShaderManager.GetShader(ShaderManager.ShaderKinds.BgLensFlare);
            if (shader == null || !shader.isSupported)
                return false;
            if (material == null)
            {
                material = CoreUtils.CreateEngineMaterial(shader);
                // Equivalent per-element draws; the original mesh pool instances this same shader.
                material.enableInstancing = false;
                material.DisableKeyword("INSTANCING_ON");
                quad = new Mesh
                {
                    name = "NativeLensFlareQuad",
                    hideFlags = HideFlags.HideAndDontSave,
                    vertices = new[]
                    {
                        Vector3.zero,
                        Vector3.zero,
                        Vector3.zero,
                        Vector3.zero
                    },
                    triangles = new[]
                    {
                        0,
                        1,
                        2,
                        0,
                        2,
                        3
                    }
                };
            }

            renderPassEvent = (RenderPassEvent)(owner.IsMirrorRendering ? 549 : 600);
            renderer.EnqueuePass(setup);
            renderer.EnqueuePass(this);
#if UNITY_EDITOR
            var diagnosticCamera = owner.GetComponent<Camera>();
            var monitor = owner.GetComponent<Live.MonitorCamera>();
#endif
            return true;
        }

        CameraState State(Camera camera)
        {
            if (!cameras.TryGetValue(camera, out var state))
            {
                removedCameras.Clear();
                foreach (var entry in cameras)
                    if (entry.Key == null)
                        removedCameras.Add(entry.Key);
                foreach (var dead in removedCameras)
                    cameras.Remove(dead);
                state = new CameraState();
                cameras.Add(camera, state);
            }

            return state;
        }

        void UpdateVisibility(Camera camera)
        {

            if (!NativeLensFlareCameraRegistry.ShouldExecute(camera))
                return;
            var state = State(camera);
            // Called once by the event0 setup for each camera render, as in the native feature.
            state.Frame = Time.frameCount;
            if (syncFrame != Time.frameCount)
            {
                Physics.SyncTransforms();
                syncFrame = Time.frameCount;
            }

            removed.Clear();
            foreach (var entry in state.Sources)
                if (entry.Key == null || !entry.Key.isActiveAndEnabled)
                    removed.Add(entry.Key);
            foreach (var source in removed)
                state.Sources.Remove(source);
            foreach (var source in CustomLensFlare.NativeSources)
            {
                if (source == null || !source.isActiveAndEnabled)
                    continue;
                if (!state.Sources.TryGetValue(source, out var item))
                {
                    item = new SourceState
                    {
                        Source = source
                    };
                    state.Sources.Add(source, item);
                }

                Vector3 origin = camera.transform.position;
                Vector3 direction = source.IsDirectional ? source.transform.TransformDirection(Vector3.forward) : source.transform.position - origin;
                Vector3 viewport = camera.WorldToViewportPoint(source.IsDirectional ? origin + direction : source.transform.position);
                bool inView = (source.IsDirectional || viewport.z < camera.farClipPlane) && viewport.x > 0 && viewport.x < 1 && viewport.y > 0 && viewport.y < 1;
                // Native excludes UI and SystemUI, in addition to each source's IgnoreLayers.
                int mask = ~((int)source.IgnoreLayers | GraphicSettings.GetCullingLayer(GraphicSettings.LayerIndex.LayerUI) | (1 << 21));
                RaycastHit hit = default;
                bool blocked = inView && Physics.Raycast(origin, source.IsDirectional ? -direction : direction.normalized, out hit, source.IsDirectional ? 10000f : direction.magnitude, mask, QueryTriggerInteraction.UseGlobal);

                item.InView = inView;
                item.Blocked = blocked;
                item.Mask = mask;
                item.Viewport = viewport;
                item.ColliderName = hit.collider ? hit.collider.name : null;
                // Native history is independent of source color/brightness. These only modulate draw uniforms.
                item.Visibility = NativeLensFlareGeometry.AdvanceVisibility(item.Visibility, true, inView, blocked, source.FadeSpeed, Time.deltaTime);
            }
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData data)
        {
            var camera = data.cameraData.camera;
            var target = data.cameraData.renderer.cameraColorTargetHandle;
            if (camera == null || material == null || target == null)
                return;
            if (!NativeLensFlareCameraRegistry.ShouldExecute(camera))
            {
#if UNITY_EDITOR
                // Read-only captures still prove that an unregistered camera's
                // event600 input/output are identical and observe copy1004.
#endif
                return;
            }

            var state = State(camera);
            if (state.Frame != Time.frameCount)
                UpdateVisibility(camera);
            var cmd = CommandBufferPool.Get("Gallop.NativeLensFlare");
            int draws = 0;
            try
            {
#if UNITY_EDITOR
                var descriptor = data.cameraData.cameraTargetDescriptor;
#endif
                cmd.SetRenderTarget(target.nameID);
                cmd.SetViewport(camera.pixelRect);
                // Original shader's camera-space quad draw follows the same
                // non-flipped projection convention as the validated GL primitive.
                // URP's RT-flipped projection mirrors these flares vertically.
                cmd.SetViewProjectionMatrices(Matrix4x4.identity, GL.GetGPUProjectionMatrix(camera.projectionMatrix, false));
                foreach (var entry in state.Sources)
                {
                    var item = entry.Value;
                    var source = item.Source;
                    if (source == null || !source.isActiveAndEnabled || item.Visibility <= 0 || source.Brightness <= 0 || source.Flare == null)
                        continue;
                    var asset = source.Flare;
                    var texture = asset.Texture;
                    if (texture == null || asset.ElementArray == null)
                        continue;
                    // MakeGeometry uses the authored position even for its directional branch.
                    Vector3 position = source.IsDirectional ? camera.worldToCameraMatrix.MultiplyVector(-source.transform.position * (camera.nearClipPlane * 2)) : camera.worldToCameraMatrix.MultiplyPoint3x4(source.transform.position);
                    foreach (var element in asset.ElementArray)
                    {
                        if (element == null)
                            continue;
                        var draw = NativeLensFlareGeometry.Prepare(element, asset.TextureLayout, new Vector2(.5f / texture.width, .5f / texture.height), position, item.Visibility * source.Brightness, source.Color, asset.UseFog, RenderSettings.fog ? RenderSettings.fogDensity : 0);
                        properties.Clear();
                        properties.SetTexture(MainTex, texture);
                        properties.SetVector(MainTexST, new Vector4(1, 1, 0, 0));
                        properties.SetVector(Size, new Vector4(draw.Size.x, draw.Size.y, 0, 0));
                        properties.SetVector(UV, draw.UV);
                        properties.SetVector(Tint, draw.Color);
                        cmd.DrawMesh(quad, Matrix4x4.Translate(draw.CameraPosition), material, 0, 0, properties);
                        draws++;
                    }
                }

                cmd.SetViewProjectionMatrices(data.cameraData.GetViewMatrix(), data.cameraData.GetGPUProjectionMatrix());
                // URP14 may swap A/B attachments during its own event550 post
                // pass. Publish the attachment actually drawn at600, otherwise
                // color604 samples the pre-swap alias and erases every flare.
                feature.ResetSourceRT(cmd, ref data);

                context.ExecuteCommandBuffer(cmd);
            }
            finally
            {
                CommandBufferPool.Release(cmd);
            }
        }

#if UNITY_EDITOR
        // changing the monitor target, source material, render gate or visibility.
#endif
        public void Dispose()
        {
            cameras.Clear();
            CoreUtils.Destroy(material);
            CoreUtils.Destroy(quad);
            material = null;
            quad = null;
        }
    }
}
