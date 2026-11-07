using UnityEngine;

namespace Gallop
{
    public sealed class EyeBlinkSettingData:ScriptableObject
    {
        public float ProbabilityOfOccurrence=100f;
        public float ForceEyeCloseTime=0.0666666f;
        public FaceEyeType[] IgnoreEyeBlinkType;
        public FaceEyeType[] SingleModeTargetEyeType;
        public FaceEyeType[] IgnoreFaceEyeTypeOnFaceChangeArray;
    }
}
