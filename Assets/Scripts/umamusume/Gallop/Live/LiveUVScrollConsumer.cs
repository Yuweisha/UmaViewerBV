using System;
using System.Collections.Generic;

namespace Gallop.Live
{
    // Dispatch reconstruction of StageController.UpdateUVScrollLight 0x4ebf070:
    // key = info[0], look up _uvScrollLightDictionary (this+0x1b0), update on hit.
    public static class LiveUVScrollConsumer
    {
        public static bool Dispatch<TController, TInfo>(Dictionary<int, TController> controllers,
            int key, TInfo info, Action<TController, TInfo> update) where TController : class
        {
            if (controllers == null) throw new NullReferenceException("uv scroll controllers");
            if (!controllers.TryGetValue(key, out var controller)) return false;
            if (controller == null) return false;
            update?.Invoke(controller, info);
            return true;
        }
    }
}
