using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public partial class UmaViewerUI
{
    private bool gachaSetPlayback;
    private GameObject gachaSetPanel, gachaSetPicker;
    private Dropdown gachaSetOpening;
    private const int OpeningSlot = -1;
    private LiveCharacterSelect gachaSetOpeningSlot;
    private Text gachaSetOpeningName;
    private LiveCharacterLoadData gachaSetOpeningActor;
    private Toggle gachaSetOverdrive;
    private readonly GachaCutInLoader.Selection[] gachaSetSlots = new GachaCutInLoader.Selection[10];
    private readonly Button[] gachaSetCharacterButtons = new Button[10];
    private readonly Button[] gachaSetCutInButtons = new Button[10];
    private readonly LiveCharacterSelect[] gachaSetCharacterSlots = new LiveCharacterSelect[10];
    private readonly LiveCharacterSelect[] gachaSetCutInSlots = new LiveCharacterSelect[10];
    private readonly Text[] gachaSetCharacterNames = new Text[10];
    private readonly Text[] gachaSetCutInNames = new Text[10];
    private readonly Dropdown[] gachaSetGates = new Dropdown[10];
    private List<DataRow> gachaSetCards;
    private GachaSetRandom gachaSetRandom;
    private ScrollRect gachaSetCharacters, gachaSetCostumes;
    private int gachaSetSelectedSlot;
    private bool gachaSetPickingCutIn;

    private Font GachaSetFont => Config.Instance.Language == Language.Cn ? HachimiGachaResources.Font : LiveSelectInfoText.font;
    private Text gachaSetCount;
    private Button gachaSetRemove;

    public Button GachaSetEntry;

    public void ShowGachaSet()
    {
        if (gachaActive || UmaAssetManager.LoadCoroutine != null)
            return;
        LiveSelectPannel.SetActive(false);
        SelectCharacterPannel.SetActive(false);
        CurrentSeletChara = null;
        gachaSetCards ??= UmaDatabaseController.Instance.ReadGachaSetCards();
        if (!gachaSetPanel)
            CreateGachaSetPanel();
        CloseGachaSetPicker();
        gachaSetPanel.SetActive(true);
        gachaSetPanel.transform.SetAsLastSibling();
        RefreshGachaSetSlots();
    }

    private void CreateGachaSetPanel()
    {
        var live = (RectTransform)LiveSelectPannel.transform;
        var panel = CreateGachaSetWindow("Gacha Set", live.parent, () => gachaSetPanel.SetActive(false));
        gachaSetPanel = panel.gameObject;
        panel.sizeDelta = live.sizeDelta;
        panel.anchoredPosition = live.anchoredPosition;
        SetGachaSetLabel(panel.Find("Title_BG"), UiLocalization.T("Gacha Set"));
        var openingLabel = GachaSetText("Opening", panel, 22);
        PlaceGachaSetItem(openingLabel.rectTransform, 15, 60, 84, 40);
        gachaSetOpening = CreateGachaSetChoice(panel, new[] { "9001", "9002", UiLocalization.T("No opening or gates") });
        PlaceGachaSetItem((RectTransform)gachaSetOpening.transform, 100, 60, 180, 40);
        var actor = CreateGachaSetSlot(panel, OpeningSlot, false);
        actor.name = "Opening actor";
        gachaSetOpeningSlot = actor.GetComponent<LiveCharacterSelect>();
        gachaSetOpeningSlot.IndexText.text = "";
        var nativeCell = LiveSelectList.content.GetComponent<GridLayoutGroup>().cellSize;
        PlaceGachaSetItem((RectTransform)actor.transform, 306, 48, nativeCell.x, nativeCell.y);
        gachaSetOpeningName = GachaSetText("Opening actor", panel, 18);
        gachaSetOpeningName.alignment = TextAnchor.MiddleLeft;
        PlaceGachaSetItem(gachaSetOpeningName.rectTransform, 440, 48, 150, 68);
        gachaSetOpening.onValueChanged.AddListener(_ =>
        {
            if (gachaSetSelectedSlot == OpeningSlot) CloseGachaSetPicker();
            RefreshGachaSetSlots();
        });
        var template = LiveSelectPannel.transform.Find("StageToggle").GetComponent<Toggle>();
        gachaSetOverdrive = Instantiate(template, panel);
        gachaSetOverdrive.name = "Overdrive";
        gachaSetOverdrive.group = null;
        gachaSetOverdrive.onValueChanged = new Toggle.ToggleEvent();
        gachaSetOverdrive.SetIsOnWithoutNotify(false);
        PlaceGachaSetItem((RectTransform)gachaSetOverdrive.transform, 605, 62, 150, 35);
        var label = gachaSetOverdrive.transform.Find("Label").GetComponent<Text>();
        label.text = UiLocalization.T("Overdrive");
        label.font = GachaSetFont;
        label.color = Color.black;
        label.fontSize = 21;
        gachaSetOverdrive.gameObject.SetActive(true);
        gachaSetOverdrive.onValueChanged.AddListener(enabled =>
        {
            if (!enabled)
                foreach (var slot in gachaSetSlots.Where(slot => slot != null))
                {
                    if (slot.Character == null)
                        slot.CutInCard = 0;
                    else
                        MatchGachaSetCutIn(slot);
                }
            CloseGachaSetPicker();
            RefreshGachaSetSlots();
        });
        CreateGachaSetGrid(panel, false, 110);
        CreateGachaSetGrid(panel, true, 392);
        var clear = CreateCompactButton(UiLocalization.T("Clear"), panel, new Vector2(60, -310), new Vector2(220, 50), 21, () =>
        {
            CloseGachaSetPicker();
            Array.Clear(gachaSetSlots, 0, gachaSetSlots.Length);
            gachaSetOpeningActor = null;
            RefreshGachaSetSlots();
        });
        clear.name = "Clear";
        clear.GetComponentInChildren<Text>().font = GachaSetFont;
        PlaceGachaSetItem((RectTransform)clear.transform, 535, 635, 200, 50);
        var random = CreateGachaSetButton("Random", panel, RandomizeGachaSet);
        PlaceGachaSetItem((RectTransform)random.transform, 278, 635, 200, 50);
        var play = CreateGachaSetButton("Play", panel, PlayGachaSet);
        PlaceGachaSetItem((RectTransform)play.transform, 20, 635, 200, 50);
    }

    private void RandomizeGachaSet()
    {
        gachaSetRandom ??= new GachaSetRandom(Main, gachaSetCards);
        if (!gachaSetRandom.TryRoll(gachaSetOverdrive.isOn, gachaSetSlots, out var actor))
        {
            ShowMessage(UiLocalization.T("No eligible gacha selections found."), UIMessageType.Warning);
            return;
        }
        CloseGachaSetPicker();
        gachaSetOpeningActor = actor;
        gachaSetOpening.SetValueWithoutNotify(UnityEngine.Random.Range(0, 2));
        RefreshGachaSetSlots();
    }

    private void CreateGachaSetGrid(RectTransform panel, bool cutIn, float top)
    {
        string title = cutIn ? "CutIn" : "Characters";
        var label = GachaSetText(title, panel, 22);
        label.alignment = TextAnchor.MiddleLeft;
        PlaceGachaSetItem(label.rectTransform, 34, top, 730, 30);
        if (!cutIn) gachaSetCount = label;
        var grid = new GameObject(title, typeof(RectTransform), typeof(GridLayoutGroup)).GetComponent<GridLayoutGroup>();
        grid.transform.SetParent(panel, false);
        grid.gameObject.layer = panel.gameObject.layer;
        var background = grid.gameObject.AddComponent<Image>();
        var nativeBackground = LiveSelectList.GetComponent<Image>();
        background.sprite = nativeBackground.sprite;
        background.type = nativeBackground.type;
        background.color = nativeBackground.color;
        background.raycastTarget = false;
        var native = LiveSelectList.content.GetComponent<GridLayoutGroup>();
        var size = native.cellSize;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 5;
        grid.childAlignment = native.childAlignment;
        grid.cellSize = size + new Vector2(0, cutIn ? 34 : 50);
        grid.spacing = new Vector2(25, 4);
        PlaceGachaSetItem((RectTransform)grid.transform, 20, top + 31, panel.rect.width - 40, 2 * grid.cellSize.y + grid.spacing.y);
        for (int index = 0; index < gachaSetSlots.Length; index++)
        {
            int slot = index;
            var cell = new GameObject("Slot " + (index + 1), typeof(RectTransform)).GetComponent<RectTransform>();
            cell.SetParent(grid.transform, false);
            cell.gameObject.layer = panel.gameObject.layer;
            var button = CreateGachaSetSlot(cell, slot, cutIn);
            PlaceGachaSetItem((RectTransform)button.transform, 0, 0, size.x, size.y);
            (cutIn ? gachaSetCutInButtons : gachaSetCharacterButtons)[index] = button;
            (cutIn ? gachaSetCutInSlots : gachaSetCharacterSlots)[index] = button.GetComponent<LiveCharacterSelect>();
            var name = GachaSetText("Empty", cell, 17);
            PlaceGachaSetItem(name.rectTransform, -6, size.y, size.x + 12, cutIn ? 34 : 22);
            (cutIn ? gachaSetCutInNames : gachaSetCharacterNames)[index] = name;
            if (cutIn) continue;
            var gate = CreateGachaSetChoice(cell, new[] { UiLocalization.T("White"), UiLocalization.T("Gold"), UiLocalization.T("Rainbow") });
            gate.name = "Gate " + (slot + 1);
            PlaceGachaSetItem((RectTransform)gate.transform, 0, size.y + 22, size.x, 28);
            gate.onValueChanged.AddListener(value =>
            {
                if (gachaSetSlots[slot] != null)
                    gachaSetSlots[slot].InitialGateFinish = value;
            });
            gachaSetGates[slot] = gate;
        }
    }

    private static void PlaceGachaSetItem(RectTransform rect, float horizontal, float vertical, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(horizontal, -vertical);
        rect.sizeDelta = new Vector2(width, height);
        rect.localScale = Vector3.one;
    }

    private Text GachaSetText(string value, Transform parent, int size)
    {
        var text = LiveRenderUI.Text(UiLocalization.T(value), parent, Vector2.zero, Vector2.zero, size);
        text.font = GachaSetFont;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = 12;
        text.resizeTextMaxSize = size;
        text.color = Color.black;
        return text;
    }

    private RectTransform CreateGachaSetWindow(string name, Transform parent, UnityEngine.Events.UnityAction closeAction)
    {
        var panel = LiveRenderUI.Panel(name, parent, Vector2.zero);
        var template = LiveSelectPannel.GetComponent<Image>();
        var image = panel.GetComponent<Image>();
        image.sprite = template.sprite;
        image.type = template.type;
        image.color = template.color;
        image.pixelsPerUnitMultiplier = template.pixelsPerUnitMultiplier;
        Instantiate(LiveSelectPannel.transform.Find("Title_BG"), panel).name = "Title_BG";
        var close = Instantiate(LiveSelectPannel.transform.Find("CancelButtton").GetComponent<Button>(), panel);
        close.onClick = new Button.ButtonClickedEvent();
        close.onClick.AddListener(closeAction);
        return panel;
    }

    private Button CreateGachaSetButton(string label, Transform parent, UnityEngine.Events.UnityAction action)
    {
        var template = LiveSelectPannel.transform.Find("PlayButtton").GetComponent<Button>();
        var button = Instantiate(template, parent);
        button.name = label;
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(action);
        SetGachaSetLabel(button.transform, UiLocalization.T(label));
        button.gameObject.SetActive(true);
        return button;
    }

    private void SetGachaSetLabel(Transform control, string value)
    {
        var text = control.GetComponentInChildren<TMPro.TMP_Text>(true);
        if (text) text.text = value;
        var legacy = control.GetComponentInChildren<Text>(true);
        if (legacy)
        {
            legacy.text = value;
            legacy.font = GachaSetFont;
        }
    }

    private Button CreateGachaSetSlot(Transform parent, int index, bool cutIn)
    {
        var slot = Instantiate(LiveSelectPrefab, parent).GetComponent<LiveCharacterSelect>();
        var button = slot.GetComponent<Button>();
        slot.name = (cutIn ? "CutIn " : "Character ") + (index + 1);
        slot.IndexText.text = (index + 1).ToString();
        slot.IndexText.font = GachaSetFont;
        button.onClick.AddListener(() => ShowGachaSetPicker(index, cutIn));
        button.gameObject.SetActive(true);
        return button;
    }

    private Dropdown CreateGachaSetChoice(Transform parent, string[] options)
    {
        var template = LiveSelectPannel.transform.Find("LiveMode").GetComponent<Dropdown>();
        var dropdown = Instantiate(template, parent);
        dropdown.onValueChanged = new Dropdown.DropdownEvent();
        dropdown.ClearOptions();
        dropdown.AddOptions(options.ToList());
        dropdown.SetValueWithoutNotify(0);
        dropdown.captionText.font = dropdown.itemText.font = GachaSetFont;
        dropdown.captionText.fontSize = dropdown.itemText.fontSize = 15;
        dropdown.captionText.resizeTextForBestFit = true;
        dropdown.captionText.resizeTextMinSize = 9;
        dropdown.captionText.resizeTextMaxSize = 15;
        dropdown.gameObject.SetActive(true);
        return dropdown;
    }

    private void RefreshGachaSetSlots()
    {
        bool hasOpening = gachaSetOpening.value != 2;
        var openingActor = hasOpening ? gachaSetOpeningActor : null;
        var actor = openingActor?.CharaEntry;
        if (hasOpening && actor == null)
            actor = Main.Characters.FirstOrDefault(item => item.Id == 9001 + gachaSetOpening.value);
        var outfit = (openingActor?.MasterDressId ?? 0) == 0 ? null : Main.Costumes.FirstOrDefault(item => item.MasterDressId == openingActor.MasterDressId);
        SetGachaSetSlotVisual(gachaSetOpeningSlot, actor, outfit);
        gachaSetOpeningSlot.GetComponent<Button>().interactable = hasOpening;
        gachaSetOpeningName.text = UiLocalization.T("Opening actor") + "\n" + (hasOpening
            ? actor?.GetName() ?? (9001 + gachaSetOpening.value).ToString() : "—");
        gachaSetCount.text = UiLocalization.T("Characters") + "   " + gachaSetSlots.Count(slot => slot?.Character != null) + " / " + gachaSetSlots.Length;
        for (int index = 0; index < gachaSetSlots.Length; index++)
        {
            var slot = gachaSetSlots[index];
            var characterCostume = (slot?.DressId ?? 0) == 0 ? null : Main.Costumes.FirstOrDefault(item => item.MasterDressId == slot.DressId);
            SetGachaSetSlotVisual(gachaSetCharacterSlots[index], slot?.Character, characterCostume);
            gachaSetCharacterNames[index].text = slot?.Character?.GetName() ?? UiLocalization.T("Empty");
            gachaSetGates[index].SetValueWithoutNotify(slot?.InitialGateFinish ?? 0);
            gachaSetGates[index].interactable = slot?.Character != null;
            int card = slot?.CutInCard ?? 0;
            var cutIn = gachaSetCutInButtons[index];
            cutIn.interactable = gachaSetOverdrive.isOn;
            var data = gachaSetCards.FirstOrDefault(row => Convert.ToInt32(row["id"]) == card);
            var costume = data == null ? null : Main.Costumes.FirstOrDefault(item => item.MasterDressId == Convert.ToInt32(data["race_dress_id"]));
            gachaSetCutInNames[index].text = card == 0 ? UiLocalization.T(slot == null ? "Empty" : "No CutIn")
                : UmaDatabaseController.Instance.ReadGachaCardTitle(card);
            var cutCharacter = data == null ? null : Main.Characters.FirstOrDefault(item => item.Id == Convert.ToInt32(data["chara_id"]));
            SetGachaSetSlotVisual(gachaSetCutInSlots[index], cutCharacter, costume);
        }
    }

    private void SetGachaSetSlotVisual(LiveCharacterSelect slot, CharaEntry character, CostumeEntry costume)
    {
        if (!slot) return;
        if (character == null)
        {
            slot.CharaImage.enabled = false;
            slot.CostumeImage.enabled = false;
            return;
        }
        slot.SetValue(character, costume?.Id ?? string.Empty, costume?.Icon ?? CostumeIconDefault, "00",
            costume?.MasterDressId ?? 0);
        slot.CharaImage.enabled = true;
        slot.CostumeImage.enabled = costume != null;
    }

    private void ShowGachaSetPicker(int index, bool cutIn)
    {
        bool opening = index == OpeningSlot;
        if ((opening && gachaSetOpening.value == 2) || (cutIn && !gachaSetOverdrive.isOn))
            return;
        gachaSetSelectedSlot = index;
        gachaSetPickingCutIn = cutIn;
        if (!gachaSetPicker)
        {
            var panel = CreateGachaSetWindow("Gacha selection", gachaSetPanel.transform, CloseGachaSetPicker);
            gachaSetPicker = panel.gameObject;
            float leftWidth = ((RectTransform)CharactersList.transform).rect.width;
            float rightWidth = ((RectTransform)CostumeList.transform).rect.width;
            float width = leftWidth + rightWidth;
            panel.sizeDelta = new Vector2(width, ((RectTransform)gachaSetPanel.transform).rect.height);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0, 1);
            var root = (RectTransform)gachaSetPanel.transform;
            var canvas = (RectTransform)root.parent;
            float leftSpace = canvas.InverseTransformPoint(root.TransformPoint(root.rect.min)).x - canvas.rect.xMin;
            panel.anchoredPosition = new Vector2(leftSpace >= width + 8 ? -width - 8 : 0, 0);
            gachaSetCharacters = CloneGachaSetList(CharactersList, panel, 0, leftWidth);
            gachaSetCostumes = CloneGachaSetList(CostumeList, panel, leftWidth, rightWidth);
            gachaSetRemove = CreateCompactButton("", panel, new Vector2(0, -panel.rect.height / 2 + 24),
                new Vector2(200, 36), 18, () =>
                {
                    if (gachaSetSelectedSlot == OpeningSlot)
                        gachaSetOpeningActor = null;
                    else if (gachaSetPickingCutIn)
                        gachaSetSlots[gachaSetSelectedSlot].CutInCard = 0;
                    else
                        gachaSetSlots[gachaSetSelectedSlot] = null;
                    RefreshGachaSetSlots();
                    CloseGachaSetPicker();
                });
        }
        gachaSetPicker.SetActive(true);
        gachaSetPicker.transform.SetAsLastSibling();
        SetGachaSetLabel(gachaSetPicker.transform.Find("Title_BG"), opening ? UiLocalization.T("Opening actor") :
            UiLocalization.T(cutIn ? "CutIn" : "Characters") + "  " + (index + 1));
        SetGachaSetLabel(gachaSetRemove.transform, UiLocalization.T(opening ? "Use original actor" : cutIn ? "No CutIn" : "Clear slot"));
        gachaSetRemove.interactable = opening ? gachaSetOpeningActor != null :
            cutIn ? (gachaSetSlots[index]?.CutInCard ?? 0) != 0 : gachaSetSlots[index] != null;
        for (int slot = 0; slot < gachaSetSlots.Length; slot++)
        {
            gachaSetCharacterSlots[slot].IndexText.color = slot == index && !cutIn ? Color.green : Color.black;
            gachaSetCutInSlots[slot].IndexText.color = slot == index && cutIn ? Color.green : Color.black;
        }
        ClearGachaSetList(gachaSetCharacters);
        ClearGachaSetList(gachaSetCostumes);
        gachaSetRandom ??= new GachaSetRandom(Main, gachaSetCards);
        var characters = Main.Characters.Where(character => !character.IsMob && (cutIn || gachaSetRandom.Outfits(character.Id).Count > 0) && (opening || gachaSetOverdrive.isOn ||
            gachaSetCards.Any(row => Convert.ToInt32(row["chara_id"]) == character.Id)));
        if (cutIn)
            characters = characters.Where(character => gachaSetCards.Any(row => Convert.ToInt32(row["chara_id"]) == character.Id &&
                Convert.ToInt32(row["default_rarity"]) == 3 &&
                GachaCutInLoader.HasUniqueCut(Convert.ToInt32(row["id"]))));
        foreach (var character in characters.OrderBy(character => character.Id))
        {
            var item = Instantiate(UmaContainerPrefab, gachaSetCharacters.content).GetComponent<UmaUIContainer>();
            item.UseLocalizedText();
            item.Name = character.Id + " " + character.GetName();
            item.Image.sprite = character.Icon ?? CharaIconDefault;
            item.Image.enabled = true;
            UIPressScaleFeedback.AddTo(item.gameObject);
            item.Button.onClick.AddListener(() =>
            {
                HighlightChildImage(gachaSetCharacters.content, item);
                if (!gachaSetPickingCutIn)
                {
                    var outfits = gachaSetRandom.Outfits(character.Id);
                    var firstCard = gachaSetCards.FirstOrDefault(row => Convert.ToInt32(row["chara_id"]) == character.Id);
                    var preferred = opening ? outfits.FirstOrDefault(a => a.CostumeId == "00") :
                        firstCard == null ? null : outfits.FirstOrDefault(a => a.MasterDressId == Convert.ToInt32(firstCard["race_dress_id"]));
                    SetGachaSetCharacter(preferred ?? outfits[0]);
                    gachaSetRemove.interactable = true;
                }
                ListGachaSetCostumes(character);
            });
        }
        gachaSetCharacters.verticalNormalizedPosition = 1f;
    }

    private void CloseGachaSetPicker()
    {
        if (!gachaSetPicker) return;
        gachaSetPicker.SetActive(false);
        if (gachaSetSelectedSlot == OpeningSlot) return;
        gachaSetCharacterSlots[gachaSetSelectedSlot].IndexText.color = Color.black;
        gachaSetCutInSlots[gachaSetSelectedSlot].IndexText.color = Color.black;
    }

    private ScrollRect CloneGachaSetList(ScrollRect template, RectTransform parent, float left, float width)
    {
        var list = Instantiate(template, parent);
        list.name = template.name + " Gacha Set";
        PlaceGachaSetItem((RectTransform)list.transform, left, 50, width, parent.rect.height - 98);
        ClearGachaSetList(list);
        list.gameObject.SetActive(true);
        return list;
    }

    private static void ClearGachaSetList(ScrollRect list)
    {
        foreach (Transform child in list.content)
        {
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }
    }

    private void ListGachaSetCostumes(CharaEntry character)
    {
        ClearGachaSetList(gachaSetCostumes);
        var outfits = gachaSetRandom.Outfits(character.Id);
        var characterCards = gachaSetCards.Where(row => Convert.ToInt32(row["chara_id"]) == character.Id);
        // CutIns are selected by the animation card, independent of the actor's wardrobe.
        var choices = gachaSetPickingCutIn
            ? characterCards.Where(row => Convert.ToInt32(row["default_rarity"]) == 3 &&
                GachaCutInLoader.HasUniqueCut(Convert.ToInt32(row["id"])))
                .Select(row => (Dress: Convert.ToInt32(row["race_dress_id"]), Card: Convert.ToInt32(row["id"]), Actor: (LiveCharacterLoadData)null))
            : outfits.Select(actor => (Dress: actor.MasterDressId, Card: 0, Actor: actor));
        foreach (var choice in choices.OrderBy(c => c.Dress))
        {
            var costume = choice.Dress == 0 ? null : Main.Costumes.FirstOrDefault(c => c.MasterDressId == choice.Dress);
            var item = Instantiate(UmaContainerCostumePrefab, gachaSetCostumes.content).GetComponent<UmaUIContainer>();
            item.UseLocalizedText();
            UIPressScaleFeedback.AddTo(item.gameObject);
            item.Name = choice.Dress == 0 ? UiLocalization.T("Default") : costume?.DressName ??
                UmaDatabaseController.Instance.DressData.FirstOrDefault(row => Convert.ToInt32(row["id"]) == choice.Dress)?["dressname"]?.ToString() ?? choice.Dress.ToString();
            var caption = item.TextMesh.gameObject.activeSelf ? item.TextMesh.rectTransform : item.Text.rectTransform;
            caption.anchorMin = Vector2.zero;
            caption.anchorMax = Vector2.one;
            caption.offsetMin = new Vector2(6, 2);
            caption.offsetMax = new Vector2(-item.Image.rectTransform.rect.width - 4, -2);
            item.TextMesh.enableWordWrapping = true;
            item.TextMesh.enableAutoSizing = true;
            item.TextMesh.fontSizeMin = 14;
            item.TextMesh.fontSizeMax = 20;
            item.Text.resizeTextForBestFit = true;
            item.Text.resizeTextMinSize = 14;
            item.Text.resizeTextMaxSize = 20;
            item.Image.sprite = costume?.Icon ?? CostumeIconDefault;
            item.Image.enabled = true;
            item.Button.onClick.AddListener(() =>
            {
                if (gachaSetPickingCutIn)
                {
                    gachaSetSlots[gachaSetSelectedSlot] ??= new GachaCutInLoader.Selection();
                    gachaSetSlots[gachaSetSelectedSlot].CutInCard = choice.Card;
                }
                else
                    SetGachaSetCharacter(choice.Actor);
                RefreshGachaSetSlots();
                CloseGachaSetPicker();
            });
        }
        gachaSetCostumes.verticalNormalizedPosition = 1f;
    }

    private void SetGachaSetCharacter(LiveCharacterLoadData actor)
    {
        if (gachaSetSelectedSlot == OpeningSlot)
        {
            gachaSetOpeningActor = actor;
            RefreshGachaSetSlots();
            return;
        }
        var cards = gachaSetCards.Where(row => Convert.ToInt32(row["chara_id"]) == actor.CharaEntry.Id);
        var selected = cards.FirstOrDefault(row => Convert.ToInt32(row["race_dress_id"]) == actor.MasterDressId) ?? cards.FirstOrDefault();
        var previous = gachaSetSlots[gachaSetSelectedSlot];
        var slot = new GachaCutInLoader.Selection
        {
            Character = actor.CharaEntry, Card = selected == null ? 0 : Convert.ToInt32(selected["id"]),
            DressId = actor.MasterDressId, Costume = actor.CostumeId, Head = actor.HeadCostumeId,
            Rarity = selected == null ? 1 : Convert.ToInt32(selected["default_rarity"])
        };
        slot.InitialGateFinish = slot.Rarity - 1;
        MatchGachaSetCutIn(slot);
        if (gachaSetOverdrive.isOn && previous != null)
            slot.CutInCard = previous.CutInCard;
        gachaSetSlots[gachaSetSelectedSlot] = slot;
        RefreshGachaSetSlots();
    }

    private void MatchGachaSetCutIn(GachaCutInLoader.Selection slot)
    {
        var cards = gachaSetCards.Where(row => Convert.ToInt32(row["chara_id"]) == slot.Character.Id &&
            Convert.ToInt32(row["default_rarity"]) == 3 &&
            GachaCutInLoader.HasUniqueCut(Convert.ToInt32(row["id"]))).ToArray();
        var matching = cards.FirstOrDefault(row => Convert.ToInt32(row["race_dress_id"]) == slot.DressId);
        if (matching == null && slot.Rarity == 3)
            matching = cards.FirstOrDefault(row => Convert.ToInt32(row["id"]) == slot.Character.Id * 100 + 1) ?? cards.FirstOrDefault();
        slot.CutInCard = matching == null ? 0 : Convert.ToInt32(matching["id"]);
    }

    private void PlayGachaSet()
    {
        var selection = gachaSetSlots.Where(slot => slot?.Character != null).Select(slot => new GachaCutInLoader.Selection
        {
            Character = slot.Character, Card = slot.Card, CutInCard = slot.CutInCard,
            Rarity = slot.Rarity, InitialGateFinish = slot.InitialGateFinish,
            DressId = slot.DressId, Costume = slot.Costume, Head = slot.Head
        }).ToArray();
        if (selection.Length == 0)
        {
            ShowMessage(UiLocalization.T("Select at least one character."), UIMessageType.Warning);
            return;
        }
        gachaSetPanel.SetActive(false);
        int opening = gachaSetOpening.value == 2 ? 0 : 9001 + gachaSetOpening.value;
        OpenGachaPlayback(selection.Select(slot => slot.Card).ToArray(), opening, selection, opening == 0 ? null : gachaSetOpeningActor);
    }
}
