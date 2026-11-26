using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using static GachaCutInLoader;
using Object = UnityEngine.Object;

internal sealed class GachaSupportStage
{
    private const int AlbumKey = 100;
    private const int FirstPageKey = 101;
    private const int PreviousPhotoKey = 111;
    private const int CurrentPhotoKey = 112;
    private static readonly int MainTexture = Shader.PropertyToID("_MainTex");
    private static readonly int PhotoLayer = LayerMask.NameToLayer("3D_UI");
    // SupportSetupCamera from GachaMainView.AlbumAnimationData.
    private static readonly AnimationCurve SetupCamera = new AnimationCurve(
        new Keyframe(0f, 0f, 0.88132f, 0f),
        new Keyframe(0.10929f, 0f, 0f, 24.85541f),
        new Keyframe(0.12f, 0.26607f, 25.75659f, 25.75659f),
        new Keyframe(0.13219f, 0.51663f, 14.81322f, 14.81322f),
        new Keyframe(0.17892f, 0.94068f, 1.9257f, 1.9257f),
        new Keyframe(0.20137f, 0.96991f, 0.88453f, 0.88453f),
        new Keyframe(0.21892f, 0.9813f, 0.25677f, 0.25677f),
        new Keyframe(0.3f, 0.98984f, 0.03225f, 0.00923f),
        new Keyframe(1.4f, 1f, 0.00923f, -5f),
        new Keyframe(1.6f, 0f, 0f, 0f));

    private readonly Transform root;
    private readonly Transform albumRoot;
    private readonly Transform animationLocator;
    private readonly IReadOnlyDictionary<string, GameObject> prefabs;
    private readonly string prefabPrefix;
    private readonly Dictionary<int, GameObject> backgrounds = new Dictionary<int, GameObject>();
    private readonly Dictionary<int, CardEffect> effects = new Dictionary<int, CardEffect>();
    private readonly Transform[] anchors = new Transform[10];
    private readonly AlbumPage[] pages = new AlbumPage[10];
    private readonly Vector3[] setupCameraOffsets = new Vector3[10];
    private readonly Photo previous;
    private readonly Photo current;
    private readonly Vector3 cameraPhotoPosition;
    private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
    private int pageIndex = -1;
    private int turnFrame;
    private int turnDuration;
    private PageMotion pageMotion;
    private int photoFrame;
    private int photoDuration;
    private PhotoMotion photoMotion;
    private bool frameVisible;
    private int upgradeRarity;

    public IReadOnlyList<Result> Results { get; }
    public Result Current => pageIndex >= 0 ? Results[pageIndex] : null;
    public Camera PhotoCamera { get; }

    public GachaSupportStage(Transform root, IReadOnlyDictionary<string, GameObject> prefabs, Result[] results,
        Camera camera, Options options, string prefabPrefix)
    {
        this.root = root;
        this.prefabs = prefabs;
        this.prefabPrefix = prefabPrefix;
        Results = results;
        var album = Instantiate("album_r", root);
        backgrounds.Add(AlbumKey, album);
        albumRoot = Node(album.transform, "Root");
        animationLocator = Node(albumRoot, "animation_locator");
        for (int i = 0; i < pages.Length; i++)
        {
            anchors[i] = Node(albumRoot, $"page{i + 1:D2}");
            setupCameraOffsets[i] = Node(album.transform, $"cam_target{i + 1:D2}").localPosition;
            var page = new AlbumPage(albumRoot, i);
            pages[i] = page;
            backgrounds.Add(FirstPageKey + i, page.Root.gameObject);
            if (i >= results.Length)
            {
                Instantiate("page_blank", page.Root).SetActive(true);
                continue;
            }
            int rarity = results[i].Rarity;
            var upgrade = options.UpgradeAt(i);
            AddPageAppearance(page, i, rarity);
            if (upgrade != Upgrade.None)
                AddPageAppearance(page, i, 1);
            if (upgrade == Upgrade.StepUp)
                AddPageAppearance(page, i, 2);
            page.ChangeRarity(upgrade == Upgrade.None ? rarity : 1);
        }
        var ambience = Object.Instantiate(prefabs[EffectName(options.Sky == SkyType.Normal ? 13 : 18)], root, false);
        SetLayer(ambience);
        cameraPhotoPosition = anchors[0].localPosition;
        previous = CreatePhoto(PreviousPhotoKey);
        current = CreatePhoto(CurrentPhotoKey);
        PhotoCamera = new GameObject("Gacha support photo camera").AddComponent<Camera>();
        PhotoCamera.transform.SetParent(camera.transform, false);
        PhotoCamera.cullingMask = 1 << PhotoLayer;
        PhotoCamera.clearFlags = CameraClearFlags.Depth;
        var settings = PhotoCamera.GetUniversalAdditionalCameraData();
        settings.renderType = CameraRenderType.Overlay;
        settings.renderPostProcessing = false;
        settings.renderShadows = false;
        camera.GetUniversalAdditionalCameraData().cameraStack.Add(PhotoCamera);
    }

    public GameObject FindBackground(int key) => backgrounds.TryGetValue(key, out var instance) ? instance : null;

    public void BeginCut(int index, bool closing, int nextUpgradeRarity)
    {
        foreach (var instance in backgrounds.Values)
            instance.SetActive(false);
        foreach (var effect in effects.Values)
            effect.Instance.SetActive(false);
        pageIndex = closing ? Results.Count - 1 : index;
        upgradeRarity = nextUpgradeRarity;
        pageMotion = PageMotion.Default;
        photoMotion = PhotoMotion.Page;
        frameVisible = true;
        SetPhotoLayer(PreviewLayer);
        PhotoCamera.enabled = false;
        if (pageIndex >= 0)
        {
            SetArt(current, Current.Art);
            SetArt(previous, Results[closing ? pageIndex : Mathf.Max(0, pageIndex - 1)].Art);
        }
    }

    public void CommitUpgrade()
    {
        if (upgradeRarity > 0)
            pages[pageIndex].ChangeRarity(upgradeRarity);
    }

    public Vector3 CameraOffset(float seconds) => upgradeRarity > 0
        ? setupCameraOffsets[pageIndex] * Mathf.Clamp01(SetupCamera.Evaluate(seconds))
        : Vector3.zero;

    public void Apply(string command, int value, int frame)
    {
        switch (command)
        {
            case "AlbumStartSlide":
                StartPageMotion(PageMotion.Slide, value, frame);
                break;
            case "AlbumStartPageOpen":
                StartPageMotion(PageMotion.Open, value, frame);
                break;
            case "AlbumStartClose":
                StartPageMotion(PageMotion.Close, value, frame);
                break;
            case "PhotoStartViewCamera":
                StartPhotoMotion(PhotoMotion.View, value, frame);
                break;
            case "PhotoStartReturenPage":
                StartPhotoMotion(PhotoMotion.Return, value, frame);
                break;
            case "PhotoFrameOn":
                frameVisible = true;
                break;
            case "PhotoFrameOff":
                frameVisible = false;
                break;
            case "PhotoSetLayer3DUI":
                SetPhotoLayer(PhotoLayer);
                PhotoCamera.enabled = true;
                break;
            case "PhotoSetLayerBG":
                SetPhotoLayer(PreviewLayer);
                PhotoCamera.enabled = false;
                break;
            case "AlbumPageUpdateEffect":
                if (upgradeRarity > 0)
                {
                    var page = pages[pageIndex];
                    PlayEffect(upgradeRarity + 12, frame, true, page.Root);
                    var bookmark = page.Bookmark.transform;
                    var flash = PlayEffect(upgradeRarity + 14, frame, true, page.Root);
                    if (flash)
                        flash.transform.SetPositionAndRotation(bookmark.position, bookmark.rotation);
                }
                break;
            case "AlbumPageUpdateRarity":
                CommitUpgrade();
                break;
            case "GachaEffectPlay":
            case "GachaEffectPlayLoop":
                var target = IsPhotoEffect(value) || value == 1 || value == 5 ? current.Joint :
                    pageIndex >= 0 ? pages[pageIndex].Root : root;
                PlayEffect(value, frame, command == "GachaEffectPlay", target);
                break;
            case "GachaEffectClear":
                if (effects.TryGetValue(value, out var effect))
                    effect.Instance.SetActive(false);
                break;
        }
    }

    public void Sample(float frame, Camera view)
    {
        float turn = Progress(frame, turnFrame, turnDuration);
        for (int i = 0; i < pages.Length; i++)
        {
            var anchor = anchors[i];
            Vector3 position = anchor.localPosition;
            Quaternion rotation = anchor.localRotation;
            if (pageMotion == PageMotion.Slide)
                position = Vector3.Lerp(anchors[0].localPosition, position, turn * turn);
            if ((pageMotion == PageMotion.Open && i == pageIndex - 1) ||
                (pageMotion == PageMotion.Close && i < pageIndex))
            {
                position += animationLocator.localPosition;
                rotation = animationLocator.localRotation;
            }
            var page = pages[i];
            page.Root.SetLocalPositionAndRotation(position, rotation);
            if (page.Bookmark)
                page.Bookmark.SetActive(i > pageIndex || (i == pageIndex && upgradeRarity > 0));
        }

        if (pageIndex >= 0)
        {
            var page = pages[pageIndex].Root;
            float move = Progress(frame, photoFrame, photoDuration);
            float blend = Mathf.SmoothStep(0f, 1f, move);
            var position = photoMotion == PhotoMotion.View ? Vector3.Lerp(page.localPosition, cameraPhotoPosition, blend) :
                photoMotion == PhotoMotion.Return ? Vector3.Lerp(cameraPhotoPosition, page.localPosition, blend) :
                page.localPosition;
            current.Root.SetLocalPositionAndRotation(position, page.localRotation);
            current.Frame.SetActive(frameVisible);
            var last = pages[Mathf.Max(0, pageIndex - 1)].Root;
            previous.Root.SetLocalPositionAndRotation(last.localPosition, last.localRotation);
        }

        foreach (var effect in effects.Values)
            if (effect.Instance.activeInHierarchy && effect.FixedTime)
                foreach (var particle in effect.Particles)
                    particle.Simulate(Mathf.Max(0f, frame - effect.StartFrame) / 30f, false, true);

        PhotoCamera.fieldOfView = view.fieldOfView;
        PhotoCamera.aspect = view.aspect;
        PhotoCamera.nearClipPlane = view.nearClipPlane;
        PhotoCamera.farClipPlane = view.farClipPlane;
    }

    private void StartPageMotion(PageMotion motion, int duration, int frame)
    {
        pageMotion = motion;
        turnFrame = frame;
        turnDuration = duration;
    }

    private void StartPhotoMotion(PhotoMotion motion, int duration, int frame)
    {
        photoMotion = motion;
        photoFrame = frame;
        photoDuration = duration;
    }

    private static string EffectName(int id) => $"pfb_eff_gacha_cd_{id:D3}";

    private static bool IsPhotoEffect(int id) => id >= 10 && id <= 12;

    private void SetPhotoLayer(int layer)
    {
        var photo = current.Root.parent;
        SetLayer(photo.gameObject, layer);
        // The effects attached to the photo keep their own render layers.
        foreach (var effect in effects)
            if (effect.Value.Instance.transform.IsChildOf(photo))
                SetLayer(effect.Value.Instance, IsPhotoEffect(effect.Key) ? PhotoLayer : PreviewLayer);
    }

    private GameObject PlayEffect(int id, int frame, bool fixedTime, Transform target)
    {
        if (!effects.TryGetValue(id, out var effect))
        {
            if (!prefabs.TryGetValue(EffectName(id), out var prefab))
                return null;
            effect = new CardEffect(Object.Instantiate(prefab, root, false));
            SetLayer(effect.Instance, IsPhotoEffect(id) ? PhotoLayer : PreviewLayer);
            effects.Add(id, effect);
        }
        effect.Instance.transform.SetParent(target, false);
        effect.Instance.SetActive(true);
        effect.StartFrame = frame;
        effect.FixedTime = fixedTime;
        foreach (var particle in effect.Particles)
        {
            particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (!fixedTime)
                particle.Play(false);
        }
        return effect.Instance;
    }

    private void AddPageAppearance(AlbumPage page, int index, int rarity)
    {
        string suffix = SupportRarities[rarity - 1];
        var visual = Instantiate("page_" + suffix, page.Root);
        var bookmark = Instantiate("bookmark_" + suffix, Node(visual.transform, $"bookmark{index + 1:D2}"));
        page.AddAppearance(rarity, visual, bookmark);
    }

    private GameObject Instantiate(string name, Transform parent)
    {
        string key = prefabPrefix + name;
        if (!prefabs.TryGetValue(key, out var prefab))
            throw new InvalidOperationException($"Support album asset unavailable: {key}");
        var instance = Object.Instantiate(prefab, parent, false);
        SetLayer(instance);
        instance.SetActive(false);
        return instance;
    }

    private Photo CreatePhoto(int key)
    {
        var instance = Instantiate("photo", albumRoot);
        backgrounds.Add(key, instance);
        return new Photo(instance.transform);
    }

    private void SetArt(Photo photo, Texture2D texture)
    {
        photo.Art.GetPropertyBlock(properties);
        properties.SetTexture(MainTexture, texture);
        photo.Art.SetPropertyBlock(properties);
    }

    private static Transform Node(Transform parent, string name) =>
        parent.Find(name) ?? throw new InvalidOperationException($"Support album node unavailable: {name}");

    private static float Progress(float frame, int start, int duration) =>
        duration > 0 ? Mathf.Clamp01((frame - start) / duration) : 1f;

    private enum PageMotion { Default, Slide, Open, Close }
    private enum PhotoMotion { Page, View, Return }

    internal enum SkyType { Normal, NightPlain, NightStardust }
    internal enum Upgrade { None, Direct, StepUp }

    internal sealed class Options
    {
        public readonly SkyType Sky;
        private readonly Upgrade[] upgrades;

        public Options(SkyType sky, params Upgrade[] upgrades)
        {
            Sky = sky;
            this.upgrades = (Upgrade[])upgrades.Clone();
        }

        public Upgrade UpgradeAt(int index) => upgrades.Length == 0 ? Upgrade.None : upgrades[index];

        public void ValidateCount(int count)
        {
            if (upgrades.Length != 0 && upgrades.Length != count)
                throw new ArgumentException("The support upgrade list must match the draw results.");
        }
    }

    private sealed class AlbumPage
    {
        public readonly Transform Root;
        private readonly GameObject[] visuals = new GameObject[3];
        private readonly GameObject[] bookmarks = new GameObject[3];
        private int rarity;

        public GameObject Bookmark => rarity > 0 ? bookmarks[rarity - 1] : null;

        public AlbumPage(Transform root, int index)
        {
            Root = new GameObject($"Album page {index + 1:D2}").transform;
            Root.SetParent(root, false);
            Root.gameObject.SetActive(false);
        }

        public void AddAppearance(int value, GameObject visual, GameObject bookmark)
        {
            visuals[value - 1] = visual;
            bookmarks[value - 1] = bookmark;
        }

        public void ChangeRarity(int value)
        {
            if (rarity == value)
                return;
            if (rarity > 0)
                visuals[rarity - 1].SetActive(false);
            rarity = value;
            visuals[rarity - 1].SetActive(true);
        }
    }

    private sealed class Photo
    {
        public readonly Transform Root;
        public readonly Transform Joint;
        public readonly Renderer Art;
        public readonly GameObject Frame;

        public Photo(Transform instance)
        {
            Root = Node(instance, "Root");
            Joint = Node(Root, "joint00");
            var mesh = Node(instance, "photo_mesh");
            Art = Node(mesh, "photo").GetComponent<Renderer>();
            Frame = Node(mesh, "frame").gameObject;
        }
    }

    private sealed class CardEffect
    {
        public readonly GameObject Instance;
        public readonly ParticleSystem[] Particles;
        public int StartFrame;
        public bool FixedTime;

        public CardEffect(GameObject instance)
        {
            Instance = instance;
            Particles = instance.GetComponentsInChildren<ParticleSystem>(true);
        }
    }

    internal sealed class Result
    {
        public readonly int Card;
        public readonly int Rarity;
        public readonly Texture2D Art;
        public readonly Texture2D NameTexture;
        public readonly GameObject Flash;
        public readonly string Title;
        public readonly string Name;

        public Result(int card, int rarity, Texture2D art, Texture2D nameTexture, GameObject flash, string title, string name)
        {
            Card = card;
            Rarity = rarity;
            Art = art;
            NameTexture = nameTexture;
            Flash = flash;
            Title = title;
            Name = name;
        }
    }
}
