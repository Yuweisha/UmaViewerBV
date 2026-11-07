using UnityEngine;

namespace Gallop
{
    public static class GraphicSettings
    {
        public static LiveDirtGlobalSettings DirtSettings { get; internal set; }
        // GraphicSettings -> GraphicModule -> BlurPanelManager owner bridge.
        // Empty until real UI panel owners are registered; never synthesize Live blur.
        public static readonly RenderPipeline.BlurPanelManager BlurPanels = new RenderPipeline.BlurPanelManager();
        // Original Graphics3DQuality values. Offline authoring uses the native
        // Live4K branch, including its deliberately smaller blur work targets.
        public enum Graphics3DQuality { None = 0, Light = 1, Normal = 2, Resolution4k = 3 }
        public static Graphics3DQuality Current3DQuality
        {
            get
            {
#if UNITY_EDITOR
                // Isolated all-song default-settings regression: normal image
                // quality with the original default Live4K option disabled.
                if(System.Environment.GetEnvironmentVariable("UMA_LIVE18_DEFAULT_OPTIONS")=="1")
                    return Graphics3DQuality.Normal;
                // Isolated comparison only; no game/user config is rewritten.
                if (System.Environment.GetEnvironmentVariable("UMA_LIVE18_LEGACY_NON4K") == "1")
                    return Graphics3DQuality.None;
#endif
                return Graphics3DQuality.Resolution4k;
            }
        }
        public static bool IsLive4KQuality => Current3DQuality == Graphics3DQuality.Resolution4k;

        // Original 2.32.0 IsUseImageEffect (0x4c33540): only Light (1)
        // bypasses the main post chain; None (0) still permits it.
        public static bool IsUseImageEffect() => UseImageEffectForQuality(Current3DQuality);
        internal static bool UseImageEffectForQuality(Graphics3DQuality quality) => quality != Graphics3DQuality.Light;

        // Native InitializeAfterShaderLoad 0x4af26c0 / SetEffectTimeScale
        // 0x4af46c0. The original effect shaders multiply their UV animation
        // clock by this global; leaving Unity's default zero freezes distortion.
        public static void InitializeAfterShaderLoad()
        {
            SetEffectTimeScale(1f);
            GraphicSettingsDirtRuntime.EnsureExists();
        }

        public static void SetEffectTimeScale(float timeScale)
        {
            Shader.SetGlobalFloat(ShaderManager.GetPropertyId(ShaderManager.PropertyId._GlobalTimeScale), timeScale);
        }

        //* 图像效果能力开关
        //* Original static effect flags; user-setting selection is a separate owner.
        public static bool IsGlobalFog;
        public static bool IsSunShaft;
        public static bool IsTiltShift;
        public static bool IsIndirectLightShaft;

        public static bool IsBloom;
        public static bool IsDOF;
        public static bool IsScreenOverlay;

        // cy拼错单词了,字段名就是 Diffution,不要改成 Diffusion
        public static bool IsDiffution;

        public static bool IsRadialBlur;
        public static bool IsFluctuation;
        public static bool IsLensDistortion;
        public static bool IsChromaticAberration;
        public static bool IsColorCorrection;
        public static bool IsColorGrading;
        public static bool IsToneCurve;
        public static bool IsExposure;
        public static bool IsTransmittedLight;
        public static bool IsRainSplash;

        static GraphicSettings()
        {
            // Native .cctor 0x4af5e50 writes all eighteen flags true.
            // Do not replace it with SetGameQualityImageEffect(true): that
            // method deliberately preserves ColorCorrection and RainSplash.
            IsGlobalFog=IsSunShaft=IsTiltShift=IsIndirectLightShaft=true;
            IsBloom=IsDOF=IsScreenOverlay=IsDiffution=IsRadialBlur=true;
            IsFluctuation=IsLensDistortion=IsChromaticAberration=true;
            IsColorCorrection=IsColorGrading=IsToneCurve=IsExposure=true;
            IsTransmittedLight=IsRainSplash=true;
        }

        public static void SetGameQualityImageEffect(bool isNormal)
        {
            // 无论isNormal是什么,都强制开启这两个
            IsScreenOverlay = true;
            IsRadialBlur = true;

            IsBloom = isNormal;
            IsDOF = isNormal;
            IsDiffution = isNormal;

            IsGlobalFog = isNormal;
            IsSunShaft = isNormal;
            IsTiltShift = isNormal;
            IsIndirectLightShaft = isNormal;

            IsFluctuation = isNormal;
            IsLensDistortion = isNormal;
            IsChromaticAberration = isNormal;

            IsColorGrading = isNormal;
            IsToneCurve = isNormal;
            IsExposure = isNormal;
            IsTransmittedLight = isNormal;


        }

        public enum LayerIndex
        {
            LayerDefault = 0,
            LayerCHAR = 1,
            LayerBG = 2,
            LayerEFFECT = 3,
            LayerWater = 4,
            LayerCircleProfile = 5,
            LayerIgnoreRaycast = 6,
            Layer3D = 7,
            LayerTransparentFX = 8,
            LayerTouchEffect = 9,
            LayerNO_VISIBLE = 10,
            LayerUI = 11,
            Layer3DUI = 12,
            LayerBackground3D_NotReflect = 13,
            LayerCharacter3D_NotReflect = 14,
            LayerCharacter3D_0 = 15,
            LayerCharacter3D_1 = 16,
            LayerCharaMini = 20
        }

        public static int GetLayer(LayerIndex layerIndex)
        {
            string layerName;
            switch (layerIndex)
            {
                case LayerIndex.LayerDefault: layerName = "Default"; break;
                case LayerIndex.LayerCHAR: layerName = "CHAR"; break;
                case LayerIndex.LayerBG: layerName = "BG"; break;
                case LayerIndex.LayerEFFECT: layerName = "EFFECT"; break;
                case LayerIndex.LayerWater: layerName = "Water"; break;
                case LayerIndex.LayerCircleProfile: layerName = "CircleProfile"; break;
                case LayerIndex.LayerIgnoreRaycast: layerName = "Ignore Raycast"; break;
                case LayerIndex.Layer3D: layerName = "3D"; break;
                case LayerIndex.LayerTransparentFX: layerName = "TransparentFX"; break;
                case LayerIndex.LayerTouchEffect: layerName = "TouchEffect"; break;
                case LayerIndex.LayerNO_VISIBLE: layerName = "NO_VISIBLE"; break;
                case LayerIndex.LayerUI: layerName = "UI"; break;
                case LayerIndex.Layer3DUI: layerName = "3D_UI"; break;
                case LayerIndex.LayerBackground3D_NotReflect: layerName = "Background3D_NotReflect"; break;
                case LayerIndex.LayerCharacter3D_NotReflect: layerName = "Character3D_NotReflect"; break;
                case LayerIndex.LayerCharacter3D_0: layerName = "Character3D_0"; break;
                case LayerIndex.LayerCharacter3D_1: layerName = "Character3D_1"; break;
                case LayerIndex.LayerCharaMini: layerName = "CharaMini"; break;
                default: return -1;
            }
            int layer = LayerMask.NameToLayer(layerName);
            if (layer < 0) Debug.LogWarning($"[GraphicSettings] Unity Layer does not exist: {layerName}");
            return layer;
        }

        public static int GetCullingLayer(LayerIndex layerIndex)
        {
            int layer = GetLayer(layerIndex);
            return layer < 0 ? 0 : 1 << layer;
        }
    }
}
