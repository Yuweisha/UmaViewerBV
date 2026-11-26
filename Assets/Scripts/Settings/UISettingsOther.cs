#if UNITY_STANDALONE || UNITY_EDITOR
using SFB;
#endif

#if UNITY_EDITOR
using UnityEditor;
#endif

using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class UISettingsOther : MonoBehaviour
{
    public Button UpdateDBButton;
    public TMP_Dropdown RegionDropdown;
    public TMP_Dropdown WorkModeDropdown;
    public TMP_Dropdown LanguageDropdown;
    public TMP_Dropdown FrameRateDropdown;
    public TMP_Dropdown CdnDropdown;
    private GameObject _runtimeFrameRateRow;
    private GameObject _runtimeCdnRow;
    private InputField _customFrameRateInput;
    private IEnumerator _updateResVerCoroutine;

    public void ApplySettings()
    {
        EnsureLanguageOptions();
        EnsureFrameRateDropdown();
        EnsureCdnDropdown();
        RefreshCdnDropdown();
        UmaViewerDownload.ApplyCdnMode();

        WorkModeDropdown.SetValueWithoutNotify((int)Config.Instance.WorkMode);
        RegionDropdown.SetValueWithoutNotify((int)Config.Instance.Region);
        LanguageDropdown.SetValueWithoutNotify(LanguageToDropdownValue(Config.Instance.Language));
        if (FrameRateDropdown != null) RefreshFrameRateOptions();
        if (_customFrameRateInput != null) _customFrameRateInput.SetTextWithoutNotify(Config.Instance.TargetFrameRate.ToString());
        UmaViewerMain.ApplyFrameRateLimit();
        UpdateDBButton.interactable = (Config.Instance.WorkMode == WorkMode.Standalone);
    }

    private void EnsureLanguageOptions()
    {
        if (LanguageDropdown == null)
            return;

        var options = new List<string>
        {
            "简体中文",
            "English",
            "日本語"
        };

        bool optionsMatch = LanguageDropdown.options.Count == options.Count;
        if (optionsMatch)
        {
            for (int i = 0; i < options.Count; i++)
            {
                if (LanguageDropdown.options[i].text != options[i])
                {
                    optionsMatch = false;
                    break;
                }
            }
        }

        if (!optionsMatch)
        {
            LanguageDropdown.ClearOptions();
            LanguageDropdown.AddOptions(options);
        }
    }

    private int LanguageToDropdownValue(Language language)
    {
        switch (language)
        {
            case Language.Cn:
                return 0;
            case Language.En:
                return 1;
            case Language.Jp:
            default:
                return 2;
        }
    }

    private Language DropdownValueToLanguage(int value)
    {
        switch (value)
        {
            case 0:
                return Language.Cn;
            case 1:
                return Language.En;
            case 2:
            default:
                return Language.Jp;
        }
    }

    private void EnsureFrameRateDropdown()
    {
        if (FrameRateDropdown != null || LanguageDropdown == null)
        {
            return;
        }

        var sourceRow = LanguageDropdown.transform.parent;
        var parent = sourceRow.parent;
        _runtimeFrameRateRow = Instantiate(sourceRow.gameObject, parent);
        _runtimeFrameRateRow.name = "FrameRate";
        _runtimeFrameRateRow.transform.SetSiblingIndex(sourceRow.GetSiblingIndex() + 1);

        var label = _runtimeFrameRateRow.transform.GetChild(0).GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null)
        {
            label.text = "Frame Rate";
        }

        FrameRateDropdown = _runtimeFrameRateRow.GetComponentInChildren<TMP_Dropdown>(true);
        FrameRateDropdown.name = "FrameRateDropdown";
        FrameRateDropdown.ClearOptions();
        FrameRateDropdown.AddOptions(UISettingsGraphics.FrameRateLabels());
        FrameRateDropdown.onValueChanged = new TMP_Dropdown.DropdownEvent();
        FrameRateDropdown.onValueChanged.AddListener(ChangeFrameRate);
        var customInput = UISettingsGraphics.AddFrameRateInput(parent, RefreshFrameRateOptions);
        _customFrameRateInput = customInput;
        FrameRateDropdown.onValueChanged.AddListener(index => customInput.SetTextWithoutNotify(UISettingsGraphics.FrameRateAt(index).ToString()));
        customInput.transform.parent.SetSiblingIndex(_runtimeFrameRateRow.transform.GetSiblingIndex() + 1);

        var parentRect = parent as RectTransform;
        var sourceRect = sourceRow as RectTransform;
        if (parentRect != null && sourceRect != null)
        {
            parentRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, parentRect.rect.height + sourceRect.rect.height + 30);
            LayoutRebuilder.MarkLayoutForRebuild(parentRect);
        }
    }

    private void EnsureCdnDropdown()
    {
        if (CdnDropdown != null || LanguageDropdown == null)
            return;

        var sourceRow = LanguageDropdown.transform.parent;
        var parent = sourceRow.parent;

        _runtimeCdnRow = Instantiate(sourceRow.gameObject, parent);
        _runtimeCdnRow.name = "CdnMode";
        _runtimeCdnRow.transform.SetSiblingIndex(sourceRow.GetSiblingIndex() + 1);

        var label = _runtimeCdnRow.transform.GetChild(0)
            .GetComponentInChildren<TextMeshProUGUI>(true);

        if (label != null)
            label.text = "CDN";

        CdnDropdown = _runtimeCdnRow.GetComponentInChildren<TMP_Dropdown>(true);
        CdnDropdown.name = "CdnDropdown";
        CdnDropdown.ClearOptions();
        CdnDropdown.AddOptions(new List<string> { "Cy", "CN" });

        CdnDropdown.onValueChanged = new TMP_Dropdown.DropdownEvent();
        CdnDropdown.onValueChanged.AddListener(ChangeCdnMode);

        var parentRect = parent as RectTransform;
        var sourceRect = sourceRow as RectTransform;

        if (parentRect != null && sourceRect != null)
        {
            parentRect.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical,
                parentRect.rect.height + sourceRect.rect.height + 30);

            LayoutRebuilder.MarkLayoutForRebuild(parentRect);
        }
    }

    private void RefreshCdnDropdown()
    {
        if (CdnDropdown == null)
            return;

        int value = string.Equals(
            Config.Instance.CdnMode,
            "CN",
            System.StringComparison.OrdinalIgnoreCase) ? 1 : 0;

        CdnDropdown.SetValueWithoutNotify(value);
        CdnDropdown.RefreshShownValue();
    }

    public void ChangeCdnMode(int value)
    {
        string mode = value == 1 ? "CN" : "Cy";

        if (Config.Instance.CdnMode == mode)
            return;

        Config.Instance.CdnMode = mode;
        Config.Instance.UpdateConfig(false);

        UmaViewerDownload.ApplyCdnMode();
    }

    public void ChangeFrameRate(int value)
    {
        var frameRate = UISettingsGraphics.FrameRateAt(value);
        if (Config.Instance.TargetFrameRate != frameRate)
        {
            Config.Instance.TargetFrameRate = frameRate;
            Config.Instance.UpdateConfig(false);
        }
        UmaViewerMain.ApplyFrameRateLimit();
    }

    private void RefreshFrameRateOptions()
    {
        FrameRateDropdown.ClearOptions();
        FrameRateDropdown.AddOptions(UISettingsGraphics.FrameRateLabels());
        FrameRateDropdown.SetValueWithoutNotify(UISettingsGraphics.FrameRateIndex());
        FrameRateDropdown.RefreshShownValue();
    }

    public void ChangeLanguage(int lang)
    {
        Language language = DropdownValueToLanguage(lang);
        if (Config.Instance.Language != language)
        {
            Config.Instance.Language = language;
            Config.Instance.UpdateConfig(false);

            // Rebuild all localized lists and reload English translations when
            // necessary without requiring the application process to restart.
            Scene activeScene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(activeScene.buildIndex);
        }
    }

    public void ChangeRegion(int region)
    {
        if ((int)Config.Instance.Region != region)
        {
            Config.Instance.Region = (Region)region;
            Config.Instance.UpdateConfig(false);
            StartCoroutine(UmaViewerUI.Instance.ApplyGraphicsSettings());
        }
    }

    public void ChangeWorkMode(int mode)
    {
        if ((int)Config.Instance.WorkMode != mode)
        {
            Config.Instance.WorkMode = (WorkMode)mode;
            if (Application.platform == RuntimePlatform.Android && Config.Instance.WorkMode == WorkMode.Standalone)
                Config.Instance.MainPath = Application.persistentDataPath;
            Config.Instance.UpdateConfig(true);
        }
    }

    public void UpdateGameDB()
    {
        if (_updateResVerCoroutine != null || Config.Instance.WorkMode != WorkMode.Standalone) return;
        if (UmaStandalone.IsSupportedPlatform)
        {
            if (UmaDatabaseController.TrySelectDatabasePaths(Config.Instance.MainPath, WorkMode.Standalone, out var meta, out _)
                && Path.GetFileName(meta) == "meta")
            {
                UmaViewerUI.Instance.ShowMessage("Using the installed game's meta/master databases. Update those through the game client, or select a separate empty directory for Standalone data.", UIMessageType.Default);
                return;
            }
            _updateResVerCoroutine = UpdateStandaloneDatabase();
            StartCoroutine(_updateResVerCoroutine);
            return;
        }
        Popup.Create($"Automatic database update is no longer supported until all issues with new files are resolved. Please run the game to obtain required files.", -1, 200,
            "Ok", null, "Ok");
    }

    private IEnumerator UpdateStandaloneDatabase()
    {
        UpdateDBButton.interactable = false;
        try
        {
            bool updated = false;
            yield return UmaStandalone.Rebuild(UmaViewerUI.Instance.ShowMessage, result => updated = result);
            if (updated) UmaViewerUI.Instance.ShowMessage("Database updated. Restart the application to refresh the displayed lists.", UIMessageType.Success);
        }
        finally
        {
            _updateResVerCoroutine = null;
            UpdateDBButton.interactable = Config.Instance.WorkMode == WorkMode.Standalone;
        }
    }

    public void ChangeDataPath()
    {
#if UNITY_EDITOR
        // Editor 里用 Unity 自带的选文件夹（不会走 SFB 的原生 .bundle，因此不会 DllNotFoundException）
        var p = EditorUtility.OpenFolderPanel("Select Folder", Config.Instance.MainPath, "");
        if (!string.IsNullOrEmpty(p) && p != Config.Instance.MainPath)
        {
            Config.Instance.MainPath = p;
            Config.Instance.UpdateConfig(true);
        }

#elif UNITY_STANDALONE
        // Standalone 运行时仍走 SFB（如果以后要打包到 mac App，这里建议再换成更现代的方案）
        var path = StandaloneFileBrowser.OpenFolderPanel("Select Folder", Config.Instance.MainPath, false);
        if (path != null && path.Length > 0 && !string.IsNullOrEmpty(path[0]) && path[0] != Config.Instance.MainPath)
        {
            Config.Instance.MainPath = path[0];
            Config.Instance.UpdateConfig(true);
        }
#elif UNITY_ANDROID
        ShowDataPathInput();
#else
        UmaViewerUI.Instance.ShowMessage("Not supported on this platform", UIMessageType.Warning);
#endif
    }

    private void ShowDataPathInput()
    {
        TMP_InputField input = null;
        var popup = Popup.Create("Enter the game data folder containing meta and master/master.mdb.", 800, 300, "Save", () =>
        {
            string path = input.text.Trim();
            if (!Directory.Exists(path) || (Config.Instance.WorkMode == WorkMode.Default &&
                !UmaDatabaseController.TrySelectDatabasePaths(path, WorkMode.Default, out _, out _)))
            {
                UmaViewerUI.Instance.ShowMessage("Cannot read meta and master/master.mdb from this folder. Check the path and Android storage access.", UIMessageType.Error);
                return;
            }
            Config.Instance.MainPath = path;
            Config.Instance.UpdateConfig(true);
        }, "Cancel");
        var message = popup.transform.Find("Panel/TextField").GetComponentInChildren<TMP_Text>();
        message.rectTransform.anchorMin = new Vector2(0, 1);
        message.rectTransform.anchorMax = Vector2.one;
        message.rectTransform.pivot = new Vector2(0.5f, 1);
        message.rectTransform.anchoredPosition = new Vector2(0, -10);
        message.rectTransform.sizeDelta = new Vector2(-40, 90);
        var field = new GameObject("DataPath", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(TMP_InputField));
        field.transform.SetParent(message.transform.parent, false);
        field.layer = message.gameObject.layer;
        var rect = (RectTransform)field.transform;
        rect.anchorMin = new Vector2(0, 0.45f);
        rect.anchorMax = new Vector2(1, 0.45f);
        rect.sizeDelta = new Vector2(-40, 55);
        var text = Instantiate(message, field.transform);
        text.name = "Text";
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = new Vector2(10, 5);
        text.rectTransform.offsetMax = new Vector2(-10, -5);
        text.enableAutoSizing = false;
        text.fontSize = 24;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.enableWordWrapping = false;
        text.raycastTarget = false;
        input = field.GetComponent<TMP_InputField>();
        input.targetGraphic = field.GetComponent<Image>();
        input.textViewport = rect;
        input.textComponent = text;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.SetTextWithoutNotify(Config.Instance.MainPath);
    }

    public void OpenConfig()
    {
#if UNITY_STANDALONE || UNITY_EDITOR
        if (File.Exists(Config.configPath))
        {
            Process.Start(new ProcessStartInfo()
            {
                FileName = Config.configPath,
                UseShellExecute = true
            });
        }
#else
        UmaViewerUI.Instance.ShowMessage("Not supported on this platform", UIMessageType.Warning);
#endif
    }

    public void UnloadAllBundle() => UmaAssetManager.UnloadAllBundle(true);
}
