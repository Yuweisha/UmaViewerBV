using System;

namespace Gallop.Live
{
    // StartPreLiveSkit 0x4eb4000: list1 false, list2 true.
    // StartAfterLiveSkit 0x4eb4180: list1 true, list2 false.
    public static class SkitStartConsumer
    {
        public static bool Apply<T>(T[] list1, T[] list2, bool list1Active, bool list2Active,
            Action<T, bool> setActive) where T : class
        {
            if (list1 == null || list2 == null) throw new NullReferenceException("skit lists");
            for (int i = 0; i < list1.Length; i++)
            {
                if (list1[i] == null) throw new NullReferenceException("list1 element");
                setActive?.Invoke(list1[i], list1Active);
            }
            for (int i = 0; i < list2.Length; i++)
            {
                if (list2[i] == null) throw new NullReferenceException("list2 element");
                setActive?.Invoke(list2[i], list2Active);
            }
            return true;
        }
    }
}
