using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;

namespace Gallop.Live
{
    // Normal lives author backdancers in live_data (backdancer_order /
    // backdancer_dress); they occupy the character slots after the principals.
    // The Viewer used to load only the principal cast, leaving those slots empty.
    public static class LiveBackdancerInstaller
    {
        public static int Append(LiveEntry live, List<LiveCharacterLoadData> selected,
            IList<DataRow> mobData, IList<DataRow> dressData)
        {
            if (live == null || selected == null || live.BackdancerOrder <= 0) return 0;
            var dress = dressData?.FirstOrDefault(r => Int(r, "id") == live.BackdancerDress);
            if (dress == null) return 0;
            bool useGender = Int(dress, "use_gender") != 0;
            int bodySetting = Int(dress, "body_setting");
            // Mob body prefab naming: bdy{type}_{sub}/pfb_bdy{type}_{sub}_{sub}_{h}_{s}_{b}.
            // The second body-type-sub token is part of the prefab name, not the
            // dress body setting (observed for backdancer dress 25 -> bdy0009_00).
            string bodyType = Int(dress, "body_type").ToString("D4", CultureInfo.InvariantCulture);
            string bodySub = Int(dress, "body_type_sub").ToString("D2", CultureInfo.InvariantCulture);
            // Mob body prefab's third token is the base body variant (00). It is
            // not body_type_sub twice; dress 32 (0011_01) only ships
            // pfb_bdy0011_01_00_*, not pfb_bdy0011_01_01_*.
            string costume = bodyType + "_" + bodySub + "_00";
            var pool = (mobData ?? new List<DataRow>())
                .Where(r => Int(r, "use_live") != 0)
                .Where(r => !useGender || Int(r, "sex") == bodySetting)
                .OrderBy(r => Int(r, "mob_id"))
                .ToList();
            if (pool.Count == 0) pool = (mobData ?? new List<DataRow>()).OrderBy(r => Int(r, "mob_id")).ToList();
            if (pool.Count == 0) return 0;
            for (int i = 0; i < live.BackdancerOrder; i++)
            {
                var mob = pool[(live.MusicId + i) % pool.Count];
                int mobId = Int(mob, "mob_id");
                selected.Add(new LiveCharacterLoadData
                {
                    CharaEntry = new CharaEntry { Id = mobId, IsMob = true, Name = "Backdancer " + mobId },
                    CostumeId = costume,
                    MasterDressId = live.BackdancerDress,
                    HeadCostumeId = "00",
                    StartHidden = true
                });
            }
            return live.BackdancerOrder;
        }

        private static int Int(DataRow row, string name) => Convert.ToInt32(row[name], CultureInfo.InvariantCulture);
    }
}
