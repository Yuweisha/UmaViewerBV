using System;

namespace Gallop.Live
{
    public sealed class MobControllerState
    {
        public bool Enabled;      // native +0x3c
        public object[] Elements; // native +0x78
        public bool Dirty;        // native +0x80

        // MobController.Update 0x4f48220: disabled -> no-op; bounds; per-element update; dirty=1.
        public bool Apply(int index, Action<object> updateElement)
        {
            if (!Enabled) return false;
            if (Elements == null) throw new NullReferenceException("mob elements");
            if (index < 0 || index >= Elements.Length) throw new IndexOutOfRangeException("mob index");
            updateElement?.Invoke(Elements[index]);
            Dirty = true;
            return true;
        }
    }
}
