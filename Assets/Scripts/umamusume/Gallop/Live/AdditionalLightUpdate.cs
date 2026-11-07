using System;

namespace Gallop.Live
{
    // StageController.UpdateAdditionalLight 0x4eb6ed0:
    // null controller throws; otherwise 0x4f02cf0(controller, arg, 0).
    public static class AdditionalLightUpdate
    {
        public static bool Apply(object controller, object arg, Action<object, object> apply)
        {
            if (controller == null) throw new NullReferenceException("additional light controller");
            apply?.Invoke(controller, arg);
            return true;
        }
    }
}
