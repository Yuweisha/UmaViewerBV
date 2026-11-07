using System;

namespace Gallop.Live
{
    // StageController.UpdateEnvironemntWaterReflection 0x4eb9290:
    // if info[0] != 0, call 0x4a8c470 on every element of this+0x350.
    public static class WaterReflectionConsumer
    {
        public static bool Apply<TElement, TInfo>(bool flag, TElement[] elements, TInfo info,
            Action<TElement, TInfo> update) where TElement : class
        {
            if (!flag) return false;
            if (elements == null) throw new NullReferenceException("water reflection elements");
            for (int i = 0; i < elements.Length; i++)
            {
                var e = elements[i];
                if (e == null) throw new NullReferenceException("water reflection element");
                update?.Invoke(e, info);
            }
            return true;
        }
    }
}
