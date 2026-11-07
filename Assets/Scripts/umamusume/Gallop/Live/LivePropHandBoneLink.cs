using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live
{
    internal sealed class LivePropHandBoneLink
    {
        // Original CharacterPropLinkBoneDefine .cctor 0x587a620; order is authored.
        private static readonly string[] Roots = { "ArmRoll", "Wrist", "Index_01", "Middle_01", "Pinky_01", "Ring_01", "Thumb_01" };
        private readonly List<KeyValuePair<Transform, Transform>> links = new List<KeyValuePair<Transform, Transform>>();

        public LivePropHandBoneLink(IDictionary<string, Transform> characterBones,
            IDictionary<string, Transform> propBones, bool left)
        {
            foreach (string name in Roots)
            {
                if (!characterBones.TryGetValue(name + (left ? "_L" : "_R"), out var root) || root == null) continue;
                foreach (var bone in root.GetComponentsInChildren<Transform>(true))
                    if (propBones.TryGetValue(bone.name, out var target) && target != null)
                        links.Add(new KeyValuePair<Transform, Transform>(bone, target));
            }
        }

        // Original LinkBoneTransformProcess.LinkProcess 0x51a0b90 copies local
        // character pose TO the prop reference skeleton. It never reparents hands.
        public void Apply()
        {
            foreach (var link in links) Copy(link.Key, link.Value);
        }

        internal static void Copy(Transform source, Transform target)
        {
            target.localPosition = source.localPosition;
            target.localRotation = source.localRotation;
            target.localScale = source.localScale;
        }
    }
}
