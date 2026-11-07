using UnityEngine;

namespace Gallop.Live
{
    // Reconstructed MiniModelController.InitializeNodeCache 0x33cbd20:
    // six FindTransform lookups stored into the node fields in this exact order.
    public sealed class LiveMiniNodeCache
    {
        public Transform PositionNode;
        public Transform HipBone;
        public Transform WaistNode;
        public Transform ChestNode;
        public Transform NeckNode;
        public Transform HeadNode;

        public void Initialize(System.Func<string,Transform> findTransform)
        {
            PositionNode=findTransform("Position");
            HipBone=findTransform("Hip");
            WaistNode=findTransform("Waist");
            ChestNode=findTransform("Chest");
            NeckNode=findTransform("Neck");
            HeadNode=findTransform("Head");
        }
    }
}
