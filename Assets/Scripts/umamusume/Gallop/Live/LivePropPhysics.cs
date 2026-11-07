using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Gallop.Live
{
    // The original Props.InitCySpring gate is AssetHolder.use_cyspring == 1.
    // The solver is the existing recovered CySpringController, with prop ownership.
    internal sealed class LivePropPhysics : IDisposable
    {
        private readonly CySpringController controller;
        private bool pending;
        private int lastStoppedFrame = -1;

        private sealed class Owner : CySpringOwner
        {
            private readonly int propId;
            private readonly float scale;
            public Owner(int propId, float scale) : base(null) { this.propId = propId; this.scale = scale; }
            public override bool IsCharaModel() => false;
            public override int GetCharaID() => propId;
            public override int GetDressID() => 0;
            public override int GetHeadID() => 0;
            public override float GetCySpringCorrectScale(bool isHead) => 1f;
            public override float GetBodyScale() => scale;
            public override float GetTotalScale() => scale;
        }

        public static string ClothPath(string prefabPath)
        {
            if (string.IsNullOrEmpty(prefabPath)) return null;
            int slash = prefabPath.LastIndexOf('/');
            if (slash < 0) return null;
            return prefabPath.Substring(0, slash) + "/clothes/" + prefabPath.Substring(slash + 1) + "_cloth00";
        }

        public static bool Requested(GameObject instance)
        {
            var holder = instance != null ? instance.GetComponent<AssetHolder>() : null;
            return holder != null && (int)holder.GetValue("use_cyspring") == 1;
        }

        internal static int SourcePropId(string prefabPath)
        {
            // Native Props._propId comes from the actual instantiated prop name,
            // not the character wearing it. Resource resolution already selected
            // gender/fallback family before this point.
            string name = Path.GetFileName(prefabPath);
            int marker = name.LastIndexOf("prop", StringComparison.Ordinal);
            if (marker < 0) return -1;
            int start = marker + 4;
            if (name.Length < start + 7 || name[start + 4] != '_') return -1;
            return int.TryParse(name.Substring(start, 4), NumberStyles.None, CultureInfo.InvariantCulture, out int major) &&
                int.TryParse(name.Substring(start + 5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int minor)
                ? unchecked(major * 100 + minor) : -1;
        }

        public LivePropPhysics(GameObject instance, string prefabPath,
            Dictionary<string, Transform> nodes)
        {
            if (!Requested(instance)) return;
            string path = ClothPath(prefabPath);
            var prefab = LiveFlashResourceUtility.LoadOnView<GameObject>(path);
            var data = prefab != null ? prefab.GetComponentInChildren<CySpringDataContainer>(true) : null;
            if (data == null)
                throw new InvalidOperationException("Authored prop requests CySpring but cloth data is missing: " + path);
            float scale = instance.transform.lossyScale.x;
            var owner = new Owner(SourcePropId(prefabPath), scale);
            controller = CySpringController.AddController(instance, null, owner);
            // Original InitializeCySpringInternal enables owner position/rotation.
            controller.IsAffectOwnerPotision = true;
            controller.IsAffectOwnerRotation = true;
            controller.LoadFromDataContainers(nodes, null, data, null, null,
                null, null, null);
            controller.Reset();
        }

        public void SetAnimationState(int keyFrame, bool animating, bool reserveWarming)
        {
            if (controller == null) return;
            if (animating) { controller.Pause(); return; }
            if (lastStoppedFrame != keyFrame)
            {
                if (reserveWarming) controller.ReserveWarmingUpCySpring();
                lastStoppedFrame = keyFrame;
            }
            controller.Resume();
        }

        public void Begin(float deltaTime, CySpringController characterController)
        {
            pending = false;
            if (controller == null || characterController == null) return;
            // Props.BeginCySpringSimulate 4ea19f0 copies only the owner previous
            // posture, not its wind arrays. Warm-up is processed even at delta0.
            controller.PreviousPos = characterController.PreviousPos;
            controller.PreviousRot = characterController.PreviousRot;
            controller.TryUpdateWarmUp();
            if (Gallop.Math.IsFloatEqualLight(deltaTime,0f)) return;
            controller.BeginSimulation(deltaTime, false);
            pending = true;
        }

        public void End()
        {
            if (!pending || controller == null) return;
            controller.EndSimulation();
            pending = false;
        }

        public void ReserveWarming(float seconds = CySpringController.DEFAULT_WARMUPTIME, int delayFrame = 0)
        {
            controller?.ReserveWarmingUpCySpring(seconds, delayFrame);
        }
        public void Dispose()
        {
            End();
            if (controller != null) UnityEngine.Object.Destroy(controller);
        }
    }
}
