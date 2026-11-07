using System;
using System.Collections.Generic;

namespace Gallop.Live
{
    public sealed class ParticleUnit
    {
        public bool Active;
        public object[] Items;

        public ParticleUnit(bool active, object[] items)
        {
            Active = active;
            Items = items;
        }
    }

    // StageController.UpdateParticle 0x4ebd9a0: toggling only when the unit state
    // differs; enable -> on calls + Active=true, disable -> off calls + Active=false.
    public static class ParticleUpdateConsumer
    {
        public static int Apply(bool enable, List<ParticleUnit> units,
            Action<object> on, Action<object> off)
        {
            if (units == null) throw new NullReferenceException("particle units");
            int calls = 0;
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (u == null) throw new NullReferenceException("particle unit");
                if (enable)
                {
                    if (u.Active) continue;
                    for (int j = 0; j < u.Items.Length; j++) { on?.Invoke(u.Items[j]); calls++; }
                    u.Active = true;
                }
                else
                {
                    if (!u.Active) continue;
                    for (int j = 0; j < u.Items.Length; j++) { off?.Invoke(u.Items[j]); calls++; }
                    u.Active = false;
                }
            }
            return calls;
        }
    }
}
