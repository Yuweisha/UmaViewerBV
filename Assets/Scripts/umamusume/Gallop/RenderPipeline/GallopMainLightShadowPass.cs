using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gallop.RenderPipeline
{
    // Native Setup 0x521f240, OnCameraSetup 0x521dd50, Configure 0x521da20,
    // draw 0x521e2d0 and receiver 0x521ead0. Unlike URP14 this uses a transient
    // 16-bit map, original receiver fade and boolean soft-shadow parameter.
    public sealed class GallopMainLightShadowPass : ScriptableRenderPass
    {
        readonly Matrix4x4[] matrices = new Matrix4x4[5];
        readonly ShadowSliceData[] slices = new ShadowSliceData[4];
        readonly Vector4[] spheres = new Vector4[4], ranges = new Vector4[4];
        int count, width, height;
        bool ready;
        RenderTexture texture;
        RTHandle handle;
        public RenderTexture ShadowTexture => texture;
        public Matrix4x4[] Matrices => matrices;
        public ShadowSliceData[] Slices => slices;

        public GallopMainLightShadowPass()
        {
            renderPassEvent = (RenderPassEvent)50;
        }

        public bool Setup(ref RenderingData data)
        {
            if (!data.shadowData.supportsMainLightShadows)
                return false;
            ready = false;
            for (int i = 0; i < 4; i++)
            {
                spheres[i] = ranges[i] = Vector4.zero;
                slices[i].Clear();
            }

            for (int i = 0; i < 5; i++)
                matrices[i] = Matrix4x4.identity;
            int index = data.lightData.mainLightIndex;
            if (index < 0 || data.lightData.visibleLights[index].light.shadows == LightShadows.None || !data.cullResults.GetShadowCasterBounds(index, out _))
                return false;
            count = data.shadowData.mainLightShadowCascadesCount;
            width = data.shadowData.mainLightShadowmapWidth;
            height = data.shadowData.mainLightShadowmapHeight;
            if (count == 2)
                height >>= 1;
            return true;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData data)
        {
            int index = data.lightData.mainLightIndex;
            var light = data.lightData.visibleLights[index].light;
            int resolution = ShadowUtils.GetMaxTileResolutionInAtlas(data.shadowData.mainLightShadowmapWidth, data.shadowData.mainLightShadowmapHeight, count);
            for (int i = 0; i < count; i++)
                ready = ShadowUtils.ExtractDirectionalLightMatrix(ref data.cullResults, ref data.shadowData, index, i, width, height, resolution, light.shadowNearPlane, out spheres[i], out slices[i]);
        // Native ready is overwritten per slice (not AND-reduced).
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor descriptor)
        {
            texture = ShadowUtils.GetTemporaryShadowTexture(width, height, 16);
            handle = RTHandles.Alloc((Texture)texture);
            ConfigureTarget(handle);
            ConfigureClear(ClearFlag.All, Color.black);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData data)
        {
            if (!ready)
                return;
            int index = data.lightData.mainLightIndex;
            var visible = data.lightData.visibleLights[index];
            var light = visible.light;
            var owner = data.cameraData.camera.GetComponent<CameraData>();
            var cmd = CommandBufferPool.Get("Gallop main shadow");
#if UNITY_EDITOR
            var originalLightShadows = light.shadows;
            bool changedLightShadows = false;
#endif
            try
            {
#if UNITY_EDITOR
                // Same-frame historical-resource hypothesis only. Restore the
                // authored timeline value after submitting this camera's pass.
#endif
                var settings = new ShadowDrawingSettings(data.cullResults, index);
                for (int i = 0; i < count; i++)
                {
                    if (owner != null && owner.IsShadowMapOffset)
                        ApplyOffset(owner, ref slices[i], i);
                    var split = settings.splitData;
                    split.cullingSphere = spheres[i];
                    settings.splitData = split;
                    // Original 0x5cf3be0 was already machine-code checked against
                    // this URP helper, including per-light soft-quality overrides.
                    var bias = ShadowUtils.GetShadowBias(ref visible, index, ref data.shadowData, slices[i].projectionMatrix, slices[i].resolution);
                    // Native helper writes all three globals for every light.
                    cmd.SetGlobalVector("_ShadowBias", bias);
                    var direction = visible.localToWorldMatrix.GetColumn(2);
                    cmd.SetGlobalVector("_LightDirection", new Vector4(-direction.x, -direction.y, -direction.z, 0f));
                    var position = visible.localToWorldMatrix.GetColumn(3);
                    cmd.SetGlobalVector("_LightPosition", new Vector4(position.x, position.y, position.z, 1f));
#if UNITY_EDITOR
                    if (i == 0)
                        ;
#endif
                    ShadowUtils.RenderShadowSlice(cmd, ref context, ref slices[i], ref settings, slices[i].projectionMatrix, slices[i].viewMatrix);
#if UNITY_EDITOR
                    if (i == 0)
                        ;
#endif
                    ranges[i] = new Vector4((float)slices[i].offsetX / width, (float)(slices[i].offsetX + slices[i].resolution) / width, (float)slices[i].offsetY / height, (float)(slices[i].offsetY + slices[i].resolution) / height);
                    matrices[i] = slices[i].shadowTransform;
                }

                var fallback = Matrix4x4.zero;
                fallback.m22 = SystemInfo.usesReversedZBuffer ? 1f : 0f;
                for (int i = count; i < 5; i++)
                    matrices[i] = fallback;
                CoreUtils.SetKeyword(cmd, "_MAIN_LIGHT_SHADOWS", true);
                CoreUtils.SetKeyword(cmd, "_MAIN_LIGHT_SHADOWS_CASCADE", data.shadowData.mainLightShadowCascadesCount > 1);
                CoreUtils.SetKeyword(cmd, "_SHADOWS_SOFT", data.shadowData.supportsSoftShadows && light.shadows == LightShadows.Soft);
                cmd.SetGlobalTexture("_MainLightShadowmapTexture", texture);
                cmd.SetGlobalMatrixArray("_MainLightWorldToShadow", matrices);
                cmd.SetGlobalVector("_MainLightShadowParams", NativeMainShadowReceiverPass.BuildParameters(light.shadowStrength, data.shadowData.supportsSoftShadows, light.shadows, data.cameraData.maxShadowDistance, data.cameraData.camera.farClipPlane));
                if (count > 1)
                {
                    for (int i = 0; i < 4; i++)
                        cmd.SetGlobalVector("_CascadeShadowSplitSpheres" + i, spheres[i]);
                    cmd.SetGlobalVector("_CascadeShadowSplitSphereRadii", new Vector4(spheres[0].w * spheres[0].w, spheres[1].w * spheres[1].w, spheres[2].w * spheres[2].w, spheres[3].w * spheres[3].w));
                    cmd.SetGlobalVectorArray("_MainLightShadowSliceUvRanges", ranges);
                }

                if (data.shadowData.supportsSoftShadows)
                {
                    if (Application.isMobilePlatform || (int)SystemInfo.graphicsDeviceType == 22)
                    {
                        float x = 0.5f / width, y = 0.5f / height;
                        cmd.SetGlobalVector("_MainLightShadowOffset0", new Vector4(-x, -y, 0f, 0f));
                        cmd.SetGlobalVector("_MainLightShadowOffset1", new Vector4(x, -y, 0f, 0f));
                        cmd.SetGlobalVector("_MainLightShadowOffset2", new Vector4(-x, y, 0f, 0f));
                        cmd.SetGlobalVector("_MainLightShadowOffset3", new Vector4(x, y, 0f, 0f));
                    }

                    cmd.SetGlobalVector("_MainLightShadowmapSize", new Vector4(1f / width, 1f / height, width, height));
                }

                context.ExecuteCommandBuffer(cmd);
            }
            finally
            {
#if UNITY_EDITOR
                if (changedLightShadows)
                    light.shadows = originalLightShadows;
#endif
                CommandBufferPool.Release(cmd);
            }
        }

        void ApplyOffset(CameraData owner, ref ShadowSliceData slice, int index)
        {
            var world = slice.viewMatrix.inverse;
            Vector3 right = ((Vector3)world.GetColumn(0)).normalized, up = ((Vector3)world.GetColumn(1)).normalized;
            Vector4 position = world.GetColumn(3);
            Vector3 offset = right * owner.ShadowMapHorizontalOffset + up * owner.ShadowMapVerticalOffset + owner.ShadowMapOffsets[index];
            position.x += offset.x;
            position.y += offset.y;
            position.z += offset.z;
            world.SetColumn(3, position);
            slice.viewMatrix = world.inverse;
            float scale = Mathf.Max(1e-6f, owner.ShadowMapScales[index]);
            slice.projectionMatrix.m00 *= scale;
            slice.projectionMatrix.m11 *= scale;
            slice.projectionMatrix.m03 *= scale;
            slice.projectionMatrix.m13 *= scale;
            var projection = slice.projectionMatrix;
            if (SystemInfo.usesReversedZBuffer)
                for (int i = 0; i < 4; i++)
                    projection[2, i] = -projection[2, i];
            var tex = Matrix4x4.identity;
            tex.m00 = tex.m11 = tex.m22 = tex.m03 = tex.m13 = tex.m23 = 0.5f;
            slice.shadowTransform = tex * (projection * slice.viewMatrix);
            if (count > 1)
                ShadowUtils.ApplySliceTransform(ref slice, width, height);
        }

        public override void OnCameraCleanup(CommandBuffer cmd) => Dispose();
        public void Dispose()
        {
            handle?.Release();
            handle = null;
            if (texture != null)
                RenderTexture.ReleaseTemporary(texture);
            texture = null;
        }
    }
}
