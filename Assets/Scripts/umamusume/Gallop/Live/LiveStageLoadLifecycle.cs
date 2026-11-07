using System;

namespace Gallop.Live
{
    public static class LiveStageLoadLifecycle
    {
        // 4e72945 -> 4e7294a in the original LoadStageResources coroutine.
        // The current Viewer stage initializer is an explicit adapter boundary,
        // not a claim that all native Stage.Initialize children are restored.
        public static bool Complete(StageController stage,Action<StageController> initialize,Action<bool> setEnabled)
        {
            if(stage==null)return false;
            initialize(stage);
            setEnabled(true);
            return true;
        }
    }
}
