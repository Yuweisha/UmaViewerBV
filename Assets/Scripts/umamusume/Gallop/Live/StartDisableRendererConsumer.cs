using System;
using System.Collections.Generic;

namespace Gallop.Live
{
    // StageController.SetStartDisableRenderer 0x4eb2210:
    // disable every renderer in this+0x48 then clear the list.
    public static class StartDisableRendererConsumer
    {
        public static bool Apply<T>(List<T> renderers, Action<T, bool> setEnabled) where T : class
        {
            if (renderers == null) return false;
            for (int i = 0; i < renderers.Count; i++)
            {
                var r = renderers[i];
                if (r == null) throw new NullReferenceException("renderer entry");
                setEnabled?.Invoke(r, false);
            }
            renderers.Clear();
            return true;
        }
    }
}
