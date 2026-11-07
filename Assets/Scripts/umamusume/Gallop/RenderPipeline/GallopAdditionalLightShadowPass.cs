using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Request = Gallop.RenderPipeline.GallopAdditionalShadowLayout.Request;

namespace Gallop.RenderPipeline
{
    // Independent game pass, not URP14's AdditionalLightsShadowCasterPass.
    // Original Setup 0x51fd7a0 / draw 0x51fcb20 / receiver 0x51fd340.
    public sealed class GallopAdditionalLightShadowPass : ScriptableRenderPass
    {
        readonly List<Request> requests = new List<Request>();
        readonly List<RectInt> free = new List<RectInt>();
        readonly List<int> sliceToAdditional = new List<int>(), sliceToFace = new List<int>();
        readonly Dictionary<int, ulong> hashes = new Dictionary<int, ulong>();
        Request[] sorted = Array.Empty<Request>();
        float[] distances = Array.Empty<float>();
        int[] firstRequest = Array.Empty<int>(), visibleToAdditional = Array.Empty<int>(), additionalToVisible = Array.Empty<int>();
        ShadowSliceData[] slices = Array.Empty<ShadowSliceData>();
        Vector4[] parameters = Array.Empty<Vector4>(), uvRanges = Array.Empty<Vector4>();
        Matrix4x4[] matrices = Array.Empty<Matrix4x4>(), worldToLight = Array.Empty<Matrix4x4>();
        readonly float[] customIndices = new float[UniversalRenderPipeline.maxPerObjectLights];
        readonly Texture2D softTexture;
        Vector4 mainParameters;
        bool rescaled, tooSmall, removed;
        int width, height, selectedCount;
        RenderTexture texture;
        RTHandle target;
        public int SliceCount => sliceToAdditional.Count;
        public RenderTexture ShadowTexture => texture;
        public int AtlasWidth => width;
        public int AtlasHeight => height;
        public Vector4[] Parameters => parameters;
        public Vector4[] UvRanges => uvRanges;
        public Matrix4x4[] ShadowMatrices => matrices;
        public Matrix4x4[] WorldToLightMatrices => worldToLight;
        public float[] CustomIndices => customIndices;
        public IReadOnlyList<int> SliceToAdditional => sliceToAdditional;
        public IReadOnlyList<int> SliceToFace => sliceToFace;
        public int GetShadowLightIndexFromLightIndex(int index) => index < 0 || index >= visibleToAdditional.Length ? -1 : visibleToAdditional[index];

        public GallopAdditionalLightShadowPass()
        {
            renderPassEvent = (RenderPassEvent)50;
            // Exact player Resources texture, extracted without repainting.
            softTexture = Resources.Load<Texture2D>("RendererPipeline/Soft");
            if (softTexture != null) softTexture.wrapMode = TextureWrapMode.Repeat;
        }
        static void Ensure<T>(ref T[] array, int size) { if (array.Length < size) Array.Resize(ref array, size); }
        static bool Valid(ref RenderingData data, int index)
        {
            var v = data.lightData.visibleLights[index]; var light = v.light;
            return index != data.lightData.mainLightIndex && v.lightType != LightType.Directional &&
                light != null && light.shadows != LightShadows.None && !Mathf.Approximately(light.shadowStrength, 0f);
        }
        ulong RequestHash(ref RenderingData data, int edge)
        {
            ulong c128 = 0, c256 = 0, c512 = 0, c1024 = 0, c2048 = 0, c4096 = 0, soft = 0, point = 0;
            for (int i = 0; i < data.lightData.visibleLights.Length; i++)
            {
                if (!Valid(ref data, i)) continue;
                var v = data.lightData.visibleLights[i]; var light = v.light;
                if (v.lightType == LightType.Point) point++;
                if (light.shadows == LightShadows.Soft) soft++;
                switch (GallopAdditionalShadowLayout.Resolution(light))
                { case 128: c128++; break; case 256: c256++; break; case 512: c512++; break;
                  case 1024: c1024++; break; case 2048: c2048++; break; case 4096: c4096++; break; }
            }
            unchecked
            {
                ulong h = c4096; h = (h << 7) | c2048; h = (h << 7) | c1024;
                h = (h << 8) | c512; h = (h << 8) | c256; h = (h << 8) | c128;
                h = (h << 8) | soft; h = (h << 8) | point;
                return (h << 3) | (edge == 512 ? 1UL : edge == 1024 ? 2UL : edge == 2048 ? 3UL : edge == 4096 ? 4UL : 0UL);
            }
        }
        public bool Setup(ref RenderingData data)
        {
            sliceToAdditional.Clear(); sliceToFace.Clear();
            width = data.shadowData.additionalLightsShadowmapWidth; height = data.shadowData.additionalLightsShadowmapHeight;
            int main = data.lightData.mainLightIndex;
            var light = main >= 0 ? data.lightData.visibleLights[main].light : null;
            mainParameters = NativeMainShadowReceiverPass.BuildParameters(light != null ? light.shadowStrength : 0f,
                data.shadowData.supportsSoftShadows, light != null ? light.shadows : LightShadows.None,
                data.cameraData.maxShadowDistance, data.cameraData.camera.farClipPlane);
            if (data.lightData.additionalLightsCount <= 0) return false;
            if (!data.cameraData.isPreviewCamera)
            {
                int id = data.cameraData.camera.GetInstanceID(); ulong hash = RequestHash(ref data, width);
                hashes.TryGetValue(id, out ulong old);
                if (old != hash) { hashes[id] = hash; rescaled = tooSmall = removed = false; }
            }
            int count = data.lightData.visibleLights.Length;
            int budget = GallopAdditionalShadowLayout.SliceBudget(UniversalRenderPipeline.maxVisibleAdditionalLights);
            Ensure(ref distances, count); Ensure(ref firstRequest, count); Ensure(ref visibleToAdditional, count);
            Ensure(ref additionalToVisible, System.Math.Max(UniversalRenderPipeline.maxVisibleAdditionalLights, count)); Ensure(ref parameters, System.Math.Max(UniversalRenderPipeline.maxVisibleAdditionalLights, count));
            Ensure(ref worldToLight, System.Math.Max(budget, count)); Ensure(ref uvRanges, System.Math.Max(budget, count));
            Ensure(ref matrices, budget);
            requests.Clear();
            for (int i = 0; i < distances.Length; i++) distances[i] = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (!Valid(ref data, i)) continue;
                var v = data.lightData.visibleLights[i]; light = v.light;
                int resolution = GallopAdditionalShadowLayout.Resolution(light);
                for (int face = 0; face < GallopAdditionalShadowLayout.SliceCount(v.lightType); face++)
                    requests.Add(new Request { visibleIndex = i, sliceIndex = face, requestedResolution = resolution,
                        soft = light.shadows == LightShadows.Soft, point = v.lightType == LightType.Point });
                var d = data.cameraData.camera.transform.position - light.transform.position;
                // Native accumulates y*y + x*x + z*z in this order.
                distances[i] = d.y * d.y + d.x * d.x + d.z * d.z;
            }
            Ensure(ref sorted, requests.Count);
            requests.CopyTo(sorted); for (int i = requests.Count; i < sorted.Length; i++) sorted[i].requestedResolution = 0;
            GallopAdditionalShadowLayout.Sort(sorted, distances, 0, requests.Count);
            selectedCount = GallopAdditionalShadowLayout.SelectCount(sorted, requests.Count,
                UniversalRenderPipeline.maxVisibleAdditionalLights, width, out int scale);
            if (selectedCount < requests.Count) removed = true;
            for (int i = selectedCount; i < sorted.Length; i++) sorted[i].requestedResolution = 0;
            for (int i = 0; i < firstRequest.Length; i++) firstRequest[i] = -1;
            for (int i = selectedCount - 1; i >= 0; i--) firstRequest[sorted[i].visibleIndex] = i;
            GallopAdditionalShadowLayout.Pack(sorted, selectedCount, width, scale, free, ref rescaled, ref tooSmall);
            Ensure(ref slices, selectedCount);
            Array.Clear(customIndices, 0, customIndices.Length);
            for (int i = 0; i < count; i++) { parameters[i] = new Vector4(0, 0, 0, -1); uvRanges[i] = new Vector4(0, 1, 0, 1); }
            int additional = -1, custom = 0, validLights = 0;
            for (int i = 0; i < count && sliceToAdditional.Count < selectedCount; i++)
            {
                if (i == main) { visibleToAdditional[i] = -1; continue; }
                additional++; visibleToAdditional[i] = additional; additionalToVisible[additional] = i;
                var v = data.lightData.visibleLights[i]; int faces = GallopAdditionalShadowLayout.SliceCount(v.lightType);
                customIndices[custom++] = additional;
                if (sliceToAdditional.Count + faces > selectedCount && Valid(ref data, i)) break;
                int firstSlice = sliceToAdditional.Count; bool any = false;
                for (int face = 0; face < faces; face++)
                {
                    if (!data.cullResults.GetShadowCasterBounds(i, out _) || !data.shadowData.supportsAdditionalLightShadows ||
                        !Valid(ref data, i) || firstRequest[i] < 0) continue;
                    int slice = sliceToAdditional.Count;
                    bool success = Extract(ref data, i, v.lightType, face, out var view, out var projection, out var split);
                    if (!success) continue;
                    slices[slice].viewMatrix = view; slices[slice].projectionMatrix = projection; slices[slice].splitData = split;
                    matrices[slice] = ShadowTransform(projection, view);
                    sliceToAdditional.Add(additional); sliceToFace.Add(face); light = v.light;
                    parameters[additional] = new Vector4(light.shadowStrength,
                        data.shadowData.supportsSoftShadows && light.shadows == LightShadows.Soft ? 1 : 0,
                        v.lightType == LightType.Point ? 1 : 0, firstSlice);
                    if (v.lightType == LightType.Spot) worldToLight[additional] = SpotWorldToLight(v.spotAngle, v.range, light.transform.worldToLocalMatrix);
                    any = true;
                }
                if (any) validLights++;
            }
            if (validLights == 0) return false;
            int maxX = 0, maxY = 0;
            for (int i = 0; i < selectedCount; i++)
            { maxX = System.Math.Max(maxX, sorted[i].offsetX + sorted[i].allocatedResolution); maxY = System.Math.Max(maxY, sorted[i].offsetY + sorted[i].allocatedResolution); }
            width = Mathf.NextPowerOfTwo(maxX); height = Mathf.NextPowerOfTwo(maxY);
            for (int i = 0; i < sliceToAdditional.Count; i++)
            {
                int index = additionalToVisible[sliceToAdditional[i]];
                var request = sorted[firstRequest[index] + sliceToFace[i]];
                slices[i].offsetX = request.offsetX; slices[i].offsetY = request.offsetY; slices[i].resolution = request.allocatedResolution;
                var tile = Matrix4x4.identity;
                tile.m00 = (float)request.allocatedResolution / width; tile.m11 = (float)request.allocatedResolution / height;
                tile.m03 = (float)request.offsetX / width; tile.m13 = (float)request.offsetY / height;
                matrices[i] = tile * matrices[i];
                // Native receiver's y-low uses atlas WIDTH, even for a rectangular atlas.
                uvRanges[i] = new Vector4((float)request.offsetX / width, (float)(request.offsetX + request.allocatedResolution) / width,
                    (float)request.offsetY / width, (float)(request.offsetY + request.allocatedResolution) / height);
            }
            return true;
        }
        static bool Extract(ref RenderingData data, int index, LightType type, int face,
            out Matrix4x4 view, out Matrix4x4 projection, out ShadowSplitData split)
        {
            if (type == LightType.Spot)
                return data.cullResults.ComputeSpotShadowMatricesAndCullingPrimitives(index, out view, out projection, out split);
            // Original GetPointLightShadowFrustumFovBiasInDegrees (0x6e9e80) returns zero.
            bool result = data.cullResults.ComputePointShadowMatricesAndCullingPrimitives(index, (CubemapFace)face, 0f, out view, out projection, out split);
            view.m10 = -view.m10; view.m11 = -view.m11; view.m12 = -view.m12; view.m13 = -view.m13;
            return result;
        }
        public static Matrix4x4 ShadowTransform(Matrix4x4 projection, Matrix4x4 view)
        {
            if (SystemInfo.usesReversedZBuffer) { projection.m20 = -projection.m20; projection.m21 = -projection.m21; projection.m22 = -projection.m22; projection.m23 = -projection.m23; }
            var t = Matrix4x4.identity; t.m00 = t.m11 = t.m22 = t.m03 = t.m13 = t.m23 = .5f;
            return t * (projection * view);
        }
        public static Matrix4x4 SpotWorldToLight(float angle, float range, Matrix4x4 worldToLocal)
        {
            float half = angle * Mathf.Deg2Rad * .5f, sin = Mathf.Sin(half), cos = Mathf.Cos(half);
            var p = Matrix4x4.identity; var scale = Matrix4x4.identity;
            if (!Mathf.Approximately(sin, 0) && !Mathf.Approximately(cos, 0)) { p.m32 = 2f / (sin / cos); p.m33 = 0; }
            // Preserve original conditional (not a guessed inverse-range attenuation).
            if (Mathf.Approximately(range, 0)) scale.m00 = scale.m11 = scale.m22 = 1f / range;
            return (p * scale) * worldToLocal;
        }
        public static Vector4 Bias(LightType type, float angle, float range, float projectionM00,
            int resolution, Vector4 bias, bool soft)
        {
            float frustum = type == LightType.Directional ? 2f / projectionM00 :
                type == LightType.Spot ? Mathf.Tan(angle * Mathf.Deg2Rad * .5f) * range :
                type == LightType.Point ? Mathf.Tan(Mathf.PI * .25f) * range : 0f;
            float texel = frustum / resolution;
            float depth = -bias.x * texel, normal = type == LightType.Point ? 0f : -bias.y * texel;
            if (soft) { depth *= 2.5f; normal *= 2.5f; }
            return new Vector4(depth, normal, 0, 0);
        }
        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor descriptor)
        {
            texture = ShadowUtils.GetTemporaryShadowTexture(width, height, 16); target = RTHandles.Alloc((Texture)texture);
            ConfigureTarget(target); ConfigureClear(ClearFlag.All, Color.black);
        }
        public override void Execute(ScriptableRenderContext context, ref RenderingData data)
        {
            bool any = false, additionalSoft = false;
            var cmd = CommandBufferPool.Get("Gallop additional shadows");
            try
            {
                for (int i = 0; i < sliceToAdditional.Count; i++)
                {
                    int additional = sliceToAdditional[i]; var p = parameters[additional];
                    if (Mathf.Approximately(p.x, 0) || Mathf.Approximately(p.w, -1)) continue;
                    int index = additionalToVisible[additional]; var v = data.lightData.visibleLights[index]; var light = v.light;
                    var settings = new ShadowDrawingSettings(data.cullResults, index) { splitData = slices[i].splitData };
                    var bias = index >= 0 && index < data.shadowData.bias.Count ? Bias(v.lightType, v.spotAngle, v.range,
                        slices[i].projectionMatrix.m00, slices[i].resolution, data.shadowData.bias[index],
                        data.shadowData.supportsSoftShadows && light.shadows == LightShadows.Soft) : Vector4.zero;
                    cmd.SetGlobalVector("_ShadowBias", bias);
                    var direction = v.localToWorldMatrix.GetColumn(2); var position = v.localToWorldMatrix.GetColumn(3);
                    cmd.SetGlobalVector("_LightDirection", new Vector4(-direction.x, -direction.y, -direction.z, 0));
                    cmd.SetGlobalVector("_LightPosition", new Vector4(position.x, position.y, position.z, 1));
                    CoreUtils.SetKeyword(cmd, "_CASTING_PUNCTUAL_LIGHT_SHADOW", true);
                    ShadowUtils.RenderShadowSlice(cmd, ref context, ref slices[i], ref settings);
                    any = true; additionalSoft |= light.shadows == LightShadows.Soft;
                }
                int main = data.lightData.mainLightIndex;
                bool mainSoft = data.shadowData.supportsMainLightShadows && main >= 0 && data.lightData.visibleLights[main].light.shadows == LightShadows.Soft;
                bool soft = data.shadowData.supportsSoftShadows && (mainSoft || additionalSoft);
                CoreUtils.SetKeyword(cmd, "_ADDITIONAL_LIGHT_SHADOWS", any);
                CoreUtils.SetKeyword(cmd, "_SHADOWS_SOFT", soft);
                if (any)
                {
                    cmd.SetGlobalTexture("_AdditionalLightsShadowmapTexture", texture);
                    cmd.SetGlobalVector("_MainLightShadowParams", mainParameters);
                    cmd.SetGlobalVectorArray("_AdditionalShadowParams", parameters);
                    cmd.SetGlobalVectorArray("_AdditionalShadowSliceUvRanges", uvRanges);
                    cmd.SetGlobalMatrixArray("_AdditionalLightsWorldToShadow", matrices);
                    cmd.SetGlobalMatrixArray("_AdditionalLightsWorldToLight", worldToLight);
                    // Original field names differ from their Shader.PropertyToID strings.
                    cmd.SetGlobalTexture("_LightTexture0", softTexture);
                    cmd.SetGlobalFloatArray("_CustomLightIndices", customIndices);
                    if (soft)
                    {
                        // Native m_SupportsBoxFilterForShadows is Application.isMobilePlatform
                        // or graphicsDeviceType==22; desktop skips only the four offsets.
                        if (Application.isMobilePlatform || (int)SystemInfo.graphicsDeviceType == 22)
                        {
                            float x = .5f / data.shadowData.additionalLightsShadowmapWidth, y = .5f / data.shadowData.additionalLightsShadowmapHeight;
                            cmd.SetGlobalVector("_AdditionalShadowOffset0", new Vector4(-x, -y, 0, 0));
                            cmd.SetGlobalVector("_AdditionalShadowOffset1", new Vector4(x, -y, 0, 0));
                            cmd.SetGlobalVector("_AdditionalShadowOffset2", new Vector4(-x, y, 0, 0));
                            cmd.SetGlobalVector("_AdditionalShadowOffset3", new Vector4(x, y, 0, 0));
                        }
                        int w = data.shadowData.additionalLightsShadowmapWidth, h = data.shadowData.additionalLightsShadowmapHeight;
                        cmd.SetGlobalVector("_AdditionalShadowmapSize", new Vector4(1f / w, 1f / h, w, h));
                    }
                }
                context.ExecuteCommandBuffer(cmd);
            }
            finally { CommandBufferPool.Release(cmd); }
        }
        public override void OnCameraCleanup(CommandBuffer cmd) => Dispose();
        public void Dispose() { target?.Release(); target = null; if (texture != null) RenderTexture.ReleaseTemporary(texture); texture = null; }
    }
}
