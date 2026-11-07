using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live
{
    // BlackBoardTexture.SetTarget (4e63b50), including material-copy-before-Add.
    // Initialize/CreateNameTexture's canvas, atlas, font and RenderTexture are
    // deliberately outside this target collector.
    public sealed class LiveBlackBoardTargets : ILiveBlackBoardTargetServices<MeshRenderer, Material>
    {
        public readonly Vector3 TextScale = new Vector3(.9f, .9f, 1f);
        internal LiveBlackBoardResources<object> Resources;
        public LiveBlackBoardResources<object> ResourceState => Resources;
        readonly List<LiveBlackBoardTargetControl<MeshRenderer, Material>.TargetMesh> targets;
        public List<LiveBlackBoardTargetControl<MeshRenderer, Material>.TargetMesh> Targets => Resources == null ? targets : (List<LiveBlackBoardTargetControl<MeshRenderer, Material>.TargetMesh>)Resources.Targets;
        public LiveBlackBoardTargets(List<LiveBlackBoardTargetControl<MeshRenderer, Material>.TargetMesh> targets) { this.targets = targets; }
        public void SetTarget(MeshRenderer target) => LiveBlackBoardTargetControl<MeshRenderer, Material>.SetTarget(target, Targets, this);
        public Material GetSharedMaterial(MeshRenderer target) => target.sharedMaterial;
        public Material CopyMaterial(Material material) => new Material(material);
        public void SetSharedMaterial(MeshRenderer target, Material material) => target.sharedMaterial = material;
    }
}
