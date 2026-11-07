using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;

namespace Gallop.Live
{
    // Story cast mobs are authored model identities, independently of the stage's
    // AudienceController crowd and its lottery. Invoke before download registration.
    public static class LiveStoryAudienceInstaller
    {
        public static int AppendAuthoredMobs(List<LiveCharacterLoadData> selected,
            IEnumerable<DataRow> storyPositions, int setId,
            IList<DataRow> mobData, IList<DataRow> dressData)
        {
            if (selected == null) throw new ArgumentNullException(nameof(selected));
            if (storyPositions == null) return 0;
            var cast = storyPositions.Where(r => Int(r, "set_id") == setId)
                .OrderBy(r => Int(r, "position_id")).ToArray();
            if (cast.Length == 0 || selected.Count >= cast.Length) return 0;
            int principals = cast.Count(r => Int(r, "chara_type") != 2);
            if (selected.Count != principals)
                throw new InvalidOperationException("Story expansion needs the authored principal count: " + principals);
            var expanded = new List<LiveCharacterLoadData>(cast.Length);
            int principal = 0;
            int added = 0;
            foreach (var row in cast)
            {
                int slot = Int(row, "position_id") - 1;
                if (slot != expanded.Count || slot >= 20)
                    throw new InvalidOperationException("Story mob slot is not contiguous: " + (slot + 1));
                if (Int(row, "chara_type") != 2)
                {
                    expanded.Add(selected[principal++]);
                    continue;
                }
                int mobId = Int(row, "chara_id"), dressId = Int(row, "dress_id");
                var mob = mobData?.FirstOrDefault(r => Int(r, "mob_id") == mobId);
                var dress = dressData?.FirstOrDefault(r => Int(r, "id") == dressId);
                if (mob == null || dress == null)
                    throw new InvalidOperationException("Missing authored mob/dress: " + mobId + "/" + dressId);
                int setting = Int(dress, "use_gender") != 0 ? Int(mob, "sex") : Int(dress, "body_setting");
                string costume = Int(dress, "body_type").ToString("D4", CultureInfo.InvariantCulture) + "_" +
                    Int(dress, "body_type_sub").ToString("D2", CultureInfo.InvariantCulture) + "_" +
                    setting.ToString("D2", CultureInfo.InvariantCulture);
                expanded.Add(new LiveCharacterLoadData
                {
                    CharaEntry = new CharaEntry { Id = mobId, IsMob = true, Name = "Story mob " + mobId },
                    CostumeId = costume,
                    MasterDressId = dressId,
                    HeadCostumeId = "00"
                });
                added++;
            }
            selected.Clear();
            selected.AddRange(expanded);
            return added;
        }

        private static int Int(DataRow row, string name) => Convert.ToInt32(row[name], CultureInfo.InvariantCulture);
    }
}
