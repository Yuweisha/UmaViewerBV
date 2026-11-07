using System;
using System.Collections.Generic;

namespace Gallop.Live
{
    // StageController.ContainsTransmittedLightMaskTimeline 0x4eac0e0:
    // worksheet -> manager(+0x290) -> sheet list(+0x88); each sheet dictionary
    // (+0x198) is enumerated and compared against the requested timeline index.
    public static class TransmittedTimelineQuery
    {
        public static bool Contains(IReadOnlyList<IReadOnlyCollection<int>> sheets, int target)
        {
            if (sheets == null) throw new NullReferenceException("worksheet");
            for (int i = 0; i < sheets.Count; i++)
            {
                var sheet = sheets[i];
                if (sheet == null) continue;
                foreach (int key in sheet)
                    if (key == target) return true;
            }
            return false;
        }
    }
}
