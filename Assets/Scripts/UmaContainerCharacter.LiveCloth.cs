using System;
using System.Collections.Generic;
using Gallop;
using Gallop.Live;
using UnityEngine;

public partial class UmaContainerCharacter
{
    public ClothAsset LiveClothAssets { get; private set; }
    public LiveClothBuildIdentity LiveClothIdentity { get; private set; }
    public LiveClothBuildPaths LiveClothPaths { get; private set; }

    public readonly List<string> LiveClothResourceRequests = new List<string>();
    public string LiveSkirtResourcePath { get; private set; }

    sealed class LiveClothImporterOwner : ILiveClothImportOwner
    {
        readonly UmaContainerCharacter owner;
        public LiveClothImporterOwner(UmaContainerCharacter value)
        {
            owner = value;
        }

        public ClothAsset Cloth => owner.LiveClothAssets;
        // The viewer resource cache is keyed by index path. Original loadHash
        // grouping/lifetime is a separate resource-manager boundary.
        public int LoadHash => 0;

        public GameObject Load(string path, int hash) => owner.LoadLiveClothPrefab(path);
        public void OverrideSkirtController() => owner.InitializeLiveSkirt();
    }

    public void InitializeLiveClothResources()
    {
        if (!IsLive || LiveClothAssets != null)
            return;
        var dress = UmaDatabaseController.Instance.ReadDressData(LiveMasterDressId);
        int D(string name) => Convert.ToInt32(dress[name]);
        int C(string name) => Convert.ToInt32(CharaData[name]);
        bool mob = CharaEntry.IsMob;
        LiveClothIdentity = new LiveClothBuildIdentity
        {
            CharaId = mob ? 1 : CharaEntry.Id,
            MobId = mob ? CharaEntry.Id : 0,
            ControllerType = IsMini ? 13 : 4,
            HeadSubId = LiveHeadModelSubId,
            BodySubId = D("body_type_sub"),
            Gender = C("sex"),
            Bust = C(mob ? "chara_bust_size" : "bust"),
            MobHairKindId = mob ? C("chara_hair_model") : 0,
            DressBodyType = D("body_type"),
            DressBodyTypeSub = D("body_type_sub"),
            DressUseGender = D("use_gender"),
            DressBodyShape = D("body_shape"),
            DirectoryCharacterId = mob ? 1 : CharaEntry.Id,
            CharacterTailId = mob ? -1 : C("tail_model_id"),
            DressTailId = D("tail_model_id"),
            DressTailSubId = D("tail_model_sub_id"),
            OverrideCategory = -1
        };

        LiveClothPaths = LiveClothPathBuilder.Build(LiveClothIdentity);
        LiveClothAssets = new ClothAsset();
        LiveClothAssets.InitClothData();
        LiveClothAssets.ClothCategory = CySpringDataContainer.Category.Live;
        LiveClothResourceImporter.Import(new LiveClothImporterOwner(this), LiveClothPaths);

    }

    GameObject LoadLiveClothPrefab(string path)
    {
        LiveClothResourceRequests.Add(path);
        if (string.IsNullOrEmpty(path))
            return null;
        var entry = LiveEffectController.FindResourceEntry(path);
        if (entry == null)
            return null;
        foreach (var dependency in UmaAssetManager.SearchAB(UmaViewerMain.Instance, entry))
            if (!LoadedAssets.Contains(dependency))
                LoadedAssets.Add(dependency);
        return entry.Get<GameObject>();
    }

    void InitializeLiveSkirt()
    {
        if (IsMini)
            return;
        int category = (int)LiveClothAssets.ClothCategory;
        LiveSkirtResourcePath = LiveClothPathBuilder.GetSkirtParamPath(LiveClothIdentity, category == 0 ? 0 : category == 1 ? 1 : 2);
        var entry = LiveEffectController.FindResourceEntry(LiveSkirtResourcePath);
        var asset = entry != null ? entry.Get<SkirtParamDataAsset>() : null;
        // Original missing-resource branch retains the old skirt and flags.
        if (asset == null)
            return;
        if (_skirtController != null)
            UnityEngine.Object.Destroy(_skirtController);
        _skirtController = null;
        if (asset._skirtBone != null && asset._skirtBone.Count > 0)
        {
            _skirtController = gameObject.AddComponent<SkirtController>();
            _skirtController.ModelScale = LiveTotalScale;
            _skirtController.CharaId = LiveClothIdentity.CharaId;
            var dress = UmaDatabaseController.Instance.ReadDressData(LiveMasterDressId);
            _skirtController.IsGeneralDress = Convert.ToInt32(dress["chara_id"]) == 0;
            _skirtController.Create(asset);
        }

    }

    void LoadLiveNativePhysics()
    {
        InitializeLiveClothResources();
        InitializeLiveSkirt();
        InitializeLiveSpringFromCloth();
    }

    void InitializeLiveSpringFromCloth()
    {
        var cache = BuildCySpringTransformCache(transform, out _);
        var hip = cache["Hip"];
        if (_cySpringOwner == null)
            _cySpringOwner = new UmaViewerCySpringOwner(this);
        if (_cySpringController == null)
            _cySpringController = CySpringController.AddController(gameObject, hip, _cySpringOwner);
        _cySpringController.LoadFromOwner(cache);
        _cySpringLoaded = true;
        _cySpringController.SetEnableAllCySpringBones(EnablePhysics);
        LinkSkirtControllerToCySpring();

    }

    sealed class LiveSpringReconstructionOwner : ILiveCySpringReconstructionOwner
    {
        readonly UmaContainerCharacter owner;
        public LiveSpringReconstructionOwner(UmaContainerCharacter value)
        {
            owner = value;
        }

        public int ClothCategory { get => (int)owner.LiveClothAssets.ClothCategory; set => owner.LiveClothAssets.ClothCategory = (CySpringDataContainer.Category)value; }
        public bool IsSpringLoaded => owner._cySpringLoaded;

        public void ResetSpring() => owner._cySpringController.Reset();
        public void ResetNativeCloth() => owner._cySpringController.ResetNativeCloth();
        public void DeleteSpring() => owner._cySpringController.Delete();
        public void InitializeSkirt() => owner.InitializeLiveSkirt();
        public void InitializeSpring() => owner.InitializeLiveSpringFromCloth();
        public void ReserveWarmup(float seconds, int delay) => owner._cySpringController.ReserveWarmingUpCySpring(seconds, delay);
    }

    internal void SetLiveFormationCySpringRate(float value)
    {
        if (_cySpringLoaded && _cySpringController != null)
            _cySpringController.SpringRate = value;
    }

    internal void ReserveLiveFormationCySpring(float seconds)
    {
        if (_cySpringLoaded && _cySpringController != null)
            _cySpringController.ReserveWarmingUpCySpring(seconds, 0);
    }

    public void ReconstructLiveCySpring(int category)
    {
        InitializeLiveClothResources();
        new LiveCySpringReconstructionRuntime(new LiveSpringReconstructionOwner(this)).Reconstruct(category);
    }
}
