using System;

namespace Gallop.Live
{
    // Dispatch reconstruction of StageController.UpdateCyalumeControl 0x4eb8aa0 and
    // UpdateMobControl 0x4ebad30: flag gate, null-controller throw, then controller update.
    public static class LiveCyalumeMobConsumer
    {
        public static bool Dispatch<TController>(bool enabled, TController controller, int key,
            float f4, float f10, float f20, Action<TController, int, float, float, float> update)
            where TController : class
        {
            if (!enabled) return false;
            if (controller == null) throw new NullReferenceException("cyalume/mob controller");
            update?.Invoke(controller, key, f4, f10, f20);
            return true;
        }
    }
}
