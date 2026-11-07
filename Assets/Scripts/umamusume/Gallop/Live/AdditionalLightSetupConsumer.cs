using System;

namespace Gallop.Live
{
    // StageController.SetupAdditionalLight 0x4eb2520:
    // OR the layer masks of ids {1,2,7,13,14,15,16}, then setup with that mask.
    public static class AdditionalLightSetupConsumer
    {
        public static int Setup(object controller, object transform, int[] layerIds,
            Func<int, int> layerToMask, Action<object, object, int> setup)
        {
            if (controller == null) throw new NullReferenceException("additional light controller");
            int mask = 0;
            for (int i = 0; i < layerIds.Length; i++) mask |= layerToMask(layerIds[i]);
            setup?.Invoke(controller, transform, mask);
            return mask;
        }
    }
}
