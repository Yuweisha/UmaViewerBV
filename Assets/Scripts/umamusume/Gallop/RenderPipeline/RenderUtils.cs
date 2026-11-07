using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gallop.RenderPipeline
{
    public static class RenderUtils
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static float Scalar(float value) => BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(value));
        // Original 2.32.0 SetZBufferParam (0x5362bd0). Keep the scalar
        // operation order: the reversed form is not algebraically reduced.
        public static Vector4 GetZBufferParams(float nearClip, float farClip, bool needReverse)
        {
            float near = nearClip < 0.00001f ? 0.00001f : nearClip;
            float far = farClip < 0.00001f ? 0.00001f : farClip;
            float inverseFar = Scalar(1f / far);
            float inverseNear = Scalar(1f / near);
            float x = Scalar(1f - Scalar(farClip * inverseNear));
            float y = Scalar(farClip * inverseNear);
            float z = Scalar(x * inverseFar);
            float w = Scalar(y * inverseFar);
            if (needReverse)
            {
                y = Scalar(y + x);
                x = -x;
                w = Scalar(w + z);
                z = -z;
            }
            return new Vector4(x, y, z, w);
        }

        public static void SetZBufferParam(CommandBuffer cmd, Camera camera, bool needReverse)
        {
            if (camera == null) return;
            cmd.SetGlobalVector(Shader.PropertyToID("_ZBufferParams"),
                GetZBufferParams(camera.nearClipPlane, camera.farClipPlane, needReverse));
        }

        // Native 0x51daf90: memoryless is cleared before querying support.
        public static bool NeedsExplicitMsaaResolve(int samples, bool autoResolve, int textureSupport)
            => samples > 1 && !autoResolve && textureSupport != 0;

        public static void SetMSAARenderTextureDescriptor(ref RenderTextureDescriptor descriptor)
        {
            descriptor.memoryless = RenderTextureMemoryless.None;
            descriptor.msaaSamples = SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor);
            descriptor.bindMS = NeedsExplicitMsaaResolve(descriptor.msaaSamples,
                SystemInfo.supportsMultisampleAutoResolve, SystemInfo.supportsMultisampledTextures);
        }

        public static Material GetMaterial(Renderer renderer)
        {
            if (renderer == null)
                return null;

            Material sharedMaterial = renderer.sharedMaterial;
            if (sharedMaterial == null)
                return null;

            string originalName = sharedMaterial.name;

            Material material = renderer.material;
            if (material == null)
                return null;

            material.name = originalName;
            return material;
        }

        // Original RenderUtils.GetMaterialArray 0x51da430: read sharedMaterials
        // first, then the instanced materials array, restore each instance name
        // from the matching shared slot, and return the instanced array.
        // A null sharedMaterials/materials array returns null; null individual
        // slots are skipped without touching the name.
        public static Material[] GetMaterialArray(Renderer renderer)
        {
            if (renderer == null)
                return null;

            Material[] sharedMaterials = renderer.sharedMaterials;
            if (sharedMaterials == null)
                return null;

            Material[] materials = renderer.materials;
            if (materials == null)
                return null;

            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material == null)
                    continue;
                Material sharedMaterial = sharedMaterials[i];
                if (sharedMaterial == null)
                    continue;
                material.name = sharedMaterial.name;
            }

            return materials;
        }

        public static void CopyMaterial(
            Material source,
            Material destination,
            bool nameReplace = true)
        {
            if (source == null || destination == null)
                return;

            if (nameReplace)
                destination.name = source.name;

            if (destination.shader != source.shader)
                destination.shader = source.shader;

            destination.CopyPropertiesFromMaterial(source);
        }

        public static void Destroy(ref Material obj)
        {
            CoreUtils.Destroy(obj);
            obj = null;
        }
    }
}
