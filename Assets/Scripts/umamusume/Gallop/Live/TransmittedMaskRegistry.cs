using System;
using System.Collections.Generic;

namespace Gallop.Live
{
    // StageController.FindAndAddTransmittedLightMaskController 0x4eacbc0:
    // if find(...) -> allocate controller, init, dictionary.Add(index, controller),
    // then holder callback; null holder throws after the add.
    public static class TransmittedMaskRegistry
    {
        public static object FindAndAdd(bool found, int index, object arg, object holder,
            Func<object> create, Action<object> init, Action<int, object> add, Action<object, int, object> holderCall)
        {
            if (!found) return null;
            var controller = create();
            init?.Invoke(controller);
            add?.Invoke(index, controller);
            if (holder == null) throw new NullReferenceException("transmitted mask holder");
            holderCall?.Invoke(holder, index, controller);
            return controller;
        }
    }
}
