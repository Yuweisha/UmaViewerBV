using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gallop;
using Gallop.ImageEffect;
using Gallop.Live;
using Gallop.Live.Cutt;
using Gallop.RenderPipeline;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Experimental.Animations;
using UnityEngine.Playables;
using Unity.Collections;
using UnityEngine.Rendering.Universal;
using static Gallop.CutIn.Cutt.CutInTimelineWorkSheet;
using static GachaCutInLoader;
using Object = UnityEngine.Object;
using CutEvent = Gallop.CutIn.Cutt.CutInTimelineWorkSheet.Event;

internal sealed partial class GachaCutInPlayer : IDisposable
{
    internal const int PlaybackFrameRate = 60;
    internal const float PlaybackStep = 1f / PlaybackFrameRate;
    private static readonly int MiniStencilMaskId = Shader.PropertyToID("_StencilMask");
    private const int MiniStencilBase = 100;
    private const float FramesPerSecond = 30f;
    private const float GateExposureReleaseFrames = 8f;
    private const int CameraAttachmentIndex = 1000;
    private const int EffectAttachmentIndex = 4000;
    private const int OrthoBackgroundLayer = 29;
    private const float DefaultBackgroundOrthoSize = 0.15f;
    private static readonly int EffectColorId = Shader.PropertyToID("_Color");
    private static readonly int EffectCharaColorId = Shader.PropertyToID("_CharaColor");
    private static readonly int EffectEmissiveColorId = Shader.PropertyToID("_EmissiveColor");
    private static readonly int BackgroundMulColorId = Shader.PropertyToID("_MulColor0");
    private static readonly int BackgroundAmbientColorId = Shader.PropertyToID("_AmbientColor");
    private static readonly int BackgroundAddColorId = Shader.PropertyToID("_AddColor");
    private static readonly int BackgroundMaskingFadeId = Shader.PropertyToID("_FadeRate");
    private static readonly int BackgroundTimeId = Shader.PropertyToID("_AppTime");
    private static readonly int GrassWindParamId = Shader.PropertyToID("_WindParam");
    private static readonly int GrassWindScaleId = Shader.PropertyToID("_WindScale");
    private static readonly int GrassWindTextureId = Shader.PropertyToID("_WindDirTex");
    private static readonly int DirtRateId = Shader.PropertyToID("_DirtRate");
    private static readonly int CharacterOutlineColorId = Shader.PropertyToID("_OutlineColor");
    private static readonly int CharacterVertexToonPowerId = Shader.PropertyToID("_VertexColorToonPower");
    private static readonly int CharacterEmissiveColorId = Shader.PropertyToID("_EmissiveColor");
    private static readonly int CharacterEmissiveIntensityId = Shader.PropertyToID("_EmissiveIntensity");
    private static readonly int CharacterEmissiveRimPowerId = Shader.PropertyToID("_EmissiveRimPower");
    private static readonly int CharacterEmissiveRimIntensityId = Shader.PropertyToID("_EmissiveRimIntensity");
    private static readonly int CharacterEmissiveRimCenterId = Shader.PropertyToID("_IsEmissiveRimCenter");
    private static readonly Color DefaultCharacterOutlineEditColor = new Color32(32, 12, 0, 155);
    private const string CourseSkyEffect = "pfb_eff_gacha_029";
    private static readonly string[] GateEffectCues = { "snd_sfx_gacha_017", "snd_sfx_gacha_015", "snd_sfx_gacha_016" };
    private static readonly string[] GateMonitors =
    {
        "monitor_1_a_long_shot_star",
        "monitor_2_the_favorite_star",
        "monitor_3_eclipse_first_the_rest_nowhere"
    };

    private readonly GachaCutInLoader loader;
    private readonly bool timingOnly;
    private readonly List<VisualTrack<BackgroundKey>> backgrounds = new List<VisualTrack<BackgroundKey>>();
    private readonly List<Material> ownedBackgroundTimeMaterials = new List<Material>();
    private readonly Dictionary<string, Transform> backgroundAnchors = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
    private readonly List<VisualTrack<EffectKey>> effects = new List<VisualTrack<EffectKey>>();
    private readonly Dictionary<string, VisualTrack<EffectKey>> effectAnchors =
        new Dictionary<string, VisualTrack<EffectKey>>(StringComparer.OrdinalIgnoreCase);
    private readonly List<Gate> gates = new List<Gate>();
    private readonly Dictionary<int, ActorPlayback> cast = new Dictionary<int, ActorPlayback>();
    private readonly Dictionary<UmaContainerCharacter, ActorPlayback> actorPlayback = new Dictionary<UmaContainerCharacter, ActorPlayback>();
    private readonly Transform root;
    private readonly GameObject cameraMotion;
    private readonly Camera camera;
    private Camera orthoBackgroundCamera;
    private readonly CameraClearFlags defaultClearFlags;
    private readonly bool defaultAllowMsaa;
    private readonly Color defaultBackgroundColor;
    private readonly float defaultOrthoSize;
    private readonly float defaultNearClipPlane;
    private readonly float defaultFarClipPlane;
    private readonly GachaEnvironment environment;
    private GachaMaskCamera maskCamera;
    private GachaMonitorCamera monitorCamera;
    private GachaCutInProps props;
    private GachaSceneCards sceneCards;
    private GachaCutInAudience audience;
    private readonly PostImageEffectFeature.Parameter post;
    private readonly Animator cameraAnimator;
    private readonly AnimatorOverrideController cameraController;
    private readonly GachaPreviewAudio audio;
    private readonly IReadOnlyList<int> cards;
    private readonly int openingId;
    private readonly LiveCharacterLoadData openingActor;
    private readonly IReadOnlyList<GachaCutInLoader.Selection> selections;
    private readonly GachaSupportStage.Options supportOptions;
    private GachaSupportStage support;
    private Vector3[] lineupPositions;
    private GachaPreviewFlash flash;
    private int flashResultIndex = -1;
    private readonly int monitor = UnityEngine.Random.Range(0, GateMonitors.Length);
    private Transform cutRoot;
    private EffectKey previousCommentKey;
    private GameObject course;
    private readonly MaterialPropertyBlock effectProperties = new MaterialPropertyBlock();
    private readonly MaterialPropertyBlock characterProperties = new MaterialPropertyBlock();
    private CameraMotion previousCamera;
    private int chapter = -1;
    private int jumpFrame = -1;
    private int lastFrame = -1;
    private bool waitForTap;
    private bool waitForVoice;
    private bool waitForCommentExit;
    private float time;
    private string title;
    private ShakeEvent shake;
    private int shakeFrame;
    private GateZoomEvent gateZoom;
    private int gateZoomFrame;
    private GatePanEvent gatePan;
    private AnimationCurve gatePanCurve;
    private int gatePanFrame;
    private float gatePanPosition;
    private bool disposed;
    private bool assetPreparationCompleted;

    private Cut Current => loader.Cuts[chapter];
    internal Camera CaptureCamera => camera;
    internal Transform ObservationView { get; set; }
    internal float ObservationFieldOfView { get; set; }
    internal int CaptureChapter => chapter;
    internal int CaptureTimelineFrame => lastFrame;
    internal GachaCutInLoader CaptureLoader => loader;
    public bool Finished => chapter == loader.Cuts.Count;
    public bool CanAdvance => CanAdvanceWithSkip(false);
    private bool CanAdvanceWithSkip(bool tapSkip) => chapter >= 0 && !Finished && !waitForCommentExit &&
        (waitForTap || (!waitForVoice && (tapSkip || supportOptions != null || Config.Instance?.GachaTapSkip == true) &&
            (jumpFrame > lastFrame || Current.AlbumUpgradeCanSkip)));
    internal bool AtFinalName => chapter >= 0 && !Finished && supportOptions == null &&
        (Current.Unique || selections != null) && Current.ResultIndex == cards.Count - 1 && waitForTap && HasNameFlash;
    internal void SetMuted(bool muted) => audio.SetMuted(muted);
    internal void SetAudioPaused(bool paused) => audio.SetPaused(paused);
    internal void SynchronizeAudio() => audio.Synchronize();
    public bool WaitingForTap => waitForTap;
    public string Title => title;
    public bool IsVoicePlaying => audio.IsVoicePlaying;
    public bool HasCommentFlash => flash?.CommentVisible == true;
    public bool HasNameFlash => flash?.NameVisible == true;
    public bool HasLocalizedCommentFlash => flash?.LocalizedCommentVisible == true;
    public bool HasLocalizedNameFlash => flash?.LocalizedNameVisible == true;
    public Draw ActiveDraw => supportOptions == null && !Finished && Current.ResultIndex >= 0
        ? loader.Draws[Current.ResultIndex] : null;
    private Color timelineFade;
    public Color Fade
    {
        get
        {
            var front = flash?.CommentFade ?? Color.clear;
            if (front.a <= 0f)
                return timelineFade;
            float behind = timelineFade.a * (1f - front.a);
            float alpha = front.a + behind;
            var result = (front * front.a + timelineFade * behind) / alpha;
            result.a = alpha;
            return result;
        }
    }

    public GachaCutInPlayer(Camera template, RenderTexture target, IReadOnlyList<int> cards, int openingId,
        GachaSupportStage.Options supportOptions = null, ReplayResources replayResources = null,
        GachaCutInLoader timingLoader = null, IReadOnlyList<GachaCutInLoader.Selection> selections = null,
        LiveCharacterLoadData openingActor = null)
    {
        timingOnly = timingLoader != null;
        loader = timingLoader ?? new GachaCutInLoader();
        this.cards = cards;
        this.openingId = openingId;
        this.openingActor = openingActor;
        this.selections = selections;
        this.supportOptions = supportOptions;
        root = new GameObject("Gacha playback").transform;
        cameraMotion = new GameObject("Camera motion");
        cameraMotion.transform.SetParent(root, false);
        cameraController = Object.Instantiate(UmaViewerBuilder.Instance.CameraOverrideController);
        cameraAnimator = cameraMotion.AddComponent<Animator>();
        cameraAnimator.runtimeAnimatorController = cameraController;
        cameraAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        cameraAnimator.speed = 0f;
        camera = new GameObject("Gacha camera").AddComponent<Camera>();
        camera.transform.SetParent(root, false);
        defaultNearClipPlane = camera.nearClipPlane;
        defaultFarClipPlane = camera.farClipPlane;
        camera.CopyFrom(template);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.rect = new Rect(0f, 0f, 1f, 1f);
        defaultClearFlags = camera.clearFlags;
        defaultAllowMsaa = camera.allowMSAA;
        defaultBackgroundColor = camera.backgroundColor;
        defaultOrthoSize = camera.orthographicSize;
        camera.targetTexture = target;
        camera.aspect = (float)target.width / target.height;
        camera.cullingMask = 1 << PreviewLayer;
        camera.enabled = false;
        environment = new GachaEnvironment(camera);
        var imageEffect = camera.gameObject.AddComponent<Gallop.GallopImageEffect>();
        camera.GetComponent<Gallop.RenderPipeline.CameraData>().IsCutInRendering = true;
        post = imageEffect.RenderParameter;
        audio = new GachaPreviewAudio(root, replayResources?.Clips);
    }

    // Renders the current playback state into a caller-owned surface. Playback
    // time and preview UI are controlled by the caller.
    public bool RenderFrame(RenderTexture target)
    {
        if (disposed || chapter < 0 || Finished)
            return false;
        if (!target)
            throw new ArgumentNullException(nameof(target));
        float aspect = camera.aspect;
        float renderAspect = (float)target.width / target.height;
        bool observing = ObservationView;
        if (!observing && !Mathf.Approximately(aspect, renderAspect))
            throw new ArgumentException("Capture and preview must use the same aspect ratio.", nameof(target));
        var previous = camera.targetTexture;
        var activeTarget = RenderTexture.active;
        var originalPosition = camera.transform.position;
        var originalRotation = camera.transform.rotation;
        float originalFov = camera.fieldOfView;
        bool originalOrtho = camera.orthographic;
        var originalShift = camera.lensShift;
        var overlays = observing ? camera.GetUniversalAdditionalCameraData().cameraStack
            .Where(overlay => overlay).Select(overlay => (Camera: overlay, Aspect: overlay.aspect)).ToArray() : null;
        try
        {
            if (observing)
            {
                camera.transform.SetPositionAndRotation(ObservationView.position, ObservationView.rotation);
                camera.orthographic = false;
                camera.fieldOfView = ObservationFieldOfView;
                camera.lensShift = Vector2.zero;
                camera.aspect = renderAspect;
                foreach (var overlay in overlays)
                    overlay.Camera.aspect = renderAspect;
                SynchronizeObservationCameras();
            }
            // Manual capture can run after pose sampling and before the
            // model's LateUpdate publishes its current head transforms.
            foreach (var binding in cast.Values)
                if (binding.Actor.isActiveAndEnabled)
                    binding.Actor.UpdateFaceLightRuntime();
            monitorCamera?.Render();
            maskCamera?.Render();
            if (orthoBackgroundCamera && orthoBackgroundCamera.enabled)
            {
                orthoBackgroundCamera.targetTexture = target;
                orthoBackgroundCamera.aspect = renderAspect;
                orthoBackgroundCamera.Render();
            }
            camera.targetTexture = target;
            camera.aspect = renderAspect;
            if (supportOptions == null)
                foreach (var background in backgrounds)
                    foreach (var mirror in background.Mirrors)
                        mirror.ForceRenderOnce();
            RenderWater();
            camera.Render();
            return true;
        }
        finally
        {
            if (orthoBackgroundCamera && orthoBackgroundCamera.enabled)
            {
                orthoBackgroundCamera.targetTexture = previous;
                orthoBackgroundCamera.aspect = aspect;
            }
            camera.targetTexture = previous;
            camera.aspect = aspect;
            if (observing)
            {
                camera.transform.SetPositionAndRotation(originalPosition, originalRotation);
                camera.fieldOfView = originalFov;
                camera.orthographic = originalOrtho;
                camera.lensShift = originalShift;
                foreach (var overlay in overlays)
                    if (overlay.Camera)
                        overlay.Camera.aspect = overlay.Aspect;
                SynchronizeObservationCameras();
            }
            RenderTexture.active = activeTarget;
        }
    }

    private void SynchronizeObservationCameras()
    {
        foreach (var overlay in new[] { colorCorrectionExemptCamera, effectOverlayCamera, effectUiCamera })
            if (overlay)
            {
                overlay.orthographic = camera.orthographic;
                overlay.orthographicSize = camera.orthographicSize;
                overlay.fieldOfView = camera.fieldOfView;
                overlay.nearClipPlane = camera.nearClipPlane;
                overlay.farClipPlane = camera.farClipPlane;
                overlay.lensShift = camera.lensShift;
            }
        maskCamera?.Sample(lastFrame);
        foreach (var binding in cast.Values)
            binding.ApplyModelGradient(camera, characterProperties);
    }

    public void RenderWater()
    {
        if (disposed || chapter < 0 || Finished || supportOptions != null)
            return;
        foreach (var background in backgrounds)
            foreach (var water in background.Water)
                water.RenderCutIn(camera, RemapWaterLayers(water.reflectLayers), RemapWaterLayers(water.refractLayers), time);
    }

    private static int RemapWaterLayers(LayerMask layers) =>
        (layers.value & ((1 << 8) | (1 << 12))) != 0 ? 1 << PreviewLayer : 0;

    public IEnumerator PrepareCharacterAssets(Action<int, int, string> progress = null)
    {
        if (!Config.Instance.DownloadMissingResources && Config.Instance.WorkMode != WorkMode.Standalone)
            yield break;

        yield return UmaViewerDownload.DownloadAssets(CollectCharacterAssets(), progress);
        assetPreparationCompleted = true;
    }

    private List<UmaDatabaseEntry> CollectCharacterAssets()
    {
        var main = UmaViewerMain.Instance;
        return GachaCutInLoader.CharacterBundles(cards, openingId, selections).Distinct()
            .SelectMany(entry => UmaAssetManager.SearchAB(main, entry))
            .Concat(audio.CollectCharacterBanks(selections == null ? cards :
                selections.Select(selection => selection.CutInCard > 0 ? selection.CutInCard : selection.Card)))
            .GroupBy(entry => entry.Url, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First()).ToList();
    }

    internal static void VerifyDownloadedAssets(IEnumerable<UmaDatabaseEntry> entries)
    {
        var missing = entries.Where(entry => !File.Exists(entry.Path))
            .Select(entry => $"{entry.Name} ({entry.Url}): {entry.Path}").ToArray();
        if (missing.Length != 0)
            throw new FileNotFoundException("CutIn required resources are unavailable after preparation:\n" + string.Join("\n", missing));
    }

    public void Load()
    {
        if (supportOptions == null && assetPreparationCompleted)
            VerifyDownloadedAssets(CollectCharacterAssets());
        if (supportOptions != null)
            support = loader.LoadSupport(cards, root, camera, supportOptions);
        else
        {
            loader.Load(cards, openingId, root, selections, openingActor);
            lineupPositions = new Vector3[cards.Count];
            course = InstantiateVisual(loader.Prefabs[Course], root);
            InstallGates(course.transform);
        }
        audio.StartMusic();
        NextCut();
        camera.enabled = true;
    }

    internal GachaCutInPlayer CreateTimingProbe(RenderTexture target, ReplayResources resources)
    {
        if (supportOptions != null || cards.Count != 1 || Current.Name != CharacterCommentCut)
            throw new InvalidOperationException("Playback duration starts at a single character comment cut.");
        var probe = new GachaCutInPlayer(camera, target, cards, openingId,
            replayResources: resources, timingLoader: loader);
        probe.chapter = chapter - 1;
        probe.SetMuted(true);
        probe.SetAudioPaused(true);
        probe.NextCut();
        probe.audio.RestorePlayback(audio.CapturePlayback());
        if (flash != null)
            probe.flash?.RestorePlayback(flash.CapturePlayback());
        flash?.EnableSeeking();
        seekable = true;
        PrepareEffectSeeking();
        return probe;
    }

    private bool seekable;

    private void PrepareEffectSeeking()
    {
        foreach (var effect in effects)
        {
            effect.SeekMaterials = Enumerable.Range(0, effect.Renderers.Length)
                .Select(index => GetEffectMaterials(effect, index).Select(material => material ? new Material(material) : null).ToArray()).ToArray();
            effect.SeekColors = effect.Renderers.Select(renderer =>
            {
                var colors = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(colors);
                return colors;
            }).ToArray();
        }
    }

    private void ClearSeekMaterials()
    {
        foreach (var effect in effects)
            if (effect.SeekMaterials != null)
            {
                foreach (var materials in effect.SeekMaterials)
                    foreach (var material in materials)
                        if (material)
                            Object.Destroy(material);
                effect.SeekMaterials = null;
                effect.SeekColors = null;
            }
    }

    internal sealed class PlaybackState
    {
        internal int Chapter, Frame, JumpFrame;
        internal float Time;
        internal bool WaitForTap, WaitForVoice, WaitForCommentExit;
        internal string Title;
        internal EffectKey CommentKey;
        internal GachaPreviewAudio.PlaybackState Audio;
        internal GachaPreviewFlash.PlaybackState Flash;
    }

    internal PlaybackState CapturePlayback()
    {
        return new PlaybackState
        {
            Chapter = chapter, Frame = lastFrame, Time = time, JumpFrame = jumpFrame,
            WaitForTap = waitForTap, WaitForVoice = waitForVoice, WaitForCommentExit = waitForCommentExit,
            Title = title, CommentKey = previousCommentKey,
            Audio = audio.CapturePlayback(), Flash = flash?.CapturePlayback()
        };
    }

    internal void SeekPlayback(PlaybackState state)
    {
        if (supportOptions != null || cards.Count != 1 || timingOnly)
            throw new InvalidOperationException("Interactive seeking requires a single character CutIn.");
        if (chapter != state.Chapter)
        {
            NextCut(state.Chapter);
            flash?.EnableSeeking();
        }
        waitForTap = state.WaitForTap;
        waitForVoice = state.WaitForVoice;
        waitForCommentExit = state.WaitForCommentExit;
        jumpFrame = state.JumpFrame;
        title = state.Title;
        previousCommentKey = state.CommentKey;
        shake = null;
        foreach (var entry in Current.Events.OrderBy(pair => pair.Key))
            if (entry.Key <= state.Frame)
                foreach (var command in entry.Value)
                    if (command.eventId == 1)
                    {
                        shake = Read<ShakeEvent>(command);
                        shakeFrame = entry.Key;
                    }
        foreach (var binding in cast.Values)
        {
            SetSpringChains(binding, null, restoring: true);
            binding.RestoreParts(release: false);
            binding.RestoreWind();
            binding.RestoreColors(release: false);
            binding.RestoreCharacterControls(release: false);
            binding.PreviousCharacter = null;
            binding.PreviousMotion = null;
        }
        foreach (var background in backgrounds)
            background.Previous = null;
        foreach (var effect in effects)
        {
            if (effect.SeekMaterials != null)
                for (int index = 0; index < effect.Renderers.Length; index++)
                {
                    var materials = GetEffectMaterials(effect, index);
                    for (int slot = 0; slot < materials.Length; slot++)
                        if (materials[slot] && effect.SeekMaterials[index][slot])
                            materials[slot].CopyPropertiesFromMaterial(effect.SeekMaterials[index][slot]);
                    effect.Renderers[index].SetPropertyBlock(effect.SeekColors[index]);
                }
            effect.Previous = null;
            effect.NextParticleKey = effect.NextAttachmentKey = 0;
            effect.ParticlePlayKey = effect.EffectAttachmentKey = effect.LastEffectLocatorKey = null;
            effect.EffectAttachment = effect.LastEffectLocator = default;
            effect.ParticlePlaybackTime = 0f;
            effect.ParticlePauseFrame = null;
            effect.ParticlePausedFrames = 0;
            effect.Instance.SetActive(false);
            SimulateEffectParticles(effect, 0f, true);
        }
        time = 0f;
        lastFrame = state.Frame;
        SampleTo(state.Time, true);
        foreach (var binding in cast.Values)
            binding.Actor.ResetDynamicBone();
        if (state.Flash != null)
            flash?.RestorePlayback(state.Flash);
        audio.RestorePlayback(state.Audio);
        SetEffectAnimatorsPaused(waitForTap || waitForVoice || waitForCommentExit);
    }

    public void Tick(float deltaTime)
    {
        if (Finished)
            return;
        audio.Tick(deltaTime);
        flash?.Tick(deltaTime);
        bool resumeEffects = waitForVoice || waitForCommentExit;
        if (waitForTap || (waitForVoice && audio.IsVoicePlaying))
            return;

        if (waitForVoice)
        {
            waitForVoice = false;
            if (flash?.CommentVisible == true)
            {
                flash.EndComment();
                audio.PlayEffect("snd_sfx_gacha_025");
                waitForCommentExit = true;
            }
        }
        if (waitForCommentExit && flash?.CommentVisible == true)
            return;
        waitForCommentExit = false;
        if (resumeEffects)
            SetEffectAnimatorsPaused(false);
        float targetTime = time + deltaTime;
        if (gatePan != null)
        {
            gatePanPosition += deltaTime * FramesPerSecond;
            int end = gatePan.PanFrameArray[cards.Count - 1];
            if (gatePanPosition >= end)
            {
                EndGatePan();
                return;
            }
            // The ten-draw camera finishes later than the lamp shot's timeline marker.
            // Stretch the end of the pan without crossing that marker or replaying its cues.
            int marker = gatePan.SkipForEndFrame;
            if (end > marker)
            {
                float tail = marker - GateExposureReleaseFrames;
                if (gatePanPosition > tail)
                    targetTime = Mathf.Lerp(tail, marker, Mathf.InverseLerp(tail, end, gatePanPosition)) / FramesPerSecond;
            }
        }
        SampleTo(Mathf.Min(targetTime, Current.Data._timeLength), false);
        if (!waitForTap && !waitForVoice && time >= Current.Data._timeLength)
            NextCut();
    }

    public void Advance()
    {
        Advance(false);
    }

    internal void Tap() => Advance(true);

    private void Advance(bool tapSkip)
    {
        if (!CanAdvanceWithSkip(tapSkip))
            return;
        if (waitForTap)
        {
            if (support == null && HasNameFlash)
            {
                NextCut();
                return;
            }
            waitForTap = false;
            SetEffectAnimatorsPaused(false);
            return;
        }
        if (Current.AlbumUpgradeCanSkip)
        {
            NextCut();
            return;
        }

        if (Current.Unique)
            audio.StopCardEffect();
        if (gatePan != null && jumpFrame == gatePan.SkipForEndFrame)
            EndGatePan();
        else
            SampleTo(jumpFrame / FramesPerSecond, true);
    }

    private void EndGatePan()
    {
        int frame = gatePan.SkipForEndFrame;
        gatePan = null;
        gateZoom = null;
        SampleTo(Mathf.Max(time, frame / FramesPerSecond), true);
    }

    private void NextCut(int? targetChapter = null)
    {
        if (timingOnly)
        {
            BeginTimingCut();
            return;
        }
        if (chapter >= 0 && !Finished && Current.Unique)
            audio.StopCardEffect();
        if (chapter >= 0 && !Finished)
            foreach (var (_, mini, _) in Current.MiniCast)
                mini.gameObject.SetActive(false);
        if (supportOptions == null)
            flash?.Clear();
        else
            support?.CommitUpgrade();
        foreach (var gate in gates)
        {
            gate.ClearEffect();
            gate.FinishOpening(loader.OpenedGateClip);
        }
        previousCommentKey = null;
        ClearSeekMaterials();
        ClearEffectMaterials();
        ClearBackgroundTimeMaterials();
        ResetPostEffects();
        post.Aura.AuraParamDic.Clear();
        environment.Clear();
        maskCamera?.Dispose();
        maskCamera = null;
        monitorCamera?.Dispose();
        monitorCamera = null;
        props?.Dispose();
        props = null;
        sceneCards?.Dispose();
        sceneCards = null;
        audience?.Dispose();
        audience = null;
        SetOrthoBackgroundActive(false);
        camera.cullingMask = 1 << PreviewLayer;
        var cameraData = camera.GetComponent<Gallop.RenderPipeline.CameraData>();
        cameraData.UseCameraOpaqueTexture = false;
        cameraData.CameraOpaqueTextureSizeRate = 1f;
        if (cutRoot)
        {
            cutRoot.gameObject.SetActive(false);
            Object.Destroy(cutRoot.gameObject);
        }
        foreach (var binding in cast.Values)
        {
            SetSpringChains(binding, null, restoring: true);
            binding.RestoreParts();
            binding.RestoreWind();
            binding.RestoreColors();
            binding.RestoreCharacterControls();
            binding.Shading?.Dispose();
            binding.Shading = null;
            if (binding.HasDirt)
                SetDirtRate(binding.Renderers, Vector4.zero);
            binding.Actor.ReleaseLiveSweat();
            binding.Shadow?.Dispose();
            binding.Shadow = null;
            binding.SetLayer(PreviewLayer);
            binding.Actor.gameObject.SetActive(false);
        }
        cast.Clear();
        if (course)
            course.SetActive(false);
        chapter = targetChapter ?? chapter + 1;
        if (Finished)
            return;

        var cut = Current;
        if (supportOptions == null)
        {
            camera.nearClipPlane = defaultNearClipPlane;
            camera.farClipPlane = defaultFarClipPlane;
        }
        if (support != null)
        {
            bool closing = cut.Name.StartsWith("gac_spt_result_", StringComparison.Ordinal);
            support.BeginCut(cut.ResultIndex, closing, cut.AlbumUpgradeRarity);
            if (cut.AlbumUpgradeRarity > 0)
            {
                flash?.Dispose();
                flash = null;
                audio.PlayEffect(cut.AlbumUpgradeRarity == 2
                    ? "snd_sfx_gacha_bookmark_gold" : "snd_sfx_gacha_bookmark_rainbow");
            }
            else if (cut.ResultIndex >= 0)
            {
                flash?.Dispose();
                flash = loader.LoadSupportFlash(camera, support.Current, support.PhotoCamera);
                audio.PlayEffect($"snd_sfx_gacha_{32 + support.Current.Rarity:D3}");
            }
            else if (chapter == 0)
            {
                audio.PlayEffect(supportOptions.Sky == GachaSupportStage.SkyType.Normal
                    ? "snd_sfx_gacha_030" : "snd_sfx_gacha_046");
            }
            else if (closing && chapter == loader.Cuts.Count - 1)
            {
                flash?.Clear();
            }
        }
        else if (cut.ResultIndex != flashResultIndex && (cut.Unique || cut.GateAlreadyOpen))
        {
            flash?.Dispose();
            flash = loader.LoadTextEffects(camera, ActiveDraw);
            flashResultIndex = cut.ResultIndex;
        }
        cutRoot = new GameObject(cut.Name).transform;
        cutRoot.SetParent(root, false);
        maskCamera = GachaMaskCamera.Create(camera, cutRoot, cut.Sheet.MaskCameraDataList);
        if (maskCamera != null)
            camera.cullingMask |= 1 << GachaMaskCamera.OverlayLayer;
        backgrounds.Clear();
        backgroundAnchors.Clear();
        effects.Clear();
        effectAnchors.Clear();
        waitForTap = false;
        waitForVoice = false;
        waitForCommentExit = false;
        lastFrame = -1;
        jumpFrame = -1;
        title = null;
        shake = null;
        gateZoom = null;
        gatePan = null;
        previousCamera = null;
        camera.backgroundColor = defaultBackgroundColor;
        camera.orthographicSize = defaultOrthoSize;
        time = 0f;

        bool hasCourse = false;
        foreach (var track in cut.Sheet._bgList ?? Array.Empty<BackgroundGroup>())
        {
            if (Keys(track._keys).Length == 0)
                continue;
            GameObject instance;
            bool useCourse = track._name == Course;
            if (support != null && track._keys._loadedType == 1)
                instance = support.FindBackground(track._keys._positionIndex);
            else if (loader.BackgroundPrefab(track) is GameObject prefab && prefab)
            {
                instance = useCourse ? course : InstantiateVisual(prefab, cutRoot);
                // Apply the course offset to the reused course. Local background prefabs retain their authored transform.
                if (useCourse)
                    instance.transform.SetLocalPositionAndRotation(cut.Data._backgroundPosOffset,
                        Quaternion.Euler(cut.Data._backgroundRotationOffset));
            }
            else
                continue;
            if (!instance)
                continue;
            if (cut.Cast == Cast.Opening)
            {
                var normalDoor = instance.transform.Find("door_normal");
                var ssrDoor = instance.transform.Find("door_ssr");
                // The corridor prefab stores two complete variants at the same location.
                if (normalDoor && ssrDoor)
                {
                    bool useSsrDoor = openingId == 9002 || loader.Draws.Any(draw => draw.Rarity == 3);
                    normalDoor.gameObject.SetActive(!useSsrDoor);
                    ssrDoor.gameObject.SetActive(useSsrDoor);
                }
            }
            var background = new VisualTrack<BackgroundKey>(instance, Keys(track._keys));
            if (supportOptions == null)
            {
                foreach (var mirror in background.Mirrors)
                {
                    if (!mirror.IsInitialized || mirror.BaseCamera != camera)
                        mirror.Initialize(camera);
                    // All CutIn world renderers are moved off their source layers.
                    mirror.RemoveCullingMask(~(1 << PreviewLayer));
                    mirror.AddCullingMask(1 << PreviewLayer);
                }
                // Unity needs an Animator to sample imported background clips, including child-only curves.
                if (cut.Unique && background.Keys.Any(key => key._isValidClip && key._clip) &&
                    !instance.TryGetComponent<Animator>(out _))
                {
                    var animator = instance.AddComponent<Animator>();
                    animator.applyRootMotion = false;
                }

                if (cut.Unique && !useCourse)
                    BindBackgroundTimeMaterials(background);
                if (background.Keys.Any(key => key.GrassList is { Length: > 0 }))
                    background.GrassMaterials = background.Renderers.SelectMany(renderer =>
                        renderer.sharedMaterials.Select((material, slot) => (renderer, slot, material)))
                        .Where(binding => binding.material && binding.material.HasProperty(GrassWindParamId)).ToArray();
            }
            backgrounds.Add(background);
            if (track.SearchId != null)
                backgroundAnchors.TryAdd(track.SearchId, instance.transform);
            hasCourse |= useCourse;
        }
        BindActors(cut, hasCourse);
        foreach (var (_, mini, _) in cut.MiniCast)
            mini.gameObject.SetActive(true);
        var backgroundObjects = backgrounds.Select(bg => bg.Instance).ToArray();
        if (cut.Sheet.AudienceList is { Length: > 0 } || cut.Sheet.ChoreographyCyalumeList is { Length: > 0 })
            audience = new GachaCutInAudience(backgroundObjects, cut.Sheet);
        if (cut.Sheet.CharaPropList is { Length: > 0 })
            props = new GachaCutInProps(cutRoot, camera.transform, FindBackgroundAnchor,
                slot => cast.TryGetValue(slot, out var actor) ? actor.Actor.transform : null,
                cut.Sheet.CharaPropList, loader.LoadProp,
                cut.Unique && supportOptions == null
                    ? slot => cast.TryGetValue(slot, out var actor) ? actor.Actor.Position : null
                    : null, ColorCorrectionExemptLayer);
        if (cut.Unique && cut.Sheet.SupportCardList is { Length: > 0 } sceneCardGroups)
            sceneCards = loader.LoadSceneCards(cutRoot, sceneCardGroups, cast.Values.Select(actor => actor.Actor.CharaEntry.Id));
        monitorCamera = GachaMonitorCamera.Create(camera, cutRoot, cut.Sheet._monitorCameraList,
            cut.Data._monitorCameraSettings, backgrounds.Select(bg => bg.Instance).Concat(props?.Instances ?? Enumerable.Empty<GameObject>()));
        if (monitorCamera != null)
            camera.cullingMask |= 1 << GachaMaskCamera.OverlayLayer;
        environment.SetTrack(cut.Sheet, cutRoot, backgroundObjects, maskCamera?.Camera, monitorCamera?.Camera);

        foreach (var track in cut.Sheet._effectList ?? Array.Empty<EffectGroup>())
        {
            if (Keys(track._keys).Length == 0 || track._keys.IsFlash)
                continue;
            var prefab = loader.EffectPrefab(track);
            if (!prefab)
                continue;
            // The effect's sky billboard exposes its edges in the viewer's render pass.
            // The course sky mesh and cloud background cover the same shot.
            if (hasCourse && prefab.name == CourseSkyEffect)
                continue;
            var visual = new VisualTrack<EffectKey>(InstantiateVisual(prefab, cutRoot), Keys(track._keys));
            foreach (var supporter in visual.TrailCustomData)
            {
                int rendererIndex = Array.IndexOf(visual.Renderers, supporter.GetComponent<ParticleSystemRenderer>());
                if (rendererIndex >= 0)
                    supporter.InitializeForTimeline(GetEffectMaterials(visual, rendererIndex));
            }
            visual.Emission = visual.Particles.Select(particle => particle.emission.enabled).ToArray();
            var subEmitterOverride = track._keys.IsOverrideSubEmitter;
            if (supportOptions == null &&
                (subEmitterOverride?._isValid == true ? subEmitterOverride._value : cut.Sheet.IsSubEmitterUse))
                visual.ParticleSimulation = BindEffectSubEmitters(visual.Particles);
            visual.UseTimeScale = track._keys._isValidTimeScaleCurve && Keys(cut.Sheet._timeScaleList).Length != 0;
            visual.UseGachaSubColor = supportOptions == null && cut.Unique && track._keys.IsEnableCharaSubColor;
            ApplyBackdropDepth(visual, prefab.name);
            // The 100501 black backdrop takes its stencil operation from the prefab.
            if (supportOptions == null && cut.Name == "crd100501_001" && prefab.name == "pfb_eff_chr1005_00_05")
                for (int index = 0; index < visual.Renderers.Length; index++)
                    foreach (var material in GetEffectMaterials(visual, index))
                        PreserveEffectStencil(material, "mtl_eff_chr1005_black");
            effects.Add(visual);
            if (!string.IsNullOrEmpty(track._keys._searchId))
                effectAnchors.TryAdd(track._keys._searchId, visual);
        }

        if (seekable)
            PrepareEffectSeeking();
        if (cut.Unique)
            audio.PlayCardEffect(ActiveDraw.Card);
        if (cut.GateAlreadyOpen)
            gates[cut.ResultIndex].Open(loader.OpenedGateClip);
        SampleTo(cut.StartFrame / FramesPerSecond, cut.StartFrame != 0);
        if (cut.Cast == Cast.Lineup)
            foreach (var actor in cast)
                lineupPositions[actor.Key] = actor.Value.Actor.transform.localPosition;
    }

    private void BeginTimingCut()
    {
        if (chapter >= 0 && !Finished && Current.Unique)
            audio.StopCardEffect();
        flash?.Clear();
        chapter++;
        if (Finished)
            return;
        previousCommentKey = null;
        waitForTap = waitForVoice = waitForCommentExit = false;
        lastFrame = -1;
        time = 0f;
        if (Current.ResultIndex != flashResultIndex && (Current.Unique || Current.GateAlreadyOpen))
        {
            flash?.Dispose();
            flash = loader.LoadTextEffects(camera, ActiveDraw);
            flashResultIndex = Current.ResultIndex;
        }
        if (Current.Unique)
            audio.PlayCardEffect(ActiveDraw.Card);
        SampleTo(Current.StartFrame / FramesPerSecond, Current.StartFrame != 0);
    }

    private void BindActors(Cut cut, bool hasCourse)
    {
        if (cut.Cast == Cast.Opening)
        {
            BindActor(cut.ActorSlot, loader.OpeningActor, Vector3.zero);
            return;
        }
        if (cut.Cast == Cast.Lineup)
        {
            for (int i = 0; i < loader.Draws.Count; i++)
                BindActor(i, loader.LineupActors[i], Vector3.zero);
            return;
        }
        if (cut.Cast != Cast.Result)
            return;

        if (!cut.FixedCast.ContainsKey(cut.ActorSlot))
            BindActor(cut.ActorSlot, ActiveDraw.Actor, hasCourse ? lineupPositions[cut.ResultIndex] : Vector3.zero);
        foreach (var actor in cut.FixedCast)
            BindActor(actor.Key, actor.Value, Vector3.zero);
        if (hasCourse)
            for (int slot = 0; slot < cut.Sheet._characterList.Length; slot++)
            {
                var keys = cut.Sheet._characterList[slot]._characterKeys;
                if (slot == cut.ActorSlot || keys?._characterType != 1)
                    continue;
                int lane = keys._charaIndex - 10;
                if (lane > cut.ResultIndex && lane < loader.LineupActors.Count)
                    BindActor(slot, loader.LineupActors[lane], Vector3.zero);
            }
    }

    private void BindActor(int slot, UmaContainerCharacter actor, Vector3 offset)
    {
        if (!actorPlayback.TryGetValue(actor, out var binding))
        {
            binding = new ActorPlayback(actor, root, loader);
            actorPlayback.Add(actor, binding);
        }
        var track = Current.Sheet._characterList[slot];
        binding.Reset(offset, track, supportOptions == null && Current.Unique);
        binding.Shadow = loader.CreateCharacterShadow(actor, track);
        binding.HasDirt = Keys(track.CharacterDirtKeyArray?.FirstOrDefault()).Length > 0;
        if (binding.HasDirt && actor.LiveDirtBindings == null)
        {
            actor.LoadLiveDirtTextures();
            var holder = new Gallop.Live.LiveRendererHolder();
            holder.OnInitialize(actor);
            actor.InstallLiveDirtMaterials(holder);
        }
        if (Keys(track._characterSweatKeys?.FirstOrDefault()).Length > 0)
            actor.InstallCutInSweat(PreviewLayer);
        binding.AuraKeys = Keys(track.CharacterAuraKeyArray?.FirstOrDefault());
        binding.Aura = binding.AuraKeys.Length == 0 ? null : new AuraPass.AuraParam
        {
            CharacterCamera = camera,
            RendererList = new List<Renderer>(binding.Renderers)
        };
        if (binding.Aura != null)
            post.Aura.AuraParamDic.Add(slot, binding.Aura);
        var holograms = Keys(track.CharacterHologramKeyArray?.FirstOrDefault());
        var mappings = Keys(track.CharacterScreenMappingKeyArray?.FirstOrDefault());
        bool hasShading = holograms.Length != 0 || mappings.Length != 0;
        if (supportOptions == null && Current.Unique)
            binding.MaterialColor = GachaCharacterMaterialColor.Create(binding.Renderers,
                Keys(track._characterColorKeys?.FirstOrDefault()), hasShading);
        if (hasShading)
            binding.Shading = new GachaCharacterShading(binding.Renderers, holograms, mappings);
        cast.Add(slot, binding);
    }

    private void InstallGates(Transform course)
    {
        for (int i = 0; i < GateMonitors.Length; i++)
        {
            var sign = FindNode(course, GateMonitors[i]);
            if (sign)
                sign.gameObject.SetActive(i == monitor);
        }

        for (int slot = 1; slot <= 10; slot++)
        {
            var target = FindNode(course, $"pos_gate_{slot:00}");
            if (!target)
                continue;
            var gate = new Gate(target);
            gates.Add(gate);
            int finishCount = slot <= cards.Count ? GateFinishes.Length : 1;
            for (int i = 0; i < finishCount; i++)
            {
                string name = Path.GetFileName(GatePath) + GateFinishes[i];
                if (!loader.Prefabs.TryGetValue(name, out var prefab))
                    continue;
                gate.Variants[i] = InstantiateVisual(prefab, target);
            }
            gate.SetFinish(slot <= loader.Draws.Count && selections != null ? loader.Draws[slot - 1].InitialGateFinish : 0);
            if (slot > cards.Count)
                gate.Open(loader.OpenedGateClip);
        }
    }

    private void SampleTo(float targetTime, bool seeking)
    {
        int targetFrame = Mathf.FloorToInt(targetTime * FramesPerSecond + 0.0001f);
        for (int next = lastFrame + 1; next <= targetFrame; ++next)
        {
            Current.Sounds.TryGetValue(next, out var sounds);
            if (sounds != null)
                foreach (var sound in sounds)
                {
                    audio.Apply(sound, seeking && next != targetFrame);
                    if ((!seeking || next == targetFrame) && sound._voiceData?._enable == true)
                    {
                        if (sound._voiceData._play && sound._voiceData._type == 1)
                        {
                            if (ActiveDraw is Draw draw)
                                audio.PlayVoice(draw.Card);
                        }
                        else if (!sound._voiceData._play)
                            audio.StopVoice();
                    }
                }

            Current.Events.TryGetValue(next, out var events);
            if (events != null)
                foreach (var entry in events)
                    Dispatch(entry, next, !seeking || next == targetFrame);

            lastFrame = next;
            if (waitForTap || waitForVoice)
            {
                targetTime = next / FramesPerSecond;
                break;
            }
        }

        float elapsed = Mathf.Max(0f, targetTime - time);
        time = targetTime;
        if (timingOnly)
        {
            SampleCommentFlash();
            return;
        }
        foreach (var bound in cast)
            SampleActor(bound.Key, bound.Value, Current.Sheet._characterList[bound.Key], seeking ? 0f : elapsed);
        foreach (var (track, mini, _) in Current.MiniCast)
            SampleMiniActor(mini, track, elapsed);
        SampleCamera();
        foreach (var binding in cast.Values)
            binding.ApplyModelGradient(camera, characterProperties);
        maskCamera?.Sample(lastFrame);
        monitorCamera?.Sample(lastFrame, time, slot => cast.TryGetValue(slot, out var actor)
            ? (actor.PositionAnimator.transform, GetCameraCharacterScale(actor)) : (null, 1f));
        environment.Sample(lastFrame);
        SamplePostEffects();
        SampleActorColorLayers();
        bool hasOrthoBackground = SampleBackgrounds();
        audience?.Sample(lastFrame, time * FramesPerSecond);
        props?.SampleBeforeEffects(lastFrame, time, GetActorLight);
        sceneCards?.Sample(lastFrame);
        bool hasOrthoEffect = SampleEffects(elapsed);
        SampleCameraBackground(Keys(Current.Sheet.CameraBackGroundKeys), hasOrthoBackground || hasOrthoEffect);
        foreach (var gate in gates)
            gate.SampleEffect(time);
        if (waitForTap || waitForVoice)
            SetEffectAnimatorsPaused(true);
        // Scene shader UVs use the same sampled clock as particles and animators.
        // UI cameras keep their own clock so the name reveal can finish at a hold.
        foreach (var sceneCamera in root.GetComponentsInChildren<Camera>(true))
        {
            if (flash?.UsesCamera(sceneCamera) == true)
                continue;
            var data = sceneCamera.GetComponent<Gallop.RenderPipeline.CameraData>();
            if (!data) data = sceneCamera.gameObject.AddComponent<Gallop.RenderPipeline.CameraData>();
            data.CutInShaderTime = time;
        }
    }

    private void Dispatch(CutEvent entry, int frame, bool playTransient)
    {
        if (timingOnly && entry.eventId != 7 && entry.eventId != 10 && entry.eventId != 99)
            return;
        switch (entry.eventId)
        {
            case 1:
                shake = Read<ShakeEvent>(entry);
                shakeFrame = frame;
                break;
            case 3:
                foreach (var binding in cast.Values)
                    binding.Actor.ResetDynamicBone();
                break;
            case 7:
                waitForTap = true;
                break;
            case 8:
                jumpFrame = Read<JumpEvent>(entry).JumpFrame;
                break;
            case 9:
                ApplyGateEvent(Read<GateEvent>(entry), frame, playTransient);
                break;
            case 10:
                waitForVoice = audio.IsVoicePlaying || flash?.CommentVisible == true;
                break;
            case 12:
                gateZoom = Read<GateZoomEvent>(entry);
                gateZoomFrame = frame;
                break;
            case 13:
                gatePan = Read<GatePanEvent>(entry);
                gatePanFrame = frame;
                gatePanPosition = frame;
                var curve = cards.Count - 1 < gatePan.CountBorder ? gatePan.Less5Curve : gatePan.More5Curve;
                gatePanCurve = new AnimationCurve(curve.keys.Select(k => new Keyframe(k.time, k.value,
                    k.inTangent, k.outTangent, k.inWeight, k.outWeight) { weightedMode = k.weightedMode }).ToArray());
                break;
            case 99:
                var eventData = Read<CommandEvent>(entry);
                string command = eventData.StringParam;
                if (support != null)
                {
                    ApplySupportCommand(command, Mathf.RoundToInt(eventData.FloatParam), frame);
                    break;
                }
                if (command == "CharaNameFlashIn" && ActiveDraw is Draw draw)
                {
                    if (flash?.PlayName() == true)
                        audio.PlayCharacterNameEffect(draw.Rarity);
                    title = draw.Label;
                }
                if (command == "ResultStart")
                    title = loader.Draws.Count == 1 ? loader.Draws[0].Label : null;
                break;
        }
    }

    private void ApplySupportCommand(string command, int value, int frame)
    {
        support.Apply(command, value, frame);
        switch (command)
        {
            case "AlbumPageUpdateEffect":
                if (Current.AlbumUpgradeRarity > 0)
                    audio.PlayEffect($"snd_sfx_gacha_{42 + Current.AlbumUpgradeRarity:D3}");
                break;
            case "SupportCardFlashIn":
                flash?.PlaySupport(true);
                audio.PlayEffect($"snd_sfx_gacha_{37 + support.Current.Rarity:D3}");
                break;
            case "SupportCardFlashOut":
                flash?.PlaySupport(false);
                break;
            case "ResultStart":
                title = support.Results.Count == 1 ? support.Current.Title + support.Current.Name : null;
                break;
        }
    }

    private void ApplyGateEvent(GateEvent key, int frame, bool playTransient)
    {
        int index = key.CharaIndex < 0 ? Current.ResultIndex : key.CharaIndex;
        if (index < 0 || index >= loader.Draws.Count)
            return;
        var gate = gates[index];
        var draw = loader.Draws[index];
        if (key.IsChangeGate)
            gate.SetFinish(Current.UpgradesGate ? draw.Rarity - 1 : draw.InitialGateFinish);
        if (key.IsGateOpen)
            gate.OpeningFrame = frame;
        if (key.IsPlayEffect && playTransient)
        {
            if (loader.Prefabs.TryGetValue(GateEffects[gate.Finish], out var prefab))
                gate.PlayEffect(prefab, frame / FramesPerSecond);
            audio.PlayEffect(GateEffectCues[gate.Finish]);
        }
    }

    private void SampleActor(int slot, ActorPlayback binding, CharacterTrack track, float deltaTime)
    {
        var actor = binding.Actor;
        var transformKeys = Keys(track._characterKeys);
        var transformKey = CurrentKey(transformKeys, lastFrame);
        actor.gameObject.SetActive(transformKey?._visible == true);
        binding.SampleAura(lastFrame, actor.gameObject.activeSelf);
        bool atClosedGate = Current.Cast == Cast.Lineup || Current.UpgradesGate ||
            Current.Name.StartsWith("gac_chr_gate_out", StringComparison.Ordinal) &&
            (slot != Current.ActorSlot || gates[Current.ResultIndex].OpeningFrame < 0);
        var motion = CurrentKey(Keys(track._characterMotionKeys), lastFrame);
        float elapsed = atClosedGate || motion == null ? 0f : RelativeTime(motion);
        bool sampleAuthoredScale = supportOptions == null && Current.Unique;
        if (!actor.gameObject.activeSelf)
        {
            if (sampleAuthoredScale && transformKey != null && motion != null)
            {
                // JP updates the locator's model target even when the actor is hidden.
                // DOF samples that model root, including for a hidden slot.
                actor.transform.SetLocalPositionAndRotation(binding.Offset, Quaternion.identity);
                UpdateActorModelScale(binding, track, transformKey, sampleAuthoredScale);
                SampleActorPlacement(binding, transformKey, motion, elapsed);
            }
            return;
        }
        binding.SetLegacyPhysicsPaused(atClosedGate || waitForTap || waitForVoice);
        binding.BodyExpressions?.Restore();
        binding.HeadExpressions?.Restore();
        binding.RestoreAdditiveFaceBase();
        binding.Shading?.Sample(lastFrame, time);
        if (transformKey != binding.PreviousCharacter)
        {
            foreach (var springKey in transformKeys)
                if (springKey._frame <= lastFrame &&
                    (binding.PreviousCharacter == null || springKey._frame > binding.PreviousCharacter._frame))
                    SetSpringChains(binding, springKey, restoring: false);
            if (actor.LiveCySpringController is { } spring)
            {
                spring.IsAffectOwnerRotation = transformKey._isAffectOwnerRotation;
                spring.SpringRate = transformKey.CySpringRate;
            }
            var eyebrowKey = transformKeys.LastOrDefault(key => key._frame <= lastFrame && key.IsMayuControl);
            binding.SetEyebrowsHidden(eyebrowKey?.IsHideMayu == true);
            var partsKey = transformKeys.LastOrDefault(key => key._frame <= lastFrame && key.IsPartsControl);
            if (binding.FacePartsScale != null)
                binding.FacePartsScale.Enabled = partsKey?.IsPartsScale ?? true;
            var expressionKey = transformKeys.LastOrDefault(key => key._frame <= lastFrame && key.IsValidTransformExpression);
            binding.BodyExpressionEnabled = expressionKey?.IsEnableBodyExpression ?? true;
            binding.HeadExpressionEnabled = expressionKey?.IsEnableHeadExpression ?? true;
            binding.PreviousCharacter = transformKey;
        }

        if (motion == null)
        {
            ApplyCharacterEmissiveColor(binding, SampleCharacterEmissiveColor(binding, transformKeys, transformKey));
            binding.SampleParts(lastFrame);
            binding.Shadow?.Sample(lastFrame, 0f);
            binding.SampleWind(track, lastFrame);
            binding.SampleSweat(track, lastFrame);
            if (!atClosedGate)
                actor.StepGachaPhysics(deltaTime);
            binding.SampleExpressions();
            return;
        }
        var body = motion._bodyClip ? motion._bodyClip : motion._bodyClips?.FirstOrDefault(c => c);
        if (!body)
            body = motion._clip;
        var face = motion._facialClip?.clip;
        var ear = motion._earClip?.clip;
        var bodyAnimator = actor.UmaAnimator;

        if (motion != binding.PreviousMotion)
        {
            if (motion._isResetCyspring && actor.OverrideController["clip_2"] != body)
            {
                // JP SetCutInHelperCharaTypeMotion reserves one second of warm-up on a body clip change.
                // Apply it after sampling the new pose; a direct reset exposes unsettled cloth at cuts.
                if (actor.LiveCySpringController is { } spring)
                    spring.ReserveWarmingUpCySpring(1f);
                else
                    actor.ResetDynamicBone();
            }
            actor.ResetBodyPose();
            binding.RestoreFaceLocators();
            actor.OverrideController["clip_2"] = body;
            bodyAnimator.SetLayerWeight(1, 0f);
            bodyAnimator.SetLayerWeight(2, 0f);
        }

        actor.transform.SetLocalPositionAndRotation(binding.Offset, Quaternion.identity);
        UpdateActorModelScale(binding, track, transformKey, sampleAuthoredScale);
        PlaySample(bodyAnimator, body, "motion_2", 0, elapsed, motion._bodyClipHeadTime, motion._bodyClipPlaySpeed, motion._loop);
        bodyAnimator.Update(0f);
        SampleActorPlacement(binding, transformKey, motion, elapsed);
        if (!sampleAuthoredScale)
            actor.Position.localScale = Vector3.one * binding.ModelScale;

        binding.SampleFace(face, ear, motion, elapsed);
        void ApplyFace() => actor.FaceDrivenKeyTarget.ProcessCutInLocator(ear ? motion._earClip : null);
        if (actor.LiveEyeHighlight != null)
        {
            float facialTime = elapsed * (motion._facialClipPlaySpeed?.Or(1f) ?? 1f) +
                (motion._facialClipHeadTime?.Or(0f) ?? 0f);
            actor.LiveEyeHighlight.SampleCutIn(time, deltaTime, facialTime,
                motion != binding.PreviousMotion && motion.ResetEyeHighlightAnimation,
                motion.IsEyeHighlightPlayTime, motion.IsNeedUpdateEyeHighlightFromTimeline,
                ApplyFace, transformKey.IsEyePupli);
        }
        else
            ApplyFace();
        binding.FacePartsScale?.AfterApplyDrivenKey(!face, !face);
        binding.SampleAdditiveFace(motion._facialAdditiveMotionAsset, elapsed + 1f / FramesPerSecond);
        binding.PreviousMotion = motion;
        SampleCharacterColor(binding, track, SampleCharacterEmissiveColor(binding, transformKeys, transformKey));
        if (binding.HasDirt)
            SampleCharacterDirt(binding.Renderers, Keys(track.CharacterDirtKeyArray.FirstOrDefault()));
        binding.SampleParts(lastFrame);
        binding.Shadow?.Sample(lastFrame, body ? body.length : 0f);
        binding.SampleWind(track, lastFrame);
        binding.SampleSweat(track, lastFrame);
        if (!atClosedGate)
            actor.StepGachaPhysics(deltaTime);
        actor.AdvanceShaderEffects(deltaTime);
        binding.SampleExpressions();
    }

    private static void SampleActorPlacement(ActorPlayback binding, CharacterKey key, CharacterMotion motion, float elapsed)
    {
        var actor = binding.Actor.transform;
        var locator = binding.SamplePositionLocator(motion, elapsed).transform;
        if (key._usePosition) actor.localPosition = binding.Offset + key._pos;
        if (key._useAddPos) actor.localPosition += key._addPos;
        if (key._useRotation) actor.localRotation = Quaternion.Euler(key._rot);
        if (motion._positionClip)
        {
            // JP applies the position locator in LateUpdate, after character keys.
            // An authored position clip must retain its placement at a shot change.
            actor.SetLocalPositionAndRotation(binding.Offset + locator.localPosition, locator.localRotation);
            binding.ApplyPositionClipScale(motion);
        }
    }

    private void UpdateActorModelScale(ActorPlayback binding, CharacterTrack track, CharacterKey transformKey,
        bool sampleAuthoredScale)
    {
        float heightRatio = transformKey._isEnableHeightScale ? transformKey._heightScale : 0f;
        binding.ModelScale = Mathf.Lerp(1f, binding.Actor.BodyScale, heightRatio) * SampleCharacterScale(track);
        if (sampleAuthoredScale)
            binding.Actor.Position.localScale = Vector3.one * binding.ModelScale;
    }

    private float SampleCharacterScale(CharacterTrack track)
    {
        float scale = 1f;
        if (track.CharacterScaleFactorKeyArray == null)
            return scale;
        foreach (var entry in track.CharacterScaleFactorKeyArray)
        {
            GetKeyInterval(Keys(entry), lastFrame, out var current, out var next);
            if (current?.ScaleFactor?._isValid != true)
                continue;
            scale = Mathf.LerpUnclamped(current.ScaleFactor._value, next?.ScaleFactor?.Or(current.ScaleFactor._value)
                ?? current.ScaleFactor._value, TrackWeight(current, next, next?._params, 0));
        }
        return scale;
    }

    private void SampleMiniActor(LiveMiniCharacter mini, MiniCharacterTrack track, float deltaTime)
    {
        var key = CurrentKey(Keys(track.CharacterKeys), lastFrame);
        bool visible = key?.IsVisible == true;
        mini.SetVisible(visible);
        if (supportOptions == null && Current.Unique && key?.IsUseUpdateStencil == true)
        {
            int stencil = MiniStencilBase + key.UpdateStencilValue;
            foreach (var materials in mini.RendererHolder.MaterialArray)
            foreach (var material in materials)
                if (material && material.HasProperty(MiniStencilMaskId))
                    material.SetInt(MiniStencilMaskId, stencil);
        }
        if (!visible)
            return;

        var modelRoot = mini.Model.transform;
        modelRoot.SetLocalPositionAndRotation(key.IsUsePosition ? key._pos : Vector3.zero,
            key.IsUseRotation ? Quaternion.Euler(key._rot) : Quaternion.identity);
        modelRoot.localScale = key.IsUseScale ? key._scale : Vector3.one;

        var motion = CurrentKey(Keys(track.CharacterMotionKeys), lastFrame);
        if (motion != null)
        {
            float elapsed = RelativeTime(motion);
            float At(FloatValue head, FloatValue speed) => (head?.Or(0f) ?? 0f) + elapsed * (speed?.Or(1f) ?? 1f);
            var body = motion._bodyClips?.FirstOrDefault(clip => clip);
            if (body)
            {
                mini.Animation.PlayBodyMotion(body, At(motion._bodyClipHeadTime, motion._bodyClipPlaySpeed), motion._loop);
                mini.Animation.Body.SetSpeed(0f);
            }
            if (motion._positionClip)
            {
                mini.Animation.PlayPositionMotion(motion._positionClip, At(motion._positionClipHeadTime, motion._positionClipPlaySpeed), motion._loop);
                mini.Animation.Position.SetSpeed(0f);
            }
            mini.Animation.PlayFacialMotion(motion._facialClip, At(motion._facialClipHeadTime, motion._facialClipPlaySpeed), motion._loop);
            mini.Animation.PlayEarMotion(motion._earClip, At(motion._earClipHeadTime, motion._earClipPlaySpeed), motion._loop);
        }

        SampleMiniColor(mini, track);
        mini.Evaluate(deltaTime);
    }

    private void SampleMiniColor(LiveMiniCharacter mini, MiniCharacterTrack track)
    {
        if (track.CharacterColorKeys == null)
            return;
        foreach (var colorTrack in track.CharacterColorKeys)
        {
            GetKeyInterval(Keys(colorTrack), lastFrame, out var color, out var next);
            if (color == null)
                break;
            bool interpolate = next?._params?.FirstOrDefault()?._interpolateType > 0;
            float weight = interpolate
                ? Interpolation.NativeWeightBetween(color, next, next._params, 0, lastFrame) : 0f;
            var tint = color.CharaColor?.Or(Color.white) ?? Color.white;
            float power = color.ColorPower?.Or(1f) ?? 1f;
            float outlineWidth = color.OutlineWidthPower?.Or(1f) ?? 1f;
            if (interpolate)
            {
                tint = Color.Lerp(tint, next.CharaColor?.Or(Color.white) ?? Color.white, weight);
                power = Mathf.Lerp(power, next.ColorPower?.Or(1f) ?? 1f, weight);
                outlineWidth = Mathf.Lerp(outlineWidth, next.OutlineWidthPower?.Or(1f) ?? 1f, weight);
            }
            mini.Color.SetCharaColor(tint * power);
            mini.Color.SetOutlineWidthForPower(outlineWidth);
            if (color.OutlineColor?._isValid == true)
            {
                Color outline = color.OutlineColor._value;
                if (interpolate)
                    outline = Color.Lerp(outline, next.OutlineColor?.Or(outline) ?? outline, weight);
                var blend = (interpolate ? next : color).OutlineColorBlendType;
                mini.Color.SetOutlineColor(outline, blend?._isValid == true
                    ? (LiveTimelineDefine.OutlineColorBlend)blend._intValue : LiveTimelineDefine.OutlineColorBlend.Blend);
            }
            mini.Color.UpdateMaterialPropertyBlock();
        }
    }

    private Color? SampleCharacterEmissiveColor(ActorPlayback binding, CharacterKey[] keys, CharacterKey current)
    {
        if (!binding.HasAuthoredEmissiveColor)
            return null;

        const int channel = 8;
        int currentIndex = Array.IndexOf(keys, current);
        for (int index = currentIndex + 1; index < keys.Length; index++)
        {
            var destination = keys[index];
            if (!IsEndpoint(destination))
                continue;
            var interpolation = destination._params[channel];
            if (interpolation?._interpolateType > 0)
            {
                for (int previous = currentIndex; previous >= 0; previous--)
                {
                    var source = keys[previous];
                    if (!IsEndpoint(source))
                        continue;
                    float weight = interpolation.WeightBetween(source, destination, lastFrame);
                    return Color.LerpUnclamped(EmissiveColor(source), EmissiveColor(destination), weight);
                }
            }
            break;
        }
        return EmissiveColor(current);

        static bool IsEndpoint(CharacterKey key) => key.IsEmissiveColor && key._params?.Length > channel;
        static Color EmissiveColor(CharacterKey key) => key.IsEmissiveColor ? key.EmissiveColor : Color.white;
    }

    private void ApplyCharacterEmissiveColor(ActorPlayback binding, Color? emissiveColor)
    {
        if (!emissiveColor.HasValue)
            return;
        foreach (var renderer in binding.Renderers)
        {
            renderer.GetPropertyBlock(characterProperties);
            characterProperties.SetColor(CharacterEmissiveColorId, emissiveColor.Value);
            renderer.SetPropertyBlock(characterProperties);
        }
    }

    private void SampleCharacterColor(ActorPlayback binding, CharacterTrack track, Color? emissiveColor)
    {
        binding.MaterialColor?.Sample(lastFrame);
        binding.MaterialColor?.Apply();
        var keys = Keys(track._characterColorKeys?.FirstOrDefault());
        var color = CurrentKey(keys, lastFrame);
        if (color == null)
        {
            ApplyCharacterEmissiveColor(binding, emissiveColor);
            return;
        }

        var next = NextKey(keys, lastFrame);
        // Character color is interpolated into the policy authored on the destination key.
        var interpolation = next?._params?.FirstOrDefault();
        float blend = interpolation?._interpolateType > 0 ? interpolation.WeightBetween(color, next, lastFrame) : 0f;
        bool usesCharacterLight = color.LightDir?._isValid == true;
        Vector3 bodyLight = Quaternion.Euler(MixAngle(color.LightDir, next?.LightDir, blend)) * Vector3.back;
        binding.BodyLight = bodyLight;
        Vector3 faceLight = bodyLight;
        // CutIn materializes an invalid face-mode key as Off, even when its
        // serialized enum still contains FaceTrace.
        var faceMode = color.UseOriginalDirectionalLight?._isValid == true
            ? color.UseOriginalDirectionalLight._intValue : 0;
        if (faceMode != 0 && color.OriginalDirectionalLightDir != null)
        {
            var angles = MixAngle(color.OriginalDirectionalLightDir, next?.OriginalDirectionalLightDir, blend);
            angles.x += 180f;
            faceLight = Quaternion.Euler(angles) * Vector3.forward;
            var head = binding.Actor.HeadToonBaseTransform;
            if (faceMode == 2 && head)
                faceLight = head.rotation * faceLight;
        }
        bool characterUnique = supportOptions == null && Current.Unique;
        bool interpolateNative = characterUnique && interpolation?._interpolateType > 0;
        float nativeWeight = interpolateNative
            ? Interpolation.NativeWeightBetween(color, next, next._params, 0, lastFrame)
            : 0f;
        var nativePolicy = interpolateNative ? next : color;
        if (binding.HasModelGradient)
            binding.SetModelGradientKey(color, nativePolicy, nativeWeight);
        float vertexToon = binding.HasVertexToon ? SampleNativeFloat(color.VertexColorToonPower, next?.VertexColorToonPower) : 1f;
        float emissiveIntensity = 1f, emissiveRimPower = 1f, emissiveRimIntensity = 1f;
        int emissiveCenter = 0;
        if (binding.HasCharacterEmissive)
        {
            emissiveIntensity = SampleEmissiveFloat(color.EmissiveIntensity, next?.EmissiveIntensity);
            emissiveRimPower = SampleEmissiveFloat(color.EmissiveRimPower, next?.EmissiveRimPower);
            emissiveRimIntensity = SampleEmissiveFloat(color.EmissiveRimIntensity, next?.EmissiveRimIntensity);
            emissiveCenter = nativePolicy.IsEmissiveCenter is { _isValid: true, _value: true } ? 1 : 0;
        }
        float outlineWidthPower = MixFloat(color.OutlineWidthPower, next?.OutlineWidthPower, 1f, blend);
        Color outline = default;
        if (characterUnique)
        {
            outlineWidthPower = color.OutlineWidthPower?.Or(1f) ?? 1f;
            outline = color.OutlineColor?.Or(DefaultCharacterOutlineEditColor) ?? DefaultCharacterOutlineEditColor;
            var blendMode = color.OutlineColorBlendType;
            if (interpolateNative)
            {
                // Invalid endpoints use the timeline defaults. The mode comes from the destination key.
                outlineWidthPower = Mathf.Lerp(outlineWidthPower, next.OutlineWidthPower?.Or(1f) ?? 1f, nativeWeight);
                outline = Color.Lerp(outline, next.OutlineColor?.Or(DefaultCharacterOutlineEditColor) ?? DefaultCharacterOutlineEditColor,
                    nativeWeight);
                blendMode = next.OutlineColorBlendType;
            }
            outline.a = (blendMode?._isValid == true ? blendMode._intValue : 0) switch
            {
                0 => (1f - outline.a) * .5f,
                1 => outline.a * .5f + .5f,
                _ => .5f
            };
        }
        bool hasHair = binding.Renderers.Any(renderer => renderer.name == "M_Hair");
        for (int index = 0; index < binding.Renderers.Length; index++)
        {
            var renderer = binding.Renderers[index];
            renderer.GetPropertyBlock(characterProperties);
            bool face = renderer == binding.Actor.FaceRenderer || renderer.name == "M_Mayu" ||
                renderer.sharedMaterials.Any(material => material &&
                LiveCharacterLightingRuntime.Classify(renderer.name, material, hasHair) is
                    LiveCharacterLightingRuntime.Destination.FaceSkin or
                    LiveCharacterLightingRuntime.Destination.Hair or
                    LiveCharacterLightingRuntime.Destination.Mayu);
            characterProperties.SetInt("_UseOriginalDirectionalLight", usesCharacterLight || face && faceMode != 0 ? 1 : 0);
            characterProperties.SetVector("_OriginalDirectionalLightDir", face ? faceLight : bodyLight);
            if (face && !characterUnique)
            {
                float noseThreshold = MixFloat(color.NosePretenseThreshold, next?.NosePretenseThreshold, .775f, blend);
                characterProperties.SetFloat("_NosePretenseThreshold", noseThreshold);
                characterProperties.SetFloat("_NoseVisibility", noseThreshold > 0f ? 1f : 0f);
                characterProperties.SetFloat("_CheekPretenseThreshold",
                    MixFloat(color.CheekPretenseThreshold, next?.CheekPretenseThreshold, .775f, blend));
            }
            var tint = MixColor(color.CharaColor, next?.CharaColor, Color.white, blend);
            var power = MixFloat(color.ColorPower, next?.ColorPower, 1f, blend);
            characterProperties.SetVector("_CharaColor", tint * power);
            characterProperties.SetVector("_ToonDarkColor",
                MixColor(color.ToonDarkColor, next?.ToonDarkColor, new Color(.75f, .75f, .75f, 0f), blend));
            characterProperties.SetVector("_ToonBrightColor",
                MixColor(color.ToonBrightColor, next?.ToonBrightColor, new Color(1f, 1f, 1f, 0f), blend));
            characterProperties.SetColor("_RimColor", MixColor(color.RimColor, next?.RimColor, Color.clear, blend));
            characterProperties.SetFloat("_RimStep", MixFloat(color.RimStep, next?.RimStep, 0f, blend));
            characterProperties.SetFloat("_RimFeather", MixFloat(color.RimFeather, next?.RimFeather, 0f, blend));
            characterProperties.SetFloat("_RimSpecRate", MixFloat(color.RimSpecRate, next?.RimSpecRate, 0f, blend));
            characterProperties.SetFloat("_RimShadowRate",
                MixFloat(color.GlobalRimShadowRate, next?.GlobalRimShadowRate, 0f, blend));
            characterProperties.SetFloat("_RimHorizonOffset", MixFloat(color.RimHorizonOffset, next?.RimHorizonOffset, 0f, blend));
            characterProperties.SetFloat("_RimVerticalOffset", MixFloat(color.RimVerticalOffset, next?.RimVerticalOffset, 0f, blend));
            characterProperties.SetFloat("_RimHorizonOffset2", MixFloat(color.RimHorizonOffset2, next?.RimHorizonOffset2, 0f, blend));
            characterProperties.SetFloat("_RimVerticalOffset2", MixFloat(color.RimVerticalOffset2, next?.RimVerticalOffset2, 0f, blend));
            characterProperties.SetColor("_RimColor2", MixColor(color.RimColor2, next?.RimColor2, Color.clear, blend));
            characterProperties.SetFloat("_RimStep2", MixFloat(color.RimStep2, next?.RimStep2, 0f, blend));
            characterProperties.SetFloat("_RimFeather2", MixFloat(color.RimFeather2, next?.RimFeather2, 0f, blend));
            characterProperties.SetFloat("_RimSpecRate2", MixFloat(color.RimSpecRate2, next?.RimSpecRate2, 0f, blend));
            characterProperties.SetFloat("_RimShadowRate2", MixFloat(color.GlobalRimShadowRate2, next?.GlobalRimShadowRate2, 0f, blend));
            characterProperties.SetFloat("_OutlineWidth", .325f * outlineWidthPower);
            if (characterUnique)
                characterProperties.SetVector(CharacterOutlineColorId, outline);
            if (binding.HasVertexToon)
                characterProperties.SetFloat(CharacterVertexToonPowerId, vertexToon);
            if (emissiveColor.HasValue)
                characterProperties.SetColor(CharacterEmissiveColorId, emissiveColor.Value);
            if (binding.HasCharacterEmissive)
            {
                characterProperties.SetFloat(CharacterEmissiveIntensityId, emissiveIntensity);
                characterProperties.SetFloat(CharacterEmissiveRimPowerId, emissiveRimPower);
                characterProperties.SetFloat(CharacterEmissiveRimIntensityId, emissiveRimIntensity);
                characterProperties.SetInt(CharacterEmissiveRimCenterId, emissiveCenter);
            }
            renderer.SetPropertyBlock(characterProperties);
            binding.ApplyEyeToon(index, characterProperties, color, next, blend,
                usesCharacterLight || faceMode != 0, faceLight);
        }

        // Native direct and interpolated samples substitute each invalid endpoint independently.
        float SampleNativeFloat(FloatValue from, FloatValue to) =>
            Mathf.Lerp(from?.Or(1f) ?? 1f, to?.Or(1f) ?? 1f, nativeWeight);

        // The CutIn helper also checks the destination's validity for emissive values.
        float SampleEmissiveFloat(FloatValue from, FloatValue to) =>
            (interpolateNative ? to : from)?._isValid == true ? SampleNativeFloat(from, to) : 1f;
    }

    private Vector3? GetActorLight(int slot) => cast.TryGetValue(slot, out var actor) ? actor.BodyLight : null;

    private static Vector3 MixAngle(VectorValue current, VectorValue next, float blend)
    {
        var angle = current?.OrZero ?? Vector3.zero;
        return Vector3.LerpUnclamped(angle, next?._isValid == true ? next._value : angle, blend);
    }

    private static Color MixColor(ColorValue current, ColorValue next, Color fallback, float blend)
    {
        var value = current?.Or(fallback) ?? fallback;
        return Color.Lerp(value, next?.Or(value) ?? value, blend);
    }

    private void SampleCharacterDirt(Renderer[] renderers, CharacterDirtKey[] keys)
    {
        var current = CurrentKey(keys, lastFrame);
        if (current == null)
            return;
        var next = NextKey(keys, lastFrame);
        var interpolation = next?._params?.FirstOrDefault();
        float blend = interpolation?._interpolateType > 0 ? interpolation.WeightBetween(current, next, lastFrame) : 0f;
        SetDirtRate(renderers, new Vector4(
            MixFloat(current.DirtEnergyR, next?.DirtEnergyR, 0f, blend),
            MixFloat(current.DirtEnergyG, next?.DirtEnergyG, 0f, blend),
            MixFloat(current.DirtEnergyB, next?.DirtEnergyB, 0f, blend), 0f));
        environment.SetDirtColors(
            MixColor(current.DirtColor, next?.DirtColor, new Color(.35f, .254f, .215f, 1f), blend),
            MixColor(current.DirtRimSpecularColor, next?.DirtRimSpecularColor, new Color(.6f, .451f, .384f, 1f), blend),
            MixColor(current.DirtToonColor, next?.DirtToonColor, new Color(.28f, .19f, .14f, 1f), blend));
    }

    private void SetDirtRate(Renderer[] renderers, Vector4 rates)
    {
        foreach (var renderer in renderers)
        {
            renderer.GetPropertyBlock(characterProperties);
            characterProperties.SetVector(DirtRateId, rates);
            renderer.SetPropertyBlock(characterProperties);
        }
    }

    private static float MixFloat(FloatValue current, FloatValue next, float fallback, float blend)
    {
        var value = current?.Or(fallback) ?? fallback;
        return Mathf.Lerp(value, next?.Or(value) ?? value, blend);
    }

    private static void SetSpringChains(ActorPlayback binding, CharacterKey key, bool restoring)
    {
        var actor = binding.Actor;
        var spring = actor.LiveCySpringController;
        if (spring == null || !actor.EnablePhysics)
            return;
        if (restoring)
        {
            foreach (var state in binding.SpringChainStates)
                if (!state.Value)
                {
                    spring.SetResetLinkCySpringBone(false, state.Key);
                    spring.SetEnableCySpringBone(true, state.Key, false);
                }
            binding.SpringChainStates.Clear();
            return;
        }
        if (key?._targetCySpringNameList == null || key._enableCySpringList == null)
            return;
        int count = Mathf.Min(key._targetCySpringNameList.Length, key._enableCySpringList.Length);
        for (int i = 0; i < count; i++)
        {
            string bone = key._targetCySpringNameList[i];
            if (!string.IsNullOrEmpty(bone))
            {
                bool enabled = key._enableCySpringList[i];
                if (binding.SpringChainStates.TryGetValue(bone, out bool previous) && previous == enabled)
                    continue;
                binding.SpringChainStates[bone] = enabled;
                spring.SetResetLinkCySpringBone(!enabled, bone);
                spring.SetEnableCySpringBone(enabled, bone, false);
            }
        }
    }

    private static void PlaySample(Animator animator, AnimationClip clip, string state, int layer, float time,
        FloatValue head, FloatValue speed, bool loop)
    {
        if (!clip || clip.length <= 0f)
            return;
        float position = time * (speed?.Or(1f) ?? 1f) + (head?.Or(0f) ?? 0f);
        float normalized = position / clip.length;
        animator.Play(state, layer, loop ? Mathf.Repeat(normalized, 1f) : Mathf.Clamp01(normalized));
    }

    private float GetCameraCharacterScale(ActorPlayback binding) =>
        supportOptions == null && Current.Unique ? binding.ModelScale : binding.Actor.Position.localScale.y;

    private void SampleCamera()
    {
        var sheet = Current.Sheet;
        int motionFrame = gatePan != null ? Mathf.Min(lastFrame, gatePanFrame) : lastFrame;
        float motionTime = gatePan != null ? Mathf.Min(time, gatePanFrame / FramesPerSecond) : time;
        GetKeyInterval(Keys(sheet._cameraParamKeys), lastFrame, out var parameters, out var nextParameters);
        if (supportOptions == null)
        {
            var cameraData = camera.GetComponent<Gallop.RenderPipeline.CameraData>();
            cameraData.UseCameraOpaqueTexture = parameters?._useCameraOpaqueTexture == true;
            cameraData.CameraOpaqueTextureSizeRate = parameters?._cameraOpaqueTextureSizeRate?._value ?? 1f;
        }
        var motion = CurrentKey(Keys(sheet._cameraMotionKeys), motionFrame);
        if (motion?._enable != true || !motion._clip)
            return;
        var motionTransform = cameraMotion.transform;
        if (previousCamera != motion)
        {
            motionTransform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            motionTransform.localScale = Vector3.one;
            cameraController["clip_1"] = motion._clip;
            previousCamera = motion;
        }

        bool uniqueCharacterCamera = supportOptions == null && Current.Unique;
        float elapsed = motionTime - motion._frame / FramesPerSecond;
        if (uniqueCharacterCamera)
            elapsed -= motion._motionHeadTime;
        elapsed = Mathf.Max(0f, elapsed) * motion._playSpeed;
        if (!uniqueCharacterCamera && motion._playLength > 0f)
            elapsed = Mathf.Min(elapsed, motion._playLength);
        if (!uniqueCharacterCamera)
            elapsed += motion._motionHeadTime;
        float normalizedTime = motion._clip.length > 0f ? elapsed / motion._clip.length : 0f;
        cameraAnimator.Play("motion_1", 0, Mathf.Clamp01(normalizedTime));
        cameraAnimator.Update(0f);
        var target = CurrentKey(Keys(sheet._cameraTargetCharaKeys), lastFrame);
        int targetSlot = target?._useTargetCharacter == true
            ? target._targetCharacterId : sheet._cameraTargetCharaKeys?._targetCharacterId ?? -1;
        bool hasTarget = cast.TryGetValue(targetSlot, out var targetActor);
        float scale = supportOptions == null && Current.Unique && target?._useTargetCharacter == true &&
            target._individualScale && target._targetCharacterIndices is { Length: > 0 } scaleSlots
            ? scaleSlots.Average(slot => cast.TryGetValue(slot, out var scaleActor)
                ? GetCameraCharacterScale(scaleActor) : 1f)
            : hasTarget ? GetCameraCharacterScale(targetActor) : 1f;
        var offset = parameters?.PositionOffset?.OrZero ?? Vector3.zero;
        var rotation = Quaternion.Euler(parameters?.RotationOffset?.OrZero ?? Vector3.zero);
        if (shake != null && shake.time > 0f &&
            (gatePan == null || lastFrame < gatePan.ShakeStopFrameArray[cards.Count - 1]))
        {
            float shakeTime = time - shakeFrame / FramesPerSecond;
            float strength = Mathf.Clamp01(1f - shakeTime / shake.time) * Mathf.Sin(shakeTime * 80f) * shake.power;
            offset += new Vector3(shake.amountX, shake.amountY, shake.amountZ) * strength;
        }

        // CutInHelper.FollowCharacterCamera places the camera root at the position
        // locator before publishing the motion camera's local transform.
        var shotOffset = Current.Cast == Cast.Result && cast.TryGetValue(Current.ActorSlot, out var main)
            ? main.Offset : Vector3.zero;
        if (gatePan != null)
        {
            int index = cards.Count - 1;
            float progress = Mathf.InverseLerp(gatePanFrame, gatePan.PanFrameArray[index], gatePanPosition);
            shotOffset.x += gatePan.TargetPosArray[index].x * gatePanCurve.Evaluate(progress);
        }
        else if (gateZoom != null && lastFrame >= gateZoomFrame && lastFrame <= gateZoom.EndFrame)
        {
            shotOffset += gateZoom.OffsetPosArray[cards.Count - 1];
        }
        var followPosition = hasTarget ? targetActor.PositionAnimator.transform.localPosition : Vector3.zero;
        var followRotation = hasTarget ? targetActor.PositionAnimator.transform.localRotation : Quaternion.identity;
        shotOffset += support?.CameraOffset(time) ?? Vector3.zero;
        camera.transform.SetLocalPositionAndRotation(shotOffset + followPosition +
            followRotation * (rotation * (motionTransform.localPosition * scale + offset)),
            followRotation * rotation * motionTransform.localRotation * Quaternion.Euler(0f, 180f, 0f));
        camera.fieldOfView = Mathf.Clamp(motionTransform.localScale.x, 1f, 179f);
        if (supportOptions != null)
        {
            camera.nearClipPlane = parameters?._nearClipPlane?.Or(0.1f) ?? 0.1f;
            camera.farClipPlane = parameters?._farClipPlane?.Or(2000f) ?? 2000f;
        }
        else
        {
            var interpolation = Current.Unique && parameters != null
                ? nextParameters?._params?.FirstOrDefault() : null;
            float weight = interpolation?._interpolateType > 0
                ? interpolation.WeightBetween(parameters, nextParameters, lastFrame) : 0f;
            if (parameters?._nearClipPlane != null)
                camera.nearClipPlane = Mathf.Lerp(parameters._nearClipPlane._value,
                    nextParameters?._nearClipPlane?._value ?? parameters._nearClipPlane._value, weight);
            if (parameters?._farClipPlane != null)
                camera.farClipPlane = Mathf.Lerp(parameters._farClipPlane._value,
                    nextParameters?._farClipPlane?._value ?? parameters._farClipPlane._value, weight);
        }
    }

    private void SampleCameraBackground(CameraBackgroundKey[] keys, bool hasOrthoBackground)
    {
        var current = CurrentKey(keys, lastFrame);
        bool useBackgroundCamera = hasOrthoBackground || (supportOptions == null && current != null);
        SetOrthoBackgroundActive(useBackgroundCamera);
        var destination = useBackgroundCamera ? orthoBackgroundCamera : camera;
        destination.backgroundColor = defaultBackgroundColor;
        destination.orthographicSize = useBackgroundCamera ? DefaultBackgroundOrthoSize : defaultOrthoSize;
        if (current == null)
            return;
        var next = NextKey(keys, lastFrame);
        var interpolation = next?._params?.FirstOrDefault();
        if (!(interpolation?._interpolateType > 0))
            next = current;
        float blend = next == current ? 0f : interpolation.WeightBetween(current, next, lastFrame);
        destination.backgroundColor = Color.Lerp(current.BackGroundColor, next.BackGroundColor, blend);
        if (current.IsOrthoSize)
            destination.orthographicSize = next.IsOrthoSize
                ? Mathf.Lerp(current.OrthoSize, next.OrthoSize, blend) : current.OrthoSize;
    }

    private void SetOrthoBackgroundActive(bool active)
    {
        camera.clearFlags = active ? CameraClearFlags.Nothing : defaultClearFlags;
        bool sharesSceneDepth = colorCorrectionExemptCamera && colorCorrectionExemptCamera.enabled;
        camera.allowMSAA = !active && !sharesSceneDepth && defaultAllowMsaa;
        if (!orthoBackgroundCamera && active)
        {
            orthoBackgroundCamera = new GameObject("CutIn_BackgroundCamera").AddComponent<Camera>();
            orthoBackgroundCamera.transform.SetParent(root, false);
            orthoBackgroundCamera.CopyFrom(camera);
            orthoBackgroundCamera.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            orthoBackgroundCamera.ResetWorldToCameraMatrix();
            orthoBackgroundCamera.ResetProjectionMatrix();
            orthoBackgroundCamera.targetTexture = camera.targetTexture;
            orthoBackgroundCamera.aspect = camera.aspect;
            orthoBackgroundCamera.clearFlags = CameraClearFlags.SolidColor;
            orthoBackgroundCamera.orthographic = true;
            orthoBackgroundCamera.nearClipPlane = 0.1f;
            orthoBackgroundCamera.farClipPlane = 100f;
            orthoBackgroundCamera.cullingMask = 1 << OrthoBackgroundLayer;
            orthoBackgroundCamera.depth = camera.depth - 1f;
            orthoBackgroundCamera.useOcclusionCulling = false;
            orthoBackgroundCamera.gameObject.AddComponent<Gallop.RenderPipeline.CameraData>();
            var settings = orthoBackgroundCamera.GetUniversalAdditionalCameraData();
            settings.renderPostProcessing = false;
            settings.renderShadows = false;
        }
        if (orthoBackgroundCamera)
            orthoBackgroundCamera.enabled = active;
    }

    private void SamplePostEffects()
    {
        timelineFade = Color.clear;
        ref var bloom = ref post.DofDiffuionBloomOverlay;
        bloom.IsEnableDof = false;
        bloom.IsEnableBloom = false;
        bloom.IsEnableDiffusion = false;
        bloom.Overlay1 = ScreenOverlayRender.Parameter.Default();
        bloom.Overlay2 = ScreenOverlayRender.Parameter.Default();
        bloom.Overlay3 = ScreenOverlayRender.Parameter.Default();
        post.ColorCorrection.Enabled = false;
        splitColorCorrection = false;
        colorCorrectionExemptActors = 0;
        post.RadialBlur.Type = 0;
        foreach (var track in Current.Sheet._postEffectList ?? Array.Empty<NamedTrack<PostEffectKey>>())
        {
            var keys = Keys(track._keys);
            var key = CurrentKey(keys, lastFrame);
            if (key == null)
                continue;
            var next = NextKey(keys, lastFrame) ?? key;
            float weight = next == key ? 0f :
                supportOptions == null && Current.Unique
                    ? Interpolation.NativeWeightBetween(key, next, next._params, 0, lastFrame)
                    : TrackWeight(key, next, next._params, 0);
            string name = EffectName(track._name).Trim();
            float gateRelease = 0f;
            if (gatePan != null && next._frame == gatePan.SkipForEndFrame)
            {
                float end = gatePan.PanFrameArray[cards.Count - 1];
                gateRelease = Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(end - GateExposureReleaseFrames, end, gatePanPosition));
            }
            switch (name)
            {
                case "Fade":
                    if (key._fadeEnable)
                        timelineFade = Color.Lerp(key._fadeColor, next._fadeColor, weight);
                    break;
                case "Bloom":
                    bloom.IsEnableBloom = key._bloomEnable;
                    bloom.IsEnableDiffusion = key._bloomEnable && key._diffusion;
                    bloom.BloomBlurSize = Mathf.Lerp(key._bloomBlurSize, next._bloomBlurSize, weight);
                    bloom.BloomIntensity = Mathf.Lerp(key._bloomIntensity, next._bloomIntensity, weight);
                    bloom.BloomThreshold = Mathf.Lerp(key._bloomThreshHold, next._bloomThreshHold, weight);
                    bloom.BloomDofWeight = Mathf.Lerp(key._bloomDofWeight, next._bloomDofWeight, weight);
                    bloom.BloomBlendMode = (DofDiffusionBloomOverlayParam.BloomScreenBlendMode)key.BloomBlendMode;
                    if (bloom.IsEnableDiffusion)
                    {
                        bloom.DiffusionContrast = Mathf.Lerp(key._diffusionContrast, next._diffusionContrast, weight);
                        bloom.DiffusionSaturation = Mathf.Lerp(key._diffusionSaturation, next._diffusionSaturation, weight);
                        bloom.DiffusionBright = Mathf.Lerp(key._diffusionBright, next._diffusionBright, weight);
                        bloom.DiffusionBlurSize = Mathf.Lerp(key._diffusionBlurSize, next._diffusionBlurSize, weight);
                        bloom.DiffusionThreshold = Mathf.Lerp(key._diffusionThreshold, next._diffusionThreshold, weight);
                    }
                    break;
                case "PostFilm":
                    ApplyPostFilm(ref bloom.Overlay1, key, next, 0, weight, gateRelease);
                    ApplyPostFilm(ref bloom.Overlay2, key, next, 1, weight, gateRelease);
                    ApplyPostFilm(ref bloom.Overlay3, key, next, 2, weight, gateRelease);
                    break;
                case "ColorCorrection":
                    post.ColorCorrection.Enabled = key._colorCorrectionEnable &&
                        (supportOptions == null || !key.IsColorCorrectionIgnoreDraw);
                    if (supportOptions == null && key._colorCorrectionEnable)
                    {
                        colorCorrectionExemptActors = key.ColorCorrectionIgnoreCharacterFlags;
                        splitColorCorrection = key.IsColorCorrectionIgnoreDraw || colorCorrectionExemptActors != 0;
                    }
                    post.ColorCorrection.Red = key._colorCorrectionRedChannel;
                    post.ColorCorrection.Green = key._colorCorrectionGreenChannel;
                    post.ColorCorrection.Blue = key._colorCorrectionBlueChannel;
                    post.ColorCorrection.Saturation = Mathf.Lerp(key._colorCorrectionSaturation,
                        next._colorCorrectionSaturation, weight);
                    break;
                case "RadialBlur":
                    ref var blur = ref post.RadialBlur;
                    blur.Type = key.MoveBlurType;
                    blur.Downsample = key.RadialBlurDownsample;
                    blur.Iteration = key.RadialBlurIteration;
                    blur.Offset = Vector2.Lerp(key.RadialBlurOffset, next.RadialBlurOffset, weight);
                    blur.EllipseDir = Vector2.Lerp(key.RadialBlurEllipseDir, next.RadialBlurEllipseDir, weight);
                    blur.StartArea = Mathf.Lerp(key.RadialBlurStartArea, next.RadialBlurStartArea, weight);
                    blur.EndArea = Mathf.Lerp(key.RadialBlurEndArea, next.RadialBlurEndArea, weight);
                    blur.Power = Mathf.Lerp(key.RadialBlurPower, next.RadialBlurPower, weight);
                    blur.RollEulerAngles = Mathf.Lerp(key.RadialBlurRollEulerAngles, next.RadialBlurRollEulerAngles, weight);
                    if (supportOptions == null)
                    {
                        blur.IsEnabledDepth = key.IsEnabledDepth;
                        blur.IsEnabledDepthCancelRect = key.IsEnabledDepthCancelRect;
                        blur.IsExpandDepthCancelRect = key.IsExpandDepthCancelRect;
                        blur.DepthPowerFront = Mathf.LerpUnclamped(key.DepthPowerFront, next.DepthPowerFront, weight);
                        blur.DepthPowerBack = Mathf.LerpUnclamped(key.DepthPowerBack, next.DepthPowerBack, weight);
                        blur.DepthCancelBlendLength = Mathf.LerpUnclamped(key.DepthCancelBlendLength, next.DepthCancelBlendLength, weight);
                        blur.DepthCancelRect = Vector4.Lerp(key.DepthCancelRect, next.DepthCancelRect, weight);
                    }
                    break;
                default:
                    SampleScenePostEffect(name, key, next, name == "GlobalFog" ? Mathf.Max(weight, gateRelease) : weight);
                    break;
            }
        }
    }

    private static void ApplyPostFilm(ref ScreenOverlayRender.Parameter overlay, PostEffectKey key,
        PostEffectKey next, int index, float weight, float gateRelease)
    {
        var source = key._postFilmParam?.ElementAtOrDefault(index);
        if (source == null || source._blendMode == 0)
            return;
        var target = next._postFilmParam?.ElementAtOrDefault(index) ?? source;
        if (source._blendMode == target._blendMode)
            weight = Mathf.Max(weight, gateRelease);
        overlay.PostFilmMode = (ScreenOverlay.Overlay.PostFilmMode)source._blendMode;
        overlay.InverseVignette = source._inverseVignette;
        overlay.DepthPower = Mathf.LerpUnclamped(source._depthPower, target._depthPower, weight);
        overlay.DepthClip = Mathf.LerpUnclamped(source.DepthClip, target.DepthClip, weight);
        overlay.PostFilmPower = Mathf.LerpUnclamped(source._postFilmPower, target._postFilmPower, weight);
        if (source._blendMode != target._blendMode)
            overlay.PostFilmPower *= 1f - gateRelease;
        overlay.PostFilmOffsetParam = Vector2.LerpUnclamped(source._postFilmOffsetParam, target._postFilmOffsetParam, weight);
        overlay.PostFilmOptionParam = Vector4.LerpUnclamped(source._postFilmOptionParam, target._postFilmOptionParam, weight);
        overlay.SetRollAngle(Mathf.LerpUnclamped(source.RollAngle, target.RollAngle, weight));
        overlay.SetScale(Vector2.LerpUnclamped(source.FilmScale, target.FilmScale, weight));
        overlay.PostFilmColor0 = Color.LerpUnclamped(source._color[0], target._color[0], weight);
        overlay.PostFilmColor1 = Color.LerpUnclamped(source._color[1], target._color[1], weight);
        overlay.PostFilmColor2 = Color.LerpUnclamped(source._color[2], target._color[2], weight);
        overlay.PostFilmColor3 = Color.LerpUnclamped(source._color[3], target._color[3], weight);
    }

    internal static void GetKeyInterval<T>(T[] keys, int frame, out T current, out T next) where T : Key
    {
        current = CurrentKey(keys, frame);
        next = NextKey(keys, frame);
    }

    private static T NextKey<T>(T[] keys, int frame) where T : Key
    {
        foreach (var key in keys)
            if (key._frame > frame)
                return key;
        return null;
    }

    private void BindBackgroundTimeMaterials(VisualTrack<BackgroundKey> track)
    {
        var timedMaterials = new List<Material>();
        foreach (var renderer in track.Renderers)
        {
            if (!(renderer is MeshRenderer || renderer is SkinnedMeshRenderer) || renderer.GetComponent<PoolWater>())
                continue;
            var originals = renderer.sharedMaterials;
            if (!originals.Any(material => material && material.HasProperty(BackgroundTimeId)))
                continue;

            // Use the stage's material instances, as CutInBgModel does. This also
            // avoids competing with components that set renderer property blocks.
            var instances = renderer.materials;
            for (int i = 0; i < instances.Length; ++i)
            {
                var material = instances[i];
                if (!material)
                    continue;
                if (material != originals[i])
                {
                    if (originals[i])
                        material.name = originals[i].name;
                    ownedBackgroundTimeMaterials.Add(material);
                }
                if (material.HasProperty(BackgroundTimeId))
                    timedMaterials.Add(material);
            }
        }
        track.BackgroundTimeMaterials = timedMaterials.ToArray();
    }

    private void ClearBackgroundTimeMaterials()
    {
        foreach (var material in ownedBackgroundTimeMaterials)
            Object.Destroy(material);
        ownedBackgroundTimeMaterials.Clear();
    }

    private bool SampleBackgrounds()
    {
        bool hasOrthoBackground = false;
        int? sampleFrame = supportOptions == null && Current.Unique ? lastFrame : null;
        foreach (var track in backgrounds)
        {
            var key = CurrentKey(track.Keys, lastFrame);
            if (sampleFrame.HasValue && key != null)
                SampleBackgroundChildVisibility(track, key);
            if (key != null && track.BackgroundTimeMaterials != null)
                foreach (var material in track.BackgroundTimeMaterials)
                    material.SetFloat(BackgroundTimeId, time);
            track.Instance.SetActive(key?._visible == true);
            if (key == null)
                continue;
            if (key._visible && supportOptions == null)
            {
                bool ortho = key.LayerChange?._isValid == true ? key.LayerChange._intValue == 5 : key.IsOrtho;
                int layer = ortho ? OrthoBackgroundLayer : PreviewLayer;
                if (track.Instance.layer != layer)
                    SetLayer(track.Instance, layer);
                hasOrthoBackground |= ortho;
            }

            // Effects can read a locator even when its stage is inactive.
            if (key._visible || sampleFrame.HasValue)
                SampleBackgroundTransform(track, key, sampleFrame);

            if (!key._visible)
            {
                if (sampleFrame.HasValue && track.Mirrors.Length > 0)
                {
                    SampleBackgroundMirrors(track, key, sampleFrame);
                    track.Previous = key;
                }
                continue;
            }

            SampleBackgroundAnimation(track, key);
            SampleBackgroundColor(track);
            SampleBackgroundMaskingFade(track, key, sampleFrame);
            SampleBackgroundWater(track, key, sampleFrame);
            if (track.GrassMaterials != null)
                SampleGrass(track, key, sampleFrame);
            if (supportOptions == null && track.Mirrors.Length > 0)
                SampleBackgroundMirrors(track, key, sampleFrame);

            if (!sampleFrame.HasValue && key != track.Previous)
                SampleBackgroundChildVisibility(track, key);
            track.Previous = key;
        }

        foreach (var gate in gates)
            if (gate.OpeningFrame >= 0 && gate.ActiveDoor)
                Sample(loader.OpenGateClip, gate.ActiveDoor, time - gate.OpeningFrame / FramesPerSecond);
        support?.Sample(time * FramesPerSecond, camera);
        return hasOrthoBackground;
    }

    private void SampleBackgroundTransform(VisualTrack<BackgroundKey> track, BackgroundKey key, int? sampleFrame)
    {
        var transform = track.Instance.transform;
        GetBackgroundSpatialKeys(track.Keys, key, candidate => candidate._usePosition, 0, out var from, out var to);
        if (from != null)
            transform.localPosition = Vector3.LerpUnclamped(from._pos, to?._pos ?? from._pos,
                BackgroundWeight(from, to, 0, sampleFrame, time));
        else if (supportOptions == null)
            transform.localPosition = track.LocalPosition;

        // Only the native position callback forwards the timeline time.
        GetBackgroundSpatialKeys(track.Keys, key, candidate => candidate._useRotation, 1, out from, out to);
        if (from != null)
        {
            var target = to?._rot ?? from._rot;
            float weight = BackgroundWeight(from, to, 1, sampleFrame);
            transform.localRotation = supportOptions == null
                ? Quaternion.Euler(Vector3.LerpUnclamped(from._rot, target, weight))
                : Quaternion.SlerpUnclamped(Quaternion.Euler(from._rot), Quaternion.Euler(target), weight);
        }
        else if (supportOptions == null)
            transform.localRotation = track.LocalRotation;

        GetBackgroundSpatialKeys(track.Keys, key, candidate => candidate._useScale, 2, out from, out to);
        if (from != null)
            transform.localScale = Vector3.LerpUnclamped(from._scale, to?._scale ?? from._scale,
                BackgroundWeight(from, to, 2, sampleFrame));
        else if (supportOptions == null)
            transform.localScale = track.LocalScale;
    }

    private float BackgroundWeight(BackgroundKey from, BackgroundKey to, int channel, int? sampleFrame,
        float interpolationTime = 0f)
    {
        return sampleFrame.HasValue
            ? Interpolation.NativeWeightBetween(from, to, to?._params, channel, sampleFrame.Value, interpolationTime)
            : TrackWeight(from, to, to?._params, channel);
    }

    private static void SampleBackgroundChildVisibility(VisualTrack<BackgroundKey> track, BackgroundKey key)
    {
        if (key._childVisibilityArray is not { Length: > 0 })
            return;
        track.BackgroundChildren ??= new Dictionary<string, Transform>(StringComparer.Ordinal);
        foreach (var child in key._childVisibilityArray)
        {
            if (child == null || string.IsNullOrEmpty(child.GameObjectName))
                continue;
            if (!track.BackgroundChildren.TryGetValue(child.GameObjectName, out var node) || !node)
            {
                node = FindNode(track.Instance.transform, child.GameObjectName);
                if (!node)
                    continue;
                track.BackgroundChildren[child.GameObjectName] = node;
            }
            if (node.gameObject.activeSelf != child.IsVisible)
                node.gameObject.SetActive(child.IsVisible);
        }
    }

    private void SampleBackgroundAnimation(VisualTrack<BackgroundKey> track, BackgroundKey current)
    {
        BackgroundKey clipStart = null;
        FloatValue head = null, speed = null;
        foreach (var key in track.Keys)
        {
            if (key._frame > lastFrame)
                break;
            if (key._isValidClip)
                clipStart = key;
            if (key._motionHeadTime?._isValid == true)
                head = key._motionHeadTime;
            if (key._playSpeed?._isValid == true)
                speed = key._playSpeed;
        }
        if (clipStart == null)
            return;

        float elapsed = RelativeTime(clipStart);
        if (supportOptions != null)
            Sample(clipStart._clip, track.Instance, elapsed, head, speed, current.IsTimeAnimationLoop);
        else
            Sample(clipStart._clip, track.Instance, (elapsed - (head?.Or(0f) ?? 0f)) * (speed?.Or(1f) ?? 1f),
                loop: Current.Unique ? clipStart._clip && clipStart._clip.isLooping : current.IsTimeAnimationLoop);
    }


    private void SampleGrass(VisualTrack<BackgroundKey> track, BackgroundKey key, float? sampleFrame)
    {
        var grass = key.GrassList;
        if (grass == null || grass.Length == 0)
            return;
        var nextKey = NextKey(track.Keys, lastFrame);
        var nextGrass = nextKey?.GrassList;
        float blend = nextGrass?.Length == grass.Length
            ? Mathf.Clamp01(TrackWeight(key, nextKey, nextKey._params, 5, sampleFrame)) : 0f;
        float duration = (lastFrame - key.GrassBaseFrame) / FramesPerSecond;

        for (int i = 0; i < grass.Length; i++)
        {
            var from = grass[i];
            if (from == null || !from.TargetMaterial)
                continue;
            var to = blend > 0f ? nextGrass[i] ?? from : from;
            var wind = Vector2.LerpUnclamped(from.WindParam, to.WindParam, blend);
            float windTime = duration * Mathf.LerpUnclamped(from.BaseTimeScale, to.BaseTimeScale, blend);
            float scale = Mathf.LerpUnclamped(from.WindScale, to.WindScale, blend);
            foreach (var (renderer, slot, material) in track.GrassMaterials)
            {
                if (material != from.TargetMaterial && material.name != from.TargetMaterial.name)
                    continue;
                // A per-material block supersedes the renderer block, so preserve the
                // fill and environment values already applied to this renderer.
                renderer.GetPropertyBlock(effectProperties);
                effectProperties.SetVector(GrassWindParamId, new Vector4(wind.x, wind.y, windTime, windTime));
                effectProperties.SetFloat(GrassWindScaleId, scale);
                if (from.WindDirTexture)
                    effectProperties.SetTexture(GrassWindTextureId, from.WindDirTexture);
                renderer.SetPropertyBlock(effectProperties, slot);
            }
        }
    }

    private void SampleBackgroundMirrors(VisualTrack<BackgroundKey> track, BackgroundKey key, float? sampleFrame)
    {
        // TimelineBackGroundData.AlterUpdateMirrorReflection uses separate
        // interpolation channels for the clip plane, rate and distortion.
        var clip = SampleMirrorScalar(track.Keys, key, candidate => candidate.MirrorClipPlaneOffset, 6, sampleFrame);
        var rate = SampleMirrorScalar(track.Keys, key, candidate => candidate.MirrorReflectionRate, 7, sampleFrame);
        var distortionTile = SampleMirrorDistortion(track.Keys, key, candidate => candidate.MirrorDistortionTileOffset, 9, sampleFrame);
        var distortionPower = SampleMirrorDistortion(track.Keys, key, candidate => candidate.MirrorDistortionPower, 10, sampleFrame);

        foreach (var mirror in track.Mirrors)
        {
            if (clip.HasValue)
                mirror.MirrorClipPlaneOffset = clip.Value;
            if (rate.HasValue)
                mirror.MirrorReflectionRate = rate.Value;
            // An unset distortion value selects the material default for this
            // shot. Clip-plane and rate keys retain their previous values.
            var tile = distortionTile ?? mirror.GetDefaultMirrorDistortionMapTileOffset();
            var power = distortionPower ?? mirror.GetDefaultMirrorDistortionPower();
            if (mirror.MirrorDistortionTileOffset != tile)
                mirror.MirrorDistortionTileOffset = tile;
            if (mirror.MirrorDistortionPower != power)
                mirror.MirrorDistortionPower = power;
            if (key != track.Previous)
                mirror.SetCutInRenderOptions(key.IsToonMirror, key.IsEnableMirrorBlur,
                    key.MirrorBlurDownSample, key.IsEnableMirrorMsaa);
        }
    }

    private float? SampleMirrorScalar(BackgroundKey[] keys, BackgroundKey current,
        Func<BackgroundKey, FloatValue> channelValue, int channel, float? sampleFrame)
    {
        bool UsesChannel(BackgroundKey key) => channelValue(key)?._isValid == true;

        BackgroundKey from, to;
        if (sampleFrame.HasValue)
            GetBackgroundSpatialKeys(keys, current, UsesChannel, channel, out from, out to);
        else
            GetBackgroundTransformKeys(keys, UsesChannel, out from, out to);

        if (from == null)
            return null;

        float value = channelValue(from)._value;
        return Mathf.LerpUnclamped(value, to == null ? value : channelValue(to)._value,
            TrackWeight(from, to, to?._params, channel, sampleFrame));
    }

    private Vector4? SampleMirrorDistortion(BackgroundKey[] keys, BackgroundKey current,
        Func<BackgroundKey, Vector4Value> channelValue, int channel, float? sampleFrame)
    {
        GetBackgroundTransformKeys(keys, key => channelValue(key)?._isValid == true, out var from, out var to);
        if (from != null && to?._params?.ElementAtOrDefault(channel)?._interpolateType > 0)
            return Vector4.LerpUnclamped(channelValue(from)._value, channelValue(to)._value,
                TrackWeight(from, to, to._params, channel, sampleFrame));

        var value = channelValue(current);
        return value?._isValid == true ? value._value : (Vector4?)null;
    }

    private void SampleBackgroundColor(VisualTrack<BackgroundKey> track)
    {
        GetBackgroundTransformKeys(track.Keys, key => key._useFillColor, out var fill, out var nextFill);
        GetBackgroundTransformKeys(track.Keys, key => key._useAddColor, out var add, out var nextAdd);
        if (fill == null && add == null)
            return;
        // Background tracks select keys on the 30 fps timeline.
        int? sampleFrame = supportOptions == null && Current.Unique ? lastFrame : null;
        var fillColor = fill != null
            ? Color.LerpUnclamped(fill._fillColor, nextFill?._fillColor ?? fill._fillColor,
                BackgroundWeight(fill, nextFill, 3, sampleFrame)) : Color.white;
        var addColor = add != null
            ? Color.LerpUnclamped(add._addColor, nextAdd?._addColor ?? add._addColor,
                BackgroundWeight(add, nextAdd, 4, sampleFrame)) : Color.clear;
        foreach (var renderer in track.Renderers)
        {
            if (!(renderer is MeshRenderer || renderer is SkinnedMeshRenderer))
                continue;
            renderer.GetPropertyBlock(effectProperties);
            if (fill != null)
            {
                effectProperties.SetColor(BackgroundMulColorId, fillColor);
                effectProperties.SetColor(EffectColorId, fillColor);
                effectProperties.SetColor(BackgroundAmbientColorId, fillColor);
            }
            if (add != null)
                effectProperties.SetColor(BackgroundAddColorId, addColor);
            renderer.SetPropertyBlock(effectProperties);
        }
        if (fill != null)
            foreach (var mirror in track.Mirrors)
                mirror.MirrorReflectionColor = fillColor;
    }

    private void SampleBackgroundMaskingFade(VisualTrack<BackgroundKey> track, BackgroundKey key, int? sampleFrame)
    {
        GetBackgroundSpatialKeys(track.Keys, key, candidate => candidate.UseMaskingFade, 8,
            out var from, out var to);
        if (from == null)
            return;

        float fadeRate = to?._params?.ElementAtOrDefault(8)?._interpolateType > 0
            ? Mathf.LerpUnclamped(from.MaskingFadeRate, to.MaskingFadeRate,
                BackgroundWeight(from, to, 8, sampleFrame))
            : from.MaskingFadeRate;
        foreach (var renderer in track.Renderers)
        {
            renderer.GetPropertyBlock(effectProperties);
            effectProperties.SetFloat(BackgroundMaskingFadeId, fadeRate);
            renderer.SetPropertyBlock(effectProperties);
        }
    }

    private void SampleBackgroundWater(VisualTrack<BackgroundKey> track, BackgroundKey key, int? sampleFrame)
    {
        ApplyWaterValue(track, key, sampleFrame, candidate => candidate.PoolWaterClipPlaneOffset, 11,
            (water, value) => water.clipPlaneOffset = value);
        ApplyWaterValue(track, key, sampleFrame, candidate => candidate.PoolWaterPlaneOffsetY, 12,
            (water, value) => water.waterPlaneOffsetY = value);
        ApplyWaterValue(track, key, sampleFrame, candidate => candidate.PoolWaterWaveScale, 16,
            (water, value) => water.waveScale = value);
        ApplyWaterValue(track, key, sampleFrame, candidate => candidate.PoolWaterDistanceFadeByNear, 13,
            (water, value) => water.distanceFadeByNear = value);
        ApplyWaterValue(track, key, sampleFrame, candidate => candidate.PoolWaterDistanceFadeByFar, 14,
            (water, value) => water.distanceFadeByFar = value);
        ApplyWaterValue(track, key, sampleFrame, candidate => candidate.PoolWaterDistanceFadeByAlpha, 15,
            (water, value) => water.distanceFadeByAlpha = value);
    }

    private void ApplyWaterValue(VisualTrack<BackgroundKey> track, BackgroundKey key, int? sampleFrame,
        Func<BackgroundKey, FloatValue> channelValue, int channel, Action<PoolWater, float> setter)
    {
        GetBackgroundSpatialKeys(track.Keys, key, candidate => channelValue(candidate)?._isValid == true,
            channel, out var from, out var to);
        if (from == null)
            return;

        float value = to?._params?.ElementAtOrDefault(channel)?._interpolateType > 0
            ? Mathf.LerpUnclamped(channelValue(from)._value, channelValue(to)._value,
                BackgroundWeight(from, to, channel, sampleFrame))
            : channelValue(from)._value;
        foreach (var water in track.Water)
            setter(water, value);
    }

    private void GetBackgroundSpatialKeys(BackgroundKey[] keys, BackgroundKey current, Func<BackgroundKey, bool> usesChannel,
        int channel, out BackgroundKey from, out BackgroundKey to)
    {
        GetBackgroundTransformKeys(keys, usesChannel, out from, out to);
        if (supportOptions != null || (from != null && to?._params?.ElementAtOrDefault(channel)?._interpolateType > 0))
            return;

        from = usesChannel(current) ? current : null;
        to = null;
    }

    private void GetBackgroundTransformKeys(BackgroundKey[] keys, Func<BackgroundKey, bool> usesChannel,
        out BackgroundKey from, out BackgroundKey to)
    {
        from = to = null;
        foreach (var key in keys)
        {
            if (!usesChannel(key))
                continue;
            if (key._frame > lastFrame)
            {
                to = key;
                return;
            }
            from = key;
        }
    }

    private bool SampleEffects(float elapsed)
    {
        bool hasWorldOverlay = false;
        bool hasUiOverlay = false;
        bool hasScreenEffect = false;
        bool hasOrthoEffect = false;
        bool hasColorExempt = colorCorrectionExemptActors != 0;
        SampleCommentFlash();

        foreach (var track in effects)
            track.Sampled = false;
        foreach (var track in effects)
        {
            SampleEffect(track, elapsed);
            if (!track.Instance.activeSelf)
                continue;
            hasWorldOverlay |= track.Instance.layer == EffectOverlayLayer;
            hasUiOverlay |= track.Instance.layer == EffectUiLayer;
            hasScreenEffect |= track.Instance.layer == EffectScreenLayer;
            hasOrthoEffect |= track.Instance.layer == OrthoBackgroundLayer;
            hasColorExempt |= track.Instance.layer == ColorCorrectionExemptLayer;
        }
        props?.SampleAfterEffects(lastFrame, time);
        hasColorExempt |= props?.HasColorCorrectionExemptProps == true;
        if (splitColorCorrection)
            camera.cullingMask &= ~(1 << ColorCorrectionExemptLayer);
        else
            camera.cullingMask |= 1 << ColorCorrectionExemptLayer;
        SampleEffectCameras(hasWorldOverlay, hasUiOverlay, hasScreenEffect, hasColorExempt);
        return hasOrthoEffect;
    }

    private void SampleCommentFlash()
    {
        foreach (var track in Current.Sheet._effectList ?? Array.Empty<EffectGroup>())
        {
            if (track._keys?.IsFlash != true ||
                track._keys.FlashName?.Replace('\\', '/').EndsWith("/pf_fl_gacha_chara_comment00", StringComparison.OrdinalIgnoreCase) != true)
                continue;
            var key = CurrentKey(Keys(track._keys), lastFrame);
            if (key == null || key == previousCommentKey)
                continue;
            previousCommentKey = key;
            if (key._stop || key._hide)
                flash?.Clear();
            else if (key._play && flash?.PlayComment(key.PlayLabel) == true)
                audio.PlayEffect("snd_sfx_gacha_003");
        }
    }

    private void SampleEffect(VisualTrack<EffectKey> track, float elapsed)
    {
        if (track.Sampled)
            return;
        track.Sampled = true;
        var key = CurrentKey(track.Keys, lastFrame);
        if (key == null)
            return;
        bool changed = key != track.Previous;
        var next = NextKey(track.Keys, lastFrame);
        var transform = track.Instance.transform;
        bool useNativeEffectKeys = supportOptions == null && Current.Unique;
        // Native timeline offsets start at zero/identity without a channel key or interpolation.
        // Imported effect roots can carry a rotation already authored into their child hierarchy.
        var position = useNativeEffectKeys ? Vector3.zero : track.LocalPosition;
        var rotation = useNativeEffectKeys ? Quaternion.identity : track.LocalRotation;
        var scale = useNativeEffectKeys ? Vector3.one : track.LocalScale;
        var localRotation = key._useRotation && key._useLocalRotation
            ? Quaternion.Euler(key._localRot) : Quaternion.identity;
        var offsetRotation = key._useRotation ? Quaternion.Euler(key._rot) : Quaternion.identity;
        if (key._usePosition)
        {
            position = key._pos;
            if (!useNativeEffectKeys)
                track.LocalPosition = position;
        }
        if (key._useRotation) rotation = track.LocalRotation = offsetRotation;
        if (key._useScale) scale = track.LocalScale = key._scale;
        scale = EffectScale(key, scale);
        if (useNativeEffectKeys)
        {
            if (TryGetEffectInterpolation(track.Keys, candidate => candidate._usePosition, 0, out var from, out var to))
            {
                float weight = Interpolation.NativeWeightBetween(from, to, to._params, 0, lastFrame, time);
                position = Vector3.LerpUnclamped(from._pos, to._pos, weight);
            }
            if (TryGetEffectInterpolation(track.Keys, candidate => candidate._useRotation, 1, out from, out to))
            {
                float weight = Interpolation.NativeWeightBetween(from, to, to._params, 1, lastFrame, time);
                rotation = offsetRotation = Quaternion.Euler(Vector3.LerpUnclamped(from._rot, to._rot, weight));
                localRotation = Quaternion.Euler(Vector3.LerpUnclamped(from._localRot, to._localRot, weight));
            }
            if (TryGetEffectInterpolation(track.Keys, candidate => candidate._useScale, 2, out from, out to))
                scale = Vector3.LerpUnclamped(EffectScale(from, from._scale), EffectScale(to, to._scale),
                    Interpolation.NativeWeightBetween(from, to, to._params, 2, lastFrame, time));
        }
        else
        {
            if (key._usePosition && next?._usePosition == true)
                position = Vector3.LerpUnclamped(position, next._pos, TrackWeight(key, next, next._params, 0));
            if (key._useRotation && next?._useRotation == true)
                rotation = Quaternion.SlerpUnclamped(rotation, Quaternion.Euler(next._rot), TrackWeight(key, next, next._params, 1));
            if (key._useScale && next?._useScale == true)
                scale = Vector3.LerpUnclamped(scale, EffectScale(next, next._scale), TrackWeight(key, next, next._params, 2));
        }
        UpdateEffectAttachment(track);
        var (anchor, parentEffect) = track.EffectAttachment;
        bool applyCameraAttachmentAfterMotion = useNativeEffectKeys && anchor == camera.transform && key._clip;
        if (anchor)
        {
            if (parentEffect != null)
                SampleEffect(parentEffect, elapsed);
            var attachment = track.EffectAttachmentKey;
            if ((supportOptions != null || !Current.Unique) &&
                TryGetEffectInterpolation(track.Keys, candidate => candidate._useRotation, 1, out var from, out var to))
            {
                float weight = TrackWeight(from, to, to._params, 1);
                offsetRotation = Quaternion.Euler(Vector3.LerpUnclamped(from._rot, to._rot, weight));
                localRotation = Quaternion.Euler(Vector3.LerpUnclamped(from._localRot, to._localRot, weight));
            }
            rotation = offsetRotation;
            var anchorRotation = anchor == camera.transform
                ? anchor.rotation * Quaternion.Euler(0f, 180f, 0f)
                : anchor.rotation;
            var attachedRotation = anchorRotation * rotation;
            if (attachment._attachUsePos)
            {
                var origin = anchor.position;
                var anchorOffset = new Vector3(attachment._attachUsePosX ? origin.x : 0f,
                    attachment._attachUsePosY ? origin.y : 0f, attachment._attachUsePosZ ? origin.z : 0f);
                position = transform.parent.InverseTransformPoint(anchorOffset +
                    attachedRotation * transform.parent.TransformVector(position));
            }
            if (attachment._attachUseRot)
                rotation = Quaternion.Inverse(transform.parent.rotation) * attachedRotation;
            rotation *= localRotation;
            if (attachment._attachUseScale)
                scale = Vector3.Scale(scale, anchor.lossyScale);
        }
        if (key._useUiLayer)
            position = AlignScreenEffect(transform.parent, position, key.ScreenAlignmentValue);
        transform.SetLocalPositionAndRotation(position, rotation);
        transform.localScale = scale;

        if (changed)
        {
            ApplyEffectRenderRouting(track, key);
            if (!useNativeEffectKeys)
                ApplyEffectMaterialState(track, key, false);
        }

        float cursorTime = time - elapsed;
        var particleKey = track.Previous;
        while (track.NextParticleKey < track.Keys.Length)
        {
            var transition = track.Keys[track.NextParticleKey];
            if (transition._frame > lastFrame)
                break;
            track.NextParticleKey++;
            float transitionTime = Mathf.Clamp(transition._frame / FramesPerSecond, cursorTime, time);
            AdvanceEffectParticles(track, particleKey, cursorTime, transitionTime);
            if (useNativeEffectKeys)
            {
                if (transition.IsPause)
                    track.ParticlePauseFrame ??= transition._frame;
                else if (track.ParticlePauseFrame is int pauseFrame)
                {
                    track.ParticlePausedFrames += transition._frame - pauseFrame;
                    track.ParticlePauseFrame = null;
                }
            }
            if (transition._hide)
                track.Instance.SetActive(false);
            else if (transition._stop)
                SetEffectEmission(track, false);
            else if (transition._play)
            {
                track.ParticlePlayKey = transition;
                track.ParticlePlaybackTime = 0f;
                track.Instance.SetActive(true);
                SetEffectEmission(track, true);
                if (useNativeEffectKeys && track.ParticlePauseFrame is int playPauseFrame)
                {
                    float restartTime = (playPauseFrame - transition._frame - track.ParticlePausedFrames) / FramesPerSecond;
                    if (transition.IsPlaySimulateTime || (transition.IsUseFixedTimeForShort && restartTime == 0f))
                        restartTime = EffectParticleMinimumStartTime;
                    track.ParticlePlaybackTime = restartTime;
                    SimulateEffectParticles(track, restartTime, true, useParticleStartTime: true);
                }
                else
                {
                    track.ParticlePausedFrames = 0;
                    SimulateEffectParticles(track, transition.IsPause ? 0f : .0001f, true);
                }
            }
            if (useNativeEffectKeys)
                ApplyEffectMaterialState(track, transition, true);
            particleKey = transition;
            cursorTime = transitionTime;
        }
        AdvanceEffectParticles(track, particleKey, cursorTime, time);

        if (useNativeEffectKeys && !track.Instance.activeSelf && track.ParticlePlayKey != null)
            SampleEffectOutline(track, key);
        if (track.Instance.activeSelf)
        {
            ApplyEffectColor(track, key, next);
            SampleEffectOutline(track, key);
            // Property-only keys keep the current motion playing. Restarting the clock
            // at every key rewinds 102102's bell at frames 49, 123 and 136.
            int motionStartFrame = useNativeEffectKeys ? track.ParticlePlayKey?._frame ?? key._frame : key._frame;
            float clipTime = EffectElapsed(track, motionStartFrame / FramesPerSecond, time);
            if (supportOptions == null && key._clip &&
                (key._motionHeadTime?._isValid == true || key._playLength?._isValid == true))
            {
                int playFrame = track.ParticlePlayKey?._frame ?? key._frame;
                clipTime = key._motionHeadTime?._isValid == true
                    ? time - playFrame / FramesPerSecond - key._motionHeadTime._value
                    : EffectElapsed(track, playFrame / FramesPerSecond, time);
                if (key._playLength?._isValid == true)
                {
                    float playLength = key._playLength._value;
                    clipTime = playLength == 0f
                        ? (clipTime > 0f ? key._clip.length : 0f)
                        : clipTime / playLength * key._clip.length;
                }
            }
            Sample(key._clip, track.Instance, clipTime);
            if (useNativeEffectKeys && track.ParticlePlayKey is { } playKey && track.Animators.Length > 0)
            {
                float effectTime = track.ParticlePauseFrame is int pauseFrame ? pauseFrame / FramesPerSecond : time;
                if (playKey.IsUseFixedTime || playKey.IsUseFixedTimeForShort)
                    effectTime = EffectTimelineFrameTime(effectTime);
                SampleEffectAnimators(track, EffectElapsed(track, playKey._frame / FramesPerSecond, effectTime));
            }
            if (applyCameraAttachmentAfterMotion)
                transform.SetLocalPositionAndRotation(position, rotation);
            // JP also resets tracks whose keys all disable emissive. Leaving the
            // prefab default untouched makes 104201's shell glow white.
            if (supportOptions == null)
                ApplyEffectEmissive(track, key);
        }
        if (!useNativeEffectKeys && track.UseTimeScale)
            foreach (var animator in track.Animators)
                animator.speed = key.IsPause ? 0f : TimeScaleAt(Keys(Current.Sheet._timeScaleList), time * FramesPerSecond);
        track.Previous = key;
    }

    private static void SampleEffectAnimators(VisualTrack<EffectKey> track, float elapsed)
    {
        foreach (var animator in track.Animators)
        {
            if (!animator.isActiveAndEnabled || !animator.runtimeAnimatorController)
                continue;
            // Some controllers report infinite state length at speed zero.
            // Evaluate the pose at normal speed with no delta, then freeze automatic playback.
            animator.speed = 1f;
            try
            {
                var state = animator.GetCurrentAnimatorStateInfo(0);
                if (state.fullPathHash == 0 || !float.IsFinite(state.length) || state.length <= 0f)
                {
                    animator.Update(0f);
                    state = animator.GetCurrentAnimatorStateInfo(0);
                }
                if (state.fullPathHash == 0 || !float.IsFinite(state.length) || state.length <= 0f)
                    continue;
                animator.Play(state.fullPathHash, 0, elapsed / state.length);
                animator.Update(0f);
            }
            finally
            {
                animator.speed = 0f;
            }
        }
    }

    private bool TryGetEffectInterpolation(EffectKey[] keys, Func<EffectKey, bool> usesChannel, int channel,
        out EffectKey from, out EffectKey to)
    {
        from = to = null;
        foreach (var key in keys)
        {
            if (!usesChannel(key))
                continue;
            if (key._frame > lastFrame)
            {
                if (from != null && key._params?.ElementAtOrDefault(channel)?._interpolateType > 0)
                {
                    to = key;
                    return true;
                }
                break;
            }
            from = key;
        }
        return false;
    }

    private void AdvanceEffectParticles(VisualTrack<EffectKey> track, EffectKey key, float from, float to)
    {
        if (!track.Instance.activeSelf || key == null || key.IsPause || to <= from)
            return;
        var playKey = track.ParticlePlayKey;
        bool fixedTime = playKey != null && (playKey.IsUseFixedTime ||
            (supportOptions == null && Current.Unique && playKey.IsUseFixedTimeForShort));
        if (fixedTime)
        {
            from = EffectTimelineFrameTime(from);
            to = EffectTimelineFrameTime(to);
        }
        float duration = EffectElapsed(track, from, to);
        if (duration > 0f)
        {
            track.ParticlePlaybackTime += duration;
            SimulateEffectParticles(track, duration, false,
                synchronizeTime: supportOptions == null && Current.Unique && !fixedTime
                    ? track.ParticlePlaybackTime : null);
        }
    }

    private static (ParticleSystem Particle, bool WithChildren)[] BindEffectSubEmitters(ParticleSystem[] particles)
    {
        var subEmitters = new HashSet<ParticleSystem>();
        foreach (var particle in particles)
        {
            var module = particle.subEmitters;
            for (int index = 0; index < module.subEmittersCount; index++)
                if (module.GetSubEmitterSystem(index) is { } subEmitter && subEmitter)
                    subEmitters.Add(subEmitter);
        }

        return particles.Where(particle => !subEmitters.Contains(particle))
            .Select(particle => (particle, particle.subEmitters.subEmittersCount > 0))
            .ToArray();
    }

    private const float EffectParticleMinimumStartTime = .00001f;

    private void SimulateEffectParticles(VisualTrack<EffectKey> track, float duration, bool restart,
        bool useParticleStartTime = false, float? synchronizeTime = null)
    {
        if (track.Particles.Length == 0)
            return;

        bool useNativeGravity = supportOptions == null && Current.Unique;
        var previousGravity = useNativeGravity ? Physics.gravity : default;
        bool restoreGravity = useNativeGravity && previousGravity != TimelineParticlePhysics.NativeGravity;
        if (restoreGravity)
            Physics.gravity = TimelineParticlePhysics.NativeGravity;
        try
        {
            if (track.ParticleSimulation == null)
            {
                foreach (var particle in track.Particles)
                    Simulate(particle, false);
            }
            else
            {
                foreach (var (particle, withChildren) in track.ParticleSimulation)
                    Simulate(particle, withChildren);
            }
            foreach (var supporter in track.TrailCustomData)
            {
                if (restart)
                    supporter.ResetTime();
                supporter.AddTime(useParticleStartTime && !supporter.GetComponent<ParticleSystem>().main.prewarm
                    ? Mathf.Max(EffectParticleMinimumStartTime, duration) : duration);
            }
        }
        finally
        {
            if (restoreGravity)
                Physics.gravity = previousGravity;
        }

        void Simulate(ParticleSystem particle, bool withChildren)
        {
            // Non-looping Unity systems clamp their clock at main.duration while particles
            // and world-space trails can remain alive. JP SetTime / ApplyParticleTime(0)
            // resynchronizes that clock by restarting at the absolute playback time.
            // Delta-only simulation keeps an old trail history across the final hold.
            var main = particle.main;
            if (synchronizeTime is float expectedTime && !main.loop &&
                particle.time >= main.duration && expectedTime > main.duration)
            {
                particle.Simulate(expectedTime, false, true, false);
                return;
            }
            float particleTime = useParticleStartTime && !particle.main.prewarm
                ? Mathf.Max(EffectParticleMinimumStartTime, duration) : duration;
            particle.Simulate(particleTime, withChildren, restart, false);
        }
    }

    private static float EffectTimelineFrameTime(float seconds) =>
        Mathf.FloorToInt(seconds * FramesPerSecond + .0001f) / FramesPerSecond;

    private void SetEffectEmission(VisualTrack<EffectKey> track, bool enabled)
    {
        bool useTimelineEmission = supportOptions == null && Current.Unique;
        if (useTimelineEmission && track.ParticleSimulation != null)
        {
            foreach (var (particle, _) in track.ParticleSimulation)
            {
                var emission = particle.emission;
                emission.enabled = enabled;
            }
            return;
        }

        for (int index = 0; index < track.Particles.Length; index++)
        {
            var emission = track.Particles[index].emission;
            emission.enabled = enabled && (useTimelineEmission || track.Emission[index]);
        }
    }

    private static float TimeScaleAt(TimeScaleKey[] keys, float frame)
    {
        TimeScaleKey from = null, to = null;
        foreach (var key in keys)
        {
            if (key._timeScaleSetting?._isValid != true)
                continue;
            if (key._frame > frame)
            {
                to = key;
                break;
            }
            from = key;
        }
        if (from == null)
            return 1f;
        var interpolation = to?._params?.FirstOrDefault();
        return interpolation?._interpolateType > 0
            ? Mathf.LerpUnclamped(from._timeScaleSetting._value, to._timeScaleSetting._value,
                interpolation.WeightBetween(from, to, frame))
            : from._timeScaleSetting._value;
    }

    private float EffectElapsed(VisualTrack<EffectKey> track, float start, float end)
    {
        var keys = Keys(Current.Sheet._timeScaleList);
        if (!track.UseTimeScale || keys.Length == 0)
            return Mathf.Max(0f, end - start);
        // Scale the simulation clock in authored 30 Hz intervals. Event and camera times stay on the worksheet clock.
        float from = start * FramesPerSecond;
        float to = end * FramesPerSecond;
        float scaledFrames = 0f;
        while (from < to)
        {
            int frame = Mathf.FloorToInt(from);
            float next = Mathf.Min(to, frame + 1);
            scaledFrames += (next - from) * Mathf.Max(0f, TimeScaleAt(keys, frame));
            from = next;
        }
        return scaledFrames / FramesPerSecond;
    }

    private float TrackWeight(Key key, Key next, Interpolation[] parameters, int channel, float? sampleFrame = null)
    {
        var interpolation = parameters?.ElementAtOrDefault(channel);
        return interpolation?._interpolateType > 0
            ? interpolation.WeightBetween(key, next, sampleFrame ?? time * FramesPerSecond) : 0f;
    }

    private void ApplyEffectColor(VisualTrack<EffectKey> track, EffectKey key, EffectKey next)
    {
        bool usesFill = key._useFillColor;
        var color = key._fillColor;
        if (track.UseGachaSubColor)
        {
            color = Color.clear;
            usesFill = true;
        }
        else if (supportOptions == null && Current.Unique &&
            TryGetEffectInterpolation(track.Keys, candidate => candidate._useFillColor, 3, out var from, out var to))
        {
            color = Color.LerpUnclamped(from._fillColor, to._fillColor,
                Interpolation.NativeWeightBetween(from, to, to._params, 3, lastFrame));
            usesFill = true;
        }
        else if (usesFill && next?._useFillColor == true)
            color = Color.LerpUnclamped(color, next._fillColor, TrackWeight(key, next, next._params, 3));

        if (!usesFill)
        {
            if (track.EffectColorApplied)
            {
                for (int i = 0; i < track.Renderers.Length; i++)
                    if (track.OriginalEffectColors[i] != null)
                        track.Renderers[i].SetPropertyBlock(track.OriginalEffectColors[i]);
                foreach (var (material, property, original) in track.EffectMaterialColors)
                    material.SetColor(property, original);
            }
            track.EffectColorApplied = false;
            return;
        }
        if (track.OriginalEffectColors == null)
            BindEffectFillColors(track);
        foreach (var (material, property, _) in track.EffectMaterialColors)
            material.SetColor(property, color);
        for (int index = 0; index < track.Renderers.Length; index++)
        {
            if (track.OriginalEffectColors[index] == null)
                continue;
            var renderer = track.Renderers[index];
            renderer.GetPropertyBlock(effectProperties);
            if (track.UseGachaSubColor)
            {
                var material = renderer.sharedMaterial;
                if (!material)
                    continue;
                if (material.HasProperty(EffectColorId))
                    effectProperties.SetColor(EffectColorId, color);
                else if (material.HasProperty(EffectCharaColorId))
                    effectProperties.SetColor(EffectCharaColorId, color);
                else
                    continue;
            }
            else
            {
                effectProperties.SetColor(EffectColorId, color);
                effectProperties.SetColor(EffectCharaColorId, color);
            }
            renderer.SetPropertyBlock(effectProperties);
        }
        track.EffectColorApplied = true;
    }

    private void BindEffectFillColors(VisualTrack<EffectKey> track)
    {
        track.OriginalEffectColors = new MaterialPropertyBlock[track.Renderers.Length];
        bool useMaterial = supportOptions == null && Current.Unique;
        for (int index = 0; index < track.Renderers.Length; index++)
        {
            var renderer = track.Renderers[index];
            if (useMaterial && renderer is ParticleSystemRenderer &&
                track.Animators.Any(animator => animator.runtimeAnimatorController &&
                    renderer.transform.IsChildOf(animator.transform)))
            {
                foreach (var material in GetEffectMaterials(track, index))
                {
                    if (!material)
                        continue;
                    int property = material.HasProperty(EffectColorId) ? EffectColorId : EffectCharaColorId;
                    if (material.HasProperty(property) &&
                        !track.EffectMaterialColors.Any(binding => binding.Material == material))
                        track.EffectMaterialColors.Add((material, property, material.GetColor(property)));
                }
                continue;
            }
            track.OriginalEffectColors[index] = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(track.OriginalEffectColors[index]);
        }
    }

    private void ApplyEffectEmissive(VisualTrack<EffectKey> track, EffectKey current)
    {
        var color = current._useEmissive ? current._emissiveColor : Color.black;
        // JP 2.32.0: channel 8 looks ahead for an enabled curve, stopping at a
        // disabled key. The backward search accepts disabled keys, which reset to black.
        bool hasDisabledKey = false;
        foreach (var key in track.Keys)
        {
            if (key._frame <= lastFrame)
            {
                hasDisabledKey |= !key._useEmissive;
                continue;
            }
            if (!hasDisabledKey || !key._useEmissive)
                break;
            if (key._params?.ElementAtOrDefault(8)?._interpolateType > 0)
            {
                color = Color.black;
                break;
            }
        }
        // Apply after fill-color restore so its saved block cannot clear emissive.
        foreach (var renderer in track.Renderers)
        {
            renderer.GetPropertyBlock(effectProperties);
            effectProperties.SetColor(EffectEmissiveColorId, color);
            renderer.SetPropertyBlock(effectProperties);
        }
    }

    private void UpdateEffectAttachment(VisualTrack<EffectKey> track)
    {
        while (track.NextAttachmentKey < track.Keys.Length && track.Keys[track.NextAttachmentKey]._frame <= lastFrame)
        {
            var key = track.Keys[track.NextAttachmentKey++];
            if (!key._attach)
            {
                if (key._detach)
                    track.EffectAttachment = default;
                continue;
            }
            // The native resolver keys its last successful lookup by category and locator path.
            var previous = track.LastEffectLocatorKey;
            if (track.LastEffectLocator.Node && previous._attachCharacterIndex == key._attachCharacterIndex &&
                previous._attachLocator == key._attachLocator)
                track.EffectAttachment = track.LastEffectLocator;
            else
                track.EffectAttachment = FindEffectAnchor(key, track);
            if (track.EffectAttachment.Node)
            {
                track.EffectAttachmentKey = key;
                track.LastEffectLocator = track.EffectAttachment;
                track.LastEffectLocatorKey = key;
            }
        }
    }

    private (Transform Node, VisualTrack<EffectKey> Parent) FindEffectAnchor(EffectKey key, VisualTrack<EffectKey> child)
    {
        if (key._attachCharacterIndex == CameraAttachmentIndex)
            return (FindNode(camera.transform, key._attachLocator) ?? camera.transform, null);
        if (key._attachCharacterIndex == GachaCutInProps.BackgroundAttachmentIndex)
            return (FindBackgroundAnchor(key._attachLocatorSearchId, key._attachLocator), null);
        if (key._attachCharacterIndex == EffectAttachmentIndex)
        {
            if (string.IsNullOrEmpty(key._attachLocatorSearchId) ||
                !effectAnchors.TryGetValue(key._attachLocatorSearchId, out var parent) || parent == child)
                return default;
            return (FindAttachmentPath(parent.Instance.transform, key._attachLocator), parent);
        }
        if (key._attachCharacterIndex == GachaCutInProps.AttachmentIndex)
            return (props?.FindAttachment(key._attachLocatorSearchId, key._attachLocator), null);
        if (cast.TryGetValue(key._attachCharacterIndex, out var binding))
            return (FindNode(binding.Actor.transform, key._attachLocator) ?? binding.Actor.transform, null);
        return default;
    }

    private void SetEffectAnimatorsPaused(bool paused)
    {
        if (supportOptions == null && Current.Unique)
            return;
        foreach (var track in effects)
            foreach (var animator in track.Animators)
                animator.speed = paused ? 0f : 1f;
    }

    private static GameObject InstantiateVisual(GameObject prefab, Transform parent)
    {
        var instance = Object.Instantiate(prefab, parent, false);
        instance.SetActive(false);
        SetLayer(instance);
        // CutIn cameras can render immediately after sampling an animated visual.
        // Refresh its skinning matrices here as for actors, without waiting for Unity's next update.
        foreach (var renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            renderer.forceMatrixRecalculationPerRender = true;
        return instance;
    }

    private Transform FindBackgroundAnchor(string searchId, string path)
    {
        if (searchId == null || !backgroundAnchors.TryGetValue(searchId, out var anchor))
            return null;
        return FindAttachmentPath(anchor, path);
    }

    private static Transform FindAttachmentPath(Transform anchor, string path) =>
        string.IsNullOrEmpty(path) ? anchor : anchor.Find(path);

    private float RelativeTime(Key key) => Mathf.Max(0f, time - key._frame / FramesPerSecond);

    private static void Sample(AnimationClip clip, GameObject target, float time,
        FloatValue head = null, FloatValue speed = null, bool loop = false)
    {
        if (!clip)
            return;
        clip.SampleAnimation(target, SampleTime(clip, time, head, speed, loop));
    }

    private static float SampleTime(AnimationClip clip, float time, FloatValue head, FloatValue speed, bool loop)
    {
        float position = time * (speed?.Or(1f) ?? 1f) + (head?.Or(0f) ?? 0f);
        return loop && clip.length > 0f ? Mathf.Repeat(position, clip.length) : Mathf.Clamp(position, 0f, clip.length);
    }

    private static T Read<T>(CutEvent entry) => JsonUtility.FromJson<T>(entry.serializedParamter);

    private static T CurrentKey<T>(T[] keys, int frame) where T : Key
    {
        for (int i = keys.Length - 1; i >= 0; i--)
            if (keys[i]._frame <= frame)
                return keys[i];
        return null;
    }

    private static Transform FindNode(Transform parent, string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;
        if (parent.name == name)
            return parent;
        foreach (Transform child in parent)
        {
            var node = FindNode(child, name);
            if (node)
                return node;
        }
        return null;
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        ClearSeekMaterials();
        environment.Dispose();
        ClearEffectMaterials();
        ClearBackgroundTimeMaterials();
        maskCamera?.Dispose();
        monitorCamera?.Dispose();
        props?.Dispose();
        sceneCards?.Dispose();
        audience?.Dispose();
        if (root)
        {
            audio.Dispose();
            root.gameObject.SetActive(false);
            Object.Destroy(root.gameObject);
        }
        Object.Destroy(cameraController);
        foreach (var binding in actorPlayback.Values)
        {
            binding.Shadow?.Dispose();
            binding.Shading?.Dispose();
            binding.DisposeAdditiveFace();
            Object.Destroy(binding.PositionController);
        }
        flash?.Dispose();
        if (!timingOnly)
            loader.Dispose();
    }

    internal sealed class ReplayResources : IDisposable
    {
        internal readonly Dictionary<string, AudioClip> Clips;
        private readonly bool ownsClips;
        private UnityEngine.Random.State random;
        private static ReplayResources active;

        internal ReplayResources(Dictionary<string, AudioClip> sharedClips = null)
        {
            ownsClips = sharedClips == null;
            Clips = sharedClips ?? new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
            random = UnityEngine.Random.state;
        }

        internal IDisposable Enter() => new RandomScope(this);

        internal static void SeedParticles(GameObject instance)
        {
            if (active == null)
                return;
            foreach (var particle in instance.GetComponentsInChildren<ParticleSystem>(true))
                if (particle.useAutoRandomSeed)
                {
                    particle.useAutoRandomSeed = false;
                    particle.randomSeed = (uint)UnityEngine.Random.Range(1, int.MaxValue);
                }
        }

        public void Dispose()
        {
            if (ownsClips)
            {
                foreach (var clip in Clips.Values)
                    if (clip) Object.Destroy(clip);
                Clips.Clear();
            }
        }

        private sealed class RandomScope : IDisposable
        {
            private readonly ReplayResources owner;
            private readonly ReplayResources previous;
            private readonly UnityEngine.Random.State saved;

            internal RandomScope(ReplayResources owner)
            {
                this.owner = owner;
                previous = active;
                saved = UnityEngine.Random.state;
                active = owner;
                UnityEngine.Random.state = owner.random;
            }

            public void Dispose()
            {
                owner.random = UnityEngine.Random.state;
                UnityEngine.Random.state = saved;
                active = previous;
            }
        }
    }

    private sealed class ActorPlayback
    {
        private static readonly int GradientTopId = Shader.PropertyToID("_TopColor");
        private static readonly int GradientBottomId = Shader.PropertyToID("_BottomColor");
        private static readonly int GradientStartId = Shader.PropertyToID("_GradientStartPos");
        private static readonly int GradientEndId = Shader.PropertyToID("_GradientEndPos");
        private static readonly int GradientScaleId = Shader.PropertyToID("_GradientScale");
        private static readonly int GradientOffsetId = Shader.PropertyToID("_GradientOffset");
        private static readonly int GradientBlendModeId = Shader.PropertyToID("_GradientBlendMode");
        private static readonly int GradientAlphaId = Shader.PropertyToID("_GradientBlendAlpha");
        private static readonly int GradientOutlineId = Shader.PropertyToID("_IsGradientOutline");
        private static readonly int GradientScreenId = Shader.PropertyToID("_IsGradientScreenMode");

        public readonly UmaContainerCharacter Actor;
        public readonly Renderer[] Renderers;
        public readonly Animator PositionAnimator;
        public readonly AnimatorOverrideController PositionController;
        public Vector3 Offset;
        public float ModelScale;
        public Vector3 BodyLight;
        public CharacterMotion PreviousMotion;
        public readonly Dictionary<string, bool> SpringChainStates = new Dictionary<string, bool>();
        private CharacterMotion previousPositionMotion;
        public CharacterKey PreviousCharacter;
        public LiveFacePartsScaleController FacePartsScale;
        public readonly GachaTransformExpressions BodyExpressions, HeadExpressions;
        public bool BodyExpressionEnabled, HeadExpressionEnabled;
        public GachaCharacterShadow Shadow;
        public bool HasDirt, HasVertexToon, HasCharacterEmissive, HasAuthoredEmissiveColor;
        public bool HasModelGradient => gradientRenderers.Length > 0;
        private int[] gradientRenderers = Array.Empty<int>();
        private readonly Dictionary<string, Transform> gradientNodes = new Dictionary<string, Transform>();
        private CharacterColor gradientFrom, gradientPolicy;
        private float gradientWeight;
        private bool gradientApplied;
        public CharacterAuraKey[] AuraKeys;
        public AuraPass.AuraParam Aura;
        public GachaCharacterShading Shading;
        public GachaCharacterMaterialColor MaterialColor;
        private readonly List<CharacterPartPlayback> parts = new List<CharacterPartPlayback>();
        private readonly (Renderer Renderer, bool InitiallyHidden)[] eyebrows;
        private MaterialPropertyBlock[] originalColors;
        private readonly (int Slot, Material Material)[][] eyeMaterials;
        private readonly (Material Material, int Original, int CutIn)[] headRenderQueues;
        private readonly (Transform Locator, Vector3 Position, Quaternion Rotation, Vector3 Scale)[] faceLocators;
        private PlayableGraph additiveFaceGraph;
        private AnimationLayerMixerPlayable additiveFaceMixer;
        private GameObject additiveFaceRoot;
        private (Transform Model, Transform Sample)[] additiveFaceBones;
        private readonly List<(Transform Bone, FacePose Base, FacePose Applied)> additiveFaceWrites =
            new List<(Transform, FacePose, FacePose)>();
        private NativeArray<FacePoseBinding> additiveFacePose;
#if !UNITY_EDITOR
        private PlayableGraph faceGraph;
        private AnimationLayerMixerPlayable faceMixer;
        private readonly AnimationClipPlayable[] facePlayables = new AnimationClipPlayable[2];
#endif
        private AnimationClipPlayable additiveFacePlayable;
        private AnimationClip additiveFaceClip;
        private readonly Dictionary<(int Renderer, int Slot), MaterialPropertyBlock> originalEyeColors =
            new Dictionary<(int, int), MaterialPropertyBlock>();
        private CySpringWindParam wind;
        private bool windApplied;
        private int previousWindMask;
        private bool legacyPhysicsPaused;
        private int layer = PreviewLayer;

        public ActorPlayback(UmaContainerCharacter actor, Transform root, GachaCutInLoader loader)
        {
            Actor = actor;
            actor.LiveEyeHighlight ??= LiveEyeHighlightController.Create(actor);
            var bodyData = loader.LoadTransformProcessData(actor.BodyResourceName);
            var headData = loader.LoadTransformProcessData(actor.LiveHeadResourceName);
            if (bodyData) BodyExpressions = new GachaTransformExpressions(actor, bodyData, actor.Body.transform);
            if (headData) HeadExpressions = new GachaTransformExpressions(actor, headData, actor.Head?.transform);
            faceLocators = actor.FaceDrivenKeyTarget.DrivenKeyLocator.GetComponentsInChildren<Transform>(true)
                .Select(node => (node, node.localPosition, node.localRotation, node.localScale)).ToArray();
            var skinnedRenderers = actor.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            // Manual camera renders must use the pose sampled by this CutIn tick.
            foreach (var renderer in skinnedRenderers)
                renderer.forceMatrixRecalculationPerRender = true;
            Renderers = skinnedRenderers;
            bool hasHair = Renderers.Any(renderer => renderer.name == "M_Hair");
            var headQueues = new List<(Material, int, int)>();
            foreach (var renderer in Renderers)
            {
                if (Gallop.Live.LiveModelRenderQueue.TryAlpha(renderer.name, out _, out _))
                    continue;
                foreach (var material in renderer.sharedMaterials)
                {
                    if (!material || material.renderQueue > (int)UnityEngine.Rendering.RenderQueue.GeometryLast ||
                        !Gallop.Live.LiveModelRenderQueue.TryPart(renderer.name, material, hasHair, out var part))
                        continue;
                    if (part is Gallop.Live.LiveModelRenderQueue.Part.FaceSkin or
                        Gallop.Live.LiveModelRenderQueue.Part.FaceProp or Gallop.Live.LiveModelRenderQueue.Part.HeadObj or
                        Gallop.Live.LiveModelRenderQueue.Part.Head or Gallop.Live.LiveModelRenderQueue.Part.Eye or
                        Gallop.Live.LiveModelRenderQueue.Part.Hair or Gallop.Live.LiveModelRenderQueue.Part.HairProp or
                        Gallop.Live.LiveModelRenderQueue.Part.HeadWithoutHair or Gallop.Live.LiveModelRenderQueue.Part.Mayu or
                        Gallop.Live.LiveModelRenderQueue.Part.Jaw)
                        headQueues.Add((material, material.renderQueue, Gallop.Live.LiveModelRenderQueue.Queue(part, 0)));
                }
            }
            headRenderQueues = headQueues.ToArray();
            eyebrows = Renderers.Where(renderer => renderer.name == "M_Mayu")
                .Select(renderer => (renderer, renderer.forceRenderingOff)).ToArray();
            eyeMaterials = Renderers.Select(renderer => renderer.sharedMaterials
                .Select((material, slot) => (Slot: slot, Material: material))
                .Where(entry => entry.Material && (renderer.name == "M_Eye" ||
                    entry.Material.shader && entry.Material.shader.name.Contains("ToonEye", StringComparison.Ordinal)))
                .ToArray()).ToArray();
            PositionAnimator = new GameObject("Character position locator").AddComponent<Animator>();
            PositionAnimator.transform.SetParent(root, false);
            PositionController = Object.Instantiate(UmaViewerBuilder.Instance.CameraOverrideController);
            PositionAnimator.runtimeAnimatorController = PositionController;
            PositionAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            PositionAnimator.speed = 0f;
        }

        public void RestoreFaceLocators()
        {
#if !UNITY_EDITOR
            // Playables retain values for controls omitted by the next sparse clip.
            // Drop that animation stream before restoring the authored neutral pose.
            if (faceGraph.IsValid())
                faceGraph.Destroy();
#endif
            // Facial clips only bind the controls they use. Restore the authored neutral pose at a motion boundary.
            foreach (var (locator, position, rotation, scale) in faceLocators)
            {
                locator.SetLocalPositionAndRotation(position, rotation);
                locator.localScale = scale;
            }
        }

        public void SampleFace(AnimationClip face, AnimationClip ear, CharacterMotion motion, float elapsed)
        {
#if UNITY_EDITOR
            var locator = Actor.FaceDrivenKeyTarget.DrivenKeyLocator.gameObject;
            Sample(face, locator, elapsed, motion._facialClipHeadTime, motion._facialClipPlaySpeed, motion._loop);
            Sample(ear, locator, elapsed, motion._earClipHeadTime, motion._earClipPlaySpeed, motion._loop);
#else
            if (face && !face.legacy || ear && !ear.legacy)
            {
                if (!faceGraph.IsValid())
                {
                    faceGraph = PlayableGraph.Create("CutIn face and ears");
                    faceGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    faceMixer = AnimationLayerMixerPlayable.Create(faceGraph, 2);
                    var output = AnimationPlayableOutput.Create(faceGraph, "Driven key locators", Actor.UmaFaceAnimator);
                    output.SetSourcePlayable(faceMixer);
                    faceGraph.Play();
                }
                BindFaceClip(0, face, elapsed, motion._facialClipHeadTime, motion._facialClipPlaySpeed, motion._loop);
                BindFaceClip(1, ear, elapsed, motion._earClipHeadTime, motion._earClipPlaySpeed, motion._loop);
                faceGraph.Evaluate(0f);
            }
            if (face && face.legacy)
                Sample(face, Actor.UmaFaceAnimator.gameObject, elapsed, motion._facialClipHeadTime, motion._facialClipPlaySpeed, motion._loop);
            if (ear && ear.legacy)
                Sample(ear, Actor.UmaFaceAnimator.gameObject, elapsed, motion._earClipHeadTime, motion._earClipPlaySpeed, motion._loop);
#endif
        }

#if !UNITY_EDITOR
        private void BindFaceClip(int layer, AnimationClip clip, float elapsed, FloatValue head, FloatValue speed, bool loop)
        {
            if (clip && clip.legacy)
                clip = null;
            var playable = facePlayables[layer];
            if (playable.IsValid() && playable.GetAnimationClip() != clip)
            {
                faceMixer.DisconnectInput(layer);
                faceGraph.DestroyPlayable(playable);
                facePlayables[layer] = playable = default;
            }
            if (clip && !playable.IsValid())
            {
                facePlayables[layer] = playable = AnimationClipPlayable.Create(faceGraph, clip);
                playable.SetApplyFootIK(false);
                playable.SetApplyPlayableIK(false);
                playable.SetSpeed(0d);
                faceGraph.Connect(playable, 0, faceMixer, layer);
                faceMixer.SetInputWeight(layer, 1f);
            }
            if (playable.IsValid())
                playable.SetTime(SampleTime(clip, elapsed, head, speed, loop));
        }
#endif

        public void SampleAdditiveFace(FacialAdditiveMotionAsset asset, float elapsed)
        {
            var clip = asset != null ? asset.AnimationClip : null;
            if (!clip)
            {
                if (additiveFaceGraph.IsValid())
                    additiveFaceGraph.Stop();
                return;
            }
            if (!additiveFaceGraph.IsValid())
                CreateAdditiveFaceGraph();
            if (clip != additiveFaceClip)
            {
                if (additiveFacePlayable.IsValid())
                {
                    additiveFaceMixer.DisconnectInput(1);
                    additiveFaceGraph.DestroyPlayable(additiveFacePlayable);
                }
                additiveFacePlayable = AnimationClipPlayable.Create(additiveFaceGraph, clip);
                additiveFacePlayable.SetApplyFootIK(false);
                additiveFacePlayable.SetSpeed(0d);
                additiveFaceMixer.ConnectInput(1, additiveFacePlayable, 0, 1f);
                additiveFaceClip = clip;
            }
            if (!additiveFaceGraph.IsPlaying())
                additiveFaceGraph.Play();
            additiveFacePlayable.SetTime(clip.isLooping && clip.length > 0f
                ? Mathf.Repeat(elapsed, clip.length) : Mathf.Clamp(elapsed, 0f, clip.length));
            additiveFaceGraph.Evaluate(0f);
            foreach (var (model, sample) in additiveFaceBones)
            {
                var basePose = new FacePose(model);
                var result = new FacePose(sample);
                if (!result.Position.Equals(basePose.Position) || !result.Rotation.Equals(basePose.Rotation))
                    model.SetLocalPositionAndRotation(result.Position, result.Rotation);
                if (!result.Scale.Equals(basePose.Scale))
                    model.localScale = result.Scale;
                var applied = new FacePose(model);
                if (!applied.Position.Equals(basePose.Position) || !applied.Rotation.Equals(basePose.Rotation) ||
                    !applied.Scale.Equals(basePose.Scale))
                    additiveFaceWrites.Add((model, basePose, applied));
            }
        }

        public void RestoreAdditiveFaceBase()
        {
            foreach (var (bone, basePose, applied) in additiveFaceWrites)
            {
                var position = bone.localPosition;
                var rotation = bone.localRotation;
                if (!basePose.Position.Equals(applied.Position) && position.Equals(applied.Position))
                    bone.localPosition = basePose.Position;
                if (!basePose.Rotation.Equals(applied.Rotation) && rotation.Equals(applied.Rotation))
                    bone.localRotation = basePose.Rotation;
                if (!basePose.Scale.Equals(applied.Scale) && bone.localScale.Equals(applied.Scale))
                    bone.localScale = basePose.Scale;
            }
            additiveFaceWrites.Clear();
        }

        private readonly struct FacePose
        {
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;
            public readonly Vector3 Scale;

            public FacePose(Transform bone)
            {
                Position = bone.localPosition;
                Rotation = bone.localRotation;
                Scale = bone.localScale;
            }
        }

        private void CreateAdditiveFaceGraph()
        {
            additiveFaceRoot = new GameObject("CutIn facial sample rig");
            additiveFaceRoot.transform.SetParent(PositionAnimator.transform.parent, false);
            var bones = new List<(Transform Model, Transform Sample)>();
            CopyFaceChildren(Actor.HeadBone.transform, additiveFaceRoot.transform, bones);
            additiveFaceBones = bones.ToArray();
            var animator = additiveFaceRoot.AddComponent<Animator>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            additiveFaceGraph = PlayableGraph.Create("CutIn facial additive");
            additiveFaceGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            additiveFacePose = new NativeArray<FacePoseBinding>(additiveFaceBones.Length, Allocator.Persistent);
            for (int index = 0; index < additiveFaceBones.Length; index++)
            {
                var (model, sample) = additiveFaceBones[index];
                additiveFacePose[index] = new FacePoseBinding
                {
                    Scene = animator.BindSceneTransform(model),
                    Stream = animator.BindStreamTransform(sample)
                };
            }
            var basePose = AnimationScriptPlayable.Create(additiveFaceGraph,
                new ReadFacePose { Bindings = additiveFacePose });
            additiveFaceMixer = AnimationLayerMixerPlayable.Create(additiveFaceGraph, 2);
            additiveFaceMixer.ConnectInput(0, basePose, 0, 1f);
            additiveFaceMixer.SetLayerAdditive(1, true);
            var output = AnimationPlayableOutput.Create(additiveFaceGraph, "Face", animator);
            output.SetSourcePlayable(additiveFaceMixer);
        }

        private static void CopyFaceChildren(Transform model, Transform sample, List<(Transform, Transform)> bones)
        {
            foreach (Transform child in model)
            {
                var copy = new GameObject(child.name).transform;
                copy.SetParent(sample, false);
                copy.SetLocalPositionAndRotation(child.localPosition, child.localRotation);
                copy.localScale = child.localScale;
                bones.Add((child, copy));
                CopyFaceChildren(child, copy, bones);
            }
        }

        public void DisposeAdditiveFace()
        {
#if !UNITY_EDITOR
            if (faceGraph.IsValid())
                faceGraph.Destroy();
#endif
            if (additiveFaceGraph.IsValid())
                additiveFaceGraph.Destroy();
            if (additiveFacePose.IsCreated)
                additiveFacePose.Dispose();
            if (additiveFaceRoot)
                Object.Destroy(additiveFaceRoot);
        }

        private struct FacePoseBinding
        {
            public TransformSceneHandle Scene;
            public TransformStreamHandle Stream;
        }

        private struct ReadFacePose : IAnimationJob
        {
            [ReadOnly] public NativeArray<FacePoseBinding> Bindings;

            public void ProcessAnimation(AnimationStream stream)
            {
                foreach (var binding in Bindings)
                {
                    var scene = binding.Scene;
                    var target = binding.Stream;
                    if (!scene.IsValid(stream) || !target.IsValid(stream))
                        continue;
                    scene.GetLocalTRS(stream, out var position, out var rotation, out var scale);
                    target.SetLocalTRS(stream, position, rotation, scale, false);
                }
            }

            public void ProcessRootMotion(AnimationStream stream) { }
        }

        public void Reset(Vector3 offset, CharacterTrack characterTrack, bool characterUnique)
        {
            // JP ModelController.UpdateRenderQueue puts face/hair/brows before
            // ordinary props (1999). Prefab queues let 104802's stencil-only
            // brow pass draw over the foreground burger, which also writes 100.
            foreach (var (material, original, cutIn) in headRenderQueues)
                material.renderQueue = characterUnique ? cutIn : original;
            Offset = offset;
            ModelScale = Actor.BodyScale;
            BodyLight = Vector3.back;
            SetLegacyPhysicsPaused(false);
            PreviousMotion = null;
            PreviousCharacter = null;
            previousPositionMotion = null;
            PositionAnimator.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            PositionAnimator.transform.localScale = Vector3.one;
            var characterKeys = Keys(characterTrack._characterKeys);
            if (characterKeys.Any(key => key.IsPartsControl))
                FacePartsScale = LiveFacePartsScaleController.Create(Actor);
            HasAuthoredEmissiveColor = characterKeys.Any(key => key.IsEmissiveColor);
            var colorKeys = Keys(characterTrack._characterColorKeys?.FirstOrDefault());
            gradientFrom = gradientPolicy = null;
            gradientApplied = false;
            gradientRenderers = characterUnique && colorKeys.Any(key => GradientEnabled(key.IsUseModelGradient))
                ? Enumerable.Range(0, Renderers.Length).Where(index => Renderers[index].sharedMaterials.Any(
                    material => material && material.HasProperty(GradientBlendModeId))).ToArray()
                : Array.Empty<int>();
            HasVertexToon = characterUnique && colorKeys.Any(key => NonDefault(key.VertexColorToonPower));
            HasCharacterEmissive = characterUnique && colorKeys.Any(key =>
                NonDefault(key.EmissiveIntensity) || NonDefault(key.EmissiveRimPower) ||
                NonDefault(key.EmissiveRimIntensity) || key.IsEmissiveCenter is { _isValid: true, _value: true });
            if (colorKeys.Length > 0 || HasAuthoredEmissiveColor)
            {
                originalColors = new MaterialPropertyBlock[Renderers.Length];
                for (int index = 0; index < Renderers.Length; index++)
                {
                    originalColors[index] = new MaterialPropertyBlock();
                    Renderers[index].GetPropertyBlock(originalColors[index]);
                }
            }

            static bool NonDefault(FloatValue value) => value != null && value.Or(1f) != 1f;

            var partTracks = characterTrack.CharacterPartsKeyArray;
            if (partTracks == null || partTracks.Length == 0)
                return;

            var renderers = Actor.GetComponentsInChildren<Renderer>(true);
            foreach (var track in partTracks)
            {
                var keys = Keys(track);
                if (keys.Length == 0)
                    continue;
                var renderer = Array.Find(renderers, candidate => candidate.name == track.RendererName);
                if (renderer)
                    parts.Add(new CharacterPartPlayback(renderer, keys));
            }
        }

        public void SetLegacyPhysicsPaused(bool paused)
        {
            if (Actor.ActivePhysicsBackend != CharacterPhysicsBackend.DynamicBone || legacyPhysicsPaused == paused)
                return;
            legacyPhysicsPaused = paused;
            Actor.SetDynamicBoneEnable(!paused);
        }

        public void RestoreColors(bool release = true)
        {
            if (release)
            {
                MaterialColor?.Restore();
                MaterialColor = null;
            }
            foreach (var color in originalEyeColors)
                Renderers[color.Key.Renderer].SetPropertyBlock(color.Value, color.Key.Slot);
            if (release)
                originalEyeColors.Clear();
            if (originalColors == null)
                return;
            for (int index = 0; index < Renderers.Length; index++)
                Renderers[index].SetPropertyBlock(originalColors[index]);
            if (release)
                originalColors = null;
        }

        public void RestoreCharacterControls(bool release = true)
        {
            if (release)
                foreach (var (material, original, _) in headRenderQueues)
                    material.renderQueue = original;
            BodyExpressions?.Restore();
            HeadExpressions?.Restore();
            Actor.transform.localScale = Vector3.one;
            if (release)
                FacePartsScale = null;
            if (Actor.LiveEyeHighlight != null)
            {
                Actor.LiveEyeHighlight.PupilScale = 1f;
                Actor.LiveEyeHighlight.ApplyMaterial();
            }
        }

        public void SampleExpressions()
        {
            BodyExpressions?.Sample(BodyExpressionEnabled);
            HeadExpressions?.Sample(HeadExpressionEnabled);
        }

        public Animator SamplePositionLocator(CharacterMotion motion, float elapsed)
        {
            var locator = PositionAnimator;
            if (motion != previousPositionMotion)
            {
                // JP CutInPositionLocator.SetClip resets TRS before binding the next clip.
                // Sparse clips must not inherit scale from the preceding shot (100703).
                locator.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                locator.transform.localScale = Vector3.one;
                PositionController["clip_1"] = motion._positionClip;
                previousPositionMotion = motion;
            }
            if (motion._positionClip)
            {
                PlaySample(locator, motion._positionClip, "motion_1", 0, elapsed,
                    motion._positionClipHeadTime, motion._positionClipPlaySpeed, motion._loop);
                locator.Update(0f);
            }
            else
            {
                locator.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                locator.transform.localScale = Vector3.one;
            }
            return locator;
        }

        public void ApplyPositionClipScale(CharacterMotion motion)
        {
            if (motion._isApplyPositionClipScale?._isValid != true || motion._isApplyPositionClipScale._value)
                Actor.transform.localScale = PositionAnimator.transform.localScale;
        }

        private static bool GradientEnabled(BoolValue value) => value is { _isValid: true, _value: true };

        public void SetModelGradientKey(CharacterColor from, CharacterColor policy, float weight)
        {
            gradientFrom = from;
            gradientPolicy = policy;
            gradientWeight = weight;
        }

        public void ApplyModelGradient(Camera targetCamera, MaterialPropertyBlock properties)
        {
            if (!HasModelGradient || !Actor.gameObject.activeSelf)
                return;
            bool enabled = GradientEnabled(gradientPolicy?.IsUseModelGradient);
            if (!enabled && !gradientApplied)
                return;

            // The native controller resolves the anchors after updating the model and the camera.
            var top = Color.black;
            var bottom = Color.black;
            var start = new Vector4(0f, 0f, 0f, 1f);
            var end = new Vector4(0f, 1f, 0f, 1f);
            float scale = 1f, offset = 0f, alpha = 0f;
            int mode = 0;
            bool outline = false, screen = false;
            if (enabled)
            {
                var from = gradientFrom;
                var to = gradientPolicy;
                screen = targetCamera && GradientEnabled(to.IsGradientScreenMode);
                outline = GradientEnabled(to.IsGradientOutline);
                mode = EnumOrZero(to.GradientBlendMode);
                bool world = EnumOrZero(to.GradientInputMode) != 0;
                start = world
                    ? WorldAnchor(from.GradientStartWorldPosition, to.GradientStartWorldPosition, Vector3.zero)
                    : NodeAnchor(to.GradientStartNodeName, from.GradientStartNodeOffset, to.GradientStartNodeOffset, Vector3.zero);
                end = world
                    ? WorldAnchor(from.GradientEndWorldPosition, to.GradientEndWorldPosition, Vector3.up)
                    : NodeAnchor(to.GradientEndNodeName, from.GradientEndNodeOffset, to.GradientEndNodeOffset, Vector3.up);
                top = Color.Lerp(from.GradientTopColor?.Or(Color.black) ?? Color.black,
                    to.GradientTopColor?.Or(Color.black) ?? Color.black, gradientWeight);
                bottom = Color.Lerp(from.GradientBottomColor?.Or(Color.black) ?? Color.black,
                    to.GradientBottomColor?.Or(Color.black) ?? Color.black, gradientWeight);
                scale = BlendFloat(from.GradientScale, to.GradientScale, 1f);
                offset = BlendFloat(from.GradientOffset, to.GradientOffset, 0f);
                alpha = BlendFloat(from.GradientBlendAlpha, to.GradientBlendAlpha, 0f);
            }

            foreach (int index in gradientRenderers)
            {
                var renderer = Renderers[index];
                renderer.GetPropertyBlock(properties);
                WriteProperties();
                renderer.SetPropertyBlock(properties);
                foreach (var (slot, _) in eyeMaterials[index])
                {
                    renderer.GetPropertyBlock(properties, slot);
                    if (properties.isEmpty)
                        continue;
                    WriteProperties();
                    renderer.SetPropertyBlock(properties, slot);
                }
            }
            gradientApplied = enabled;

            void WriteProperties()
            {
                properties.SetColor(GradientTopId, top);
                properties.SetColor(GradientBottomId, bottom);
                properties.SetVector(GradientStartId, start);
                properties.SetVector(GradientEndId, end);
                properties.SetFloat(GradientScaleId, scale);
                properties.SetFloat(GradientOffsetId, offset);
                properties.SetInt(GradientBlendModeId, mode);
                properties.SetFloat(GradientAlphaId, alpha);
                properties.SetInt(GradientOutlineId, outline ? 1 : 0);
                properties.SetInt(GradientScreenId, screen ? 1 : 0);
            }

            float BlendFloat(FloatValue from, FloatValue to, float fallback) =>
                Mathf.Lerp(from?.Or(fallback) ?? fallback, to?.Or(fallback) ?? fallback, gradientWeight);

            Vector3 BlendVector(VectorValue from, VectorValue to, Vector3 fallback) =>
                Vector3.Lerp(from?._isValid == true ? from._value : fallback,
                    to?._isValid == true ? to._value : fallback, gradientWeight);

            Vector4 WorldAnchor(VectorValue from, VectorValue to, Vector3 fallback) =>
                Project(BlendVector(from, to, fallback), 1f);

            Vector4 NodeAnchor(StringValue name, VectorValue from, VectorValue to, Vector3 fallback)
            {
                if (name?._isValid != true || string.IsNullOrEmpty(name._value))
                    return Project(Vector3.zero, 0f);
                if (!gradientNodes.TryGetValue(name._value, out var node))
                {
                    node = FindNode(Actor.transform, name._value);
                    gradientNodes.Add(name._value, node);
                }
                if (!node)
                    return Project(Vector3.zero, 0f);
                var direction = BlendVector(from, to, fallback);
                return Project(node.position + (targetCamera ? targetCamera.transform.TransformDirection(direction) : direction), 1f);
            }

            Vector4 Project(Vector3 position, float w)
            {
                if (screen)
                {
                    position = targetCamera.WorldToScreenPoint(position);
                    w = 0f;
                }
                return new Vector4(position.x, position.y, position.z, w);
            }

            static int EnumOrZero(EnumValue value) => value?._isValid == true ? value._intValue : 0;
        }

        public void ApplyEyeToon(int rendererIndex, MaterialPropertyBlock properties, CharacterColor current,
            CharacterColor next, float weight, bool usesCharacterLight, Vector3 eyeLight)
        {
            var renderer = Renderers[rendererIndex];
            foreach (var (slot, material) in eyeMaterials[rendererIndex])
            {
                if (!originalEyeColors.ContainsKey((rendererIndex, slot)))
                {
                    var original = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(original, slot);
                    originalEyeColors.Add((rendererIndex, slot), original);
                }
                properties.SetInt("_UseOriginalDirectionalLight",
                    usesCharacterLight ? 1 : material.GetInt("_UseOriginalDirectionalLight"));
                properties.SetVector("_OriginalDirectionalLightDir",
                    usesCharacterLight ? eyeLight : material.GetVector("_OriginalDirectionalLightDir"));
                Apply("_ToonStep", current.EyeToonStep, next?.EyeToonStep);
                Apply("_ToonFeather", current.EyeToonFeather, next?.EyeToonFeather, .0001f);
                Apply("_Saturation", current.EyeSaturation, next?.EyeSaturation);
                Apply("_Silhouette", current.EyeSilhouette, next?.EyeSilhouette);
                renderer.SetPropertyBlock(properties, slot);

                void Apply(string property, FloatValue a, FloatValue b, float minimum = 0f)
                {
                    int id = Shader.PropertyToID(property);
                    if (!material.HasProperty(id))
                        return;
                    float original = MaterialColor?.GetOriginalFloat(rendererIndex, slot, id) ?? material.GetFloat(id);
                    if (MaterialColor != null && MaterialColor.TryGetEyeValue(id, original, out float eyeValue))
                    {
                        properties.SetFloat(id, eyeValue);
                        return;
                    }
                    properties.SetFloat(id, Mathf.Clamp(MixFloat(a, b, original, weight), minimum, 1f));
                }
            }
        }

        public void SampleParts(int frame)
        {
            foreach (var part in parts)
            {
                var key = CurrentKey(part.Keys, frame);
                if (key == null || key.IsApplyToSpecificCharaId &&
                    key.SpecificCharaId != Convert.ToInt32(Actor.CharaData?["id"]))
                    continue;
                part.Renderer.enabled = key.IsVisible;
            }
        }

        public void SetEyebrowsHidden(bool hidden)
        {
            foreach (var (renderer, initiallyHidden) in eyebrows)
                renderer.forceRenderingOff = initiallyHidden || hidden;
        }

        public void RestoreParts(bool release = true)
        {
            SetEyebrowsHidden(false);
            foreach (var part in parts)
                part.Renderer.enabled = part.InitiallyVisible;
            if (release)
                parts.Clear();
        }

        public void SampleWind(CharacterTrack track, int frame)
        {
            var keys = Keys(track.CharacterWindKeysArray?.FirstOrDefault());
            var current = CurrentKey(keys, frame);
            if (current?._windParam == null)
                return;
            var parameter = current._windParam;
            var next = NextKey(keys, frame);
            var interpolation = next?._params?.FirstOrDefault();
            if (next?._windParam != null && interpolation?._interpolateType > 0)
            {
                wind ??= new CySpringWindParam();
                CySpringWindParam.SetInterp(parameter, next._windParam, wind,
                    interpolation.WeightBetween(current, next, frame));
                parameter = wind;
            }
            if (!windApplied && Actor.LiveCySpringController is { } spring)
            {
                previousWindMask = 0;
                for (int part = 0; part < 3; part++)
                    if (spring.GetHasWind((CySpringController.Parts)part))
                        previousWindMask |= 1 << part;
            }
            Actor.SetLiveWind(true, parameter);
            windApplied = true;
        }

        public void RestoreWind()
        {
            if (!windApplied)
                return;
            Actor.SetLiveWind(false, null);
            if (Actor.LiveCySpringController is { } spring)
                for (int part = 0; part < 3; part++)
                    spring.SetHasWind((previousWindMask & (1 << part)) != 0, (CySpringController.Parts)part);
            windApplied = false;
        }

        public void SetLayer(int nextLayer)
        {
            if (layer == nextLayer)
                return;
            layer = nextLayer;
            GachaCutInLoader.SetLayer(Actor.gameObject, layer);
        }

        public void SampleSweat(CharacterTrack track, int frame)
        {
            if (Actor.LiveSweat == null)
                return;
            var keys = Keys(track._characterSweatKeys?.FirstOrDefault());
            var current = CurrentKey(keys, frame);
            if (current == null)
                return;
            var next = NextKey(keys, frame);
            var interpolation = next?._params?.FirstOrDefault();
            if (!(interpolation?._interpolateType > 0))
                next = current;
            float blend = next == current ? 0f : interpolation.WeightBetween(current, next, frame);
            for (int i = 0; i < next._sweatInfo.Length; i++)
            {
                var a = current._sweatInfo[i];
                var b = next._sweatInfo[i];
                Actor.LiveSweat.ApplyCutInLocator(i, b._isEnable,
                    Vector3.Lerp(a._offset._value, b._offset._value, blend),
                    Color.Lerp(a._color._value, b._color._value, blend),
                    Mathf.Lerp(a._colorPower._value, b._colorPower._value, blend),
                    Mathf.Lerp(a._strength._value, b._strength._value, blend),
                    Mathf.Lerp(a._threshHold._value, b._threshHold._value, blend));
            }
        }

        public void SampleAura(int frame, bool visible)
        {
            if (Aura == null)
                return;
            var current = CurrentKey(AuraKeys, frame);
            Aura.IsEnable = visible && current?.IsEnable == true;
            if (!Aura.IsEnable)
                return;
            var next = NextKey(AuraKeys, frame);
            var interpolation = next?._params?.FirstOrDefault();
            float blend = interpolation?._interpolateType > 0 ? interpolation.WeightBetween(current, next, frame) : 0f;
            next ??= current;
            Aura.Fresnel = Mathf.Lerp(current.Fresnel, next.Fresnel, blend);
            Aura.LuminousParam = new Vector4(Mathf.Lerp(current.Threshold, next.Threshold, blend),
                Mathf.Lerp(current.Intensity, next.Intensity, blend), Mathf.Lerp(current.Spread, next.Spread, blend), 0f);
            float speed = Mathf.Lerp(current.Speed, next.Speed, blend);
            Aura.DistortionParam = new Vector4(frame / FramesPerSecond * speed,
                Mathf.Lerp(current.Distortion, next.Distortion, blend),
                Mathf.Lerp(current.Fluctuation, next.Fluctuation, blend), speed);
            Aura.AuraColor = Color.Lerp(current.Color, next.Color, blend);
            Aura.OffsetViewDir = Vector3.Lerp(current.OffsetViewDirection, next.OffsetViewDirection, blend);
        }
    }

    private readonly struct CharacterPartPlayback
    {
        public readonly Renderer Renderer;
        public readonly CharacterPartKey[] Keys;
        public readonly bool InitiallyVisible;

        public CharacterPartPlayback(Renderer renderer, CharacterPartKey[] keys)
        {
            Renderer = renderer;
            Keys = keys;
            InitiallyVisible = renderer.enabled;
        }
    }

    private sealed class Gate
    {
        private readonly Transform root;
        private GameObject effect;
        private ParticleSystem[] effectParticles;
        private float effectTime;
        public readonly GameObject[] Variants = new GameObject[GateFinishes.Length];
        public int Finish { get; private set; }
        public GameObject ActiveDoor => Variants[Finish];
        public int OpeningFrame = -1;

        public Gate(Transform root) => this.root = root;

        public void SetFinish(int finish)
        {
            Finish = finish;
            for (int i = 0; i < Variants.Length; i++)
                if (Variants[i])
                    Variants[i].SetActive(i == finish);
        }

        public void Open(AnimationClip clip)
        {
            if (ActiveDoor)
                Sample(clip, ActiveDoor, clip.length);
            OpeningFrame = -1;
        }

        public void FinishOpening(AnimationClip clip)
        {
            if (OpeningFrame >= 0)
                Open(clip);
        }

        public void PlayEffect(GameObject prefab, float startTime)
        {
            ClearEffect();
            effect = InstantiateVisual(prefab, root);
            effect.SetActive(true);
            effectTime = startTime;
            effectParticles = effect.GetComponentsInChildren<ParticleSystem>();
            foreach (var particle in effectParticles)
                particle.Simulate(0f, false, true, false);
        }

        public void SampleEffect(float time)
        {
            if (!effect || time <= effectTime)
                return;
            foreach (var particle in effectParticles)
                particle.Simulate(time - effectTime, false, false, false);
            effectTime = time;
        }

        public void ClearEffect()
        {
            if (!effect)
                return;
            effect.SetActive(false);
            Object.Destroy(effect);
            effect = null;
            effectParticles = null;
        }
    }

    private sealed class VisualTrack<T> where T : Key
    {
        public readonly GameObject Instance;
        public readonly T[] Keys;
        public readonly ParticleSystem[] Particles;
        public readonly Gallop.TrailCustomDataSurpporter[] TrailCustomData;
        public readonly Renderer[] Renderers;
        public readonly Animator[] Animators;
        public readonly MirrorReflection[] Mirrors;
        public readonly PoolWater[] Water;
        public Material[] BackgroundTimeMaterials;
        public Dictionary<string, Transform> BackgroundChildren;
        public (Renderer Renderer, int Slot, Material Material)[] GrassMaterials;
        public bool Sampled;
        public bool UseTimeScale;
        public int[] OriginalSortingOrders;
        public int[] OriginalSortingLayers;
        public Material[][] EffectMaterials;
        public Material[][] SeekMaterials;
        public MaterialPropertyBlock[] SeekColors;
        public MaterialPropertyBlock[] OriginalEffectColors;
        public readonly List<(Material Material, int Property, Color Original)> EffectMaterialColors = new();
        public bool EffectColorApplied;
        public bool UseGachaSubColor;
        public bool[] Emission;
        public (ParticleSystem Particle, bool WithChildren)[] ParticleSimulation;
        public int NextParticleKey;
        public EffectKey ParticlePlayKey;
        public float ParticlePlaybackTime;
        public int? ParticlePauseFrame;
        public int ParticlePausedFrames;
        public int NextAttachmentKey;
        public EffectKey EffectAttachmentKey, LastEffectLocatorKey;
        public (Transform Node, VisualTrack<EffectKey> Parent) EffectAttachment, LastEffectLocator;
        public T Previous;
        public Vector3 LocalPosition;
        public Quaternion LocalRotation;
        public Vector3 LocalScale;

        public VisualTrack(GameObject instance, T[] keys)
        {
            Instance = instance;
            Keys = keys;
            var transform = instance.transform;
            LocalPosition = transform.localPosition;
            LocalRotation = transform.localRotation;
            LocalScale = transform.localScale;
            Particles = instance.GetComponentsInChildren<ParticleSystem>(true);
            TrailCustomData = instance.GetComponentsInChildren<Gallop.TrailCustomDataSurpporter>(true);
            Renderers = instance.GetComponentsInChildren<Renderer>(true);
            Animators = instance.GetComponentsInChildren<Animator>(true);
            Mirrors = instance.GetComponentsInChildren<MirrorReflection>(true);
            Water = instance.GetComponentsInChildren<PoolWater>(true);
        }
    }

    [Serializable] private class JumpEvent { public int JumpFrame; }
    [Serializable] private class GateEvent
    {
        public int CharaIndex;
        public bool IsChangeGate;
        public bool IsGateOpen;
        public bool IsPlayEffect;
    }
    [Serializable] private class GateZoomEvent
    {
        public Vector3[] OffsetPosArray;
        public int EndFrame;
    }
    [Serializable] private class GatePanEvent
    {
        public Vector3[] TargetPosArray;
        public int[] PanFrameArray;
        public int[] ShakeStopFrameArray;
        public int CountBorder;
        public int SkipForEndFrame;
        public GatePanCurve Less5Curve;
        public GatePanCurve More5Curve;
    }
    [Serializable] private class GatePanCurve { public GatePanKey[] keys; }
    [Serializable] private class GatePanKey
    {
        public float time, value, inTangent, outTangent, inWeight, outWeight;
        public WeightedMode weightedMode;
    }
    [Serializable] private class CommandEvent { public string StringParam; public float FloatParam; }
    [Serializable] private class ShakeEvent { public float time; public float amountX, amountY, amountZ, power; }
}
