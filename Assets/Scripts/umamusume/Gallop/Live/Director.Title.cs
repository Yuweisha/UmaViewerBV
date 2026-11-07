using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace Gallop.Live
{
    public partial class Director
    {
        LiveTitleController _titleController;
        Camera _titleCamera;
        Camera _titleCameraOwner;
        GameObject _titleCanvas;
        Text _subtitleLyrics;
        RenderTexture _subtitleSceneBackdrop;
        public RenderTexture SubtitleSceneBackdrop=>_subtitleSceneBackdrop;
        public LiveTitleController TitleController => _titleController;
        public Camera TitleRenderingCamera => _titleCamera;

        void InitializeLiveTitle()
        {
            ReleaseLiveTitle();
            var prefab = Resources.Load<GameObject>("UI/Parts/Live/LiveTitleController");
            if (prefab == null) throw new InvalidOperationException("Original converted LiveTitleController prefab missing");
            // The host uses a final UI overlay to transport the original title
            // geometry. Original UIManager/orientation ownership remains separate.
            _titleCanvas = new GameObject("LiveTitleCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            _titleCanvas.layer = LayerMask.NameToLayer("UI");
            _titleCanvas.transform.SetParent(transform, false);
            var cameraObject = new GameObject("LiveTitleCamera", typeof(Camera), typeof(UniversalAdditionalCameraData));
            // ScreenSpaceCamera places its Canvas relative to the camera.
            // Parenting that camera below the Canvas creates a transform cycle.
            cameraObject.transform.SetParent(transform, false);
            _titleCamera = cameraObject.GetComponent<Camera>();
            _titleCamera.cullingMask = 1 << LayerMask.NameToLayer("UI");
            _titleCamera.orthographic = true;
            _titleCamera.orthographicSize = 540f;
            _titleCamera.nearClipPlane = 0.1f;
            _titleCamera.farClipPlane = 100f;
            _titleCamera.allowHDR = false;
            _titleCamera.allowMSAA = false;
            _titleCamera.clearFlags = CameraClearFlags.Depth;
            var gallopData = cameraObject.AddComponent<Gallop.RenderPipeline.CameraData>();
            gallopData.IsUIRendering = true;
            gallopData.RequestAntiAliasing = Gallop.RenderPipeline.CameraData.AntiAliasLevel.None;
            var cameraData = cameraObject.GetComponent<UniversalAdditionalCameraData>();
            // Viewer subtitles use the same screen UI during playback and a
            // dedicated UI-only camera during export. They do not inherit the
            // game camera's post-processing/culling surface.
            cameraData.renderType = CameraRenderType.Base;
            cameraData.SetRenderer(0);
            cameraData.renderPostProcessing = false;
            cameraData.renderShadows = false;
            var canvas = _titleCanvas.GetComponent<Canvas>();
            _titleCamera.enabled=false;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder=10;
            canvas.planeDistance = 10f;
            var scaler = _titleCanvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            // Original resources.assets LiveView: LandscapeTitle GO107083,
            // RectTransform1215318, bottom-center anchoredPosition.y=82.
            var anchor = new GameObject("LandscapeTitle", typeof(RectTransform)).GetComponent<RectTransform>();
            anchor.gameObject.layer = _titleCanvas.layer;
            anchor.SetParent(_titleCanvas.transform, false);
            anchor.anchorMin = anchor.anchorMax = new Vector2(0.5f, 0f);
            anchor.sizeDelta = Vector2.zero;
            anchor.anchoredPosition = new Vector2(0f, 82f);
            _titleController = Instantiate(prefab, anchor, false).GetComponent<LiveTitleController>();
            // Original ImageCommon serialized Atlas=Live, sprite=..._sl.
            // Membership/PPtr is recorded in source-atlas-inventory.json. Loading
            // the texture bundle preserves the original Sprite/UV/mesh/texture.
            var main = UmaViewerMain.Instance;
            if (!main.AbList.TryGetValue("atlas/live/live_tex", out var atlasEntry))
                throw new InvalidOperationException("Original Live title atlas texture entry missing");
            var atlasBundle = UmaAssetManager.LoadAssetBundle(atlasEntry, neverUnload: true, isRecursive: true);
            _titleController._titleImage.sprite = atlasBundle.LoadAllAssets<Sprite>()
                .Single(x => x.name == "utx_bg_livetitle_00_sl");
            var database = UmaDatabaseController.Instance;
            var row = database.LiveData.First(x => Convert.ToInt32(x["music_id"]) == live.MusicId);
            ColorUtility.TryParseHtmlString(Convert.ToString(row["title_color_top"]), out var top);
            ColorUtility.TryParseHtmlString(Convert.ToString(row["title_color_bottom"]), out var bottom);
            Canvas.ForceUpdateCanvases();
            _titleController.Initialize(database.ReadLiveTitle(live.MusicId), database.ReadLiveAuthor(live.MusicId), top, bottom);
            LiveSubtitleSettings.Changed+=ApplyLiveSubtitleVisibility;
            ApplyLiveSubtitleVisibility(LiveSubtitleSettings.Enabled);
            _liveTimelineControl.TitleController = _titleController;
            UpdateLiveTitleCamera();
        }

        void UpdateLiveTitleCamera()
        {
            if (_titleCamera == null || MainRenderCamera == null) return;
            _titleCamera.aspect = _titleCamera.targetTexture!=null
                ? (float)_titleCamera.targetTexture.width/_titleCamera.targetTexture.height:MainRenderCamera.aspect;
        }

        public void BeginSubtitleCapture(RenderTexture target)
        {
            var descriptor=target.descriptor;descriptor.depthBufferBits=0;descriptor.msaaSamples=1;
            _subtitleSceneBackdrop=new RenderTexture(descriptor){name="Subtitle scene backdrop"};_subtitleSceneBackdrop.Create();
            _titleCamera.targetTexture=target;
            _titleCamera.aspect=(float)target.width/target.height;
            var canvas=_titleCanvas.GetComponent<Canvas>();
            canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=_titleCamera;
            Canvas.ForceUpdateCanvases();
        }
        public void RenderSubtitlesTo(RenderTexture target)
        {
            if(!LiveSubtitleSettings.Enabled||_titleCanvas==null)return;
            if(_titleController.IsDisappear&&string.IsNullOrEmpty(CurrentSubtitleText))return;
            Graphics.Blit(target,_subtitleSceneBackdrop);
            _titleCamera.enabled=true;
            try
            {
                Canvas.ForceUpdateCanvases();
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(_titleCamera,new UnityEngine.Rendering.RenderPipeline.StandardRequest{destination=target});
            }
            finally {_titleCamera.enabled=false;}
        }
        public void EndSubtitleCapture()
        {
            if(_titleCanvas==null)return;
            _titleCamera.targetTexture=null;_titleCamera.enabled=false;
            var canvas=_titleCanvas.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.worldCamera=null;
            if(_subtitleSceneBackdrop!=null){_subtitleSceneBackdrop.Release();Destroy(_subtitleSceneBackdrop);_subtitleSceneBackdrop=null;}
        }

        void RemoveTitleCameraFromOwner()
        {
            if (_titleCameraOwner != null && _titleCameraOwner.TryGetComponent<UniversalAdditionalCameraData>(out var data))
                data.cameraStack.Remove(_titleCamera);
            _titleCameraOwner = null;
        }
        void ReleaseLiveTitle()
        {
            if(_subtitleSceneBackdrop!=null){_subtitleSceneBackdrop.Release();Destroy(_subtitleSceneBackdrop);_subtitleSceneBackdrop=null;}
            LiveSubtitleSettings.Changed-=ApplyLiveSubtitleVisibility;
            if (_liveTimelineControl != null && ReferenceEquals(_liveTimelineControl.TitleController, _titleController))
                _liveTimelineControl.TitleController = null;
            RemoveTitleCameraFromOwner();
            if (_titleCamera != null) Destroy(_titleCamera.gameObject);
            if (_titleCanvas != null) Destroy(_titleCanvas);
            _titleCanvas = null; _titleCamera = null; _titleController = null; _subtitleLyrics=null;
        }
        public void SetLiveSubtitleText(string text,Text template)
        {
            if(_titleCanvas==null)return;
            bool created=_subtitleLyrics==null;
            if(_subtitleLyrics==null)
            {
                _subtitleLyrics=Instantiate(template,_titleCanvas.transform,false);
                _subtitleLyrics.name="LiveSubtitleLyrics";
                _subtitleLyrics.gameObject.SetActive(true);
                _subtitleLyrics.enabled=true;
                _subtitleLyrics.fontSize=30;_subtitleLyrics.lineSpacing=1f;
                _subtitleLyrics.horizontalOverflow=HorizontalWrapMode.Wrap;
                _subtitleLyrics.verticalOverflow=VerticalWrapMode.Overflow;
                var rect=_subtitleLyrics.rectTransform;
                rect.anchorMin=new Vector2(0,0);rect.anchorMax=new Vector2(1,0);rect.pivot=new Vector2(.5f,0);
                rect.anchoredPosition=new Vector2(0,32);rect.sizeDelta=new Vector2(-240,96);
                template.enabled=false;
            }
            if(created||_subtitleLyrics.text!=text)
            {
                _subtitleLyrics.text=text;
                // Lyrics change after timeline evaluation, before the final
                // camera. Flush the UI mesh at that boundary, including export.
                Canvas.ForceUpdateCanvases();
            }
        }
        public bool LiveSubtitleCanvasEnabled => _titleCanvas!=null&&_titleCanvas.GetComponent<Canvas>().enabled;
        public string CurrentSubtitleText => _subtitleLyrics!=null?_subtitleLyrics.text:string.Empty;
        private void ApplyLiveSubtitleVisibility(bool enabled)
        {
            if(_titleCanvas==null)return;
            _titleCanvas.GetComponent<Canvas>().enabled=enabled;
            if(enabled)Canvas.ForceUpdateCanvases();
        }
    }
}
