using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gallop
{
    public sealed class PoolWater : MonoBehaviour
    {
        public bool disablePixelLights;
        public int textureSize = 64;
        public FilterMode renderTextureFilterMode = FilterMode.Bilinear;
        public int renderTextureantiAliasing = 1;
        public int renderTextureAnisoLevel;
        public float clipPlaneOffset = .07f;
        public LayerMask reflectLayers;
        public LayerMask refractLayers;
        public float waterPlaneOffsetY = -.3f;
        public float outsideWaterRefractBias;
        public float insideWaterRefractBias;
        public float outsideBumpSpecular;
        public float insideBumpSpecular;
        public Color _ClearColor;
        public Color _outsideFarColor;
        public Color _insideFarColor;
        public Vector4 waveSpeed;
        public float waveScale = .02f;
        public float distanceFadeByNear = 1f;
        public float distanceFadeByFar = 40f;
        public float distanceFadeByAlpha = 1f;

        private Camera captureCamera;
        private Gallop.RenderPipeline.CameraData captureData;
        private RenderTexture reflection, refraction;
        private Renderer surface;
        private Material[] originalMaterials, waterMaterials;

        public void RenderCutIn(Camera source, int reflectionMask, int refractionMask, float time)
        {
            if (!isActiveAndEnabled || !TryInitialize(source) || !surface.enabled ||
                !GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(source), surface.bounds))
                return;

            var planePosition = transform.position + Vector3.up * waterPlaneOffsetY;
            var normal = transform.up;
            bool inside = Vector3.Dot(source.transform.position - planePosition, normal) < 0f;
            if (inside)
                normal = -normal;
            var scales = new Vector4(waveScale, waveScale, waveScale * .4f, waveScale * .45f);
            var waves = Vector4.Scale(waveSpeed, scales);

            foreach (var material in waterMaterials)
            {
                if (!material)
                    continue;
                CoreUtils.SetKeyword(material, "WATER_INSIDE", inside);
                material.SetTexture("_ReflectionTex", reflection);
                material.SetTexture("_RefractionTex", refraction);
                material.SetFloat("_ReflectionBias", inside ? insideWaterRefractBias : outsideWaterRefractBias);
                material.SetFloat("_BumpSpecular", inside ? insideBumpSpecular : outsideBumpSpecular);
                material.SetColor("_WaterFarColor", inside ? _insideFarColor : _outsideFarColor);
                material.SetFloat("_DistanceAlphaFadeNear", distanceFadeByNear);
                material.SetFloat("_DistanceAlphaFadeFar", distanceFadeByFar);
                material.SetFloat("_DistanceAlphaFadeAlpha", distanceFadeByAlpha);
                material.SetVector("_WaveScale4", scales);
                material.SetVector("_WaveOffset", waves);
                material.SetFloat("_AppTime", time);
            }

            var previousTarget = RenderTexture.active;
            surface.enabled = false;
            try
            {
                if (inside && reflectionMask != 0)
                    RenderPass(source, reflection, reflectionMask, planePosition, normal, true);
                if (refractionMask != 0)
                    RenderPass(source, refraction, refractionMask, planePosition, normal, false);
            }
            finally
            {
                RenderTexture.active = previousTarget;
                surface.enabled = true;
            }
        }

        private bool TryInitialize(Camera source)
        {
            if (captureCamera)
                return true;
            if (!TryGetComponent(out surface))
                return false;
            originalMaterials = surface.sharedMaterials;
            waterMaterials = new Material[originalMaterials.Length];
            var slots = (Material[])originalMaterials.Clone();
            for (int i = 0; i < slots.Length; ++i)
            {
                if (slots[i] && slots[i].HasProperty("_ReflectionTex") && slots[i].HasProperty("_RefractionTex"))
                    slots[i] = waterMaterials[i] = new Material(slots[i]);
            }
            surface.sharedMaterials = slots;
            reflection = CreateTarget("Water reflection");
            refraction = CreateTarget("Water refraction");
            var go = new GameObject("CutIn water camera") { hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetParent(transform, false);
            captureCamera = go.AddComponent<Camera>();
            captureCamera.enabled = false;
            captureData = go.AddComponent<Gallop.RenderPipeline.CameraData>();
            captureData.IsClearDepth = true;
            var cameraData = captureCamera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = false;
            cameraData.renderShadows = source.GetUniversalAdditionalCameraData().renderShadows;
            cameraData.requiresDepthOption = CameraOverrideOption.Off;
            cameraData.requiresColorOption = CameraOverrideOption.Off;
            cameraData.SetRenderer(2);
            return true;
        }

        private RenderTexture CreateTarget(string label)
        {
            var desc = new RenderTextureDescriptor(Mathf.Max(1, textureSize), Mathf.Max(1, textureSize),
                RenderTextureFormat.ARGB32, 24);
            return new RenderTexture(desc)
            {
                name = label,
                filterMode = renderTextureFilterMode,
                anisoLevel = renderTextureAnisoLevel,
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        private void RenderPass(Camera source, RenderTexture target, int layers, Vector3 point, Vector3 normal, bool reflected)
        {
            var sourceTransform = source.transform;
            var position = sourceTransform.position;
            var forward = sourceTransform.forward;
            var up = sourceTransform.up;
            var view = source.worldToCameraMatrix;
            if (reflected)
            {
                float distance = Vector3.Dot(position - point, normal);
                position -= 2f * distance * normal;
                forward = Vector3.Reflect(forward, normal);
                up = Vector3.Reflect(up, normal);
                var mirror = Matrix4x4.identity;
                for (int row = 0; row < 3; ++row)
                {
                    for (int col = 0; col < 3; ++col)
                        mirror[row, col] -= 2f * normal[row] * normal[col];
                    mirror[row, 3] = 2f * Vector3.Dot(point, normal) * normal[row];
                }
                view *= mirror;
            }

            captureCamera.CopyFrom(source);
            captureCamera.enabled = false;
            captureCamera.targetTexture = target;
            captureCamera.rect = new Rect(0f, 0f, 1f, 1f);
            captureCamera.allowMSAA = false;
            captureCamera.clearFlags = CameraClearFlags.SolidColor;
            captureCamera.backgroundColor = _ClearColor;
            captureCamera.cullingMask = layers;
            captureData.IsInvertCulling = reflected;
            captureData.IsDisableLightCulling = disablePixelLights;
            captureCamera.useOcclusionCulling = false;
            captureCamera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward, up));
            captureCamera.worldToCameraMatrix = view;
            captureCamera.projectionMatrix = source.projectionMatrix;
            var clipPoint = view.MultiplyPoint(point + normal * clipPlaneOffset);
            var clipNormal = view.MultiplyVector(normal).normalized * (reflected ? 1f : -1f);
            var clip = new Vector4(clipNormal.x, clipNormal.y, clipNormal.z, -Vector3.Dot(clipPoint, clipNormal));
            captureCamera.projectionMatrix = captureCamera.CalculateObliqueMatrix(clip);
            captureCamera.ResetCullingMatrix();

            RenderTexture.active = target;
            GL.Clear(true, true, _ClearColor);
            captureCamera.Render();
        }

        private void OnDestroy()
        {
            if (surface && originalMaterials != null)
                surface.sharedMaterials = originalMaterials;
            if (waterMaterials != null)
                foreach (var material in waterMaterials)
                    if (material)
                        Destroy(material);
            if (captureCamera)
                Destroy(captureCamera.gameObject);
            if (reflection)
                Destroy(reflection);
            if (refraction)
                Destroy(refraction);
        }
    }
}
