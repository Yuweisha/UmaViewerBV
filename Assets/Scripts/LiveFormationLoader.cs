using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using Gallop.Live;

// Opt-in Viewer preset: use authored recommendation identities and dresses.
// Story-only songs with one authored cast (for example SHAKEROCK) supply it in story_live_position.
public static class LiveFormationLoader
{
    // Native LiveDefine: LIVE_CHARACTER_MAX=20, LIVE_THEATER_CHARACTER_MAX=18.
    // Story-only music_type99 (e.g.1095) can use more than eighteen slots.
    public const int CharacterCapacity = 20;
    public static int BaseCharacterCount(LiveEntry live) =>
        Math.Min(live.MusicType == 99 ? CharacterCapacity : 18, Math.Max(0, live.MemberCount));

    public static int[] SelectableCharacterSlots(LiveEntry live)
    {
        var fixedSlots = new HashSet<int>(UmaDatabaseController.Instance.ReadLiveFixMembers(live.MusicId)
            .Select(row => Int(row, "order") - 1));
        // LiveTheaterInfo.ctor 0x39fa974 subtracts fixed-member count before
        // allocating the selectable CharaDressIdArray.
        return Enumerable.Range(0, BaseCharacterCount(live)).Where(i => !fixedSlots.Contains(i)).ToArray();
    }

    public static List<LiveCharacterLoadData> PrepareBaseCharacters(LiveEntry live,
        IList<LiveCharacterLoadData> selection, bool fillBaseSlots = true)
    {
        var originals = selection.Where(c => c != null && !c.AuthoredExpansion).ToArray();
        int baseCount = BaseCharacterCount(live);
        int count = fillBaseSlots ? baseCount : Math.Min(baseCount, originals.Length);
        if (count > 0 && originals.Length == 0)
            throw new InvalidOperationException("Live base cast is empty: " + live.MusicId);
        var selectable = SelectableCharacterSlots(live);
        bool physicalSlotsSupplied = originals.Length >= baseCount;
        var characters = new List<LiveCharacterLoadData>(count);
        int selectedIndex = 0;
        for (int i = 0; i < count; i++)
        {
            bool userSlot = Array.IndexOf(selectable, i) >= 0;
            int sourceIndex = physicalSlotsSupplied ? i : selectedIndex;
            var source = originals[sourceIndex % originals.Length];
            characters.Add(new LiveCharacterLoadData {
                CharaEntry = source.CharaEntry, CostumeId = source.CostumeId,
                HeadCostumeId = source.HeadCostumeId, MasterDressId = source.MasterDressId,
                StartHidden = source.StartHidden
            });
            if (userSlot) selectedIndex++;
        }
        // Fixed members belong to the Live, independently of Recommended.
        var db = UmaDatabaseController.Instance;
        ApplyFixedMembers(characters, live.MusicId, db, UmaViewerMain.Instance, count);
        return characters;
    }

    // One cast is resolved before download registration and used unchanged by
    // model creation. CSV expansions never become selectable base characters.
    public static List<LiveCharacterLoadData> PrepareCharacters(LiveEntry live,
        IList<LiveCharacterLoadData> selection, bool fillBaseSlots = true,
        int storySetId = 0, bool recommended = false,
        IDictionary<int,LiveCharacterLoadData> fixedOverrides = null)
    {
        var originals = selection.Where(c => c != null && !c.AuthoredExpansion).ToList();
        if (storySetId > 0)
        {
            var db = UmaDatabaseController.Instance;
            LiveStoryAudienceInstaller.AppendAuthoredMobs(originals,
                db.ReadStoryLivePositions(live.MusicId, storySetId), storySetId,
                db.MobCharaData, db.DressData);
        }
        var characters = PrepareBaseCharacters(live, originals, fillBaseSlots);
        if (recommended && storySetId <= 0)
            ApplyRecommendedCostumes(characters, live.MusicId, characters.Count);
        if (fixedOverrides != null && fixedOverrides.Count > 0)
        {
            var fixedSlots = UmaDatabaseController.Instance.ReadLiveFixMembers(live.MusicId)
                .Select(row => Int(row, "order") - 1);
            foreach (int slot in fixedSlots)
                if (slot >= 0 && slot < characters.Count &&
                    fixedOverrides.TryGetValue(slot, out var replacement) && replacement?.CharaEntry != null)
                    characters[slot] = replacement;
        }
        AppendAuthoredCharacters(live, characters, CharacterCapacity);
        return characters;
    }

    public static bool CommandLineRecommendedCloth =>
        Environment.GetCommandLineArgs().Any(a => a == "-liveRecommendedCloth");

    public static List<DataRow> ReadRecommendedRows(int musicId, out int storySetId)
    {
        var db = UmaDatabaseController.Instance;
        var rows = db.ReadLiveRecommendFormation(musicId);
        storySetId = 0;
        if (rows.Count != 0) return rows;
        rows = db.ReadRecommendedStoryLivePositions(musicId);
        if (rows.Count != 0) storySetId = Int(rows[0], "set_id");
        return rows;
    }

    public static int ApplyRecommendedCostumes(List<LiveCharacterLoadData> characters, int musicId,
        int slotLimit = CharacterCapacity)
    {
        if (characters == null) return 0;
        var db = UmaDatabaseController.Instance;
        var rows = ReadRecommendedRows(musicId, out int storySetId);
        foreach (var row in rows)
        {
            int slot = Int(row, "position_id") - 1;
            if (slot < 0 || slot >= slotLimit) continue;
            int dressId = Int(row, "dress_id");
            var dress = db.DressData.FirstOrDefault(d => Int(d, "id") == dressId);
            if (dress == null) continue;
            while (characters.Count <= slot) characters.Add(new LiveCharacterLoadData());
            var selected = characters[slot] ?? (characters[slot] = new LiveCharacterLoadData());
            int charaId = Int(row, "chara_id");
            if (charaId > 0)
            {
                bool mob = Int(row, "chara_type") == 2;
                var pool = mob ? UmaViewerMain.Instance?.MobCharacters : UmaViewerMain.Instance?.Characters;
                selected.CharaEntry = pool?.FirstOrDefault(c => c.Id == charaId) ??
                    new CharaEntry { Id = charaId, IsMob = mob, Name = charaId.ToString(CultureInfo.InvariantCulture) };
            }
            selected.CostumeId = CostumeForDress(dress, selected.CharaEntry, db);
            selected.MasterDressId = dressId;
            selected.HeadCostumeId = Int(dress, "head_sub_id").ToString("D2", CultureInfo.InvariantCulture);
            selected.StartHidden = false;
        }
        if (storySetId == 0) ApplyFixedMembers(characters, musicId, db, UmaViewerMain.Instance, slotLimit);
        return storySetId;
    }

    // Original Live3DSettings.ApplyCharaExpansion 4e65ce0: the CSV's
    // type13 source indices refer to the unexpanded caller-supplied cast.
    // High/low polygon settings describe model quality, not existence.
    public static int AppendAuthoredCharacters(LiveEntry live,List<LiveCharacterLoadData> characters,
        int capacity,IList<DataRow> dresses=null)
    {
        var rows=live.LiveSettings.Where(row=>row.Length>2&&row[1].Trim()=="13").ToArray();
        int originalCount=characters.Count;
        if(rows.Length==0||originalCount+rows.Length>capacity)return 0;
        var originals=characters.ToArray();
        foreach(var row in rows)
        {
            if(!int.TryParse(row[2],NumberStyles.Integer,CultureInfo.InvariantCulture,out int source)||
                source<0||source>=originalCount)continue;
            var original=originals[source];
            var clone=new LiveCharacterLoadData{CharaEntry=original.CharaEntry,
                CostumeId=original.CostumeId,HeadCostumeId=original.HeadCostumeId,
                MasterDressId=original.MasterDressId,StartHidden=true,AuthoredExpansion=true};
            // Native tries the primary dress label, then its fallback. A label
            // can contain [CharaID], and ':0' chooses the default color variant.
            DataRow dress=null;
            for(int column=3;column<=4&&column<row.Length&&dress==null;column++)
            {
                string label=row[column].Trim().Replace("[CharaID]",original.CharaEntry.Id.ToString(CultureInfo.InvariantCulture));
                string token=label.Split(':')[0];
                if(!int.TryParse(token,NumberStyles.Integer,CultureInfo.InvariantCulture,out int id)||id<=0)continue;
                if(dresses==null)dresses=UmaDatabaseController.Instance.DressData;
                dress=dresses.FirstOrDefault(d=>Int(d,"id")==id&&
                    (Int(d,"chara_id")==0||Int(d,"chara_id")==original.CharaEntry.Id));
            }
            if(dress!=null)
            {
                clone.MasterDressId=Int(dress,"id");
                clone.CostumeId=CostumeForDress(dress,clone.CharaEntry,
                    Int(dress,"use_gender")!=0?UmaDatabaseController.Instance:null);
                // Expansion changes body dress only; the original head survives.
            }
            characters.Add(clone);
        }
        return characters.Count-originalCount;
    }

    static void ApplyFixedMembers(List<LiveCharacterLoadData> characters, int musicId,
        UmaDatabaseController db, UmaViewerMain main, int slotLimit)
    {
        foreach (DataRow row in db.ReadLiveFixMembers(musicId))
        {
            int charaId = Int(row, "chara_id");
            int dressId = Int(row, "dress_id1");
            int slot = Int(row, "order") - 1;
            if (charaId <= 0 || dressId <= 0 || slot < 0 || slot >= slotLimit) continue;

            var dress = db.DressData.FirstOrDefault(d => Int(d, "id") == dressId);
            if (dress == null) continue;
            bool isMob = !db.CharaData.Any(r => Int(r, "id") == charaId) &&
                         db.MobCharaData.Any(r => Int(r, "mob_id") == charaId);
            var character = main != null
                ? (isMob
                    ? main.MobCharacters.FirstOrDefault(c => c != null && c.Id == charaId)
                    : main.Characters.FirstOrDefault(c => c != null && c.Id == charaId))
                : null;
            if (character == null)
            {
                character = new CharaEntry
                {
                    Id = charaId,
                    IsMob = isMob,
                    Name = isMob ? "Mob " + charaId : charaId.ToString(CultureInfo.InvariantCulture)
                };
            }
            while (characters.Count <= slot) characters.Add(new LiveCharacterLoadData());
            characters[slot] = new LiveCharacterLoadData
            {
                CharaEntry = character,
                CostumeId = CostumeForDress(dress, character, db),
                MasterDressId = dressId,
                HeadCostumeId = Int(dress, "head_sub_id").ToString("D2", CultureInfo.InvariantCulture),
                StartHidden = false
            };
        }
    }

    static string CostumeForDress(DataRow dress, CharaEntry character, UmaDatabaseController db)
    {
        int costumeType = Int(dress, "costume_type");
        int dressChara = Int(dress, "chara_id");
        int bodyType = Int(dress, "body_type");
        int bodySub = Int(dress, "body_type_sub");
        if (costumeType == 1 || dressChara > 0) return bodySub.ToString("D2", CultureInfo.InvariantCulture);
        // Native GetBodyMeshPath -> DressData.GetBodyPartsID uses gender and
        // BodyShape. BodySetting controls dress behavior; it is not a prefab ID.
        int setting = 0;
        if (Int(dress, "use_gender") != 0 && character != null)
        {
            var data = character.IsMob
                ? db.MobCharaData.FirstOrDefault(r => Int(r, "mob_id") == character.Id)
                : db.CharaData.FirstOrDefault(r => Int(r, "id") == character.Id);
            if (data != null)
            {
                int gender = Int(data, "sex");
                if (gender == 1 || gender == 2) setting = gender;
            }
        }
        if (Int(dress, "body_shape") == 1) setting += 3;
        return bodyType.ToString("D4", CultureInfo.InvariantCulture) + "_" +
            bodySub.ToString("D2", CultureInfo.InvariantCulture) + "_" + setting.ToString("D2", CultureInfo.InvariantCulture);
    }

    static int Int(DataRow row, string name) =>
        row.Table.Columns.Contains(name) && row[name] != DBNull.Value
            ? Convert.ToInt32(row[name], CultureInfo.InvariantCulture) : 0;
}
