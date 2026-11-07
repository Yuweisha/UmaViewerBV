using System;

namespace Gallop.Live
{
    // Dispatch reconstruction of StageController.UpdateStageObjectColor1 0x4ebe820:
    // objectId -> group index via _stageObjectHashToID; each group hash is updated,
    // then the object id itself is updated (always).
    public static class LiveStageColorConsumer
    {
        public static bool Dispatch(int objectId, int[][] groupHashes, Func<int, int> lookupGroup,
            Action<int> updateByHash)
        {
            int index = lookupGroup(objectId);
            if (index >= 0 && groupHashes != null && index < groupHashes.Length)
            {
                var hashes = groupHashes[index];
                if (hashes != null)
                    for (int i = 0; i < hashes.Length; i++) updateByHash?.Invoke(hashes[i]);
            }
            updateByHash?.Invoke(objectId);
            return true;
        }
    }
}
