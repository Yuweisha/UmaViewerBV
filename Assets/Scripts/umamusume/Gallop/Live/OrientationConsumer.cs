using System;

namespace Gallop.Live
{
    // StageController.SetOrientation 0x4eb1fe0:
    // static gate + this+0x150 list; every element gets 0x3ee4140(element, 0).
    public static class OrientationConsumer
    {
        public static bool Apply<T>(bool enabled, T[] elements, Action<T> orient) where T : class
        {
            if (!enabled) return false;
            if (elements == null) throw new NullReferenceException("orientation elements");
            for (int i = 0; i < elements.Length; i++)
            {
                if (elements[i] == null) throw new NullReferenceException("orientation element");
                orient?.Invoke(elements[i]);
            }
            return true;
        }
    }
}
