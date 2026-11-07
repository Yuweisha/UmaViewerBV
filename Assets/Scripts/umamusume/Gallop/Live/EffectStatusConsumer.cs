using System;

namespace Gallop.Live
{
    // StageController.UpdateEffectStatus 0x4eb8af0:
    // controller (this+0x208) gets the enable flag; global status gets 0 when
    // enabled, otherwise the configured disabled value.
    public static class EffectStatusConsumer
    {
        public static void Apply(bool enabled, object controller, float disabledValue,
            Action<object, bool> controllerStatus, Action<float> globalStatus)
        {
            if (controller != null) controllerStatus?.Invoke(controller, enabled);
            globalStatus?.Invoke(enabled ? 0f : disabledValue);
        }
    }
}
