using System;

namespace Gallop.Live
{
    // StageController.PostUpdateAllTimeline 0x4eb1270:
    // if cyalume flag: 0x4f46e70(cy, this+0x70); 0x4f45f50(cy, 0)
    // if mob flag: 0x4f47ff0(mob, 0)
    public static class StagePostUpdate
    {
        public static void Apply(bool cyalumeFlag, bool mobFlag,
            object cyalumeController, object mobController, object someObject,
            Action<object, object> cyalumeUpdate, Action<object> cyalumePost, Action<object> mobPost)
        {
            if (cyalumeFlag)
            {
                if (cyalumeController == null) throw new NullReferenceException("cyalume controller");
                cyalumeUpdate?.Invoke(cyalumeController, someObject);
                cyalumePost?.Invoke(cyalumeController);
            }
            if (mobFlag)
            {
                if (mobController == null) throw new NullReferenceException("mob controller");
                mobPost?.Invoke(mobController);
            }
        }
    }
}
