using System;

namespace Gallop.Live
{
    // StageController.UpdateAdditionalLightCount 0x4eb6ea0:
    // controller at this+0x2b0; null throws; otherwise call 0x4f02a70(controller, count, 0).
    public static class AdditionalLightCount
    {
        public static bool Update(object controller, int count, Action<object, int> apply)
        {
            if (controller == null) throw new NullReferenceException("additional light controller");
            apply?.Invoke(controller, count);
            return true;
        }
    }
}
