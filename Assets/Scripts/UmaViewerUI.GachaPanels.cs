using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Live 标签 → Gacha 分页：育成卡 CutIn 演出。
///
/// 布局照游戏内的「演出」页：
///   左栏  角色列表（缩略图 + ID + 名字）
///   右栏  该角色的育成卡 = 服装列表（服装名 + 缩略图）
///   点击一张卡 → 弹窗里只有一个槽位，指定「用哪个角色播这段 CutIn」→ 播放
///
/// 演出是实时演算，资源在 3d/motion/cutin/card/：
///   body/crd{cardId}_001/anm_cti_crd{cardId}_001_01..NN   身体动作（多段）
///   camera/..._NN_cam   facial/..._NN_face/ear   position/..._NN_pos   bg/..._bg
///   UmaContainerCharacter.LoadAnimation 认识 "_cti_crd"，拿到第 01 段会自动串后续段。
///
/// 舞台由 card_data 的 chara_id / bg_id 决定：
///   3d/env/cutin/cutin{charaId}_{bgId:00}/pfb_env_cutin{charaId}_{bgId:00}_00
/// </summary>
public partial class UmaViewerUI
{
    private const string CutinCardMotionRoot = "3d/motion/cutin/card/body/";
    private const float GachaRowHeight = 54f;

    private ScrollRect _gachaList;
    private Transform _gachaCharaColumn;
    private Transform _gachaCardColumn;
    private GameObject _gachaStage;
    private string _gachaStagePath;
    private GameObject _gachaPopup;
    private int _gachaPopupCharaId = -1;

    /// <summary>建立 Gacha 分页：左角色、右育成卡。</summary>
    public void LoadGachaPanels()
    {
        Debug.LogWarning($"[Gacha] LoadGachaPanels 进入 chars={Main.Characters?.Count ?? 0} costumes={Main.Costumes?.Count ?? 0}");

        _gachaList = FindGachaListScrollRect();

        RectTransform host;

        if (_gachaList != null && _gachaList.content != null)
        {
            host = _gachaList.content;
            Debug.LogWarning($"[Gacha] 容器 = ScrollRect '{_gachaList.name}'.content size={host.rect.size}");
        }
        else
        {
            // 场景里的 Gacha List 只是一个 Image 面板，没有 ScrollRect —— 直接把面板本身当容器
            RectTransform panel = FindGachaListPanel();

            if (panel == null)
            {
                Debug.LogWarning("[Gacha] 既没找到 ScrollRect 也没找到 Gacha List 面板，分页保持空白");
                return;
            }

            host = panel;
            Debug.LogWarning($"[Gacha] 容器 = 面板 '{panel.name}' size={panel.rect.size}");
        }

        for (int i = host.childCount - 1; i >= 0; i--)
            Destroy(host.GetChild(i).gameObject);
        // 面板被 tab 切换时是整块显示/隐藏的，这里只保证两栏铺满它
        host.pivot = new Vector2(0.5f, 1f);

        _gachaCharaColumn = MakeColumn("GachaCharaColumn", host, new Vector2(0f, 0f), new Vector2(0.42f, 1f));
        _gachaCardColumn = MakeColumn("GachaCardColumn", host, new Vector2(0.44f, 0f), new Vector2(1f, 1f));

        BuildGachaCharaColumn();

        Debug.LogWarning($"[Gacha] 分页建立完成 chars={Main.Characters?.Count ?? 0} costumes={Main.Costumes?.Count ?? 0}");
    }

    private Transform MakeColumn(string name, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup));

        VerticalLayoutGroup vlg = go.GetComponent<VerticalLayoutGroup>();
        vlg.spacing = 4f;
        vlg.padding = new RectOffset(4, 4, 4, 4);
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        go.transform.SetParent(parent, false);

        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = new Vector2(8f, 8f);
        rect.offsetMax = new Vector2(-8f, -8f);

        return rect;
    }

    /// <summary>左栏：有育成卡的角色。</summary>
    private void BuildGachaCharaColumn()
    {
        if (_gachaCharaColumn == null || Main.Characters == null)
            return;

        for (int i = _gachaCharaColumn.childCount - 1; i >= 0; i--)
            Destroy(_gachaCharaColumn.GetChild(i).gameObject);

        const float rowHeight = 52f;
        float y = 0f;
        int count = 0;

        foreach (CharaEntry chara in Main.Characters)
        {
            if (chara == null || chara.IsMob)
                continue;

            int charaId = chara.Id;

            // 只列有服装（= 有育成卡）的角色
            if (Main.Costumes == null || !Main.Costumes.Any(c => c.CharaId == charaId))
                continue;

            if (MakeContainer(UmaContainerPrefab, _gachaCharaColumn, chara.Icon,
                    $"{charaId}  {chara.GetName()}", () => SelectGachaChara(charaId), count) == null)
                break;

            count++;
        }

        Debug.LogWarning($"[Gacha] 角色栏 {count} 项");
    }

    private void SelectGachaChara(int charaId)
    {
        BuildGachaCardColumn(charaId);
    }

    /// <summary>右栏：该角色的育成卡（每套服装一张）。</summary>
    private void BuildGachaCardColumn(int charaId)
    {
        if (_gachaCardColumn == null || Main.Costumes == null)
            return;

        for (int i = _gachaCardColumn.childCount - 1; i >= 0; i--)
            Destroy(_gachaCardColumn.GetChild(i).gameObject);

        const float rowHeight = 60f;
        float y = 0f;
        int count = 0;

        foreach (CostumeEntry costume in Main.Costumes.Where(c => c.CharaId == charaId))
        {
            int cardId = costume.MasterDressId > 0
                ? costume.MasterDressId
                : (int.TryParse(costume.Id, out int parsed) ? parsed : 0);

            if (cardId == 0)
                continue;

            // 只列真的有 CutIn 动作的卡（动作名 crd{cardId}_001）
            string clipPath = $"{CutinCardMotionRoot}crd{cardId}_001/anm_cti_crd{cardId}_001_01";

            if (!Main.AbList.ContainsKey(clipPath))
                continue;

            string label = string.IsNullOrEmpty(costume.DressName) ? costume.Id : costume.DressName;
            int capturedCard = cardId;
            int capturedChara = charaId;

            if (MakeContainer(UmaContainerCostumePrefab, _gachaCardColumn, costume.Icon, label,
                    () => ShowGachaCutinPopup(capturedCard, capturedChara), count) == null)
                break;

            count++;
        }

        Debug.LogWarning($"[Gacha] 角色 {charaId} 的育成卡 {count} 张");
    }

    /// <summary>用角色页同一套 prefab 建一行，样式自动一致（白色按钮/缩略图/高亮）。</summary>
    private UmaUIContainer MakeContainer(GameObject prefab, Transform parent, Sprite icon,
        string label, Action onClick, int index)
    {
        if (prefab == null || parent == null)
            return null;

        var container = Instantiate(prefab, parent).GetComponent<UmaUIContainer>();

        if (container == null)
            return null;

        container.Name = label;

        // prefab 上的 LayoutElement / 布局组会把行钉成固定尺寸（被压成小方块就是这个原因），
        // 拆掉后由栏上的 VerticalLayoutGroup 统一拉成整栏宽、固定行高。
        LayoutElement element = container.GetComponent<LayoutElement>();

        if (element != null)
            Destroy(element);

        foreach (LayoutGroup group in container.GetComponents<LayoutGroup>())
            Destroy(group);

        RectTransform rect = (RectTransform)container.transform;
        rect.sizeDelta = new Vector2(rect.sizeDelta.x, GachaRowHeight);

        if (icon != null && container.Image != null)
        {
            container.Image.sprite = icon;
            container.Image.enabled = true;
        }

        if (container.Button != null)
            container.Button.onClick.AddListener(() => onClick());

        return container;
    }

    /// <summary>点击一张卡 → 单槽位窗口，选择用哪个角色播。</summary>
    private void ShowGachaCutinPopup(int cardId, int defaultCharaId)
    {
        CloseGachaPopup();

        var panel = new GameObject("GachaCutinPopup", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(transform, false);

        RectTransform panelRect = (RectTransform)panel.transform;
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(440f, 230f);

        panel.GetComponent<Image>().color = new Color(0.12f, 0.15f, 0.20f, 0.98f);
        _gachaPopup = panel;
        _gachaPopupCharaId = defaultCharaId;

        var title = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI));
        title.transform.SetParent(panel.transform, false);
        RectTransform titleRect = (RectTransform)title.transform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -12f);
        titleRect.sizeDelta = new Vector2(-24f, 30f);

        TextMeshProUGUI titleText = title.GetComponent<TextMeshProUGUI>();
        titleText.text = $"CutIn · 卡 {cardId}";
        titleText.font = TMP_Settings.defaultFontAsset;
        titleText.fontSize = 20f;
        titleText.color = Color.white;
        titleText.alignment = TextAlignmentOptions.Center;
        titleText.raycastTarget = false;

        TMP_Dropdown dropdown = CreateDropdown(panel.transform, new Vector2(0f, 20f), new Vector2(320f, 34f));

        if (dropdown != null)
        {
            var options = new List<TMP_Dropdown.OptionData>();
            var ids = new List<int>();
            int initial = 0;

            foreach (CharaEntry chara in Main.Characters)
            {
                if (chara == null || chara.IsMob)
                    continue;

                if (chara.Id == defaultCharaId)
                    initial = options.Count;

                options.Add(new TMP_Dropdown.OptionData($"{chara.Id} {chara.GetName()}"));
                ids.Add(chara.Id);
            }

            dropdown.ClearOptions();
            dropdown.AddOptions(options);
            dropdown.SetValueWithoutNotify(initial);
            dropdown.RefreshShownValue();
            dropdown.onValueChanged.AddListener(index =>
            {
                if (index >= 0 && index < ids.Count)
                    _gachaPopupCharaId = ids[index];
            });

            FitDropdownTemplate(dropdown);
        }

        LiveRenderUI.Button(UiLocalization.T("Play"), panel.transform, new Vector2(-85f, -75f), () =>
        {
            int playAs = _gachaPopupCharaId;
            CloseGachaPopup();
            PlayGachaCutin(cardId, playAs);
        });

        LiveRenderUI.Button(UiLocalization.T("Cancel"), panel.transform, new Vector2(85f, -75f), CloseGachaPopup);
    }

    private void CloseGachaPopup()
    {
        if (_gachaPopup != null)
        {
            Destroy(_gachaPopup);
            _gachaPopup = null;
        }
    }

    /// <summary>加载指定角色并播放这张卡的 CutIn 第 01 段，同时换上它的舞台。</summary>
    private void PlayGachaCutin(int cardId, int playAsCharaId)
    {
        // 卡本身记着归属角色与舞台编号
        int ownerCharaId = cardId / 100;
        int bgId = 0;

        var cards = UmaDatabaseController.Instance != null
            ? UmaDatabaseController.Instance.CardData
            : null;

        if (cards != null)
        {
            var row = cards.Find(r => Convert.ToInt32(r["id"]) == cardId);

            if (row != null)
            {
                ownerCharaId = Convert.ToInt32(row["chara_id"]);
                bgId = Convert.ToInt32(row["bg_id"]);
            }
        }

        if (playAsCharaId <= 0)
            playAsCharaId = ownerCharaId;

        CharaEntry chara = Main.Characters != null
            ? Main.Characters.FirstOrDefault(c => c.Id == playAsCharaId)
            : null;

        if (chara == null)
        {
            ShowMessage($"找不到角色 {playAsCharaId}（卡 {cardId}）", UIMessageType.Warning);
            return;
        }

        // 服装跟着卡走（不是跟着播放角色），动作才对得上
        CostumeEntry costume = Main.Costumes != null
            ? Main.Costumes.FirstOrDefault(c => c.CharaId == ownerCharaId
                && (c.MasterDressId == cardId || c.Id == cardId.ToString()))
            : null;

        string costumeId = costume != null ? costume.Id : string.Empty;
        string clipPath = $"{CutinCardMotionRoot}crd{cardId}_001/anm_cti_crd{cardId}_001_01";

        if (!Main.AbList.TryGetValue(clipPath, out UmaDatabaseEntry clip) || clip == null)
        {
            ShowMessage($"卡 {cardId} 没有 CutIn 动作", UIMessageType.Warning);
            return;
        }

        string stagePath = $"3d/env/cutin/cutin{ownerCharaId}_{bgId:00}/pfb_env_cutin{ownerCharaId}_{bgId:00}_00";
        Debug.LogWarning($"[Gacha] 卡 {cardId} 归属 {ownerCharaId} 舞台 {stagePath} 播放角色 {playAsCharaId} 服装 {costumeId}");

        var load = new List<UmaDatabaseEntry>();
        load.AddRange(Main.AbChara.Where(a => a.Name.StartsWith(UmaDatabaseController.BodyPath)
                                           && a.Name.Contains(playAsCharaId.ToString())));
        load.AddRange(Main.AbChara.Where(a => a.Name.StartsWith(UmaDatabaseController.HeadPath)
                                           && a.Name.Contains(playAsCharaId.ToString())));
        load.Add(clip);

        if (Main.AbList.TryGetValue("3d/animator/drivenkeylocator", out var locator))
            load.Add(locator);

        if (Main.AbList.TryGetValue(stagePath, out UmaDatabaseEntry stageEntry) && stageEntry != null)
            load.Add(stageEntry);
        else
            Debug.LogWarning($"[Gacha] 找不到舞台 {stagePath}，只放角色");

        Builder.UnloadUma();
        UmaAssetManager.PreLoadAndRun(load, delegate
        {
            StartCoroutine(Builder.LoadUma(chara, costumeId, false, CurrentHeadCostumeId));
            StartCoroutine(PlayWhenReady(clip, stagePath));
        });
    }

    private System.Collections.IEnumerator PlayWhenReady(UmaDatabaseEntry clip, string stagePath)
    {
        SpawnStage(stagePath);

        for (int i = 0; i < 300; i++)
        {
            var container = Builder.CurrentUMAContainer;

            if (container != null)
            {
                container.LoadAnimation(clip);
                LoadedAnimation();
                yield break;
            }

            yield return null;
        }

        Debug.LogWarning("[Gacha] 角色加载超时，未能播放 " + clip.Name);
    }

    /// <summary>换上 CutIn 舞台，换卡时销毁旧的。</summary>
    private void SpawnStage(string stagePath)
    {
        if (string.IsNullOrEmpty(stagePath))
            return;

        if (_gachaStage != null && _gachaStagePath == stagePath)
            return;

        if (_gachaStage != null)
        {
            Destroy(_gachaStage);
            _gachaStage = null;
        }

        try
        {
            _gachaStage = Builder.LoadAssetPath(stagePath, null);
            _gachaStagePath = stagePath;
        }
        catch (Exception error)
        {
            Debug.LogWarning($"[Gacha] 舞台实例化失败 {stagePath}: {error.Message}");
            _gachaStagePath = null;
        }
    }

    private bool _gachaTabHooked;

    /// <summary>
    /// 给场景里的 Gacha tab 挂一个监听：点一下就把分页建起来。
    /// 启动时序不可靠（Start 里调用实测没生效），改成点击时懒加载最稳。
    /// </summary>
    public void HookGachaTab()
    {
        if (_gachaTabHooked)
            return;

        foreach (Button button in UnityEngine.Object.FindObjectsOfType<Button>(true))
        {
            Transform t = button.transform;

            for (int depth = 0; depth < 4 && t != null; depth++)
            {
                if (t.name == "Gacha")
                {
                    button.onClick.AddListener(() => LoadGachaPanels());
                    _gachaTabHooked = true;
                    Debug.LogWarning($"[Gacha] 已给 tab '{t.name}' 挂上监听");
                    return;
                }

                t = t.parent;
            }
        }

        Debug.LogWarning("[Gacha] 没找到 Gacha tab 按钮，无法挂监听");
    }

    /// <summary>按名字找 Gacha List 面板（tab 切换 SetActive 的就是它）。</summary>
    private RectTransform FindGachaListPanel()
    {
        // 面板挂在 Canvas 的另一个分支下，不在 UmaViewerUI 的子树里，所以整场景找
        foreach (var rect in UnityEngine.Object.FindObjectsOfType<RectTransform>(true))
        {
            if (rect.name == "Gacha List")
                return rect;
        }

        return null;
    }

    /// <summary>Gacha List 面板下的 ScrollRect（场景里没有对应的 public 字段）。</summary>
    private ScrollRect FindGachaListScrollRect()
    {
        if (_gachaList != null)
            return _gachaList;

        foreach (var rect in UnityEngine.Object.FindObjectsOfType<ScrollRect>(true))
        {
            Transform t = rect.transform;

            for (int depth = 0; depth < 4 && t != null; depth++)
            {
                if (t.name.IndexOf("Gacha", StringComparison.OrdinalIgnoreCase) >= 0)
                    return rect;

                t = t.parent;
            }
        }

        return null;
    }
}
