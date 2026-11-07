using System;

namespace Gallop.Live
{
    // StageController.InitializeStageObjectUnit 0x4eadae0:
    // for each non-null unit in this+0x50 call 0x4f0c970(unit, arg, 0).
    public static class StageObjectUnitInitializer
    {
        public static bool Apply<TUnit>(TUnit[] units, object arg, Action<TUnit, object> initialize)
            where TUnit : class
        {
            if (units == null) throw new NullReferenceException("stage object units");
            for (int i = 0; i < units.Length; i++)
            {
                var unit = units[i];
                if (unit == null) continue;
                initialize?.Invoke(unit, arg);
            }
            return true;
        }
    }
}
