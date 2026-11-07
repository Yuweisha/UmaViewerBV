using System;
using System.Linq;
using SFB;
using UnityEngine;
using UnityEngine.UI;

public partial class UmaViewerUI
{
    internal void SelectBatchRenderLive(LiveEntry live) => ShowLiveSelectPanel(live,false);
    private Button _liveRenderButton;
    private GameObject _liveRenderDialog;
    private Dropdown _renderResolution,_renderFrameRate,_renderBitrate,_renderEncoder;

    private void EnsureLiveRenderEntry()
    {
        foreach(string name in new[]{"StageToggle","VMDToggle","ControlModeToggle","LiveMode"})
        {
            var control=LiveSelectPannel.transform.Find(name);
            if(control!=null)control.gameObject.SetActive(false);
        }
        if(_liveRenderButton!=null)return;
        LiveRenderUI.Font=LiveSelectInfoText.font;
        var play=LiveSelectPannel.transform.Find("PlayButtton") as RectTransform;
        play.anchoredPosition=new Vector2(-180,40);play.sizeDelta=new Vector2(220,50);
        _liveRenderButton=LiveRenderUI.Button("Render",LiveSelectPannel.transform,Vector2.zero,RenderLive);
        var rect=(RectTransform)_liveRenderButton.transform;
        rect.anchorMin=rect.anchorMax=new Vector2(.5f,0);
        rect.anchoredPosition=new Vector2(60,40);rect.sizeDelta=new Vector2(220,50);
        _liveRenderButton.name="RenderButton";
    }

    public void RenderLive()
    {
        if(currentLive==null||LiveVideoExporter.IsActive)return;
        if(!LiveRenderSettings.FfmpegDetected)
        { ShowMessage(LiveRenderSettings.FfmpegMissingMessage,UIMessageType.Error);return; }
        if(_liveRenderDialog==null)
        {
            _liveRenderDialog=new GameObject("Live render settings",typeof(RectTransform),typeof(Image));
            _liveRenderDialog.transform.SetParent(LiveSelectPannel.transform,false);
            _liveRenderDialog.layer=LiveSelectPannel.layer;
            var shade=_liveRenderDialog.GetComponent<RectTransform>();
            shade.anchorMin=Vector2.zero;shade.anchorMax=Vector2.one;shade.offsetMin=shade.offsetMax=Vector2.zero;
            _liveRenderDialog.GetComponent<Image>().color=new Color(0,0,0,.7f);
            var panel=LiveRenderUI.Panel("Render settings",shade,new Vector2(740,450));
            LiveRenderUI.Text("Render Live",panel,new Vector2(0,180),new Vector2(680,40),26);
            var template=LiveSelectPannel.transform.root.GetComponentsInChildren<Dropdown>(true).First();
            _renderResolution=RenderChoice(template,panel,"Resolution",100,LiveRenderSettings.ResolutionLabels,1);
            _renderFrameRate=RenderChoice(template,panel,"Frame rate",40,LiveRenderSettings.FrameRates.Select(f=>f+" fps").ToArray(),2);
            _renderBitrate=RenderChoice(template,panel,"Video bitrate",-20,LiveRenderSettings.Bitrates.Select(b=>b+" Mbps").ToArray(),2);
            _renderEncoder=RenderChoice(template,panel,"Encoder",-80,LiveRenderSettings.EncoderLabels,0);
            LiveRenderUI.Text("Audio: AAC / 320 kbps / stereo",panel,new Vector2(0,-128),new Vector2(670,30),17);
            LiveRenderUI.Button("Cancel",panel,new Vector2(-145,-183),()=>_liveRenderDialog.SetActive(false));
            LiveRenderUI.Button("Render MP4",panel,new Vector2(145,-183),ChooseLiveRenderDirectory);
        }
        _liveRenderDialog.SetActive(true);
        _liveRenderDialog.transform.SetAsLastSibling();
    }

    private Dropdown RenderChoice(Dropdown template,RectTransform panel,string label,float y,string[] choices,int initial)
    {
        LiveRenderUI.Text(label,panel,new Vector2(-235,y),new Vector2(210,38),20).alignment=TextAnchor.MiddleLeft;
        var dropdown=Instantiate(template,panel);
        dropdown.name=label;
        dropdown.onValueChanged=new Dropdown.DropdownEvent();
        var rect=(RectTransform)dropdown.transform;
        rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);
        rect.localScale=Vector3.one;rect.anchoredPosition=new Vector2(105,y);rect.sizeDelta=new Vector2(420,38);
        dropdown.ClearOptions();dropdown.AddOptions(choices.ToList());dropdown.SetValueWithoutNotify(initial);dropdown.RefreshShownValue();
        dropdown.captionText.fontSize=22;dropdown.itemText.fontSize=22;
        dropdown.gameObject.SetActive(true);
        return dropdown;
    }

    private void ChooseLiveRenderDirectory()
    {
        var directories=StandaloneFileBrowser.OpenFolderPanel("Render output folder",Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),false);
        if(directories.Length==0||string.IsNullOrWhiteSpace(directories[0]))return;
        BeginLiveRender(ReadLiveRenderSettings(),directories[0]);
    }

    private LiveRenderSettings ReadLiveRenderSettings()
    {
        var resolution=LiveRenderSettings.Resolutions[_renderResolution.value];
        return new LiveRenderSettings{Width=resolution.x,Height=resolution.y,Fps=LiveRenderSettings.FrameRates[_renderFrameRate.value],
            BitrateMbps=LiveRenderSettings.Bitrates[_renderBitrate.value],Encoder=LiveRenderSettings.Encoders[_renderEncoder.value]};
    }

    public void BeginLiveRender(LiveRenderSettings settings,string directory)
    {
        if(currentLive==null)return;
        if(_liveRenderDialog!=null)_liveRenderDialog.SetActive(false);
        try { LiveVideoExporter.Begin(this,currentLive,settings,directory); }
        catch(Exception error) { ShowMessage(error.Message,UIMessageType.Error); }
    }
}

internal static class LiveRenderUI
{
    internal static Font Font;
    internal static RectTransform Panel(string name,Transform parent,Vector2 size)
    {
        var obj=new GameObject(name,typeof(RectTransform),typeof(Image));obj.transform.SetParent(parent,false);obj.layer=parent.gameObject.layer;
        var rect=(RectTransform)obj.transform;rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.sizeDelta=size;
        obj.GetComponent<Image>().color=new Color(.13f,.15f,.19f,.98f);
        return rect;
    }
    internal static Text Text(string name,Transform parent,Vector2 position,Vector2 size,int fontSize)
    {
        var obj=new GameObject(name,typeof(RectTransform),typeof(Text));obj.transform.SetParent(parent,false);obj.layer=parent.gameObject.layer;
        var text=obj.GetComponent<Text>();text.text=name;text.font=Font!=null?Font:Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize=fontSize;text.color=Color.white;text.alignment=TextAnchor.MiddleCenter;text.raycastTarget=false;
        var rect=text.rectTransform;rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.anchoredPosition=position;rect.sizeDelta=size;
        return text;
    }
    internal static Button Button(string label,Transform parent,Vector2 position,UnityEngine.Events.UnityAction action)
    {
        var rect=Panel(label,parent,new Vector2(240,42));rect.anchoredPosition=position;
        rect.GetComponent<Image>().color=new Color(.23f,.32f,.43f,1f);
        var button=rect.gameObject.AddComponent<Button>();button.targetGraphic=rect.GetComponent<Image>();button.onClick.AddListener(action);
        Text(label,rect,Vector2.zero,new Vector2(230,38),21);
        return button;
    }
}
