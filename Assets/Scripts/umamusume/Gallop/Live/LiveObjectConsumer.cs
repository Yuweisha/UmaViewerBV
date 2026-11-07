using System;

namespace Gallop.Live
{
    public sealed class StageObjectUnitState
    {
        public bool Enabled;
        public int UnitHash;
        public int[] ObjectIds;
    }

    // Dispatch reconstruction of StageController.UpdateObject(info) 0x4ebd8b0:
    // find the enabled StageObjectUnit whose unit hash equals data.nameHash and
    // update each of its object ids; otherwise update the nameHash id directly.
    public static class LiveObjectConsumer
    {
        public static bool Dispatch<TInfo>(int nameHash, TInfo info,
            StageObjectUnitState[] units, Action<int, TInfo> updateObject)
        {
            if (units == null) throw new NullReferenceException("stage object units");
            for (int i = 0; i < units.Length; i++)
            {
                var u = units[i];
                if (u != null && u.Enabled && u.UnitHash == nameHash)
                {
                    var ids = u.ObjectIds;
                    if (ids != null)
                        for (int j = 0; j < ids.Length; j++) updateObject?.Invoke(ids[j], info);
                    return true;
                }
            }
            updateObject?.Invoke(nameHash, info);
            return true;
        }
    }
}
