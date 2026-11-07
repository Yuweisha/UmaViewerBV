using System;

namespace Gallop.Live
{
    // StageController.SetActiveParticleController 0x4eb1e20:
    // for each element, get its gameObject and SetActive(enable).
    public static class ParticleActiveConsumer
    {
        public static void Apply<TElement>(bool active, TElement[] elements,
            Func<TElement, object> getGameObject, Action<object, bool> setActive) where TElement : class
        {
            if (elements == null) return;
            for (int i = 0; i < elements.Length; i++)
            {
                var e = elements[i];
                if (e == null) throw new NullReferenceException("particle element");
                var go = getGameObject(e);
                if (go == null) throw new NullReferenceException("particle gameObject");
                setActive?.Invoke(go, active);
            }
        }
    }
}
