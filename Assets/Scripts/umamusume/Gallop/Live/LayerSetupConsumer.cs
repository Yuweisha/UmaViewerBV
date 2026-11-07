using System;

namespace Gallop.Live
{
    // StageController.ResetLayer 0x4eb1db0: two singleton calls with 0.
    // StageController.SetUpTempLayer 0x4eb24a0: two singleton calls with 10.
    public static class LayerSetupConsumer
    {
        public static void Reset(Action<object, int> callA, Action<object, int> callB)
        {
            callA?.Invoke(null, 0);
            callB?.Invoke(null, 0);
        }

        public static void SetUpTempLayer(Action<object, int> callA, Action<object, int> callB)
        {
            callA?.Invoke(null, 10);
            callB?.Invoke(null, 10);
        }
    }
}
