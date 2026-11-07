using System;
using System.Collections.Generic;

namespace Gallop.Live
{
    // Dispatch reconstruction of StageController.UpdateWashLight 0x4ebf480:
    // flagC != 0 -> enumerate this+0x1c8 and update each controller (then return);
    // flagC == 0 -> dictionary lookup by info[0] and update on hit.
    public static class LiveWashLightConsumer
    {
        public static bool Dispatch<TController, TInfo>(bool flagC, List<TController> collection,
            Dictionary<int, TController> controllers, int key, TInfo info,
            Action<TController, TInfo> update) where TController : class
        {
            if (flagC)
            {
                if (collection == null) throw new NullReferenceException("wash light collection");
                for (int i = 0; i < collection.Count; i++)
                {
                    var c = collection[i];
                    if (c == null) throw new NullReferenceException("wash light controller");
                    update?.Invoke(c, info);
                }
                return true;
            }
            if (controllers == null) throw new NullReferenceException("wash light controllers");
            if (!controllers.TryGetValue(key, out var controller)) return false;
            if (controller == null) return false;
            update?.Invoke(controller, info);
            return true;
        }
    }
}
