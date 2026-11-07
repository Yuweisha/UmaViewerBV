using System;

namespace Gallop.Live
{
    // StageController.UpdateWaveParam 0x4ebf650:
    // for each non-null element in this+0x2a0 call 0x53e5190(element, arg).
    public static class WaveParamConsumer
    {
        public static bool Apply<TElement>(TElement[] elements, object arg, Action<TElement, object> update)
            where TElement : class
        {
            if (elements == null) throw new NullReferenceException("wave elements");
            for (int i = 0; i < elements.Length; i++)
            {
                var e = elements[i];
                if (e == null) throw new NullReferenceException("wave element");
                update?.Invoke(e, arg);
            }
            return true;
        }
    }
}
