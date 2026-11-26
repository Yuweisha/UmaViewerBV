using UnityEngine;

namespace Gallop
{
    public sealed class FacialAdditiveMotionAsset : ScriptableObject
    {
        [SerializeField] private AnimationClip _animationClip;

        public AnimationClip AnimationClip => _animationClip;
    }
}
