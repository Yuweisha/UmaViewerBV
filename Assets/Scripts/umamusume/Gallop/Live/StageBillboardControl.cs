using System.Collections.Generic;
using Gallop.Live.Cutt;
using UnityEngine;

namespace Gallop.Live
{
    public partial class StageController
    {
        private readonly Dictionary<int, BillboardController> _billboardControllerDictionary =
            new Dictionary<int, BillboardController>();

        // Installed CollectBillboardController 0x4eabd30, also in Initialize
        // 0x4eadb70. Discover the instantiated stage, never the source prefab.
        private void CollectBillboardController(Transform cameraTransform)
        {
            _billboardControllerDictionary.Clear();
            foreach (var billboard in GetComponentsInChildren<BillboardController>())
            {
                billboard.Initialize(cameraTransform);
                _billboardControllerDictionary.Add(FNVHash.Generate(billboard.name), billboard);
            }
        }

        // Original StageController.AlterLateUpdate 0x4eaa970 dispatches every
        // registered component after the timeline has set manual-angle flags.
        public void AlterLateUpdateBillboards()
        {
            foreach (var billboard in _billboardControllerDictionary.Values)
                if (billboard != null) billboard.AlterLateUpdate();
        }

        public void UpdateBillboard(ref BillboardUpdateInfo info)
        {
            // 0x4eb7c00: a StageObjectUnit takes precedence over a direct node.
            foreach (var unit in StageObjectUnitMap.Values)
                if (unit != null && unit.IsEnabled && unit.UnitNameHash == info.nodeHash)
                {
                    foreach (int hash in unit.ChildObjectNameHashArray)
                        if (_billboardControllerDictionary.TryGetValue(hash, out var child))
                            ApplyBillboard(child, ref info);
                    return;
                }
            if (_billboardControllerDictionary.TryGetValue(info.nodeHash, out var billboard))
                ApplyBillboard(billboard, ref info);
        }

        private static void ApplyBillboard(BillboardController billboard, ref BillboardUpdateInfo info)
        {
            // Installed overload 0x4eb7dc0: manual rotation is LOCAL; the
            // automatic controller must be disabled until its next automatic key.
            billboard.enabled = !info.enableManualAngle;
            if (info.enableManualAngle) billboard.transform.localRotation = info.manualRotation;
        }
    }
}
