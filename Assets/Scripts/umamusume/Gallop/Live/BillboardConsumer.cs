using System;

namespace Gallop.Live
{
    // StageController.UpdateBillboard 0x4eb7dc0:
    // info+4 flag on -> set active false and apply rotation (transform+0x30);
    // flag off -> set active true.
    public static class BillboardConsumer
    {
        public static void Apply(bool flag, object transform, object rotationTarget, object rotation,
            Action<object, bool> setActive, Action<object, object> setRotation)
        {
            if (transform == null) throw new NullReferenceException("billboard transform");
            if (flag)
            {
                setActive?.Invoke(transform, false);
                if (rotationTarget == null) throw new NullReferenceException("billboard rotation target");
                setRotation?.Invoke(rotationTarget, rotation);
            }
            else
            {
                setActive?.Invoke(transform, true);
            }
        }
    }
}
