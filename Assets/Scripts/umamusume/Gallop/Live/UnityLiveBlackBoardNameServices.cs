using System;
using UnityEngine;

namespace Gallop.Live
{
    public sealed class UnityLiveBlackBoardNameServices :
        ILiveBlackBoardNameServices<GameObject, MeshRenderer, LiveBlackBoardTargets>
    {
        readonly Func<LiveBlackBoardTargets> construct;
        readonly Action<LiveBlackBoardTargets> initialize;
        readonly Action<LiveBlackBoardTargets, GameObject> setAtlas;
        // Require the original resource installer. No empty font/canvas fallback.
        public UnityLiveBlackBoardNameServices(Func<LiveBlackBoardTargets> construct,
            Action<LiveBlackBoardTargets> initialize, Action<LiveBlackBoardTargets, GameObject> setAtlas)
        {
            this.construct = construct ?? throw new ArgumentNullException(nameof(construct));
            this.initialize = initialize ?? throw new ArgumentNullException(nameof(initialize));
            this.setAtlas = setAtlas ?? throw new ArgumentNullException(nameof(setAtlas));
        }
        public bool ObjectEquals(GameObject a, GameObject b) => a == b;
        public LiveBlackBoardTargets Construct() => construct();
        public void Initialize(LiveBlackBoardTargets texture) => initialize(texture);
        public void SetAtlas(LiveBlackBoardTargets texture, GameObject atlas) => setAtlas(texture, atlas);
        public MeshRenderer GetComponentInChildren(GameObject instance) => instance.GetComponentInChildren<MeshRenderer>();
        public void SetTarget(LiveBlackBoardTargets texture, MeshRenderer target) => texture.SetTarget(target);
    }
}
