using System.Collections.Generic;
using UnityEngine;

namespace Gallop
{
    // Original Gallop identity, separate from the older global compatibility type.
    public class CySpringCollisionComponent : MonoBehaviour
    {
        [SerializeField] protected float _radius;
        [SerializeField] protected CySpringCollisionData.CollisionType _collisionType;
        [SerializeField] protected bool _isInner;
        protected Vector3 _offset, _offset2;
        protected bool _filterSpiringNode, _filterSpiringNodeExclusive;
        protected HashSet<CySpringBoneBase> _filterSpringHashSet;
        protected CySpringCollisionRuntimeData _runtimeData;

        public bool IsInitializeRuntimeData => _runtimeData != null;
        public CySpringCollisionRuntimeData RuntimeData => _runtimeData;
        public Transform CacheTransform => _runtimeData.TargetTransform;
        public float Distance => _runtimeData.Distance;
        public Vector3 Normal => _runtimeData.Normal;
        public float Radius { get => _radius; set { _runtimeData.SetComponentRadius(value); _radius = value; } }
        public Vector3 Offset { get => _offset; set { _runtimeData.SetComponentOffset(value); _offset = value; } }
        public Vector3 Offset2 { get => _offset2; set { _runtimeData.SetComponentOffset2(value); _offset2 = value; } }
        public CySpringCollisionData.CollisionType CollisionType { get => _collisionType; set { _runtimeData.CollisionType = value; _collisionType = value; } }
        public bool IsInner { get => _isInner; set { _runtimeData.IsInner = value; _isInner = value; } }
        public bool FilterSpiringNode { get => _filterSpiringNode; set { _runtimeData.FilterSpringNode = value; _filterSpiringNode = value; } }
        public bool FilterSpiringNodeExclusive { get => _filterSpiringNodeExclusive; set { _runtimeData.FilterSpringNodeExclusive = value; _filterSpiringNodeExclusive = value; } }
        public HashSet<CySpringBoneBase> FilterSpringHashSet { get => _filterSpringHashSet; set { _runtimeData.FilterSpringHashSet = value; _filterSpringHashSet = value; } }
        public virtual bool IsWorldCollision() => false;

        public void Initialize(CySpringCollisionRuntimeData runtimeData = null)
        {
            if (runtimeData != null)
            {
                _runtimeData = runtimeData;
                _offset = runtimeData.Offset; _offset2 = runtimeData.Offset2; _radius = runtimeData.Radius;
                _collisionType = runtimeData.CollisionType; _isInner = runtimeData.IsInner;
                _filterSpiringNode = runtimeData.FilterSpringNode; _filterSpringHashSet = runtimeData.FilterSpringHashSet;
                return;
            }
            _runtimeData = new CySpringCollisionRuntimeData
            {
                Radius = _radius, Offset = _offset, Offset2 = _offset2, CollisionType = _collisionType, IsInner = _isInner,
                FilterSpringNode = _filterSpiringNode, FilterSpringNodeExclusive = _filterSpiringNodeExclusive,
                FilterSpringHashSet = _filterSpringHashSet, IsWorld = IsWorldCollision()
            };
            _runtimeData.Initialize(transform);
        }
        public void ScaleParams(float legacyScale) => _runtimeData.ScaleParams(legacyScale);
        public void SetTransform(float distance, Vector3 normal) => _runtimeData.SetTransform(distance, normal);
    }
}
