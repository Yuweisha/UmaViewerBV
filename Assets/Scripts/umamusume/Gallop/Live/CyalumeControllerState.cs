using System;

namespace Gallop.Live
{
    public sealed class CyalumeControllerState
    {
        public object[] Elements;
        public bool Dirty;

        // CyalumeController.Update 0x4f46f20: element array this+0x60, stride 0x40;
        // bounds check then per-element update and dirty flag this+0x68=1.
        public void Apply(int index, Action<object> updateElement)
        {
            if (Elements == null) throw new NullReferenceException("cyalume element array");
            if (index < 0 || index >= Elements.Length) throw new IndexOutOfRangeException("cyalume index");
            updateElement?.Invoke(Elements[index]);
            Dirty = true;
        }
    }
}
