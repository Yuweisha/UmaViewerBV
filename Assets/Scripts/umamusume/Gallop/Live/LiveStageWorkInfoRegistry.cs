using System.Collections.Generic;

namespace Gallop.Live
{
    // Reconstruction of <AddObjectWorkInfoDictionary>g__AddObjectsWorkInfoDictionary|176_0
    // 0x4eb6b10. Native quirk: a miss creates the list and inserts it into the
    // dictionary, but does NOT append the work info on that first call.
    public static class LiveStageWorkInfoRegistry
    {
        public static void AddObjectsWorkInfo(Dictionary<int, List<object>> dictionary, int id, object workInfo)
        {
            if (dictionary == null) throw new System.NullReferenceException("stage work info dictionary");
            if (!dictionary.TryGetValue(id, out var list))
            {
                list = new List<object>();
                dictionary.Add(id, list);
                return;
            }
            list.Add(workInfo);
        }
    }
}
