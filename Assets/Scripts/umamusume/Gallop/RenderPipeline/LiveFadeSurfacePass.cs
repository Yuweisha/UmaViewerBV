using Gallop.Live;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gallop.RenderPipeline
{
    // Host transport for native RawImageCommon color, after scene post effects
    // and multi-camera output. This is UI alpha blending, not a Film/LUT effect.
    public sealed class LiveFadeSurfacePass : ScriptableRenderPass
    {
        private Material _material;
        private Mesh _quad;
        public LiveFadeSurfacePass()
        {
            renderPassEvent = (RenderPassEvent)((int)RenderPassEvent.AfterRendering + 5);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var director = Director.instance;
            var camera = renderingData.cameraData.camera;
            if (director == null || camera != director.FinalRenderCamera)
                return;
            var color = director.PresentationFadeColor;

            if (color.a <= 0f)
                return;
            if (_material == null)
            {
                var shader = Shader.Find("UI/Default");
                if (shader == null)
                    throw new System.InvalidOperationException("Native fade UI surface shader unavailable");
                _material = new Material(shader)
                {
                    hideFlags = HideFlags.DontSave
                };
                _material.mainTexture = Texture2D.whiteTexture;
                _quad = new Mesh()
                {
                    name = "Live native fade UI surface",
                    hideFlags = HideFlags.DontSave
                };
                _quad.vertices = new[]
                {
                    new Vector3(-1, -1, 0),
                    new Vector3(-1, 1, 0),
                    new Vector3(1, 1, 0),
                    new Vector3(1, -1, 0)
                };
                _quad.uv = new[]
                {
                    Vector2.zero,
                    Vector2.up,
                    Vector2.one,
                    Vector2.right
                };
                _quad.colors = new[]
                {
                    Color.white,
                    Color.white,
                    Color.white,
                    Color.white
                };
                _quad.triangles = new[]
                {
                    0,
                    1,
                    2,
                    0,
                    2,
                    3
                };
            }

            _material.SetColor("_Color", color);
            var cmd = CommandBufferPool.Get("LiveFade.RawImageSurface");
            try
            {
                // Gallop keeps the screen color readable until final presentation.
                var renderer = renderingData.cameraData.renderer;
                var destination = renderer is GallopRenderer && camera.targetTexture == null ? renderer.cameraColorTargetHandle.nameID : camera.targetTexture != null ? new RenderTargetIdentifier(camera.targetTexture) : new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget);
                cmd.SetRenderTarget(destination);
                cmd.SetViewport(camera.pixelRect);
                cmd.SetGlobalFloat("unity_GUIZTestMode", (float)CompareFunction.Always);
                cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
                cmd.DrawMesh(_quad, Matrix4x4.identity, _material);
                cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix, GL.GetGPUProjectionMatrix(camera.projectionMatrix, camera.targetTexture != null));
                context.ExecuteCommandBuffer(cmd);
            }
            finally
            {
                CommandBufferPool.Release(cmd);
            }
        }

        public void Dispose()
        {
            CoreUtils.Destroy(_material);
            CoreUtils.Destroy(_quad);
            _material = null;
            _quad = null;
        }
    }
}
