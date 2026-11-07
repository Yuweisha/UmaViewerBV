using System;

namespace Gallop.Live
{
    // StageController 0x4eb6f00: every element of this+0x228 gets element+0x74 = value.
    public static class VolumeLightParamState
    {
        public static bool Apply<T>(T[] elements, float value, Action<T, float> assign) where T : class
        {
            if (elements == null) throw new NullReferenceException("volume light elements");
            for (int i = 0; i < elements.Length; i++)
            {
                if (elements[i] == null) throw new NullReferenceException("volume light element");
                assign?.Invoke(elements[i], value);
            }
            return true;
        }
    }
}
