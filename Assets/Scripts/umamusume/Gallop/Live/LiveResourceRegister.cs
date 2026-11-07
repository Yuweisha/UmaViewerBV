using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;

namespace Gallop.Live
{
    /// <summary>
    /// Builds the Live download roots before UmaAssetManager expands bundle dependencies.
    /// The target registers Director roots first, then the body/head/tail folders and
    /// shared face assets for each selected character (RegisterDownload RVA 0x1ab98b0).
    /// This is not an AssetBundle.GetAllAssetNames/LoadAsset count.
    /// </summary>
    public static class LiveResourceRegister
    {
        public static List<UmaDatabaseEntry> RegisterDownload(LiveEntry live, IList<LiveCharacterLoadData> characters, bool requireStage)
        {
            var roots = new List<UmaDatabaseEntry>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var main = UmaViewerMain.Instance;
            if (main == null || main.AbList == null)
                return roots;
            void Add(UmaDatabaseEntry entry)
            {
                if (entry != null && !string.IsNullOrEmpty(entry.Name) && names.Add(entry.Name))
                    roots.Add(entry);
            }

            void TryRegister(string path)
            {
                if (!string.IsNullOrEmpty(path) && main.AbList.TryGetValue(path, out var entry))
                    Add(entry);
            }

            // RegisterFolder: AbList.Values.Where(entry.Name.StartsWith(prefix,
            // OrdinalIgnoreCase)) -> RegisterEntries. No arbitrary global chara scan.
            void RegisterFolder(string prefix)
            {
                if (string.IsNullOrEmpty(prefix))
                    return;
                foreach (var entry in main.AbList.Values)
                    if (entry != null && entry.Name != null && entry.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        Add(entry);
            }

            void RegisterFaceRuntime()
            {
                TryRegister("3d/animator/drivenkeylocator");
                TryRegister("3d/chara/common/textures/tex_chr_tear00");
                for (int i = 0; i < 4; i++)
                    TryRegister("3d/effect/charaemotion/pfb_eff_chr_emo_eye_" + i.ToString("000", CultureInfo.InvariantCulture));
                TryRegister("3d/chara/common/tear/tear000/pfb_chr_tear000");
                TryRegister("3d/chara/common/tear/tear001/pfb_chr_tear001");
            }

            foreach (var entry in Director.GetLivePreloadEntries(live, characters == null ? null : new List<LiveCharacterLoadData>(characters), requireStage))
                Add(entry);
            // Original Director.RegisterDownload 0x4e322d0 requests model0
            // before its per-character sweat locators. This host preload bridge
            // uses the same selected-head identities as the actual model loader.
            TryRegister(UmaContainerCharacter.LiveSweatModelResource);
            TryRegister("atlas/live/live_tex");
            var settings = LiveSettingsResourceMap.Load(live.MusicId);
            foreach (var row in settings.RawRows)
                if (row.Type == 14 && row.Fields.Count >= 3)
                {
                    int kind = int.Parse(row.Fields[2], CultureInfo.InvariantCulture);
                    TryRegister(Director.GetLiveFlashResourcePath(kind)?.ToLowerInvariant());
                    if (kind == 1)
                    {
                        var race = UmaDatabaseController.Instance.ReadLatestChampionsLiveRaceInfo();
                        TryRegister($"live/image/champions/tex_championslive_racename_{race.ChampionsMeetingResourceId:D2}");
                        TryRegister($"live/image/champions/tex_championslive_year_{race.DateYear - 2022:D2}");
                        TryRegister($"live/image/champions/tex_championslive_year_l_{race.DateYear - 2022:D2}");
                    }

                    if (kind == 2 || kind == 4)
                        TryRegister("font/mincho/fot-tsukuminpro-e");
                    if (kind == 2)
                    {
                        int id = characters != null && characters.Count > 0 ? (characters[0].CharaEntry.IsMob ? 1 : characters[0].CharaEntry.Id) : 0;
                        TryRegister(LiveFlashLegendController.GetCatchCopyImagePath(id, settings.PermissionCharacterVoices.Contains(id)).ToLowerInvariant());
                    }

                    if (kind == 5)
                        foreach (string path in LiveFlashRamenController.GetLyricsImagePaths())
                            TryRegister(path.ToLowerInvariant());
                }

            int directorRoots = roots.Count;
            int normalRoots = 0, mobRoots = 0, normalCharacters = 0, mobCharacters = 0, skipped = 0;
            if (characters != null)
            {
                foreach (var selection in characters)
                {
                    var chara = selection?.CharaEntry;
                    string costume = selection?.CostumeId;
                    if (chara == null || string.IsNullOrEmpty(costume))
                    {
                        skipped++;
                        continue;
                    }

                    DataRow data = UmaDatabaseController.ReadCharaData(chara);
                    if (data == null)
                    {
                        skipped++;
                        continue;
                    }

                    int before = roots.Count;
                    string bodyFolder = costume.Length >= 4 && costume.LastIndexOf('_') > 0 ? "bdy" + costume.Substring(0, costume.LastIndexOf('_')) + "/" : "bdy" + chara.Id + "_" + costume + "/";
                    RegisterFolder(UmaDatabaseController.BodyPath + bodyFolder);
                    if (chara.IsMob)
                    {
                        // RegisterMob RVA 0x1ab1e60: fixed mob head/tail folders.
                        RegisterFolder(UmaDatabaseController.HeadPath + "chr0001_00/");
                        string face = Convert.ToInt32(data["chara_face_model"], CultureInfo.InvariantCulture).ToString("000", CultureInfo.InvariantCulture);
                        TryRegister("3d/chara/sweat/chr0001_00/pfb_chr0001_00_face" + face + "_sweatlocator");
                        RegisterFolder("3d/chara/tail/tail0001_00/");
                        RegisterFaceRuntime();
                        mobRoots += roots.Count - before;
                        mobCharacters++;
                        continue;
                    }

                    // RegisterNormal RVA 0x1ab2130: explicit head prefab if present,
                    // otherwise the default chr{id}_00 folder. Match Live's head override.
                    int headId = chara.Id;
                    string headCostume = string.IsNullOrEmpty(selection.HeadCostumeId) ? costume : selection.HeadCostumeId;
                    int tailId = Convert.ToInt32(data["tail_model_id"], CultureInfo.InvariantCulture);
                    var builder = UmaViewerBuilder.Instance;
                    if (UmaViewerUI.Instance != null && UmaViewerUI.Instance.ModelSettings.IsHeadFix && builder != null && builder.CurrentHead != null)
                    {
                        headId = builder.CurrentHead.id;
                        headCostume = builder.CurrentHead.costumeId;
                        tailId = builder.CurrentHead.tailId;
                    }

                    string headFolder = UmaDatabaseController.HeadPath + "chr" + headId + "_" + headCostume + "/";
                    string headPrefab = headFolder + "pfb_chr" + headId + "_" + headCostume;
                    if (main.AbList.ContainsKey(headPrefab))
                    {
                        TryRegister(headPrefab);
                        RegisterFolder(headFolder);
                        TryRegister("3d/chara/sweat/chr" + headId + "_" + headCostume + "/pfb_chr" + headId + "_" + headCostume + "_sweatlocator");
                    }
                    else
                    {
                        RegisterFolder(UmaDatabaseController.HeadPath + "chr" + headId + "_00/");
                        TryRegister("3d/chara/sweat/chr" + headId + "_00/pfb_chr" + headId + "_00_sweatlocator");
                    }

                    if (tailId > 0)
                    {
                        string exclusive = "3d/chara/tail/tail" + chara.Id + "_" + costume + "/";
                        string exclusivePrefab = exclusive + "pfb_tail" + chara.Id + "_" + costume;
                        if (main.AbList.ContainsKey(exclusivePrefab))
                        {
                            TryRegister(exclusivePrefab);
                            RegisterFolder(exclusive);
                        }
                        else
                        {
                            RegisterFolder("3d/chara/tail/tail" + tailId.ToString("0000", CultureInfo.InvariantCulture) + "_00/");
                        }
                    }

                    RegisterFaceRuntime();
                    // The target's optional event idle path is conditional; LoadLive
                    // passes false, so it is not registered here.
                    normalRoots += roots.Count - before;
                    normalCharacters++;
                }
            }

            return roots;
        }
    }
}
