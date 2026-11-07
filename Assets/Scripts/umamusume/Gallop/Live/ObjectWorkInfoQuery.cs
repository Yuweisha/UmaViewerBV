using System;
using System.Collections.Generic;

namespace Gallop.Live
{
    // StageController.GetObjectWorkInfoTransform 0x4eacfe0:
    // count = work-info list length; if count >= 1 return first entry+0x10 transform.
    public static class ObjectWorkInfoQuery
    {
        public static object GetTransform<TWorkInfo>(Dictionary<int, List<TWorkInfo>> workInfos, int key,
            out int count) where TWorkInfo : class
        {
            count = 0;
            if (workInfos == null) throw new NullReferenceException("object work info dict");
            if (!workInfos.TryGetValue(key, out var list) || list == null) return null;
            count = list.Count;
            if (count < 1) return null;
            var first = list[0];
            if (first == null) throw new NullReferenceException("object work info entry");
            return first;
        }
    }
}
