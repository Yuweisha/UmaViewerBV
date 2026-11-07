using System;

namespace Gallop.Live
{
    public struct VolumeLightInfo
    {
        public int v0;      // +0x00 (vector3 x)
        public int color0;  // +0x0c (16 bytes)
        public int v1c;     // +0x1c
        public int v20;     // +0x20
        public int v24;     // +0x24
        public int v28;     // +0x28
        public int v2c;     // +0x2c
        public int v30;     // +0x30
        public byte b34, b35, b36;
    }

    public sealed class VolumeLightState
    {
        public int This370;
        // light fields
        public int X0, X30, X2c, X4c, X50, X60, X64, X68, X6c;
        public object RotationTarget;

        public void Apply(bool enabled, in VolumeLightInfo info, object light, object rotationTarget,
            Action<object> setPosition)
        {
            if (!enabled) return;
            This370 = info.v1c;
            X30 = info.color0;
            RotationTarget = rotationTarget;
            setPosition?.Invoke(rotationTarget);
            X4c = info.v20;
            X60 = info.v24;
            X2c = info.v28;
            X64 = info.v2c;
            X68 = info.v30;
            X50 = info.b35;
            X6c = info.b36;
            X0 = info.b34;
        }
    }
}
