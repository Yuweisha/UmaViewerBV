using System;
using System.Collections.Generic;

namespace Gallop.Live
{
    // StageController.RemoveBlinkLightRootObjectArray 0x4eb14a0:
    // for each root object: name -> FNV hash -> dictionary lookup; a missing entry
    // is a native null-reference; on hit call the value and remove the key.
    public static class BlinkRootRemoval
    {
        public static void Remove(IReadOnlyList<int> hashes,
            Func<int, object> lookup, Action<object> onHit, Action<int> remove)
        {
            if (hashes == null) throw new NullReferenceException("hashes");
            for (int i = 0; i < hashes.Count; i++)
            {
                int hash = hashes[i];
                object value = lookup(hash);
                if (value == null) throw new NullReferenceException("blink root entry");
                onHit?.Invoke(value);
                remove?.Invoke(hash);
            }
        }
    }
}
