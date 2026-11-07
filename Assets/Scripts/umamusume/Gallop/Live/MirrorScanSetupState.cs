using System;

namespace Gallop.Live
{
    // StageController.SetupMirrorScanLight 0x4eb3bd0:
    // for each element in this+0x210: element[0x30] = this+0xd0 (material).
    public static class MirrorScanSetupState
    {
        public static bool Apply<T>(T[] elements, object material, Action<T, object> assign) where T : class
        {
            if (elements == null) throw new NullReferenceException("mirror scan elements");
            for (int i = 0; i < elements.Length; i++)
            {
                if (elements[i] == null) throw new NullReferenceException("mirror scan element");
                assign?.Invoke(elements[i], material);
            }
            return true;
        }
    }
}
