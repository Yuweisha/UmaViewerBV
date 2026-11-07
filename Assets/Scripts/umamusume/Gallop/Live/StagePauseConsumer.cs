using System;

namespace Gallop.Live
{
    // StageController.Pause 0x4eb11c0: pre/main/tail always, controller when present,
    // global status = 0 on pause and disabledValue on resume.
    public static class StagePauseConsumer
    {
        public static void Apply(bool enable, object controller, float disabledValue,
            Action<bool> pre, Action<bool> main, Action<bool> controllerStatus,
            Action<float> globalStatus, Action<bool> tail)
        {
            pre?.Invoke(enable);
            main?.Invoke(enable);
            if (controller != null) controllerStatus?.Invoke(enable);
            globalStatus?.Invoke(enable ? 0f : disabledValue);
            tail?.Invoke(enable);
        }
    }
}
