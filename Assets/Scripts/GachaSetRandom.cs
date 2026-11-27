using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Random = UnityEngine.Random;

// Catalog lifetime matches the menu and the loaded master/asset index.
internal sealed class GachaSetRandom
{
    private readonly List<(LiveCharacterLoadData Actor, int Card, int Rarity)> draws = new();
    private readonly List<List<LiveCharacterLoadData>> wardrobes = new();
    private readonly Dictionary<int, List<LiveCharacterLoadData>> byCharacter = new();

    internal IReadOnlyList<LiveCharacterLoadData> Outfits(int character) =>
        byCharacter.TryGetValue(character, out var outfits) ? outfits : Array.Empty<LiveCharacterLoadData>();

    private readonly List<int> cutIns = new();

    internal GachaSetRandom(UmaViewerMain main, List<DataRow> cards)
    {
        var db = UmaDatabaseController.Instance;
        var drawable = db.ReadDrawableGachaCardIds();
        var characters = main.Characters.Where(c => !c.IsMob && c.Id > 1);
        var dressGrants = db.ReadGachaSetDressGrants();
        var dresses = db.DressData.ToLookup(d => Convert.ToInt32(d["chara_id"]));
        var byDress = new Dictionary<(int, int), LiveCharacterLoadData>();
        foreach (var character in characters)
        {
            var wardrobe = new List<LiveCharacterLoadData>();
            bool HasHead(string head) => main.AbList.ContainsKey(UmaDatabaseController.HeadPath +
                $"chr{character.Id}_{head}/pfb_chr{character.Id}_{head}");
            void Add(string costume, string head, int dressId)
            {
                if (!LiveAutoSelection.HasBody(main, character, costume)) return;
                if (!HasHead(head)) head = "00";
                if (!HasHead(head)) return;
                var actor = new LiveCharacterLoadData { CharaEntry = character, CostumeId = costume,
                    HeadCostumeId = head, MasterDressId = dressId };
                byDress[(character.Id, dressId)] = actor;
                wardrobe.Add(actor);
            }

            if (character.Id >= 2001)
                Add("00", "00", 0);
            else
            {
                foreach (var dress in dresses[character.Id].Concat(dresses[0]))
                {
                    int id = Convert.ToInt32(dress["id"]);
                    if (!dressGrants.Contains((0, id)) && !dressGrants.Contains((character.Id, id))) continue;
                    Add(LiveFormationLoader.CostumeForDress(dress, character, db),
                        Convert.ToInt32(dress["head_sub_id"]).ToString("D2"), id);
                }
            }
            if (wardrobe.Count > 0)
            {
                wardrobes.Add(wardrobe);
                byCharacter.Add(character.Id, wardrobe);
            }
        }
        foreach (var row in cards)
        {
            int card = Convert.ToInt32(row["id"]), rarity = Convert.ToInt32(row["default_rarity"]);
            bool unique = rarity == 3 && GachaCutInLoader.HasUniqueCut(card);
            if (unique) cutIns.Add(card);
            if (rarity < 1 || rarity > 3 || (rarity == 3 && !unique) || !drawable.Contains(card)) continue;
            if (byDress.TryGetValue((Convert.ToInt32(row["chara_id"]), Convert.ToInt32(row["race_dress_id"])), out var actor))
                draws.Add((actor, card, rarity));
        }
    }

    private static T Pick<T>(List<T> list) => list[Random.Range(0, list.Count)];
    private LiveCharacterLoadData PickActor() => Pick(Pick(wardrobes));

    internal bool TryRoll(bool replace, GachaCutInLoader.Selection[] slots, out LiveCharacterLoadData openingActor)
    {
        openingActor = null;
        if (replace ? wardrobes.Count == 0 || cutIns.Count == 0 : draws.Count == 0) return false;
        if (replace) openingActor = PickActor();
        for (int i = 0; i < slots.Length; i++)
        {
            LiveCharacterLoadData actor;
            int card, cut, rarity, gate;
            if (replace)
            {
                actor = PickActor();
                card = 0; // A freely cast actor need not own any gacha card.
                cut = Pick(cutIns);
                rarity = 1;
                gate = Random.Range(0, 3);
            }
            else
            {
                (actor, card, rarity) = Pick(draws);
                cut = rarity == 3 ? card : 0;
                gate = rarity - 1;
            }
            slots[i] = new GachaCutInLoader.Selection { Character = actor.CharaEntry, Card = card,
                Rarity = rarity, CutInCard = cut, InitialGateFinish = gate, DressId = actor.MasterDressId,
                Costume = actor.CostumeId, Head = actor.HeadCostumeId };
        }
        return true;
    }
}
