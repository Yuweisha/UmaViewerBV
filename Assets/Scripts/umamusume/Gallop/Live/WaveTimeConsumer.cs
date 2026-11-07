using System;

namespace Gallop.Live
{
    // StageController.UpdateWaveTime 0x4ebf700:
    // for each non-null element in this+0x2a0 call its virtual UpdateWaveTime(time).
    public static class WaveTimeConsumer
    {
        public static bool Apply<TElement>(TElement[] elements, float time, Action<TElement, float> update)
            where TElement : class
        {
            if (elements == null) throw new NullReferenceException("wave elements");
            for (int i = 0; i < elements.Length; i++)
            {
                var e = elements[i];
                if (e == null) throw new NullReferenceException("wave element");
                update?.Invoke(e, time);
            }
            return true;
        }
    }
}
