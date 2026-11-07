using System;
using System.Collections.Generic;

namespace Gallop.Live
{
    // Dispatch reconstruction of StageController.UpdateTransmittedLightMask 0x4ebefe0:
    // key is the explicit third argument; dictionary at this+0x1d0.
    public static class LiveTransmittedMaskConsumer
    {
        public static bool Dispatch<TController, TInfo>(Dictionary<int, TController> controllers,
            int key, TInfo info, Action<TController, TInfo> update) where TController : class
        {
            if (controllers == null) throw new NullReferenceException("transmitted light mask controllers");
            if (!controllers.TryGetValue(key, out var controller)) return false;
            if (controller == null) return false;
            update?.Invoke(controller, info);
            return true;
        }
    }
}
