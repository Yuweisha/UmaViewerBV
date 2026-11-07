using UnityEngine;

namespace Gallop.Live.Cutt
{
    // Original LiveModelController.AlterLateUpdate0x5757000..0x57572a3.
    // One cache belongs to each registered loaded model, not the whole cast.
    public sealed class LiveFormationPositionAddState
    {
        public Vector3 CachedPosition { get; private set; } = Vector3.zero;

        public void AlterLateUpdate(Transform root, Transform position, bool enabled)
        {
            if (!enabled)
            {
                CachedPosition = root.localPosition;
                return;
            }
            if (root.hasChanged)
            {
                CachedPosition = root.localPosition;
                root.localPosition = LiveFormationLocatorTransform.Add(root.localPosition,
                    LiveFormationLocatorTransform.Rotate(root.localRotation, position.localPosition));
                position.localPosition = Vector3.zero;
            }
            else
                root.localPosition = LiveFormationLocatorTransform.Add(CachedPosition, position.localPosition);
            root.hasChanged = false;
        }
    }
}
