using System;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System.IO;
using Gallop.Live;

namespace Gallop.Live.Cutt
{
    public partial class LiveTimelineControl : MonoBehaviour
    {
        public LiveTimelineData data;
        public ILiveTitleTarget TitleController { get; set; }
        public ILiveFlashController FlashController { get; set; }
        public LiveChampionsTextController ChampionsTextController { get; set; }
        public bool IsSequencialPlay { get; set; }
        // Original Director supplies LiveContext.Settings.SheetVariationId.
        // Host default is the native default variation, not the final duplicate.
        public int SheetVariationId { get; set; }

        private int _currentWorkSheetIndex;
        private int[] _sheetIndices;
        public LiveTimelineWorkSheet CurrentWorkSheet => data.GetWorkSheet(_currentWorkSheetIndex);

        public void InitSheetTable()
        {
            int count = data.worksheetList.Count;
            _sheetIndices = new[]
            {
                -1,
                -1,
                -1
            };
            for (int i = 0; i < count; i++)
                _sheetIndices[(int)data.worksheetList[i].SheetType] = i;
            int present = 0;
            for (int i = 0; i < 3; i++)
                if (_sheetIndices[i] != -1)
                    present++;
            IsSequencialPlay = present > 1;
        }

        public int GetSheetIndex(LiveTimelineDefine.SheetIndex index) => _sheetIndices[(int)index];
        public bool TrySetupSheetIndexTable(int sheetVariationId)
        {
            // Original 4ef7690 preserves entries with no matching variant and
            // writes each match in worksheet order; last match of a type wins.
            bool changed = false;
            int count = data.worksheetList.Count;
            for (int index = 0; index < count; index++)
            {
                var sheet = data.GetWorkSheet(index);
                if (sheet.SheetVariationId != sheetVariationId)
                    continue;
                _sheetIndices[(int)sheet.SheetType] = index;
                changed = true;
            }

            return changed;
        }

        public bool IsSheet(LiveTimelineDefine.SheetIndex index) => GetSheetIndex(index) >= 0;
        public bool SetCurrentSheet(LiveTimelineDefine.SheetIndex index)
        {
            int value = GetSheetIndex(index);
            if (value < 0)
                return false;
            _currentWorkSheetIndex = value;
            return true;
        }

        public LiveTimelineDefine.SheetIndex GetCurrentSheetType() => CurrentWorkSheet.SheetType;
        public void SetFirstSheetToCurrentSheetNo()
        {
            for (int i = 0; i < _sheetIndices.Length; i++)
                if (_sheetIndices[i] >= 0)
                {
                    _currentWorkSheetIndex = _sheetIndices[i];
                    return;
                }
        }

        public event Action<int, Gallop.ImageEffect.ScreenOverlay.Overlay> OnUpdatePostFilm;
        public event Action<Color> OnUpdateFade;
        // Native AlterUpdate_Fade 0x4ecfe40 -> Director.OnUpdateFade 0x4e2e6f0.
        private void AlterUpdate_Fade(LiveTimelineWorkSheet sheet, int frame)
        {
            var keys = sheet.fadeKeys;
            if (OnUpdateFade == null || keys == null || keys.Count == 0 || keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) || !keys.EnablePlayModeTimeline(_playMode))
                return;
            FindTimelineKey(out var current, out var next, keys, frame);
            var a = current as LiveTimelineKeyFadeData;
            var b = next as LiveTimelineKeyFadeData;
            if (a == null)
                return;
            OnUpdateFade(b != null && b.IsInterpolateKey() ? FadeBlinkColorLerp(a.fadeColor, b.fadeColor, CalculateInterpolationValue(a, b, frame)) : a.fadeColor);
        }

        private readonly Gallop.ImageEffect.ScreenOverlay _postFilmSamples = new Gallop.ImageEffect.ScreenOverlay();
        private StageController _postFilmBlinkStage;
        private StageBlinkLightDriver _postFilmBlinkDriver;
        private LivePostFilmTimeline.BlinkColorResolver _postFilmBlinkResolver;
        private int _postFilmBlinkAttempts, _postFilmBlinkSuccesses;
        // Per-layer source availability, sampled by the existing opt-in Live diagnostics.
        public string PostFilmBlinkLookupStatus { get; private set; } = "none|none|none";

        private bool ResolvePostFilmBlinkColor(string name, int hash, int containerIndex, out Color raw)
        {
            raw = Color.black;
            ++_postFilmBlinkAttempts;
            if (_postFilmBlinkDriver == null || containerIndex < 0)
                return false;
            bool found = _postFilmBlinkDriver.TryGetCurrentBlinkColor(name, hash, containerIndex, out raw, out _);
            if (found)
                ++_postFilmBlinkSuccesses;
            return found;
        }

        private void BindPostFilmBlinkDriver()
        {
            var director = Director.instance;
            var stage = director != null ? director._stageController : null;
            if (stage == _postFilmBlinkStage && _postFilmBlinkDriver != null)
                return;
            _postFilmBlinkStage = stage;
            _postFilmBlinkDriver = stage != null ? stage.GetComponent<StageBlinkLightDriver>() : null;
            if (_postFilmBlinkResolver == null)
                _postFilmBlinkResolver = ResolvePostFilmBlinkColor;
        }

        public Action<Gallop.RenderPipeline.RadialBlurPass.Parameter> OnUpdateRadialBlur;
        public Action<Gallop.RenderPipeline.SimpleColorCorrectionPass.Parameter> OnUpdateColorCorrection;
        public event Action<Gallop.Live.Cutt.PreColorCorrectionUpdateInfo> OnUpdatePreColorCorrection;
        public event Action<int, float> OnVoicePlay;
        public event Action<Gallop.Live.Cutt.TextureAnimationUpdateInfo> OnUpdateTextureAnimation;
        public event Action OnVoiceStopAll;
        private readonly Gallop.Live.Cutt.LiveVoiceScheduler _voiceScheduler = new Gallop.Live.Cutt.LiveVoiceScheduler();
        public event Action<Gallop.RenderPipeline.SimpleColorCorrectionPass.Parameter, int> OnUpdateColorCorrectionMultiCamera;
        // Native 0x4eda740 dispatches each group's own keys and MultiCameraNo.
        private void AlterUpdate_MultiCameraColorCorrection(LiveTimelineWorkSheet sheet)
        {
            if (OnUpdateColorCorrectionMultiCamera == null || sheet.multiCameraColorCorrectionDataLists == null)
                return;
            foreach (var group in sheet.multiCameraColorCorrectionDataLists)
            {
                var keys = group?.keys;
                if (keys == null || keys.Count == 0 || keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) || !keys.EnablePlayModeTimeline(_playMode))
                    continue;
                OnUpdateColorCorrectionMultiCamera(LiveColorCorrectionTimeline.EvaluateKeys(keys, _currentFrame, _playMode), group.MultiCameraNo);
            }
        }

        public event Action<LiveGlobalFogTimeline.UpdateInfo> OnUpdateGlobalFog;
        public event Action<LiveTransmittedLightTimeline.UpdateInfo> OnUpdateTransmittedLight;
        public event Action<LiveTransmittedLightTimeline.UpdateInfo, int> OnUpdateTransmittedLightMultiCamera;
        private LiveGlobalFogTimeline.BlinkColorResolver _fogBlinkResolver;
        // 子系统 A7b：原生 LiveTimelineControl 持有 OnUpdateExposure/OnUpdateToneCurve
        // 事件（Director.InitializeTimeline 于 0x181a5f21d/0x181a5f247 订阅：
        // add_OnUpdateExposure RVA=0x1ae96c0、add_OnUpdateToneCurve RVA=0x1aea4b0，
        // 委托类型 ExposureDelegate/ToneCurveDelegate，签名 in 结构体）。
        // 这里以同语义的泛型 Action<in T> 近似（C# 泛型委托无 in 变体声明，
        // 传入后不修改即可对齐原生按引用传递的只读语义）。
        public event Action<LiveExposureToneCurveTimeline.ExposureUpdateInfo> OnUpdateExposure;
        public event Action<LiveExposureToneCurveTimeline.ToneCurveUpdateInfo> OnUpdateToneCurve;
        public event Spotlight3dUpdateInfoDelegate OnUpdateSpotlight3d;
        public event BillboardUpdateInfoDelegate OnUpdateBillboard;
        public event Action<LiveVolumeLightTimeline.VolumeUpdateInfo> OnUpdateVolumeLight;
        public event Action<LiveVolumeLightTimeline.ShaftsUpdateInfo> OnUpdateLightShafts;
        public event Action<LiveTimelineParticleData, float> OnUpdateParticle;
        public event Action<LiveTimelineParticleGroupData, Vector2> OnUpdateParticleGroup;
        public event Action<LiveEffectTimeline.EffectUpdateInfo> OnUpdateEffect;
        public event Action<LiveEffectTimeline.EffectScaleUpdateInfo> OnUpdateEffectScale;
        private Func<int, bool> _effectOwnerEnabled;
        // Native worksheet lookup indexes SheetType; the last duplicate wins (0x1ae25b0).
        public LiveTimelineWorkSheet GetWorkSheetBySheetIndex(LiveTimelineDefine.SheetIndex index)
        {
            if (data?.worksheetList == null)
                return null;
            if (_sheetIndices != null)
            {
                int mapped = GetSheetIndex(index);
                return mapped >= 0 ? data.GetWorkSheet(mapped) : null;
            }

            for (int i = data.worksheetList.Count - 1; i >= 0; --i)
            {
                var sheet = data.worksheetList[i];
                if (sheet != null && sheet.SheetType == index)
                    return sheet;
            }

            return null;
        }

        private bool IsEffectOwnerEnabled(int owner)
        {
            if (owner == 18 || owner == 19 || owner == 22)
                return true;
            int index = LiveEffectController.CharacterIndex(owner);
            return index >= 0 && index < _liveCharactorLocators.Length && _liveCharactorLocators[index] != null;
        }

        public void ResetEffectTimelineForSeek()
        {
            var effects = GetWorkSheetBySheetIndex(LiveTimelineDefine.SheetIndex.MainLive)?.effectList;
            if (effects == null)
                return;
            foreach (var group in effects)
                if (group != null)
                    group.updatedKeyFrame = -1;
        }

        private void AlterUpdate_Effects(bool scale)
        {
            if (scale ? OnUpdateEffectScale == null : OnUpdateEffect == null)
                return;
            var effects = GetWorkSheetBySheetIndex(LiveTimelineDefine.SheetIndex.MainLive)?.effectList;
            if (effects == null)
                return;
            if (_effectOwnerEnabled == null)
                _effectOwnerEnabled = IsEffectOwnerEnabled;
            for (int i = 0; i < effects.Count; ++i)
            {
                if (scale)
                {
                    if (LiveEffectTimeline.EvaluateScale(effects[i], i, _currentFrame, _playMode, _effectOwnerEnabled, out var info))
                        OnUpdateEffectScale(info);
                }
                else
                {
                    if (LiveEffectTimeline.Evaluate(effects[i], i, _currentFrame, currentLiveTime, Timescale, _playMode, _effectOwnerEnabled, out var info))
                    {
                        if (info.isTransparentFX && !info.isClear)
                            TransparentCameraEnabled = true;
                        OnUpdateEffect(info);
                    }
                }
            }
        }

        private void AlterUpdate_Particles(LiveTimelineWorkSheet sheet, float frame)
        {
            if (OnUpdateParticle != null && sheet.particleList != null)
                foreach (var group in sheet.particleList)
                    if (LiveEffectTimeline.EvaluateParticle(group, frame, _playMode, out var rate))
                        OnUpdateParticle(group, rate);
            if (OnUpdateParticleGroup != null && sheet.particleGroupList != null)
                foreach (var group in sheet.particleGroupList)
                    if (LiveEffectTimeline.EvaluateParticleGroup(group, frame, _playMode, out var range))
                        OnUpdateParticleGroup(group, range);
        }

        private Func<int, Transform> _spotlightMultiCameraResolver;
        private LiveVolumeLightTimeline.BlinkColorResolver _volumeBlinkResolver;
        private Transform ResolveSpotlightMultiCameraTransform(int index)
        {
            var camera = GetMultiCamera(index);
            return camera != null ? camera.transform : null;
        }

        private void AlterUpdate_Spotlight3d(float frame)
        {
            if (OnUpdateSpotlight3d == null)
                return;
            if (_spotlightMultiCameraResolver == null)
                _spotlightMultiCameraResolver = ResolveSpotlightMultiCameraTransform;
            for (int w = 0; w < data.worksheetList.Count; ++w)
            {
                var groups = data.worksheetList[w] != null ? data.worksheetList[w].spotlight3dList : null;
                if (groups == null || groups.Count == 0)
                    continue;
                for (int i = 0; i < groups.Count; ++i)
                    if (LiveSpotlight3dTimeline.TryEvaluate(groups[i], i, frame, _playMode, Director.instance.MainCameraTransform, _spotlightMultiCameraResolver, out var info))
                        OnUpdateSpotlight3d(ref info);
                break;
            }
        }

        private bool ResolveVolumeBlinkColor(LiveTimelineKeyVolumeLightData key, ref Color color)
        {
            BindPostFilmBlinkDriver();
            if (!ResolvePostFilmBlinkColor(key.BlinkLightName, key.BlinkLightNameHash, key.BlinkLightContainerIndex, out var raw))
                return false;
            raw = LivePostFilmTimeline.AdjustBlinkRgb(raw, key.BlinkLightBrightnessPower, key.IsAdjustedBlinkLightColor);
            color.r = raw.r;
            color.g = raw.g;
            color.b = raw.b;
            return true;
        }

        private void AlterUpdate_VolumeLight(LiveTimelineWorkSheet sheet, float frame)
        {
            if (OnUpdateVolumeLight == null || sheet.volumeLightKeys == null)
                return;
            if (_volumeBlinkResolver == null)
                _volumeBlinkResolver = ResolveVolumeBlinkColor;
            foreach (var group in sheet.volumeLightKeys)
                if (LiveVolumeLightTimeline.TryEvaluateVolume(group != null ? group.keys : null, frame, _playMode, _volumeBlinkResolver, out var info))
                    OnUpdateVolumeLight(info);
        }

        private void AlterLateUpdate_LightProjection(LiveTimelineWorkSheet sheet, float frame)
        {
            var handler = OnUpdateLightProjection;
            if (handler == null || sheet == null || sheet.lightProjectionList == null)
                return;
            BindPostFilmBlinkDriver();
            foreach (var group in sheet.lightProjectionList)
            {
                if (group == null)
                    continue;
                LiveLightProjectionTimeline.UpdateInfo info;
                if (sheet.enableAtRuntime)
                    LiveLightProjectionTimeline.TryEvaluate(group, frame, _playMode, out info, _postFilmBlinkResolver);
                else
                {
                    info = default;
                    info.group = group;
                }

                handler(ref info);
            }
        }

        private void AlterUpdate_LightShafts(LiveTimelineWorkSheet sheet, float frame)
        {
            if (OnUpdateLightShafts == null || sheet.lightShaftsKeysLine == null)
                return;
            foreach (var group in sheet.lightShaftsKeysLine)
                if (LiveVolumeLightTimeline.TryEvaluateShafts(group != null ? group.keys : null, frame, _playMode, _deltaTimeRatio, out var info))
                    OnUpdateLightShafts(info);
        }

        private void AlterUpdate_PostFilm(LiveTimelineWorkSheet sheet, float frame)
        {
            BindPostFilmBlinkDriver();
            _postFilmBlinkAttempts = _postFilmBlinkSuccesses = 0;
            LivePostFilmTimeline.Evaluate(sheet != null ? sheet.postFilmKeys : null, frame, _playMode, _postFilmSamples.Overlay1, _postFilmBlinkResolver);
            string layer1 = PostFilmBlinkStatus();
            _postFilmBlinkAttempts = _postFilmBlinkSuccesses = 0;
            LivePostFilmTimeline.Evaluate(sheet != null ? sheet.postFilm2Keys : null, frame, _playMode, _postFilmSamples.Overlay2, _postFilmBlinkResolver);
            string layer2 = PostFilmBlinkStatus();
            _postFilmBlinkAttempts = _postFilmBlinkSuccesses = 0;
            LivePostFilmTimeline.Evaluate(sheet != null ? sheet.postFilm3Keys : null, frame, _playMode, _postFilmSamples.Overlay3, _postFilmBlinkResolver);
            PostFilmBlinkLookupStatus = layer1 + "|" + layer2 + "|" + PostFilmBlinkStatus();
            OnUpdatePostFilm?.Invoke(0, _postFilmSamples.Overlay1);
            OnUpdatePostFilm?.Invoke(1, _postFilmSamples.Overlay2);
            OnUpdatePostFilm?.Invoke(2, _postFilmSamples.Overlay3);
        }

        private string PostFilmBlinkStatus() => _postFilmBlinkAttempts == 0 ? "none" : _postFilmBlinkSuccesses > 0 ? "resolved" : "missing";
        // 子系统 A7b：Exposure 轨道求值。原生 AlterUpdate_Exposure RVA=0x1acfde0，
        // AlterLateUpdate 调用点 0x1ac9e0f（AlterUpdate_PostFilm×3 +
        // MultiCameraPostFilm×3 + MultiCameraPostEffect_BloomDiffusion 之后、
        // AlterUpdate_BgColor1(0x1aca720) 之前，REPORT §2.5）。
        private void AlterUpdate_Exposure(LiveTimelineWorkSheet sheet, float currentFrame)
        {
            if (OnUpdateExposure == null)
                return;
            var info = LiveExposureToneCurveTimeline.EvaluateExposure(sheet != null ? sheet.ExposureKeys : null, currentFrame, _playMode);
            OnUpdateExposure(info);
        }

        // 子系统 A7b：ToneCurve 轨道求值。原生 AlterUpdate_ToneCurve RVA=0x1adace0，
        // 调用点 0x1ac9e24（紧跟 AlterUpdate_Exposure 之后、BgColor1 之前）。
        private void AlterUpdate_ToneCurve(LiveTimelineWorkSheet sheet, float currentFrame)
        {
            if (OnUpdateToneCurve == null)
                return;
            var info = LiveExposureToneCurveTimeline.EvaluateToneCurve(sheet != null ? sheet.ToneCurveKeys : null, currentFrame, _playMode);
            OnUpdateToneCurve(info);
        }

        public const int kTargetFps = 60;
        public const float kTargetFpsF = 60;
        public const float kFrameToSec = 0.016666668f;
        public Transform cameraPositionLocatorsRoot;
        public Transform cameraLookAtLocatorsRoot;
        [SerializeField]
        private Transform[] characterStandPosLocators = new Transform[20];
        public void SetCharacterStandPosLocators(Transform[] locators)
        {
            if (locators == null)
                return;
            for (int i = 0; i < characterStandPosLocators.Length && i < locators.Length; i++)
                characterStandPosLocators[i] = locators[i];
        }

        public void InitializeCharacterLocatorStandPosition(int slot, ILiveTimelineCharactorLocator locator)
        {
            // Original SetCharactorLocator0x4ef34e7..0x4ef3644. The engine leaf
            // 0x5e68c30 is get_position (world), not get_localPosition.
            if (slot >= characterStandPosLocators.Length || characterStandPosLocators[slot] == null)
                return;
            var root = locator.liveRootTransform;
            if (root == null)
                return;
            root.position = characterStandPosLocators[slot].position;
            locator.liveCharaInitialPosition = characterStandPosLocators[slot].position;
        }

        public const float BaseCharaHeight = 158;
        public const float BaseCharaHeightMin = 130;
        public const float BaseCharaHeightMax = 190;
        public const float BaseCharaHeightDiff = 60;
        public struct FindTimelineConfig
        {
            public enum KeyType
            {
                KeyDirect,
                CurrentFrame
            }

            public KeyType keyType;
            public ILiveTimelineKeyDataList posKeys;
            public ILiveTimelineKeyDataList lookAtKeys;
            public LiveTimelineKey curKey;
            public LiveTimelineKey nextKey;
            public int extraCameraIndex;
        }

        public struct MirrorReflectionUpdateInfo
        {
            public int TimelineNameHash;
            public LiveTimelineDefine.MirrorReflectionBaseCameraType BaseCameraType;
            public int BaseCameraIndex;
            public bool EnableMirror;
            public bool EnableBgLayer;
            public bool Enable3dLayer;
            public bool IsToonMirror;
            public float MirrorReflectionRate;
            public LiveCharaPositionFlag TargetChara;
            public bool EnableCharaHead;
            public LiveCharaPositionFlag TargetCharaHead;
        }

        public delegate void MirrorReflectionUpdateInfoDelegate(in MirrorReflectionUpdateInfo updateInfo);
        public event MirrorReflectionUpdateInfoDelegate OnUpdateMirrorReflection;
        private LiveTimelineMotionSequence[] _motionSequenceArray;
        public LiveTimelineKeyCharaMotionSeqDataList[] _keyArray;
        public event Action<LiveTimelineKeyIndex, float> OnUpdateLipSync;
        public event Action<FacialDataUpdateInfo, float, int> OnUpdateFacial;
        public event Action<int> OnUpdateCameraSwitcher;
        public event GlobalLightUpdateInfoDelegate OnUpdateGlobalLight;
        public event EnvironmentMirrorDelegate OnEnvironmentMirror;
        public event BgColor1UpdateInfoDelegate OnUpdateBgColor1;
        public event BgColor2UpdateInfoDelegate OnUpdateBgColor2;
        public event LensFlareUpdateInfoDelegate OnUpdateLensFlare;
        public event LightProjectionUpdateInfoDelegate OnUpdateLightProjection;
        public event TransformUpdateInfoDelegate OnUpdateTransform;
        public event ObjectUpdateInfoDelegate OnUpdateObject;
        public event MobCyalumeUpdateInfoDelegate OnUpdateMobControl;
        public event MobCyalumeUpdateInfoDelegate OnUpdateCyalumeControl;
        public event BlinkLightUpdateInfoDelegate OnUpdateBlinkLight;
        public event WashLightUpdateInfoDelegate OnUpdateWashLight;
        public event AnimationUpdateInfoDelegate OnUpdateAnimation;
        public event LaserUpdateInfoDelegate OnUpdateLaser;
        private LaserUpdateInfo _laserUpdateInfo;
        private int _laserRuntimeIndexOffset;
        public event HdrBloomUpdateInfoDelegate OnUpdateHdrBloom;
        public event Action<PostEffectUpdateInfo_BloomDiffusion> OnUpdatePostEffect_BloomDiffusion;
        public event Action<LiveTimelineKeyPostEffectDOFData, Vector3?> OnUpdatePostEffect_Dof;
        public bool HasDofCameraLookAt { get; private set; }
        public Vector3 DofCameraLookAt { get; private set; }
        public Camera DofTrackCamera { get; private set; }
        public Camera DofLookAtCamera { get; private set; }

        private readonly LiveTimelineKeyPostEffectDOFData _dofSample = new LiveTimelineKeyPostEffectDOFData();
        public event UVScrollLightUpdateInfoDelegate OnUpdateUVScrollLight;
        private static Func<LiveTimelineKeyCameraPositionData, LiveTimelineControl, FindTimelineConfig, Vector3> fnGetCameraPosValue = GetCameraPosValue;
        private static Func<LiveTimelineKeyCameraLookAtData, LiveTimelineControl, Vector3, FindTimelineConfig, Vector3> fnGetCameraLookAtValue = GetCameraLookAtValue;
        private Vector3 _cameraLayerOffset = Vector3.zero;
        private Vector3 _cameraLayerOffsetMin, _cameraLayerOffsetDiff;
        private Vector3 _cameraPosLayerOffset, _cameraLookAtLayerOffset;
        private static Vector3 InterpolateCameraLayer(Vector3 start, Vector3 end, float t)
        {
            // Match the original subss/mulss/addss boundaries. Unity's generic
            // Vector3.LerpUnclamped can retain a wider Mono intermediate.
            return new Vector3(MonitorFloat32(MonitorFloat32(MonitorFloat32(end.x - start.x) * t) + start.x), MonitorFloat32(MonitorFloat32(MonitorFloat32(end.y - start.y) * t) + start.y), MonitorFloat32(MonitorFloat32(MonitorFloat32(end.z - start.z) * t) + start.z));
        }

        // Native AlterUpdate_CameraLayer 0x4ec8320. Disabled/no-current tracks
        // preserve the previous range; missing host fields start at zero.
        private void AlterUpdate_CameraLayer(LiveTimelineWorkSheet sheet, int frame)
        {
            var keys = sheet.cameraLayerKeys;
            if (keys == null || keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) || !keys.EnablePlayModeTimeline(_playMode))
                return;
            FindTimelineKey(out var a, out var b, keys, frame);
            if (!(a is LiveTimelineKeyCameraLayerData current))
                return;
            Vector3 maximum = current.offsetMaxPosition;
            Vector3 minimum = current.offsetMinPosition;
            if (b is LiveTimelineKeyCameraLayerData next && next.IsInterpolateKey())
            {
                float t = CalculateInterpolationValue(current, next, frame);
                maximum = InterpolateCameraLayer(maximum, next.offsetMaxPosition, t);
                minimum = InterpolateCameraLayer(minimum, next.offsetMinPosition, t);
            }

            _cameraLayerOffsetMin = minimum;
            _cameraLayerOffsetDiff = maximum - minimum;
        }

        // Native AlterLateUpdate 0x4ec3a20 applies both offsets in the already
        // oriented camera's axes, before roll. DOF receives the unshifted target.
        private void ApplyMainCameraLayerOffsets(LiveTimelineWorkSheet sheet, Vector3 lookAt)
        {
            var transform = GetCamera(sheet.targetCameraIndex)?.cacheTransform;
            if (transform == null)
                return;
            transform.position += transform.rotation * _cameraPosLayerOffset;
            transform.LookAt(lookAt + transform.rotation * _cameraLookAtLayerOffset, Vector3.up);
        }

        private CacheCamera[] _cameraArray = new CacheCamera[3];
        private LiveTimelineCamera[] _cameraScriptArray = new LiveTimelineCamera[3];
        private Dictionary<string, Transform> _cameraPositionLocatorDict;
        private Dictionary<string, Transform> _cameraLookAtLocatorDict;
        private CacheCamera[] _multiCameraCache;
        private bool[] _multiCameraPositionValid;
        // Fixed original SetMonitorCamera0x4ef3790 layout/initialization.
        private CacheCamera[] _monitorCameraCacheArray;
        private MonitorCamera[] _monitorCameraArray;
        private float[] _monitorRollArray;
        private bool[] _isUsedMonitorCameraArray;
        private bool[] _isUseRenderTextureForMonitorArray;
        private Vector3[] _monitorCameraLayerOffsetMinArray;
        private Vector3[] _monitorCameraLayerOffsetDiffArray;
        private Vector3[] _monitorCameraPosLayerOffsetArray;
        private bool _isInitializedMonitorCamera;
        public void SetMonitorCamera(MonitorCamera[] monitorCameraArray)
        {
            // Original null/empty is a no-op, not an unbind/reset operation.
            if (monitorCameraArray == null || monitorCameraArray.Length == 0)
                return;
            _monitorCameraCacheArray = new CacheCamera[monitorCameraArray.Length];
            for (int i = 0; i < _monitorCameraCacheArray.Length; i++)
                _monitorCameraCacheArray[i] = new CacheCamera(monitorCameraArray[i].targetCamera);
            _monitorCameraArray = monitorCameraArray;
            _monitorRollArray = new float[monitorCameraArray.Length];
            _isUseRenderTextureForMonitorArray = new bool[monitorCameraArray.Length];
            _isUsedMonitorCameraArray = new bool[monitorCameraArray.Length];
            _monitorCameraLayerOffsetMinArray = new Vector3[monitorCameraArray.Length];
            _monitorCameraLayerOffsetDiffArray = new Vector3[monitorCameraArray.Length];
            _monitorCameraPosLayerOffsetArray = new Vector3[monitorCameraArray.Length];
            _isInitializedMonitorCamera = true;
        }

        public bool IsActiveMonitorCamera(int index = -1)
        {
            if (!_isInitializedMonitorCamera)
                return false;
            if (index < 0)
            {
                for (int i = 0; i < _monitorCameraArray.Length; i++)
                    if (_monitorCameraArray[i].gameObject.activeSelf)
                        return true;
                return false;
            }

            return index < _monitorCameraArray.Length && _monitorCameraArray[index].gameObject.activeSelf;
        }

        public int GetActiveMonitorCameraCount()
        {
            if (!_isInitializedMonitorCamera)
                return 0;
            int count = 0;
            foreach (var monitor in _monitorCameraArray)
                if (monitor.gameObject.activeSelf)
                    count++;
            return count;
        }

        private void ResetUsedMonitorCameraArray()
        {
            if (!_isInitializedMonitorCamera)
                return;
            for (int i = 0; i < _isUsedMonitorCameraArray.Length; i++)
                _isUsedMonitorCameraArray[i] = false;
        }

        public bool IsUseRenderTextureForMonitor(int index) => _isUseRenderTextureForMonitorArray != null && index >= 0 && index < _isUseRenderTextureForMonitorArray.Length && _isUseRenderTextureForMonitorArray[index];
        public Vector3 MonitorCameraLayerOffsetMin(int index) => index < _monitorCameraLayerOffsetMinArray.Length ? _monitorCameraLayerOffsetMinArray[index] : Vector3.zero;
        public Vector3 MonitorCameraLayerOffsetDiff(int index) => index < _monitorCameraLayerOffsetDiffArray.Length ? _monitorCameraLayerOffsetDiffArray[index] : Vector3.zero;
        private void ResetMonitorLayerOffsets()
        {
            for (int i = 0; i < _monitorCameraLayerOffsetMinArray.Length; i++)
            {
                _monitorCameraLayerOffsetMinArray[i] = Vector3.zero;
                _monitorCameraLayerOffsetDiffArray[i] = Vector3.zero;
                _monitorCameraPosLayerOffsetArray[i] = Vector3.zero;
            }
        }

        private void AlterUpdate_MonitorCameraLayer(LiveTimelineWorkSheet sheet, int currentFrame)
        {
            ResetMonitorLayerOffsets();
            for (int i = 0; i < sheet.monitorCameraLayerKeys.Count; i++)
            {
                var keys = sheet.monitorCameraLayerKeys[i].keys;
                if (keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) || !keys.EnablePlayModeTimeline(_playMode))
                    continue;
                FindTimelineKey(out var current, out var next, keys, currentFrame);
                if (current == null)
                    continue;
                var a = current as LiveTimelineKeyMonitorCameraLayerData;
                var b = next as LiveTimelineKeyMonitorCameraLayerData;
                Vector3 min = a.offsetMinPosition, max = a.offsetMaxPosition;
                if (b != null && b.IsInterpolateKey())
                {
                    float t = CalculateInterpolationValue(a, b, currentFrame);
                    min = Vector3.LerpUnclamped(min, b.offsetMinPosition, t);
                    max = Vector3.LerpUnclamped(max, b.offsetMaxPosition, t);
                }

                _monitorCameraLayerOffsetMinArray[i] = min;
                _monitorCameraLayerOffsetDiffArray[i] = max - min;
                _monitorCameraPosLayerOffsetArray[i] = Vector3.zero;
            }
        }

        // Host worksheet adapter. Missing lists in partial Viewer data only run
        // the original zero/reset prefix; full native dispatch remains separate.
        internal void PrepareMonitorCameraLayerState(LiveTimelineWorkSheet sheet, int currentFrame)
        {
            if (!_isInitializedMonitorCamera)
                return;
            if (sheet == null || sheet.monitorCameraLayerKeys == null)
                ResetMonitorLayerOffsets();
            else
                AlterUpdate_MonitorCameraLayer(sheet, currentFrame);
        }

        internal void BeginMonitorControlFrame()
        {
            if (!_isInitializedMonitorCamera)
                return;
            ResetUsedMonitorCameraArray();
            if (_isUseRenderTextureForMonitorArray != null)
                for (int i = 0; i < _isUseRenderTextureForMonitorArray.Length; i++)
                    _isUseRenderTextureForMonitorArray[i] = false;
        }

        internal void MarkMonitorCameraUsed(int cameraIndex, int groupIndex)
        {
            if (cameraIndex < 0)
                return;
            _isUsedMonitorCameraArray[cameraIndex] = true;
            if (_isUseRenderTextureForMonitorArray != null && groupIndex < _isUseRenderTextureForMonitorArray.Length)
                _isUseRenderTextureForMonitorArray[groupIndex] = true;
        }

        public float GetHeightValueWithCharacters(LiveCharaPositionFlag flags)
        {
            if ((int)flags <= 0)
                return 1f;
            float height = 0f;
            int count = 0;
            for (int i = 0; i < 20; i++)
                if (flags.hasFlag((LiveCharaPosition)i) && _liveCharactorLocators[i] != null)
                {
                    height += _liveCharactorLocators[i].liveCharaHeightValue;
                    count++;
                }

            return count > 1 ? height / count : height;
        }

        // Original GetHeightRateWithCharacters (0x4eef670): division precedes
        // summation. Dividing GetHeightValueWithCharacters by 158 changes rounding.
        public float GetHeightRateWithCharacters(LiveCharaPositionFlag flags)
        {
            if ((int)flags <= 0)
                return 1f;
            float rate = 0f;
            int count = 0;
            for (int i = 0; i < 20; i++)
                if (flags.hasFlag((LiveCharaPosition)i) && _liveCharactorLocators[i] != null)
                {
                    rate = MonitorFloat32(rate + MonitorFloat32(_liveCharactorLocators[i].liveCharaHeightValue / 158f));
                    count++;
                }

            return count > 1 ? MonitorFloat32(rate / count) : rate;
        }

        // Mono may retain wider floating intermediates; original SSE divss/addss
        // rounds after each operation. This boundary preserves those float bits.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        internal static float MonitorFloat32(float value)
        {
            return BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(value));
        }

        // Original 0x4ef9960: two height reads and setter readbacks are deliberate.
        // HeightRatio is an unclamped range coordinate, distinct from RatioBase.
        public static void UpdateCharactorLocator(ILiveTimelineCharactorLocator locator)
        {
            locator.liveCharaHeightRatioBase = MonitorFloat32(locator.liveCharaHeightValue / 158f);
            if (Mathf.Abs(locator.liveCharaHeightRatioBase) < 0.001f)
                locator.liveCharaHeightRatioBase = 0f;
            float difference = MonitorFloat32(locator.liveCharaHeightValue - 127f);
            locator.liveCharaHeightRatio = MonitorFloat32(difference / 55f);
            if (Mathf.Abs(locator.liveCharaHeightRatio) < 0.001f)
                locator.liveCharaHeightRatio = 0f;
        }

        private bool GetCameraLayerOffset(LiveCharaPositionFlag flags, Vector3 minimum, Vector3 difference, out Vector3 offset)
        {
            offset = Vector3.zero;
            float height = GetHeightValueWithCharacters(flags);
            if (!(height > 0f))
                return false;
            offset = minimum + difference * ((height - 130f) / 60f);
            return true;
        }

        // Original controller operations after key/value sampling. Sampling is
        // still the existing Viewer adapter, not the complete native core/props path.
        internal void ApplyMonitorCameraPositionSample(int index, LiveTimelineKeyMonitorCameraPositionData key, LiveTimelineKeyMonitorCameraPositionData next, float ratio, bool positionValid, Vector3 position)
        {
            var cached = _monitorCameraCacheArray[index];
            if (positionValid)
            {
                if (GetCameraLayerOffset(key.charaRelativeBase, MonitorCameraLayerOffsetMin(index), MonitorCameraLayerOffsetDiff(index), out var offset))
                    _monitorCameraPosLayerOffsetArray[index] = offset;
                cached.cacheTransform.position = position;
            }

            bool interpolate = next != null && next.IsInterpolateKey();
            float fov = interpolate ? key.fov + (next.fov - key.fov) * ratio : key.fov;
            _monitorRollArray[index] = interpolate ? key.roll + (next.roll - key.roll) * ratio : key.roll;
            cached.camera.fieldOfView = fov;
            cached.camera.nearClipPlane = key.nearClip;
            cached.camera.farClipPlane = key.farClip;
            bool enable = Gallop.Live.LiveQualitySettings.IsMonitor ? key.enable : _isUsedMonitorCameraArray[index];
            _monitorCameraArray[index].gameObject.SetActive(enable);
            if (!enable)
                return;
            if (((int)key.attribute & 0x20000) != 0)
                cached.camera.cullingMask = key.GetCullingMask();
            if (((int)key.attribute & 0x40000) != 0)
                cached.camera.backgroundColor = key.GetBgColor();
        }

        internal void ApplyMonitorCameraLookAtSample(int index, LiveTimelineKeyMonitorCameraLookAtData key, Vector3 lookAt, float scale)
        {
            var transform = _monitorCameraCacheArray[index].cacheTransform;
            transform.LookAt(lookAt);
            transform.localScale = Vector3.one * scale;
            if (!GetCameraLayerOffset(key.lookAtCharaPos, MonitorCameraLayerOffsetMin(index), MonitorCameraLayerOffsetDiff(index), out var offset))
                return;
            Vector3 position = transform.position;
            Quaternion rotation = transform.rotation;
            lookAt += rotation * offset;
            position += rotation * _monitorCameraPosLayerOffsetArray[index];
            transform.position = position;
            transform.LookAt(lookAt);
        }

        internal void FinishMonitorCameraRoll()
        {
            if (!_isInitializedMonitorCamera)
                return;
            for (int i = 0; i < _monitorRollArray.Length; i++)
                _monitorCameraCacheArray[i].cacheTransform.Rotate(0f, 0f, _monitorRollArray[i]);
        }

        private MultiCamera[] _multiCamera;
        private bool _isMultiCameraEnable;
        public bool IsMultiCameraEnabled => _isMultiCameraEnable;
        public int MultiCameraCount => _multiCamera != null ? _multiCamera.Length : 0;

        public Camera GetMultiCamera(int index)
        {
            if (_multiCamera == null || index < 0 || index >= _multiCamera.Length || _multiCamera[index] == null)
                return null;
            return _multiCamera[index].GetCamera();
        }

        private bool _isNowAlterUpdate;
        private int _oldFrame;
        private float _oldLiveTime;
        private float _currentLiveTime;
        private float _deltaTimeRatio;
        private float _deltaTime;
        private float _baseCameraAspectRatio = 1.77777779f;
        public bool _limitFovForWidth = false;
        private int _currentFrame;
        public int CurrentFrame => _currentFrame;

        private bool _isExtraCameraLayer;
        public bool IsRecordVMD;
        public List<LiveCameraFrame> RecordFrames = new List<LiveCameraFrame>();
        public List<List<LiveCameraFrame>> MultiRecordFrames = new List<List<LiveCameraFrame>>();
        public event Action RecordUma;
        public Dictionary<string, GameObject> StageObjectMap = new Dictionary<string, GameObject>();
        public Dictionary<string, StageObjectUnit> StageObjectUnitMap = new Dictionary<string, StageObjectUnit>();
        public float currentLiveTime
        {
            get
            {
                return _currentLiveTime;
            }

            private set
            {
                _currentLiveTime = value;
            }
        }

        public event CameraPosUpdateInfoDelegate OnUpdateCameraPos;
        public CacheCamera[] cameraArray
        {
            get
            {
                return _cameraArray;
            }

            private set
            {
                _cameraArray = value;
            }
        }

        private LiveTimelineCamera[] cameraScriptArray => _cameraScriptArray;

        private Dictionary<string, Transform> cameraPositionLocatorDict
        {
            get
            {
                if (_cameraPositionLocatorDict == null)
                {
                    _cameraPositionLocatorDict = new Dictionary<string, Transform>();
                    if (cameraPositionLocatorsRoot != null)
                    {
                        Transform[] componentsInChildren = cameraPositionLocatorsRoot.GetComponentsInChildren<Transform>();
                        foreach (Transform transform in componentsInChildren)
                        {
                            _cameraPositionLocatorDict[transform.name] = transform;
                        }
                    }
                }

                return _cameraPositionLocatorDict;
            }
        }

        private Dictionary<string, Transform> cameraLookAtLocatorDict
        {
            get
            {
                if (_cameraLookAtLocatorDict == null)
                {
                    _cameraLookAtLocatorDict = new Dictionary<string, Transform>();
                    if (cameraLookAtLocatorsRoot != null)
                    {
                        Transform[] componentsInChildren = cameraLookAtLocatorsRoot.GetComponentsInChildren<Transform>();
                        foreach (Transform transform in componentsInChildren)
                        {
                            _cameraLookAtLocatorDict[transform.name] = transform;
                        }
                    }
                }

                return _cameraLookAtLocatorDict;
            }
        }

        public ILiveTimelineCharactorLocator[] liveCharactorLocators => _liveCharactorLocators;

        private ILiveTimelineCharactorLocator[] _liveCharactorLocators = new ILiveTimelineCharactorLocator[liveCharaPositionMax];
        public Vector3 liveStageCenterPos => _liveStageCenterPos;

        private Vector3 _liveStageCenterPos = Vector3.zero;
        private TimelinePlayerMode _playMode = TimelinePlayerMode.Default;
        public TimelinePlayerMode PlayMode => _playMode;

        public static int liveCharaPositionMax
        {
            get
            {
                if (_liveCharaPositionMax < 0)
                {
                    _liveCharaPositionMax = Enum.GetValues(typeof(LiveCharaPosition)).Length;
                }

                return _liveCharaPositionMax;
            }
        }

        public CacheCamera GetCamera(int index)
        {
            if (index < 0 || index >= _cameraArray.Length)
            {
                return null;
            }

            return _cameraArray[index];
        }

        private static int _liveCharaPositionMax = -1;
        private static bool availableFindKeyCache => true;

        private int _currentCameraPosKeyFrame = -1;
        private int _currentCameraLookAtKeyFrame = -1;
        internal int CurrentCameraPositionKeyFrame => _currentCameraPosKeyFrame;
        internal int CurrentCameraLookAtKeyFrame => _currentCameraLookAtKeyFrame;

        internal void CacheFutureCameraPosition(LiveTimelineKeyCameraPositionData current, LiveTimelineKeyCameraPositionData next, int frame, ILiveTimelineKeyDataList keys)
        {
            if (current.frame == _currentCameraPosKeyFrame || current.frame > _currentFrame)
                return;
            if (!next.IsUseCharaPositionAtPrevKeyStartFrame)
                return;
            int count = keys.Count;
            int index = keys.FindKeyCached(frame, availableFindKeyCache).index + 1;
            for (; index < count; index++)
            {
                var future = keys.At(index) as LiveTimelineKeyCameraPositionData;
                if (future == null || !future.IsUseCharaPositionAtPrevKeyStartFrame)
                    return;
                future.CacheStartFramePositionIfNeed(this, current.frame);
            }
        }

        internal void CacheFutureCameraLookAt(LiveTimelineKeyCameraLookAtData current, LiveTimelineKeyCameraLookAtData next, int frame, ILiveTimelineKeyDataList keys)
        {
            if (current.frame == _currentCameraLookAtKeyFrame || current.frame > _currentFrame)
                return;
            if (!next.IsUseCharaPositionAtPrevKeyStartFrame)
                return;
            int count = keys.Count;
            int index = keys.FindKeyCached(frame, availableFindKeyCache).index + 1;
            for (; index < count; index++)
            {
                var future = keys.At(index) as LiveTimelineKeyCameraLookAtData;
                if (future == null || !future.IsUseCharaPositionAtPrevKeyStartFrame)
                    return;
                future.CacheStartFramePositionIfNeed(this, current.frame);
            }
        }

        private static Func<LiveTimelineKeyCameraPositionData, LiveTimelineControl, FindTimelineConfig, Vector3> fnGetMultiCameraPositionValueFunc = GetMultiCameraPositionValue;
        private static Func<LiveTimelineKeyCameraLookAtData, LiveTimelineControl, Vector3, FindTimelineConfig, Vector3> fnGetMultiCameraLookAtValueFunc = GetMultiCameraLookAtValue;
        public void CopyValues<T>(T from, T to)
        {
            var json = JsonUtility.ToJson(from);
            JsonUtility.FromJsonOverwrite(json, to);
        }

        private void Awake()
        {
            InitializeTimeLineData();
            if (Director.instance)
            {
                Director.instance._liveTimelineControl = this;
                IsRecordVMD = Director.instance.IsRecordVMD;
            }
        }

        public bool IsEnablePostEffect { get; set; } = true;
        public float Timescale { get; private set; } = 1f;
        public int CurrentTimescaleFrame { get; private set; }

        internal void SampleTimelineTimescale(LiveTimelineWorkSheet sheet)
        {
            Timescale = 1f;
            CurrentTimescaleFrame = 0;
            var keys = sheet.timescaleKeys;
            if (keys == null)
                throw new NullReferenceException("Timescale key list");
            if (keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) || !keys.EnablePlayModeTimeline(_playMode) || keys.Count == 0)
                return;
            var current = keys.FindKeyCached(_currentFrame, true).key as LiveTimelineKeyTimescaleData;
            if (current != null)
            {
                Timescale = current.Timescale;
                CurrentTimescaleFrame = current.frame;
            }
        }

        public event Action OnBeforeTimelineUpdate;
        public event Action OnAfterTimelineUpdate;
        // Stage-owned audience instances consume this at the original 4ee8610
        // Audience phase, after ObjectControl and before FormationOffset.
        public event Action<LiveTimelineControl> OnUpdateAudience;
        // Opt-in observation of a real sampler invocation. The action always
        // executes, even if an audit listener is absent; this is not a dry-run.
        public event Action<string> OnTimelineSamplerDispatch;
#if UNITY_EDITOR
        public static Action<LiveTimelineControl, string, Action> EditorSamplerExecutor;
#endif
        private void DispatchSampler(string nativeName, Action sampler)
        {
            {
                OnTimelineSamplerDispatch?.Invoke(nativeName);
#if UNITY_EDITOR
                if (EditorSamplerExecutor != null)
                {
                    EditorSamplerExecutor(this, nativeName, sampler);
                    return;
                }

#endif
                sampler();
            }
        }

        private void AlterUpdate_Billboard(LiveTimelineWorkSheet sheet)
        {
            if (sheet.billboardList == null || OnUpdateBillboard == null)
                return;
            foreach (var group in sheet.billboardList)
                if (LiveBillboardTimeline.TryEvaluate(group, (int)_currentFrame, _playMode, out var info))
                    OnUpdateBillboard(ref info);
        }

        // Original AlterUpdate_TextureAnimationControl 0x4ee4500: per-group gates,
        // key fetch, scroll interpolation and publish to the texture-animation event.
        private void AlterUpdate_TextureAnimationControl(LiveTimelineWorkSheet sheet, float currentFrame)
        {
            if (OnUpdateTextureAnimation == null || sheet?.textureAnimationList == null)
                return;
            int frame = (int)currentFrame;
            var list = sheet.textureAnimationList;
            for (int i = 0; i < list.Count; i++)
            {
                if (LiveTextureAnimationProducer.Evaluate(list[i], frame, _playMode, out var info))
                    OnUpdateTextureAnimation(info);
            }
        }

        // Original AlterUpdate_Voice 0x4ee7050: gate list/play mode, fetch the key,
        // dedupe by key frame, resolve LiveVariationId cue overrides, then play the
        // cue with start offset = current time - key.frame/60. A null key stops all
        // voice and resets the last frame to -1.
        private void AlterUpdate_Voice(LiveTimelineWorkSheet sheet)
        {
            var keys = sheet.VoiceKeys;
            if (keys == null)
                return;
            if (!keys.EnablePlayModeTimeline(_playMode))
                return;
            var enabledVariations = Director.instance?.EnabledLiveVariationOrder;
            int voiceVariation = enabledVariations != null && enabledVariations.Count > 0 ? enabledVariations[0] : 0;
            var sample = LiveVoiceTimeline.Evaluate(keys, _currentFrame, voiceVariation, currentLiveTime);
            switch (_voiceScheduler.Update(sample, out int cueId, out float startTime))
            {
                case Gallop.Live.Cutt.LiveVoiceScheduler.VoiceAction.Play:
                    OnVoicePlay?.Invoke(cueId, startTime);
                    break;
                case Gallop.Live.Cutt.LiveVoiceScheduler.VoiceAction.Stop:
                    OnVoiceStopAll?.Invoke();
                    break;
            }
        }

        private void AlterUpdate_LensFlare(LiveTimelineWorkSheet sheet)
        {
            if (sheet.lensFlareList == null || OnUpdateLensFlare == null)
                return;
            foreach (var group in sheet.lensFlareList)
                if (LiveLensFlareTimeline.TryEvaluate(group, _currentFrame, _playMode, out var info))
                    OnUpdateLensFlare(ref info);
        }

        readonly Dictionary<LiveTimelineWorkSheet, LiveWorksheetRegistrationState> _worksheetRegistrations = new Dictionary<LiveTimelineWorkSheet, LiveWorksheetRegistrationState>();
        public bool HasInitializedWorksheet(LiveTimelineWorkSheet sheet) => _worksheetRegistrations.ContainsKey(sheet);
        public LiveWorksheetRegistrationState InitializeWorksheet(LiveTimelineWorkSheet sheet)
        {
            if (_worksheetRegistrations.TryGetValue(sheet, out var prior))
                return prior;
            var state = new LiveWorksheetRegistrationState();
            sheet.InitializeRegisteredKeys(this, state);
            _worksheetRegistrations.Add(sheet, state);
            return state;
        }

        public void InitializeTimeLineData()
        {
            if (data?.worksheetList == null)
                return;
            InitSheetTable();
            TrySetupSheetIndexTable(SheetVariationId);
            SetFirstSheetToCurrentSheetNo();
            foreach (var sheet in data.worksheetList)
                if (sheet != null && sheet.SheetVariationId == SheetVariationId)
                {
                    InitializeWorksheet(sheet);
                }
        }

        private static Transform EnsureMotionTransform(Transform parent, string name)
        {
            var node = parent.Find(name);
            if (node != null)
                return node;
            node = new GameObject(name).transform;
            node.SetParent(parent, false);
            return node;
        }

        private static void EnsureMicMotionTransforms(Transform animationRoot)
        {
            // LiveModelController caches these four named nodes (native RVA 0x19b0b50).
            // Their hierarchy is authored in the body motion curves, not in body prefabs.
            // Reconstruct missing nodes before AddClip/Sample and locator caching; never
            // replace the animated mic target with a hand or discard attachment keys.
            var position = animationRoot.Find("Position");
            if (position == null)
                return;
            var locator = EnsureMotionTransform(position, "Mic_Attach_00_loc");
            EnsureMotionTransform(locator, "Mic_Node_L");
            EnsureMotionTransform(locator, "Mic_Node_R");
            EnsureMotionTransform(animationRoot, "Mic_Attach_00");
        }

        public void InitCharaMotionSequence(int[] motionSequence)
        {
            var director = Director.instance;
            InitializeFacialNoiseNative();
            InitializeCharacterConsumers();
            ResetCharaNodeConsumers();
            InitializeCharaCollisionConsumers(CurrentWorkSheet);
            // Native InitCharaMotionSequence retains an entry for each physical slot.
            foreach (var obj in director.charaObjs)
            {
                var container = obj != null ? obj.GetComponentInChildren<UmaContainer>() : null;
                Animation animation = null;
                if (container != null)
                {
                    EnsureMicMotionTransforms(container.transform);
                    // The game's body clips have authored Position_light curves, but
                    // the assembled Viewer model omits this non-skinning motion target.
                    // Restore that target before Animation.AddClip binds its curves;
                    // FollowSpot must consume this track, not the actor's animated feet.
                    if (!string.IsNullOrEmpty(data.spotLightParentName))
                        EnsureMotionTransform(container.transform, data.spotLightParentName);
                    animation = container.gameObject.AddComponent<Animation>();
                }

                director.charaAnims.Add(animation);
            }

            //Get KeyArray
            var listCount = data.worksheetList[0].charaMotSeqList.Count;
            _keyArray = new LiveTimelineKeyCharaMotionSeqDataList[listCount];
            for (int i = 0; i < listCount; i++)
            {
                _keyArray[i] = data.worksheetList[0].charaMotSeqList[i].keys;
            }

            if (Director.instance.liveMode == 1)
            {
                director.SetupLiveSystemTextRandomMotions(data, motionSequence);
                //Set Motion
                int CharaPositionMax = Mathf.Max(0, Mathf.Min(director.allowCount, Mathf.Min(director.charaObjs.Count, motionSequence != null ? motionSequence.Length : 0)));
                _motionSequenceArray = new LiveTimelineMotionSequence[CharaPositionMax];
                for (int i = 0; i < CharaPositionMax; i++)
                {
                    _motionSequenceArray[i] = new LiveTimelineMotionSequence();
                    _motionSequenceArray[i].Initialize(Director.instance.charaObjs[i], i, motionSequence[i], this);
                }
            }
            else if (Director.instance.liveMode == 0)
            {
                List<AnimationClip> Anims = new List<AnimationClip>();
                //Get MotionList
                foreach (var motion in UmaViewerMain.Instance.AbMotions.Where(a => a.Name.StartsWith($"3d/motion/live/body/son{Director.instance.live.MusicId}") && Path.GetFileName(a.Name).Split('_').Length == 4))
                {
                    AssetBundle motionAB = UmaAssetManager.LoadAssetBundle(motion);
                    AnimationClip motionAnim = motionAB != null ? motionAB.LoadAsset<AnimationClip>(Path.GetFileName(motion.Name).Split('.')[0]) : null;
                    if (motionAnim == null)
                        Debug.LogError("[LiveTimelineControl] Basic motion clip load failed: " + motion.Name);
                    Anims.Add(motionAnim);
                }

                //Set Motion
                int CharaPositionMax = Mathf.Max(0, Mathf.Min(director.allowCount, Mathf.Min(director.charaObjs.Count, Mathf.Min(director.charaAnims.Count, Anims.Count))));
                _motionSequenceArray = new LiveTimelineMotionSequence[CharaPositionMax];
                for (int i = 0; i < CharaPositionMax; i++)
                {
                    _motionSequenceArray[i] = new LiveTimelineMotionSequence();
                    _motionSequenceArray[i].Initialize(director.charaObjs[i], i, i, this, Anims);
                }
            }
        }

        public void AlterUpdate(float liveTime)
        {
            if (data == null)
                return;
            _isNowAlterUpdate = true;
            OnBeforeTimelineUpdate?.Invoke();
            _isMultiCameraEnable = false;
            AdvanceLiveClock(liveTime);
            AdvanceFacialNoiseClockNative(currentLiveTime);
            TransparentCameraEnabled = false;
            TransparentCameraOrthographic = false;
            TransparentCameraDefaultTransform = false;
            TransparentCameraOrthographicSize = 5f;
            LiveTimelineWorkSheet sheet = CurrentWorkSheet;
            // Original 4ee8610 leaves the in-update flag set on this exit.
            if (!sheet.enableAtRuntime)
                return;
            SampleTimelineTimescale(sheet);
            // Sampler order is pinned to 4ee8610. These are the installed
            // consumers; unsupported native families remain in the audit ledger.
            DispatchSampler("AlterUpdate_BlinkLight", () => AlterUpdate_BlinkLight(sheet, _currentFrame));
            DispatchSampler("AlterUpdate_IKController", () => AlterUpdate_IKController(sheet, _currentFrame));
            if (_motionSequenceArray != null)
            {
                DispatchSampler("AlterUpdate_CharaMotionNoise", AlterUpdate_CharaMotionNoise);
                DispatchSampler("CharaMotionSequence.AlterUpdate", () => AlterUpdate_CharaMotionSequence(liveTime));
            }

            DispatchSampler("AlterUpdate_FacialData", () => AlterUpdate_FacialDataNative(sheet, _currentFrame));
            DispatchSampler("AlterUpdate_LipSyncPatternRange", () => AlterUpdate_LipSyncPatternRange(sheet, _currentFrame));
            DispatchSampler("AlterUpdate_LipSyncPattern", () => AlterUpdate_LipSyncPattern(sheet, _currentFrame));
            if (OnUpdateLipSync != null)
            {
                DispatchSampler("AlterUpdate_LipSync", () => AlterUpdate_OrdinaryLipSync(sheet, _currentFrame, liveTime));
            }

            DispatchSampler("AlterUpdate_MiniCharaCamera", () => AlterUpdate_MiniCharaCamera(sheet, (int)_currentFrame, currentLiveTime));
            DispatchSampler("AlterUpdate_MiniCharaColor", () => AlterUpdate_MiniCharaColor(sheet, (int)_currentFrame, currentLiveTime));
            DispatchSampler("AlterUpdate_MiniCharaMotion", () => AlterUpdate_MiniCharaMotion(sheet, (int)_currentFrame, currentLiveTime));
            DispatchSampler("AlterUpdate_MiniCharaTransform", () => AlterUpdate_MiniCharaTransform(sheet, (int)_currentFrame, currentLiveTime));
            DispatchSampler("AlterUpdate_BgColor1", () => AlterUpdate_BgColor1(sheet, _currentFrame));
            DispatchSampler("AlterUpdate_BgColor2", () => AlterUpdate_BgColor2(sheet, _currentFrame));
            DispatchSampler("AlterUpdate_VolumeLight", () => AlterUpdate_VolumeLight(sheet, _currentFrame));
            DispatchSampler("AlterUpdate_LightShafts", () => AlterUpdate_LightShafts(sheet, _currentFrame));
            DispatchSampler("AlterUpdate_ColorCorrection", () => OnUpdateColorCorrection?.Invoke(LiveColorCorrectionTimeline.Evaluate(sheet.colorCorrectionDataLists, _currentFrame, _playMode)));
            DispatchSampler("AlterUpdate_PreColorCorrection", () => OnUpdatePreColorCorrection?.Invoke(LivePreColorCorrectionTimeline.Evaluate(sheet.preColorCorrectionDataLists, _currentFrame, _playMode)));
            DispatchSampler("AlterUpdate_Projector", () => AlterUpdate_Projector(sheet, _currentFrame));
            DispatchSampler("AlterUpdate_AnimationControl", () => AlterUpdate_AnimationControl(sheet, _currentFrame));
            DispatchSampler("AlterUpdate_TextureAnimationControl", () => AlterUpdate_TextureAnimationControl(sheet, _currentFrame));
            DispatchSampler("AlterUpdate_TransformControl", () => AlterUpdate_TransformControl(sheet, _currentFrame));
            DispatchSampler("AlterUpdate_RendererControl", () => AlterUpdate_RendererControl(sheet, _currentFrame));
            DispatchSampler("AlterUpdate_ObjectControl", () => AlterUpdate_ObjectControl(sheet, _currentFrame));
            DispatchSampler("AlterUpdate_WaveObject", () => AlterUpdate_WaveObject(sheet, _currentFrame));
            DispatchSampler("AlterUpdate_Audience", () => OnUpdateAudience?.Invoke(this));
            DispatchSampler("AlterUpdate_FormationOffset", () => AlterLateUpdate_FormationOffset(liveTime));
            DispatchSampler("AlterUpdate_ParentConstraint", () => AlterUpdate_ParentConstraint(sheet, (int)_currentFrame, currentLiveTime));
            DispatchSampler("AlterUpdate_Particle", () => AlterUpdate_Particles(sheet, _currentFrame));
            _laserRuntimeIndexOffset = 0;
            DispatchSampler("AlterUpdate_Laser", () => AlterUpdate_Laser(sheet, _currentFrame));
            DispatchSampler("AlterUpdate_UVScrollLight", () => AlterUpdate_UVScrollLight(sheet, _currentFrame));
            DispatchSampler("AlterUpdate_TiltShift", () => AlterUpdate_TiltShift(sheet, _currentFrame));
            DispatchSampler("AlterUpdate_Hatching", () => AlterUpdate_Hatching(sheet, _currentFrame));
            DispatchSampler("AlterUpdate_EdgeStyle", () => AlterUpdate_EdgeStyle(sheet, _currentFrame));
            DispatchSampler("AlterUpdate_ChromaticAberration", () => AlterUpdate_ChromaticAberration(sheet, _currentFrame));
            DispatchSampler("AlterUpdate_Billboard", () => AlterUpdate_Billboard(sheet));
            DispatchSampler("AlterUpdate_WashLight", () => AlterUpdate_WashLight(sheet, _currentFrame));
            DispatchSampler("AlterUpdate_MultiCameraColorCorrection", () => AlterUpdate_MultiCameraColorCorrection(sheet));
            DispatchSampler("AlterUpdate_MultiCameraTiltShift", () => AlterUpdate_MultiCameraTiltShift(sheet, (int)_currentFrame));
            DispatchSampler("AlterUpdate_Environment", () => AlterUpdate_EnvironmentMirror(sheet, _currentFrame));
            DispatchSampler("AlterUpdate_MirrorReflection", () => AlterUpdate_MirrorReflection(sheet, _currentFrame));
            DispatchSampler("AlterUpdate_GlobalFog", () => AlterUpdate_GlobalFogControl(sheet, _currentFrame));
            DispatchSampler("AlterUpdate_SweatLocator", () => AlterUpdate_SweatLocator(sheet, (int)_currentFrame));
            DispatchSampler("AlterUpdate_LensFlare", () => AlterUpdate_LensFlare(sheet));
            DispatchSampler("AlterUpdate_FlashPlayer", () => FlashEffectProducer.Apply(sheet.flashPlayerKeys, (int)_currentFrame, currentLiveTime, _playMode, FlashController, ChampionsTextController));
            DispatchSampler("AlterUpdate_Title", () => LiveTitleProducer.Evaluate(sheet.titleKeys, (int)_currentFrame, _playMode, TitleController));
            DispatchSampler("AlterUpdate_CameraSwitcher", () => AlterUpdate_CameraSwitcher(sheet, _currentFrame));
            DispatchSampler("AlterUpdate_FacialToon", () => AlterUpdate_FacialToon(sheet, (int)_currentFrame));
            DispatchSampler("AlterUpdate_AdditionalLight", () => AlterUpdate_AdditionalLight(sheet, (int)_currentFrame));
            DispatchSampler("AlterUpdate_MultiLightShadow", () => AlterUpdate_MultiLightShadow(sheet, (int)_currentFrame));
            DispatchSampler("AlterUpdate_MobControl", () => AlterUpdate_MobControl(sheet, _currentFrame));
            DispatchSampler("AlterUpdate_CyalumeControl", () => AlterUpdate_CyalumeControl(sheet, _currentFrame));
            DispatchSampler("AlterUpdate_CharaWind", () => AlterUpdate_CharaWind(sheet, Director.instance != null ? Director.instance.characterCount : 0));
            DispatchSampler("AlterUpdate_CharaCollision", () => AlterUpdate_CharaCollision(sheet, (int)_currentFrame));
            DispatchSampler("AlterUpdate_TransmittedLightMask", () => AlterUpdate_TransmittedLightMask(sheet, (int)_currentFrame));
            DispatchSampler("AlterUpdate_Voice", () => AlterUpdate_Voice(sheet));
            DispatchSampler("AlterUpdate_TransparentCamera", AlterUpdate_TransparentCamera);
            DispatchSampler("AlterUpdate_ScreenCapture", () => AlterUpdate_ScreenCapture(sheet, (int)_currentFrame));
            OnAfterTimelineUpdate?.Invoke();
            _isNowAlterUpdate = false;
        }

        // Clock fields produced by original AlterUpdate (0x4ee8610). Track
        // scheduling and lifecycle guards remain the existing Viewer bridge.
        internal bool AdvanceLiveClock(float liveTime)
        {
            if (data == null)
                return false;
            _oldLiveTime = currentLiveTime;
            currentLiveTime = liveTime;
            _currentFrame = unchecked((int)System.Math.Floor((double)MonitorFloat32(liveTime * 60f)));
            _oldFrame = unchecked((int)System.Math.Floor((double)MonitorFloat32(_oldLiveTime * 60f)));
            _deltaTime = MonitorFloat32(liveTime - _oldLiveTime);
            _deltaTimeRatio = MonitorFloat32(_deltaTime / 0.0166666675f);
            return true;
        }

        // Runtime interpolation consumes the unquantized seconds. Legacy direct
        // frame sampling still has an explicit frame/60 bridge outside updates.
        internal float CameraSampleTime(float frame)
        {
            return _isNowAlterUpdate ? currentLiveTime : MonitorFloat32(frame / 60f);
        }

        // Monitor runs in a separate Viewer LateUpdate. Bridge the original
        // late-phase flag and clock inputs only around its camera evaluation.
        internal void UpdateMonitorCameraCapture(StageMonitorCameraDriver driver)
        {
            bool previous = _isNowAlterUpdate;
            _isNowAlterUpdate = true;
            try
            {
                driver.UpdateCapture(this, _currentFrame, data);
            }
            finally
            {
                _isNowAlterUpdate = previous;
            }
        }

        private bool IsCameraDelayActive(LiveTimelineKey key, float frame)
        {
            return _isNowAlterUpdate && ((int)key.attribute & 2) != 0 && (_oldFrame >= key.frame || frame < key.frame || ((int)key.attribute & 4) != 0);
        }

        internal Vector3 ApplyCameraLookAtDelay(LiveTimelineKeyCameraLookAtData key, Vector3 target, Transform cameraTransform, float frame, Vector3 initialPosition)
        {
            if (!IsCameraDelayActive(key, frame))
                return target;
            Vector3 p = cameraTransform.position;
            float x = MonitorFloat32(target.x - p.x), y = MonitorFloat32(target.y - p.y), z = MonitorFloat32(target.z - p.z);
            float sum = MonitorFloat32(MonitorFloat32(MonitorFloat32(y * y) + MonitorFloat32(x * x)) + MonitorFloat32(z * z));
            float distance = MonitorFloat32((float)System.Math.Sqrt((double)sum));
            if (distance >= float.Epsilon)
            {
                Vector3 direction = new Vector3(MonitorFloat32(x / distance), MonitorFloat32(y / distance), MonitorFloat32(z / distance));
                Vector3 result = Vector3.Slerp(cameraTransform.forward, direction, MonitorFloat32(key.traceSpeed * _deltaTimeRatio));
                return new Vector3(MonitorFloat32(MonitorFloat32(result.x * distance) + initialPosition.x), MonitorFloat32(MonitorFloat32(result.y * distance) + initialPosition.y), MonitorFloat32(MonitorFloat32(result.z * distance) + initialPosition.z));
            }

            return target;
        }

        public void AlterLateUpdate()
        {
            HasDofCameraLookAt = false;
            DofLookAtCamera = null;
            DofTrackCamera = null;
            if (data == null || data.worksheetList == null || data.worksheetList.Count == 0)
                return;
            LiveTimelineWorkSheet camSheet = CurrentWorkSheet;
            _isNowAlterUpdate = true;
            if (!camSheet.enableAtRuntime)
                return;
            Vector3 outLookAt = Vector3.zero;
            DispatchSampler("AlterUpdate_Spotlight3d", () => AlterUpdate_Spotlight3d(_currentFrame));
            DispatchSampler("AlterUpdate_CharaNode", () => AlterUpdate_CharaNode(camSheet, (int)_currentFrame));
            DispatchSampler("AlterUpdate_NodeScale", () => AlterUpdate_NodeScale(camSheet, (int)_currentFrame));
            DispatchSampler("AlterUpdate_CharaNodeOffset", () => AlterUpdate_CharaNodeOffset(camSheet, (int)_currentFrame));
            DispatchSampler("AlterUpdate_Effect", () => AlterUpdate_Effects(false));
            DispatchSampler("AlterUpdate_CameraLayer", () => AlterUpdate_CameraLayer(camSheet, _currentFrame));
            DispatchSampler("AlterUpdate_CameraPos", () => AlterUpdate_CameraPos(camSheet, _currentFrame));
            DispatchSampler("AlterUpdate_CameraLookAt", () => AlterUpdate_CameraLookAt(camSheet, _currentFrame, ref outLookAt));
            ApplyMainCameraLayerOffsets(camSheet, outLookAt);
            DispatchSampler("AlterUpdate_CameraRoll", () => AlterUpdate_CameraRoll(camSheet, _currentFrame));
            DispatchSampler("AlterUpdate_CameraFov", () => AlterUpdate_CameraFov(camSheet, _currentFrame));
            DispatchSampler("AlterUpdate_EffectScale", () => AlterUpdate_Effects(true));
            // Native 0x502b833: attached Object keys run only after the main
            // camera pose, immediately before foot lights / projections.
            DispatchSampler("AlterLateUpdate_ObjectControl", () => AlterUpdate_ObjectControl(camSheet, _currentFrame, true));
            DispatchSampler("AlterLateUpdate_CharaFootLight", () => AlterLateUpdate_CharaFootLight(camSheet, (int)_currentFrame));
            DispatchSampler("AlterLateUpdate_LightProjection", () => AlterLateUpdate_LightProjection(camSheet, _currentFrame));
            DispatchSampler("AlterUpdate_GlobalLight", () => AlterUpdate_GlobalLight(camSheet, _currentFrame));
            DispatchSampler("AlterUpdate_MultiCamera", () => AlterUpdate_MultiCamera(camSheet, _currentFrame));
            if (IsEnablePostEffect)
            {
                DispatchSampler("AlterUpdate_PostEffect_DOF", () => AlterUpdate_PostEffect_Dof(camSheet, (int)_currentFrame));
                DispatchSampler("AlterUpdate_PostEffect_BloomDiffusion", () => AlterUpdate_PostEffect_BloomDiffusion(camSheet, (int)_currentFrame));
                DispatchSampler("AlterUpdate_RadialBlur", () => AlterUpdate_RadialBlur(camSheet, (int)_currentFrame));
            }

            DispatchSampler("AlterUpdate_PostFilm", () => AlterUpdate_PostFilm(camSheet, _currentFrame));
            // Multi-camera films remain unconditional. The bloom track's
            // separate native post gate is supplied at the helper boundary.
            DispatchSampler("AlterUpdate_MultiCameraPostFilm", () => LiveMultiCameraTimeline.EvaluatePostFilms(camSheet.postFilm1MultiCameraKeys, camSheet.postFilm2MultiCameraKeys, camSheet.postFilm3MultiCameraKeys, _multiCamera, _currentFrame, _playMode, _postFilmBlinkResolver));
            if (IsEnablePostEffect)
            {
                DispatchSampler("AlterUpdate_MultiCameraPostEffect_DOF", () => AlterUpdate_MultiCameraPostEffect_DOF(camSheet, (int)_currentFrame));
                DispatchSampler("AlterUpdate_MultiCameraPostEffect_BloomDiffusion", () => LiveMultiCameraTimeline.EvaluateBloomDiffusion(camSheet.postEffectBloomDiffusionMultiCameraKeys, _multiCamera, _currentFrame, _playMode));
                DispatchSampler("AlterUpdate_MultiCameraRadialBlur", () => AlterUpdate_MultiCameraRadialBlur(camSheet, (int)_currentFrame));
                DispatchSampler("AlterUpdate_MultiCameraTransmittedLight", () => AlterUpdate_MultiCameraTransmittedLight(camSheet, (int)_currentFrame));
                DispatchSampler("AlterUpdate_Fluctuation", () => AlterUpdate_Fluctuation(camSheet, (int)_currentFrame));
                // Installed 4ec3a20 calls ToneCurve before Exposure.
                DispatchSampler("AlterUpdate_ToneCurve", () => AlterUpdate_ToneCurve(camSheet, _currentFrame));
                DispatchSampler("AlterUpdate_Exposure", () => AlterUpdate_Exposure(camSheet, _currentFrame));
                DispatchSampler("AlterUpdate_Vortex", () => AlterUpdate_Vortex(camSheet, (int)_currentFrame));
                DispatchSampler("AlterUpdate_TransmittedLight", () => AlterUpdate_TransmittedLight(camSheet, (int)_currentFrame));
                DispatchSampler("AlterUpdate_LensDistortion", () => AlterUpdate_LensDistortion(camSheet, (int)_currentFrame));
            }

            DispatchSampler("AlterUpdate_Fade", () => AlterUpdate_Fade(camSheet, (int)_currentFrame));
            _isNowAlterUpdate = false;
            if (IsRecordVMD)
            {
                var currentFrame = Mathf.RoundToInt(_currentFrame);
                var oldFrame = Mathf.RoundToInt(_oldFrame);
                CacheCamera cacheCamera = GetCamera(camSheet.targetCameraIndex);
                if ((oldFrame == currentFrame && currentFrame > 0) || cacheCamera == null)
                    return;
                LiveCameraFrame lastframe = RecordFrames.Count > 0 ? RecordFrames[RecordFrames.Count - 1] : null;
                var transform = cacheCamera.cacheTransform;
                var camera = cacheCamera.camera;
                LiveCameraFrame frame = new LiveCameraFrame(currentFrame, transform, camera.fieldOfView, lastframe);
                RecordFrames.Add(frame);
                for (int i = 0; i < MultiRecordFrames.Count; i++)
                {
                    var MulFrame = MultiRecordFrames[i];
                    LiveCameraFrame lastMulframe = MulFrame.Count > 0 ? MulFrame[MulFrame.Count - 1] : null;
                    MultiCamera MulCamera = _multiCamera[i];
                    var cam = MulCamera.GetCamera();
                    LiveCameraFrame mulframe = new LiveCameraFrame(currentFrame, MulCamera.transform, cam.fieldOfView, lastMulframe);
                    MulFrame.Add(mulframe);
                }

                if (currentFrame % 2 == 0)
                    RecordUma?.Invoke(); //Set 30 FPS
            }
        }

        public void AlterUpdate_CharaMotionSequence(float liveTime)
        {
            for (int slot = 0; slot < _motionSequenceArray.Length; slot++)
            {
                _motionSequenceArray[slot].AlterUpdate(liveTime, CurrentWorkSheet.timescaleKeys, _motionNoise != null ? _motionNoise.Offset[slot] : 0f);
            }
        }

        public void AlterUpdate_FacialData(float liveTime)
        {
            AdvanceFacialNoiseClockNative(liveTime);
            int frame = unchecked((int)System.Math.Floor((double)MonitorFloat32(liveTime * 60f)));
            AlterUpdate_FacialDataNative(CurrentWorkSheet, frame);
        }

        private void AlterUpdate_AnimationControl(LiveTimelineWorkSheet sheet, float currentFrame)
        {
            if (OnUpdateAnimation == null)
                return;
            if (sheet == null || sheet.animationList == null)
                return;
            int count = sheet.animationList.Count;
            for (int i = 0; i < count; i++)
            {
                LiveTimelineAnimationData animData = sheet.animationList[i];
                if (animData == null)
                    continue;
                LiveTimelineKeyAnimationDataList keys = animData.keys;
                if (keys == null)
                    continue;
                if (keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable))
                    continue;
                if (!keys.EnablePlayModeTimeline(_playMode))
                    continue;
                LiveTimelineKey currentBaseKey;
                LiveTimelineKey nextBaseKey;
                FindTimelineKey(out currentBaseKey, out nextBaseKey, keys, currentFrame);
                LiveTimelineKeyAnimationData currentKey = currentBaseKey as LiveTimelineKeyAnimationData;
                if (currentKey == null)
                    continue;
                LiveTimelineKeyAnimationData nextKey = nextBaseKey as LiveTimelineKeyAnimationData;
                float animationTime = currentKey.AnimationTime;
                if (nextKey != null && nextKey.IsInterpolateKey())
                {
                    float t = CalculateInterpolationValue(currentKey, nextKey, currentFrame);
                    animationTime = MonitorFloat32(currentKey.AnimationTime + MonitorFloat32(MonitorFloat32(nextKey.AnimationTime - currentKey.AnimationTime) * t));
                }

                AnimationUpdateInfo updateInfo = default;
                updateInfo.progressTime = (currentFrame - currentKey.frame) / 60.0f;
                updateInfo.data = animData;
                updateInfo.animationId = currentKey.animationID;
                updateInfo.wrapMode = currentKey.wrapMode;
                updateInfo.speed = currentKey.speed;
                updateInfo.offsetTime = currentKey.offsetTime;
                updateInfo.AnimationTime = animationTime;
                updateInfo.UseAnimationTime = currentKey.UseAnimationTime;
                OnUpdateAnimation(ref updateInfo);
            }
        }

        private void SetupFacialUpdateInfo_Mouth(ref FacialDataUpdateInfo updateInfo, LiveTimelineKeyFacialMouthDataList keys, float time)
        {
            LiveTimelineKey liveTimelineKey = null;
            LiveTimelineKey liveTimelineKey2 = null;
            LiveTimelineKey liveTimelineKey3 = null;
            LiveTimelineKeyIndex curKey = AlterUpdate_Key(keys, time);
            if (curKey != null)
            {
                liveTimelineKey = curKey.prevKey;
                liveTimelineKey2 = curKey.key;
                liveTimelineKey3 = curKey.nextKey;
                updateInfo.mouthPrev = liveTimelineKey as LiveTimelineKeyFacialMouthData;
                updateInfo.mouthCur = liveTimelineKey2 as LiveTimelineKeyFacialMouthData;
                updateInfo.mouthNext = liveTimelineKey3 as LiveTimelineKeyFacialMouthData;
                updateInfo.mouthKeyIndex = curKey.index;
            }
        }

        private void SetupFacialUpdateInfo_Eye(ref FacialDataUpdateInfo updateInfo, LiveTimelineKeyFacialEyeDataList keys, float time)
        {
            LiveTimelineKey liveTimelineKey = null;
            LiveTimelineKey liveTimelineKey2 = null;
            LiveTimelineKey liveTimelineKey3 = null;
            LiveTimelineKeyIndex curKey = AlterUpdate_Key(keys, time);
            if (curKey != null)
            {
                liveTimelineKey = curKey.prevKey;
                liveTimelineKey2 = curKey.key;
                liveTimelineKey3 = curKey.nextKey;
                updateInfo.eyePrev = liveTimelineKey as LiveTimelineKeyFacialEyeData;
                updateInfo.eyeCur = liveTimelineKey2 as LiveTimelineKeyFacialEyeData;
                updateInfo.eyeNext = liveTimelineKey3 as LiveTimelineKeyFacialEyeData;
                updateInfo.eyeKeyIndex = curKey.index;
            }
        }

        private void SetupFacialUpdateInfo_Effect(ref FacialDataUpdateInfo updateInfo, LiveTimelineKeyFacialEffectDataList keys, float time)
        {
            LiveTimelineKey liveTimelineKey = null;
            LiveTimelineKeyIndex curKey = AlterUpdate_Key(keys, time);
            if (curKey != null)
            {
                liveTimelineKey = curKey.key;
                updateInfo.effect = liveTimelineKey as LiveTimelineKeyFacialEffectData;
                updateInfo.effectKeyIndex = curKey.index;
            }
        }

        private void SetupFacialUpdateInfo_Eyebrow(ref FacialDataUpdateInfo updateInfo, LiveTimelineKeyFacialEyebrowDataList keys, float time)
        {
            LiveTimelineKey liveTimelineKey = null;
            LiveTimelineKey liveTimelineKey2 = null;
            LiveTimelineKey liveTimelineKey3 = null;
            LiveTimelineKeyIndex curKey = AlterUpdate_Key(keys, time);
            if (curKey != null)
            {
                liveTimelineKey = curKey.prevKey;
                liveTimelineKey2 = curKey.key;
                liveTimelineKey3 = curKey.nextKey;
                updateInfo.eyebrowPrev = liveTimelineKey as LiveTimelineKeyFacialEyebrowData;
                updateInfo.eyebrowCur = liveTimelineKey2 as LiveTimelineKeyFacialEyebrowData;
                updateInfo.eyebrowNext = liveTimelineKey3 as LiveTimelineKeyFacialEyebrowData;
                updateInfo.eyebrowKeyIndex = curKey.index;
            }
        }

        private void SetupFacialUpdateInfo_Ear(ref FacialDataUpdateInfo updateInfo, LiveTimelineKeyFacialEarDataList keys, float time)
        {
            LiveTimelineKey liveTimelineKey = null;
            LiveTimelineKey liveTimelineKey2 = null;
            LiveTimelineKey liveTimelineKey3 = null;
            LiveTimelineKeyIndex curKey = AlterUpdate_Key(keys, time);
            if (curKey != null)
            {
                liveTimelineKey = curKey.prevKey;
                liveTimelineKey2 = curKey.key;
                liveTimelineKey3 = curKey.nextKey;
                updateInfo.earPrev = liveTimelineKey as LiveTimelineKeyFacialEarData;
                updateInfo.earCur = liveTimelineKey2 as LiveTimelineKeyFacialEarData;
                updateInfo.earNext = liveTimelineKey3 as LiveTimelineKeyFacialEarData;
                updateInfo.earKeyIndex = curKey.index;
            }
        }

        private void SetupFacialUpdateInfo_EyeTrack(ref FacialDataUpdateInfo updateInfo, LiveTimelineKeyFacialEyeTrackDataList keys, float time)
        {
            LiveTimelineKey liveTimelineKey = null;
            LiveTimelineKey liveTimelineKey2 = null;
            LiveTimelineKey liveTimelineKey3 = null;
            LiveTimelineKeyIndex curKey = AlterUpdate_Key(keys, time);
            if (curKey != null)
            {
                liveTimelineKey = curKey.prevKey;
                liveTimelineKey2 = curKey.key;
                liveTimelineKey3 = curKey.nextKey;
                updateInfo.eyeTrackPrev = liveTimelineKey as LiveTimelineKeyFacialEyeTrackData;
                updateInfo.eyeTrackCur = liveTimelineKey2 as LiveTimelineKeyFacialEyeTrackData;
                updateInfo.eyeTrackNext = liveTimelineKey3 as LiveTimelineKeyFacialEyeTrackData;
                updateInfo.eyeTrackKeyIndex = curKey.index;
            }
        }

        public void AlterUpdate_LipSync(float liveTime)
        {
            SampleOrdinaryLipSyncTrack(CurrentWorkSheet.ripSyncKeys, _currentFrame, liveTime);
        }

        public void AlterUpdate_LipSync2(float liveTime)
        {
            SampleOrdinaryLipSyncTrack(CurrentWorkSheet.ripSync2Keys, _currentFrame, liveTime);
        }

        // Original 0x4ed6980 samples both ordinary tracks, in order, using
        // integer currentFrame. Seconds remain the callback's time argument;
        // converting them back to frames can select the previous key at a
        // boundary and the legacy time cursor cannot follow arbitrary jumps.
        void AlterUpdate_OrdinaryLipSync(LiveTimelineWorkSheet sheet, int frame, float liveTime)
        {
            if (OnUpdateLipSync == null)
                return;
            SampleOrdinaryLipSyncTrack(sheet.ripSyncKeys, frame, liveTime);
            SampleOrdinaryLipSyncTrack(sheet.ripSync2Keys, frame, liveTime);
        }

        void SampleOrdinaryLipSyncTrack(LiveTimelineKeyLipSyncDataList keys, int frame, float liveTime)
        {
            if (keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) || !keys.EnablePlayModeTimeline(_playMode))
                return;
            var selected = keys.FindKeyCached(frame, true);
            if (selected.key == null)
                return;
            // The host delegate retains its index DTO, while the native
            // callback receives the selected LipSyncData object directly.
            OnUpdateLipSync?.Invoke(new LiveTimelineKeyIndex { index = selected.index, key = selected.key, prevKey = keys.At(selected.index - 1), nextKey = keys.At(selected.index + 1) }, liveTime);
        }

        public static void FindTimelineKey(out LiveTimelineKey curKey, out LiveTimelineKey nextKey, ILiveTimelineKeyDataList keys, float curFrame)
        {
            FindKeyResult findKeyResult = keys.FindKeyCached(curFrame, availableFindKeyCache);
            curKey = findKeyResult.key;
            if (curKey != null)
            {
                nextKey = keys.At(findKeyResult.index + 1);
            }
            else
            {
                nextKey = null;
            }
        }

        readonly LiveIKTimelineDispatcher _ikTimelineDispatcher = new LiveIKTimelineDispatcher();
        private void AlterUpdate_IKController(LiveTimelineWorkSheet sheet, int frame)
        {
            var director = Director.instance;
            if (director == null)
                return;
            _ikTimelineDispatcher.Update(sheet.formationOffsetSet.Init(), frame, (int)_playMode, index => director.ResolvePhysicalFacialCharacter(index)?.LiveIKActor);
        }

        private void AlterLateUpdate_FormationOffset(float liveTime)
        {
            var formationList = CurrentWorkSheet.formationOffsetSet.Init();
            for (int i = 0; i < formationList.Count && i < liveCharactorLocators.Length; i++)
            {
                // Native 0x4ef79d0 indexes the locator array by the formation
                // slot, not by the compact cast list.
                if (liveCharactorLocators[i] == null)
                    continue;
                // Original formation parent 0x4ed09e0 gates each list, then
                // refinds current/next using integer currentFrame. The seconds
                // cursor can select late and cannot follow a backwards seek.
                var keys = formationList[i];
                if (keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) || !keys.EnablePlayModeTimeline(_playMode) || keys.Count < 1)
                    continue;
                var selected = keys.FindKeyCached(_currentFrame, true);
                var curKey = keys.TimeKeyIndex;
                curKey.index = selected.index;
                curKey.key = selected.key;
                curKey.prevKey = selected.key != null ? keys.At(selected.index - 1) : null;
                curKey.nextKey = selected.key != null ? keys.At(selected.index + 1) : null;
                if (curKey.key != null)
                {
                    // Prior Viewer backdancer fix (restored): appended mob slots
                    // without an authored formation track must not fall through
                    // to the default origin; they are hidden instead of stacking
                    // on the same formation position.
                    var backdancerLocator = liveCharactorLocators[i] as LiveTimelineCharaLocator;
                    var backdancer = backdancerLocator != null ? backdancerLocator.UmaContainer : null;
                    if (backdancer != null && backdancer.IsBackdancer)
                    {
                        if (keys.Count <= 1 || keys.At(keys.Count - 1).frame <= 0)
                        {
                            backdancer.SetLiveVisible(false);
                            continue;
                        }
                    }

                    LiveFormationSample? prepared = null;
                    ApplyFormationOffsetTransform(i, curKey, liveTime, prepared);
                }
            }
        }

        // Original formation locator consumer 0x4ef7de0..0x4ef7f48.
        // Offsets are distances at the two authored height extremes; they are
        // not endpoints of a single normalized Lerp, and are not clamped.
        internal static Vector3 NativeFormationHeightOffset(float height, float baseHeight, bool enabled, Vector3 maximum, Vector3 minimum)
        {
            if (!enabled || !(height > 0f))
                return Vector3.zero;
            Vector3 bound;
            float rate;
            if (height > baseHeight)
            {
                bound = maximum;
                rate = MonitorFloat32(MonitorFloat32(height - baseHeight) * 0.03125f);
            }
            else if (height < baseHeight)
            {
                bound = minimum;
                rate = MonitorFloat32(MonitorFloat32(height - baseHeight) / -28f);
            }
            else
                return Vector3.zero;
            return new Vector3(MonitorFloat32(bound.x * rate), MonitorFloat32(bound.y * rate), MonitorFloat32(bound.z * rate));
        }

        private float FormationCameraTargetHeight()
        {
            // Original GetCameraTargetCharacterHeight 0x4eee6c0 defaults to 158,
            // then reads the current ordinary main-camera LookAt character flags.
            var keys = CurrentWorkSheet?.cameraLookAtKeys;
            if (keys == null || keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) || !keys.EnablePlayModeTimeline(_playMode))
                return 158f;
            FindTimelineKey(out var current, out var next, keys, _currentFrame);
            var look = current as LiveTimelineKeyCameraLookAtData;
            return look != null && look.lookAtType == LiveCameraLookAtType.Character ? GetHeightValueWithCharacters(look.lookAtCharaPos) : 158f;
        }

        private static float LinearInterpolateKeyframes(LiveTimelineKey from, LiveTimelineKey to, float curFrame)
        {
            int num = to.frame - from.frame;
            return Mathf.Clamp01((curFrame - (float)from.frame) / (float)num);
        }

        // Original 0x4ef1fd0 accepts seconds, not a float frame. Keep the
        // existing frame APIs separate until their complete callers are restored.
        public static float LinearInterpolateKeyframesAtTime(LiveTimelineKey from, LiveTimelineKey to, float time)
        {
            float start = MonitorFloat32(from.frame / 60f);
            int frames = unchecked(to.frame - from.frame);
            float duration = MonitorFloat32(frames / 60f);
            float elapsed = MonitorFloat32(time - start);
            return Mathf.Clamp01(MonitorFloat32(elapsed / duration));
        }

        private static float CurveInterpolateKeyframes(LiveTimelineKey from, LiveTimelineKey to, float curFrame)
        {
            LiveTimelineKeyWithInterpolate liveTimelineKeyWithInterpolate = from as LiveTimelineKeyWithInterpolate;
            LiveTimelineKeyWithInterpolate liveTimelineKeyWithInterpolate2 = to as LiveTimelineKeyWithInterpolate;
            if (liveTimelineKeyWithInterpolate == null)
            {
                return 0f;
            }

            if (liveTimelineKeyWithInterpolate2 == null)
            {
                return 0f;
            }

            int num = to.frame - from.frame;
            float time = Mathf.Clamp01((curFrame - (float)from.frame) / (float)num);
            return liveTimelineKeyWithInterpolate2.curve.Evaluate(time);
        }

        private static float EaseInterpolateKeyframes(LiveTimelineKey from, LiveTimelineKey to, float curFrame)
        {
            LiveTimelineKeyWithInterpolate liveTimelineKeyWithInterpolate = from as LiveTimelineKeyWithInterpolate;
            LiveTimelineKeyWithInterpolate liveTimelineKeyWithInterpolate2 = to as LiveTimelineKeyWithInterpolate;
            if (liveTimelineKeyWithInterpolate == null)
            {
                return 0f;
            }

            if (liveTimelineKeyWithInterpolate2 == null)
            {
                return 0f;
            }

            int num = to.frame - from.frame;
            return LiveTimelineEasing.GetValue(liveTimelineKeyWithInterpolate2.easingType, curFrame - (float)from.frame, 0f, 1f, (float)num);
        }

        public void LateUpdateFormationOffset_Transform(int targetIndex, LiveTimelineKeyIndex curKeyIndex, float time) => ApplyFormationOffsetTransform(targetIndex, curKeyIndex, time, null);
        private void ApplyFormationOffsetTransform(int targetIndex, LiveTimelineKeyIndex curKeyIndex, float time, LiveFormationSample? prepared)
        {
            bool ControlMode = UmaViewerUI.Instance != null && UmaViewerUI.Instance.isControlMode;
            LiveTimelineKeyFormationOffsetData curKey = curKeyIndex.key as LiveTimelineKeyFormationOffsetData;
            LiveTimelineKeyFormationOffsetData nextKey = curKeyIndex.nextKey as LiveTimelineKeyFormationOffsetData;
            var locatorOwner = targetIndex >= 0 && targetIndex < liveCharactorLocators.Length ? liveCharactorLocators[targetIndex] as LiveTimelineCharaLocator : null;
            var chara = locatorOwner != null ? locatorOwner.UmaContainer : null;
            if (locatorOwner == null)
                return;
            // Native formation consumer 0x4ef7c58..0x4ef7c77 forwards the
            // authored Live layer index only when IsLayerIndex is enabled.
            if (curKey.IsLayerIndex)
                locatorOwner.LayerIndex = curKey.LayerIndex;
            // Formation's shadow switch was previously only serialized, never consumed.
            // 0x4ef7b0f..0x4ef7b7a: forced bypasses the quality gate; it does
            // not turn a key's disabled shadow into an enabled shadow.
            locatorOwner.IsCastShadow = curKey.IsCastShadow && (LiveQualitySettings.IsRealShadow || (((int)curKey.attribute & 0x40000) != 0));
            var sample = prepared ?? LiveFormationSample.Evaluate(curKey, nextKey, _currentFrame);
            Vector3 heightOffset = NativeFormationHeightOffset(locatorOwner.liveCharaHeightValue, curKey.isEnabledOffset && curKey.isEnabledOffsetCameraTargetChara ? FormationCameraTargetHeight() : 158f, curKey.isEnabledOffset, sample.OffsetMax, sample.OffsetMin);
            locatorOwner.liveCharaFormationHeightRateOffset = heightOffset;
            if (chara != null)
                chara.SetLiveVisible(curKey.visible);
            locatorOwner.IsPositionNodePositionAddParent = curKey.IsPositionAddParentNode;
            locatorOwner.CySpringRate = curKey.CySpringRate;
            if (curKey.visible || IsRecordVMD)
            {
                var locator = locatorOwner;
                locator.liveParentTransform = locator.liveParentDefaultTransform;
                if (curKey.IsEnabledParentObject)
                    locator.liveParentTransform = curKey.GetParentObjectTransform(this);
                var parent = locator.liveParentTransform;
                var output = LiveFormationLocatorTransform.Evaluate(sample, locator.liveCharaInitialPosition, heightOffset, parent != null, parent != null ? parent.localToWorldMatrix : Matrix4x4.identity);
                LiveFormationLocatorTransform.Apply(locator, output);
            }

            // A coordinate-only slot has no model materials or spring state.
            if (chara == null)
                return;
            if (curKey.visible)
                chara.LiveLocator.SetScaleFactor(((int)curKey.attribute & 0x80000) != 0 ? sample.ScaleFactor : 1f);
            // Original parent fills the emissive closure, then the locator child
            // emits color/scroll/intensity/soft-edge every update.
            bool batchSubmission = true;
            var emissiveModels = chara.LiveLocator.EmissiveModels;
            if (batchSubmission)
                emissiveModels.BeginMaterialSubmission();
            try
            {
                bool emissive = curKey.IsEmissiveColor;
                chara.LiveLocator.EmissiveColor = emissive ? sample.EmissiveColor : Color.black;
                chara.LiveLocator.SetEmissiveScale(emissive ? sample.EmissiveSpeed : 0f, emissive ? sample.EmissiveEnergyScale : 0f);
                chara.LiveLocator.SetEmissiveIntensityParam(sample.EmissiveIntensity);
                chara.LiveLocator.SetEmissiveSoftEdgeParam(sample.EmissiveRimPower, sample.EmissiveRimIntensity, emissive && sample.IsEmissiveCenter);
                // Original model lifecycle child forwards DirtEnergyB/G/R per model.
                chara.LiveLocator.SetDirtParameter(sample.DirtEnergyB, sample.DirtEnergyG, sample.DirtEnergyR, sample.DirtColor, sample.DirtRimSpecularColor, sample.DirtToonColor);
            }
            finally
            {
                if (batchSubmission)
                    emissiveModels.EndMaterialSubmission();
            }

            chara.LiveLocator.ApplyFormationCySpringWarmup(curKey, (dress, seconds) => Director.instance.ReserveFormationPropCySpring(targetIndex, dress, seconds));
        }

        public static void FindTimelineKeyCurrent(out LiveTimelineKey curKey, ILiveTimelineKeyDataList keys, float curFrame)
        {
            LiveTimelineKey nextKey;
            FindTimelineKey(out curKey, out nextKey, keys, curFrame);
        }

        public static void FindTimelineKeyCurrent(out LiveTimelineKeyIndex curKey, ILiveTimelineKeyDataList keys, float curTime)
        {
            curKey = keys.FindCurrentKey(curTime);
        }

        public static void UpdateTimelineKeyCurrent(out LiveTimelineKeyIndex curKey, ILiveTimelineKeyDataList keys, float curTime)
        {
            curKey = keys.UpdateCurrentKey(curTime);
        }

        public static LiveTimelineKeyIndex AlterUpdate_Key(ILiveTimelineKeyDataList keys, float curTime)
        {
            LiveTimelineKeyIndex curKey = keys.TimeKeyIndex;
            if (curKey.index == -1 || Director.instance.sliderControl.is_Touched)
            {
                FindTimelineKeyCurrent(out curKey, keys, curTime);
            }
            else
            {
                UpdateTimelineKeyCurrent(out curKey, keys, curTime);
            }

            return curKey;
        }

        public Quaternion GetMultiCameraWorldRotation(int index)
        {
            if (_multiCameraCache == null || _multiCameraCache.Length <= index)
            {
                return Quaternion.identity;
            }

            return _multiCameraCache[index].cacheTransform.rotation;
        }

        public Vector3 GetMultiCameraWorldPosition(int index)
        {
            if (_multiCameraCache == null || _multiCameraCache.Length <= index)
            {
                return Vector3.zero;
            }

            return _multiCameraCache[index].cacheTransform.position;
        }

        public bool ExistsMultiCamera(int index)
        {
            if (_multiCameraCache != null && index < _multiCameraCache.Length)
            {
                return _multiCameraCache[index] != null;
            }

            return false;
        }

        public float GetCharacterHeight(LiveCharaPosition position)
        {
            return liveCharactorLocators[(int)position].liveCharaHeightValue;
        }

        // Original 0x4eef900: pure locator reduction. Layer/height offsets belong
        // to the camera producers, not to this target query.
        public Vector3 GetPositionWithCharacters(LiveCharaPositionFlag posFlags, LiveCameraCharaParts parts)
        {
            if (posFlags == 0)
                return liveStageCenterPos;
            int part = (int)parts;
            if (part < 0 || part > 19 || (part >= 11 && part <= 14))
                return Vector3.zero;
            Vector3 sum = Vector3.zero;
            int count = 0;
            for (int i = 0; i < 20; i++)
            {
                if (((int)posFlags & (1 << i)) == 0)
                    continue;
                var locator = _liveCharactorLocators[i];
                if (locator == null)
                    continue;
                Vector3 value;
                switch (part)
                {
                    case 0:
                        value = locator.liveCharaHeadPosition;
                        break;
                    case 1:
                        value = locator.liveCharaWaistPosition;
                        break;
                    case 2:
                        value = locator.liveCharaLeftHandWristPosition;
                        break;
                    case 3:
                        value = locator.liveCharaRightHandAttachPosition;
                        break;
                    case 4:
                        value = locator.liveCharaChestPosition;
                        break;
                    case 5:
                        value = locator.liveCharaFootPosition;
                        break;
                    case 6:
                        value = locator.liveCharaConstHeightHeadPosition;
                        break;
                    case 7:
                        value = locator.liveCharaConstHeightWaistPosition;
                        break;
                    case 8:
                        value = locator.liveCharaConstHeightChestPosition;
                        break;
                    case 9:
                        value = locator.liveCharaRightHandWristPosition;
                        break;
                    case 10:
                        value = locator.liveCharaLeftHandAttachPosition;
                        break;
                    case 15:
                        value = locator.liveCharaPosition;
                        break;
                    case 16:
                        value = locator.liveCharaPosition - locator.liveCharaFormationHeightRateOffset;
                        break;
                    case 17:
                        value = locator.liveCharaInitialHeightHeadPosition;
                        break;
                    case 18:
                        value = locator.liveCharaInitialHeightChestPosition;
                        break;
                    default:
                        value = locator.liveCharaInitialHeightWaistPosition;
                        break;
                }

                sum += value;
                count++;
            }

            return count > 1 ? new Vector3(MonitorFloat32(sum.x / count), MonitorFloat32(sum.y / count), MonitorFloat32(sum.z / count)) : sum;
        }

        // Legacy adapter retained for DOF/parts20 pending their own native restoration.
        // Ordinary camera keys now use the two-argument original reduction above.
        public Vector3 GetPositionWithCharacters(LiveCharaPositionFlag posFlags, LiveCameraCharaParts parts, Vector3 charaPos, Vector3 cameraOffset)
        {
            Vector3 retPos = Vector3.zero;
            Vector3 tmpPos = Vector3.zero;
            if (posFlags == 0)
            {
                retPos = liveStageCenterPos;
                tmpPos = liveStageCenterPos;
            }
            else
            {
                int num = 0;
                switch (parts)
                {
                    case LiveCameraCharaParts.Face:
                    {
                        for (int i = 0; i < liveCharaPositionMax; i++)
                        {
                            if (posFlags.hasFlag((LiveCharaPosition)i) && liveCharactorLocators[i] != null)
                            {
                                retPos += liveCharactorLocators[i].liveCharaHeadPosition;
                                retPos += cameraOffset * liveCharactorLocators[i].liveCharaHeightRatio;
                                num++;
                            }
                        }

                        break;
                    }

                    case LiveCameraCharaParts.Waist:
                    {
                        for (int i = 0; i < liveCharaPositionMax; i++)
                        {
                            if (posFlags.hasFlag((LiveCharaPosition)i) && liveCharactorLocators[i] != null)
                            {
                                retPos += liveCharactorLocators[i].liveCharaWaistPosition;
                                retPos += cameraOffset * liveCharactorLocators[i].liveCharaHeightRatio;
                                num++;
                            }
                        }

                        break;
                    }

                    case LiveCameraCharaParts.LeftHandWrist:
                    {
                        for (int i = 0; i < liveCharaPositionMax; i++)
                        {
                            if (posFlags.hasFlag((LiveCharaPosition)i) && liveCharactorLocators[i] != null)
                            {
                                retPos += liveCharactorLocators[i].liveCharaLeftHandWristPosition;
                                retPos += cameraOffset * liveCharactorLocators[i].liveCharaHeightRatio;
                                num++;
                            }
                        }

                        break;
                    }

                    case LiveCameraCharaParts.LeftHandAttach:
                    {
                        for (int i = 0; i < liveCharaPositionMax; i++)
                        {
                            if (posFlags.hasFlag((LiveCharaPosition)i) && liveCharactorLocators[i] != null)
                            {
                                retPos += liveCharactorLocators[i].liveCharaLeftHandAttachPosition;
                                retPos += cameraOffset * liveCharactorLocators[i].liveCharaHeightRatio;
                                num++;
                            }
                        }

                        break;
                    }

                    case LiveCameraCharaParts.RightHandWrist:
                    {
                        for (int i = 0; i < liveCharaPositionMax; i++)
                        {
                            if (posFlags.hasFlag((LiveCharaPosition)i) && liveCharactorLocators[i] != null)
                            {
                                retPos += liveCharactorLocators[i].liveCharaRightHandWristPosition;
                                retPos += cameraOffset * liveCharactorLocators[i].liveCharaHeightRatio;
                                num++;
                            }
                        }

                        break;
                    }

                    case LiveCameraCharaParts.RightHandAttach:
                    {
                        for (int i = 0; i < liveCharaPositionMax; i++)
                        {
                            if (posFlags.hasFlag((LiveCharaPosition)i) && liveCharactorLocators[i] != null)
                            {
                                retPos += liveCharactorLocators[i].liveCharaRightHandAttachPosition;
                                retPos += cameraOffset * liveCharactorLocators[i].liveCharaHeightRatio;
                                num++;
                            }
                        }

                        break;
                    }

                    case LiveCameraCharaParts.Chest:
                    {
                        for (int i = 0; i < liveCharaPositionMax; i++)
                        {
                            if (posFlags.hasFlag((LiveCharaPosition)i) && liveCharactorLocators[i] != null)
                            {
                                retPos += liveCharactorLocators[i].liveCharaChestPosition;
                                retPos += cameraOffset * liveCharactorLocators[i].liveCharaHeightRatio;
                                num++;
                            }
                        }

                        break;
                    }

                    case LiveCameraCharaParts.Foot:
                    {
                        for (int i = 0; i < liveCharaPositionMax; i++)
                        {
                            if (posFlags.hasFlag((LiveCharaPosition)i) && liveCharactorLocators[i] != null)
                            {
                                retPos += liveCharactorLocators[i].liveCharaFootPosition;
                                retPos += cameraOffset * liveCharactorLocators[i].liveCharaHeightRatio;
                                num++;
                            }
                        }

                        break;
                    }

                    case LiveCameraCharaParts.ConstFaceHeight:
                    {
                        for (int i = 0; i < liveCharaPositionMax; i++)
                        {
                            if (posFlags.hasFlag((LiveCharaPosition)i) && liveCharactorLocators[i] != null)
                            {
                                retPos.y += liveCharactorLocators[i].liveCharaConstHeightHeadPosition.y;
                                retPos += cameraOffset * liveCharactorLocators[i].liveCharaHeightRatio;
                                retPos += charaPos;
                                num++;
                            }
                        }

                        break;
                    }

                    case LiveCameraCharaParts.ConstWaistHeight:
                    {
                        for (int i = 0; i < liveCharaPositionMax; i++)
                        {
                            if (posFlags.hasFlag((LiveCharaPosition)i) && liveCharactorLocators[i] != null)
                            {
                                retPos.y += liveCharactorLocators[i].liveCharaConstHeightWaistPosition.y;
                                retPos += cameraOffset * liveCharactorLocators[i].liveCharaHeightRatio;
                                retPos += charaPos;
                                num++;
                            }
                        }

                        break;
                    }

                    case LiveCameraCharaParts.ConstChestHeight:
                    {
                        for (int i = 0; i < liveCharaPositionMax; i++)
                        {
                            if (posFlags.hasFlag((LiveCharaPosition)i) && liveCharactorLocators[i] != null)
                            {
                                retPos.y += liveCharactorLocators[i].liveCharaConstHeightChestPosition.y;
                                retPos += cameraOffset * liveCharactorLocators[i].liveCharaHeightRatio;
                                retPos += charaPos;
                                num++;
                            }
                        }

                        break;
                    }

                    case LiveCameraCharaParts.ConstFootHeight:
                    {
                        for (int i = 0; i < liveCharaPositionMax; i++)
                        {
                            if (posFlags.hasFlag((LiveCharaPosition)i) && liveCharactorLocators[i] != null)
                            {
                                retPos.y += liveCharactorLocators[i].liveCharaFootPosition.y;
                                retPos += cameraOffset * liveCharactorLocators[i].liveCharaHeightRatio;
                                retPos += charaPos;
                                num++;
                            }
                        }

                        break;
                    }

                    case LiveCameraCharaParts.InitFaceHeight:
                    {
                        for (int i = 0; i < liveCharaPositionMax; i++)
                        {
                            if (posFlags.hasFlag((LiveCharaPosition)i) && liveCharactorLocators[i] != null)
                            {
                                /*
                                    retPos += liveCharactorLocators[i].liveCharaHeadPosition;
                                    tmpPos += liveCharactorLocators[i].liveCharaConstHeightHeadPosition;
                                    Vector3 vector3 = cameraOffset * liveCharactorLocators[i].liveCharaHeightRatio;
                                    retPos += vector3;
                                    tmpPos += vector3;
                                    num++;
                                    */
                                retPos += liveCharactorLocators[i].liveCharaInitialHeightHeadPosition;
                                retPos += cameraOffset * liveCharactorLocators[i].liveCharaHeightRatio;
                                num++;
                            }
                        }

                        break;
                    }

                    case LiveCameraCharaParts.InitChestHeight:
                    {
                        for (int i = 0; i < liveCharaPositionMax; i++)
                        {
                            if (posFlags.hasFlag((LiveCharaPosition)i) && liveCharactorLocators[i] != null)
                            {
                                /*
                                    retPos += liveCharactorLocators[i].liveCharaChestPosition;
                                    tmpPos += liveCharactorLocators[i].liveCharaConstHeightChestPosition;
                                    Vector3 vector2 = cameraOffset * liveCharactorLocators[i].liveCharaHeightRatio;
                                    retPos += vector2;
                                    tmpPos += vector2;
                                    num++;
                                    */
                                retPos += liveCharactorLocators[i].liveCharaInitialHeightChestPosition;
                                retPos += cameraOffset * liveCharactorLocators[i].liveCharaHeightRatio;
                                num++;
                            }
                        }

                        break;
                    }

                    case LiveCameraCharaParts.InitWaistHeight:
                    {
                        for (int i = 0; i < liveCharaPositionMax; i++)
                        {
                            if (posFlags.hasFlag((LiveCharaPosition)i) && liveCharactorLocators[i] != null)
                            {
                                /*
                                    retPos += liveCharactorLocators[i].liveCharaWaistPosition;
                                    tmpPos += liveCharactorLocators[i].liveCharaConstHeightWaistPosition;
                                    Vector3 vector = cameraOffset * liveCharactorLocators[i].liveCharaHeightRatio;
                                    retPos += vector;
                                    tmpPos += vector;
                                    num++;
                                    */
                                retPos += liveCharactorLocators[i].liveCharaInitialHeightWaistPosition;
                                retPos += cameraOffset * liveCharactorLocators[i].liveCharaHeightRatio;
                                num++;
                            }
                        }

                        break;
                    }
                }

                bool flag = num > 1;
                if (flag)
                {
                    retPos /= (float)num;
                }
            /*
                if ((uint)(parts - 11) <= 2u)
                {
                    if (flag)
                    {
                        tmpPos /= (float)num;
                    }
                    retPos.y = tmpPos.y;
                }
                */
            }

            return retPos;
        }

        public Vector3 GetPositionWithCharacters(LiveCharaPositionFlag posFlags, LiveCameraCharaParts parts, Vector3 charaPos)
        {
            return GetPositionWithCharacters(posFlags, parts, charaPos, _cameraLayerOffset);
        }

        // Original 0x4eeb970/0x4eed540 use seconds. Keep the legacy frame
        // dispatcher below for tracks whose time producers are still pending.
        public static float CalculateInterpolationValueAtTime(LiveTimelineKey curKey, LiveTimelineKeyWithInterpolate nextKey, float time)
        {
            switch (nextKey.interpolateType)
            {
                case LiveCameraInterpolateType.Linear:
                    return LinearInterpolateKeyframesAtTime(curKey, nextKey, time);
                case LiveCameraInterpolateType.Curve:
                    if (!(curKey is LiveTimelineKeyWithInterpolate))
                        return 0f;
                    return nextKey.curve.Evaluate(LinearInterpolateKeyframesAtTime(curKey, nextKey, time));
                case LiveCameraInterpolateType.Ease:
                    if (!(curKey is LiveTimelineKeyWithInterpolate))
                        return 0f;
                    float start = MonitorFloat32(curKey.frame / 60f);
                    float duration = MonitorFloat32(unchecked(nextKey.frame - curKey.frame) / 60f);
                    float elapsed = MonitorFloat32(time - start);
                    return LiveTimelineEasing.GetValue(nextKey.easingType, elapsed, 0f, 1f, duration);
                default:
                    return 0f;
            }
        }

        // Existing Viewer clock bridge, not equivalence to the original clock.
        public static float CalculateCameraInterpolationValue(LiveTimelineKey curKey, LiveTimelineKeyWithInterpolate nextKey, float frame)
        {
            return CalculateInterpolationValueAtTime(curKey, nextKey, MonitorFloat32(frame / 60f));
        }

        public static float CalculateInterpolationValue(LiveTimelineKey curKey, LiveTimelineKeyWithInterpolate nextKey, float frame)
        {
            float result = 0f;
            switch (nextKey.interpolateType)
            {
                case LiveCameraInterpolateType.Linear:
                    result = LinearInterpolateKeyframes(curKey, nextKey, frame);
                    break;
                case LiveCameraInterpolateType.Curve:
                    result = CurveInterpolateKeyframes(curKey, nextKey, frame);
                    break;
                case LiveCameraInterpolateType.Ease:
                    result = EaseInterpolateKeyframes(curKey, nextKey, frame);
                    break;
            }

            return result;
        }

        public void SetTimelineCamera(Camera cam, int index)
        {
            if (index < _cameraArray.Length)
            {
                if (_cameraArray[index] == null)
                {
                    _cameraArray[index] = new CacheCamera(cam);
                }
                else
                {
                    _cameraArray[index].Set(cam);
                }

                LiveTimelineCamera liveTimelineCamera = cam.gameObject.GetComponent<LiveTimelineCamera>();
                if (liveTimelineCamera == null)
                {
                    liveTimelineCamera = cam.gameObject.AddComponent<LiveTimelineCamera>();
                }

                _cameraScriptArray[index] = liveTimelineCamera;
                if (liveTimelineCamera != null)
                {
                    liveTimelineCamera.AlterAwake();
                }
            }
        }

        private void AlterUpdate_CameraSwitcher(LiveTimelineWorkSheet sheet, float currentFrame)
        {
            if (sheet.cameraSwitcherKeys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) || !sheet.cameraSwitcherKeys.EnablePlayModeTimeline(_playMode))
            {
                return;
            }

            LiveTimelineKey curKey = null;
            FindTimelineKeyCurrent(out curKey, sheet.cameraSwitcherKeys, currentFrame);
            if (curKey == null)
            {
                return;
            }

            LiveTimelineKeyCameraSwitcherData liveTimelineKeyCameraSwitcherData = curKey as LiveTimelineKeyCameraSwitcherData;
            if (this.OnUpdateCameraSwitcher != null)
            {
                this.OnUpdateCameraSwitcher(liveTimelineKeyCameraSwitcherData.cameraIndex);
            }
            else
            {
                if (liveTimelineKeyCameraSwitcherData.cameraIndex >= cameraArray.Length)
                {
                    return;
                }

                for (int i = 0; i < cameraArray.Length; i++)
                {
                    if (cameraArray[i] == null)
                    {
                        continue;
                    }

                    if (i == liveTimelineKeyCameraSwitcherData.cameraIndex)
                    {
                        if (!cameraArray[i].camera.enabled)
                        {
                            cameraArray[i].camera.enabled = true;
                            cameraScriptArray[i].enabled = true;
                        }
                    }
                    else if (cameraArray[i].camera.enabled)
                    {
                        cameraArray[i].camera.enabled = false;
                        cameraScriptArray[i].enabled = false;
                    }
                }
            }
        }

        private void AlterUpdate_CameraPos(LiveTimelineWorkSheet sheet, float currentFrame)
        {
            _cameraPosLayerOffset = Vector3.zero;
            if (sheet.cameraPosKeys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) || !sheet.cameraPosKeys.EnablePlayModeTimeline(_playMode))
            {
                return;
            }

            CacheCamera camera = GetCamera(sheet.targetCameraIndex);
            if (camera == null)
            {
                return;
            }

            LiveTimelineKey curKey = null;
            LiveTimelineKey nextKey = null;
            FindTimelineKey(out curKey, out nextKey, sheet.cameraPosKeys, currentFrame);
            if (curKey == null)
            {
                return;
            }

            LiveTimelineKeyCameraPositionData liveTimelineKeyCameraPositionData = curKey as LiveTimelineKeyCameraPositionData;
            var timelineCameraController = camera.camera.GetComponent<Gallop.TimelineCameraController>();
            if (timelineCameraController != null)
            {
                if (!timelineCameraController.IsInitialized)
                    timelineCameraController.Init(camera.camera, camera.cacheTransform, null, camera.camera.GetComponent<Animator>());
                timelineCameraController.SetClipPlane(liveTimelineKeyCameraPositionData.nearClip, liveTimelineKeyCameraPositionData.farClip);
            }
            else
            {
                camera.camera.nearClipPlane = liveTimelineKeyCameraPositionData.nearClip;
                camera.camera.farClipPlane = liveTimelineKeyCameraPositionData.farClip;
            }

            // Original AlterUpdate_CameraPos (0x4ec8f40) only writes flagged
            // culling/background state; background resolves the shared key getter.
            if (((int)liveTimelineKeyCameraPositionData.attribute & 0x20000) != 0)
                camera.camera.cullingMask = liveTimelineKeyCameraPositionData.GetCullingMask();
            if (((int)liveTimelineKeyCameraPositionData.attribute & 0x40000) != 0)
                camera.camera.backgroundColor = liveTimelineKeyCameraPositionData.GetBgColor();

            bool hasPosition = CalculateCameraPos(out var pos, sheet, curKey, nextKey, currentFrame);
            if (hasPosition)
            {
                camera.cacheTransform.position = pos;
                if (liveTimelineKeyCameraPositionData.setType == LiveCameraPositionType.Character)
                    GetCameraLayerOffset(liveTimelineKeyCameraPositionData.charaRelativeBase, _cameraLayerOffsetMin, _cameraLayerOffsetDiff, out _cameraPosLayerOffset);
                _currentCameraPosKeyFrame = liveTimelineKeyCameraPositionData.frame;
            }

        }

        private static Vector3 GetCameraPosValue(LiveTimelineKeyCameraPositionData keyData, LiveTimelineControl timelineControl, FindTimelineConfig config)
        {
            return keyData.GetValue(timelineControl);
        }

        public bool CalculateCameraPos(out Vector3 pos, LiveTimelineWorkSheet sheet, float currentFrame)
        {
            FindTimelineConfig config = default(FindTimelineConfig);
            config.curKey = null;
            config.nextKey = null;
            config.keyType = FindTimelineConfig.KeyType.CurrentFrame;
            config.posKeys = sheet.cameraPosKeys;
            config.lookAtKeys = null;
            config.extraCameraIndex = 0;
            CacheCamera camera = GetCamera(sheet.targetCameraIndex);
            return CalculateCameraPos(out pos, sheet, currentFrame, camera, ref config, ref fnGetCameraPosValue);
        }

        public bool CalculateCameraPos(out Vector3 pos, LiveTimelineWorkSheet sheet, LiveTimelineKey curKey, LiveTimelineKey nextKey, float currentFrame)
        {
            FindTimelineConfig config = default(FindTimelineConfig);
            config.curKey = curKey;
            config.nextKey = nextKey;
            config.keyType = FindTimelineConfig.KeyType.KeyDirect;
            config.posKeys = sheet.cameraPosKeys;
            config.lookAtKeys = null;
            CacheCamera camera = GetCamera(sheet.targetCameraIndex);
            config.extraCameraIndex = 0;
            return CalculateCameraPos(out pos, sheet, currentFrame, camera, ref config, ref fnGetCameraPosValue);
        }

        public bool CalculateCameraPos(out Vector3 pos, LiveTimelineWorkSheet sheet, float currentFrame, CacheCamera targetCamera, ref FindTimelineConfig config, ref Func<LiveTimelineKeyCameraPositionData, LiveTimelineControl, FindTimelineConfig, Vector3> getFunc)
        {
            pos = Vector3.zero;
            LiveTimelineKey curKey = null;
            LiveTimelineKey nextKey = null;
            if (config.posKeys == null)
            {
                return false;
            }

            if (config.keyType == FindTimelineConfig.KeyType.CurrentFrame)
            {
                FindTimelineKey(out curKey, out nextKey, config.posKeys, currentFrame);
            }
            else
            {
                curKey = config.curKey;
                nextKey = config.nextKey;
            }

            if (curKey == null)
            {
                return false;
            }

            LiveTimelineKeyCameraPositionData liveTimelineKeyCameraPositionData = curKey as LiveTimelineKeyCameraPositionData;
            LiveTimelineKeyCameraPositionData liveTimelineKeyCameraPositionData2 = nextKey as LiveTimelineKeyCameraPositionData;
            if (liveTimelineKeyCameraPositionData2 != null && liveTimelineKeyCameraPositionData2.interpolateType != 0)
            {
                CacheFutureCameraPosition(liveTimelineKeyCameraPositionData, liveTimelineKeyCameraPositionData2, Mathf.RoundToInt(currentFrame), config.posKeys);
                float t = CalculateInterpolationValueAtTime(liveTimelineKeyCameraPositionData, liveTimelineKeyCameraPositionData2, CameraSampleTime(currentFrame));
                Vector3 start = getFunc(liveTimelineKeyCameraPositionData, this, config);
                Vector3 end = getFunc(liveTimelineKeyCameraPositionData2, this, config);
                int bezierPointCount = liveTimelineKeyCameraPositionData2.GetBezierPointCount();
                if (bezierPointCount == 0)
                {
                    pos = BezierUtil.LerpUnclamped(start, end, t);
                }
                else if (liveTimelineKeyCameraPositionData2.necessaryToUseNewBezierCalcMethod)
                {
                    BezierCalcWork.cameraPos.Set(start, end, bezierPointCount);
                    BezierCalcWork.cameraPos.UpdatePoints(liveTimelineKeyCameraPositionData2, this);
                    BezierCalcWork.cameraPos.Calc(bezierPointCount, t, out pos);
                }
                else
                {
                    Vector3 cp1 = liveTimelineKeyCameraPositionData2.GetBezierPoint(0, this);
                    Vector3 cp2 = liveTimelineKeyCameraPositionData2.GetBezierPoint(1, this);
                    Vector3 cp3 = liveTimelineKeyCameraPositionData2.GetBezierPoint(2, this);
                    Vector3 cp4 = liveTimelineKeyCameraPositionData2.GetBezierPoint(3, this);
                    switch (liveTimelineKeyCameraPositionData2.GetBezierPointCount())
                    {
                        case 1:
                            BezierUtil.Calc(ref start, ref end, ref cp1, t, out pos);
                            break;
                        case 2:
                            BezierUtil.Calc(ref start, ref end, ref cp1, ref cp2, t, out pos);
                            break;
                        case 3:
                            BezierUtil.Calc(ref start, ref end, ref cp1, ref cp2, ref cp3, t, out pos);
                            break;
                        case 4:
                            BezierUtil.Calc(ref start, ref end, ref cp1, ref cp2, ref cp3, ref cp4, t, out pos);
                            break;
                        default:
                            pos = BezierUtil.LerpUnclamped(start, end, t);
                            break;
                    }
                }
            }
            else
            {
                pos = getFunc(liveTimelineKeyCameraPositionData, this, config);
            }

            if (IsCameraDelayActive(liveTimelineKeyCameraPositionData, currentFrame))
            {
                if (targetCamera == null || targetCamera.cacheTransform == null)
                {
                    return false;
                }

                float t2 = MonitorFloat32(liveTimelineKeyCameraPositionData.traceSpeed * _deltaTimeRatio);
                pos = Vector3.Slerp(targetCamera.cacheTransform.position, pos, t2);
            }

            return true;
        }

        private void AlterUpdate_CameraLookAt(LiveTimelineWorkSheet sheet, float currentFrame, ref Vector3 outLookAt)
        {
            _cameraLookAtLayerOffset = Vector3.zero;
            if (!sheet.cameraLookAtKeys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) && sheet.cameraLookAtKeys.EnablePlayModeTimeline(_playMode))
            {
                CacheCamera camera = GetCamera(sheet.targetCameraIndex);
                if (camera == null)
                    return;

                bool hasLookAt = CalculateCameraLookAt(out var lookAtPos, sheet, currentFrame);
                if (hasLookAt)
                {
                    camera.cacheTransform.LookAt(lookAtPos, Vector3.up);
                    FindTimelineKeyCurrent(out LiveTimelineKey appliedLookAtKey, sheet.cameraLookAtKeys, currentFrame);
                    if (appliedLookAtKey is LiveTimelineKeyCameraLookAtData key)
                    {
                        float rate = key.GetHeightRate(this);
                        FindTimelineKey(out _, out var nextBase, sheet.cameraLookAtKeys, currentFrame);
                        if (nextBase is LiveTimelineKeyCameraLookAtData next && next.IsInterpolateKey())
                            rate = LerpWithoutClamp(rate, next.GetHeightRate(this), CalculateInterpolationValueAtTime(key, next, CameraSampleTime(currentFrame)));
                        camera.cacheTransform.localScale = Vector3.one * rate;
                        if (key.lookAtType == LiveCameraLookAtType.Character)
                            GetCameraLayerOffset(key.lookAtCharaPos, _cameraLayerOffsetMin, _cameraLayerOffsetDiff, out _cameraLookAtLayerOffset);
                    }

                    if (appliedLookAtKey != null)
                        _currentCameraLookAtKeyFrame = appliedLookAtKey.frame;
                    outLookAt = lookAtPos;
                    DofCameraLookAt = lookAtPos;
                    DofLookAtCamera = camera.camera;
                    HasDofCameraLookAt = true;
                }

            }
        }

        private static Vector3 GetCameraLookAtValue(LiveTimelineKeyCameraLookAtData keyData, LiveTimelineControl timelineControl, Vector3 camPos, FindTimelineConfig config)
        {
            return keyData.GetValue(timelineControl, camPos);
        }

        public bool CalculateCameraLookAt(out Vector3 lookAtPos, LiveTimelineWorkSheet sheet, float currentFrame)
        {
            CacheCamera camera = GetCamera(sheet.targetCameraIndex);
            FindTimelineConfig config = default(FindTimelineConfig);
            config.curKey = null;
            config.nextKey = null;
            config.keyType = FindTimelineConfig.KeyType.CurrentFrame;
            config.posKeys = sheet.cameraPosKeys;
            config.lookAtKeys = sheet.cameraLookAtKeys;
            config.extraCameraIndex = 0;
            return CalculateCameraLookAt(out lookAtPos, sheet, currentFrame, camera, ref config, ref fnGetCameraLookAtValue, ref fnGetCameraPosValue);
        }

        private bool CalculateCameraLookAt(out Vector3 lookAtPos, LiveTimelineWorkSheet sheet, float currentFrame, CacheCamera targetCamera, ref FindTimelineConfig config, ref Func<LiveTimelineKeyCameraLookAtData, LiveTimelineControl, Vector3, FindTimelineConfig, Vector3> getLookAtValueFunc, ref Func<LiveTimelineKeyCameraPositionData, LiveTimelineControl, FindTimelineConfig, Vector3> getPosValueFunc)
        {
            lookAtPos = Vector3.zero;
            CacheCamera camera = targetCamera;
            if (camera == null || camera.cacheTransform == null || config.lookAtKeys == null || config.posKeys == null)
            {
                return false;
            }

            LiveTimelineKey curKey = null;
            LiveTimelineKey nextKey = null;
            if (config.keyType == FindTimelineConfig.KeyType.CurrentFrame)
                FindTimelineKey(out curKey, out nextKey, config.lookAtKeys, currentFrame);
            else
            {
                curKey = config.curKey;
                nextKey = config.nextKey;
            }

            if (curKey == null)
            {
                return false;
            }

            Vector3 position = camera.cacheTransform.position;
            LiveTimelineKeyCameraLookAtData liveTimelineKeyCameraLookAtData = curKey as LiveTimelineKeyCameraLookAtData;
            LiveTimelineKeyCameraLookAtData liveTimelineKeyCameraLookAtData2 = nextKey as LiveTimelineKeyCameraLookAtData;
            if (liveTimelineKeyCameraLookAtData2 != null && liveTimelineKeyCameraLookAtData2.interpolateType != 0)
            {
                CacheFutureCameraLookAt(liveTimelineKeyCameraLookAtData, liveTimelineKeyCameraLookAtData2, Mathf.RoundToInt(currentFrame), config.lookAtKeys);
                float t = CalculateInterpolationValueAtTime(liveTimelineKeyCameraLookAtData, liveTimelineKeyCameraLookAtData2, CameraSampleTime(currentFrame));
                Vector3 start = getLookAtValueFunc(liveTimelineKeyCameraLookAtData, this, position, config);
                Vector3 end = getLookAtValueFunc(liveTimelineKeyCameraLookAtData2, this, position, config);
                int bezierPointCount = liveTimelineKeyCameraLookAtData2.GetBezierPointCount();
                if (bezierPointCount == 0)
                {
                    lookAtPos = BezierUtil.LerpUnclamped(start, end, t);
                }
                else if (liveTimelineKeyCameraLookAtData2.necessaryToUseNewBezierCalcMethod)
                {
                    Vector3 zero = Vector3.zero;
                    BezierCalcWork.cameraLookAt.Set(start, end, bezierPointCount);
                    BezierCalcWork.cameraLookAt.UpdatePoints(liveTimelineKeyCameraLookAtData2, this, zero);
                    BezierCalcWork.cameraLookAt.Calc(bezierPointCount, t, out lookAtPos);
                }
                else
                {
                    Vector3 zero2 = Vector3.zero;
                    Vector3 cp = liveTimelineKeyCameraLookAtData2.GetBezierPoint(0, this, zero2);
                    Vector3 cp2 = liveTimelineKeyCameraLookAtData2.GetBezierPoint(1, this, zero2);
                    Vector3 cp3 = liveTimelineKeyCameraLookAtData2.GetBezierPoint(2, this, zero2);
                    Vector3 cp4 = liveTimelineKeyCameraLookAtData2.GetBezierPoint(3, this, zero2);
                    switch (liveTimelineKeyCameraLookAtData2.GetBezierPointCount())
                    {
                        default:
                            lookAtPos = BezierUtil.LerpUnclamped(start, end, t);
                            break;
                        case 1:
                        {
                            BezierUtil.Calc(ref start, ref end, ref cp, t, out lookAtPos);
                            break;
                        }

                        case 2:
                        {
                            BezierUtil.Calc(ref start, ref end, ref cp, ref cp2, t, out lookAtPos);
                            break;
                        }

                        case 3:
                        {
                            BezierUtil.Calc(ref start, ref end, ref cp, ref cp2, ref cp3, t, out lookAtPos);
                            break;
                        }

                        case 4:
                            BezierUtil.Calc(ref start, ref end, ref cp, ref cp2, ref cp3, ref cp4, t, out lookAtPos);
                            break;
                    }
                }
            }
            else
            {
                lookAtPos = getLookAtValueFunc(liveTimelineKeyCameraLookAtData, this, position, config);
            }

            lookAtPos = ApplyCameraLookAtDelay(liveTimelineKeyCameraLookAtData, lookAtPos, camera.cacheTransform, currentFrame, position);
            return true;
        }

        private void AlterUpdate_CameraFov(LiveTimelineWorkSheet sheet, float currentFrame)
        {
            if (sheet.cameraFovKeys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) || !sheet.cameraFovKeys.EnablePlayModeTimeline(_playMode))
            {
                return;
            }

            CacheCamera camera = GetCamera(sheet.targetCameraIndex);
            if (camera == null)
            {
                return;
            }

            LiveTimelineKey curKey = null;
            LiveTimelineKey nextKey = null;
            FindTimelineKey(out curKey, out nextKey, sheet.cameraFovKeys, currentFrame);
            if (curKey == null)
            {
                return;
            }

            LiveTimelineKeyCameraFovData liveTimelineKeyCameraFovData = curKey as LiveTimelineKeyCameraFovData;
            LiveTimelineKeyCameraFovData liveTimelineKeyCameraFovData2 = nextKey as LiveTimelineKeyCameraFovData;
            float num = 80f;
            if (liveTimelineKeyCameraFovData2 != null && liveTimelineKeyCameraFovData2.interpolateType != 0)
            {
                // Original AlterUpdate_CameraFov 0x4ec82a7 calls the integer
                // frame overload (4eebb30), unlike Position/LookAt seconds.
                float t = CalculateInterpolationValue(liveTimelineKeyCameraFovData, liveTimelineKeyCameraFovData2, currentFrame);
                num = LerpWithoutClamp(liveTimelineKeyCameraFovData.fov, liveTimelineKeyCameraFovData2.fov, t);
            }
            else if (liveTimelineKeyCameraFovData.fovType == LiveCameraFovType.Direct)
            {
                num = liveTimelineKeyCameraFovData.fov;
            }

            if (_limitFovForWidth)
            {
                float num2 = (float)camera.camera.pixelWidth / (float)camera.camera.pixelHeight;
                if (num2 > _baseCameraAspectRatio)
                {
                    float num3 = num2 / _baseCameraAspectRatio;
                    num /= num3;
                }
            }

            camera.camera.fieldOfView = num;

        }

        private void AlterUpdate_CameraRoll(LiveTimelineWorkSheet sheet, float currentFrame)
        {
            if (sheet.cameraRollKeys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) || !sheet.cameraRollKeys.EnablePlayModeTimeline(_playMode))
            {
                return;
            }

            CacheCamera camera = GetCamera(sheet.targetCameraIndex);
            if (camera == null)
            {
                return;
            }

            LiveTimelineKey curKey = null;
            LiveTimelineKey nextKey = null;
            FindTimelineKey(out curKey, out nextKey, sheet.cameraRollKeys, currentFrame);
            if (curKey != null)
            {
                LiveTimelineKeyCameraRollData liveTimelineKeyCameraRollData = curKey as LiveTimelineKeyCameraRollData;
                LiveTimelineKeyCameraRollData liveTimelineKeyCameraRollData2 = nextKey as LiveTimelineKeyCameraRollData;
                float num = 80f;
                if (liveTimelineKeyCameraRollData2 != null && liveTimelineKeyCameraRollData2.interpolateType != 0)
                {
                    // Original AlterUpdate_CameraRoll 0x4ec9640 also samples
                    // integer frames; preserve this channel-specific clock.
                    float t = CalculateInterpolationValue(liveTimelineKeyCameraRollData, liveTimelineKeyCameraRollData2, currentFrame);
                    num = LerpWithoutClamp(liveTimelineKeyCameraRollData.degree, liveTimelineKeyCameraRollData2.degree, t);
                }
                else
                {
                    num = liveTimelineKeyCameraRollData.degree;
                }

                camera.cacheTransform.Rotate(0f, 0f, num);

            }
        }

        //public void SetMultiCamera(MultiCameraManager manager, MultiCamera[] multiCamera)
        public void SetMultiCamera(MultiCamera[] multiCamera)
        {
            _multiCamera = multiCamera;
            //_multiCameraManager = manager;
            if (multiCamera != null)
            {
                _multiCameraCache = new CacheCamera[multiCamera.Length];
                _multiCameraDofLookAt = new Vector3[multiCamera.Length];
                _multiCameraPositionValid = new bool[multiCamera.Length];
                for (int i = 0; i < multiCamera.Length; i++)
                {
                    _multiCameraCache[i] = new CacheCamera(multiCamera[i].GetCamera());
                }
            }
        }

        private static Vector3 GetMultiCameraPositionValue(LiveTimelineKeyCameraPositionData keyData, LiveTimelineControl timelineControl, FindTimelineConfig config)
        {
            return (keyData as LiveTimelineKeyMultiCameraPositionData).GetValue(timelineControl);
        }

        public bool CalculateMultiCameraPos(out Vector3 pos, LiveTimelineWorkSheet sheet, LiveTimelineKey curKey, LiveTimelineKey nextKey, float currentFrame, int timelineIndex)
        {
            if (sheet.multiCameraPosKeys.Count <= timelineIndex)
            {
                pos = Vector3.zero;
                return false;
            }

            FindTimelineConfig config = default(FindTimelineConfig);
            config.curKey = curKey;
            config.nextKey = nextKey;
            config.keyType = FindTimelineConfig.KeyType.KeyDirect;
            config.posKeys = sheet.multiCameraPosKeys[timelineIndex].keys;
            config.lookAtKeys = null;
            config.extraCameraIndex = timelineIndex;
            return CalculateCameraPos(out pos, sheet, currentFrame, _multiCameraCache[timelineIndex], ref config, ref fnGetMultiCameraPositionValueFunc);
        }

        private void AlterUpdate_MultiCameraPosition(LiveTimelineWorkSheet sheet, float currentFrame)
        {
            for (int i = 0; i < _multiCamera.Length; ++i)
            {
                var camera = _multiCamera[i];
                _multiCameraPositionValid[i] = false;
                var group = sheet.multiCameraPosKeys != null && i < sheet.multiCameraPosKeys.Count ? sheet.multiCameraPosKeys[i] : null;
                var keys = group != null ? group.keys : null;
                if (keys == null || keys.Count == 0 || keys[0] == null || currentFrame < keys[0].frame || keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) || !keys.EnablePlayModeTimeline(_playMode))
                {
                    camera.DisableCameraTimeline();
                    continue;
                }

                FindTimelineKey(out var curKey, out var nextKey, keys, currentFrame);
                var current = curKey as LiveTimelineKeyMultiCameraPositionData;
                if (current == null || !CalculateMultiCameraPos(out var pos, sheet, curKey, nextKey, currentFrame, i))
                {
                    camera.DisableCameraTimeline();
                    continue;
                }

                camera.Composite.ApplyTimeline(current, nextKey as LiveTimelineKeyMultiCameraPositionData, currentFrame);
                camera.Composite.UpdateLayerOffset(current.charaRelativeBase, liveCharactorLocators);
                _multiCameraCache[i].cacheTransform.position = pos;
                _multiCameraPositionValid[i] = true;
                if (camera.gameObject.activeSelf)
                    _isMultiCameraEnable = true;
            }
        }

        private static Vector3 GetMultiCameraLookAtValue(LiveTimelineKeyCameraLookAtData keyData, LiveTimelineControl timelineControl, Vector3 camPos, FindTimelineConfig config)
        {
            return (keyData as LiveTimelineKeyMultiCameraLookAtData).GetValue(timelineControl, camPos);
        }

        public bool CalculateMultiCameraLookAt(out Vector3 pos, LiveTimelineWorkSheet sheet, LiveTimelineKey curKey, LiveTimelineKey nextKey, float currentFrame, int timelineIndex = 0)
        {
            if (sheet.multiCameraPosKeys.Count <= timelineIndex || sheet.multiCameraLookAtKeys.Count <= timelineIndex)
            {
                pos = Vector3.zero;
                return false;
            }

            FindTimelineConfig config = default(FindTimelineConfig);
            config.curKey = curKey;
            config.nextKey = nextKey;
            config.keyType = FindTimelineConfig.KeyType.KeyDirect;
            config.posKeys = sheet.multiCameraPosKeys[timelineIndex].keys;
            config.lookAtKeys = sheet.multiCameraLookAtKeys[timelineIndex].keys;
            config.extraCameraIndex = timelineIndex;
            return CalculateCameraLookAt(out pos, sheet, currentFrame, _multiCameraCache[timelineIndex], ref config, ref fnGetMultiCameraLookAtValueFunc, ref fnGetMultiCameraPositionValueFunc);
        }

        private void AlterUpdate_MultiCameraLookAt(LiveTimelineWorkSheet sheet, float currentFrame)
        {
            if (sheet.multiCameraLookAtKeys == null)
                return;
            int count = Mathf.Min(sheet.multiCameraLookAtKeys.Count, _multiCamera.Length);
            for (int i = 0; i < count; ++i)
            {
                if (!_multiCameraPositionValid[i])
                    continue;
                var group = sheet.multiCameraLookAtKeys[i];
                var keys = group != null ? group.keys : null;
                if (keys == null || keys.Count == 0 || keys[0] == null || currentFrame < keys[0].frame || keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) || !keys.EnablePlayModeTimeline(_playMode) || sheet.multiCameraPosKeys == null || i >= sheet.multiCameraPosKeys.Count)
                    continue;
                FindTimelineKey(out var curKey, out var nextKey, keys, currentFrame);
                var current = curKey as LiveTimelineKeyMultiCameraLookAtData;
                if (current == null || !CalculateMultiCameraLookAt(out var pos, sheet, curKey, nextKey, currentFrame, i))
                    continue;
                var camera = _multiCamera[i];
                pos = camera.Composite.ApplyLookAt(pos, current.lookAtCharaPos, liveCharactorLocators);
                _multiCameraDofLookAt[i] = pos;
                if (camera.Composite.UpdateMainCamera)
                {
                    var main = Director.instance.MainRenderCamera;
                    if (main == null)
                        continue;
                    main.transform.SetPositionAndRotation(camera.transform.position, camera.transform.rotation);
                    main.fieldOfView = camera.GetCamera().fieldOfView;
                    DofCameraLookAt = pos;
                    DofLookAtCamera = main;
                    HasDofCameraLookAt = true;
                }
            }
        }

        private void AlterUpdate_MultiCamera(LiveTimelineWorkSheet sheet, float currentFrame)
        {
            if (_multiCameraCache == null || _multiCamera == null)
                return;
            var owner = Director.instance.MultiCameraFinalComposite;
            if (owner == null)
                return;
            owner.BeginTimelineFrame();
            LiveMultiCameraTimeline.EvaluateLayers(sheet.multiCameraLayerKeys, _multiCamera, currentFrame, _playMode);
            AlterUpdate_MultiCameraPosition(sheet, currentFrame);
            AlterUpdate_MultiCameraLookAt(sheet, currentFrame);
            owner.EndTimelineFrame();
        }

        private void AlterUpdate_EnvironmentMirror(LiveTimelineWorkSheet sheet, float currentFrame)
        {
            EvaluateNativeEnvironment(sheet, currentFrame);
        }

        private void AlterUpdate_MirrorReflection(LiveTimelineWorkSheet sheet, float currentFrame)
        {
            if (OnUpdateMirrorReflection == null || sheet?.MirrorReflectionDataList == null)
                return;
            foreach (var group in sheet.MirrorReflectionDataList)
            {
                var keys = group?.keys;
                if (keys == null || keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) || !keys.EnablePlayModeTimeline(_playMode))
                    continue;
                FindTimelineKey(out var currentBase, out var nextBase, keys, currentFrame);
                if (!(currentBase is LiveTimelineKeyMirrorReflectionData current) || !current.EnableKey)
                    continue;
                var info = new MirrorReflectionUpdateInfo
                {
                    TimelineNameHash = group.nameHash,
                    BaseCameraType = group.BaseCameraType,
                    BaseCameraIndex = group.BaseCameraIndex,
                    EnableMirror = current.EnableMirror,
                    EnableBgLayer = current.EnableBgLayer,
                    Enable3dLayer = current.Enable3dLayer,
                    IsToonMirror = current.IsToonMirror,
                    MirrorReflectionRate = current.MirrorReflectionRate,
                    TargetChara = current.TargetChara,
                    EnableCharaHead = current.EnableCharaHead,
                    TargetCharaHead = current.TargetCharaHead
                };
                if (nextBase is LiveTimelineKeyMirrorReflectionData next && next.IsInterpolateKey() && next.EnableKey)
                    info.MirrorReflectionRate = FacialToonLerp(current.MirrorReflectionRate, next.MirrorReflectionRate, CalculateInterpolationValueAtTime(current, next, CameraSampleTime(currentFrame)));
                OnUpdateMirrorReflection(in info);
            }
        }

        private void AlterUpdate_GlobalLight(LiveTimelineWorkSheet sheet, float currentFrame)
        {
            GlobalLightUpdateInfo updateInfo = default;
            int count = sheet.globalLightDataLists.Count;
            for (int i = 0; i < count; i++)
            {
                LiveTimelineKeyGlobalLightDataList keys = sheet.globalLightDataLists[i].keys;
                if (keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) || !keys.EnablePlayModeTimeline(_playMode))
                {
                    continue;
                }
                else if (sheet.globalLightDataLists[i].name != "GlobalLight")
                {
                    continue;
                }

                FindTimelineKey(out var curKey, out var nextKey, keys, currentFrame);
                if (curKey != null)
                {
                    LiveTimelineKeyGlobalLightData lightData = curKey as LiveTimelineKeyGlobalLightData;
                    LiveTimelineKeyGlobalLightData lightData2 = nextKey as LiveTimelineKeyGlobalLightData;
                    Quaternion quaternion = Quaternion.identity;
                    if (lightData.cameraFollow)
                    {
                        quaternion = GetCamera(sheet.targetCameraIndex).cacheTransform.rotation;
                    }

                    updateInfo.flags = lightData.flags;
                    if (lightData2 != null && lightData2.interpolateType != 0)
                    {
                        float ratio = CalculateInterpolationValue(lightData, lightData2, currentFrame);
                        Quaternion a = Quaternion.Euler(lightData.lightDir);
                        Quaternion b = Quaternion.Euler(lightData2.lightDir);
                        updateInfo.lightRotation = Quaternion.Lerp(a, b, ratio) * quaternion;
                        updateInfo.globalRimShadowRate = LerpWithoutClamp(lightData.globalRimShadowRate, lightData2.globalRimShadowRate, ratio);
                        updateInfo.rimColor = Color.Lerp(lightData.rimColor, lightData2.rimColor, ratio);
                        updateInfo.rimStep = LerpWithoutClamp(lightData.rimStep, lightData2.rimStep, ratio);
                        updateInfo.rimFeather = LerpWithoutClamp(lightData.rimFeather, lightData2.rimFeather, ratio);
                        updateInfo.rimSpecRate = LerpWithoutClamp(lightData.rimSpecRate, lightData2.rimSpecRate, ratio);
                        updateInfo.flags = lightData2.flags;
                        updateInfo.RimHorizonOffset = LerpWithoutClamp(lightData.RimHorizonOffset, lightData2.RimHorizonOffset, ratio);
                        updateInfo.RimVerticalOffset = LerpWithoutClamp(lightData.RimVerticalOffset, lightData2.RimVerticalOffset, ratio);
                        updateInfo.RimHorizonOffset2 = LerpWithoutClamp(lightData.RimHorizonOffset2, lightData2.RimHorizonOffset2, ratio);
                        updateInfo.RimVerticalOffset2 = LerpWithoutClamp(lightData.RimVerticalOffset2, lightData2.RimVerticalOffset2, ratio);
                        updateInfo.rimColor2 = Color.Lerp(lightData.rimColor2, lightData2.rimColor2, ratio);
                        updateInfo.rimStep2 = LerpWithoutClamp(lightData.rimStep2, lightData2.rimStep2, ratio);
                        updateInfo.rimFeather2 = LerpWithoutClamp(lightData.rimFeather2, lightData2.rimFeather2, ratio);
                        updateInfo.rimSpecRate2 = LerpWithoutClamp(lightData.rimSpecRate2, lightData2.rimSpecRate2, ratio);
                        updateInfo.globalRimShadowRate2 = LerpWithoutClamp(lightData.globalRimShadowRate2, lightData2.globalRimShadowRate2, ratio);
                    }
                    else
                    {
                        updateInfo.lightRotation = Quaternion.Euler(lightData.lightDir) * quaternion;
                        updateInfo.globalRimShadowRate = lightData.globalRimShadowRate;
                        updateInfo.rimColor = lightData.rimColor;
                        updateInfo.rimStep = lightData.rimStep;
                        updateInfo.rimFeather = lightData.rimFeather;
                        updateInfo.rimSpecRate = lightData.rimSpecRate;
                        updateInfo.flags = lightData.flags;
                        updateInfo.RimHorizonOffset = lightData.RimHorizonOffset;
                        updateInfo.RimVerticalOffset = lightData.RimVerticalOffset;
                        updateInfo.RimHorizonOffset2 = lightData.RimHorizonOffset2;
                        updateInfo.RimVerticalOffset2 = lightData.RimVerticalOffset2;
                        updateInfo.rimColor2 = lightData.rimColor2;
                        updateInfo.rimStep2 = lightData.rimStep2;
                        updateInfo.rimFeather2 = lightData.rimFeather2;
                        updateInfo.rimSpecRate2 = lightData.rimSpecRate2;
                        updateInfo.globalRimShadowRate2 = lightData.globalRimShadowRate2;
                    }

                    OnUpdateGlobalLight.Invoke(ref updateInfo);
                }
            }
        }

        private void AlterUpdate_BgColor1(LiveTimelineWorkSheet sheet, float currentFrame)
        {
            int count = sheet.bgColor1List.Count;
            for (int i = 0; i < count; i++)
            {
                LiveTimelineKeyBgColor1DataList keys = sheet.bgColor1List[i].keys;
                if (keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) || !keys.EnablePlayModeTimeline(_playMode))
                {
                    continue;
                }

                FindTimelineKey(out var curKey, out var nextKey, keys, currentFrame);
                if (curKey == null)
                {
                    continue;
                }

                BgColor1UpdateInfo updateInfo = default;
                LiveTimelineKeyBgColor1Data bgColorData = curKey as LiveTimelineKeyBgColor1Data;
                LiveTimelineKeyBgColor1Data bgColorData2 = nextKey as LiveTimelineKeyBgColor1Data;
                updateInfo.TimelineName = sheet.bgColor1List[i].name;
                updateInfo.TimelineNameHash = sheet.bgColor1List[i].nameHash;
                updateInfo.TargetCharaIdArray = sheet.bgColor1List[i].TargetCharaIdArray;
                updateInfo.TargetDressIdArray = sheet.bgColor1List[i].TargetDressIdArray;
                updateInfo.IsSilhouette = bgColorData != null && bgColorData.IsSilhouette;
                updateInfo.IsProjector = bgColorData != null && bgColorData.IsProjector;
                updateInfo.IsSyncBlinkLight = bgColorData != null && bgColorData.IsSyncBlinkLight;
                updateInfo.BlinkLightNameHash = bgColorData != null ? bgColorData.BlinkLightNameHash : 0;
                updateInfo.BlinkLightContainerIndex = bgColorData != null ? bgColorData.BlinkLightContainerIndex : -1;
                updateInfo.BlinkLightBrightnessPower = bgColorData != null ? bgColorData.BlinkLightBrightnessPower : 0f;
                updateInfo.IsAdjustedBlinkLightColor = bgColorData != null && bgColorData.IsAdjustedBlinkLightColor;
                updateInfo.colorPower = bgColorData != null ? bgColorData.power : 0f;
                updateInfo.scale = bgColorData != null ? bgColorData.scale : 1f;
                updateInfo.vertexColorToonPower = bgColorData != null ? bgColorData.vertexColorToonPower : 0f;
                updateInfo.outlineWidthPower = bgColorData != null ? bgColorData.outlineWidthPower : 0f;
                updateInfo.LightBlendMode = bgColorData != null ? bgColorData.LightBlendMode : 0;
                updateInfo.CurrentColorType = bgColorData != null ? bgColorData.ColorType : 0;
                updateInfo.CurrentColor = bgColorData != null ? bgColorData.color : Color.white;
                updateInfo.CurrentFlags = bgColorData != null ? bgColorData.flags : 0;
                updateInfo.IsEnabledFakeLight = bgColorData.IsEnabledFakeLight;
                updateInfo.IsFakeLightOutline = bgColorData.IsFakeLightOutline;
                updateInfo.FakeLightRate = bgColorData.FakeLightRate;
                updateInfo.FakeLightReceiveColor = bgColorData.FakeLightReceiveColor;
                updateInfo.FakeLightBlendMode = bgColorData.FakeLightBlendMode;
                if (bgColorData2 != null && bgColorData2.interpolateType != 0)
                {
                    float t = CalculateInterpolationValue(bgColorData, bgColorData2, currentFrame);
                    updateInfo.InterpolateRatio = t;
                    updateInfo.flags = bgColorData.flags;
                    updateInfo.color = Color.Lerp(bgColorData.color, bgColorData2.color, t);
                    updateInfo.colorPower = LerpWithoutClamp(bgColorData.power, bgColorData2.power, t);
                    updateInfo.toonDarkColor = Color.Lerp(bgColorData.toonDarkColor, bgColorData2.toonDarkColor, t);
                    updateInfo.toonBrightColor = Color.Lerp(bgColorData.toonBrightColor, bgColorData2.toonBrightColor, t);
                    updateInfo.outlineColor = Color.Lerp(bgColorData.outlineColor, bgColorData2.outlineColor, t);
                    updateInfo.outlineColorBlend = bgColorData.outlineColorBlend;
                    updateInfo.Saturation = LerpWithoutClamp(bgColorData.Saturation, bgColorData2.Saturation, t);
                    updateInfo.NextColorType = bgColorData2.ColorType;
                    updateInfo.NextColor = bgColorData2.color;
                    updateInfo.NextFlags = bgColorData2.flags;
                    updateInfo.IsSyncBlinkLightNext = bgColorData2.IsSyncBlinkLight;
                    updateInfo.FakeLightRate = FadeBlinkLerp(bgColorData.FakeLightRate, bgColorData2.FakeLightRate, t);
                    updateInfo.FakeLightReceiveColor = FadeBlinkColorLerp(bgColorData.FakeLightReceiveColor, bgColorData2.FakeLightReceiveColor, t);
                }
                else
                {
                    updateInfo.flags = bgColorData.flags;
                    updateInfo.color = bgColorData.color;
                    updateInfo.toonDarkColor = bgColorData.toonDarkColor;
                    updateInfo.toonBrightColor = bgColorData.toonBrightColor;
                    updateInfo.outlineColor = bgColorData.outlineColor;
                    updateInfo.outlineColorBlend = bgColorData.outlineColorBlend;
                    updateInfo.Saturation = bgColorData.Saturation;
                    updateInfo.NextColorType = updateInfo.CurrentColorType;
                    updateInfo.NextColor = updateInfo.CurrentColor;
                    updateInfo.NextFlags = updateInfo.CurrentFlags;
                }

                OnUpdateBgColor1?.Invoke(ref updateInfo);
            }
        }

        private bool TryGetBlinkLightColorRGB(string blinkLightName, int blinkLightNameHash, int blinkLightContainerIndex, bool isAdjustedBlinkLightColor, out Color color, out float colorPower)
        {
            color = Color.black;
            colorPower = 0f;
            if (blinkLightContainerIndex < 0)
                return false;
            var drivers = FindObjectsOfType<StageBlinkLightDriver>(true);
            if (drivers == null || drivers.Length == 0)
                return false;
            for (int i = 0; i < drivers.Length; i++)
            {
                StageBlinkLightDriver driver = drivers[i];
                if (driver == null)
                    continue;
                Color rawColor;
                float rawColorPower;
                if (!driver.TryGetCurrentBlinkColor(blinkLightName, blinkLightNameHash, blinkLightContainerIndex, out rawColor, out rawColorPower))
                {
                    continue;
                }

                colorPower = rawColorPower;
                color = ApplyOfficialBlinkLightColorRGB(rawColor, rawColorPower, isAdjustedBlinkLightColor);
                return true;
            }

            return false;
        }

        private static Color ApplyOfficialBlinkLightColorRGB(Color rawColor, float colorPower, bool isAdjustedBlinkLightColor)
        {
            float r = rawColor.r;
            float g = rawColor.g;
            float b = rawColor.b;
            // 瀵瑰簲 Gallop.Math.IsFloatEqualLight(colorPower, 1.0f)
            if (!Gallop.Math.IsFloatEqualLight(colorPower, 1.0f))
            {
                float h;
                float s;
                float v;
                Color.RGBToHSV(new Color(r, g, b, rawColor.a), out h, out s, out v);
                v *= colorPower;
                // true = hdr锛屼笉鑳?clamp 鍒?0~1
                Color hsvColor = Color.HSVToRGB(h, s, v, true);
                r = hsvColor.r;
                g = hsvColor.g;
                b = hsvColor.b;
            }

            // 瀵瑰簲浼唬鐮侀噷鐨?a4 鍒嗘敮
            if (isAdjustedBlinkLightColor)
            {
                r += 1.0f;
                g += 1.0f;
                b += 1.0f;
            }

            return new Color(r, g, b, rawColor.a);
        }

        private void AlterUpdate_BgColor2(LiveTimelineWorkSheet sheet, float currentFrame)
        {
            if (OnUpdateBgColor2 == null)
                return;
            if (sheet == null || sheet.bgColor2List == null)
                return;
            int count = sheet.bgColor2List.Count;
            for (int i = 0; i < count; i++)
            {
                var bgData = sheet.bgColor2List[i];
                if (bgData == null || bgData.keys == null)
                    continue;
                var keys = bgData.keys;
                if (keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable))
                    continue;
                if (!keys.EnablePlayModeTimeline(_playMode))
                    continue;
                FindTimelineKey(out var curKey, out var nextKey, keys, currentFrame);
                var cur = curKey as LiveTimelineKeyBgColor2Data;
                var next = nextKey as LiveTimelineKeyBgColor2Data;
                if (cur == null)
                    continue;
                Color curColor1 = cur.color1;
                Color curColor2 = cur.color2;
                if ((cur.IsSyncBlinkLightToColor1 || cur.IsSyncBlinkLightToColor2) && TryGetBlinkLightColorRGB(cur.BlinkLightName, cur.BlinkLightNameHash, cur.BlinkLightContainerIndex, cur.IsAdjustedBlinkLightColor, out var blinkColor, out _))
                {
                    if (cur.IsSyncBlinkLightToColor1)
                    {
                        curColor1.r = blinkColor.r;
                        curColor1.g = blinkColor.g;
                        curColor1.b = blinkColor.b;
                    }

                    if (cur.IsSyncBlinkLightToColor2)
                    {
                        curColor2.r = blinkColor.r;
                        curColor2.g = blinkColor.g;
                        curColor2.b = blinkColor.b;
                    }
                }

                BgColor2UpdateInfo updateInfo = default;
                updateInfo.TimelineName = bgData.name;
                updateInfo.TimelineNameHash = !string.IsNullOrEmpty(bgData.name) ? Animator.StringToHash(bgData.name) : 0;
                updateInfo.color1 = curColor1;
                updateInfo.color2 = curColor2;
                updateInfo.power = cur.power;
                updateInfo.randomTableIndex = cur.RandomTableIndex();
                if (next != null && next.interpolateType != 0)
                {
                    float t = CalculateInterpolationValue(cur, next, currentFrame);
                    Color nextColor1 = next.color1;
                    Color nextColor2 = next.color2;
                    if ((next.IsSyncBlinkLightToColor1 || next.IsSyncBlinkLightToColor2) && TryGetBlinkLightColorRGB(next.BlinkLightName, next.BlinkLightNameHash, next.BlinkLightContainerIndex, next.IsAdjustedBlinkLightColor, out var nextBlinkColor, out _))
                    {
                        if (next.IsSyncBlinkLightToColor1)
                        {
                            nextColor1.r = nextBlinkColor.r;
                            nextColor1.g = nextBlinkColor.g;
                            nextColor1.b = nextBlinkColor.b;
                        }

                        if (next.IsSyncBlinkLightToColor2)
                        {
                            nextColor2.r = nextBlinkColor.r;
                            nextColor2.g = nextBlinkColor.g;
                            nextColor2.b = nextBlinkColor.b;
                        }
                    }

                    updateInfo.color1 = Color.Lerp(curColor1, nextColor1, t);
                    updateInfo.color2 = Color.Lerp(curColor2, nextColor2, t);
                    updateInfo.power = LerpWithoutClamp(cur.power, next.power, t);
                }

                OnUpdateBgColor2?.Invoke(ref updateInfo);
            }
        }

        private void AlterUpdate_MobControl(LiveTimelineWorkSheet sheet, float currentFrame)
        {
            AlterUpdate_MobCyalumeControl(sheet != null ? sheet.MobControlKeys : null, currentFrame, OnUpdateMobControl);
        }

        private void AlterUpdate_CyalumeControl(LiveTimelineWorkSheet sheet, float currentFrame)
        {
            AlterUpdate_MobCyalumeControl(sheet != null ? sheet.CyalumeControlKeys : null, currentFrame, OnUpdateCyalumeControl);
        }

        private void AlterUpdate_MobCyalumeControl<T>(IList<T> dataList, float currentFrame, MobCyalumeUpdateInfoDelegate callback)
            where T : LiveTimelineMobCyalumeControlData
        {
            if (callback == null || dataList == null)
                return;
            int count = dataList.Count;
            for (int i = 0; i < count; i++)
            {
                var controlData = dataList[i];
                if (controlData == null)
                    continue;
                var keys = controlData.GetKeyList();
                if (keys == null)
                    continue;
                if (keys.Count <= 0)
                    continue;
                if (keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable))
                    continue;
                FindTimelineKey(out var curKey, out var nextKey, keys, currentFrame);
                var current = curKey as LiveTimelineKeyMobCyalumeControlData;
                var next = nextKey as LiveTimelineKeyMobCyalumeControlData;
                if (current == null)
                    continue;
                MobCyalumeUpdateInfo info = default;
                info.data = controlData;
                info.unk0 = controlData.GroupIndex;
                info.currentFrame = currentFrame;
                info.currentLiveTime = currentLiveTime;
                if (next != null && next.interpolateType != 0)
                {
                    float t = CalculateInterpolationValue(current, next, currentFrame);
                    info.position = Vector3.LerpUnclamped(current.Position, next.Position, t);
                    info.rotation = Quaternion.Lerp(current.GetRotation(), next.GetRotation(), t);
                    info.scale = Vector3.LerpUnclamped(current.Scale, next.Scale, t);
                }
                else
                {
                    info.position = current.Position;
                    info.rotation = current.GetRotation();
                    info.scale = current.Scale;
                }

                callback.Invoke(ref info);
            }
        }

        private const float kBlinkFps = 60f;
        private void AlterUpdate_BlinkLight(LiveTimelineWorkSheet workSheet, float currentFrame)
        {
            if (OnUpdateBlinkLight == null)
                return;
            if (workSheet == null || workSheet.blinkLightList == null)
                return;
            var list = workSheet.blinkLightList;
            if (list.Count <= 0)
                return;
            for (int i = 0; i < list.Count; i++)
            {
                var blinkData = list[i];
                if (blinkData == null || blinkData.keys == null)
                    continue;
                var keys = blinkData.keys;
                if (keys.Count <= 0)
                    continue;
                if (keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable))
                    continue;
                if (!keys.EnablePlayModeTimeline(_playMode))
                    continue;
                var selection = keys.FindKeyCached(currentFrame, availableFindKeyCache);
                LiveTimelineKey nextKey = selection.key != null ? keys.At(selection.index + 1) : null;
                var curBlink = selection.key as LiveTimelineKeyBlinkLightData;
                if (curBlink == null)
                    continue;
                float localTime = Mathf.Max(0f, (currentFrame - curBlink.frame) * kFrameToSec);
                BlinkLightUpdateInfo info = BuildOfficialBlinkLightUpdateInfo(curBlink, localTime);
                // 4ec7436 reads FindKeyResult.index; 4ec7519 writes it to
                // updateInfo.keyIndex. A key's frame is a different value.
                info.keyIndex = selection.index;
                if (nextKey is LiveTimelineKeyBlinkLightData nextBlink && nextBlink.IsInterpolateKey())
                    InterpolateBlinkLight(ref info, curBlink, nextBlink, CalculateInterpolationValue(curBlink, nextBlink, currentFrame));
                OnUpdateBlinkLight(blinkData, ref info, currentLiveTime);
            }
        }

        private static BlinkLightUpdateInfo BuildOfficialBlinkLightUpdateInfo(LiveTimelineKeyBlinkLightData key, float localTime)
        {
            BlinkLightUpdateInfo info = new BlinkLightUpdateInfo();
            if (key == null)
                return info;
            info.progressTime = Mathf.Max(0f, localTime);
            // This value has no meaning without its containing key list.
            // AlterUpdate_BlinkLight installs the selected ordinal immediately.
            info.keyIndex = 0;
            info.LightBlendMode = ToLightBlendMode(key.LightBlendMode);
            info.color0Array = key.color0Array;
            info.color1Array = key.color1Array;
            info.powerArray = key.powerArray;
            // 瀹樻柟 BlinkLightUpdateInfo 鏄?bool[]
            info.isReverseHueArray = ConvertReverseHueArray(key.isReverseHueArray);
            info.pattern = (BlinkLightPattern)key.pattern;
            info.colorType = (BlinkLightColorType)key.colorType;
            info.powerMin = key.powerMin;
            info.powerMax = key.powerMax;
            info.loopCount = key.loopCount;
            info.waitTime = key.waitTime;
            info.turnOnTime = key.turnOnTime;
            info.turnOffTime = key.turnOffTime;
            info.keepTime = key.keepTime;
            info.intervalTime = key.intervalTime;
            info.UseWashLightBlendMode = ReadUseWashLightBlendModeFromKey(key);
            info.IsEnabledFakeLightOverride = key.IsEnabledFakeLightOverride;
            info.FakeLightRadiusScale = key.FakeLightRadiusScale;
            info.FakeLightIntensityScale = key.FakeLightIntensityScale;
            return info;
        }

        // Native 0x4ec7597..0x4ec7c6a. Discrete fields (including reverse hue,
        // pattern and loop count) stay on the current key; destination gates interpolation.
        private static float FadeBlinkLerp(float a, float b, float t) => MonitorFloat32(MonitorFloat32(MonitorFloat32(b - a) * t) + a);
        private static Color FadeBlinkColorLerp(Color a, Color b, float t) => new Color(FadeBlinkLerp(a.r, b.r, t), FadeBlinkLerp(a.g, b.g, t), FadeBlinkLerp(a.b, b.b, t), FadeBlinkLerp(a.a, b.a, t));
        internal static void InterpolateBlinkLight(ref BlinkLightUpdateInfo info, LiveTimelineKeyBlinkLightData a, LiveTimelineKeyBlinkLightData b, float t)
        {
            info.color0Array = new Color[10];
            info.color1Array = new Color[10];
            info.powerArray = new float[10];
            for (int i = 0; i < 10; i++)
            {
                info.color0Array[i] = FadeBlinkColorLerp(a.color0Array[i], b.color0Array[i], t);
                info.color1Array[i] = FadeBlinkColorLerp(a.color1Array[i], b.color1Array[i], t);
                info.powerArray[i] = FadeBlinkLerp(a.powerArray[i], b.powerArray[i], t);
            }

            info.powerMin = FadeBlinkLerp(a.powerMin, b.powerMin, t);
            info.powerMax = FadeBlinkLerp(a.powerMax, b.powerMax, t);
            info.waitTime = FadeBlinkLerp(a.waitTime, b.waitTime, t);
            info.turnOnTime = FadeBlinkLerp(a.turnOnTime, b.turnOnTime, t);
            info.turnOffTime = FadeBlinkLerp(a.turnOffTime, b.turnOffTime, t);
            info.keepTime = FadeBlinkLerp(a.keepTime, b.keepTime, t);
            info.intervalTime = FadeBlinkLerp(a.intervalTime, b.intervalTime, t);
            info.FakeLightRadiusScale = FadeBlinkLerp(a.FakeLightRadiusScale, b.FakeLightRadiusScale, t);
            info.FakeLightIntensityScale = FadeBlinkLerp(a.FakeLightIntensityScale, b.FakeLightIntensityScale, t);
        }

        private static LiveDefine.LightBlendMode ToLightBlendMode(object raw)
        {
            if (raw == null)
                return LiveDefine.LightBlendMode.Multiply;
            if (raw is LiveDefine.LightBlendMode m)
                return m;
            try
            {
                return (LiveDefine.LightBlendMode)Convert.ToInt32(raw);
            }
            catch
            {
                return LiveDefine.LightBlendMode.Multiply;
            }
        }

        private static bool[] ConvertReverseHueArray(object src)
        {
            if (src == null)
                return null;
            if (src is bool[] boolArray)
                return boolArray;
            if (src is int[] intArray)
            {
                bool[] dst = new bool[intArray.Length];
                for (int i = 0; i < intArray.Length; i++)
                    dst[i] = intArray[i] != 0;
                return dst;
            }

            if (src is float[] floatArray)
            {
                bool[] dst = new bool[floatArray.Length];
                for (int i = 0; i < floatArray.Length; i++)
                    dst[i] = Mathf.Abs(floatArray[i]) > 0.0001f;
                return dst;
            }

            return null;
        }

        private static bool ReadUseWashLightBlendModeFromKey(LiveTimelineKeyBlinkLightData key)
        {
            if (key == null)
                return false;
            var t = key.GetType();
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            string[] names =
            {
                "UseWashLightBlendMode",
                "useWashLightBlendMode",
                "useWashLightBlend",
                "UseWashLightBlend",
                "isUseWashLightBlendMode",
                "IsUseWashLightBlendMode"
            };
            for (int i = 0; i < names.Length; i++)
            {
                var f = t.GetField(names[i], flags);
                if (f != null && f.FieldType == typeof(bool))
                    return (bool)f.GetValue(key);
                var p = t.GetProperty(names[i], flags);
                if (p != null && p.PropertyType == typeof(bool) && p.GetIndexParameters().Length == 0)
                    return (bool)p.GetValue(key, null);
            }

            return false;
        }

        private void AlterUpdate_WashLight(LiveTimelineWorkSheet workSheet, float currentFrame)
        {
            if (OnUpdateWashLight == null)
                return;
            if (workSheet == null || workSheet.washLightList == null)
                return;
            var list = workSheet.washLightList;
            if (list.Count <= 0)
                return;
            for (int i = 0; i < list.Count; i++)
            {
                var washData = list[i];
                if (washData == null || washData.keys == null)
                    continue;
                var keys = washData.keys;
                if (keys.Count <= 0)
                    continue;
                if (keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable))
                    continue;
                if (!keys.EnablePlayModeTimeline(_playMode))
                    continue;
                FindTimelineKey(out LiveTimelineKey curKey, out LiveTimelineKey nextKey, keys, currentFrame);
                var curWash = curKey as LiveTimelineKeyWashLightData;
                var nextWash = nextKey as LiveTimelineKeyWashLightData;
                if (curWash == null)
                    continue;
                float raycastDistance = curWash.RaycastDistance;
                float cameraProjectionSide = curWash.CameraProjectionSide;
                float cameraProjectionColorPower = curWash.CameraProjectionColorPower;
                if (nextWash != null && nextWash.interpolateType != LiveCameraInterpolateType.None)
                {
                    float t = CalculateInterpolationValue(curWash, nextWash, currentFrame);
                    raycastDistance = LerpWithoutClamp(curWash.RaycastDistance, nextWash.RaycastDistance, t);
                    cameraProjectionSide = LerpWithoutClamp(curWash.CameraProjectionSide, nextWash.CameraProjectionSide, t);
                    cameraProjectionColorPower = LerpWithoutClamp(curWash.CameraProjectionColorPower, nextWash.CameraProjectionColorPower, t);
                }

                WashLightUpdateInfo info = default;
                // Native 4ee7f8e reads the group's registered nameHash (+18h).
                info.NameHash = washData.nameHash;
                info.IsEnabledRaycast = curWash.IsEnabledRaycast;
                info.RaycastDistance = raycastDistance;
                info.IsAllSettings = washData._isAllSettings != 0;
                info.CameraProjectionSide = cameraProjectionSide;
                info.CameraProjectionColorPower = cameraProjectionColorPower;
                OnUpdateWashLight(ref info);
            }
        }

        private void AlterUpdate_Laser(LiveTimelineWorkSheet sheet, float currentFrame)
        {
            int frame = Mathf.FloorToInt(currentFrame);
            AlterUpdate_LaserInternal(sheet, frame, currentLiveTime, ref _laserUpdateInfo, _laserRuntimeIndexOffset);
            if (sheet != null && sheet.laserList != null)
                _laserRuntimeIndexOffset += sheet.laserList.Count;
        }

        // Official public entry point/signature.
        public void AlterUpdate_Laser(LiveTimelineWorkSheet sheet, int currentFrame, float currentTime, ref LaserUpdateInfo updateInfo)
        {
            AlterUpdate_LaserInternal(sheet, currentFrame, currentTime, ref updateInfo, 0);
        }

        private void AlterUpdate_LaserInternal(LiveTimelineWorkSheet sheet, int currentFrame, float currentTime, ref LaserUpdateInfo updateInfo, int runtimeIndexOffset)
        {
            LaserUpdateInfoDelegate handler = OnUpdateLaser;
            if (handler == null || sheet == null || sheet.laserList == null)
                return;
            int count = sheet.laserList.Count;
            for (int i = 0; i < count; i++)
            {
                LiveTimelineLaserData laserData = sheet.laserList[i];
                if (laserData == null || laserData.keys == null)
                    continue;
                LiveTimelineKeyLaserDataList keys = laserData.keys;
                if (keys.Count <= 0 || keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) || !keys.EnablePlayModeTimeline(_playMode))
                {
                    continue;
                }

                FindTimelineKey(out LiveTimelineKey currentKeyBase, out LiveTimelineKey nextKeyBase, keys, currentFrame);
                LiveTimelineKeyLaserData currentKey = currentKeyBase as LiveTimelineKeyLaserData;
                if (currentKey == null)
                    continue;
                LiveTimelineKeyLaserData nextKey = nextKeyBase as LiveTimelineKeyLaserData;
                Quaternion objectRotation = Quaternion.Euler(currentKey.objectRotate);
                Quaternion rotation = Quaternion.Euler(currentKey.rotate);
                Vector3 objectPosition = currentKey.objectPosition;
                Vector3 objectScale = currentKey.objectScale;
                float degRootYaw = currentKey.degRootYaw;
                float degLaserPitch = currentKey.degLaserPitch;
                float positionInterval = currentKey.posInterval;
                float blinkPeriod = currentKey.blinkPeriod;
                // Official condition: interpolation belongs to the NEXT key.
                if (nextKey != null && nextKey.IsInterpolateKey())
                {
                    float t = CalculateInterpolationValue(currentKey, nextKey, currentFrame);
                    objectPosition.x = (nextKey.objectPosition.x - currentKey.objectPosition.x) * t + currentKey.objectPosition.x;
                    objectPosition.y = (nextKey.objectPosition.y - currentKey.objectPosition.y) * t + currentKey.objectPosition.y;
                    objectPosition.z = (nextKey.objectPosition.z - currentKey.objectPosition.z) * t + currentKey.objectPosition.z;
                    objectRotation = Quaternion.Lerp(objectRotation, Quaternion.Euler(nextKey.objectRotate), t);
                    objectScale.x = (nextKey.objectScale.x - currentKey.objectScale.x) * t + currentKey.objectScale.x;
                    objectScale.y = (nextKey.objectScale.y - currentKey.objectScale.y) * t + currentKey.objectScale.y;
                    objectScale.z = (nextKey.objectScale.z - currentKey.objectScale.z) * t + currentKey.objectScale.z;
                    rotation = Quaternion.Lerp(rotation, Quaternion.Euler(nextKey.rotate), t);
                    degRootYaw = (nextKey.degRootYaw - currentKey.degRootYaw) * t + currentKey.degRootYaw;
                    degLaserPitch = (nextKey.degLaserPitch - currentKey.degLaserPitch) * t + currentKey.degLaserPitch;
                    positionInterval = (nextKey.posInterval - currentKey.posInterval) * t + currentKey.posInterval;
                    blinkPeriod = (nextKey.blinkPeriod - currentKey.blinkPeriod) * t + currentKey.blinkPeriod;
                }

                updateInfo.timelineIndex = runtimeIndexOffset + i;
                //蹇呴』鏄綋鍓岾ey寮€濮嬪悗鐨勭浉瀵瑰抚,涓嶆槸 Live 鍏ㄥ眬甯?
                updateInfo.ProgressFrame = Mathf.Max(0, currentFrame - currentKey.frame);
                //currentTime - GetTimeFromFrame(currentKey.frame)銆?
                updateInfo.ProgressTime = Mathf.Max(0f, currentTime - currentKey.frame / 60f);
                updateInfo.objectPosition = objectPosition;
                updateInfo.objectRotation = objectRotation;
                updateInfo.objectScale = objectScale;
                updateInfo.isEnabledRender = currentKey.IsEnabledRender;
                updateInfo.formation = currentKey.formation;
                updateInfo.rotation = rotation;
                updateInfo.degRootYaw = degRootYaw;
                updateInfo.degLaserPitch = degLaserPitch;
                updateInfo.posInterval = positionInterval;
                updateInfo.blink = currentKey.blink;
                updateInfo.blinkPeroid = blinkPeriod;
                updateInfo.IsDisabledRootLight = currentKey.IsDisabledRootLight;
                updateInfo.IsEnabledRaycast = currentKey.IsEnabledRaycast;
                updateInfo.RaycastDistance = currentKey.RaycastDistance;
                handler(ref updateInfo);
            }
        }

        private void AlterUpdate_UVScrollLight(LiveTimelineWorkSheet workSheet, float currentFrame)
        {
            if (OnUpdateUVScrollLight == null)
                return;
            if (workSheet == null || workSheet.uvScrollLightList == null)
                return;
            var list = workSheet.uvScrollLightList;
            if (list.Count <= 0)
                return;
            for (int i = 0; i < list.Count; i++)
            {
                var uvData = list[i];
                if (uvData == null || uvData.keys == null)
                    continue;
                var keys = uvData.keys;
                if (keys.Count <= 0)
                    continue;
                if (keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable))
                    continue;
                if (!keys.EnablePlayModeTimeline(_playMode))
                    continue;
                FindTimelineKey(out LiveTimelineKey curKey, out LiveTimelineKey nextKey, keys, currentFrame);
                var curUv = curKey as LiveTimelineKeyUVScrollLightData;
                if (curUv == null)
                    continue;
                float elapsedTime = Mathf.Max(0f, (currentFrame - curUv.frame) * kFrameToSec);
                UVScrollLightUpdateInfo info = UVScrollLightUpdateInfo.Create(uvData, curUv, elapsedTime);
                // Native AlterUpdate_UVScrollLight 0x504e63e..0x504e92b:
                // the destination key gates interpolation. Texture selection
                // and elapsed-time origin remain owned by the current key.
                if (nextKey is LiveTimelineKeyUVScrollLightData nextUv && nextUv.IsInterpolateKey())
                {
                    float t = CalculateInterpolationValue(curUv, nextUv, currentFrame);
                    info.mulColor0 = FadeBlinkColorLerp(curUv.mulColor0, nextUv.mulColor0, t);
                    info.mulColor1 = FadeBlinkColorLerp(curUv.mulColor1, nextUv.mulColor1, t);
                    info.colorPower = FadeBlinkLerp(curUv.colorPower, nextUv.colorPower, t);
                    info.scrollOffsetX = FadeBlinkLerp(curUv.scrollOffsetX, nextUv.scrollOffsetX, t);
                    info.scrollOffsetY = FadeBlinkLerp(curUv.scrollOffsetY, nextUv.scrollOffsetY, t);
                    info.scrollSpeedX = FadeBlinkLerp(curUv.scrollSpeedX, nextUv.scrollSpeedX, t);
                    info.scrollSpeedY = FadeBlinkLerp(curUv.scrollSpeedY, nextUv.scrollSpeedY, t);
                }

                OnUpdateUVScrollLight(ref info);
            }
        }

        private void AlterUpdate_TransformControl(LiveTimelineWorkSheet sheet, float currentFrame)
        {
            TransformUpdateInfoDelegate handler = OnUpdateTransform;
            if (handler == null || sheet == null || sheet.transformList == null)
                return;
            int count = sheet.transformList.Count;
            for (int i = 0; i < count; i++)
            {
                var transformEntry = sheet.transformList[i];
                if (transformEntry == null || transformEntry.keys == null)
                    continue;
                var keys = transformEntry.keys;
                if (keys.Count <= 0 || keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) || !keys.EnablePlayModeTimeline(_playMode))
                {
                    continue;
                }

                FindTimelineKey(out var curKey, out var nextKey, keys, currentFrame);
                LiveTimelineKeyTransformData transformData = curKey as LiveTimelineKeyTransformData;
                LiveTimelineKeyTransformData transformData2 = nextKey as LiveTimelineKeyTransformData;
                if (transformData == null)
                    continue;
                TransformUpdateInfo updateInfo = default;
                updateInfo.data = transformEntry;
                if (transformData2 != null && transformData2.interpolateType != 0)
                {
                    float t = CalculateInterpolationValue(transformData, transformData2, currentFrame);
                    updateInfo.updateData.position = Vector3.Lerp(transformData.position, transformData2.position, t);
                    updateInfo.updateData.rotation = Quaternion.Lerp(Quaternion.Euler(transformData.rotate), Quaternion.Euler(transformData2.rotate), t);
                    updateInfo.updateData.scale = Vector3.Lerp(transformData.scale, transformData2.scale, t);
                }
                else
                {
                    updateInfo.updateData.position = transformData.position;
                    updateInfo.updateData.rotation = Quaternion.Euler(transformData.rotate);
                    updateInfo.updateData.scale = transformData.scale;
                }

                handler(ref updateInfo);
            }
        }

        private void AlterUpdate_ObjectControl(LiveTimelineWorkSheet sheet, float currentFrame, bool attached = false)
        {
            ObjectUpdateInfoDelegate handler = OnUpdateObject;
            if (handler == null || sheet == null || sheet.objectList == null)
                return;
            int count = sheet.objectList.Count;
            for (int i = 0; i < count; i++)
            {
                var objectEntry = sheet.objectList[i];
                if (objectEntry == null || objectEntry.keys == null || string.IsNullOrEmpty(objectEntry.name))
                    continue;
                var keys = objectEntry.keys;
                if (keys.Count <= 0 || keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) || !keys.EnablePlayModeTimeline(_playMode))
                {
                    continue;
                }

                FindTimelineKey(out var curKey, out var nextKey, keys, currentFrame);
                LiveTimelineKeyObjectData objectData = curKey as LiveTimelineKeyObjectData;
                LiveTimelineKeyObjectData objectData2 = nextKey as LiveTimelineKeyObjectData;
                if (objectData == null)
                    continue;
                // Native early/late passes are complementary, not a permanent
                // rejection of attached objects (0x50465f0 / 0x502abf0).
                if (((int)objectData.AttachTarget != 0) != attached)
                    continue;
                ObjectUpdateInfo updateInfo = default;
                if (objectData2 != null && objectData2.interpolateType != 0)
                {
                    // Original Object selects keys by frame but interpolates in seconds.
                    float t = CalculateInterpolationValueAtTime(objectData, objectData2, CameraSampleTime(currentFrame));
                    updateInfo.updateData.position = InterpolateCameraLayer(objectData.position, objectData2.position, t);
                    updateInfo.updateData.rotation = Quaternion.Lerp(Quaternion.Euler(objectData.rotate), Quaternion.Euler(objectData2.rotate), t);
                    updateInfo.updateData.scale = InterpolateCameraLayer(objectData.scale, objectData2.scale, t);
                }
                else
                {
                    updateInfo.updateData.position = objectData.position;
                    updateInfo.updateData.rotation = Quaternion.Euler(objectData.rotate);
                    updateInfo.updateData.scale = objectData.scale;
                }

                updateInfo.data = objectEntry;
                updateInfo.renderEnable = objectData.renderEnable;
                updateInfo.AttachTarget = objectData.AttachTarget;
                updateInfo.CharacterPosition = objectData.CharacterPosition;
                updateInfo.MultiCameraIndex = objectData.MultiCameraIndex;
                updateInfo.OffsetType = objectData.OffsetValueType;
                updateInfo.LayerType = objectData.LayerTypeValue;
                updateInfo.IsLayerTypeRecursively = objectData.IsLayerTypeRecursively;
                updateInfo.IsAttachProps = objectData.IsAttachProps;
                updateInfo.PropsIndex = objectData.PropsIndex;
                updateInfo.UseScreenCapture = objectData.UseScreenCapture;
                updateInfo.ScreenCaptureId = objectData.ScreenCaptureId;
                updateInfo.UseCaptureAspectType = objectData.UseCaptureAspectType;
                updateInfo.Use2dBackground = objectData.Use2dBackground;
                updateInfo.BgId = objectData.BgId;
                updateInfo.BgSubId = objectData.BgSubId;
                handler(ref updateInfo);
            }
        }

        private void AlterUpdate_HdrBloom(LiveTimelineWorkSheet sheet, int currentFrame)
        {
            if (OnUpdateHdrBloom == null)
                return;
            if (sheet == null || sheet.hdrBloomList == null)
                return;
            int count = sheet.hdrBloomList.Count;
            for (int i = 0; i < count; i++)
            {
                LiveTimelineHdrBloomData timelineData = sheet.hdrBloomList[i];
                if (timelineData == null || timelineData.keys == null)
                {
                    continue;
                }

                LiveTimelineKeyHdrBloomDataList keys = timelineData.keys;
                if (keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable))
                {
                    continue;
                }

                FindTimelineKey(out LiveTimelineKey currentBaseKey, out LiveTimelineKey nextBaseKey, keys, currentFrame);
                LiveTimelineKeyHdrBloomData currentKey = currentBaseKey as LiveTimelineKeyHdrBloomData;
                LiveTimelineKeyHdrBloomData nextKey = nextBaseKey as LiveTimelineKeyHdrBloomData;
                if (currentKey == null)
                    continue;
                HdrBloomUpdateInfo updateInfo = default;
                updateInfo.data = timelineData;
                if (nextKey != null && nextKey.IsInterpolateKey())
                {
                    float t = CalculateInterpolationValue(currentKey, nextKey, currentFrame);
                    updateInfo.intensity = Mathf.Lerp(currentKey.intensity, nextKey.intensity, t);
                    updateInfo.blurSpread = Mathf.Lerp(currentKey.blurSpread, nextKey.blurSpread, t);
                }
                else
                {
                    updateInfo.intensity = currentKey.intensity;
                    updateInfo.blurSpread = currentKey.blurSpread;
                }

                updateInfo.enable = currentKey.enable;
                OnUpdateHdrBloom(ref updateInfo);
            }
        }

        private void SetupBloomDiffusionUpdateInfo(ref PostEffectUpdateInfo_BloomDiffusion updateInfo, LiveTimelineKeyPostEffectBloomDiffusionData currentKey, LiveTimelineKeyPostEffectBloomDiffusionData nextKey, int currentFrame)
        {
            if (currentKey == null)
                return;
            updateInfo.IsEnabledBloom = currentKey.IsEnabledBloom;
            updateInfo.IsEnabledDiffusion = currentKey.IsEnabledDiffusion;
            updateInfo.diffusionBlurSize = currentKey.diffusionBlurSize;
            updateInfo.diffusionBright = currentKey.diffusionBright;
            updateInfo.diffusionThreshold = currentKey.diffusionThreshold;
            updateInfo.diffusionSaturation = currentKey.diffusionSaturation;
            updateInfo.diffusionContrast = currentKey.diffusionContrast;
            if (nextKey != null && nextKey.IsInterpolateKey())
            {
                float t = CalculateInterpolationValue(currentKey, nextKey, currentFrame);
                updateInfo.bloomDofWeight = Mathf.Lerp(currentKey.bloomDofWeight, nextKey.bloomDofWeight, t);
                updateInfo.threshold = Mathf.Lerp(currentKey.threshold, nextKey.threshold, t);
                updateInfo.intensity = Mathf.Lerp(currentKey.intensity, nextKey.intensity, t);
                updateInfo.BloomBlurSize = Mathf.Lerp(currentKey.BloomBlurSize, nextKey.BloomBlurSize, t);
                updateInfo.BloomBlendMode = currentKey.BloomBlendMode;
                if (currentKey.IsEnabledDiffusion && nextKey.IsEnabledDiffusion)
                {
                    updateInfo.diffusionBlurSize = Mathf.Lerp(currentKey.diffusionBlurSize, nextKey.diffusionBlurSize, t);
                    updateInfo.diffusionBright = Mathf.Lerp(currentKey.diffusionBright, nextKey.diffusionBright, t);
                    updateInfo.diffusionThreshold = Mathf.Lerp(currentKey.diffusionThreshold, nextKey.diffusionThreshold, t);
                    updateInfo.diffusionSaturation = Mathf.Lerp(currentKey.diffusionSaturation, nextKey.diffusionSaturation, t);
                    updateInfo.diffusionContrast = Mathf.Lerp(currentKey.diffusionContrast, nextKey.diffusionContrast, t);
                }
            }
            else
            {
                updateInfo.bloomDofWeight = currentKey.bloomDofWeight;
                updateInfo.threshold = currentKey.threshold;
                updateInfo.intensity = currentKey.intensity;
                updateInfo.BloomBlurSize = currentKey.BloomBlurSize;
                updateInfo.BloomBlendMode = currentKey.BloomBlendMode;
            }
        }

        private bool ResolveFogBlinkColor(int nameHash, int index, out Color raw)
        {
            BindPostFilmBlinkDriver();
            return ResolvePostFilmBlinkColor(null, nameHash, index, out raw);
        }

        private void AlterUpdate_TransmittedLight(LiveTimelineWorkSheet sheet, int frame)
        {
            if (sheet == null)
                throw new NullReferenceException();
            if (LiveTransmittedLightTimeline.TryEvaluate(sheet.TransmittedLightKeys, frame, _playMode, out var info))
                OnUpdateTransmittedLight.Invoke(info);
        }

        private void AlterUpdate_MultiCameraTransmittedLight(LiveTimelineWorkSheet sheet, int frame)
        {
            if (!_isMultiCameraEnable || OnUpdateTransmittedLightMultiCamera == null)
                return;
            if (sheet == null || sheet.MultiCameraTransmittedLightDataList == null)
                throw new NullReferenceException();
            foreach (var group in sheet.MultiCameraTransmittedLightDataList)
            {
                if (group == null)
                    throw new NullReferenceException();
                if (LiveTransmittedLightTimeline.TryEvaluate(group.keys, frame, _playMode, out var info))
                    OnUpdateTransmittedLightMultiCamera.Invoke(info, group.MultiCameraNo);
            }
        }

        private void AlterUpdate_GlobalFogControl(LiveTimelineWorkSheet sheet, float currentFrame)
        {
            if (sheet == null || sheet.globalFogDataLists == null || OnUpdateGlobalFog == null)
                return;
            if (_fogBlinkResolver == null)
                _fogBlinkResolver = ResolveFogBlinkColor;
            foreach (var group in sheet.globalFogDataLists)
                if (group != null && LiveGlobalFogTimeline.TryEvaluate(group.keys, (int)currentFrame, _playMode, out var info, _fogBlinkResolver))
                    OnUpdateGlobalFog.Invoke(info);
        }

        private Vector3? ResolveDofWorldFocus(LiveTimelineKeyPostEffectDOFData key)
        {
            if (((int)key.attribute & (1 << 16)) != 0)
                return HasDofCameraLookAt ? DofCameraLookAt : (Vector3? )null;
            bool valid = key.charactor == 0;
            for (int i = 0; !valid && i < liveCharactorLocators.Length && i < liveCharaPositionMax; ++i)
                valid = (key.charactor & (1 << i)) != 0 && liveCharactorLocators[i] != null;
            return valid ? GetPositionWithCharacters(key.FocusCharacters, LiveCameraCharaParts.Face, Vector3.zero, Vector3.zero) : (Vector3? )null;
        }

        private Vector3? SetupDofWorldFocus(LiveTimelineKeyPostEffectDOFData current, LiveTimelineKeyPostEffectDOFData next, float progress)
        {
            var start = ResolveDofWorldFocus(current);
            if (ReferenceEquals(current, next))
                return start;
            var end = ResolveDofWorldFocus(next);
            return start.HasValue && end.HasValue ? Vector3.LerpUnclamped(start.Value, end.Value, progress) : (Vector3? )null;
        }

        private void AlterUpdate_PostEffect_Dof(LiveTimelineWorkSheet sheet, int frame)
        {
            DofTrackCamera = GetCamera(sheet.targetCameraIndex)?.camera;
            var keys = sheet.postEffectDOFKeys;
            if (keys == null || keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) || !keys.EnablePlayModeTimeline(_playMode))
            {
                OnUpdatePostEffect_Dof?.Invoke(null, null);
                return;
            }

            FindTimelineKey(out var currentBase, out var nextBase, keys, frame);
            var current = currentBase as LiveTimelineKeyPostEffectDOFData;
            var next = nextBase as LiveTimelineKeyPostEffectDOFData;
            if (current == null)
            {
                OnUpdatePostEffect_Dof?.Invoke(null, null);
                return;
            }

            float t = next != null && next.IsInterpolateKey() ? CalculateInterpolationValue(current, next, frame) : 0f;
            if (next == null || !next.IsInterpolateKey())
                next = current;
            var sample = _dofSample;
            sample.frame = current.frame;
            sample.attribute = current.attribute;
            sample.charactor = current.charactor;
            sample.dofBlurType = current.dofBlurType;
            sample.dofQuality = current.dofQuality;
            sample.forcalSize = LerpWithoutClamp(current.forcalSize, next.forcalSize, t);
            sample.blurSpread = LerpWithoutClamp(current.blurSpread, next.blurSpread, t);
            sample.dofForegroundSize = LerpWithoutClamp(current.dofForegroundSize, next.dofForegroundSize, t);
            sample.dofFocalPoint = LerpWithoutClamp(current.dofFocalPoint, next.dofFocalPoint, t);
            sample.dofSmoothness = LerpWithoutClamp(current.dofSmoothness, next.dofSmoothness, t);
            sample.BallBlurPowerFactor = LerpWithoutClamp(current.BallBlurPowerFactor, next.BallBlurPowerFactor, t);
            sample.BallBlurBrightnessThreshhold = LerpWithoutClamp(current.BallBlurBrightnessThreshhold, next.BallBlurBrightnessThreshhold, t);
            sample.BallBlurBrightnessIntensity = LerpWithoutClamp(current.BallBlurBrightnessIntensity, next.BallBlurBrightnessIntensity, t);
            sample.BallBlurSpread = LerpWithoutClamp(current.BallBlurSpread, next.BallBlurSpread, t);
            OnUpdatePostEffect_Dof?.Invoke(sample, SetupDofWorldFocus(current, next, t));
        }

        private void AlterUpdate_PostEffect_BloomDiffusion(LiveTimelineWorkSheet sheet, int currentFrame)
        {
            if (sheet == null)
                return;
            LiveTimelineKeyPostEffectBloomDiffusionDataList keys = sheet.postEffectBloomDiffusionKeys;
            if (keys == null)
                return;
            if (keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable))
            {
                return;
            }

            if (!keys.EnablePlayModeTimeline(_playMode))
                return;
            if (OnUpdatePostEffect_BloomDiffusion == null)
                return;
            FindTimelineKey(out LiveTimelineKey currentBaseKey, out LiveTimelineKey nextBaseKey, keys, currentFrame);
            LiveTimelineKeyPostEffectBloomDiffusionData currentKey = currentBaseKey as LiveTimelineKeyPostEffectBloomDiffusionData;
            LiveTimelineKeyPostEffectBloomDiffusionData nextKey = nextBaseKey as LiveTimelineKeyPostEffectBloomDiffusionData;
            if (currentKey == null)
                return;
            PostEffectUpdateInfo_BloomDiffusion updateInfo = default;
            SetupBloomDiffusionUpdateInfo(ref updateInfo, currentKey, nextKey, currentFrame);
            OnUpdatePostEffect_BloomDiffusion(updateInfo);
        }

        private static float LerpWithoutClamp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        private static Vector2 LerpWithoutClamp(Vector2 a, Vector2 b, float t)
        {
            return new Vector2(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t);
        }

        private static Vector3 LerpWithoutClamp(Vector3 a, Vector3 b, float t)
        {
            return new Vector3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);
        }

        private static Vector4 LerpWithoutClamp(Vector4 a, Vector4 b, float t)
        {
            return new Vector4(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t, a.w + (b.w - a.w) * t);
        }

        private static Color LerpWithoutClamp(Color a, Color b, float t)
        {
            return new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, a.a + (b.a - a.a) * t);
        }
    }
}
