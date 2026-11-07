using System;

namespace Gallop.Live
{
    // Dispatch reconstruction of StageController.UpdateLaser 0x4eb9330:
    // index info[0] into this+0xa8 array; negative/out-of-range/null -> no call.
    public static class LiveLaserConsumer
    {
        public static bool Dispatch<T>(T[] lasers, int index, object info, Action<T, object> update) where T : class
        {
            if (lasers == null || lasers.Length == 0) return false;
            if (index < 0 || index >= lasers.Length) return false;
            var laser = lasers[index];
            if (laser == null) return false;
            update?.Invoke(laser, info);
            return true;
        }
    }
}
