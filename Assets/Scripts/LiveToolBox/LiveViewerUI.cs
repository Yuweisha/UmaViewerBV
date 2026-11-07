using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UI;
using Toggle=UnityEngine.UI.Toggle;

public class LiveViewerUI : MonoBehaviour
{
    public static LiveViewerUI Instance;

    public UnityEngine.UI.Slider ProgressBar;

    public RectTransform BottonUITransform;

    public Dropdown FrameRateDropDown;

    public GameObject RecordingUI;

    public Text RecordingText;

    public Text LyricsText;

    public List<UmaLyricsData> CurrentLyrics = new List<UmaLyricsData>();

    private UnityEngine.UI.Text progressSeconds;
    private int displayedCurrentTenth = -1;
    private int displayedTotalTenth = -1;

    float targetHeight = 0;
    float height;
    bool playbackPaused;
    bool cursorHidden;
    private void Awake()
    {
        if (Config.Instance == null)
        {
            new Config();
        }
        // Graphics controls live in the main settings panel.
        if(FrameRateDropDown!=null)FrameRateDropDown.transform.parent.gameObject.SetActive(false);
        RuntimeGraphicsSettings.Apply();
        UmaViewerMain.ApplyFrameRateLimit();

        height = BottonUITransform.rect.height;
        targetHeight = 0;
        Invoke(nameof(HideSlider), 1.5f);
        Instance = this;
        CreatePlaybackProgress();
        var subtitleToggle=GetComponentsInChildren<Toggle>(true);
        foreach(var toggle in subtitleToggle)
            if(toggle.name=="Lyrics")
            {
                toggle.name="Subtitles";
                toggle.onValueChanged=new Toggle.ToggleEvent();
                toggle.SetIsOnWithoutNotify(LiveSubtitleSettings.Enabled);
                var label=toggle.GetComponentInChildren<Text>();
                if(label!=null)label.text="Subtitles";
                toggle.onValueChanged.AddListener(value=>LiveSubtitleSettings.SetEnabled(value));
            }
        LiveSubtitleSettings.Changed+=RefreshSubtitleToggle;
        //TrueProgressBar = (UnityEngine.UIElements.Slider)ProgressBar;
    }

    private void Update()
    {
        var director = Gallop.Live.Director.instance;
        if (director == null || !director._isLiveSetup)
        {
            RestoreCursor();
            playbackPaused = false;
            return;
        }

        // Space owns the persistent playback state. Pointer events only seek.
        if (!director.IsOfflineRendering && Input.GetKeyDown(KeyCode.Space))
        {
            director.sliderControl?.TogglePause();
        }

        bool paused = director.IsPlaybackPaused ||
            (director.sliderControl != null && director.sliderControl.is_Touched);
        if (paused == playbackPaused) return;
        playbackPaused = paused;
        ApplyPlaybackVisibility(paused);
    }

    private void ApplyPlaybackVisibility(bool paused)
    {
        CancelInvoke();
        targetHeight = paused ? 0f : -height;
        UnityEngine.Cursor.lockState = paused ? CursorLockMode.None : CursorLockMode.Locked;
        UnityEngine.Cursor.visible = paused;
        cursorHidden = !paused;
    }

    private void RestoreCursor()
    {
        if (!cursorHidden && UnityEngine.Cursor.visible) return;
        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;
        cursorHidden = false;
    }

    private void OnDisable()
    {
        RestoreCursor();
    }

    private void OnDestroy()
    {
        LiveSubtitleSettings.Changed-=RefreshSubtitleToggle;
        RestoreCursor();
        if (Instance == this) Instance = null;
    }

    // The scene reserves 200 canvas units on each side of the progress slider.
    private void CreatePlaybackProgress()
    {
        if (ProgressBar == null || BottonUITransform == null) return;
        progressSeconds = CreateProgressText("ProgressSeconds", BottonUITransform);
        var rect = progressSeconds.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(1, 0.5f);
        rect.pivot = new Vector2(1, 0.5f);
        rect.anchoredPosition = new Vector2(-8, 0);
        rect.sizeDelta = new Vector2(184, 24);
        progressSeconds.alignment = TextAnchor.MiddleRight;
        progressSeconds.text = "0.0s / 0.0s";

    }

    private UnityEngine.UI.Text CreateProgressText(string name, RectTransform parent)
    {
        var textObject = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Text));
        textObject.layer = parent.gameObject.layer;
        textObject.transform.SetParent(parent, false);
        var text = textObject.GetComponent<UnityEngine.UI.Text>();
        text.font = FrameRateDropDown != null && FrameRateDropDown.captionText != null
            ? FrameRateDropDown.captionText.font : null;
        if (text.font == null && LyricsText != null) text.font = LyricsText.font;
        if (text.font == null) text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 16;
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }


    private void LateUpdate()
    {
        if (progressSeconds == null) return;
        var director = Gallop.Live.Director.instance;
        float total = director != null ? director.totalTime : 0f;
        if (float.IsNaN(total) || float.IsInfinity(total) || total < 0) total = 0;
        bool seeking = director != null && director.sliderControl != null &&
            (director.sliderControl.is_Touched || director.sliderControl.is_Outed);
        float current = director == null ? 0f : seeking
            ? ProgressBar.normalizedValue * total : director._liveCurrentTime;
        if (float.IsNaN(current) || float.IsInfinity(current)) current = 0;
        int currentTenth = Mathf.FloorToInt(Mathf.Clamp(current, 0, total) * 10f);
        int totalTenth = Mathf.FloorToInt(total * 10f);
        if (currentTenth == displayedCurrentTenth && totalTenth == displayedTotalTenth) return;
        displayedCurrentTenth = currentTenth;
        displayedTotalTenth = totalTenth;
        progressSeconds.text = string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "{0:F1}s / {1:F1}s", currentTenth / 10f, totalTenth / 10f);

        // Enforce the playback cursor state after other scene components update;
        // a transition-only assignment can be overwritten by another cursor user.
        var live = Gallop.Live.Director.instance;
        if (live == null || !live._isLiveSetup)
        {
            RestoreCursor();
            return;
        }
        if(live.IsOfflineRendering)
        {
            UnityEngine.Cursor.visible=true;UnityEngine.Cursor.lockState=CursorLockMode.None;
            targetHeight=-height;
            return;
        }
        bool paused = live.IsPlaybackPaused ||
            (live.sliderControl != null && live.sliderControl.is_Touched);
        UnityEngine.Cursor.visible = paused;
        UnityEngine.Cursor.lockState = paused ? CursorLockMode.None : CursorLockMode.Locked;
        targetHeight = paused ? 0f : -height;
    }

    public void OnMouse(bool isEnter)
    {
        // Moving the mouse during playback must never reveal the timeline bar.
        // While paused the bar remains visible; there is no auto-hide timer.
        var director = Gallop.Live.Director.instance;
        bool paused = director != null && director._isLiveSetup &&
            (director.IsPlaybackPaused ||
             (director.sliderControl != null && director.sliderControl.is_Touched));
        CancelInvoke();
        targetHeight = paused ? 0f : -height;
    }

    private void FixedUpdate()
    {
        BottonUITransform.anchoredPosition = Vector2.Lerp(BottonUITransform.anchoredPosition, new Vector2(0, targetHeight), Time.fixedDeltaTime * 5);
    }

    private void HideSlider()
    {
        targetHeight = -height;
    }

    public void SetFrameRate(int fps)
    {
        Config.Instance.TargetFrameRate = UISettingsGraphics.FrameRateAt(fps);
        Config.Instance.UpdateConfig(false);
        UmaViewerMain.ApplyFrameRateLimit();
    }

    private void ApplyFrameRateOptions()
    {
        if (FrameRateDropDown == null) return;

        FrameRateDropDown.ClearOptions();
        FrameRateDropDown.AddOptions(UISettingsGraphics.FrameRateLabels());
        FrameRateDropDown.SetValueWithoutNotify(UISettingsGraphics.FrameRateIndex());
        FrameRateDropDown.RefreshShownValue();
    }

    public void UpdateLyrics(float time, int sheetVariationId)
    {
        var text = UmaUtility.GetCurrentLyrics(time, CurrentLyrics, sheetVariationId);
        if(LyricsText.text!=text)LyricsText.text = text;
        var director=Gallop.Live.Director.instance;
        if(director!=null)director.SetLiveSubtitleText(text,LyricsText);
    }
    private void RefreshSubtitleToggle(bool enabled)
    {
        foreach(var toggle in GetComponentsInChildren<Toggle>(true))
            if(toggle.name=="Subtitles")toggle.SetIsOnWithoutNotify(enabled);
    }
}
