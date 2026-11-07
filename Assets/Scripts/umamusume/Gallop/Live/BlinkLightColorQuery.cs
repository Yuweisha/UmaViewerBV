using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live
{
    // StageController.TryGetBlinkLightContainerColor 0x4eb68f0:
    // miss -> static default color, false; hit -> leaf color, true.
    public static class BlinkLightColorQuery
    {
        public static bool TryGet<TController>(Dictionary<int, TController> controllers, int key, int index,
            Color defaultValue, out Color color, Func<TController, int, Color> leaf) where TController : class
        {
            color = defaultValue;
            if (controllers == null) throw new NullReferenceException("blink light controllers");
            if (!controllers.TryGetValue(key, out var controller) || controller == null) return false;
            color = leaf(controller, index);
            return true;
        }
    }
}
