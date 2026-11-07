using System;

namespace Gallop.Live
{
    // StageController.SetupParticle 0x4eb3f60:
    // set this+0x28c = 1, then SetActive(true) on every element of this+0x200.
    public static class ParticleSetupConsumer
    {
        public static bool Apply<T>(T[] elements, Action<T> activate)
        {
            if (elements == null) throw new NullReferenceException("particle elements");
            for (int i = 0; i < elements.Length; i++)
            {
                if (elements[i] == null) throw new NullReferenceException("particle element");
                activate?.Invoke(elements[i]);
            }
            return true;
        }
    }
}
