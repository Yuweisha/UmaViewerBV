using System;

namespace Gallop.Live
{
    // StageController.UpdateAudience 0x4eb72b0:
    // index = info[1]; if in [0, controllers.Count): write default color into extra[index+2];
    // if controller non-null, update(controller, info) and write computed color.
    public static class AudienceConsumer
    {
        public static bool Apply(object[] controllers, object[] extraSlots, int index, object defaultColor,
            object info, Action<object, object> update, Func<object, object> compute)
        {
            if (controllers == null) throw new NullReferenceException("audience controllers");
            if (index < 0 || index >= controllers.Length) return false;
            extraSlots[index + 2] = defaultColor;
            var controller = controllers[index];
            if (controller == null) return true;
            update?.Invoke(controller, info);
            extraSlots[index + 2] = compute(controller);
            return true;
        }
    }
}
