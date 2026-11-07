using System;

namespace Gallop.Live
{
    public struct FovShiftElement
    {
        public float factor;   // +0x20
        public float offset;   // +0x24
        public byte enabled;   // +0x28
    }

    // StageController.UpdateEnvironemntStageFovShift 0x4eb9200.
    public static class FovShiftConsumer
    {
        public static bool Apply(bool flag, float f4, float f8, byte b12, FovShiftElement[] elements)
        {
            if (!flag) return false;
            if (elements == null) throw new NullReferenceException("fov shift elements");
            for (int i = 0; i < elements.Length; i++)
            {
                elements[i].factor = f4;
                elements[i].offset = f8;
                elements[i].enabled = b12;
            }
            return true;
        }
    }
}
