using System;

namespace Gallop.Live
{
    public struct TransmittedMaskUpdateInfo
    {
        public float[] values;
    }

    public sealed class TransmittedMaskControllerState
    {
        public float[] Buffer;

        // TransmittedLightMaskController.UpdateInfo 0x4f0d870: allocate once, then Array.Copy.
        public void Update(in TransmittedMaskUpdateInfo info)
        {
            var src = info.values;
            if (Buffer == null) Buffer = new float[src.Length];
            Array.Copy(src, Buffer, src.Length);
        }
    }
}
