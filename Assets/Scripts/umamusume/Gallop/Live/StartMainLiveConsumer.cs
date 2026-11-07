using System;

namespace Gallop.Live
{
    // StageController.StartMainLive 0x4eb40c0:
    // SetActive(false) on every element of this+0x1f0 and this+0x1f8.
    public static class StartMainLiveConsumer
    {
        public static bool Apply<T>(T[] list1, T[] list2, Action<T, bool> setActive) where T : class
        {
            if (list1 == null || list2 == null) throw new NullReferenceException("start main live lists");
            for (int i = 0; i < list1.Length; i++)
            {
                if (list1[i] == null) throw new NullReferenceException("list1 element");
                setActive?.Invoke(list1[i], false);
            }
            for (int i = 0; i < list2.Length; i++)
            {
                if (list2[i] == null) throw new NullReferenceException("list2 element");
                setActive?.Invoke(list2[i], false);
            }
            return true;
        }
    }
}
