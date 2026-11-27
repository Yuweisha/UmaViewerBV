using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gallop.CutIn.Cutt;
using Gallop.Live;
using UnityEngine;
using static Gallop.CutIn.Cutt.CutInTimelineWorkSheet;
using CharaTransformProcessData = Gallop.CharaTransformProcessData;
using CutEvent = Gallop.CutIn.Cutt.CutInTimelineWorkSheet.Event;

internal sealed class GachaCutInLoader : IDisposable
{
    internal const int PreviewLayer = 3;
    internal const string Course = "pfb_env_gacha_chara00_base00";
    internal const string CharacterCommentCut = "gac_chr_get_ssr1_001";
    internal static readonly string[] SupportRarities = { "r", "sr", "ssr" };
    private static readonly int[] SupportEffects = { 1, 2, 3, 4, 5, 7, 8, 9, 10, 11, 12 };
    internal const string GatePath = "3d/env/gacha/gacha_chara00/pfb_env_gacha_chara00_gate00_";
    internal static readonly string[] GateFinishes = { "normal", "sr", "ssr" };
    internal static readonly string[] GateEffects = { "pfb_eff_gacha_037", "pfb_eff_gacha_011", "pfb_eff_gacha_012" };
    private const string GateMotionPath = "3d/motion/gacha/bg/type00/anm_gac_type00_chr_gate_001_bg_gate_open";
    private const string FootShadowPrefab = "3d/effect/chara/pfb_eff_chr_shadow_01";
    private const string FootShadowRun = "3d/effect/chara/mtl_eff_chr_shadow_detail";
    private const string FootShadowStop = "3d/effect/chara/mtl_eff_chr_shadow_circle";
    private const string CharacterCompleteEffect = "uianimation/flashcombine/timeline/gacha/tat_gacha_eff_chara_complete00";
    private const string UiAdditiveMaterial = "assets/_gallop/resources/ui/materials/uiadditive";

    private readonly List<UmaDatabaseEntry> bundleLoads = new List<UmaDatabaseEntry>();
    private readonly List<Cut> cuts = new List<Cut>();
    private readonly List<Draw> draws = new List<Draw>();
    private readonly List<UmaContainerCharacter> lineupActors = new List<UmaContainerCharacter>();
    private readonly Dictionary<string, UmaDatabaseEntry> optionalEffects;
    public readonly Dictionary<string, GameObject> Prefabs = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<Cut> Cuts => cuts;
    public IReadOnlyList<Draw> Draws => draws;
    public IReadOnlyList<UmaContainerCharacter> LineupActors => lineupActors;
    public AnimationClip OpenGateClip { get; private set; }
    public AnimationClip OpenedGateClip { get; private set; }
    public UmaContainerCharacter OpeningActor { get; private set; }

    internal sealed class Selection
    {
        internal CharaEntry Character;
        internal int Card, CutInCard, Rarity, InitialGateFinish, DressId;
        internal string Costume, Head;
    }

    public GachaCutInLoader()
    {
        optionalEffects = UmaViewerMain.Instance.AbList.Values
            .Where(e => e.Name.StartsWith("3d/effect/", StringComparison.OrdinalIgnoreCase) &&
                e.Name.Contains("/pfb_eff_"))
            .GroupBy(e => Path.GetFileName(e.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    internal static bool HasUniqueCut(int card) => UmaViewerMain.Instance.AbList.ContainsKey(
        $"cutt/cutin/skill/crd{card:D6}_001/crd{card:D6}_001");

    internal static IEnumerable<UmaDatabaseEntry> CharacterBundles(IReadOnlyList<int> cards, int openingId,
        IReadOnlyList<Selection> selections = null)
    {
        var main = UmaViewerMain.Instance;
        var results = selections == null ? cards.Select(ReadDraw).ToArray() : null;
        var rarities = selections?.Select(result => result.CutInCard > 0 ? 3 : result.Rarity).ToArray()
            ?? results.Select(result => result.Rarity).ToArray();
        var cutCards = selections?.Select(result => result.CutInCard).ToArray() ?? cards;
        foreach (var cut in CharacterCuts(rarities, cutCards, openingId, selections))
            if (main.AbList.TryGetValue(TimelinePath(cut.Name), out var timeline))
                yield return timeline;

        foreach (var path in GateFinishes.Select(finish => GatePath + finish)
            .Concat(GateEffects.Select(effect => $"3d/effect/gacha/{effect}"))
            .Concat(new[] { GateMotionPath + "01", GateMotionPath + "02", "imageeffect/fluctuation/noise" }))
            if (main.AbList.TryGetValue(path, out var gate))
                yield return gate;

        if (rarities.Any(rarity => rarity == 3))
            foreach (var path in new[] { FootShadowPrefab, FootShadowRun, FootShadowStop,
                Gallop.Live.FootLightRuntime.DefaultShadowPath, CharacterCompleteEffect, UiAdditiveMaterial })
                if (main.AbList.TryGetValue(path, out var shadow))
                    yield return shadow;

        for (int i = 0; i < cards.Count; i++)
        {
            int textCard = selections?[i].CutInCard > 0 ? selections[i].CutInCard : cards[i];
            var character = selections?[i].Character ?? results[i].Character;
            var text = TextPaths(textCard, NameplateOwner(textCard, character.Id), rarities[i]);
            foreach (var path in new[] { text.Name, text.NameTexture, text.Comment, text.CommentEffect, text.CommentTexture })
                if (path != null && main.AbList.TryGetValue(path, out var entry))
                    yield return entry;
        }
    }

    public void Load(IReadOnlyList<int> cards, int openingId, Transform root,
        IReadOnlyList<Selection> selections = null, LiveCharacterLoadData openingActor = null)
    {
        if (cards.Count < 1 || cards.Count > 10)
            throw new ArgumentException("Gacha previews require 1 to 10 results.", nameof(cards));
        UmaAssetManager.LoadTracked(bundleLoads, () =>
        {
            for (int index = 0; index < cards.Count; index++)
            {
                var selected = selections?[index];
                draws.Add(selected == null ? LoadDraw(cards[index], root) : new Draw(
                    selected.CutInCard > 0 ? selected.CutInCard : selected.Card, selected.Character,
                    selected.CutInCard > 0 ? 3 : selected.Rarity,
                    CreateActor(selected.Character, selected.Costume, selected.Head, root, selected.DressId),
                    selected.InitialGateFinish, selected.CutInCard > 0));
            }
            var cutCards = selections?.Select(result => result.CutInCard).ToArray() ?? cards;
            foreach (var cut in CharacterCuts(draws.Select(draw => draw.Rarity).ToArray(), cutCards, openingId, selections))
                cuts.Add(LoadCut(cut.Name, cut.Cast, cut.ResultIndex, cut.ActorSlot, cut.Unique,
                    cut.StartFrame, cut.UpgradesGate, cut.GateAlreadyOpen));
            LoadFixedCast(root);
            LoadMiniCast(root);

            foreach (string finish in GateFinishes)
                if (!LoadPrefab(GatePath + finish))
                    throw new InvalidOperationException($"Gacha gate prefab unavailable: {finish}");
            foreach (string effect in GateEffects)
                LoadPrefab($"3d/effect/gacha/{effect}");
            OpenGateClip = LoadBundle(GateMotionPath + "01")?.LoadAllAssets<AnimationClip>().FirstOrDefault();
            OpenedGateClip = LoadBundle(GateMotionPath + "02")?.LoadAllAssets<AnimationClip>().FirstOrDefault();
            if (!OpenGateClip || !OpenedGateClip)
                throw new InvalidOperationException("Gacha gate motion unavailable.");

            if (openingId != 0)
            {
                var actor = openingActor?.CharaEntry ?? UmaViewerMain.Instance.Characters.FirstOrDefault(c => c.Id == openingId && !c.IsMob)
                    ?? new CharaEntry { Id = openingId };
                OpeningActor = CreateActor(actor, openingActor?.CostumeId ?? "00", openingActor?.HeadCostumeId ?? "00",
                    root, openingActor?.MasterDressId ?? 0);
            }
            // MakeMob uses character 1, mob 8019 and race dress 1 for the gate silhouettes.
            var mob = new CharaEntry { Id = 8019, IsMob = true };
            var database = UmaDatabaseController.Instance;
            string raceDress = LiveFormationLoader.CostumeForDress(database.ReadDressData(1), mob, database);
            for (int i = 0; openingId != 0 && i < draws.Count; i++)
                lineupActors.Add(CreateActor(mob, raceDress, "00", root));
        });
    }

    private static Draw LoadDraw(int card, Transform root)
    {
        var result = ReadDraw(card);
        return new Draw(card, result.Character, result.Rarity,
            CreateActor(result.Character, result.Costume, result.Head, root, result.DressId));
    }

    private void LoadFixedCast(Transform root)
    {
        var database = UmaDatabaseController.Instance;
        var actors = new Dictionary<(int Character, string Costume, string Head), UmaContainerCharacter>();
        foreach (var cut in cuts.Where(cut => cut.Unique))
        {
            var draw = draws[cut.ResultIndex];
            for (int slot = 0; slot < cut.Sheet._characterList.Length; ++slot)
            {
                var track = cut.Sheet._characterList[slot]._characterKeys;
                if (slot == cut.ActorSlot && draw.OverrideActor)
                    continue;
                // Native FixCharaId and CardId slots send their authored model and dress IDs to the loader.
                if (track == null ||
                    (track._characterType != 3 && track._characterType != 4) || !Keys(track).Any(key => key._visible))
                    continue;

                var character = UmaViewerMain.Instance.Characters.FirstOrDefault(c => c.Id == track._selectCharaId && !c.IsMob);
                var dress = database.ReadDressData(track._selectClothId);
                if (character == null || dress == null)
                    throw new InvalidOperationException($"Gacha CutIn cast unavailable: {cut.Name}, slot {slot}, " +
                        $"{track._selectCharaId}/{track._selectClothId}");

                // JP CharacterBuildInfo.Rebuild uses the dress head only for an unspecified (0) head.
                // Explicit timeline heads must survive, e.g. 100501's hat uses head 01.
                int head = track._selectHeadId == 0 && track._isUseDressDataHeadModelSubId
                    ? Convert.ToInt32(dress["head_sub_id"])
                    : track._selectHeadId;
                if (slot == cut.ActorSlot && draw.Character.Id == character.Id &&
                    draw.Actor.LiveMasterDressId == track._selectClothId &&
                    draw.Actor.LiveHeadModelSubId == head)
                    continue;

                string costume = LiveFormationLoader.CostumeForDress(dress, character, database);
                var key = (character.Id, costume, head.ToString("D2"));
                if (!actors.TryGetValue(key, out var actor) || cut.FixedCast.ContainsValue(actor))
                {
                    actor = CreateActor(character, costume, key.Item3, root, track._selectClothId);
                    actors[key] = actor;
                }
                cut.FixedCast.Add(slot, actor);
            }
        }
    }

    private static (CharaEntry Character, int Rarity, string Costume, string Head, int DressId) ReadDraw(int card)
    {
        var database = UmaDatabaseController.Instance;
        var result = database.ReadGachaCard(card);
        if (result == null)
            throw new InvalidOperationException($"Gacha card unavailable: {card}");
        int characterId = Convert.ToInt32(result["chara_id"]);
        int rarity = Convert.ToInt32(result["default_rarity"]);
        var character = UmaViewerMain.Instance.Characters.FirstOrDefault(c => c.Id == characterId && !c.IsMob);
        int dressId = Convert.ToInt32(result["race_dress_id"]);
        var dress = database.ReadDressData(dressId);
        if (character == null || dress == null || rarity < 1 || rarity > 3)
            throw new InvalidOperationException($"Gacha card character, rarity or race dress unavailable: {card}");

        string costume = LiveFormationLoader.CostumeForDress(dress, character, database);
        string head = Convert.ToInt32(dress["head_sub_id"]).ToString("D2");
        return (character, rarity, costume, head, dressId);
    }

    private void LoadMiniCast(Transform root)
    {
        foreach (var cut in cuts)
        {
            foreach (var track in cut.Sheet.MiniCharacterDataList ?? Array.Empty<MiniCharacterTrack>())
            {
                var keys = track.CharacterKeys;
                if (!Keys(keys).Any(key => key.IsVisible))
                    continue;

                var host = new GameObject($"Mini preview {keys._selectCharaId}").transform;
                host.SetParent(root, false);
                var model = UmaViewerBuilder.Instance.LoadLiveMiniUma(new LiveMiniSelection.MiniCharacterInfo
                {
                    CharaId = keys._selectCharaId,
                    DressId = keys._selectClothId
                }, host, keys._isUseDressDataHeadModelSubId ? (int?)null : keys._selectHeadId);
                var mini = host.gameObject.AddComponent<LiveMiniCharacter>();
                // Resolve the Mini controller assets while loading the preview, before playback starts.
                // CutIn samples position on a locator outside the body rig.
                mini.Initialize(model, host.gameObject.AddComponent<Animator>());
                SetLayer(host.gameObject);
                mini.SetVisible(false);
                host.gameObject.SetActive(false);
                cut.MiniCast.Add((track, mini, model));
            }
        }
    }

    private static IEnumerable<CharacterCut> CharacterCuts(IReadOnlyList<int> rarities, IReadOnlyList<int> cards, int openingId,
        IReadOnlyList<Selection> selections = null)
    {
        if (openingId != 0)
            yield return new CharacterCut(openingId == 9002 ? "gac_chr_start_002" : "gac_chr_start_001", Cast.Opening);
        string lineup = cards.Count == 1 ? "gac_chr_gate_single_001" :
            cards.Count <= 5 ? "gac_chr_gate_5ren_001" : "gac_chr_gate_001";
        if (openingId != 0)
            yield return new CharacterCut(lineup, Cast.Lineup);
        for (int i = 0; i < cards.Count; i++)
        {
            bool hasCutIn = selections == null ? rarities[i] == 3 : selections[i].CutInCard > 0;
            bool upgradesGate = openingId != 0 && hasCutIn &&
                (selections == null || selections[i].InitialGateFinish < 2);
            if (upgradesGate)
                yield return new CharacterCut("gac_chr_gate_more_001", Cast.Result, i, actorSlot: 10,
                    startFrame: 24, upgradesGate: true);
            if (openingId != 0 && (i == 0 || hasCutIn))
                yield return new CharacterCut(hasCutIn ? "gac_chr_gate_out2_001" : "gac_chr_gate_out1_001",
                    Cast.Result, i, actorSlot: 10);
            if (hasCutIn)
            {
                yield return new CharacterCut(CharacterCommentCut, Cast.Result, i, gateAlreadyOpen: true);
                yield return new CharacterCut($"crd{cards[i]:D6}_001", Cast.Result, i, unique: true);
            }
            else
            {
                yield return new CharacterCut("gac_chr_get_r_sr_001", Cast.Result, i, actorSlot: 9,
                    startFrame: 6, gateAlreadyOpen: true);
            }
        }
        yield return new CharacterCut("gac_chr_result_001", Cast.None);
    }

    private readonly struct CharacterCut
    {
        public readonly string Name;
        public readonly Cast Cast;
        public readonly int ResultIndex;
        public readonly int ActorSlot;
        public readonly bool Unique;
        public readonly int StartFrame;
        public readonly bool UpgradesGate;
        public readonly bool GateAlreadyOpen;

        public CharacterCut(string name, Cast cast, int resultIndex = -1, int actorSlot = 0,
            bool unique = false, int startFrame = 0, bool upgradesGate = false, bool gateAlreadyOpen = false)
        {
            Name = name;
            Cast = cast;
            ResultIndex = resultIndex;
            ActorSlot = actorSlot;
            Unique = unique;
            StartFrame = startFrame;
            UpgradesGate = upgradesGate;
            GateAlreadyOpen = gateAlreadyOpen;
        }
    }

    public void Dispose()
    {
        UmaAssetManager.ReleaseTracked(bundleLoads);
    }

    public GachaSupportStage LoadSupport(IReadOnlyList<int> cards, Transform root, Camera camera,
        GachaSupportStage.Options options)
    {
        if (cards.Count < 1 || cards.Count > 10)
            throw new ArgumentException("The normal support album holds one to ten cards.", nameof(cards));
        options.ValidateCount(cards.Count);

        GachaSupportStage stage = null;
        UmaAssetManager.LoadTracked(bundleLoads, () =>
        {
            var results = cards.Select(LoadSupportDraw).ToArray();
            int variant = options.Sky == GachaSupportStage.SkyType.Normal ? 1 : 11;
            string folder = variant == 1 ? "gacha_support_card00" : "gacha_support_card02";
            string prefabPrefix = "pfb_env_" + folder + "_";
            string stagePath = "3d/env/gacha/" + folder + "/" + prefabPrefix;
            cuts.Add(LoadCut($"gac_spt_start_{variant:D3}", Cast.None, support: true));
            var tiers = new HashSet<int>();
            for (int i = 0; i < results.Length; i++)
            {
                int rarity = results[i].Rarity;
                var upgrade = options.UpgradeAt(i);
                tiers.Add(rarity);
                string page = $"gac_spt_page{(i == 0 ? 1 : 2)}";
                if (upgrade != GachaSupportStage.Upgrade.None)
                {
                    if (rarity == 1 || (upgrade == GachaSupportStage.Upgrade.StepUp && rarity != 3))
                        throw new ArgumentException($"The requested support upgrade is unavailable for card {cards[i]}.");
                    tiers.Add(1);
                    if (upgrade == GachaSupportStage.Upgrade.StepUp)
                    {
                        tiers.Add(2);
                        cuts.Add(LoadCut($"{page}_more_{variant:D3}", Cast.None, i, support: true,
                            albumUpgradeRarity: 2));
                    }
                    cuts.Add(LoadCut($"{page}_more_{variant:D3}", Cast.None, i, support: true,
                        albumUpgradeRarity: rarity, albumUpgradeCanSkip: true));
                }
                cuts.Add(LoadCut($"{page}_{SupportRarities[rarity - 1]}_{variant:D3}", Cast.None, i, support: true));
            }
            cuts.Add(LoadCut($"gac_spt_result_{variant:D3}", Cast.None, support: true));
            cuts.Add(LoadCut($"gac_spt_result_{variant + 1:D3}", Cast.None, support: true));
            foreach (var name in new[] { "album_r", "photo", "page_blank" })
                LoadPrefab(stagePath + name);
            foreach (int tier in tiers)
            {
                string rarity = SupportRarities[tier - 1];
                LoadPrefab(stagePath + "page_" + rarity);
                LoadPrefab(stagePath + "bookmark_" + rarity);
            }
            foreach (int effect in SupportEffects)
                LoadSupportEffect(effect);
            LoadSupportEffect(variant == 1 ? 13 : 18);
            if (options.Sky == GachaSupportStage.SkyType.NightStardust)
            {
                LoadSupportEffect(19);
                LoadSupportEffect(20);
            }
            foreach (int rarity in cuts.Select(cut => cut.AlbumUpgradeRarity).Where(rarity => rarity > 0).Distinct())
            {
                LoadSupportEffect(rarity + 12);
                LoadSupportEffect(rarity + 14);
            }
            stage = new GachaSupportStage(root, Prefabs, results, camera, options, prefabPrefix);
        });
        return stage;
    }

    private void LoadSupportEffect(int effect) => LoadPrefab($"3d/effect/gachacard/pfb_eff_gacha_cd_{effect:D3}");

    private static GachaSupportStage.Result LoadSupportDraw(int card)
    {
        var database = UmaDatabaseController.Instance;
        var data = database.ReadGachaSupportCard(card);
        if (data == null)
            throw new InvalidOperationException($"Support card unavailable: {card}");
        int rarity = Convert.ToInt32(data["rarity"]);
        int character = Convert.ToInt32(data["chara_id"]);
        bool group = Convert.ToInt32(data["support_card_type"]) == 3;
        string texture = $"tex_support_card_{card:D5}";
        var illustration = LoadBundle($"supportcard/support{card:D5}/{texture}")?.LoadAsset<Texture2D>(texture);
        string nameTexture = group ? $"gacha_supportname_{card:D5}_{rarity:D2}" : $"gacha_chrname_{character:D4}_{rarity:D2}";
        string nameFolder = group ? "supportname" : "charaname";
        var name = LoadBundle($"gacha/{nameFolder}/rarity_{rarity}/{nameTexture}")?.LoadAsset<Texture2D>(nameTexture);
        if (!illustration || !name || rarity < 1 || rarity > 3)
            throw new InvalidOperationException($"Support card artwork unavailable: {card}");
        string flashName = $"fa_gacha_supportcard_{SupportRarities[rarity - 1]}00";
        var flash = LoadBundle($"uianimation/flashcombine/action/gacha/{flashName}")?.LoadAsset<GameObject>(flashName);
        if (!flash)
            throw new InvalidOperationException($"Support card Flash unavailable: {card}");
        return new GachaSupportStage.Result(card, rarity, illustration, name, flash,
            database.ReadGachaSupportTitle(card), database.ReadGachaSupportName(card));
    }

    public GachaPreviewFlash LoadSupportFlash(Camera camera, GachaSupportStage.Result result, Camera photoCamera) =>
        new GachaPreviewFlash(camera, result.Flash, result.Art, result.NameTexture, result.Title, result.Rarity,
            photoCamera, LoadFlashPrefab);

    private static UmaContainerCharacter CreateActor(CharaEntry entry, string costume, string head, Transform root, int dressId = 0)
    {
        var model = UmaViewerBuilder.Instance.CreateGachaCharacter(entry, costume, head, root);
        if (!model.UmaAnimator || !model.Body || !model.FaceDrivenKeyTarget)
            throw new InvalidOperationException($"Gacha model unavailable: {entry.Id}/{costume}");
        model.UmaAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        model.UmaAnimator.speed = 0f;
        model.UmaAnimator.enabled = false;
#if UNITY_EDITOR
        model.UmaFaceAnimator.enabled = false;
#else
        model.UmaFaceAnimator.runtimeAnimatorController = null;
        model.UmaFaceAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        model.UmaFaceAnimator.enabled = true;
#endif
        model.isAnimatorControl = true;
        model.EnableEyeTracking = false;
        model.LiveMasterDressId = dressId;
        SetLayer(model.gameObject);
        model.gameObject.SetActive(false);
        return model;
    }

    private Cut LoadCut(string name, Cast cast, int resultIndex = -1, int actorSlot = 0, bool unique = false,
        int startFrame = 0, bool upgradesGate = false, bool gateAlreadyOpen = false, bool support = false,
        int albumUpgradeRarity = 0, bool albumUpgradeCanSkip = false)
    {
        string path = TimelinePath(name);
        var bundle = LoadBundle(path);
        var controller = bundle?.LoadAllAssets<GameObject>()
            .Select(p => p.GetComponentInChildren<CutInTimelineController>(true)).FirstOrDefault(c => c);
        var data = controller ? controller._data : null;
        var sheet = data ? data._worksheetList?.FirstOrDefault(s => s && s._enableAtRuntime) : null;
        if (!sheet || Keys(sheet._cameraMotionKeys).Length == 0)
            throw new InvalidOperationException($"Gacha CutIn timeline unavailable: {path}");

        var deps = UmaViewerMain.Instance.AbList[path].Prerequisites?.Split(';') ?? Array.Empty<string>();
        foreach (var track in sheet._bgList ?? Array.Empty<BackgroundGroup>())
        {
            if (Keys(track._keys).Length == 0 || Prefabs.ContainsKey(track._name) ||
                (support && track._keys._loadedType == 1))
                continue;
            if (track._keys._prefab)
            {
                if (track._name == Course)
                    Prefabs.Add(Course, track._keys._prefab);
                continue;
            }
            var resource = deps.FirstOrDefault(p => p.EndsWith("/" + track._name, StringComparison.OrdinalIgnoreCase));
            if (resource == null || !LoadPrefab(resource))
            {
                if (Keys(track._keys).Any(key => key._visible))
                    throw new InvalidOperationException($"Gacha stage unavailable: {track._name}");
            }
        }

        foreach (var track in sheet._effectList ?? Array.Empty<EffectGroup>())
        {
            if (Keys(track._keys).Length == 0 || track._keys.IsFlash || track._keys._prefab)
                continue;
            string effect = EffectName(track._name);
            if (optionalEffects.TryGetValue(effect, out var entry))
                LoadPrefab(entry.Name);
        }

        if (cast != Cast.None &&
            (sheet._characterList == null || actorSlot >= sheet._characterList.Length ||
                Keys(sheet._characterList[actorSlot]._characterMotionKeys).Length == 0))
            throw new InvalidOperationException($"Gacha actor slot {actorSlot} unavailable: {path}");

        return new Cut(name, data, sheet, cast, resultIndex, actorSlot, unique, startFrame, upgradesGate, gateAlreadyOpen,
            albumUpgradeRarity, albumUpgradeCanSkip);
    }

    public GachaPreviewFlash LoadTextEffects(Camera camera, Draw draw)
    {
        int card = draw.Card;
        int rarity = draw.Rarity;
        GachaPreviewFlash flash = null;
        UmaAssetManager.LoadTracked(bundleLoads, () =>
        {
            var paths = TextPaths(card, draw.NameplateCharacter, rarity);
            var database = UmaDatabaseController.Instance;
            string cardTitle = database.ReadGachaCardTitle(card);
            var commentPrefab = LoadNamedAsset<GameObject>(paths.Comment);
            var commentTexture = LoadNamedAsset<Texture2D>(paths.CommentTexture);
            var commentEffect = LoadNamedAsset<GameObject>(paths.CommentEffect);
            var namePrefab = LoadNamedAsset<GameObject>(paths.Name);
            var nameTexture = LoadNamedAsset<Texture2D>(paths.NameTexture);
            var nameEffect = rarity == 3 ? LoadNamedAsset<GameObject>(CharacterCompleteEffect) : null;
            var nameEffectMaterial = nameEffect ? LoadNamedAsset<Material>(UiAdditiveMaterial) : null;
            var font = Config.Instance.Language == Language.Cn ? HachimiGachaResources.Font : null;
            var localizedName = Config.Instance.Language == Language.Cn && namePrefab
                ? HachimiGachaResources.LoadName(paths.NameTexture) : null;
            var localizedComment = Config.Instance.Language == Language.Cn && commentPrefab
                ? HachimiGachaResources.LoadComment(card)
                : null;
            flash = new GachaPreviewFlash(camera, commentPrefab, commentTexture, commentEffect,
                namePrefab, nameTexture, nameEffect, nameEffectMaterial, cardTitle, LoadFlashPrefab, font,
                localizedComment, localizedName);
        });
        return flash;
    }

    // The card identifies the owner of the result artwork even when another actor performs it.
    private static int NameplateOwner(int card, int actor) =>
        card > 0 && UmaDatabaseController.Instance.ReadGachaCard(card) is System.Data.DataRow row
            ? Convert.ToInt32(row["chara_id"]) : actor;

    private static string TimelinePath(string name) => $"cutt/cutin/skill/{name}/{name}";

    private static (string Name, string NameTexture, string Comment, string CommentEffect, string CommentTexture)
        TextPaths(int card, int character, int rarity) =>
        ($"uianimation/flashcombine/action/gacha/fa_gacha_chara_name{rarity - 1:D2}_00",
         $"gacha/charaname/rarity_{rarity}/gacha_chrname_{character:D4}_{rarity:D2}",
         rarity == 3 ? "uianimation/flash/gacha/pf_fl_gacha_chara_comment00" : null,
         rarity == 3 ? "uianimation/uiunityanimation/gacha/gacha_chara_comment00/gacha_chara_comment00" : null,
         rarity == 3 ? $"gacha/comment/gacha_comment_{card:D6}" : null);

    private static T LoadNamedAsset<T>(string path) where T : UnityEngine.Object =>
        path == null ? null : LoadBundle(path)?.LoadAsset<T>(Path.GetFileName(path));

    public GachaCharacterShadow CreateCharacterShadow(UmaContainerCharacter actor, CharacterTrack track)
    {
        var keys = Keys(track._characterShadowKeys?.FirstOrDefault());
        if (keys.Length == 0)
            return null;
        bool hasFoot = keys.Any(GachaCharacterShadow.HasFootShadow);
        bool hasCross = keys.Any(GachaCharacterShadow.HasCrossShadow);
        if (!hasFoot && !hasCross)
            return new GachaCharacterShadow(actor, keys, null, null, null, null);

        GameObject prefab = null, cross = null;
        Material moving = null, stopped = null;
        UmaAssetManager.LoadTracked(bundleLoads, () =>
        {
            if (hasFoot)
            {
                prefab = LoadPrefab(FootShadowPrefab);
                moving = LoadNamedAsset<Material>(FootShadowRun);
                stopped = LoadNamedAsset<Material>(FootShadowStop);
            }
            if (hasCross)
                cross = LoadPrefab(Gallop.Live.FootLightRuntime.DefaultShadowPath);
        });
        if (hasFoot && (!prefab || !moving || !stopped))
            Debug.LogWarning("Gacha foot shadow assets unavailable.");
        if (hasCross && !cross)
            Debug.LogWarning("Gacha cross shadow asset unavailable.");
        return new GachaCharacterShadow(actor, keys, prefab, moving, stopped, cross);
    }

    private GameObject LoadFlashPrefab(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;
        string normalized = path.Replace('\\', '/').ToLowerInvariant();
        GameObject prefab = null;
        UmaAssetManager.LoadTracked(bundleLoads, () => prefab = LoadPrefab(normalized));
        return prefab;
    }

    private static AssetBundle LoadBundle(string path)
    {
        return UmaViewerMain.Instance.AbList.TryGetValue(path, out var entry)
            ? UmaAssetManager.LoadAssetBundle(entry)
            : null;
    }

    internal CharaTransformProcessData LoadTransformProcessData(string modelPath)
    {
        if (string.IsNullOrEmpty(modelPath))
            return null;
        int separator = modelPath.LastIndexOf('/');
        if (separator <= 0)
            return null;
        string directory = modelPath.Substring(0, separator);
        string name = $"ast_{Path.GetFileName(directory)}_transform_process_data";
        string path = $"{directory}/extensions/{name}";
        CharaTransformProcessData data = null;
        UmaAssetManager.LoadTracked(bundleLoads, () => data = LoadBundle(path)?.LoadAsset<CharaTransformProcessData>(name));
        return data;
    }

    internal GachaSceneCards LoadSceneCards(Transform parent, SceneCardGroup[] groups, IEnumerable<int> actorIds)
    {
        var index = UmaViewerMain.Instance.AbList;
        var excluded = new HashSet<int>(actorIds);
        var database = UmaDatabaseController.Instance;
        var candidates = database.ReadAvailableSceneCards(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            .Where(card => !excluded.Contains(card.Character))
            .Select(card => card.Id)
            .ToList();
        bool canDownload = Config.Instance.DownloadMissingResources || Config.Instance.WorkMode == WorkMode.Standalone;
        var art = new List<(int Id, Texture2D Texture, bool Thumbnail)>(groups.Length);
        Texture2D thumbMask = null, fullMask = null;
        UmaAssetManager.LoadTracked(bundleLoads, () =>
        {
            thumbMask = LoadTexture("supportcard/support00000/tex_support_thumb_00000_mask");
            fullMask = LoadTexture("supportcard/support00000/tex_support_card_00000_mask");
            while (art.Count < groups.Length && candidates.Count > 0)
            {
                int slot = UnityEngine.Random.Range(0, candidates.Count);
                int id = candidates[slot];
                candidates[slot] = candidates[candidates.Count - 1];
                candidates.RemoveAt(candidates.Count - 1);
                var folder = $"supportcard/support{id:D5}/";
                var thumb = thumbMask ? LoadTexture(folder + $"support_thumb_{id:D5}") : null;
                var texture = thumb ? thumb : LoadTexture(folder + $"tex_support_card_{id:D5}");
                if (texture)
                    art.Add((id, texture, thumb != null));
            }
        });
        return art.Count == 0 ? null : GachaSceneCards.Create(parent, groups, art, thumbMask, fullMask);

        Texture2D LoadTexture(string path)
        {
            return index.TryGetValue(path, out var entry) && (canDownload || File.Exists(entry.Path))
                ? UmaAssetManager.LoadAssetBundle(entry)?.LoadAsset<Texture2D>(Path.GetFileName(path))
                : null;
        }
    }

    private GameObject LoadPrefab(string path)
    {
        string name = Path.GetFileName(path);
        if (Prefabs.TryGetValue(name, out var prefab))
            return prefab;
        prefab = LoadBundle(path)?.LoadAsset<GameObject>(name);
        if (prefab)
            Prefabs.Add(name, prefab);
        return prefab;
    }

    internal static void SetLayer(GameObject instance, int layer = PreviewLayer)
    {
        GachaCutInPlayer.ReplayResources.SeedParticles(instance);
        foreach (var node in instance.GetComponentsInChildren<Transform>(true))
            node.gameObject.layer = layer;
    }

    internal static string EffectName(string name) => name.Split('(')[0];

    public GameObject BackgroundPrefab(BackgroundGroup track) =>
        track._keys._prefab ? track._keys._prefab : Prefabs.GetValueOrDefault(track._name);

    public GameObject EffectPrefab(EffectGroup track) =>
        track._keys._prefab ? track._keys._prefab : Prefabs.GetValueOrDefault(EffectName(track._name));

    public (GameObject Prefab, int RandomEntryIndex) LoadProp(PropTrack track)
    {
        if (Keys(track).Length == 0)
            return (null, -1);
        if (!track._enableRandomEntry || track._randomEntryArray == null)
            return (track._charaPropPrefab, -1);
        var entries = track._randomEntryArray;
        int weight = entries.Sum(entry => entry != null && entry._propId > 0 ? Math.Max(0, entry._weight) : 0);
        if (weight == 0)
            return (track._charaPropPrefab, -1);
        int roll = UnityEngine.Random.Range(0, weight);
        for (int index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            if (entry == null || entry._propId <= 0 || entry._weight <= 0)
                continue;
            roll -= entry._weight;
            if (roll >= 0)
                continue;
            string id = $"prop{entry._propId:D4}_{entry._propSubId:D2}";
            GameObject prefab = null;
            UmaAssetManager.LoadTracked(bundleLoads, () => prefab = LoadPrefab($"3d/chara/prop/{id}/pfb_chr_{id}"));
            return prefab ? (prefab, index) : (track._charaPropPrefab, -1);
        }
        return (track._charaPropPrefab, -1);
    }

    internal static T[] Keys<T>(Track<T> track) => track?.thisList ?? Array.Empty<T>();

    internal enum Cast { None, Opening, Lineup, Result }

    internal sealed class Draw
    {
        public readonly int Card;
        public readonly CharaEntry Character;
        public readonly int Rarity;
        public readonly UmaContainerCharacter Actor;
        public readonly int NameplateCharacter;
        public readonly int InitialGateFinish;
        public readonly bool OverrideActor;

        public Draw(int card, CharaEntry character, int rarity, UmaContainerCharacter actor,
            int initialGateFinish = -1, bool overrideActor = false)
        {
            Card = card;
            Character = character;
            NameplateCharacter = NameplateOwner(card, character.Id);
            Rarity = rarity;
            Actor = actor;
            InitialGateFinish = initialGateFinish < 0 ? Math.Min(rarity - 1, 1) : initialGateFinish;
            OverrideActor = overrideActor;
        }

        public string Label => $"{new string('★', Rarity)}  {Character.GetName()}";
    }

    internal sealed class Cut
    {
        public readonly string Name;
        public readonly CutInTimelineData Data;
        public readonly CutInTimelineWorkSheet Sheet;
        public readonly Cast Cast;
        public readonly int ResultIndex;
        public readonly int ActorSlot;
        public readonly bool Unique;
        public readonly int StartFrame;
        public readonly bool UpgradesGate;
        public readonly bool GateAlreadyOpen;
        public readonly int AlbumUpgradeRarity;
        public readonly bool AlbumUpgradeCanSkip;
        public readonly Dictionary<int, CutEvent[]> Events;
        public readonly Dictionary<int, SoundKey[]> Sounds;
        public readonly Dictionary<int, UmaContainerCharacter> FixedCast = new Dictionary<int, UmaContainerCharacter>();
        public readonly List<(MiniCharacterTrack Track, LiveMiniCharacter Player, UmaContainerCharacter Model)> MiniCast =
            new List<(MiniCharacterTrack, LiveMiniCharacter, UmaContainerCharacter)>();

        public Cut(string name, CutInTimelineData data, CutInTimelineWorkSheet sheet, Cast cast,
            int resultIndex, int actorSlot, bool unique, int startFrame, bool upgradesGate, bool gateAlreadyOpen,
            int albumUpgradeRarity, bool albumUpgradeCanSkip)
        {
            Name = name;
            Data = data;
            Sheet = sheet;
            Cast = cast;
            ResultIndex = resultIndex;
            ActorSlot = actorSlot;
            Unique = unique;
            StartFrame = startFrame;
            UpgradesGate = upgradesGate;
            GateAlreadyOpen = gateAlreadyOpen;
            AlbumUpgradeRarity = albumUpgradeRarity;
            AlbumUpgradeCanSkip = albumUpgradeCanSkip;
            Events = Keys(sheet._eventKeys).GroupBy(key => key._frame)
                .ToDictionary(g => g.Key, g => g.SelectMany(key => key.eventList ?? Array.Empty<CutEvent>()).ToArray());
            Sounds = (sheet._soundList ?? Array.Empty<NamedTrack<SoundKey>>())
                .SelectMany(track => Keys(track._keys)).GroupBy(key => key._frame)
                .ToDictionary(g => g.Key, g => g.ToArray());
        }
    }

}
