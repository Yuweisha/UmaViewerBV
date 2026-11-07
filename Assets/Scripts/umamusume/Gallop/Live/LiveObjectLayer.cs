using System;

namespace Gallop.Live
{
    // Dispatch reconstruction of StageController.UpdateObjectLayer 0x4ebd0d0.
    // layerType: 0 no-op, 1->2, 2->13, 3->7, 4->8; resolver returns a layer index.
    public static class LiveObjectLayer
    {
        public static int Apply(int layerType, object target, bool recursive,
            Func<int, int> resolveLayer, Func<object, object> getGameObject,
            Action<object, int> setLayer, Action<object, int> setLayerRecursively)
        {
            int arg;
            switch (layerType)
            {
                case 1: arg = 2; break;
                case 2: arg = 13; break;
                case 3: arg = 7; break;
                case 4: arg = 8; break;
                default: return -1;
            }
            int layer = resolveLayer(arg);
            if (layer < 0) return layer;
            if (target == null) throw new NullReferenceException("object target");
            object go = getGameObject(target);
            if (recursive) setLayerRecursively?.Invoke(go, layer);
            else setLayer?.Invoke(go, layer);
            return layer;
        }
    }
}
