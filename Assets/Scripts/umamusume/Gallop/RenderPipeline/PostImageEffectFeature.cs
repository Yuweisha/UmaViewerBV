using System;
using System.Collections.Generic;
using System.Reflection;
using Gallop.ImageEffect;
using Gallop.Live;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gallop.RenderPipeline
{
    /// <summary>
    /// Recovered temporary chain with camera resolve before late main color604.
    /// Other reference passes are not implemented by this feature yet.
    /// </summary>
    public class PostImageEffectFeature : ScriptableRendererFeature
    {
        private const int INVALID_NAME_ID = -2;
        private const int REGISTER_PASS_SIZE = 32;
        private enum EffectMode
        {
            Disabled,
            ForegroundOnly,
            Simple,
            Full
        }

        // Original RegisterPass 0x535d48d..0x535d4eb. FilmRoll and BgBlur
        // are scheduled before these gates and are independent of this mode.
        private static EffectMode SelectEffectMode(bool useImageEffects, bool simpleMode, bool foregroundOnly)
        {
            if (foregroundOnly)
                return EffectMode.ForegroundOnly;
            if (!useImageEffects)
                return EffectMode.Disabled;
            return simpleMode ? EffectMode.Simple : EffectMode.Full;
        }

        private const string SOURCE_SETUP_POOL_NAME = "PostImageEffectFeature.SourceSetup";
        private const string FINAL_BLIT_POOL_NAME = "PostImageEffectFeature.FinalBlit";
        // Installed native post paths; unsupported slots remain in the integration ledger.
        private SourceSetupPass _sourceSetupPass;
        private NativeMainShadowReceiverPass _nativeMainShadowReceiverPass;

        private DofDiffusionBloomOverlayPass _dofDiffusionBloomOverlayPass;
        private FinalBlitPass _finalBlitPass;
        private RadialBlurPass _radialBlurPass;
        private SimpleColorCorrectionPass _colorCorrectionPass;
        private FinalBlitPass _colorEarlyResolvePass;
        private ExposureToneCurvePass _exposurePass, _toneCurvePass;
        private SunShaftsPass _sunShaftsPass;
        private IndirectLightShaftsPass _indirectLightShaftsPass;
        private MultiCameraCompositePass _multiCameraCompositePass;
        private MultiCameraFinalCompositePass _multiCameraFinalCompositePass;
        private LiveFadeSurfacePass _liveFadeSurfacePass;
        private LiveSubtitleBackdropPass _liveSubtitleBackdropPass;
        private ProjectorRenderPass _projectorPass;
        private NativeLensFlarePass _nativeLensFlarePass;
        private ImageEffectCallbackPass _prepareScreenMakeupPass;
        private ImageEffectCallbackPass _prepareLastScreenMakeupPass;
        private ImageEffectCallbackPass _afterImageEffectPass;
        private ColorGradingPass _colorGradingPass;
        private VortexPass _vortexPass;
        private ChromaticAberrationPass _chromaticAberrationPass;
        private LetterBoxPass _letterBoxPass;
        private LensDistortionPass _lensDistortionPass;
        private MonitorCameraCopyPass _monitorCameraCopyPass;
        private HatchingPass _hatchingPass;
        private TiltShiftPass _tiltShiftPass;
        private FluctuationPass _fluctuationPass;
        private BgBlurPass _bgBlurPass;
        private FilmRollPass _filmRollPass;
        private BlurOptimizePass _blurOptimizePass;
        private GlobalFogPass _globalFogPass;
        private AuraPass _auraPass;
        private TransmittedLightPass _transmittedLightPass;
        private RainSplashPass _rainSplashPass;
        private RenderTextureHandle _sourceRT;
        private List<ScriptableRenderPass> _registerPass;
        private bool _loggedEnqueue;
        private bool _loggedParameterMissing;
        private bool _loggedParameterDisabled;
        private bool _loggedSetupFailed;
        private bool _loggedBypass;
        private bool _shaderLoadRequested;
        private float _nextShaderLoadAttempt;
        // Missing shader resources cause Setup to fail without replacing camera color.
        // Disable this switch for an unprocessed A/B baseline.
        [SerializeField]
        private bool _enableRendering = true;
        // Unlike SetActive, this leaves the mandatory multi-camera composition passes scheduled.
        public bool EnableRendering { get => _enableRendering; set => _enableRendering = value; }

        // Active-camera publication alias, not storage shared by every camera.
        // GallopImageEffect / MultiCamera retain their own CameraData parameters.
        private static Parameter _runtimeParameter = new Parameter();
        public static Parameter RuntimeParameter
        {
            get
            {
                if (_runtimeParameter == null)
                    _runtimeParameter = new Parameter();
                return _runtimeParameter;
            }

            internal set => _runtimeParameter = value;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetRuntimeParameter()
        {
            _runtimeParameter = new Parameter();
        }

        public RenderTextureHandle SourceRT
        {
            get
            {
                return _sourceRT;
            }
        }

        // Pure scheduling policy, shared with regression tests. URP performs the stable
        // event sort; this does not change the existing DOF pass or its late camera writes.
        public struct ColorChainSchedule
        {
            public int FirstEvent, LastEvent;
            public bool EarlyResolve, FinalResolve;
        }

        public static ColorChainSchedule GetColorChainSchedule(bool dof, bool radial, bool color, RenderPassEvent effectEvent, int colorEvent = 550, bool earlyEffects = false)
        {
            bool existing = dof || radial;
            bool early = earlyEffects || (color && colorEvent <= 550);
            int first = existing ? (int)effectEvent : early ? 550 : colorEvent;
            int last = existing ? (int)effectEvent : 550;
            if (early)
            {
                first = System.Math.Min(first, 550);
                last = System.Math.Max(last, 550);
            }

            if (color)
                first = System.Math.Min(first, colorEvent);
            return new ColorChainSchedule
            {
                FirstEvent = first,
                LastEvent = last,
                EarlyResolve = early && existing && (int)effectEvent > 550,
                FinalResolve = radial || (early && (!existing || (int)effectEvent <= 550)) || (existing && (int)effectEvent <= 550)
            };
        }

        public override void Create()
        {
            Dispose();
            _loggedEnqueue = false;
            _loggedParameterMissing = false;
            _loggedParameterDisabled = false;
            _loggedSetupFailed = false;
            _loggedBypass = false;
            _shaderLoadRequested = false;
            _nextShaderLoadAttempt = 0f;
            RenderPassEvent passEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            _sourceSetupPass = new SourceSetupPass(this, passEvent);
            _afterImageEffectPass = new ImageEffectCallbackPass(passEvent, "AfterImageEffect");
            _dofDiffusionBloomOverlayPass = new DofDiffusionBloomOverlayPass(passEvent);
            _finalBlitPass = new FinalBlitPass(this, passEvent);
            _radialBlurPass = new RadialBlurPass(passEvent);
            _colorCorrectionPass = new SimpleColorCorrectionPass();
            _colorEarlyResolvePass = new FinalBlitPass(this, passEvent, "ColorEarlyResolve");
            _exposurePass = new ExposureToneCurvePass(false);
            _toneCurvePass = new ExposureToneCurvePass(true);
            _sunShaftsPass = new SunShaftsPass(passEvent);
            _indirectLightShaftsPass = new IndirectLightShaftsPass(passEvent);
            _multiCameraCompositePass = new MultiCameraCompositePass();
            _multiCameraFinalCompositePass = new MultiCameraFinalCompositePass();
            _liveFadeSurfacePass = new LiveFadeSurfacePass();
            _projectorPass = new ProjectorRenderPass();
            _nativeLensFlarePass = new NativeLensFlarePass(this);
            _prepareScreenMakeupPass = new ImageEffectCallbackPass(RenderPassEvent.BeforeRenderingPostProcessing);
            _prepareLastScreenMakeupPass = new ImageEffectCallbackPass(RenderPassEvent.BeforeRenderingPostProcessing, "PrepareLastScreenMakeup");
            _colorGradingPass = new ColorGradingPass();
            _vortexPass = new VortexPass();
            _chromaticAberrationPass = new ChromaticAberrationPass();
            _letterBoxPass = new LetterBoxPass();
            _lensDistortionPass = new LensDistortionPass();
            _monitorCameraCopyPass = new MonitorCameraCopyPass();
            _hatchingPass = new HatchingPass();
            _tiltShiftPass = new TiltShiftPass();
            _fluctuationPass = new FluctuationPass();
            _bgBlurPass = new BgBlurPass();
            _filmRollPass = new FilmRollPass();
            _blurOptimizePass = new BlurOptimizePass();
            _globalFogPass = new GlobalFogPass();
            _auraPass = new AuraPass();
            _transmittedLightPass = new TransmittedLightPass();
            _rainSplashPass = new RainSplashPass();
            _registerPass = new List<ScriptableRenderPass>(REGISTER_PASS_SIZE);
            _sourceRT = RenderTextureHandle.Make(new RenderTargetIdentifier(INVALID_NAME_ID), 0, 0);
            Debug.Log($"[RecoveredPostEffect] Create enabled={_enableRendering} " + $"shaderReady={ShaderManager.IsShaderBundleReady}");
            // 让 Dof Pass 直接使用同一套 CameraData 参数解析逻辑。
            DofDiffusionBloomOverlayPass.FeatureParameterResolver = ResolveFeatureParameter;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var titleDirector = Gallop.Live.Director.instance;
            if (titleDirector != null && renderingData.cameraData.camera == titleDirector.TitleRenderingCamera)
            {
                if (titleDirector.SubtitleSceneBackdrop != null)
                {
                    if (_liveSubtitleBackdropPass == null)
                        _liveSubtitleBackdropPass = new LiveSubtitleBackdropPass();
                    _liveSubtitleBackdropPass.Setup(titleDirector.SubtitleSceneBackdrop);
                    renderer.EnqueuePass(_liveSubtitleBackdropPass);
                }

                return;
            }

            if (_nativeMainShadowReceiverPass == null)
                _nativeMainShadowReceiverPass = new NativeMainShadowReceiverPass();
            renderer.EnqueuePass(_nativeMainShadowReceiverPass);

            Camera camera = renderingData.cameraData.camera;
            if (camera == null)
                return;

            var director = Gallop.Live.Director.instance;
            // The native feature is independent of optional post effects. UI is
            // excluded by CameraData; mirrors use549, other owned cameras600.
            if (Application.isPlaying && camera.cameraType == CameraType.Game && director != null)
            {
                camera.TryGetComponent<CameraData>(out var flareCameraData);
                if (flareCameraData == null && (director.IsPresentationCamera(camera) || camera.GetComponent<MultiCamera>() != null))
                    flareCameraData = camera.gameObject.AddComponent<CameraData>();
                _nativeLensFlarePass?.Enqueue(renderer, flareCameraData);
            }

            // Native ProjectorFeature draws stage projectors after opaque geometry,
            // independently of presentation-camera post-processing (including reflection/monitor views).
            if (Application.isPlaying && camera.cameraType == CameraType.Game && director != null && _projectorPass != null && _projectorPass.Setup(director.LightProjectors, director.MirrorBallProjectors))
            {
                renderer.EnqueuePass(_projectorPass);
            }

            if (_monitorCameraCopyPass != null && _monitorCameraCopyPass.Setup(director?.MonitorCaptureDriver, camera))
            {
                renderer.EnqueuePass(_monitorCameraCopyPass);
                return;
            }

            // The timeline parameter is authored for the Live presentation
            // cameras. Reflection/monitor cameras use different render targets
            // and depth ranges; running the same multi-RT DOF/Bloom chain on
            // them corrupts the reflection texture and can leak black holes or
            // a white veil into the main frame.
            // [REPORT §10.3-B1/§4.2] 原生按相机组逐台注册后处理（RegisterPass 走
            // camera stack，覆盖演示相机组），master7 原以 camera != MainRenderCamera
            // 的引用相等做门控：CameraSwitcher 切换瞬间旧相机当帧不注册、新相机
            // 重走 Setup/SourceRT 重建链，切换帧丢失一次后处理输出。改为对
            // Director 的演示相机组做成员判定（IsPresentationCamera），同帧内
            // 组内每台相机都注册整链，消除切换窗口。非 Game 类型和
            // Monitor相机由专用末尾复制Pass处理；原生并非按名字跳过。
            if (!Application.isPlaying || camera.cameraType != CameraType.Game || director == null)
                return;
            camera.TryGetComponent<MultiCamera>(out var multiCamera);
            camera.TryGetComponent<CameraData>(out var mirrorCameraData);
            // Mirror cameras are not presentation cameras, but native
            // CreateMirrorCamera enables BlurOptimized and native
            // AddRenderPasses still registers that single pass. Let only the
            // mirror camera through this gate; the narrow branch below returns
            // before the presentation post chain.
            if (multiCamera == null && !director.IsPresentationCamera(camera) && (mirrorCameraData == null || !mirrorCameraData.IsMirrorRendering))
                return;
            if (camera == director.FinalRenderCamera && _liveFadeSurfacePass != null)
                renderer.EnqueuePass(_liveFadeSurfacePass);
            // Native composition runs even when the optional image-effect chain is disabled.
            if (multiCamera != null)
            {
                if (_multiCameraCompositePass != null && _multiCameraCompositePass.Setup(multiCamera))
                    renderer.EnqueuePass(_multiCameraCompositePass);
            }
            else if (camera == director.MainRenderCamera && _multiCameraFinalCompositePass != null && _multiCameraFinalCompositePass.Setup(director.MultiCameraFinalComposite))
                renderer.EnqueuePass(_multiCameraFinalCompositePass);
            bool renderEnabled = _enableRendering;
            if (!renderEnabled)
            {
                if (!_loggedBypass)
                {
                    _loggedBypass = true;
                    Debug.Log("[RecoveredPostEffect] bypass enabled: camera color is preserved");
                }

                return;
            }

            // Renderer features are constructed while the pipeline is being
            // deserialized, before UmaViewerMain.Start() has loaded shader.a.
            // Request the bundle here as soon as a Live camera actually needs
            // the recovered chain. Setup below is retried on subsequent frames
            // and will create the materials after the synchronous load lands.
            if (!ShaderManager.IsShaderBundleReady && Time.realtimeSinceStartup >= _nextShaderLoadAttempt)
            {
                _nextShaderLoadAttempt = Time.realtimeSinceStartup + 1f;
                ShaderManager.LoadShaderBundle();
                if (!_shaderLoadRequested)
                {
                    _shaderLoadRequested = true;
                    Debug.Log("[RecoveredPostEffect] requested shader bundle load from render pass");
                }
            }

            CameraData cameraData;
            if (!camera.TryGetComponent(out cameraData))
                cameraData = camera.gameObject.AddComponent<CameraData>();
            // Bind this camera's owned state before deciding draw type.
            // Live auto-disable must never leak through the active-camera alias.
            var imageEffect = camera.GetComponent<GallopImageEffect>();
            // Native MirrorReflection owns a CameraData.ImageEffectParameter
            // instance on the mirror camera (CreateMirrorCamera writes its
            // BlurOptimized fields). Do not replace it with the presentation
            // camera's RuntimeParameter; that erased the mirror blur.
            var cameraParameter = cameraData.IsMirrorRendering ? cameraData.Parameter : multiCamera != null ? multiCamera.PostEffectParameter : imageEffect != null ? imageEffect.RenderParameter : RuntimeParameter;
            cameraData.Parameter = cameraParameter;
            cameraParameter.TargetCamera = camera;
            cameraData.UpdateImageEffectParameter();
            Parameter parameter = ResolveFeatureParameter(cameraData);
            if (parameter == null)
            {
                if (!_loggedParameterMissing)
                {
                    _loggedParameterMissing = true;
                    Debug.LogWarning($"[RecoveredPostEffect] skip camera={camera.name}: CameraData.Parameter missing");
                }

                return;
            }

            if (!parameter.IsEnable)
            {
                if (!_loggedParameterDisabled)
                {
                    _loggedParameterDisabled = true;
                    Debug.Log($"[RecoveredPostEffect] skip camera={camera.name}: feature parameter disabled");
                }

                return;
            }

            // Native mirror cameras do not run the presentation post chain, but
            // CreateMirrorCamera writes BlurOptimized and native AddRenderPasses
            // still schedules that single pass over the mirror target. The old
            // presentation-camera gate dropped it entirely.
            if (cameraData.IsMirrorRendering)
            {
                if (GraphicSettings.IsUseImageEffect() && !parameter.IsSimpleMode && parameter.BlurOptimize.IsEnable && _sourceSetupPass != null && _blurOptimizePass != null)
                {
                    _sourceSetupPass.renderPassEvent = parameter.BlurOptimize.RenderPassEvent;
                    _sourceSetupPass.Setup(cameraData);
                    renderer.EnqueuePass(_sourceSetupPass);
                    if (_blurOptimizePass.Setup(cameraData, this))
                        renderer.EnqueuePass(_blurOptimizePass);
                }

                return;
            }

            if (_registerPass == null)
            {
                _registerPass = new List<ScriptableRenderPass>(REGISTER_PASS_SIZE);
            }

            _registerPass.Clear();
            if (_sourceSetupPass == null || _dofDiffusionBloomOverlayPass == null || _finalBlitPass == null)
            {
                return;
            }

            // Native main camera OnApplyCallback copies Director's current sample
            // before color gates. Apply to every presentation camera, not merely
            // the active-camera alias from the previous cut; MultiCamera is separate.
            if (multiCamera == null)
                director.ApplyColorCorrectionParameter(ref parameter.ColorCorrection);
            // Original RegisterPass: FilmRoll/BgBlur precede the foreground-only
            // gate. Simple mode retains only ColorCorrection/ColorGrading.
            bool foregroundReady = parameter.ForegroundTexture != null;
            bool foregroundOnly = foregroundReady && parameter.IsForegroundTextureOnly;
            var effectMode = SelectEffectMode(GraphicSettings.IsUseImageEffect(), parameter.IsSimpleMode, foregroundOnly);
            bool fullEffects = effectMode == EffectMode.Full;
            bool colorEffects = effectMode == EffectMode.Full || effectMode == EffectMode.Simple;
            bool dofReady = fullEffects && _dofDiffusionBloomOverlayPass.Setup(cameraData, this);
            bool radialReady = fullEffects && _radialBlurPass != null && _radialBlurPass.Setup(parameter.RadialBlur, this);
            bool colorReady = colorEffects && _colorCorrectionPass != null && _colorCorrectionPass.Setup(parameter.ColorCorrection, this);
            bool exposureReady = fullEffects && _exposurePass != null && _exposurePass.Setup(parameter.ExposureParam, parameter.ToneCurveParam, this);
            bool toneReady = fullEffects && _toneCurvePass != null && _toneCurvePass.Setup(parameter.ExposureParam, parameter.ToneCurveParam, this);
            bool sunReady = fullEffects && _sunShaftsPass != null && _sunShaftsPass.Setup(parameter.SunShafts, this);
            bool shaftsReady = fullEffects && _indirectLightShaftsPass != null && _indirectLightShaftsPass.Setup(parameter.LightShafts, this);
            bool callbackReady = _prepareScreenMakeupPass.Setup(this, fullEffects ? parameter.OnPrepareScreenMakeup : null);
            bool lastCallbackReady = _prepareLastScreenMakeupPass.Setup(this, fullEffects ? parameter.OnPrepareLastScreenMakeup : null);
            bool afterCallbackReady = _afterImageEffectPass.Setup(this, parameter.OnAfterImageEffect);
            bool gradingReady = colorEffects && _colorGradingPass.Setup(cameraData, this);
            bool vortexReady = fullEffects && _vortexPass.Setup(cameraData, this);
            bool chromaticReady = fullEffects && _chromaticAberrationPass.Setup(cameraData, this);
            bool letterBoxReady = fullEffects && _letterBoxPass.Setup(cameraData, this);
            bool lensReady = fullEffects && _lensDistortionPass.Setup(cameraData, this);
            bool hatchingReady = fullEffects && _hatchingPass.Setup(cameraData, this), tiltReady = fullEffects && _tiltShiftPass.Setup(cameraData, this);
            bool fluctuationReady = fullEffects && _fluctuationPass.Setup(cameraData, this), bgBlurReady = _bgBlurPass.Setup(cameraData, this);
            bool filmReady = _filmRollPass.Setup(cameraData, this), blurReady = fullEffects && _blurOptimizePass.Setup(cameraData, this);
            bool fogReady = fullEffects && _globalFogPass.Setup(cameraData, this);
            bool auraReady = fullEffects && _auraPass.Setup(cameraData, this), transmittedReady = fullEffects && _transmittedLightPass.Setup(cameraData, this), rainReady = fullEffects && _rainSplashPass.Setup(cameraData, this);
            _auraPass.ConfigureInput(auraReady ? ScriptableRenderPassInput.Depth : ScriptableRenderPassInput.None);
            _transmittedLightPass.ConfigureInput(transmittedReady ? ScriptableRenderPassInput.Depth : ScriptableRenderPassInput.None);
            _rainSplashPass.ConfigureInput(rainReady ? ScriptableRenderPassInput.Depth : ScriptableRenderPassInput.None);
            _globalFogPass.ConfigureInput(fogReady ? ScriptableRenderPassInput.Depth : ScriptableRenderPassInput.None);
            bool correctionReady = auraReady || transmittedReady || rainReady || fogReady || filmReady || blurReady || colorReady || exposureReady || toneReady || gradingReady || vortexReady || chromaticReady || letterBoxReady || lensReady || hatchingReady || tiltReady || fluctuationReady || bgBlurReady;
            if (!dofReady && !radialReady && !correctionReady && !sunReady && !shaftsReady && !callbackReady && !lastCallbackReady && !afterCallbackReady && !foregroundReady && parameter.PreEffectTexture == null)
            {
                if (!_loggedSetupFailed)
                {
                    _loggedSetupFailed = true;
                    Debug.LogWarning($"[RecoveredPostEffect] setup failed camera={camera.name} " + $"type={parameter.DofDiffuionBloomOverlay.UseDofDiffusionBloomType} " + $"shaderReady={ShaderManager.IsShaderBundleReady}");
                }

                return;
            }

            if (!_loggedEnqueue)
            {
                _loggedEnqueue = true;
                Debug.Log($"[RecoveredPostEffect] enqueue camera={camera.name} " + $"type={RuntimeParameter.DofDiffuionBloomOverlay.UseDofDiffusionBloomType} " + $"shaderReady={ShaderManager.IsShaderBundleReady}");
            }

            // Explicitly request URP depth for CoC; do not rely on another feature
            // incidentally producing _CameraDepthTexture. Reset when leaving DOF.
            _dofDiffusionBloomOverlayPass.ConfigureInput(parameter.IsUseDepthTexture ? ScriptableRenderPassInput.Depth : ScriptableRenderPassInput.None);
            RenderPassEvent effectEvent = parameter.DofDiffuionBloomOverlay.RenderPassEvent;
            // Main color604 resolves itself. Flush any earlier temporaries before
            // flare600, rather than carrying a stale SourceRT across the flare draw.
            var schedule = GetColorChainSchedule(dofReady, radialReady, colorReady, effectEvent, (int)_colorCorrectionPass.renderPassEvent, exposureReady || toneReady || sunReady || shaftsReady || gradingReady || vortexReady || chromaticReady || letterBoxReady || lensReady || hatchingReady || tiltReady || fluctuationReady || bgBlurReady || filmReady || blurReady || fogReady || auraReady || transmittedReady || rainReady || foregroundReady);
            _sourceSetupPass.renderPassEvent = (RenderPassEvent)schedule.FirstEvent;
            if (callbackReady || lastCallbackReady || afterCallbackReady)
                _sourceSetupPass.renderPassEvent = (RenderPassEvent)System.Math.Min(schedule.FirstEvent, 550);
            _finalBlitPass.renderPassEvent = (RenderPassEvent)schedule.LastEvent;
            // 同一 RenderPassEvent 下按 Enqueue 顺序执行。
            _sourceSetupPass.Setup(cameraData);
            _registerPass.Add(_sourceSetupPass);
            if (filmReady)
                _registerPass.Add(_filmRollPass);
            if (bgBlurReady)
                _registerPass.Add(_bgBlurPass);
            if (auraReady)
                _registerPass.Add(_auraPass);
            if (blurReady)
                _registerPass.Add(_blurOptimizePass);
            if (fogReady)
                _registerPass.Add(_globalFogPass);
            if (sunReady)
                _registerPass.Add(_sunShaftsPass);
            if (transmittedReady)
                _registerPass.Add(_transmittedLightPass);
            if (shaftsReady)
                _registerPass.Add(_indirectLightShaftsPass);
            if (vortexReady)
                _registerPass.Add(_vortexPass);
            if (callbackReady)
                _registerPass.Add(_prepareScreenMakeupPass);
            if (dofReady)
                _registerPass.Add(_dofDiffusionBloomOverlayPass);
            if (tiltReady)
                _registerPass.Add(_tiltShiftPass);
            if (hatchingReady)
                _registerPass.Add(_hatchingPass);
            if (radialReady)
            {
                _radialBlurPass.renderPassEvent = effectEvent;
                _radialBlurPass.ConfigureInput(parameter.RadialBlur.IsEnabledDepth ? ScriptableRenderPassInput.Depth : ScriptableRenderPassInput.None);
                _registerPass.Add(_radialBlurPass);
            }

            if (fluctuationReady)
                _registerPass.Add(_fluctuationPass);
            if (lensReady)
                _registerPass.Add(_lensDistortionPass);
            if (chromaticReady)
                _registerPass.Add(_chromaticAberrationPass);
            if (toneReady)
                _registerPass.Add(_toneCurvePass);
            if (exposureReady)
                _registerPass.Add(_exposurePass);
            if (colorReady)
            {
                _registerPass.Add(_colorCorrectionPass);
            }

            if (lastCallbackReady)
                _registerPass.Add(_prepareLastScreenMakeupPass);
            if (gradingReady)
                _registerPass.Add(_colorGradingPass);
            if (letterBoxReady)
                _registerPass.Add(_letterBoxPass);
            if (rainReady)
                _registerPass.Add(_rainSplashPass);
            // All native event550 corrections must resolve before a late DOF draw.
            if (schedule.EarlyResolve)
            {
                _registerPass.Add(_colorEarlyResolvePass);
            }

            // Radial always hands its temporary result back to this feature.
            // DOF alone writes directly to camera color for events after 550.
            if (schedule.FinalResolve)
            {
                _registerPass.Add(_finalBlitPass);
            }

            if (afterCallbackReady)
                _registerPass.Add(_afterImageEffectPass);
            for (int i = 0; i < _registerPass.Count; i++)
                renderer.EnqueuePass(_registerPass[i]);
        // Read-only runtime evidence: this records the actual URP descriptor
        // and scheduled pass count without changing the render chain.
        }

        /// <summary>
        /// 将当前源设置为相机颜色目标
        /// 这替代原版SetSourceRTPass对 Dof-only 链路的作用
        /// </summary>
        public void ResetSourceRT(CommandBuffer cmd, ref RenderingData renderingData)
        {
            ScriptableRenderer renderer = renderingData.cameraData.renderer;
            if (renderer == null)
                return;
            RTHandle cameraColorHandle = renderer.cameraColorTargetHandle;
            if (cameraColorHandle == null)
                return;
            RenderTargetIdentifier cameraColorTarget = cameraColorHandle.nameID;
            RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;
            if (cmd != null && !_sourceRT.RtId.Equals(cameraColorTarget) && _sourceRT.NameId != INVALID_NAME_ID && _sourceRT.NameId != 0)
            {
                cmd.ReleaseTemporaryRT(_sourceRT.NameId);
            }

            _sourceRT = RenderTextureHandle.Make(cameraColorTarget, descriptor.width, descriptor.height);
        }

        /// <summary>
        /// DofDiffusionBloomOverlayPass.Execute 在完成后调用此函数，
        /// 把输出临时 RT 交回 Feature。
        /// </summary>
        public void SetSourceRT(RenderTextureHandle sourceHandle, CommandBuffer cmd, ref RenderingData renderingData)
        {
            if (_sourceRT.NameId == sourceHandle.NameId)
                return;
            ReleaseCurrentTemporaryRT(cmd, ref renderingData);
            _sourceRT = sourceHandle;
        }

        private void ReleaseCurrentTemporaryRT(CommandBuffer cmd, ref RenderingData renderingData)
        {
            ScriptableRenderer renderer = renderingData.cameraData.renderer;
            if (renderer == null)
                return;
            RTHandle cameraColorHandle = renderer.cameraColorTargetHandle;
            if (cameraColorHandle == null)
                return;
            RenderTargetIdentifier cameraColorTarget = cameraColorHandle.nameID;
            if (!_sourceRT.RtId.Equals(cameraColorTarget) && _sourceRT.NameId != INVALID_NAME_ID && _sourceRT.NameId != 0 && cmd != null)
            {
                cmd.ReleaseTemporaryRT(_sourceRT.NameId);
            }
        }

        protected override void Dispose(bool disposing)
        {
            _liveFadeSurfacePass?.Dispose();
            _liveFadeSurfacePass = null;
            _nativeLensFlarePass?.Dispose();
            _nativeLensFlarePass = null;
            _prepareScreenMakeupPass?.Dispose();
            _prepareScreenMakeupPass = null;
            _prepareLastScreenMakeupPass?.Dispose();
            _prepareLastScreenMakeupPass = null;
            _afterImageEffectPass?.Dispose();
            _afterImageEffectPass = null;
            _colorGradingPass?.Dispose();
            _colorGradingPass = null;
            _vortexPass?.Dispose();
            _vortexPass = null;
            _hatchingPass?.Dispose();
            _hatchingPass = null;
            _tiltShiftPass?.Dispose();
            _tiltShiftPass = null;
            _fluctuationPass?.Dispose();
            _fluctuationPass = null;
            _bgBlurPass?.Dispose();
            _bgBlurPass = null;
            _globalFogPass?.Dispose();
            _globalFogPass = null;
            _auraPass?.Dispose();
            _auraPass = null;
            _transmittedLightPass?.Dispose();
            _transmittedLightPass = null;
            _rainSplashPass?.Dispose();
            _rainSplashPass = null;
            _filmRollPass?.Dispose();
            _filmRollPass = null;
            _blurOptimizePass?.Dispose();
            _blurOptimizePass = null;
            _chromaticAberrationPass?.Dispose();
            _chromaticAberrationPass = null;
            _letterBoxPass?.Dispose();
            _letterBoxPass = null;
            _lensDistortionPass?.Dispose();
            _lensDistortionPass = null;
            _registerPass = null;
            _radialBlurPass?.Dispose();
            _radialBlurPass = null;
            _colorCorrectionPass?.Dispose();
            _colorCorrectionPass = null;
            _colorEarlyResolvePass = null;
            _exposurePass?.Dispose();
            _toneCurvePass?.Dispose();
            _exposurePass = _toneCurvePass = null;
            _sunShaftsPass?.Dispose();
            _indirectLightShaftsPass?.Dispose();
            _sunShaftsPass = null;
            _indirectLightShaftsPass = null;
            _multiCameraCompositePass = null;
            _multiCameraFinalCompositePass = null;
            if (_dofDiffusionBloomOverlayPass != null)
            {
                _dofDiffusionBloomOverlayPass.Dispose();
                _dofDiffusionBloomOverlayPass = null;
            }

            _sourceSetupPass = null;
            _finalBlitPass = null;
            base.Dispose(disposing);
        }

        /// <summary>
        /// CameraData 原生偏移 +0x150 对应的 Parameter 解析。
        /// 字段真实名称确定后，可以直接替换为明确字段访问。
        /// </summary>
        private static Parameter ResolveFeatureParameter(CameraData cameraData)
        {
            if (cameraData == null)
                return null;
            object instance = cameraData;
            Type type = instance.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            while (type != null)
            {
                FieldInfo[] fields = type.GetFields(flags);
                for (int i = 0; i < fields.Length; i++)
                {
                    if (typeof(Parameter).IsAssignableFrom(fields[i].FieldType))
                    {
                        return fields[i].GetValue(instance) as Parameter;
                    }
                }

                PropertyInfo[] properties = type.GetProperties(flags);
                for (int i = 0; i < properties.Length; i++)
                {
                    PropertyInfo property = properties[i];
                    if (!property.CanRead || property.GetIndexParameters().Length != 0)
                    {
                        continue;
                    }

                    if (!typeof(Parameter).IsAssignableFrom(property.PropertyType))
                    {
                        continue;
                    }

                    try
                    {
                        return property.GetValue(instance, null) as Parameter;
                    }
                    catch
                    {
                    // 继续搜索基类中的参数字段。
                    }
                }

                type = type.BaseType;
            }

            return null;
        }

        /// <summary>
        /// 在 Dof Pass 前，把 Feature.SourceRT 指向 Camera Color。
        /// </summary>
        private sealed class SourceSetupPass : ScriptableRenderPass
        {
            private readonly PostImageEffectFeature _owner;
            private CameraData _cameraData;
            private static readonly int ForegroundTarget = Shader.PropertyToID("SetSourceRTPass_RT0");
            public SourceSetupPass(PostImageEffectFeature owner, RenderPassEvent passEvent)
            {
                _owner = owner;
                renderPassEvent = passEvent;
            }

            public void Setup(CameraData cameraData)
            {
                _cameraData = cameraData;
                // Keep the aggregate request on a pass that is actually queued,
                // including chains such as Vortex with no DOF pass to carry it.
                ConfigureInput(cameraData.Parameter.IsUseDepthTexture ? ScriptableRenderPassInput.Depth : ScriptableRenderPassInput.None);
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (_owner == null)
                    return;
                CommandBuffer cmd = CommandBufferPool.Get(SOURCE_SETUP_POOL_NAME);
                try
                {
                    Parameter parameter = _cameraData.Parameter;
                    RenderUtils.SetZBufferParam(cmd, parameter.TargetCamera, true);
                    _owner.ResetSourceRT(cmd, ref renderingData);
                    if (parameter.PreEffectTexture != null)
                    {
                        cmd.Blit(_owner.SourceRT.RtId, parameter.PreEffectTexture);
                        context.ExecuteCommandBuffer(cmd);
                        cmd.Clear();
                    }

                    if (parameter.ForegroundTexture != null)
                    {
                        var descriptor = renderingData.cameraData.cameraTargetDescriptor;
                        var foreground = RenderTextureHandle.Make(ForegroundTarget, descriptor.width, descriptor.height);
                        foreground.GetTemporaryRT(cmd, FilterMode.Bilinear);
                        cmd.Blit(parameter.ForegroundTexture, foreground.RtId);
                        _owner.SetSourceRT(foreground, cmd, ref renderingData);
                    }

                    context.ExecuteCommandBuffer(cmd);
                }
                finally
                {
                    CommandBufferPool.Release(cmd);
                }
            }
        }

        /// <summary>
        /// Dof Pass 在 550 或更早执行时，只更新 Feature.SourceRT。
        /// 此 Pass 将结果写回 Camera Color，并释放 Dof 临时 RT。
        /// </summary>
        private sealed class FinalBlitPass : ScriptableRenderPass
        {
            private readonly PostImageEffectFeature _owner;
            private readonly string _traceStep;
            public FinalBlitPass(PostImageEffectFeature owner, RenderPassEvent passEvent, string traceStep = "FinalBlit")
            {
                _owner = owner;
                _traceStep = traceStep;
                renderPassEvent = passEvent;
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (_owner == null)
                    return;
                RenderTextureHandle source = _owner.SourceRT;
                if (source.NameId == INVALID_NAME_ID)
                    return;
                ScriptableRenderer renderer = renderingData.cameraData.renderer;
                if (renderer == null)
                    return;
                RTHandle cameraColorHandle = renderer.cameraColorTargetHandle;
                if (cameraColorHandle == null)
                    return;
                RenderTargetIdentifier cameraColorTarget = cameraColorHandle.nameID;
                CommandBuffer cmd = CommandBufferPool.Get(FINAL_BLIT_POOL_NAME);
                try
                {
                    if (!source.RtId.Equals(cameraColorTarget))
                    {
                        cmd.Blit(source.RtId, cameraColorTarget);
                    }

                    // Blit 后释放 Dof 输出临时 RT,并把 SourceRT 恢复为 Camera Color
                    _owner.ResetSourceRT(cmd, ref renderingData);
                    context.ExecuteCommandBuffer(cmd);
                }
                finally
                {
                    CommandBufferPool.Release(cmd);
                }
            }
        }

        public class Parameter
        {
            private Camera _targetCamera;
            private CameraData _targetCameraData;
            // Native PostImageEffectFeature.Parameter .ctor 0x535aa50
            // writes byte +0x20 = 1 before copying sub-parameters.
            public bool IsEnable = true;
            public ImageEffectCallbackPass.ImageEffectCallback OnPrepareScreenMakeup;
            public ImageEffectCallbackPass.ImageEffectCallback OnPrepareLastScreenMakeup;
            public ImageEffectCallbackPass.ImageEffectCallback OnAfterImageEffect;
            public bool IsSimpleMode;
            public Texture ForegroundTexture;
            public bool IsForegroundTextureOnly;
            public RenderTexture PreEffectTexture;
            public ColorGradingPass.Parameter ColorGrading = ColorGradingPass.Parameter.Default();
            public VortexPass.Parameter Vortex = VortexPass.Parameter.Default();
            public ChromaticAberrationPass.Parameter ChromaticAberration = ChromaticAberrationPass.Parameter.Default();
            public LetterBoxPass.Parameter LetterBox = LetterBoxPass.Parameter.Default();
            public LensDistortionPass.Parameter LensDistortion = LensDistortionPass.Parameter.Default();
            public HatchingPass.Parameter Hatching = HatchingPass.Parameter.Default();
            public TiltShiftPass.Parameter TiltShift = TiltShiftPass.Parameter.Default();
            public FluctuationPass.Parameter Fluctuation = FluctuationPass.Parameter.Default();
            public BgBlurPass.Parameter BgBlur = BgBlurPass.Parameter.Default();
            public FilmRollPass.Parameter FilmRoll = FilmRollPass.Parameter.Default();
            public BlurOptimizePass.Parameter BlurOptimize = BlurOptimizePass.Parameter.Default();
            public GlobalFogPass.Parameter GlobalFog = GlobalFogPass.Parameter.Default();
            public AuraPass.Parameter Aura = AuraPass.Parameter.Default();
            public TransmittedLightPass.Parameter TransmittedLight = TransmittedLightPass.Parameter.Default();
            public RainSplashPass.Parameter RainSplash = RainSplashPass.Parameter.Default();
            // DofDiffusionBloomOverlayPass 实际使用的参数。
            public DofDiffusionBloomOverlayPass.Parameter DofDiffuionBloomOverlay;
            public RadialBlurPass.Parameter RadialBlur = RadialBlurPass.Parameter.Default();
            public SimpleColorCorrectionPass.Parameter ColorCorrection;
            public SunShaftsPass.Parameter SunShafts = SunShaftsPass.Parameter.Default();
            public IndirectLightShaftsPass.Parameter LightShafts;
            // Native ToneCurve/Exposure parameter blocks consumed at event550.
            public Gallop.ImageEffect.ExposureParam ExposureParam = new Gallop.ImageEffect.ExposureParam();
            public Gallop.ImageEffect.ToneCurveParam ToneCurveParam = new Gallop.ImageEffect.ToneCurveParam();
            public bool IsRequestDepthPass;
            public Camera TargetCamera
            {
                get
                {
                    return _targetCamera;
                }

                set
                {
                    _targetCamera = value;
                    DofDiffuionBloomOverlay.TargetCamera = value;
                    TransmittedLight.TargetCamera = value;
                    RainSplash.TargetCamera = value;
                    if (_targetCamera == null)
                        return;
                    if (!_targetCamera.TryGetComponent(out _targetCameraData))
                    {
                        _targetCameraData = _targetCamera.gameObject.AddComponent<CameraData>();
                        InvokeCameraDataInitialize(_targetCameraData);
                    }

                    TransmittedLight.TargetCameraData = _targetCameraData;
                    RainSplash.TargetCameraData = _targetCameraData;
                }
            }

            public bool IsUseDepthTexture
            {
                get
                {
                    // The recovered IsDepthTexture property represents the original
                    // overlay/depth-request contract, not URP's complete DOF input needs.
                    // Original 2.32.0 aggregate (0x535b3a0). A radial effect
                    // requests depth even when its depth mask is disabled;
                    // a DOF mode alone does not. Rain has its own pass input.
                    // The current simple color correction supports mode 0,
                    // so the original mode-1 depth correction is not present.
                    return DofDiffuionBloomOverlay.IsDepthTexture || (SunShafts.IsEnable && GraphicSettings.IsSunShaft) || TransmittedLight.IsEnable || GlobalFog.IsUseDepthTexture || (RadialBlur.Type != 0 && GraphicSettings.IsRadialBlur) || Fluctuation.IsDepthTexture || (ToneCurveParam.IsEnable && GraphicSettings.IsToneCurve) || (ExposureParam.IsEnable && GraphicSettings.IsExposure) || Vortex.IsEnable || Aura.IsValidity;
                }
            }

            public Parameter()
            {
                IsEnable = true;
                DofDiffuionBloomOverlay = DofDiffusionBloomOverlayPass.Parameter.Default();
                IsRequestDepthPass = true;
            }

            public void ShallowCopyFrom(Parameter src)
            {
                if (src == null)
                    throw new ArgumentNullException(nameof(src));
                ShallowCopyParameters(src);
                OnPrepareScreenMakeup = src.OnPrepareScreenMakeup;
                OnPrepareLastScreenMakeup = src.OnPrepareLastScreenMakeup;
                OnAfterImageEffect = src.OnAfterImageEffect;
                IsSimpleMode = src.IsSimpleMode;
                ForegroundTexture = src.ForegroundTexture;
                IsForegroundTextureOnly = src.IsForegroundTextureOnly;
                PreEffectTexture = src.PreEffectTexture;
                IsRequestDepthPass = src.IsRequestDepthPass;
            }

            public void ShallowCopyParameters(Parameter src)
            {
                if (src == null)
                    throw new ArgumentNullException(nameof(src));
                DofDiffuionBloomOverlay = src.DofDiffuionBloomOverlay;
                RadialBlur = src.RadialBlur;
                ColorCorrection = src.ColorCorrection;
                SunShafts = src.SunShafts;
                LightShafts = src.LightShafts;
                ColorGrading = src.ColorGrading;
                Vortex = src.Vortex;
                ChromaticAberration = src.ChromaticAberration;
                LetterBox = src.LetterBox;
                LensDistortion = src.LensDistortion;
                Hatching = src.Hatching;
                TiltShift = src.TiltShift;
                Fluctuation = src.Fluctuation;
                BgBlur = src.BgBlur;
                GlobalFog = src.GlobalFog;
                FilmRoll = src.FilmRoll;
                BlurOptimize = src.BlurOptimize;
                Aura = src.Aura;
                TransmittedLight = src.TransmittedLight;
                RainSplash = src.RainSplash;
                ExposureParam.Setup(src.ExposureParam);
                ToneCurveParam.Setup(src.ToneCurveParam);
            }

            public void CopyFromGallopImageEffect(GallopImageEffectParameter imageEffectParam)
            {
                if (imageEffectParam == null)
                {
                    throw new ArgumentNullException(nameof(imageEffectParam));
                }

                DofDiffuionBloomOverlay.Setup(imageEffectParam.DofDiffusionBloomOverlayParam);
            }

            private static void InvokeCameraDataInitialize(CameraData cameraData)
            {
                if (cameraData == null)
                    return;
                MethodInfo[] methods = cameraData.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                for (int i = 0; i < methods.Length; i++)
                {
                    MethodInfo method = methods[i];
                    if (method.Name != "Initialize")
                        continue;
                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length != 1)
                        continue;
                    Type parameterType = parameters[0].ParameterType;
                    if (parameterType == typeof(bool))
                    {
                        method.Invoke(cameraData, new object[] { false });
                        return;
                    }

                    if (parameterType == typeof(int))
                    {
                        method.Invoke(cameraData, new object[] { 0 });
                        return;
                    }
                }
            }
        }
    }
}
