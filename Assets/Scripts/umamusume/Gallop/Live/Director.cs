using Gallop.Live.Cutt;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using Gallop.ImageEffect;

namespace Gallop.Live
{
    public partial class Director : MonoBehaviour
    {
        private static Director _instance = null;
        public LiveTimelineControl _liveTimelineControl; //Edited to public
        [SerializeField]
        public float _liveCurrentTime; //Edited to public
        private LiveTimeController _liveTimeController;
        private float _livePrevTime;
        private readonly RandomTable<float> _randomTable = new RandomTable<float>();
        internal float GetBackgroundRandomValue(int index) => _randomTable[index];
        private readonly LiveModelRenderQueue _modelRenderQueue = new LiveModelRenderQueue();
        public LiveModelRenderQueue.Observation[] ObserveModelRenderQueues() => _modelRenderQueue.Observe();
        private bool _modelQueueCallbacksBound;
        // Native Director.Start 0x4e3ab80 owns the separate Blink ratio table.
        // Keep it distinct from the per-frame background-color RandomTable.
        public RandomRatioTable BlinkRandomRatios { get; } = new RandomRatioTable();

        private readonly LiveFadeController _fadeController = new LiveFadeController();
        private sealed class RenderFadeSurface : ILiveFadeSurface
        {
            public bool IsAlive => true;
            public Color Color { get; set; }
        }

        private readonly RenderFadeSurface _renderFadeSurface = new RenderFadeSurface();
        public Color PresentationFadeColor => _renderFadeSurface.Color;

        private void OnUpdateFade(Color color) => _fadeController.SetColor(color);
        public void InitializeLiveFadeSurfaces(ILiveFadeSurface landscape, ILiveFadeSurface portrait, ILiveFadeSurface fullPortrait) => _fadeController.Initialize(landscape, portrait, fullPortrait);
        internal void InitializePlaybackClock()
        {
            InitializeRandomTables();
            PreparePlaybackClock();
            _liveTimeController.StartCalcElapsedTime();
        }

        private void PreparePlaybackClock()
        {
            _liveTimeController = new LiveTimeController(new DirectorLiveTimeSource(this));
            _liveTimeController.Initialize();
        }

        private void InitializeRandomTables()
        {
            if (_randomTable.isInitialized)
                return;
            // Original Director.Start builds the background table before the
            // independent Blink table. Seeking/restarting only the clock must
            // not rebuild either table or consume another random history.
            _randomTable.Build(16, 256, (bucket, stack) => LiveRandomBoundary.Value());
            BlinkRandomRatios.Generate(10, 64, 0);
        }

        internal void AdvancePlaybackClock(float deltaTime)
        {
            if (_liveTimeController == null)
                InitializePlaybackClock();
            LiveDirectorTimelineDispatch.AdvanceClock(ref _liveTimeController, ref _liveCurrentTime, ref _livePrevTime, deltaTime);
            _randomTable.Update();
            _fadeController.AlterUpdate(_liveCurrentTime);
        }

        private void SetPlaybackClockTime(float time)
        {
            if (_liveTimeController == null)
                InitializePlaybackClock();
            _liveTimeController.SetCurrentTime(time);
        }

        public bool _isLiveSetup; //Edit to pulic
        public StageController _stageController; //Edited to public
        private StageMonitorDriver _monitorResourceOwner;
        private StageMonitorCameraDriver _monitorCameraDriver;
        public RenderTexture[] MonitorTextureArray { get; private set; }
        internal StageMonitorCameraDriver MonitorCaptureDriver => _monitorCameraDriver;
        public Vector2Int MonitorVirtualResolution => LiveMonitorResolution.CurrentVirtualResolution();

        internal void EnsureMonitorResources(StageMonitorDriver owner, StageMonitorCameraDriver cameras, LiveTimelineMonitorCameraSettings settings)
        {
            float rate = settings != null && settings.IsEnabledTextureWidthRate ? LiveMonitorResolution.ClampWidthRate(settings.TextureWidthRate) : 0.25f;
            var size = LiveMonitorResolution.TextureSize(MonitorVirtualResolution, rate, LiveQualitySettings.IsMonitorCamera);
            // Each authored position group owns an independent MonitorCamera.
            int count = GetAuthoredMonitorCameraCount();
            if (count == 0)
                return;
            if (_monitorResourceOwner != owner || _monitorCameraDriver != cameras || cameras.Count != count || MonitorTextureArray == null || MonitorTextureArray.Length != count || MonitorTextureArray[0] == null || MonitorTextureArray[0].width != size.x || MonitorTextureArray[0].height != size.y)
                RemakeMonitorResources(owner, cameras, size);
        }

        private int GetAuthoredMonitorCameraCount()
        {
            int count = 0;
            var sheets = _liveTimelineControl != null ? _liveTimelineControl.data?.worksheetList : null;
            if (sheets != null)
                foreach (var sheet in sheets)
                    if (sheet?.monitorCameraPosKeys != null)
                        count = Mathf.Max(count, sheet.monitorCameraPosKeys.Count);
            return count;
        }

        // Native RemakeMonitorTexture: private cameras first, shared allocation
        // second, then borrow targets and publish the new array to image effects.
        internal void RemakeMonitorResources(StageMonitorDriver owner, StageMonitorCameraDriver cameras, Vector2Int size)
        {
            int count = Mathf.Max(1, GetAuthoredMonitorCameraCount());
            if (_monitorCameraDriver != null && _monitorCameraDriver != cameras)
                _monitorCameraDriver.SetupCaptureTexture(null);
            cameras.InitializeCapture(size.x, size.y, count);
            ReleaseMonitorTextures();
            _monitorResourceOwner = owner;
            _monitorCameraDriver = cameras;
            MonitorTextureArray = new RenderTexture[count];
            for (int i = 0; i < count; i++)
            {
                var texture = new RenderTexture(size.x, size.y, 0)
                {
                    name = "LiveMonitorScreenRT[" + i + "]",
                    filterMode = FilterMode.Bilinear
                };
                texture.Create();
                MonitorTextureArray[i] = texture;
            }

            cameras.SetupCaptureTextures(MonitorTextureArray);
            PublishMonitorTextures();
        }

        private void PublishMonitorTextures()
        {
            // Native Director +0xd8 is MultiCameraFinalComposite[], not the
            // auxiliary MultiCamera array. Its +0x48 target is borrowed.
            if (MultiCameraFinalComposite != null)
                MultiCameraFinalComposite.monitorTexture = MonitorTextureArray != null && MonitorTextureArray.Length > 0 ? MonitorTextureArray[0] : null;
            if (_cameraImageEffects != null)
                foreach (var effect in _cameraImageEffects)
                    if (effect is LiveImageEffect live)
                        live.MonitorTextureArray = MonitorTextureArray;
        }

        private void ReleaseMonitorTextures()
        {
            if (MonitorTextureArray != null)
                foreach (var texture in MonitorTextureArray)
                    if (texture != null)
                    {
                        texture.Release();
                        if (Application.isPlaying)
                            Destroy(texture);
                        else
                            DestroyImmediate(texture);
                    }

            MonitorTextureArray = null;
            PublishMonitorTextures();
        }

        internal void UnbindMonitorResources(StageMonitorDriver owner)
        {
            if (_monitorResourceOwner != owner)
                return;
            _monitorCameraDriver?.SetupCaptureTexture(null);
            ReleaseMonitorTextures();
            _monitorResourceOwner = null;
            _monitorCameraDriver = null;
        }

        // Native Director delegates these to the timeline-owned arrays. The
        // host owner guard avoids reading disposed slots after Viewer unbind.
        public bool IsActiveMonitorCamera(int index = -1) => _monitorResourceOwner != null && _liveTimelineControl != null && _liveTimelineControl.IsActiveMonitorCamera(index);
        public bool IsUseRenderTextureForMonitor(int index) => _monitorResourceOwner != null && _liveTimelineControl != null && _liveTimelineControl.IsUseRenderTextureForMonitor(index);
        [SerializeField]
        private GameObject[] _cameraNodes;
        private Camera[] _cameraObjects;
        // Native Director retains one main timeline sample; each presentation
        // camera applies a full parameter copy before its color Setup gate.
        private Gallop.RenderPipeline.SimpleColorCorrectionPass.Parameter _mainColorCorrection;
        private GallopImageEffect[] _cameraImageEffects;
        private Transform[] _cameraTransforms;
        [SerializeField]
        private CameraLookAt _cameraLookAt;
        private int _activeCameraIndex = 1;
        private readonly int[] kTimelineCameraIndices = new int[3]
        {
            1,
            2,
            3
        };
        [SerializeField]
        private bool _enableMirrorReflection = true;
        [SerializeField]
        private List<MirrorReflection> _mirrorReflections = new List<MirrorReflection>();
        [SerializeField]
        private bool _mirrorRenderInLateUpdate = true;
        private bool _mirrorInitPending;
        [SerializeField]
        public MultiCameraFinalComposite MultiCameraFinalComposite { get; private set; }

        private MultiCamera[] _multiCameras;
        private LiveVolumeLightController _volumeLightController;
        private LiveStageParticleController _stageParticleController;
        private LiveEffectController _effectController;
        private readonly Dictionary<int, Transform> _effectStageObjects = new Dictionary<int, Transform>();
        private float _effectLastTimelineTime = float.NaN;
        public const string Spotlight3dControllerPath = "3d/env/live/common/spotlight3d/pfb_env_live_cmn_spotlight3d_controller000";
        private Spotlight3dRuntime _spotlightRuntime;
        private MaterialPropertyBlock _characterLightingBlock;
        private Gallop.RenderPipeline.RecoveredLensFlareController _stageFlares;
        private LightProjectionRuntimeController _lightProjectionController;
        private FootLightRuntime _footLightRuntime;
        private LivePropsController _livePropsController;
        private readonly Dictionary<int, Texture> _lightProjectionTextures = new Dictionary<int, Texture>();
        private readonly Dictionary<int, Texture> _mirrorBallProjectionTextures = new Dictionary<int, Texture>();
        private LiveSettingsResourceMap _liveSettingsResources;
        public IReadOnlyList<Gallop.RenderPipeline.CustomProjector> LightProjectors => _lightProjectionController?.Projectors;
        public IReadOnlyList<Gallop.RenderPipeline.MirrorBallProjector> MirrorBallProjectors => _lightProjectionController?.MirrorBallProjectors;

        // Legacy filename helpers only. Timeline IDs must use LiveSettingsResourceMap.
        public static string GetLightProjectionTexturePath(int textureId) => string.Format(System.Globalization.CultureInfo.InvariantCulture, "3d/env/live/common/projector/tex_env_live_cmn_projector{0:000}", textureId);
        public static string GetMirrorBallProjectionTexturePath(int textureId) => string.Format(System.Globalization.CultureInfo.InvariantCulture, "3d/env/live/common/projectormirrorball/tex_env_live_cmn_projector_mirrorball{0:000}", textureId);
        private Texture ResolveMirrorBallProjectionTexture(int textureId)
        {
            if (textureId < 0)
                return null;
            if (_mirrorBallProjectionTextures.TryGetValue(textureId, out var texture))
                return texture;
            string path = _liveSettingsResources?.ProjectorPath(textureId, true);
            if (string.IsNullOrEmpty(path))
                path = GetMirrorBallProjectionTexturePath(textureId);
            texture = LiveFlashResourceUtility.LoadOnView<Texture>(path);
            _mirrorBallProjectionTextures.Add(textureId, texture);
            return texture;
        }

        private Texture ResolveLightProjectionTexture(int textureId)
        {
            if (textureId < 0)
                return null;
            if (_lightProjectionTextures.TryGetValue(textureId, out var texture))
                return texture;
            string path = _liveSettingsResources?.ProjectorPath(textureId, false);
            if (string.IsNullOrEmpty(path))
                path = GetLightProjectionTexturePath(textureId);
            texture = LiveFlashResourceUtility.LoadOnView<Texture>(path);
            _lightProjectionTextures.Add(textureId, texture);
            return texture;
        }

        public void NotifyLiveCharacterVisible(UmaContainer character, bool visible)
        {
            if (character == null)
                return;
            _footLightRuntime?.SetCharacterVisible(character.transform, visible);
        }

        private Transform ResolveEffectCharacter(int index)
        {
            var locators = _liveTimelineControl.liveCharactorLocators;
            return index >= 0 && index < locators.Length ? locators[index]?.liveRootTransform : null;
        }

        private Transform ResolveEffectStageObject(string name, int hash)
        {
            return _effectStageObjects.TryGetValue(hash, out var owner) ? owner : null;
        }

        private void InitializeLiveEffects(int normalLayer, int excludedLayer, int transparentLayer)
        {
            if (_effectController != null)
            {
                _liveTimelineControl.OnUpdateEffect -= _effectController.Update;
                _liveTimelineControl.OnUpdateEffectScale -= _effectController.UpdateScale;
                _effectController.Dispose();
            }

            _effectStageObjects.Clear();
            foreach (var item in _liveTimelineControl.StageObjectMap)
                if (item.Value != null)
                    _effectStageObjects[FNVHash.Generate(item.Key)] = item.Value.transform;
            _effectController = new LiveEffectController(transform, transform, ResolveEffectCharacter, ResolveEffectStageObject, normalLayer, excludedLayer, transparentLayer);
            // Effect.Update owner19 reads Director._mainCameraTransform (native
            // 0x4e8b85f), and must follow switches rather than the Director root.
            _effectController.CameraOwner = () => MainCameraTransform;
            var sheet = _liveTimelineControl.GetWorkSheetBySheetIndex(LiveTimelineDefine.SheetIndex.MainLive);
            _effectController.Load(sheet, TimelinePlayerMode.Default, variation => variation == _liveTimelineControl.SheetVariationId);
            _liveTimelineControl.OnUpdateEffect += _effectController.Update;
            _liveTimelineControl.OnUpdateEffectScale += _effectController.UpdateScale;
            _liveTimelineControl.ResetEffectTimelineForSeek();
            _effectLastTimelineTime = float.NaN;
        }

        private void InitializeLiveSpotlights()
        {
            if (_spotlightRuntime != null)
            {
                _liveTimelineControl.OnUpdateSpotlight3d -= _spotlightRuntime.Update;
                _spotlightRuntime.Dispose();
                _spotlightRuntime = null;
            }

            if (_stageController == null)
                return;
            List<LiveTimelineSpotlight3dData> groups = null;
            foreach (var sheet in _liveTimelineControl.data.worksheetList)
                if (sheet?.spotlight3dList != null && sheet.spotlight3dList.Count != 0)
                {
                    groups = sheet.spotlight3dList;
                    break;
                }

            if (groups == null)
                return;
            var prefab = LiveFlashResourceUtility.LoadOnView<GameObject>(Spotlight3dControllerPath);
            var holder = prefab != null ? prefab.GetComponent<AssetHolder>() : null;
            if (holder == null)
            {
                Debug.LogError("[Spotlight3d] Missing native common controller AssetHolder");
                return;
            }

            var roots = new List<Transform>(CharaContainerScript.Count);
            for (int i = 0; i < CharaContainerScript.Count; ++i)
                roots.Add(ResolveEffectCharacter(i));
            _spotlightRuntime = new Spotlight3dRuntime(roots, MainCameraTransform, GraphicSettings.GetLayer(GraphicSettings.LayerIndex.LayerBG));
            _spotlightRuntime.CreateControllers(groups, TimelinePlayerMode.Default, id => holder.Get<GameObject>(LiveTimelineSpotlight3dData.GetAssetName(id)));
            _liveTimelineControl.OnUpdateSpotlight3d += _spotlightRuntime.Update;
        }

        public static Director instance => _instance;

        //real work start
        public LiveEntry live;
        private const string CUTT_PATH = "cutt/cutt_son{0}/cutt_son{0}";
        private const string STAGE_PATH = "3d/env/live/live{0}/pfb_env_live{0}_controller000";
        private const string SONG_PATH = "sound/l/{0}/snd_bgm_live_{0}_oke_01";
        private const string VOCAL_PATH = "sound/l/{0}/snd_bgm_live_{0}_chara_{1}_01";
        private const string RANDOM_VOCAL_PATH = "sound/l/{0}/snd_bgm_live_{0}_chara";
        private const string LIVE_PART_PATH = "live/musicscores/m{0}/m{0}_part";
        private const string CYALUME_SCORE_PATH = "live/musicscores/m{0:0000}/m{0:0000}_cyalume";
        private UmaViewerBuilder Builder => UmaViewerBuilder.Instance;

        public List<Transform> charaObjs;
        public List<UmaContainerCharacter> CharaContainerScript = new List<UmaContainerCharacter>();
        // Host bridge for original TryGetLiveSettingsCharacterData -> master
        // lookup. Existing loaded models supply slot identity, not raw slot IDs.
        // Full native LiveLoadSettings construction/lifecycle remains separate.
        internal static System.Data.DataRow GetLiveCameraCharaData(int index)
        {
            if (index < 0)
                return null;
            var characters = instance.CharaContainerScript;
            if (index >= characters.Count)
                return null;
            var character = characters[index];
            if (character == null)
                return null;
            // Original CharacterData constructor represents a mob as CharaId=1,
            // keeping MobId separately. Background lookup still uses chara_data.
            int id = character.IsMob ? 1 : character.CharaEntry.Id;
            return UmaDatabaseController.Instance.CharaData.Find(row => Convert.ToInt32(row["id"]) == id);
        }

        internal bool TryGetPropsCameraNode(int index, int propsIndex, int nodeIndex, out Transform target)
        {
            target = null;
            if (index < 0 || index >= CharaContainerScript.Count || CharaContainerScript[index] == null)
                return false;
            // Current host contexts belong to the loaded costume only; native
            // multi-dress/host-swap installation remains a separate boundary.
            return _livePropsController.TryGetAttachNodeTransform(index, propsIndex, nodeIndex, out target);
        }

        internal Transform ResolveStageObjectAttachment(ObjectUpdateInfo info)
        {
            if (info.AttachTarget == AttachType.Camera)
                return info.MultiCameraIndex < 0 ? MainCameraTransform : _multiCameras != null && info.MultiCameraIndex < _multiCameras.Length ? _multiCameras[info.MultiCameraIndex]?.transform : null;
            if (info.AttachTarget != AttachType.Character)
                return null;
            if (!info.IsAttachProps)
                return ResolveEffectCharacter(info.CharacterPosition);
            return _livePropsController?.GetStageAttachmentRoot(info.CharacterPosition, info.PropsIndex);
        }

        public List<Animation> charaAnims;
        public List<UmaViewerAudio.CuteAudioSource> liveVocal = new List<UmaViewerAudio.CuteAudioSource>();
        public UmaViewerAudio.CuteAudioSource liveMusic = new UmaViewerAudio.CuteAudioSource();
        public PartEntry partInfo;
        public bool _syncTime = false;
        public bool _soloMode = false;
        public int characterCount = 0;
        public int allowCount = 0;
        public int liveMode = 1;
        public LiveViewerUI UI;
        public float totalTime;
        public SliderControl sliderControl;
        public bool IsRecordVMD;
        public bool RequireStage = true;
        private bool _lateTimelineAppliedThisFrame;
        public HandShakeCamera HandShake { get; } = new HandShakeCamera();

        private float _handShakeLastTime;
        public Transform MainCameraTransform => _mainCameraTransform;

        public Camera MainRenderCamera
        {
            get
            {
                if (_mainCameraTransform != null)
                {
                    var cam = _mainCameraTransform.GetComponent<Camera>();
                    if (cam == null)
                        cam = _mainCameraTransform.GetComponentInChildren<Camera>(true);
                    if (cam != null)
                        return cam;
                }

                if (_cameraObjects != null && _activeCameraIndex >= 0 && _activeCameraIndex < _cameraObjects.Length)
                    return _cameraObjects[_activeCameraIndex];
                return Camera.main;
            }
        }

        // [REPORT §10.3-B1/§4.2] 原生 RegisterPass 按相机组逐台注册后处理；
        // 演示相机组 = cutt prefab 的时间轴相机（_cameraObjects，InitializeCamera
        // 收集，CameraSwitcher 逐台 SetActive 激活）。成员判定取代 master7 原
        // "camera == MainRenderCamera 引用相等"门控，使切换帧新旧相机同帧都
        // 持有完整后处理链。多相机（MultiCamera）/镜面反射/监视器捕获相机
        // 不在组内，继续走各自链路。
        public bool IsPresentationCamera(Camera camera)
        {
            if (camera == null || _cameraObjects == null)
                return false;
            for (int i = 0; i < _cameraObjects.Length; i++)
                if (ReferenceEquals(_cameraObjects[i], camera))
                    return true;
            return false;
        }

        private Transform _mainCameraTransform;
        private LiveTransparentCamera _transparentCamera;
        public Camera FinalRenderCamera => _transparentCamera != null && _transparentCamera.Active ? _transparentCamera.Camera : MainRenderCamera;

        private static readonly Dictionary<string, UmaDatabaseEntry> _laserBundleCache = new Dictionary<string, UmaDatabaseEntry>();
        public bool isTimelineControlled
        {
            get
            {
                if (_liveTimelineControl != null)
                {
                    return _liveTimelineControl.data != null;
                }

                return false;
            }
        }

        public float CalcFrameJustifiedMusicTime()
        {
            if (isTimelineControlled)
            {
                return Mathf.RoundToInt(musicScoreTime * 60f) / 60f;
            }

            return musicScoreTime;
        }

        public float musicScoreTime => Mathf.Clamp(smoothMusicScoreTime, 0f, 99999f);
        private float smoothMusicScoreTime => _liveCurrentTime; //temp to liveCurrentTime

        public void Initialize()
        {
            InitializeRandomTables();
            foreach (var driver in GetComponentsInChildren<StageBlinkLightDriver>(true))
                driver.RandomRatios = BlinkRandomRatios;
            if (live != null)
            {
                _instance = this;
                Gallop.RenderPipeline.PostImageEffectFeature.ResetRuntimeParameter();
                Debug.Log(string.Format(CUTT_PATH, live.MusicId));
                Builder.LoadAssetPath(string.Format(CUTT_PATH, live.MusicId), transform);
                // Character eye tracking can run while subsequent asset preloads yield.
                // Establish Camera.main before constructing/enabling those characters.
                InitializeCamera();
                UpdateMainCamera();
                if (RequireStage)
                {
                    Debug.Log(live.BackGroundId);
                    string stagePath = $"3d/env/live/live{live.BackGroundId}/pfb_env_live{live.BackGroundId}_controller{live.BackGroundVariation:D3}";
                    if (UmaViewerMain.Instance.AbList.TryGetValue(stagePath, out var stageEntry) && !UmaAssetManager.Exist(stageEntry))
                    {
                        // 正常从 LoadLive 进入时已经异步预载；这里仅作为其他入口的同步兜底。
                        PreloadStageBundlesBeforeInstantiate(live.BackGroundId);
                    }

                    var loadedStageRoot = Builder.LoadAssetPath(stagePath, transform);
                    _stageController = loadedStageRoot == null ? null : loadedStageRoot.GetComponent<StageController>();
                    if (LiveStageLoadLifecycle.Complete(_stageController, stage => stage.InitializeLoadedStage(_liveTimelineControl), enabled => IsEnabledStageController = enabled))
                    {
                        _liveTimelineControl.StageObjectMap = _stageController.StageObjectMap;
                        _liveTimelineControl.StageObjectUnitMap = _stageController.StageObjectUnitMap;
                    }
                }

                //Make CharacterObject
                var characterStandPos = _liveTimelineControl.transform.Find("CharacterStandPos");
                int counter = 0;
                var standPos = characterStandPos.GetComponentsInChildren<Transform>();
                var count = _liveTimelineControl.data.characterSettings.useHighPolygonModel.Length;
                for (int i = 0; i < count; i++)
                {
                    var newObj = Instantiate(standPos[i % characterStandPos.childCount + 1], transform);
                    newObj.gameObject.name = string.Format("CharacterObject{0}", counter);
                    charaObjs.Add(newObj.transform);
                    counter++;
                }

                _liveTimelineControl.SetCharacterStandPosLocators(standPos.Skip(1).ToArray());
                //Get live parts info
                UmaDatabaseEntry partAsset = UmaViewerMain.Instance.AbList[string.Format(LIVE_PART_PATH, live.MusicId)];
                UmaViewerAudio.LastAudioPartIndex = -1;
                Debug.Log(partAsset.Name);
                AssetBundle bundle = UmaAssetManager.LoadAssetBundle(partAsset);
                TextAsset partData = bundle.LoadAsset<TextAsset>($"m{live.MusicId}_part");
                partInfo = new PartEntry(partData.text);
            }
        }

        public void InitializeUI()
        {
            UI = GameObject.Find("LiveUI").GetComponent<LiveViewerUI>();
            sliderControl = UI.ProgressBar.GetComponent<SliderControl>();
            LiveViewerUI.Instance.RecordingUI.SetActive(IsRecordVMD);
            LiveViewerUI.Instance.RecordingText.text = $"�� Recording...\r\n VMD will be saved in {Path.GetFullPath(Application.dataPath + UnityHumanoidVMDRecorder.FileSavePath)}";
        }

        internal static int ResolveCharacterColorMask(string name, int flags)
        {
            switch (name)
            {
                case "CharaColor":
                    return flags != 0 ? flags : 0xfffff;
                case "CharaCenter":
                    return 1;
                case "CharaLeft":
                    return 2;
                case "CharaRight":
                    return 4;
                case "CharaBack":
                    return 0xffff8;
                case "CharaOther":
                    return 0xffffe;
                default:
                    return 0;
            }
        }

        readonly Dictionary<int, UmaContainerCharacter> _physicalFacialOwners = new Dictionary<int, UmaContainerCharacter>();
        internal UmaContainerCharacter ResolvePhysicalFacialCharacter(int physicalSlot)
        {
            // Original CharacterObject keeps its model reference across
            // formation/constraint reparenting. Cache by physical root slot,
            // not the compact loaded cast index (sparse slots are possible).
            if (_physicalFacialOwners.TryGetValue(physicalSlot, out var bound) && bound != null)
                return bound;
            if (charaObjs == null || physicalSlot < 0 || physicalSlot >= charaObjs.Count)
                return null;
            var root = charaObjs[physicalSlot];
            var actor = root != null ? root.GetComponentInChildren<UmaContainerCharacter>(true) : null;
            if (actor != null)
                _physicalFacialOwners[physicalSlot] = actor;
            return actor;
        }

        public void InitializeTimeline(List<LiveCharacterLoadData> characters, int mode)
        {
            InitializeObjectImages();
            SyncCharacterFromStage();
            InitializeMotionSwapContext(characters);
            InitializeLiveSystemTextCharacters(characters);
            _physicalFacialOwners.Clear();
            for (int slot = 0; slot < charaObjs.Count; slot++)
                ResolvePhysicalFacialCharacter(slot);
            totalTime = _liveTimelineControl.data.timeLength;
            liveMode = mode;
            allowCount = characters.Count;
            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i].CharaEntry.Name != "")
                {
                    characterCount += 1;
                }
            }

            if (characterCount == 1)
            {
                _soloMode = true;
            }

            _liveTimelineControl.InitCharaMotionSequence(_liveTimelineControl.data.characterSettings.motionSequenceIndices);
            _liveTimelineControl.OnUpdateLipSync += delegate (LiveTimelineKeyIndex keyData_, float liveTime_)
            {
                var prevKey = keyData_.prevKey as LiveTimelineKeyLipSyncData;
                var curKey = keyData_.key as LiveTimelineKeyLipSyncData;
                var nextKey = keyData_.nextKey as LiveTimelineKeyLipSyncData;
                int characterFlags = (int)curKey.GetCharacterFlags(SongPartVariationId);
                for (int k = 0; k < charaObjs.Count; k++)
                {
                    if (k < CharaContainerScript.Count)
                    {
                        if (_liveTimelineControl.GetLipSyncPatternId(k) >= 0)
                            continue;
                        var container = CharaContainerScript[k];
                        container.FaceDrivenKeyTarget.AlterUpdateAutoLip(prevKey, curKey, liveTime_, (characterFlags & (1 << k)) != 0 ? 1 : 0);
                    }
                }
            };
            _liveTimelineControl.OnUpdateFacial += delegate (FacialDataUpdateInfo updateInfo_, float liveTime_, int position)
            {
                var container = ResolvePhysicalFacialCharacter(position);
                if (container != null)
                {
                    if (container.LiveSystemTextFace?.Active != true)
                        container.LiveRandomEar?.SetTimelineKey(updateInfo_.earCur);
                    container.FaceDrivenKeyTarget.AlterUpdateFacialNew(ref updateInfo_, liveTime_);
                }
            };
            _liveTimelineControl.OnUpdateFacialToon += OnUpdateFacialToon;
            BindCharaNodeOffset();
            BindParentConstraint();
            _liveTimelineControl.OnUpdateCharaWind += OnUpdateCharaWind;
            BindSweatLocator();
            _liveTimelineControl.OnUpdateGlobalLight += delegate (ref GlobalLightUpdateInfo updateInfo)
            {
                foreach (var locator in _liveTimelineControl.liveCharactorLocators)
                {
                    if (locator != null && updateInfo.flags.hasFlag(locator.liveCharaStandingPosition) && locator is LiveTimelineCharaLocator charaLocator)
                    {
                        var container = charaLocator.UmaContainer;
                        if (container)
                        {
                            container.SetLiveGlobalLightRotation(updateInfo.lightRotation);
                            var propertyBlock = _characterLightingBlock ?? (_characterLightingBlock = new MaterialPropertyBlock());
                            foreach (var renderer in container.Renderers)
                            {
                                renderer.GetPropertyBlock(propertyBlock);
                                propertyBlock.SetFloat("_RimShadowRate", updateInfo.globalRimShadowRate);
                                propertyBlock.SetColor("_RimColor", updateInfo.rimColor);
                                propertyBlock.SetFloat("_RimStep", updateInfo.rimStep);
                                propertyBlock.SetFloat("_RimFeather", updateInfo.rimFeather);
                                propertyBlock.SetFloat("_RimSpecRate", updateInfo.rimSpecRate);
                                propertyBlock.SetFloat("_RimHorizonOffset", updateInfo.RimHorizonOffset);
                                propertyBlock.SetFloat("_RimVerticalOffset", updateInfo.RimVerticalOffset);
                                propertyBlock.SetFloat("_RimHorizonOffset2", updateInfo.RimHorizonOffset2);
                                propertyBlock.SetFloat("_RimVerticalOffset2", updateInfo.RimVerticalOffset2);
                                propertyBlock.SetColor("_RimColor2", updateInfo.rimColor2);
                                propertyBlock.SetFloat("_RimStep2", updateInfo.rimStep2);
                                propertyBlock.SetFloat("_RimFeather2", updateInfo.rimFeather2);
                                propertyBlock.SetFloat("_RimSpecRate2", updateInfo.rimSpecRate2);
                                propertyBlock.SetFloat("_RimShadowRate2", updateInfo.globalRimShadowRate2);
                                renderer.SetPropertyBlock(propertyBlock);
                            }
                        }
                    }
                }
            };
            _liveTimelineControl.OnUpdateBgColor1 += delegate (ref BgColor1UpdateInfo updateInfo)
            {
                // StageController.UpdateCharaColor1 0x4eb7ec0 resolves named
                // singer tracks before flags. A zero flags field is common for
                // these tracks and must never make the right singer overwrite all.
                int targetMask = ResolveCharacterColorMask(updateInfo.TimelineName, updateInfo.CurrentFlags);
                if (targetMask <= 0)
                    return;
                foreach (var locator in _liveTimelineControl.liveCharactorLocators)
                {
                    if (locator != null && ((LiveCharaPositionFlag)targetMask).hasFlag(locator.liveCharaStandingPosition) && locator is LiveTimelineCharaLocator charaLocator)
                    {
                        var container = charaLocator.UmaContainer;
                        if (container)
                        {
                            var propertyBlock = _characterLightingBlock ?? (_characterLightingBlock = new MaterialPropertyBlock());
                            foreach (var renderer in container.Renderers)
                                LiveCharacterColorBlock.Apply(renderer, propertyBlock, in updateInfo);
                            SetFakeLightReceiveSetting(container, in updateInfo);
                        }
                    }
                }
            };
            SetupCharacterLocator();
            _stageController?.StartUpdateTimeline(_liveTimelineControl);
            InitializeLipSyncPatterns();
            _modelRenderQueue.Bind(CharaContainerScript);
            BindModelQueueCameraCallbacks();
            foreach (var sheet in _liveTimelineControl.data.worksheetList)
            {
                sheet?.InitializeProps(_liveTimelineControl);
                sheet?.InitializeAudience(_liveTimelineControl);
            }

            InitializeLiveProps();
            _stageParticleController = new LiveStageParticleController(_stageController != null ? _stageController.transform : null);
            _liveTimelineControl.OnUpdateParticle += _stageParticleController.Update;
            _liveTimelineControl.OnUpdateParticleGroup += _stageParticleController.UpdateGroup;
            InitializeCamera();
            _transparentCamera?.Dispose();
            _transparentCamera = new LiveTransparentCamera(transform);
            InitializeLiveTitle();
            InitializeLiveFlash(characters);
            InitializeMirrorReflections();
            _volumeLightController = new LiveVolumeLightController();
            _volumeLightController.Initialize(_stageController != null ? _stageController.transform : null, "Sunshafts", _liveTimelineControl.data.sunShaftsSettings, MainRenderCamera);
            _volumeLightController.SetLightShaftsTextures(_liveTimelineControl.data.indirectLightShaftsSettings, LiveLightShaftsResources.LoadTextures(_liveTimelineControl.data.indirectLightShaftsSettings));
            UpdateMainCamera();
            InitializeMultiCamera(_liveTimelineControl);
            InitializeLiveEffects(GraphicSettings.GetLayer(GraphicSettings.LayerIndex.LayerEFFECT), GraphicSettings.GetLayer(GraphicSettings.LayerIndex.LayerCircleProfile), GraphicSettings.GetLayer(GraphicSettings.LayerIndex.LayerTransparentFX));
            InitializeLiveSpotlights();
            if (_lightProjectionController != null)
            {
                _liveTimelineControl.OnUpdateLightProjection -= _lightProjectionController.Apply;
                _lightProjectionController.Dispose();
                _lightProjectionController = null;
                _lightProjectionTextures.Clear();
                _mirrorBallProjectionTextures.Clear();
            }

            if (_stageController != null)
            {
                if (_liveSettingsResources == null)
                    _liveSettingsResources = LiveSettingsResourceMap.Load(live.MusicId);
                _lightProjectionController = new LightProjectionRuntimeController();
                _lightProjectionController.Initialize(_stageController.transform, ResolveLightProjectionTexture, ResolveEffectCharacter);
                _lightProjectionController.ConfigureCharacterPosition(index =>
                {
                    var locators = _liveTimelineControl.liveCharactorLocators;
                    return index >= 0 && index < locators.Length && locators[index] != null ? locators[index].liveCharaPosition : Vector3.zero;
                });
                _lightProjectionController.ConfigureMirrorBallTextures(ResolveMirrorBallProjectionTexture);
                _liveTimelineControl.OnUpdateLightProjection += _lightProjectionController.Apply;
                var monitorDriver = GetComponentInParent<StageMonitorDriver>();
                if (monitorDriver != null)
                    _lightProjectionController.ConfigureMovieFrames(monitorDriver.TryGetProjectionMovieFrame);
            }

            var footRoots = new List<Transform>(CharaContainerScript.Count);
            for (int i = 0; i < CharaContainerScript.Count; i++)
                footRoots.Add(ResolveEffectCharacter(i));
            _footLightRuntime = new FootLightRuntime();
            _footLightRuntime.Initialize(_stageController != null ? _stageController.transform : transform, footRoots, index =>
            {
                var locators = _liveTimelineControl.liveCharactorLocators;
                return index >= 0 && index < locators.Length && locators[index] != null ? locators[index].liveCharaPosition : Vector3.zero;
            }, path => LiveFlashResourceUtility.LoadOnView<GameObject>(path), (index, left) =>
            {
                var character = CharaContainerScript[index];
                if (character == null || character.LiveLocator?.Bones == null)
                    return null;
                character.LiveLocator.Bones.TryGetValue(left ? "Ankle_L" : "Ankle_R", out var foot);
                return foot;
            });
            _liveTimelineControl.OnUpdateBgColor1 += _footLightRuntime.ApplySpotlight;
            _liveTimelineControl.OnUpdateBgColor1 += _footLightRuntime.ApplyShadow;
            var flareSetting = _liveTimelineControl.data.lensFlareSetting;
            _stageFlares = new Gallop.RenderPipeline.RecoveredLensFlareController(_stageController != null ? _stageController.transform : null, flareSetting != null ? flareSetting.lightStandard : 3f, flareSetting != null ? flareSetting.underLimit : 0f);
            _livePropsController.RegisterStageLighting(_stageController, _stageFlares);
            foreach (var sheet in _liveTimelineControl.data.worksheetList)
            {
                if (sheet.bgColor1List != null)
                    foreach (var group in sheet.bgColor1List)
                        group?.UpdateStatus();
                if (sheet.lensFlareList != null)
                    foreach (var group in sheet.lensFlareList)
                        group?.UpdateStatus();
            }

            _liveTimelineControl.OnUpdateLensFlare += _stageFlares.Apply;
            for (int i = 0; i < kTimelineCameraIndices.Length; i++)
            {
                int num = kTimelineCameraIndices[i];
                if (num < _cameraObjects.Length)
                {
                    _liveTimelineControl.SetTimelineCamera(_cameraObjects[num], i);
                }
            }

            _liveTimelineControl.OnUpdatePostEffect_BloomDiffusion += OnUpdatePostEffect_BloomDiffusion;
            _liveTimelineControl.OnUpdatePostEffect_Dof += OnUpdatePostEffect_Dof;
            _liveTimelineControl.OnUpdatePostFilm += OnUpdatePostFilm;
            // The viewer has no game's three RawImageCommon canvases. Retain the
            // native controller and publish its landscape surface after final blit.
            _fadeController.Initialize(_renderFadeSurface, new RenderFadeSurface(), new RenderFadeSurface());
            _liveTimelineControl.OnUpdateFade += OnUpdateFade;
            _liveTimelineControl.OnUpdateRadialBlur += OnUpdateRadialBlur;
            SubscribeNativePostTimeline(_liveTimelineControl);
            SubscribeChromaticAberrationTimeline(_liveTimelineControl);
            _liveTimelineControl.OnUpdateColorCorrection += OnUpdateColorCorrection;
            _liveTimelineControl.OnUpdatePreColorCorrection += OnUpdatePreColorCorrection;
            _liveTimelineControl.OnVoicePlay += OnVoicePlay;
            _liveTimelineControl.OnVoiceStopAll += StopVoiceAll;
            _liveTimelineControl.OnUpdateColorCorrectionMultiCamera += OnUpdateColorCorrectionMultiCamera;
            _liveTimelineControl.OnUpdateGlobalFog += OnUpdateGlobalFog;
            _liveTimelineControl.OnUpdateEdgeStyle += OnUpdateEdgeStyle;
            _liveTimelineControl.OnUpdateTransmittedLight += OnUpdateTransmittedLight;
            _liveTimelineControl.OnUpdateTransmittedLightMultiCamera += OnUpdateTransmittedLightMultiCamera;
            // [A7b][原生订阅点] Director.InitializeTimeline 在
            // 0x181a5f21d（add_OnUpdateExposure RVA=0x1ae96c0）与
            // 0x181a5f1c9（add_OnUpdateToneCurve RVA=0x1aea4b0）订阅两条轨道，
            // 顺序：BloomDiffusion → ToneCurve → Spotlight3d → Exposure → PostFilm。
            // master7 统一在 Initialize 事件绑定块订阅，行为一致。
            _liveTimelineControl.OnUpdateVolumeLight += OnUpdateVolumeLight;
            _liveTimelineControl.OnUpdateLightShafts += OnUpdateLightShafts;
            _liveTimelineControl.OnUpdateExposure += OnUpdateExposure;
            _liveTimelineControl.OnUpdateToneCurve += OnUpdateToneCurve;
            _liveTimelineControl.OnUpdateCameraSwitcher += delegate (int cameraIndex_)
            {
                if (cameraIndex_ < 0)
                {
                    _activeCameraIndex = 0;
                }
                else if (cameraIndex_ < kTimelineCameraIndices.Length)
                {
                    _activeCameraIndex = kTimelineCameraIndices[cameraIndex_];
                }

                UpdateMainCamera();
            };
        }

        public void InitializeCamera()
        {
            if (_cameraObjects == null)
            {
                _cameraObjects = new Camera[_cameraNodes.Length + 1];
                _cameraTransforms = new Transform[_cameraNodes.Length + 1];
                for (int i = 0; i < _cameraNodes.Length; i++)
                {
                    GameObject gameObject = _cameraNodes[i];
                    Camera camera = gameObject.GetComponent<Camera>();
                    if (camera == null)
                    {
                        camera = gameObject.GetComponentInChildren<Camera>();
                    }

                    // Original prefabs/live/director serializes these defaults
                    // for MotionCamera and CameraTimeline1/2/3. The Viewer
                    // prefab's Everything mask also draws layer16 led_dummy
                    // meshes. Restore the native mask only on first creation;
                    // flagged timeline keys can subsequently replace it.
                    Gallop.RenderPipeline.NativeLensFlareCameraRegistry.RestoreSerializedFlareLayer(camera);
                    camera.clearFlags = CameraClearFlags.Color;
                    camera.cullingMask = 0x3E283FC8;
                    _cameraObjects[i] = camera;
                    _cameraTransforms[i] = camera.transform;
                }
            }

            if (_cameraImageEffects == null)
            {
                _cameraImageEffects = new GallopImageEffect[_cameraObjects.Length];
                for (int i = 0; i < _cameraObjects.Length; ++i)
                {
                    var camera = _cameraObjects[i];
                    if (camera == null)
                        continue;
                    if (i < _cameraNodes.Length)
                        Gallop.RenderPipeline.NativeLensFlareCameraRegistry.RestoreSerializedFlareLayer(camera);
                    var effect = camera.GetComponent<GallopImageEffect>();
                    if (effect == null)
                        effect = camera.gameObject.AddComponent<LiveImageEffect>();
                    // Actual Director.InitializeCamera dispatches Initialize slot5
                    // before author enable/bloom state; do not rely on Awake.
                    effect.InitializeVolume();
                    _cameraImageEffects[i] = effect;
                }
            }

            foreach (var effect in _cameraImageEffects)
                if (effect is LiveImageEffect live)
                {
                    live.MonitorTextureArray = MonitorTextureArray;
                    live.SetUpEdgeStyleController(_liveTimelineControl.CurrentWorkSheet.edgeStyleKeys.EdgeStyle);
                }
        }

        public void InitializeMultiCamera(LiveTimelineControl control)
        {
            var cameraCount = control.data.multiCameraSettings != null ? control.data.multiCameraSettings.cameraNum : 0;
            MultiCamera[] cameras = new MultiCamera[cameraCount];
            var root = new GameObject("MultiCameras");
            root.transform.SetParent(control.transform);
            MultiCameraFinalComposite = root.AddComponent<MultiCameraFinalComposite>();
            var presentationCamera = MainRenderCamera;
            for (int i = 0; i < cameraCount; i++)
            {
                var camObj = new GameObject($"MultiCamera_{i}");
                camObj.transform.SetParent(root.transform);
                var sceneCamera = camObj.AddComponent<Camera>();
                if (presentationCamera != null)
                    sceneCamera.CopyFrom(presentationCamera);
                sceneCamera.clearFlags = CameraClearFlags.Color;
                sceneCamera.targetTexture = null;
                sceneCamera.depth = i + 1;
                var cameraData = camObj.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                cameraData.renderType = UnityEngine.Rendering.Universal.CameraRenderType.Base;
                var cam = camObj.AddComponent<MultiCamera>();
                cam.Initialize(MultiCameraFinalComposite, i);
                camObj.SetActive(false);
                cameras[i] = cam;
                control.MultiRecordFrames.Add(new List<LiveCameraFrame>());
            }

            _multiCameras = cameras;
            MultiCameraFinalComposite.Initialize(cameras, presentationCamera);
            PublishMonitorTextures();
            if (presentationCamera != null)
                presentationCamera.depth = cameraCount + 1;
            control.SetMultiCamera(cameras);
        }

        private void UpdateMainCamera()
        {
            if (_cameraObjects == null)
                return;
            for (int i = 0; i < _cameraNodes.Length; i++)
            {
                bool activeSelf = _cameraNodes[i].activeSelf;
                bool flag = i == _activeCameraIndex;
                _cameraNodes[i].SetActive(flag);
                // Camera.main consumers (eye tracking) must follow the same presentation switch.
                var camera = _cameraObjects[i];
                if (camera != null)
                {
                    if (flag)
                        camera.gameObject.tag = "MainCamera";
                    if (flag)
                    {
                        var cameraData = camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                        if (cameraData == null)
                            cameraData = camera.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                        cameraData.renderPostProcessing = true;
                    }
                    else if (camera.CompareTag("MainCamera"))
                        camera.gameObject.tag = "Untagged";
                }

                if (i == 0 && activeSelf != flag && flag && _cameraLookAt != null)
                {
                    _cameraLookAt.ActivationUpdate();
                }
            }

            _mainCameraTransform = _cameraTransforms[_activeCameraIndex];
            if (MultiCameraFinalComposite != null)
            {
                MainRenderCamera.depth = (_multiCameras != null ? _multiCameras.Length : 0) + 1;
                MultiCameraFinalComposite.SetRenderCamera(MainRenderCamera);
            }
        }

        private void SetupCharacterLocator()
        {
            if (!_liveTimelineControl)
                return;
            for (int i = 0; i < CharaContainerScript.Count; i++)
            {
                var container = CharaContainerScript[i];
                container.LiveLocator = new LiveTimelineCharaLocator(container);
                var face = container.FaceDrivenKeyTarget;
                container.LiveEyeHighlight?.Dispose();
                container.LiveEyeHighlight = LiveEyeHighlightController.Create(container);
                container.LiveFacePartsScale = LiveFacePartsScaleController.Create(container);
                container.LiveEyeBlink?.Dispose();
                container.LiveEyeBlink = LiveEyeBlinkController.Create(container);
                container.LiveRandomEar?.Dispose();
                container.LiveRandomEar = null;
                var randomEarTypes = new HashSet<EarType>();
                foreach (var sheet in _liveTimelineControl.data.worksheetList)
                {
                    var facial = _liveTimelineControl.GetFacialDataNative(sheet, i);
                    if (facial?.earKeys?.thisList == null)
                        continue;
                    foreach (var key in facial.earKeys.thisList)
                        if (key.useEarRandomMotion)
                            randomEarTypes.Add(key.randomMotionEarType);
                }

                if (randomEarTypes.Count > 0 && !container.IsMini)
                    container.LiveRandomEar = new LiveRandomEarController(container, randomEarTypes);
                face.LiveEyeTrace?.Dispose();
                face.LiveEyeTrace = LiveEyeTraceController.Create(container);
                if (face.LiveEyeTrace != null)
                    face.SetEyeRange(0, 0, 0, 0);
                container.LiveLocator.liveCharaStandingPosition = (LiveCharaPosition)i;
                _liveTimelineControl.liveCharactorLocators[i] = container.LiveLocator;
                _liveTimelineControl.InitializeCharacterLocatorStandPosition(i, container.LiveLocator);
            }

            // These authored slots still drive Formation -> Object even when
            // the selected cast does not instantiate a character in that slot.
            var attachmentSlots = new HashSet<int>();
            foreach (var sheet in _liveTimelineControl.data.worksheetList)
                if (sheet?.objectList != null)
                    foreach (var group in sheet.objectList)
                        foreach (var key in group.keys.thisList)
                            if (key.AttachTarget == AttachType.Character && !key.IsAttachProps)
                                attachmentSlots.Add(key.CharacterPosition);
            foreach (int slot in attachmentSlots)
            {
                if (slot < 0 || slot >= charaObjs.Count || slot >= _liveTimelineControl.liveCharactorLocators.Length || _liveTimelineControl.liveCharactorLocators[slot] != null)
                    continue;
                var locator = new LiveTimelineCharaLocator(charaObjs[slot]);
                locator.liveCharaStandingPosition = (LiveCharaPosition)slot;
                _liveTimelineControl.liveCharactorLocators[slot] = locator;
                _liveTimelineControl.InitializeCharacterLocatorStandPosition(slot, locator);
            }
        }

        public void InitializeMusic(int songid, List<LiveCharacterLoadData> characters)
        {
            if (AudioPlan == null || AudioPlan.SongId != songid)
                PrepareMusic(songid, characters);
            liveVocal.Clear();
            foreach (var binding in AudioPlan.Voices)
                if (binding.Entry != null)
                {
                    var bank = UmaViewerAudio.ApplySound(binding.Entry, binding.Slot);
                    bank.characterId = binding.CharacterId;
                    bank.voiceCharacterId = binding.VoiceCharacterId;
                    liveVocal.Add(bank);
                }

            liveMusic = UmaViewerAudio.ApplySound(AudioPlan.Accompaniment, -1);
        }

        public LiveSongAudioPlan AudioPlan { get; private set; }
        // Original AudioManager.UpdateSong uses the audio clock, independently
        // of the smoothed LiveTimeController clock used by scene animation.
        public Func<float> SongAudioTimeOverride { get; set; }

        public float SongAudioTime
        {
            get
            {
                if (SongAudioTimeOverride != null)
                    return SongAudioTimeOverride();
                var source = liveMusic?.sourceList?.FirstOrDefault();
                return source != null && source.clip != null ? source.timeSamples / (float)source.clip.frequency : 0f;
            }
        }

        public void PrepareMusic(int songid, List<LiveCharacterLoadData> characters)
        {
            InitializeLiveVariationSelection(songid, characters);
            AudioPlan = LiveSongAudioPlan.Create(songid, partInfo.SingerCount, characters, OkeCallOnVariationId, IsLiveCall, _liveSettingsResources.OkeLiveVariationPattern, EnabledLiveVariationOrder);
        }

        // Native LiveLoadSettings input; 0 keeps RegisterSongCueSheet's default01.
        public int OkeCallOnVariationId { get; set; }

        public void Play()
        {
            PreparePlaybackClock();
            StartMainSongAudio(LiveStartControl.MainSongRequest(partInfo, LiveStartControl.StartTimeFromFrame(0), null));
            _liveTimeController.StartCalcElapsedTime();
            _isLiveSetup = true;
            _liveCurrentTime = 0;
            // Flash sequences, including Champions, start through their authored keys.
            if (IsRecordVMD)
            {
                foreach (var container in CharaContainerScript)
                {
                    var rootbone = container.transform.Find("Position");
                    var newRecorder = rootbone.gameObject.AddComponent<UnityHumanoidVMDRecorder>();
                    newRecorder.UseParentOfAll = true;
                    newRecorder.UseAbsoluteCoordinateSystem = true;
                    newRecorder.Initialize();
                    if (!newRecorder.IsRecording)
                    {
                        newRecorder.StartRecording(true);
                    }
                }
            }
        }

        // Existing decoded stems are the host audio boundary; original CRI cue,
        // fade/callback handling and SongPart installation require their own port.
        internal void StartMainSongAudio(LiveSongStartRequest request)
        {
            if (request.Loop || request.FadeInTime != 0f || request.SongVolumeRate != 1f || request.CueName != null || request.StopType != 0 || request.OnComplete != null)
                throw new NotSupportedException("Decoded main-live adapter supports the original no-loop/no-fade request without a CRI callback.");
            LiveSongAudioControl.Start(this, request.StartTime);
        }

        private void OnUpdateFacialToon(ref FacialToonUpdateInfo info)
        {
            foreach (var locator in _liveTimelineControl.liveCharactorLocators)
                if (locator is LiveTimelineCharaLocator character && info.CharacterPos.hasFlag(locator.liveCharaStandingPosition) && character.UmaContainer != null)
                    character.UmaContainer.SetLiveFacialToon(ref info);
        }

        private void OnUpdateCharaWind(int physicalSlot, bool enabled, CySpringWindParam parameter)
        {
            if (physicalSlot < 0 || physicalSlot >= CharaContainerScript.Count)
                return;
            // The current Viewer owns one assembled model per physical slot.
            // Reserved dress-model lifecycle remains a separate conformance gap.
            CharaContainerScript[physicalSlot]?.SetLiveWind(enabled, parameter);
        }

        private void OnTimelineUpdate(float _liveCurrentTime)
        {
            {
                liveModelPhases.AlterUpdatePre();
                if (_effectController != null)
                {
                    bool seek = sliderControl.is_Touched || sliderControl.is_Outed || _liveCurrentTime < _effectLastTimelineTime;
                    if (seek && _liveCurrentTime != _effectLastTimelineTime)
                    {
                        _effectController.ResetForSeek();
                        _liveTimelineControl.ResetEffectTimelineForSeek();
                    }

                    _effectLastTimelineTime = _liveCurrentTime;
                }

                _liveTimelineControl.IsEnablePostEffect = GraphicSettings.IsBloom || GraphicSettings.IsDOF;
                float highlightDelta = _liveTimeController != null ? _liveTimeController.DeltaTimePauseReset : 0f;
                if (IsRecordVMD)
                    highlightDelta = _liveCurrentTime - _eyeHighlightLastTime;
                _eyeHighlightLastTime = _liveCurrentTime;
                foreach (var character in CharaContainerScript)
                    character.LiveEyeHighlight?.BeforeTimeline(_liveCurrentTime, highlightDelta);
                foreach (var character in CharaContainerScript)
                    character.FaceDrivenKeyTarget?.BeginLiveFacialSubmissions();
                try
                {
                    _liveTimelineControl.AlterUpdate(_liveCurrentTime);
                }
                finally
                {
                    foreach (var character in CharaContainerScript)
                        character.FaceDrivenKeyTarget?.EndLiveFacialSubmissions();
                }

                _titleController?.AlterUpdate(_liveTimeController != null ? _liveTimeController.DeltaTimePauseReset : 0f);
                _stageController?.UpdateWaveTime(_liveCurrentTime);
                _stageParticleController?.Pause(!IsRecordVMD && (IsPlaybackPaused || !_syncTime || sliderControl.is_Touched));
                _stageParticleController?.AlterUpdate(highlightDelta);
                foreach (var character in CharaContainerScript)
                    character.FaceDrivenKeyTarget?.AdvanceLiveFacialWeights(highlightDelta);
                foreach (var character in CharaContainerScript)
                    character.LiveEyeHighlight?.AfterTimeline();
                // Original Director.AlterUpdate flushes Stage after timeline update,
                // before the independent late camera/postprocessing phase.
                _stageController?.GetComponent<StageBlinkLightDriver>()?.AlterUpdateStageLights();
                _stageController?.AlterUpdateTransmittedLightMaskControllers();
                if (!_soloMode)
                {
                    UmaViewerAudio.AlterUpdate(SongAudioTime, partInfo, liveVocal, sliderControl.is_Outed);
                }

                liveModelPhases.AlterUpdate();
                liveModelPhases.AlterUpdatePost();
                EvaluateMiniCharacters();
            }
        }

        // Native Director._isEnabledStageController is an independently owned
        // gate; complete context installation must set it explicitly.
        public bool IsEnabledStageController { get; set; }

        float _eyeHighlightLastTime;
        // Adapter for LiveLoadSettings.StageObjectSelectTargetDictionary (+48).
        // Original ctor creates an empty dictionary. Full LiveLoadSettings is
        // still pending; authored selections consume actual supplied overrides.
        public Dictionary<int, int> StageObjectSelectTargetDictionary { get; } = new Dictionary<int, int>();

        private void ApplyTimelineLateUpdate()
        {
            {
                if (_lateTimelineAppliedThisFrame || _liveTimelineControl == null)
                    return;
                // GameSystem.LateUpdate 0x4ae9040: the global model PreLate,
                // IK1 and IK2 passes precede Constraint.Pre and scene late/cameras.
                {
                    liveModelPhases.AlterLateUpdatePre();
                    AlterConstraintLateUpdatePre();
                }

                // Original Director character AlterLateUpdate precedes props and
                // timeline camera sampling. Consume animation Position movement
                // once here, after Update's formation and motion samplers.
                float modelDelta = _liveTimeController != null ? _liveTimeController.DeltaTime : Time.deltaTime;
                if (IsRecordVMD)
                    modelDelta = _liveCurrentTime - _handShakeLastTime;
                foreach (var character in CharaContainerScript)
                {
                    character.UpdateLiveEmissiveTiming(_liveTimelineControl.Timescale, modelDelta, true);
                    character.LiveLocator?.AlterModelPositionAddParent();
                    if (character.LiveAnimationMode?.CurrentMode == 1)
                    {
                        character.LiveSystemTextIK?.UpdateWristIKBlend();
#if UNITY_EDITOR
                        if (character.LiveSystemTextIK != null)
                            ;
#endif
                    }

                    character.AdvanceLiveEmissiveScroll(Time.deltaTime);
                }

                if (_livePropsController != null)
                    _livePropsController.ScrollDeltaTime = modelDelta;
                _livePropsController?.Evaluate(_liveTimelineControl.data.worksheetList, _liveTimelineControl.CurrentFrame, _liveTimelineControl.PlayMode, _liveTimelineControl.currentLiveTime, _liveTimelineControl.CurrentWorkSheet);
                float propsDelta = _liveTimeController != null ? _liveTimeController.DeltaTimePauseReset : Time.deltaTime;
                if (IsRecordVMD)
                    propsDelta = _liveCurrentTime - _handShakeLastTime;
                _livePropsController?.BeginPhysics(propsDelta);
                // Original Director.AlterLateUpdate: PropsManager before timeline
                // camera sampling, so attached cameras read the current prop pose.
                _liveTimelineControl.AlterLateUpdate();
                _livePropsController?.EndPhysics();
                // Original Director 4e21a30: after timeline camera sampling, add the
                // HandShake result to the selected camera's local position once.
                {
                    var shakeCamera = MainRenderCamera;
                    LiveHandShakeCameraTimeline.Evaluate(_liveTimelineControl.CurrentWorkSheet?.handShakeCameraKeys, _liveTimelineControl.CurrentFrame, _liveTimelineControl.PlayMode, HandShake);
                    float shakeDelta = _liveTimeController != null ? _liveTimeController.DeltaTimePauseReset : _liveCurrentTime - _handShakeLastTime;
                    if (IsRecordVMD)
                        shakeDelta = _liveCurrentTime - _handShakeLastTime;
                    if (_liveCurrentTime < _handShakeLastTime)
                    {
                        HandShake.Reset();
                        shakeDelta = 0f;
                    }

                    _handShakeLastTime = _liveCurrentTime;
                    if (shakeCamera != null && HandShake.UpdateHandShakeCamera(shakeCamera.transform, shakeDelta, live.MusicId, _liveCurrentTime))
                        shakeCamera.transform.localPosition += HandShake.Position;
                    _transparentCamera?.Update(MainRenderCamera, _liveTimelineControl.TransparentCameraEnabled, _liveTimelineControl.TransparentCameraOrthographic, _liveTimelineControl.TransparentCameraOrthographicSize, _liveTimelineControl.TransparentCameraDefaultTransform);
                    UpdateLiveTitleCamera();
                    LiveViewerUI.Instance.UpdateLyrics(_liveCurrentTime, _liveTimelineControl != null ? _liveTimelineControl.SheetVariationId : 0);
                    LiveFlashController?.AlterUpdate();
                    ChampionsTextController?.AlterUpdate();

                    foreach (var character in CharaContainerScript)
                        character.FaceDrivenKeyTarget?.LiveEyeTrace?.AlterCameraLateUpdate(this, Mathf.Max(0, shakeDelta));
                    _stageController?.AlterLateUpdateBillboards();
                    _footLightRuntime?.AlterLateUpdate();
                    bool effectPaused = !IsRecordVMD && (IsPlaybackPaused || !_syncTime || sliderControl.is_Touched);
                    _effectController?.Pause(effectPaused);
                    // StageController.UpdateEffectStatus 0x4eb8af0 also updates the
                    // shared shader clock, independently of ParticleSystem simulation.
                    GraphicSettings.SetEffectTimeScale(effectPaused ? 0f : 1f);
                    _spotlightRuntime?.AlterLateUpdate();
                    _stageController?.GetComponent<StageWashLightDriver>()?.AlterLateUpdateForLive(MainRenderCamera);
                    AlterLateUpdateFakeLight();
                    _stageFlares?.AlterUpdate(MainRenderCamera);
                    if (_volumeLightController != null)
                    {
                        _volumeLightController.LateUpdateSunShafts(MainRenderCamera);
                        var parameter = Gallop.RenderPipeline.PostImageEffectFeature.RuntimeParameter;
                        parameter.SunShafts = _volumeLightController.SunShafts;
                        parameter.LightShafts = _volumeLightController.LightShafts;
                    }

                    UpdateDofMaxFocalSize();
                }

                // Submit this frame's FacialToon/GlobalLight state even when Unity
                // has already run a character's independent LateUpdate callback.
                {
                    foreach (var character in CharaContainerScript)
                    {
                        character?.UpdateLiveEnvironmentMirrorRuntime();
                        character?.UpdateLiveFacialToonMaterialRuntime();
                    }

                    AlterConstraintLateUpdateBeforeModel();
                    liveModelPhases.AlterLateUpdate();
                    liveModelPhases.AlterLateUpdatePost();
                }

                _lateTimelineAppliedThisFrame = true;
            }
        }

        // Both GameTime graphs and automatic Legacy stage animations are
        // evaluated by Unity between Update and LateUpdate. Scene/model late
        // phases must wait for both, independently of character motion mode.
        // The first model LateUpdate flushes any pending scene/model phases.
        // Director.LateUpdate also flushes them; the frame guard prevents repeats.
        // This also covers unsynchronized audio frames without a timeline advance.
        internal void ApplyDeferredLiveTimelineLateUpdate()
        {
            if (_isLiveSetup)
                ApplyTimelineLateUpdate();
        }

        bool isExit;
        void Update()
        {
            if (isExit)
                return;
            if (_isLiveSetup)
            {
                _lateTimelineAppliedThisFrame = false;
                if (!IsOfflineRendering && (Input.GetKeyDown(KeyCode.Escape) || _liveCurrentTime >= totalTime))
                {
                    ExitLive();
                }

                if (_syncTime == false)
                {
                    if (liveMusic?.sourceList == null || liveMusic.sourceList.Count == 0 || liveMusic.sourceList[0] == null)
                    {
                        _syncTime = true;
                    }
                    else if (liveMusic.sourceList[0].time > 0.01)
                    {
                        _liveCurrentTime = UI.ProgressBar.value * totalTime;
                        _liveCurrentTime = Mathf.Clamp(_liveCurrentTime, 0f, Mathf.Max(0f, totalTime - 0.001f));
                        _liveCurrentTime = SongPlaybackClock.Read(AudioSettings.dspTime, Time.realtimeSinceStartupAsDouble);
                        SetPlaybackClockTime(_liveCurrentTime);
                        _syncTime = true;
                    }
                }
                else
                {
                    if (IsRecordVMD)
                    {
                        _liveCurrentTime += (1 / 60f);
                        if (liveMusic != null)
                        {
                            UmaViewerAudio.Stop(liveMusic);
                            foreach (var vocal in liveVocal)
                            {
                                UmaViewerAudio.Stop(vocal);
                            }
                        }

                        UI.ProgressBar.SetValueWithoutNotify(_liveCurrentTime / totalTime);
                        OnTimelineUpdate(_liveCurrentTime);
                    }
                    else if (sliderControl.is_Outed)
                    {
                        _liveCurrentTime = UI.ProgressBar.value * totalTime;
                        SetPlaybackClockTime(_liveCurrentTime);
                        bool remainPaused = sliderControl.IsUserPaused || IsPlaybackPaused;
                        if (liveMusic != null && !remainPaused)
                            LiveSongAudioControl.Start(this, _liveCurrentTime);
                        OnTimelineUpdate(_liveCurrentTime);
                        sliderControl.is_Outed = false;
                        sliderControl.is_Touched = sliderControl.IsUserPaused;
                        // A held seek has no new audio playback to wait for.
                        _syncTime = remainPaused;
                    }
                    else if (sliderControl.is_Touched)
                    {
                        _liveCurrentTime = UI.ProgressBar.value * totalTime;
                        SetPlaybackClockTime(_liveCurrentTime);
                        if (liveMusic != null)
                        {
                            UmaViewerAudio.Stop(liveMusic);
                            foreach (var vocal in liveVocal)
                            {
                                UmaViewerAudio.Stop(vocal);
                            }
                        }

                        OnTimelineUpdate(_liveCurrentTime);
                    }
                    else
                    {
                        AdvancePlaybackClock(Time.deltaTime);
                        UI.ProgressBar.SetValueWithoutNotify(_liveCurrentTime / totalTime);
                        OnTimelineUpdate(_liveCurrentTime);
                    }
                }

                UpdateMainCamera();
                // 时间轴和主相机都更新完后再同步 Laser Renderer/朝向。
                // 这样既不会读取上一帧 LaserUpdateInfo，也不会读取上一帧相机姿态。
                if (_stageController != null)
                    _stageController.AlterUpdateLaserControllers();
            }
        }

        private void LateUpdate()
        {
            if (_isLiveSetup)
            {
                ApplyTimelineLateUpdate();
            }

            if (_enableMirrorReflection && _mirrorRenderInLateUpdate)
            {
                UpdateMirrorReflections();
            }

            UpdateEyeReflection();
        }

        // Eye reflection production wiring: the current eye camera key drives the
        // per-character reflection controller (enabled/power/fov/mask/culling).
        private void UpdateEyeReflection()
        {
            {
                if (_liveTimelineControl == null || CharaContainerScript == null || CharaContainerScript.Count == 0)
                {
                    return;
                }

                var sheet = _liveTimelineControl.GetWorkSheetBySheetIndex(LiveTimelineDefine.SheetIndex.MainLive);
                if (sheet == null || sheet.EyeCameraPosList == null || sheet.EyeCameraPosList.Count == 0)
                {
                    bool force = System.Environment.GetEnvironmentVariable("UMA_EYE_REFLECTION_FORCE") == "1";
                    for (int index = 0; index < CharaContainerScript.Count; index++)
                    {
                        var idle = CharaContainerScript[index];
                        if (idle == null)
                            continue;
                        var idleDriver = idle.GetComponent<LiveEyeReflectionDriver>();
                        if (idleDriver == null)
                            continue;
                        bool enable = force && index == 0;
                        if (enable)
                        {
                            var forced = LiveEyeReflectionContext.Default();
                            forced.Enabled = true;
                            forced.ReflectionPower = 1f;
                            forced.FieldOfView = 30f;
                            idleDriver.Context = forced;
                            idleDriver.Configure(true, 1f, null, 1, idle.HeadBone != null ? idle.HeadBone.transform : idle.transform);
                        }
                        else if (idleDriver.Enabled)
                        {
                            idleDriver.SetEnabled(false);
                        }
                    }

                    return;
                }

                for (int groupIndex = 0; groupIndex < sheet.EyeCameraPosList.Count; groupIndex++)
                {
                    var group = sheet.EyeCameraPosList[groupIndex];
                    if (group == null)
                        continue;
                    // Authored CharacterIndex is a physical slot, not an index into
                    // the compact loaded cast. Reuse the persistent owner registry.
                    int index = group.CharacterIndex;
                    var container = ResolvePhysicalFacialCharacter(index);
                    if (container == null)
                        continue;
                    var driver = container.GetComponent<LiveEyeReflectionDriver>();
                    if (driver == null)
                        continue;
                    var rates = _liveTimelineControl.data.characterSettings?.EyeReflectionTextureSizeRateArray;
                    int sizeRate = rates != null && index >= 0 && index < rates.Length ? rates[index] : 1;
                    _liveTimelineControl.ApplyEyeReflection(sheet, groupIndex, driver, sizeRate);
                }
            }
        }

        DateTime ExitTime;
        private void ExitLive()
        {
            isExit = true;
            if (_liveTimelineControl.IsRecordVMD)
            {
                ExitTime = DateTime.Now;
                SaveCameraVMD();
                SaveMultiCameraVMD();
                SaveCharacterVMD();
            }

            UmaSceneController.LoadScene("Version2", null, delegate
            {
                // 等旧 LiveScene 完全销毁后再清理，避免过场期间角色/舞台对象失去资源。
                UmaAssetManager.UnloadAllBundle(true);
            });
        }

        private void SaveCharacterVMD()
        {
            foreach (var container in CharaContainerScript)
            {
                var rootbone = container.transform.Find("Position");
                if (rootbone.gameObject.TryGetComponent(out UnityHumanoidVMDRecorder recorder))
                {
                    if (recorder.IsRecording)
                    {
                        recorder.StopRecording();
                        recorder.SaveLiveVMD(live, ExitTime, $"Live{live.MusicId}_Pos{CharaContainerScript.IndexOf(container)}", Config.Instance.VmdKeyReductionLevel);
                    }
                }
            }
        }

        private void SaveMultiCameraVMD()
        {
            for (int i = 0; i < _liveTimelineControl.data.worksheetList[0].multiCameraPosKeys.Count; i++)
            {
                var frames = _liveTimelineControl.MultiRecordFrames[i];
                frames[0].FovVaild = true;
                var fov = _liveTimelineControl.data.worksheetList[0].multiCameraPosKeys[i].keys.thisList;
                fov.ForEach(k =>
                {
                    var keyframe = frames.Find(f => f.frameIndex == k.frame);
                    if (keyframe != null)
                    {
                        var index = frames.IndexOf(keyframe);
                        keyframe.FovVaild = true;
                        if (index + 1 < frames.Count)
                            frames[index + 1].FovVaild = true;
                        if (index - 1 > 0)
                            frames[index - 1].FovVaild = true;
                        if (index - 2 > 0)
                            frames[index - 2].FovVaild = true;
                        if (index - 3 > 0)
                            frames[index - 3].FovVaild = true;
                    }
                });
                UnityCameraVMDRecorder.SaveLiveCameraVMD(live, ExitTime, frames, i);
            }
        }

        private void SaveCameraVMD()
        {
            var frames = _liveTimelineControl.RecordFrames;
            frames[0].FovVaild = true;
            var fov = _liveTimelineControl.data.worksheetList[0].cameraFovKeys.thisList;
            fov.ForEach(k =>
            {
                var keyframe = frames.Find(f => f.frameIndex == k.frame);
                if (keyframe != null)
                {
                    var index = frames.IndexOf(keyframe);
                    keyframe.FovVaild = true;
                    if (index + 1 < frames.Count)
                        frames[index + 1].FovVaild = true;
                    if (index - 1 > 0)
                        frames[index - 1].FovVaild = true;
                    if (index - 2 > 0)
                        frames[index - 2].FovVaild = true;
                    if (index - 3 > 0)
                        frames[index - 3].FovVaild = true;
                }
            });
            UnityCameraVMDRecorder.SaveLiveCameraVMD(live, ExitTime, frames);
        }

        public static List<UmaDatabaseEntry> GetLiveAllVoiceEntry(int songid, List<LiveCharacterLoadData> characters)
        {
            List<UmaDatabaseEntry> entryList = new List<UmaDatabaseEntry>();
            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i].CharaEntry.Name != "")
                {
                    var charaid = characters[i].CharaEntry.Id;
                    var entry = UmaViewerMain.Instance.AbSounds.FirstOrDefault(a => a.Name.Contains(string.Format(VOCAL_PATH, songid, charaid)) && a.Name.EndsWith("awb"));
                    // Fallback identities are selected once by PrepareMusic,
                    // after the score is loaded, then explicitly preloaded.
                    if (entry != null)
                    {
                        entryList.Add(entry);
                    }
                }
            }

            var bgEntry = UmaViewerMain.Instance.AbSounds.FirstOrDefault(a => a.Name.Contains(string.Format(SONG_PATH, songid)) && a.Name.EndsWith("awb"));
            if (bgEntry != null)
            {
                entryList.Add(bgEntry);
            }

            var voiceSettings = LiveSettingsResourceMap.Load(songid);
            string voiceSheet = LiveVoiceCueSheet.Select(songid, characters.Select(c => c.CharaEntry.Id), voiceSettings);
            if (LiveVoiceCueSheet.TryFindEntries(UmaViewerMain.Instance.AbSounds, voiceSheet, out var voiceAcb, out var voiceAwb))
            {
                entryList.Add(voiceAcb);
                if (voiceAwb != null)
                    entryList.Add(voiceAwb);
            }

            return entryList;
        }

        public static List<UmaDatabaseEntry> GetLivePreloadEntries(LiveEntry live, List<LiveCharacterLoadData> characters, bool requireStage)
        {
            var result = new List<UmaDatabaseEntry>();
            if (live == null)
                return result;
            result.AddRange(GetLiveAllVoiceEntry(live.MusicId, characters));
            var main = UmaViewerMain.Instance;
            if (main == null || main.AbList == null)
                return result;
            void AddByKey(string key)
            {
                if (main.AbList.TryGetValue(key, out var entry) && entry != null)
                    result.Add(entry);
            }

            // Cutt 和歌曲 part 也提前加载，避免进入场景后同步卡顿。
            AddByKey(string.Format(CUTT_PATH, live.MusicId));
            AddByKey(string.Format(LIVE_PART_PATH, live.MusicId));
            // Target GetLivePreloadEntries calls ResourcePath.GetCyalumeScorePath
            // and passes its result to AddByKey (RVA 0x1a5c18f -> 0x1a5c19f).
            AddByKey(string.Format(CYALUME_SCORE_PATH, live.MusicId));
            if (requireStage && !string.IsNullOrEmpty(live.BackGroundId))
            {
                string folderPrefix = $"3d/env/live/live{live.BackGroundId}/";
                // 保留该舞台目录下全部 AssetBundle，不删 laser、light、monitor 等任何资源。
                foreach (var kv in main.AbList)
                {
                    UmaDatabaseEntry entry = kv.Value;
                    if (entry == null || !entry.IsAssetBundle)
                        continue;
                    bool keyMatches = kv.Key.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase);
                    bool nameMatches = entry.Name != null && entry.Name.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase);
                    if (keyMatches || nameMatches)
                        result.Add(entry);
                }
            }

            return result.Where(e => e != null && !string.IsNullOrEmpty(e.Name)).GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        }

        private void PreloadStageBundlesBeforeInstantiate(string bgId)
        {
            if (string.IsNullOrEmpty(bgId))
                return;
            var main = UmaViewerMain.Instance;
            if (main == null || main.AbList == null)
                return;
            string folderPrefix = $"3d/env/live/live{bgId}/";
            var required = new List<UmaDatabaseEntry>();
            foreach (var kv in main.AbList)
            {
                UmaDatabaseEntry entry = kv.Value;
                if (entry == null || !entry.IsAssetBundle)
                    continue;
                bool keyMatches = kv.Key.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase);
                bool nameMatches = entry.Name != null && entry.Name.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase);
                if (keyMatches || nameMatches)
                    required.AddRange(UmaAssetManager.SearchAB(main, entry));
            }

            // 兜底路径也只做“新增加载”，不调用任何 Unload；依赖去重后每个只处理一次。
            foreach (UmaDatabaseEntry entry in required.Where(e => e != null && !string.IsNullOrEmpty(e.Name)).GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
            {
                UmaAssetManager.LoadAssetBundle(entry, neverUnload: false, isRecursive: false);
            }

            Debug.Log($"[StagePreloadFallback] bgId={bgId}, bundles={required.Count}");
        }

        private void InitializeMirrorReflections()
        {
            foreach (var oldMirror in _mirrorReflections)
                if (oldMirror != null)
                {
                    oldMirror.RemoveBeginCameraRenderingCallback(OnBeginModelCamera);
                    oldMirror.RemoveEndCameraRenderingCallback(OnEndModelCamera);
                }

            _stageController?.DestroyMirrorReflections();
            _mirrorReflections.Clear();
            if (!_enableMirrorReflection)
                return;
            AddMirrorReflections(_mirrorReflections, GetComponentsInChildren<MirrorReflection>(true));
            if (_stageController != null)
                AddMirrorReflections(_mirrorReflections, _stageController.GetComponentsInChildren<MirrorReflection>(true));
            if (_mirrorReflections.Count == 0)
                AddMirrorReflections(_mirrorReflections, FindObjectsOfType<MirrorReflection>(true));
            if (_mirrorReflections.Count == 0)
            {
                // Stage prefabs load after Director initialization in the
                // viewer. Native prefab components exist once the stage is
                // instantiated, so retry from the mirror late-update instead
                // of permanently leaving the native components uninitialized.
                _mirrorInitPending = true;
                return;
            }

            _mirrorInitPending = false;
            Camera mainCam = null;
            if (_cameraObjects != null && _activeCameraIndex >= 0 && _activeCameraIndex < _cameraObjects.Length)
                mainCam = _cameraObjects[_activeCameraIndex];
            if (mainCam == null)
                mainCam = Camera.main;
            if (_stageController != null)
            {
                _stageController.InitializeMirrorReflections(_mirrorReflections, mainCam, GetMainCameraFovFactor, _liveTimelineControl, CharaContainerScript);
            }
            else
            {
                for (int i = 0; i < _mirrorReflections.Count; i++)
                {
                    var mirror = _mirrorReflections[i];
                    if (mirror == null)
                        continue;
                    mirror.Initialize(mainCam, i, false);
                    mirror.SetupBaseCamera(mainCam, GetMainCameraFovFactor);
                }
            }

            Debug.Log($"[Mirror] Initialized {_mirrorReflections.Count} mirrors.");
            foreach (var mirror in _mirrorReflections)
                if (mirror != null)
                {
                    mirror.RemoveBeginCameraRenderingCallback(OnBeginModelCamera);
                    mirror.RemoveEndCameraRenderingCallback(OnEndModelCamera);
                    mirror.AddBeginCameraRenderingCallback(OnBeginModelCamera);
                    mirror.AddEndCameraRenderingCallback(OnEndModelCamera);
                }
        }

        private void BindModelQueueCameraCallbacks()
        {
            if (_modelQueueCallbacksBound)
                return;
            RenderPipelineManager.beginCameraRendering += OnBeginModelCamera;
            RenderPipelineManager.endCameraRendering += OnEndModelCamera;
            Camera.onPreCull += OnBeginBuiltInModelCamera;
            Camera.onPostRender += OnEndBuiltInModelCamera;
            _modelQueueCallbacksBound = true;
        }

        private void OnBeginModelCamera(ScriptableRenderContext context, Camera camera)
        {
            if (!_isLiveSetup || camera == null || camera.cameraType == CameraType.SceneView || camera.cameraType == CameraType.Preview)
                return;
            _modelRenderQueue.BeginCamera(camera);
        }

        private void OnEndModelCamera(ScriptableRenderContext context, Camera camera)
        {
            if (!_isLiveSetup || camera == null || camera.cameraType == CameraType.SceneView || camera.cameraType == CameraType.Preview)
                return;
            _modelRenderQueue.EndCamera(camera);
        }

        private void OnBeginBuiltInModelCamera(Camera camera)
        {
            if (GraphicsSettings.currentRenderPipeline == null)
                OnBeginModelCamera(default, camera);
        }

        private void OnEndBuiltInModelCamera(Camera camera)
        {
            if (GraphicsSettings.currentRenderPipeline == null)
                OnEndModelCamera(default, camera);
        }

        private void UnbindModelQueueCameraCallbacks()
        {
            if (_modelQueueCallbacksBound)
            {
                RenderPipelineManager.beginCameraRendering -= OnBeginModelCamera;
                RenderPipelineManager.endCameraRendering -= OnEndModelCamera;
                Camera.onPreCull -= OnBeginBuiltInModelCamera;
                Camera.onPostRender -= OnEndBuiltInModelCamera;
                _modelQueueCallbacksBound = false;
            }

            foreach (var mirror in _mirrorReflections)
                if (mirror != null)
                {
                    mirror.RemoveBeginCameraRenderingCallback(OnBeginModelCamera);
                    mirror.RemoveEndCameraRenderingCallback(OnEndModelCamera);
                }

            _modelRenderQueue.Clear();
        }

        private static void AddMirrorReflections(List<MirrorReflection> target, MirrorReflection[] mirrors)
        {
            if (target == null || mirrors == null)
                return;
            for (int i = 0; i < mirrors.Length; i++)
            {
                var mirror = mirrors[i];
                if (mirror == null || target.Contains(mirror))
                    continue;
                target.Add(mirror);
            }
        }

        private float GetMainCameraFovFactor()
        {
            var camera = MainRenderCamera;
            var cameraData = camera != null ? camera.GetComponent<Gallop.RenderPipeline.CameraData>() : null;
            return cameraData != null ? cameraData.FieldOfViewFactor : 1f;
        }

        internal void SetupMirrorReflectionBaseCamera(MirrorReflection mirror, LiveTimelineDefine.MirrorReflectionBaseCameraType cameraType, int cameraIndex)
        {
            if (mirror == null)
                return;
            if (cameraType == LiveTimelineDefine.MirrorReflectionBaseCameraType.MultiCamera)
            {
                var selected = _multiCameras != null && cameraIndex >= 0 && cameraIndex < _multiCameras.Length ? _multiCameras[cameraIndex] : null;
                if (selected == null)
                {
                    // Original failed TryGetMultiCameraInfo leaves the prior
                    // base camera intact and clears its factor delegate.
                    mirror.SetFovFactorGetter(null);
                    return;
                }

                var camera = selected.GetCamera();
                if (camera != null)
                    mirror.SetBaseCamera(camera);
                mirror.SetFovFactorGetter(() =>
                {
                    var cameraData = camera != null ? camera.GetComponent<Gallop.RenderPipeline.CameraData>() : null;
                    return cameraData != null ? cameraData.FieldOfViewFactor : 1f;
                });
            }
            else
            {
                var camera = MainRenderCamera;
                if (camera != null)
                    mirror.SetBaseCamera(camera);
                mirror.SetFovFactorGetter(GetMainCameraFovFactor);
            }
        }

        private void UpdateMirrorReflections()
        {
            if (_mirrorInitPending)
            {
                InitializeMirrorReflections();
                if (_mirrorReflections.Count == 0)
                    return;
            }

            _stageController?.PrepareMirrorCharacters();
            if (_mirrorReflections == null || _mirrorReflections.Count == 0)
                return;
            Camera mainCam = null;
            if (_cameraObjects != null && _activeCameraIndex >= 0 && _activeCameraIndex < _cameraObjects.Length)
                mainCam = _cameraObjects[_activeCameraIndex];
            if (mainCam == null)
                mainCam = Camera.main;
            for (int i = 0; i < _mirrorReflections.Count; i++)
            {
                var mirror = _mirrorReflections[i];
                if (mirror == null)
                    continue;
                // Native MirrorReflection keeps its camera enabled and lets the
                // pipeline render it; Director only prepares the mirror actors.
                mirror.PrepareAutomaticRender();
            }
        }

        private GallopImageEffect GetActivePostEffect() => NativePostEffect();
        private string _lastDofBypassReason;
        private float GetMaxForcalSize()
        {
            // Actual game RVA 0x71439d0 / AlterLateUpdate 0x713ec50.
            // 0x0da24260 is the native 1e-30 fallback; never accumulate sampled sizes.
            var data = _liveTimelineControl != null ? _liveTimelineControl.data : null;
            return Mathf.Max(1e-30f, data != null ? data.maxForcalSize : 0f);
        }

        private void UpdateDofMaxFocalSize()
        {
            float maximum = GetMaxForcalSize();
            if (_cameraImageEffects != null)
                for (int i = 0; i < _cameraImageEffects.Length; ++i)
                    if (_cameraImageEffects[i] != null)
                    {
                        _cameraImageEffects[i].DofDiffusionBloomOverlayParam.DofMaxFocalSize = maximum;
                        _cameraImageEffects[i].RenderParameter.DofDiffuionBloomOverlay.DofMaxFocalSize = maximum;
                    }

            if (_multiCameras != null)
                for (int i = 0; i < _multiCameras.Length; ++i)
                    if (_multiCameras[i] != null)
                        _multiCameras[i].PostEffectParameter.DofDiffuionBloomOverlay.DofMaxFocalSize = maximum;
            GetActivePostEffect()?.ApplyBloomParameter();
        }

        private void OnUpdatePostEffect_Dof(LiveTimelineKeyPostEffectDOFData key, Vector3? worldFocus)
        {
            var effect = GetActivePostEffect();
            if (effect == null)
                return;
            var param = effect.DofDiffusionBloomOverlayParam;
            param.IsEnableDof = false;
            param.IsEnableOldDof = false;
            bool dofParametersUpdated = false;
            var renderCamera = MainRenderCamera;
            string bypassReason = null;
            // [REPORT §10.3-B3] 原生无跨相机 DOF bypass（专用多相机 DOF 轨道是独立轨道，
            // 主 DOF 轨道始终作用于活跃相机）。删除 master7 自造的
            // "renderCamera != DofTrackCamera（引用相等）即整体关闭 DOF"——
            // 该绑定导致多相机/镜面反射/相机切换帧整帧失焦。
            // 仅保留 renderCamera==null 的退化保护：WorldToViewportPoint/nearClipPlane
            // 求值需要相机，null 相机时无法算焦点（此时场景本身也无渲染相机）。
            if (key != null && renderCamera == null)
                bypassReason = "no render camera available for DOF evaluation";
            if (key != null && bypassReason == null)
            {
                // Actual game SetupDOFUpdateInfo RVA 0x726a610: bit18 enables DOF;
                // bit16 selects camera look-at, bit17 a metric focal point.
                bool metric = ((int)key.attribute & (1 << 17)) != 0;
                Vector3 focus = worldFocus.GetValueOrDefault();
                bool valid = metric || worldFocus.HasValue;
                // Native PrepareDofParam clamps normalized negative depth, not the
                // authored focal point. Missing targets remain a DOF-only bypass.
                float focusDepth = metric ? key.dofFocalPoint : renderCamera.WorldToViewportPoint(focus).z;
                valid = valid && !float.IsNaN(focusDepth) && !float.IsInfinity(focusDepth);
                if (!valid)
                    bypassReason = "missing or invalid focus depth";
                param.IsEnableDof = valid && ((int)key.attribute & (1 << 18)) != 0;
                // Source setters select the mode; write only the selected coordinate.
                if (metric)
                    param.DofFocalPoint = key.dofFocalPoint;
                else
                    param.DofFocalPosition = focus;
                param.DofQualityType = (DepthBlurAndBloom.DofQuality)key.dofQuality;
                param.DofFocalSize = key.forcalSize;
                param.DofMaxFocalSize = GetMaxForcalSize();
                param.DofMaxBlurSpread = key.blurSpread;
                param.DofForegroundSize = key.dofForegroundSize;
                param.DofSmoothness = key.dofSmoothness;
                param.DofBlurType = (DepthBlurAndBloom.DofBlur)key.dofBlurType;
                param.BallBlurPowerFactor = key.BallBlurPowerFactor;
                param.BallBlurBrightnessThreshhold = key.BallBlurBrightnessThreshhold;
                param.BallBlurBrightnessIntensity = key.BallBlurBrightnessIntensity;
                param.BallBlurSpread = key.BallBlurSpread;
                param.IsPointBallBlur = ((int)key.attribute & (1 << 19)) != 0;
                dofParametersUpdated = true;
            }

            if (bypassReason != _lastDofBypassReason)
            {
                _lastDofBypassReason = bypassReason;
                if (bypassReason != null)
                    Debug.LogWarning($"[LiveDOF] Bypass DOF only: {bypassReason}; time={_liveCurrentTime:F3}, " + $"camera={renderCamera?.name}, key={key?.frame}, attr=0x{(key != null ? (int)key.attribute : 0):X}");
            }

            effect.ApplyBloomParameter();
            if (dofParametersUpdated)
            {
                // Native DOF writes these runtime fields directly. Generic
                // Parameter.Setup deliberately does not copy BallBlur state.
                ref var render = ref effect.RenderParameter.DofDiffuionBloomOverlay;
                render.BallBlurPowerFactor = param.BallBlurPowerFactor;
                render.BallBlurBrightnessThreshhold = param.BallBlurBrightnessThreshhold;
                render.BallBlurBrightnessIntensity = param.BallBlurBrightnessIntensity;
                render.BallBlurSpread = param.BallBlurSpread;
                render.IsPointBallBlur = param.IsPointBallBlur;
            }
        }

        private Gallop.ExcludeColorCorrection _excludeColorCorrection;
        private readonly Gallop.Live.LiveVoicePlayer _voicePlayer = new Gallop.Live.LiveVoicePlayer();
        private string _voicePlayError;
        // Original Director.TryPlayVoice 0x4e3c350: the cue sheet path comes from
        // RegisterSongCueSheet/ResourcePath.GetLiveVoiceCueSheetPath(musicId).
        public bool TryPlayVoice(int cueId, float startTime)
        {
            if (live == null)
            {
                _voicePlayError = "no live";
                return false;
            }

            var main = UmaViewerMain.Instance;
            if (main == null || main.AbSounds == null)
            {
                _voicePlayError = "no AbSounds";
                return false;
            }

            string sheet = LiveVoiceCueSheet.Select(live.MusicId, CharaContainerScript.Select(c => c.CharaEntry.Id), _liveSettingsResources ?? LiveSettingsResourceMap.Load(live.MusicId), _liveTimelineControl.SheetVariationId);
            if (!LiveVoiceCueSheet.TryFindEntries(main.AbSounds, sheet, out var acb, out var awb))
            {
                _voicePlayError = "missing live voice ACB: " + sheet;
                Debug.LogWarning("[LiveVoice] " + _voicePlayError);
                return false;
            }

            bool ok = _voicePlayer.TryPlay(acb.FilePath, awb?.FilePath, cueId, startTime, 1f, out string error);
            _voicePlayError = ok ? null : error;
            if (!ok)
                Debug.LogWarning("[LiveVoice] " + _voicePlayError);
            return ok;
        }

        public void StopVoiceAll()
        {
            _voicePlayer.Stop();
        }

        // Original IsEnableVariationId 0x4e2b710 checks the context's enabled
        // variation list; the Viewer exposes the single active sheet variation id.
        public bool IsEnableVariationId(int variationId) => _liveTimelineControl != null && _liveTimelineControl.SheetVariationId == variationId;
        private void OnVoicePlay(int cueId, float startTime)
        {
            TryPlayVoice(cueId, startTime);
        }

        // Native Director.OnUpdatePreColorCorrection 0x4e2f9d0 forwards to
        // ExcludeColorCorrection.OnUpdatePreColorCorrection.
        private void OnUpdatePreColorCorrection(Gallop.Live.Cutt.PreColorCorrectionUpdateInfo info)
        {
            var camera = MainRenderCamera;
            if (camera == null)
                return;
            if (_excludeColorCorrection == null)
                _excludeColorCorrection = new Gallop.ExcludeColorCorrection(camera.transform, CharaContainerScript, camera);
            _excludeColorCorrection.OnUpdatePreColorCorrection(info, camera);
        }

        private void OnUpdateColorCorrection(Gallop.RenderPipeline.SimpleColorCorrectionPass.Parameter sample)
        {
            // Installed OnUpdateColorCorrection0x4e2df70 writes0x25c=604.
            // This main timeline callback must not change MultiCamera's default550.
            sample.RenderPassEvent = (UnityEngine.Rendering.Universal.RenderPassEvent)604;
            _mainColorCorrection = sample;
            Gallop.RenderPipeline.PostImageEffectFeature.RuntimeParameter.ColorCorrection = sample;
        }

        // Native 0x4e2de50 updates only the addressed MultiCamera image effect.
        // Its default color event remains550; no main-camera604 owner callback.
        private void OnUpdateColorCorrectionMultiCamera(Gallop.RenderPipeline.SimpleColorCorrectionPass.Parameter sample, int index)
        {
            if (_multiCameras == null || index < 0 || index >= _multiCameras.Length || _multiCameras[index] == null)
                return;
            _multiCameras[index].PostEffectParameter.ColorCorrection = sample;
        }

        public void ApplyColorCorrectionParameter(ref Gallop.RenderPipeline.SimpleColorCorrectionPass.Parameter parameter)
        {
            // Equivalent ownership boundary to native0x4e22b70. No blending or
            // white balance; also copies the disabled state when a track is absent.
            parameter = _mainColorCorrection;
        }

        // [A7b] 原生 Director.OnUpdateExposure(RVA=0x1a63430) 三连：
        // GetActivePostEffect → get_ExposureParam 整块覆盖写（0x1a6349e-0x1a634ae，
        // IsEnable/DepthMask/Gain/Lift/MaskGain/MaskLift 顺序）→ 空值门后
        // CameraData.UpdateImageEffectParameter(0x1a0da30)。master7 的参数发布
        // 由 ApplyBloomParameter 统一完成（含 TargetCamera/DOF），因此这里只写参数，
        // 末尾调 ApplyBloomParameter 等价原生 UpdateImageEffectParameter。
        private void OnUpdateExposure(LiveExposureToneCurveTimeline.ExposureUpdateInfo info)
        {
            var effect = GetActivePostEffect();
            if (effect == null)
                return;
            // 原生无 null 门（事件触发即写），这里 GetActivePostEffect 的 null
            // 返回已在上方处理；直接整块覆盖。
            effect.ExposureParam.Set(info.IsEnable, info.DepthMask, info.Gain, info.Lift, info.MaskGain, info.MaskLift);
            effect.ApplyBloomParameter();
        }

        // [A7b] 原生 Director.OnUpdateToneCurve(RVA=0x1a63f80) 三连：
        // GetActivePostEffect → get_ToneCurveParam → IsValidity 门(0x1a64000)
        // 通过才拷贝曲线引用（0x1a64005-0x1a64026）→ UpdateImageEffectParameter。
        private void OnUpdateToneCurve(LiveExposureToneCurveTimeline.ToneCurveUpdateInfo info)
        {
            var effect = GetActivePostEffect();
            if (effect == null)
                return;
            effect.ToneCurveParam.Set(info.IsEnable, info.ToneCurve, info.MaskToneCurve);
            effect.ApplyBloomParameter();
        }

        private void OnUpdateRadialBlur(Gallop.RenderPipeline.RadialBlurPass.Parameter sample)
        {
            var effect = NativePostEffect();
            if (effect != null)
                LiveRadialBlurTimeline.Apply(ref effect.RenderParameter.RadialBlur, sample);
        }

        private void OnUpdateTransmittedLight(LiveTransmittedLightTimeline.UpdateInfo info)
        {
            LiveTransmittedLightTimeline.ApplyToCameraArray(_cameraImageEffects, _activeCameraIndex, 0, info);
        }

        private void OnUpdateTransmittedLightMultiCamera(LiveTransmittedLightTimeline.UpdateInfo info, int index)
        {
            LiveTransmittedLightTimeline.ApplyToMultiCamera(_multiCameras, index, info);
        }

        private void OnUpdateGlobalFog(LiveGlobalFogTimeline.UpdateInfo info)
        {
            LiveGlobalFogTimeline.ApplyToCameraArray(_cameraImageEffects, _activeCameraIndex, 0, info);
        }

        private void OnUpdateEdgeStyle(ref EdgeStyleUpdateInfo info)
        {
            LiveEdgeStyleTimeline.ApplyToCameraArray(_cameraImageEffects, _activeCameraIndex, 0, _liveCurrentTime, info);
        }

        private void OnUpdatePostFilm(int layer, Gallop.ImageEffect.ScreenOverlay.Overlay sample)
        {
            var effect = GetActivePostEffect();
            if (effect == null)
                return;
            var overlay = effect.DofDiffusionBloomOverlayParam.ScreenOverlay;
            var target = layer == 0 ? overlay.Overlay1 : layer == 1 ? overlay.Overlay2 : overlay.Overlay3;
            LivePostFilmTimeline.Copy(sample, target);
            // Movie fields are runtime state excluded by Overlay.Setup.
            // Publish only the selected layer of this active camera.
            ref var render = ref effect.RenderParameter.DofDiffuionBloomOverlay;
            if (layer == 0)
                render.Overlay1.SetMovieInfo(sample.MovieTexture, sample.MovieMaskTexture, sample.MovieTextureScale, sample.MovieTextureOffset);
            else if (layer == 1)
                render.Overlay2.SetMovieInfo(sample.MovieTexture, sample.MovieMaskTexture, sample.MovieTextureScale, sample.MovieTextureOffset);
            else
                render.Overlay3.SetMovieInfo(sample.MovieTexture, sample.MovieMaskTexture, sample.MovieTextureScale, sample.MovieTextureOffset);
            // Publish only after all three layers, including disabled/empty tracks,
            // have replaced the active camera's old state.
            if (layer == 2)
                effect.ApplyBloomParameter();
        }

        private void OnUpdatePostEffect_BloomDiffusion(PostEffectUpdateInfo_BloomDiffusion updateInfo)
        {
            GallopImageEffect imageEffect = GetActivePostEffect();
            if (imageEffect == null)
                return;
            DofDiffusionBloomOverlayParam param = imageEffect.DofDiffusionBloomOverlayParam;
            param.IsEnableBloom = updateInfo.IsEnabledBloom;
            param.BloomDofWeight = updateInfo.bloomDofWeight;
            param.BloomThreshold = updateInfo.threshold;
            param.BloomIntensity = updateInfo.intensity;
            param.BloomBlurSize = updateInfo.BloomBlurSize;
            param.BloomBlendMode = updateInfo.BloomBlendMode;
            param.IsEnableDiffusion = updateInfo.IsEnabledDiffusion;
            param.DiffusionBlurSize = updateInfo.diffusionBlurSize;
            param.DiffusionBright = updateInfo.diffusionBright;
            param.DiffusionThreshold = updateInfo.diffusionThreshold;
            param.DiffusionSaturation = updateInfo.diffusionSaturation;
            param.DiffusionContrast = updateInfo.diffusionContrast;
            imageEffect.ApplyBloomParameter();
        //         Debug.Log(
        // $"[BloomDirector] activeCameraIndex={_activeCameraIndex}, " +
        // $"imageEffect={(imageEffect != null ? imageEffect.name : "null")}");
        }

        private void OnUpdateVolumeLight(LiveVolumeLightTimeline.VolumeUpdateInfo info)
        {
            _volumeLightController?.UpdateVolume(info);
        }

        private void OnUpdateLightShafts(LiveVolumeLightTimeline.ShaftsUpdateInfo info)
        {
            _volumeLightController?.UpdateLightShafts(info);
        }

        private void OnDestroy()
        {
            ScreenCaptures?.Release();
            BackgroundImages?.Release();
            ReleaseLiveTitle();
            ReleaseLiveFlash();
            if (_liveTimelineControl != null)
                _liveTimelineControl.OnUpdateLipSyncPattern -= OnUpdateLipSyncPattern;
            ReleaseLipSyncPatternAsset();
            UnbindModelQueueCameraCallbacks();
            _transparentCamera?.Dispose();
            _monitorCameraDriver?.SetupCaptureTexture(null);
            ReleaseMonitorTextures();
            _stageController?.DestroyMirrorReflections();
            UnbindTimelineEvents();
            ReleaseMiniCharacters();
            DestroyMiniCharaCamera();
            _footLightRuntime?.Dispose();
            _footLightRuntime = null;
            _livePropsController?.Dispose();
            _livePropsController = null;
            _stageParticleController?.Dispose();
            _effectController?.Dispose();
            _spotlightRuntime?.Dispose();
            _lightProjectionController?.Dispose();
            _mirrorBallProjectionTextures.Clear();
            _effectStageObjects.Clear();
            if (_instance == this)
            {
                Gallop.RenderPipeline.PostImageEffectFeature.ResetRuntimeParameter();
                _instance = null;
            }
        }

        private void UnbindTimelineEvents()
        {
            if (_liveTimelineControl == null)
                return;
            _liveTimelineControl.OnUpdateFacialToon -= OnUpdateFacialToon;
            UnbindCharaNodeOffset();
            UnbindParentConstraint();
            _liveTimelineControl.OnUpdateCharaWind -= OnUpdateCharaWind;
            UnbindSweatLocator();
            if (_footLightRuntime != null)
            {
                _liveTimelineControl.OnUpdateBgColor1 -= _footLightRuntime.ApplySpotlight;
                _liveTimelineControl.OnUpdateBgColor1 -= _footLightRuntime.ApplyShadow;
            }

            _liveTimelineControl.OnUpdatePostEffect_Dof -= OnUpdatePostEffect_Dof;
            _liveTimelineControl.OnUpdatePostFilm -= OnUpdatePostFilm;
            _liveTimelineControl.OnUpdateFade -= OnUpdateFade;
            _liveTimelineControl.OnUpdateRadialBlur -= OnUpdateRadialBlur;
            UnsubscribeNativePostTimeline(_liveTimelineControl);
            UnsubscribeChromaticAberrationTimeline(_liveTimelineControl);
            _liveTimelineControl.OnUpdateColorCorrection -= OnUpdateColorCorrection;
            _liveTimelineControl.OnUpdateColorCorrectionMultiCamera -= OnUpdateColorCorrectionMultiCamera;
            _liveTimelineControl.OnUpdateGlobalFog -= OnUpdateGlobalFog;
            _liveTimelineControl.OnUpdateEdgeStyle -= OnUpdateEdgeStyle;
            _liveTimelineControl.OnUpdateTransmittedLight -= OnUpdateTransmittedLight;
            _liveTimelineControl.OnUpdateTransmittedLightMultiCamera -= OnUpdateTransmittedLightMultiCamera;
            _liveTimelineControl.OnUpdatePostEffect_BloomDiffusion -= OnUpdatePostEffect_BloomDiffusion;
            // [A7b] 原生 UnbindTimelineEvents(RVA=0x1a69920) 对应解除两条轨道。
            _liveTimelineControl.OnUpdateExposure -= OnUpdateExposure;
            _liveTimelineControl.OnUpdateToneCurve -= OnUpdateToneCurve;
            _liveTimelineControl.OnUpdateVolumeLight -= OnUpdateVolumeLight;
            _liveTimelineControl.OnUpdateLightShafts -= OnUpdateLightShafts;
            if (_stageParticleController != null)
            {
                _liveTimelineControl.OnUpdateParticle -= _stageParticleController.Update;
                _liveTimelineControl.OnUpdateParticleGroup -= _stageParticleController.UpdateGroup;
            }

            if (_effectController != null)
            {
                _liveTimelineControl.OnUpdateEffect -= _effectController.Update;
                _liveTimelineControl.OnUpdateEffectScale -= _effectController.UpdateScale;
            }

            if (_spotlightRuntime != null)
                _liveTimelineControl.OnUpdateSpotlight3d -= _spotlightRuntime.Update;
            if (_lightProjectionController != null)
                _liveTimelineControl.OnUpdateLightProjection -= _lightProjectionController.Apply;
        }
    }
}
