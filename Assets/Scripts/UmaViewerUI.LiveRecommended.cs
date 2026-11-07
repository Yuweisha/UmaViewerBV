using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public partial class UmaViewerUI
{
    private Toggle _recommendedLiveToggle;
    private List<LiveCharacterLoadData> _manualLiveSelections;
    public bool UseRecommendedLive => _recommendedLiveToggle != null && _recommendedLiveToggle.isOn && _recommendedLiveToggle.interactable;

    private void EnsureRecommendedLiveToggle()
    {
        if (_recommendedLiveToggle != null) return;
        var template = LiveSelectPannel.transform.Find("StageToggle").GetComponent<Toggle>();
        _recommendedLiveToggle = Instantiate(template, template.transform.parent);
        _recommendedLiveToggle.name = "RecommendedToggle";
        _recommendedLiveToggle.onValueChanged = new Toggle.ToggleEvent();
        _recommendedLiveToggle.group = null;
        var rect = (RectTransform)_recommendedLiveToggle.transform;
        rect.anchoredPosition = new Vector2(160, 85);
        rect.sizeDelta = new Vector2(300, 35);
        var label = rect.Find("Label").GetComponent<Text>();
        label.text = "Recommended cast & outfits";
        label.fontSize = 18;
        var background = (RectTransform)rect.Find("Background");
        background.anchoredPosition = new Vector2(150, 0);
        _recommendedLiveToggle.SetIsOnWithoutNotify(true);
        _recommendedLiveToggle.onValueChanged.AddListener(_ =>
        {
            CurrentSeletChara = null;
            if (currentLive != null) ShowLiveSelectPanel(currentLive, false);
        });
    }

    private void RestoreManualLiveSelections(List<LiveCharacterSelect> selections)
    {
        if (UseRecommendedLive || _manualLiveSelections == null) return;
        for (int i = 0; i < selections.Count && i < _manualLiveSelections.Count; i++)
            SetRecommendedSlot(selections[i], _manualLiveSelections[i]);
        _manualLiveSelections = null;
    }

    private void ApplyRecommendedLiveSelections(LiveEntry entry, List<LiveCharacterSelect> selections)
    {
        foreach (var slot in selections) slot.GetComponent<Button>().interactable = !UseRecommendedLive;
        if (!UseRecommendedLive) return;
        if (_manualLiveSelections == null)
            _manualLiveSelections = selections.Select(s => new LiveCharacterLoadData
            {
                CharaEntry = s.CharaEntry, CostumeId = s.CostumeId,
                HeadCostumeId = s.HeadCostumeId, MasterDressId = s.MasterDressId
            }).ToList();
        var recommended = LiveFormationLoader.PrepareBaseCharacters(entry,
            LiveCharacterLoadData.CaptureAll(selections));
        LiveFormationLoader.ApplyRecommendedCostumes(recommended, entry.MusicId,
            LiveFormationLoader.BaseCharacterCount(entry));
        int[] selectableSlots = LiveFormationLoader.SelectableCharacterSlots(entry);
        for (int i = 0; i < selections.Count && i < selectableSlots.Length; i++)
            SetRecommendedSlot(selections[i], recommended[selectableSlots[i]]);
    }

    private void SetRecommendedSlot(LiveCharacterSelect slot, LiveCharacterLoadData value)
    {
        if (value == null || value.CharaEntry == null) return;
        var character = (value.CharaEntry.IsMob ? Main.MobCharacters : Main.Characters)
            .FirstOrDefault(c => c.Id == value.CharaEntry.Id) ?? value.CharaEntry;
        var costume = Main.Costumes.FirstOrDefault(c => c.MasterDressId == value.MasterDressId);
        slot.SetValue(character, value.CostumeId, costume?.Icon ?? CostumeIconDefault,
            value.HeadCostumeId, value.MasterDressId);
        if (character.Icon == null) slot.CharaImage.sprite = CharaIconDefault;
    }
}
