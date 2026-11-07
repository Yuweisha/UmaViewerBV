using System;

namespace Gallop.Live
{
    // Cyalume flush 0x4f45f50 and mob flush 0x4f47ff0:
    // when enabled && dirty: reset dirty, resolve shader property literal and apply
    // to the material array.
    public sealed class CyalumeMobFlushState
    {
        public bool Enabled;
        public bool Dirty;
        public object[] Materials;

        public bool Flush(int literalId, Action<int, object[]> applyProperty)
        {
            if (!Enabled || !Dirty) return false;
            Dirty = false;
            applyProperty?.Invoke(literalId, Materials);
            return true;
        }
    }
}
