using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gallop.RenderPipeline
{
    // Original SetupMainLightShadowReceiverConstants, RVA0x521ead0. Stock URP14
    // uses squared-distance fade and a quality enum in y. Restore the original
    // receiver's linear-distance coefficients and boolean soft switch.
    // Atlas generation/matrices/bias remain the engine/URP boundary; this pass
    // does not blur the image or change the light/character geometry.
    public sealed class NativeMainShadowReceiverPass : ScriptableRenderPass
    {
        private static readonly int ShadowParams = Shader.PropertyToID("_MainLightShadowParams");
#if UNITY_EDITOR
        public struct BiasObservation
        {
            // InputBias belongs to this RenderingData. CasterBias is only the
            // CPU global snapshot: queued SRP commands may not be reflected in
            // Shader.GetGlobalVector. Use the opt-in GPU probe for draw state.
            public Vector4 InputBias, CasterBias;
            public bool HasAdditionalData, UsePipelineSettings;
        }

        private static readonly System.Collections.Generic.Dictionary<Camera, BiasObservation> Observations = new System.Collections.Generic.Dictionary<Camera, BiasObservation>();
        public static bool TryGetBiasObservation(Camera camera, out BiasObservation value) => Observations.TryGetValue(camera, out value);
#endif
        public NativeMainShadowReceiverPass()
        {
            renderPassEvent = (RenderPassEvent)((int)RenderPassEvent.BeforeRenderingOpaques - 1);
        }

        public static Vector4 BuildParameters(float strength, bool supportsSoftShadows, LightShadows shadows, float maximumDistance, float farClip)
        {
            // Native COMISS selects farClip on an unordered comparison too.
            float distance = float.IsNaN(maximumDistance) || float.IsNaN(farClip) || farClip <= maximumDistance ? farClip : maximumDistance;
            float start = distance * 0.8f;
            float scale = 1f / (distance - start);
            return new Vector4(strength, supportsSoftShadows && shadows == LightShadows.Soft ? 1f : 0f, scale, -start * scale);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData data)
        {
            var camera = data.cameraData.camera;
            var director = Gallop.Live.Director.instance;
            if (director == null || !director.IsPresentationCamera(camera) || !data.shadowData.supportsMainLightShadows || data.lightData.mainLightIndex < 0)
                return;
            var light = data.lightData.visibleLights[data.lightData.mainLightIndex].light;
            if (light == null || light.shadows == LightShadows.None)
                return;
#if UNITY_EDITOR
            var additional = light.GetComponent<UniversalAdditionalLightData>();
            Observations[camera] = new BiasObservation
            {
                InputBias = data.shadowData.bias[data.lightData.mainLightIndex],
                CasterBias = Shader.GetGlobalVector("_ShadowBias"),
                HasAdditionalData = additional != null,
                UsePipelineSettings = additional == null || additional.usePipelineSettings
            };
#endif
            // Native Setup0x521f240 rejects an absent caster set. Keep the host
            // empty-atlas constants intact for that camera instead of turning
            // a1x1 empty map into a stale-matrix shadow receiver.
            if (data.cameraData.maxShadowDistance <= 0f || !data.cullResults.GetShadowCasterBounds(data.lightData.mainLightIndex, out _))
                return;
            var receiverShadows = light.shadows;
#if UNITY_EDITOR
            // Same opt-in experiment as the atlas pass. This later legacy
            // adapter is the final writer of params.y before the floor draw.
#endif
            var parameters = BuildParameters(light.shadowStrength, data.shadowData.supportsSoftShadows, receiverShadows, data.cameraData.maxShadowDistance, camera.farClipPlane);
            var cmd = CommandBufferPool.Get("Gallop native main shadow receiver");
            try
            {
                cmd.SetGlobalVector(ShadowParams, parameters);
                context.ExecuteCommandBuffer(cmd);
            }
            finally
            {
                CommandBufferPool.Release(cmd);
            }
        }
    }
}
