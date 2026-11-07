using System;
using System.Collections.Generic;

namespace Gallop.Live.Cutt
{
    // Dispatch reconstruction of StageController.UpdateBlinkLight 0x4eb7e30:
    // look up _blinkLightControllerDictionary (this+0x1a8) by nameHash and call
    // BlinkLightController.Update(updateInfo, 0) on hit.
    public static class LiveBlinkLightConsumer
    {
        public static bool Dispatch<T>(Dictionary<int, T> controllers, int nameHash,
            in BlinkLightUpdateInfo info, Action<T, BlinkLightUpdateInfo> update) where T : class
        {
            if (controllers == null) return false;
            if (!controllers.TryGetValue(nameHash, out var controller)) return false;
            if (controller == null) return false;
            update?.Invoke(controller, info);
            return true;
        }
    }
}
