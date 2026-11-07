using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public partial class UmaViewerUI
{
    private readonly Dictionary<int,LiveCharacterSelect> _fixedLiveSelections = new Dictionary<int,LiveCharacterSelect>();
    private RectTransform _fixedLiveSection;
    private Vector2 _liveListSize, _liveListPosition;

    internal Dictionary<int,LiveCharacterLoadData> CaptureFixedLiveOverrides(LiveEntry live)
    {
        if (currentLive?.MusicId != live.MusicId) return null;
        return _fixedLiveSelections.ToDictionary(pair=>pair.Key,pair=>LiveCharacterLoadData.Capture(pair.Value));
    }

    private void ShowFixedLiveSelections(LiveEntry live,List<LiveCharacterSelect> selections,bool keepSelections)
    {
        var listRect=(RectTransform)LiveSelectList.transform;
        if (_fixedLiveSection==null)
        {
            _liveListSize=listRect.sizeDelta;_liveListPosition=listRect.anchoredPosition;
            var section=new GameObject("FixedLiveCast",typeof(RectTransform));
            section.transform.SetParent(LiveSelectPannel.transform,false);section.layer=LiveSelectPannel.layer;
            _fixedLiveSection=section.GetComponent<RectTransform>();
            _fixedLiveSection.anchorMin=listRect.anchorMin;_fixedLiveSection.anchorMax=listRect.anchorMax;
            _fixedLiveSection.pivot=new Vector2(.5f,.5f);
            _fixedLiveSection.sizeDelta=new Vector2(_liveListSize.x,100);
            _fixedLiveSection.anchoredPosition=_liveListPosition+new Vector2(0,-_liveListSize.y*.5f+50);
            var label=LiveRenderUI.Text("Fixed cast (Override)",section.transform,new Vector2(0,36),new Vector2(_liveListSize.x,24),18);
            label.alignment=TextAnchor.MiddleLeft;label.color=new Color(.2f,.2f,.2f,1f);
            var row=new GameObject("Slots",typeof(RectTransform),typeof(HorizontalLayoutGroup));
            row.transform.SetParent(section.transform,false);row.layer=section.layer;
            var rect=row.GetComponent<RectTransform>();rect.sizeDelta=new Vector2(_liveListSize.x,72);rect.anchoredPosition=new Vector2(0,-10);
            var layout=row.GetComponent<HorizontalLayoutGroup>();layout.childAlignment=TextAnchor.MiddleLeft;
            layout.childControlWidth=layout.childControlHeight=false;layout.childForceExpandWidth=layout.childForceExpandHeight=false;layout.spacing=6;
        }
        if (!keepSelections)
        {
            foreach (var slot in _fixedLiveSelections.Values)
            {slot.transform.SetParent(null);Destroy(slot.gameObject);}
            _fixedLiveSelections.Clear();
        }
        var fixedRows=UmaDatabaseController.Instance.ReadLiveFixMembers(live.MusicId);
        _fixedLiveSection.gameObject.SetActive(fixedRows.Count>0);
        listRect.sizeDelta=_liveListSize-new Vector2(0,fixedRows.Count>0?110:0);
        listRect.anchoredPosition=_liveListPosition+new Vector2(0,fixedRows.Count>0?55:0);
        if (fixedRows.Count==0) return;
        foreach (var slot in _fixedLiveSelections.Values)
            slot.GetComponentInChildren<Text>().color=Color.black;
        var nativeCast=LiveFormationLoader.PrepareBaseCharacters(live,LiveCharacterLoadData.CaptureAll(selections));
        var cell=LiveSelectList.content.GetComponent<GridLayoutGroup>();
        foreach (var row in fixedRows)
        {
            int index=System.Convert.ToInt32(row["order"])-1;
            if (_fixedLiveSelections.ContainsKey(index))continue;
            var slot=Instantiate(LiveSelectPrefab,_fixedLiveSection.Find("Slots")).GetComponent<LiveCharacterSelect>();
            if(cell!=null)((RectTransform)slot.transform).sizeDelta=cell.cellSize;
            slot.IndexText.text=(index+1).ToString();
            SetRecommendedSlot(slot,nativeCast[index]);
            var button=slot.GetComponent<Button>();button.interactable=true;
            button.onClick.AddListener(()=> {
                slot.SelectChara(this);
                foreach(var item in LiveSelectList.content.GetComponentsInChildren<LiveCharacterSelect>().Concat(_fixedLiveSelections.Values))
                    item.GetComponentInChildren<Text>().color=item==slot?Color.green:Color.black;
            });
            _fixedLiveSelections.Add(index,slot);
        }
    }
}
