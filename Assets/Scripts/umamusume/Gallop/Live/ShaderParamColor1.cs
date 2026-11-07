using System;

namespace Gallop.Live
{
    // StageController.UpdateShaderParamColor1 0x4ebe190:
    // if info[0] != static mode -> false; else color = vec4(info+0x18) * info+0x28,
    // stored at target+0x38; if the target is alive, apply(target, color); returns true.
    public static class ShaderParamColor1
    {
        public struct Vec4
        {
            public float X, Y, Z, W;
        }

        public static bool Apply(int mode, int expectedMode, Vec4 color, float scale,
            bool targetAlive, Action<Vec4> apply, out Vec4 stored)
        {
            stored = default;
            if (mode != expectedMode) return false;
            stored = new Vec4 { X = color.X * scale, Y = color.Y * scale, Z = color.Z * scale, W = color.W * scale };
            if (targetAlive) apply?.Invoke(stored);
            return true;
        }
    }
}
