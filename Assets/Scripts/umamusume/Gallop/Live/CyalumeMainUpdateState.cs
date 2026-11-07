using System;

namespace Gallop.Live
{
    // CyalumeController main update 0x4f46e70:
    // flags +0x79/+0x7a gate a property apply of literal 0x11e to the array;
    // optional holder (+0x88/+0x90) stores the array at +0x70.
    public sealed class CyalumeMainUpdateState
    {
        public bool Flag79;
        public bool Flag7A;
        public bool HolderFlag;   // +0x88
        public object Holder;     // +0x90
        public object Holder70;   // holder+0x70

        public bool Apply(object[] array, Action<int, object[]> applyProperty)
        {
            if (!Flag79 || !Flag7A || array == null || array.Length == 0) return false;
            applyProperty?.Invoke(0x11e, array);
            if (HolderFlag)
            {
                if (Holder == null) throw new NullReferenceException("cyalume holder");
                Holder70 = array;
            }
            return true;
        }
    }
}
