using UnityEngine;
using UnityEngine.UI;

public partial class UmaViewerUI
{
    private Toggle _liveSubtitleToggle;
    private void EnsureLiveSubtitleToggle()
    {
        if(_liveSubtitleToggle==null)
        {
            var template=LiveSelectPannel.transform.Find("StageToggle").GetComponent<Toggle>();
            _liveSubtitleToggle=Instantiate(template,template.transform.parent);
            _liveSubtitleToggle.name="SubtitlesToggle";
            _liveSubtitleToggle.onValueChanged=new Toggle.ToggleEvent();
            _liveSubtitleToggle.group=null;
            var rect=(RectTransform)_liveSubtitleToggle.transform;
            rect.anchoredPosition=new Vector2(160,125);rect.sizeDelta=new Vector2(300,35);
            rect.Find("Label").GetComponent<Text>().text="Subtitles";
            ((RectTransform)rect.Find("Background")).anchoredPosition=new Vector2(150,0);
            _liveSubtitleToggle.onValueChanged.AddListener(value=>LiveSubtitleSettings.SetEnabled(value));
        }
        _liveSubtitleToggle.SetIsOnWithoutNotify(LiveSubtitleSettings.Enabled);
    }
}
