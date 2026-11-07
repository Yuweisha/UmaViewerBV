using System.Collections.Generic;
using UnityEngine;

namespace Gallop
{
    public partial class FaceDrivenKeyTarget
    {
        sealed class LiveEarVirtualParent
        {
            public Transform Root, Parent, Target;
        }

        readonly List<LiveEarVirtualParent> liveEarVirtualParents = new List<LiveEarVirtualParent>();

        void InitializeLiveEarVirtualParents()
        {
            liveEarVirtualParents.Clear();
            // ModelController.InitDrivenKey 0x540aad0 registers these four pairs.
            foreach (string side in new[] { "L", "R" })
            {
                for (int joint = 1; joint <= 2; joint++)
                {
                    Transform root = null, parent = null, target = null;
                    string rootName = "Sp_He_Ear0_" + side + "_0" + (joint - 1);
                    string parentName = "Sp_He_Ear0_" + side + "_0" + joint;
                    string targetName = "Ear_0" + (joint + 1) + "_" + side;
                    foreach (Transform node in Objs.Values)
                    {
                        if (node == null) continue;
                        if (node.name == rootName) root = node;
                        if (node.name == parentName) parent = node;
                        if (node.name == targetName) target = node;
                    }
                    if (root != null && parent != null && target != null)
                        liveEarVirtualParents.Add(new LiveEarVirtualParent { Root = root, Parent = parent, Target = target });
                }
            }
        }

        public void ApplyLiveEarVirtualParents()
        {
            if (Container == null || !Container.IsLive) return;
            foreach (var pair in liveEarVirtualParents)
            {
                if (!RotationRecorder.TryGetValue(pair.Target, out Vector3 euler)) continue;
                // DrivenKeyComponent.ApplyVirtualParentTransform 0x4c02610:
                // use the driven-key result, never the previous corrected Transform.
                pair.Target.localRotation = (Quaternion.Inverse(pair.Root.rotation) * pair.Parent.rotation)
                    * FacialRotation(euler);
            }
        }
    }
}
