using System;
using System.Collections.Generic;

namespace Gallop.Live
{
    // StageController.GetTransmittedLightMaskController 0x4ead1d0 and
    // IsCharaColorTimeline 0x4eaf3a0.
    public static class StageQuery
    {
        public static T GetTransmittedLightMaskController<T>(Dictionary<int, T> dict, int key) where T : class
        {
            if (dict == null) throw new NullReferenceException("transmitted mask dict");
            return dict.TryGetValue(key, out var value) ? value : null;
        }

        public static bool IsCharaColorTimeline(int value, int staticValue)
        {
            return value == staticValue;
        }
    }
}
