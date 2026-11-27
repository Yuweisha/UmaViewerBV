using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;

public partial class UmaViewerUI
{
    public ScrollRect GachaCharactersList;
    public ScrollRect GachaCostumeList;

    private const int GachaWidth = 1080;
    private const int GachaHeight = 1920;
    private const float GachaStep = GachaCutInPlayer.PlaybackStep;
    private Canvas gachaCanvas;
    private RenderTexture gachaTexture;
    private RenderTexture gachaFreeTexture;
    private RawImage gachaViewport;
    private Image gachaFade;
    private Material gachaPresentation;
    private Scene gachaScene;
    private string gachaReturnScene;
    private bool gachaActive;
    private bool gachaReturning;
    private Canvas gachaProgressCanvas;
    private int gachaProgressOrder;
    private Coroutine gachaLoad;
    private GachaCutInPlayer gachaPlayer;
    private readonly Stack<IEnumerator> gachaSteps = new Stack<IEnumerator>();
    private bool gachaReady;
    private GachaCutInPlayer.ReplayResources gachaReplayResources;
    private Slider gachaPlaybackProgress;
    private SliderControl gachaPlaybackControl;
    private RectTransform gachaPlaybackBar;
    private Text gachaPlaybackSeconds;
    private Canvas gachaControlsCanvas;
    private FreeCam gachaFreeCamera;
    private Text gachaCameraLabel;
    private Text gachaFallbackName;
    private Text gachaCameraHelp;
    private bool gachaPaused;
    private bool gachaSeeking;
    private int gachaPlaybackTick;
    private int gachaDurationTicks;
    private int gachaRequestedTick = -1;
    private readonly List<GachaCutInPlayer.PlaybackState> gachaPlaybackStates = new List<GachaCutInPlayer.PlaybackState>();
#if UNITY_ANDROID && !UNITY_EDITOR
    private ScreenOrientation gachaReturnOrientation;
#endif
    internal bool IsGachaPlaybackActive => gachaActive;
    private RenderTexture GachaPlaybackTexture => gachaFreeCamera ? gachaFreeTexture : gachaTexture;

    internal void LoadGachaMenu()
    {
        if (!GachaCharactersList || GachaCharactersList.content.childCount != 0)
            return;

        var cards = UmaDatabaseController.Instance.ReadGachaCaptureCards()
            .Select(row => (Card: Convert.ToInt32(row["id"]), Character: Convert.ToInt32(row["chara_id"])))
            .Where(row => GachaCutInLoader.HasUniqueCut(row.Card))
            .GroupBy(row => row.Character).OrderBy(group => group.Key);
        foreach (var group in cards)
        {
            var character = Main.Characters.FirstOrDefault(c => c.Id == group.Key && !c.IsMob);
            if (character == null)
                continue;
            var costumes = group.Select(row => row.Card).OrderBy(card => card).ToArray();
            var item = Instantiate(UmaContainerPrefab, GachaCharactersList.content).GetComponent<UmaUIContainer>();
            UIPressScaleFeedback.AddTo(item.gameObject);
            item.UseLocalizedText();
            item.Id = character.Id.ToString();
            item.Name = item.name = character.Id + " " + character.GetName();
            item.Image.sprite = character.Icon ? character.Icon : CharaIconDefault;
            item.Image.enabled = true;
            item.Button.onClick.AddListener(() =>
            {
                HighlightChildImage(GachaCharactersList.content, item);
                ListGachaCostumes(costumes);
            });
        }
    }

    private void ListGachaCostumes(IEnumerable<int> cards)
    {
        foreach (Transform child in GachaCostumeList.content)
        {
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }
        var database = UmaDatabaseController.Instance;
        foreach (int card in cards)
        {
            var data = database.ReadGachaCard(card);
            int dressId = Convert.ToInt32(data["race_dress_id"]);
            var costume = Main.Costumes.FirstOrDefault(c => c.MasterDressId == dressId);
            var item = Instantiate(UmaContainerCostumePrefab, GachaCostumeList.content).GetComponent<UmaUIContainer>();
            UIPressScaleFeedback.AddTo(item.gameObject);
            item.UseLocalizedText();
            item.Id = card.ToString();
            item.name = "Gacha card " + card;
            item.Name = $"{card} {database.ReadGachaCardTitle(card)}";
            item.Image.sprite = costume?.Icon ? costume.Icon : CostumeIconDefault;
            item.Image.enabled = true;
            item.Button.onClick.AddListener(() =>
            {
                HighlightChildImage(GachaCostumeList.content, item);
                OpenGachaPlayback(card);
            });
        }
        GachaCostumeList.verticalNormalizedPosition = 1f;
    }

    private void OpenGachaPlayback(int card)
    {
        OpenGachaPlayback(new[] { card }, 9001, null);
    }

    private void OpenGachaPlayback(IReadOnlyList<int> cards, int opening,
        IReadOnlyList<GachaCutInLoader.Selection> selections, LiveCharacterLoadData openingActor = null)
    {
        if (gachaActive || UmaAssetManager.LoadCoroutine != null)
            return;
#if UNITY_ANDROID && !UNITY_EDITOR
        gachaReturnOrientation = Screen.orientation;
        Screen.orientation = ScreenOrientation.Portrait;
#endif
        gachaActive = true;
        gachaSetPlayback = selections != null;
        gachaSteps.Push(PlayGachaCutIn(cards, opening, selections, openingActor));
        gachaLoad = StartCoroutine(RunGachaPlayback());
    }

    private IEnumerator RunGachaPlayback()
    {
        yield return null;
        while (gachaSteps.Count != 0)
        {
            object next = null;
            bool moved = false;
            Exception failure = null;
            try
            {
                var step = gachaSteps.Peek();
                moved = step.MoveNext();
                if (moved)
                    next = step.Current;
                else
                    (gachaSteps.Pop() as IDisposable)?.Dispose();
            }
            catch (Exception error) { failure = error; }
            if (failure != null)
            {
                gachaLoad = null;
                FailGachaPlayback(failure);
                yield break;
            }
            if (!moved)
                continue;
            if (next is IEnumerator nested)
                gachaSteps.Push(nested);
            else
                yield return next;
        }
        gachaLoad = null;
        CloseGachaPlayback();
    }

    private IEnumerator PlayGachaCutIn(IReadOnlyList<int> cards, int opening,
        IReadOnlyList<GachaCutInLoader.Selection> selections, LiveCharacterLoadData openingActor)
    {
        var loading = UmaSceneController.instance;
        yield return loading.ShowLoadingScreen();
        var renderSize = new Vector2Int(GachaWidth, GachaHeight);
#if UNITY_ANDROID && !UNITY_EDITOR
        float orientationDeadline = Time.realtimeSinceStartup + 5f;
        while (Screen.width > Screen.height)
        {
            if (Time.realtimeSinceStartup >= orientationDeadline)
                throw new TimeoutException("Android did not switch to portrait for CutIn playback.");
            yield return null;
        }
        yield return null;
        renderSize = GetGachaRenderSize(Screen.width, Screen.height);
#endif
        loading.CavansInstance.GetComponent<Canvas>().sortingOrder = 600;
        gachaProgressCanvas = loading.LoadingProgressPanel.GetComponentInParent<Canvas>();
        gachaProgressOrder = gachaProgressCanvas.sortingOrder;
        gachaProgressCanvas.sortingOrder = 601;
        var previousScene = SceneManager.GetActiveScene();
        var sun = RenderSettings.sun;
        var skybox = RenderSettings.skybox;
        var ambientMode = RenderSettings.ambientMode;
        var ambientLight = RenderSettings.ambientLight;
        var ambientProbe = RenderSettings.ambientProbe;
        float reflectionIntensity = RenderSettings.reflectionIntensity;
        gachaReturnScene = previousScene.name;
        gachaScene = SceneManager.CreateScene("GachaPlayback");
        var template = Builder.AnimationCamera;
        var roots = new HashSet<GameObject>
        {
            gameObject.transform.root.gameObject,
            Main.transform.root.gameObject,
            canvasScaler.transform.root.gameObject,
            template.transform.root.gameObject
        };
        var globals = FindObjectOfType<UmaViewerGlobalShader>();
        if (globals)
            roots.Add(globals.transform.root.gameObject);
        if (RenderSettings.sun)
            roots.Add(RenderSettings.sun.transform.root.gameObject);
        foreach (var listener in FindObjectsOfType<AudioListener>())
            roots.Add(listener.transform.root.gameObject);
        foreach (var root in roots)
            if (root.scene == previousScene)
                SceneManager.MoveGameObjectToScene(root, gachaScene);
        Builder.UnloadUma();
        Builder.UnloadProp();
        Builder.ClearLiveSounds();
        foreach (var camera in FindObjectsOfType<Camera>())
            camera.enabled = false;
        if (CameraOrbit.instance)
            CameraOrbit.instance.enabled = false;
        foreach (var canvas in canvasScaler.transform.root.GetComponentsInChildren<Canvas>(true))
            canvas.enabled = false;
        SceneManager.SetActiveScene(gachaScene);
        RenderSettings.sun = sun;
        RenderSettings.skybox = skybox;
        RenderSettings.ambientMode = ambientMode;
        RenderSettings.ambientLight = ambientLight;
        RenderSettings.ambientProbe = ambientProbe;
        RenderSettings.reflectionIntensity = reflectionIntensity;
        yield return SceneManager.UnloadSceneAsync(previousScene);
        yield return null;
        UmaAssetManager.UnloadAllBundle();
        globals?.ApplyDefaultsForLive();
        if (globals)
            globals.enabled = false;
        CreateGachaSurface(renderSize);
        gachaTexture = new RenderTexture(renderSize.x, renderSize.y, 24) { name = "CutIn menu" };
        gachaTexture.Create();
        gachaViewport.texture = gachaTexture;
        gachaReplayResources = new GachaCutInPlayer.ReplayResources();
        using (gachaReplayResources.Enter())
            gachaPlayer = new GachaCutInPlayer(template, gachaTexture, cards, opening,
                replayResources: gachaReplayResources, selections: selections, openingActor: openingActor);
        loading.LoadingProgressChange(0, 1);
        yield return gachaPlayer.PrepareCharacterAssets((completed, total, message) =>
        {
            if (gachaActive)
                loading.LoadingProgressChange(completed, total, message);
        });

        gachaPlayer.SetMuted(true);
        gachaPlayer.SetAudioPaused(true);
        using (gachaReplayResources.Enter())
            gachaPlayer.Load();
        gachaPlayer.CaptureCamera.enabled = false;
        if (!gachaSetPlayback)
            yield return WarmupGachaPlayback(cards[0]);
        if (!gachaSetPlayback)
        using (var timingResources = new GachaCutInPlayer.ReplayResources(gachaReplayResources.Clips))
        {
            GachaCutInPlayer timing;
            using (timingResources.Enter())
                timing = gachaPlayer.CreateTimingProbe(gachaTexture, timingResources);
            using (timing)
            {
                gachaPlaybackStates.Add(timing.CapturePlayback());
                var durationBudget = System.Diagnostics.Stopwatch.StartNew();
                while (!timing.AtFinalName)
                {
                    if (timing.Finished || gachaDurationTicks >= 120 * GachaCutInPlayer.PlaybackFrameRate)
                        throw new InvalidOperationException($"Card {cards[0]} did not reach its final name hold.");
                    using (timingResources.Enter())
                        timing.Tick(GachaStep);
                    gachaDurationTicks++;
                    gachaPlaybackStates.Add(timing.CapturePlayback());
                    if (durationBudget.Elapsed.TotalMilliseconds >= 8)
                    {
                        yield return null;
                        durationBudget.Restart();
                    }
                }
            }
        }
        yield return null;

        gachaPlayer.RenderFrame(gachaTexture);
        gachaFade.color = gachaPlayer.Fade;
        gachaViewport.color = Color.white;
        gachaCanvas.enabled = true;
        CreateGachaPlaybackBar();
        loading.LoadingProgressChange(1, 1);
        yield return loading.HideLoadingScreen();
        gachaProgressCanvas.sortingOrder = gachaProgressOrder;
        gachaPlayer.SetMuted(false);
        gachaPlayer.SetAudioPaused(false);
        gachaReady = true;
        float elapsed = 0f;
        while (!gachaPlayer.Finished)
        {
            yield return null;
            if (Input.GetKeyDown(KeyCode.Space))
                gachaPlaybackControl.TogglePause();
#if !UNITY_ANDROID || UNITY_EDITOR
            if (Input.GetKeyDown(KeyCode.C))
                ToggleGachaFreeCamera();
            if (gachaFreeCamera && Input.GetKeyDown(KeyCode.Home))
                ResetGachaFreeCamera();
#endif
            if (gachaPlaybackControl.is_Outed)
            {
                int releasedTick = Mathf.RoundToInt(gachaPlaybackProgress.value * gachaDurationTicks);
                if (releasedTick != gachaPlaybackTick)
                    gachaRequestedTick = releasedTick;
                gachaPlaybackControl.is_Outed = false;
                if (gachaFreeCamera && EventSystem.current)
                    EventSystem.current.SetSelectedGameObject(null);
            }
            if (gachaRequestedTick >= 0)
            {
                SeekGachaPlayback();
                elapsed = 0f;
            }
            bool paused = gachaPaused || gachaPlaybackControl.IsDragging;
            gachaPlayer.SetAudioPaused(paused);
            UpdateGachaPlaybackBar();
            if (paused)
            {
                elapsed = 0f;
                if (gachaFreeCamera)
                    gachaPlayer.RenderFrame(GachaPlaybackTexture);
                continue;
            }
            elapsed += Time.unscaledDeltaTime;
            while (elapsed >= GachaStep && !gachaPlayer.Finished)
            {
                StepGachaPlayback();
                gachaPlaybackTick = Mathf.Min(gachaPlaybackTick + 1, gachaDurationTicks);
                elapsed -= GachaStep;
            }
            if (gachaPlayer.Finished)
                yield break;
            gachaPlayer.SynchronizeAudio();
            gachaPlayer.RenderFrame(GachaPlaybackTexture);
            gachaFade.color = gachaPlayer.Fade;
            if (gachaFallbackName) gachaFallbackName.text = gachaPlayer.FallbackName ?? "";
        }
    }

    private void StepGachaPlayback()
    {
        using (gachaReplayResources.Enter())
        {
            if (gachaSetPlayback && gachaPlayer.WaitingForTap && !gachaPlayer.ShowingResultName)
                gachaPlayer.Advance();
            gachaPlayer.Tick(GachaStep);
        }
    }

    private IEnumerator WarmupGachaPlayback(int card)
    {
        var loader = gachaPlayer.CaptureLoader;
        int commentChapter = Enumerable.Range(0, loader.Cuts.Count)
            .First(index => loader.Cuts[index].Name == GachaCutInLoader.CharacterCommentCut);
        if (!gachaReady)
            UmaSceneController.instance.LoadingProgressChange(0, commentChapter);
        var warmupBudget = System.Diagnostics.Stopwatch.StartNew();
        for (int warmup = 0; !gachaPlayer.Finished &&
            loader.Cuts[gachaPlayer.CaptureChapter].Name != GachaCutInLoader.CharacterCommentCut; warmup++)
        {
            if (warmup >= 60 * 30)
                throw new TimeoutException($"Card {card} did not enter the comment cut.");
            int previousChapter = gachaPlayer.CaptureChapter;
            using (gachaReplayResources.Enter())
            {
                if (gachaPlayer.WaitingForTap)
                    gachaPlayer.Advance();
                else
                    gachaPlayer.Tick(GachaStep);
            }
            if (!gachaPlayer.Finished &&
                loader.Cuts[gachaPlayer.CaptureChapter].Name != GachaCutInLoader.CharacterCommentCut &&
                (gachaPlayer.CaptureChapter != previousChapter || warmupBudget.Elapsed.TotalMilliseconds >= 8))
            {
                if (!gachaReady)
                    UmaSceneController.instance.LoadingProgressChange(gachaPlayer.CaptureChapter, commentChapter);
                yield return null;
                warmupBudget.Restart();
            }
        }
        if (gachaPlayer.Finished)
            throw new InvalidOperationException($"Card {card} ended before its comment cut.");

    }

    private void SeekGachaPlayback()
    {
        gachaSeeking = true;
        gachaPlayer.SetAudioPaused(true);
        gachaPlayer.SetMuted(true);
        int target = gachaRequestedTick;
        gachaRequestedTick = -1;
        if (target != gachaPlaybackTick)
        {
            using (gachaReplayResources.Enter())
                gachaPlayer.SeekPlayback(gachaPlaybackStates[target]);
            gachaPlaybackTick = target;
        }
        gachaPlayer.RenderFrame(GachaPlaybackTexture);
        gachaFade.color = gachaPlayer.Fade;
        gachaPlayer.SetAudioPaused(gachaPaused || gachaPlaybackControl.IsDragging);
        gachaPlayer.SynchronizeAudio();
        gachaPlayer.SetMuted(false);
        gachaSeeking = false;
    }

    private void CreateGachaPlaybackBar()
    {
        var controls = new GameObject("CutIn controls", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster));
        gachaControlsCanvas = controls.GetComponent<Canvas>();
        gachaControlsCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        gachaControlsCanvas.sortingOrder = gachaCanvas.sortingOrder + 1;
        var scaler = controls.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(800f, 600f);
        scaler.matchWidthOrHeight = .5f;
        gachaPlaybackBar = new GameObject("CutIn playback controls", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        gachaPlaybackBar.SetParent(controls.transform, false);
        gachaPlaybackBar.anchorMin = Vector2.zero;
        gachaPlaybackBar.anchorMax = new Vector2(1f, 0f);
        gachaPlaybackBar.pivot = new Vector2(.5f, 0f);
        gachaPlaybackBar.sizeDelta = new Vector2(0f, 25f);
        gachaPlaybackBar.GetComponent<Image>().color = Color.clear;
        gachaPlaybackProgress = SliderControl.CreateProgressBar(gachaPlaybackBar);
        gachaPlaybackControl = gachaPlaybackProgress.GetComponent<SliderControl>();
        gachaPlaybackControl.PauseRequested = () =>
        {
            gachaPaused = !gachaPaused;
            gachaPlaybackControl.SetPaused(gachaPaused);
            gachaPlayer?.SetAudioPaused(gachaPaused || gachaPlaybackControl.IsDragging || gachaSeeking);
        };
        var pauseButton = gachaPlaybackControl.PauseButton;
#if UNITY_ANDROID && !UNITY_EDITOR
        pauseButton.gameObject.SetActive(true);
#endif
        pauseButton.onClick = new Button.ButtonClickedEvent();
        pauseButton.onClick.AddListener(gachaPlaybackControl.TogglePause);
        gachaPlaybackSeconds = LiveViewerUI.CreateProgressSeconds(gachaPlaybackBar);
#if !UNITY_ANDROID || UNITY_EDITOR
        gachaCameraLabel = CreateGachaCameraButton("Free Camera", 8f, 130f, ToggleGachaFreeCamera);
        CreateGachaCameraButton("Reset", 142f, 54f, ResetGachaFreeCamera);
        gachaCameraHelp = LiveViewerUI.CreateProgressText("Camera controls", (RectTransform)controls.transform,
            Config.Instance.Language == Language.Cn ? HachimiGachaResources.Font : null);
        gachaCameraHelp.rectTransform.anchorMin = gachaCameraHelp.rectTransform.anchorMax = new Vector2(0f, 0f);
        gachaCameraHelp.rectTransform.pivot = Vector2.zero;
        gachaCameraHelp.rectTransform.anchoredPosition = new Vector2(8f, 32f);
        gachaCameraHelp.rectTransform.sizeDelta = new Vector2(400f, 48f);
        gachaCameraHelp.fontSize = 14;
        gachaCameraHelp.alignment = TextAnchor.LowerLeft;
        gachaCameraHelp.text = UiLocalization.T("C: camera mode | Home: reset to current shot\nWASD: move | Q/E, R/F: height | Shift: faster\nHold right mouse: look | Wheel: move forward/back");
        gachaCameraHelp.gameObject.SetActive(false);
#endif
        gachaPlaybackProgress.onValueChanged.AddListener(value =>
        {
            gachaRequestedTick = Mathf.Clamp(Mathf.RoundToInt(value * gachaDurationTicks), 0, gachaDurationTicks);
            UpdateGachaPlaybackSeconds(gachaRequestedTick);
        });
        if (gachaSetPlayback)
        {
            pauseButton.transform.SetParent(gachaPlaybackBar, false);
            gachaPlaybackProgress.gameObject.SetActive(false);
            gachaPlaybackSeconds.text = "";
        }
        UpdateGachaPlaybackBar();
    }

    private void UpdateGachaPlaybackBar()
    {
        if (!gachaSetPlayback && !gachaPlaybackControl.IsDragging && gachaRequestedTick < 0)
            gachaPlaybackProgress.SetValueWithoutNotify((float)gachaPlaybackTick / gachaDurationTicks);
        float current = gachaPlaybackControl.IsDragging || gachaRequestedTick >= 0
            ? gachaPlaybackProgress.value * gachaDurationTicks : gachaPlaybackTick;
        if (!gachaSetPlayback)
            UpdateGachaPlaybackSeconds(current);
#if UNITY_ANDROID && !UNITY_EDITOR
        bool visible = true;
#else
        bool visible = gachaPaused || gachaPlaybackControl.IsDragging;
#endif
        gachaPlaybackBar.anchoredPosition = new Vector2(0f, visible ? 0f : -gachaPlaybackBar.rect.height);
    }

    private void UpdateGachaPlaybackSeconds(float tick)
    {
        gachaPlaybackSeconds.text = string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "{0:F1}s / {1:F1}s", Mathf.Floor(tick * GachaStep * 10f) / 10f,
            Mathf.Floor(gachaDurationTicks * GachaStep * 10f) / 10f);
    }

    private Text CreateGachaCameraButton(string label, float left, float width, UnityEngine.Events.UnityAction action)
    {
        var button = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
        var rect = (RectTransform)button.transform;
        rect.SetParent(gachaPlaybackBar, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0f, .5f);
        rect.pivot = new Vector2(0f, .5f);
        rect.anchoredPosition = new Vector2(left, 0f);
        rect.sizeDelta = new Vector2(width, 22f);
        button.GetComponent<Image>().color = new Color(.15f, .15f, .15f, .9f);
        button.GetComponent<Button>().onClick.AddListener(action);
        var text = LiveViewerUI.CreateProgressText("Label", rect,
            Config.Instance.Language == Language.Cn ? HachimiGachaResources.Font : null);
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
        text.text = UiLocalization.T(label);
        return text;
    }

    private void ToggleGachaFreeCamera()
    {
        if (!gachaReady || gachaPlayer == null)
            return;
        if (gachaFreeCamera)
        {
            gachaPlayer.ObservationView = null;
            gachaFreeCamera.enabled = false;
            Destroy(gachaFreeCamera.gameObject);
            gachaFreeCamera = null;
        }
        else
        {
            if (!gachaFreeTexture)
            {
                float scale = Mathf.Min(1f, (float)GachaWidth / Mathf.Min(Screen.width, Screen.height));
                gachaFreeTexture = new RenderTexture(Mathf.RoundToInt(Screen.width * scale),
                    Mathf.RoundToInt(Screen.height * scale), 24) { name = "CutIn free view" };
                gachaFreeTexture.Create();
            }
            var observer = new GameObject("CutIn free camera");
            observer.transform.SetParent(gachaPlayer.CaptureCamera.transform.parent, false);
            gachaFreeCamera = observer.AddComponent<FreeCam>();
            gachaFreeCamera.movementSpeed = 2f;
            gachaFreeCamera.fastMovementSpeed = 10f;
            gachaFreeCamera.zoomSensitivity = 2f;
            gachaFreeCamera.fastZoomSensitivity = 10f;
            gachaFreeCamera.UseUnscaledTime = true;
            gachaFreeCamera.InputAllowed = () => gachaReady && Application.isFocused &&
                !gachaPlaybackControl.IsDragging && !RectTransformUtility.RectangleContainsScreenPoint(
                    gachaPlaybackBar, Input.mousePosition);
            ResetGachaFreeCamera();
            gachaPlayer.ObservationView = observer.transform;
        }
        gachaViewport.texture = GachaPlaybackTexture;
        gachaViewport.GetComponent<AspectRatioFitter>().aspectRatio = gachaFreeCamera
            ? (float)Screen.width / Screen.height : (float)gachaTexture.width / gachaTexture.height;
        gachaCameraLabel.text = UiLocalization.T(gachaFreeCamera ? "Animation Camera" : "Free Camera");
        gachaCameraHelp.gameObject.SetActive(gachaFreeCamera);
        if (EventSystem.current)
            EventSystem.current.SetSelectedGameObject(null);
        gachaPlayer.RenderFrame(GachaPlaybackTexture);
    }

    private void ResetGachaFreeCamera()
    {
        if (!gachaFreeCamera || gachaPlayer == null)
            return;
        var camera = gachaPlayer.CaptureCamera;
        gachaFreeCamera.StopLooking();
        gachaFreeCamera.transform.SetPositionAndRotation(camera.transform.position, camera.transform.rotation);
        gachaPlayer.ObservationFieldOfView = camera.fieldOfView;
        if (EventSystem.current)
            EventSystem.current.SetSelectedGameObject(null);
    }

    private void TapGachaPlayback()
    {
        if (!gachaReady || gachaPlayer == null)
            return;
        if (!gachaSeeking && !gachaFreeCamera)
        {
            if (gachaPlayer.AtFinalName)
                CloseGachaPlayback();
            else if (gachaSetPlayback && gachaPlayer.WaitingForTap)
                gachaPlayer.Advance();
        }
    }

    private void FailGachaPlayback(Exception error)
    {
        Debug.LogException(error);
        string message = UiLocalization.T("Unable to play the selected CutIn.") + "\n" + error.Message;
        if (gachaScene.IsValid() && !gachaReturning)
            SceneManager.sceneLoaded += ShowFailure;
        else
            ShowMessage(message, UIMessageType.Error);
        CloseGachaPlayback();

        void ShowFailure(Scene scene, LoadSceneMode mode)
        {
            SceneManager.sceneLoaded -= ShowFailure;
            Instance.ShowMessage(message, UIMessageType.Error);
        }
    }

    internal static Vector2Int GetGachaRenderSize(int width, int height)
    {
        int renderWidth = Mathf.Min(width, GachaWidth);
        return new Vector2Int(renderWidth, Mathf.RoundToInt((float)renderWidth * height / width));
    }

    private void CreateGachaSurface(Vector2Int renderSize)
    {
        var root = new GameObject("CutIn playback", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster));
        var display = new GameObject("CutIn presentation camera").AddComponent<Camera>();
        display.transform.SetParent(root.transform, false);
        display.clearFlags = CameraClearFlags.SolidColor;
        display.backgroundColor = Color.black;
        display.cullingMask = 0;
        gachaCanvas = root.GetComponent<Canvas>();
        gachaCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        gachaCanvas.sortingOrder = 500;
        gachaCanvas.enabled = false;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = renderSize;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

        var backdrop = new GameObject("Black margins", typeof(RectTransform), typeof(Image), typeof(Button));
        backdrop.transform.SetParent(root.transform, false);
        var backdropRect = (RectTransform)backdrop.transform;
        backdropRect.anchorMin = Vector2.zero;
        backdropRect.anchorMax = Vector2.one;
        backdropRect.offsetMin = backdropRect.offsetMax = Vector2.zero;
        backdrop.GetComponent<Image>().color = Color.black;
        var tap = backdrop.GetComponent<Button>();
        tap.transition = Selectable.Transition.None;
        tap.onClick.AddListener(TapGachaPlayback);

        var surface = new GameObject("CutIn viewport", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
        surface.transform.SetParent(backdrop.transform, false);
        gachaViewport = surface.GetComponent<RawImage>();
        gachaPresentation = new Material(Resources.Load<Shader>("GachaPresentation"));
        gachaViewport.material = gachaPresentation;
        gachaViewport.color = Color.clear;
        gachaViewport.raycastTarget = false;
        var fitter = surface.GetComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fitter.aspectRatio = (float)renderSize.x / renderSize.y;

        if (gachaSetPlayback)
        {
            gachaFallbackName = LiveRenderUI.Text("", surface.transform, Vector2.zero, new Vector2(0, 60), 32);
            gachaFallbackName.font = HachimiGachaResources.Font;
            gachaFallbackName.rectTransform.anchorMin = new Vector2(0, .16f);
            gachaFallbackName.rectTransform.anchorMax = new Vector2(1, .16f);
            gachaFallbackName.gameObject.AddComponent<Outline>();
        }
        gachaFade = new GameObject("CutIn fade", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        gachaFade.rectTransform.SetParent(surface.transform, false);
        gachaFade.rectTransform.anchorMin = Vector2.zero;
        gachaFade.rectTransform.anchorMax = Vector2.one;
        gachaFade.rectTransform.offsetMin = gachaFade.rectTransform.offsetMax = Vector2.zero;
        gachaFade.color = Color.clear;
        gachaFade.raycastTarget = false;
    }

    private void CloseGachaPlayback()
    {
        bool returnToMenu = gachaActive && gachaScene.IsValid() && !gachaReturning;
        bool wasActive = gachaActive;
        gachaActive = false;
        gachaReady = false;
        gachaSetPlayback = false;
#if UNITY_ANDROID && !UNITY_EDITOR
        if (wasActive)
            Screen.orientation = gachaReturnOrientation;
#endif
        if (gachaLoad != null)
            StopCoroutine(gachaLoad);
        gachaLoad = null;
        while (gachaSteps.Count != 0)
            (gachaSteps.Pop() as IDisposable)?.Dispose();
        if (gachaFreeCamera)
            gachaFreeCamera.enabled = false;
        gachaFreeCamera = null;
        gachaPlayer?.Dispose();
        gachaPlayer = null;
        gachaReplayResources?.Dispose();
        gachaReplayResources = null;
        gachaPaused = gachaSeeking = false;
        gachaPlaybackTick = gachaDurationTicks = 0;
        gachaRequestedTick = -1;
        gachaPlaybackStates.Clear();
        gachaPlaybackProgress = null;
        gachaPlaybackControl = null;
        gachaPlaybackBar = null;
        gachaPlaybackSeconds = null;
        gachaCameraLabel = null;
        gachaCameraHelp = null;
        if (gachaControlsCanvas)
        {
            gachaControlsCanvas.gameObject.SetActive(false);
            Destroy(gachaControlsCanvas.gameObject);
        }
        gachaControlsCanvas = null;
        if (gachaPresentation)
            Destroy(gachaPresentation);
        gachaPresentation = null;
        if (gachaProgressCanvas)
            gachaProgressCanvas.sortingOrder = gachaProgressOrder;
        gachaProgressCanvas = null;
        if (wasActive && UmaSceneController.instance && !gachaReturning)
            UmaSceneController.instance.ClearLoadingScreen();
        if (gachaCanvas)
        {
            gachaCanvas.gameObject.SetActive(false);
            Destroy(gachaCanvas.gameObject);
        }
        if (gachaTexture)
        {
            gachaTexture.Release();
            Destroy(gachaTexture);
        }
        if (gachaFreeTexture)
        {
            gachaFreeTexture.Release();
            Destroy(gachaFreeTexture);
        }
        gachaCanvas = null;
        gachaTexture = null;
        gachaFreeTexture = null;
        gachaViewport = null;
        gachaFade = null;
        if (returnToMenu)
        {
            gachaReturning = true;
            SceneManager.LoadScene(gachaReturnScene);
        }
    }
}
