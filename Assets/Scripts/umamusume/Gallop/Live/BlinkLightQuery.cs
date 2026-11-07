using System;
using System.Collections.Generic;

namespace Gallop.Live
{
    // StageController.TryGetBlinkLightContainerColorPower 0x4eb6840 and
    // GetBlinkLightContainerCount 0x4eacca0.
    public static class BlinkLightQuery
    {
        public static bool TryGetColorPower<TController>(Dictionary<int, TController> controllers,
            int key, int index, out float value, Func<TController, int, float> leaf) where TController : class
        {
            value = 0f;
            if (controllers == null) throw new NullReferenceException("blink light controllers");
            if (!controllers.TryGetValue(key, out var controller) || controller == null) return false;
            value = leaf(controller, index);
            return true;
        }

        public static int GetCount<TController>(Dictionary<int, TController> controllers, int key,
            Func<TController, int> count) where TController : class
        {
            if (controllers == null) throw new NullReferenceException("blink light controllers");
            if (!controllers.TryGetValue(key, out var controller) || controller == null) return 0;
            return count(controller);
        }
    }
}
