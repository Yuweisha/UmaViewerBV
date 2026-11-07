using UnityEngine;

namespace Gallop.RenderPipeline
{
    /// <summary>
    /// Runtime image-effect state attached to a render camera.
    /// The recovered Gallop feature reads this parameter from CameraData.
    /// </summary>
    [DisallowMultipleComponent]
    public partial class CameraData : MonoBehaviour
    {
        // Original static constructor 0x51c9c20 and SetupCullingParameters
        // 0x5230db0. Zero camera override selects the game's global default.
        public static float GlobalMaxShadowDistance = 15f;
        public float MaxShadowDistance { get; set; }
        // Original CameraData +0xa4. LowResolutionCameraBase's factor setter
        // also publishes here; mirror delegates read this state directly.
        public float FieldOfViewFactor = 1f;

        public static float ResolveShadowDistance(float globalDistance, float cameraOverride,
            bool hasCameraData, bool renderShadows)
        {
            if (!hasCameraData) return globalDistance;
            if (!renderShadows) return 0f;
            return cameraOverride > 0f ? cameraOverride : globalDistance;
        }

        // Original CameraData texture/culling owners (0xd0, 0xe8, 0xf0, 0xf8).
        // They must be supplied by the camera resource owner, not inferred from
        // the source color or a similarly named texture.
        public uint RenderingLayerMask = 1;
        public bool IsMSAA;
        // Installed CameraData +0xd9 / +0x148: flare event549 for mirrors;
        // UI rendering is excluded by LensFlareFeature.AddRenderPasses.
        public bool IsMirrorRendering;
        public bool IsUIRendering;
        // Renderer-owned state recovered from GallopRenderer.Setup / culling.
        public bool IsClearDepth = true;
        public bool IsInvertCulling, IsDisableObjectCulling, IsDisableLightCulling;
        public bool IsSkipDrawObjectsPass, UseCameraOpaqueTexture;
        public float CameraOpaqueTextureSizeRate = 1f;
        public bool IsOpaqueRenderingForUICamera;
        public bool IsOverrideShadowResolution;
        public int OverrideShadowResolution = 1024;
        public int MirrorCasterQuality;
        public bool IsCreateAntialiasTexture, IsDelayReleaseAntiAliasingTexture;
        public int RenderingAntiAliasing = 1;
        public enum AntiAliasLevel { Auto = 0, None = 1 }
        public AntiAliasLevel RequestAntiAliasing;
        public bool ForceViewProjectionMatrix;
        public uint ShadowCasterRenderingLayerMask = 1;
        public RenderTexture InternalRenderingTexture, InternalRenderingDepthTexture;
        public Matrix4x4 ViewMatrix { get; internal set; }
        public Matrix4x4 ProjectionMatrix { get; internal set; }
        // Optional original single-target owner. Live does not enable it by default.
        public bool IsShadowMapOffset;
        public float ShadowMapHorizontalOffset, ShadowMapVerticalOffset;
        public Vector3[] ShadowMapOffsets = new Vector3[4];
        public float[] ShadowMapScales = { 1f, 1f, 1f, 1f };
        public RenderTexture ColorTextureMSAA;
        public RenderTexture DepthTexture;
        [SerializeField]
        private PostImageEffectFeature.Parameter _parameter;
        private bool _nativeFlareRegistrationAttempted;

        public PostImageEffectFeature.Parameter Parameter
        {
            get
            {
                if (_parameter == null)
                    _parameter = new PostImageEffectFeature.Parameter();
                return _parameter;
            }
            set => _parameter = value;
        }

        public void Initialize(bool _)
        {
            _ = false;
            MaxShadowDistance = 0f;
            if (_parameter == null)
                _parameter = new PostImageEffectFeature.Parameter();
            if (!_nativeFlareRegistrationAttempted)
            {
                _nativeFlareRegistrationAttempted = true;
                NativeLensFlareCameraRegistry.AddCamera(GetComponent<Camera>());
            }
        }

        public void UpdateImageEffectParameter()
        {
            // Native RVA 0x1a0da30; recomputed again before each render.
            ref var dof = ref Parameter.DofDiffuionBloomOverlay;
            dof.IsDisableDofTemporary = dof.IsEnableDofAutoDisable &&
                !(dof.DofFocalSize < dof.DofMaxFocalSize);
            dof.DecideDrawType(dof.IsEnableDof && !dof.IsDisableDofTemporary &&
                GallopImageEffect.UserDofEnabled);
        }

        private void Awake()
        {
            Initialize(false);
        }

        private void OnDestroy()
        {
            NativeLensFlareCameraRegistry.RemoveCamera(GetComponent<Camera>());
        }
    }
}
