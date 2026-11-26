using UnityEngine;

namespace Gallop
{
    public sealed class DrivenKeyAnimation : ScriptableObject
    {
        public AnimationClip clip;
        public FaceTypeSet[] animationFaceType;
        public EarTypeSet[] animationEarType;
        [SerializeField] bool _isCheckEyeRange;
        public bool IsCheckEyeRange=>_isCheckEyeRange;
    }
}
