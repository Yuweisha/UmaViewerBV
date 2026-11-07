using UnityEngine;

namespace Gallop.Live
{
    // EyeHighlightController.EyeHighlightPart 5191060/5190f90/51913d0.
    public sealed class LiveEyeHighlightPart
    {
        public Transform Transform;
        public Vector4[] FinalPoses;
        public int WriteIndex;
        public Vector3 AnimationPosition, AnimationScale;
        public Vector4 AnimatedPose;
        public bool NeedsInterpolation;

        public LiveEyeHighlightPart(Transform transform, Vector4[] finalPoses, int index)
        {
            Transform = transform; FinalPoses = finalPoses; WriteIndex = index;
            AnimationPosition = transform.localPosition; AnimationScale = transform.localScale;
        }

        public static Vector4 MakeParameter(Transform transform)
        {
            var p = transform.localPosition;
            return new Vector4(p.x * 100f, -p.y * 100f, p.z * 1.7453293f, transform.localScale.x);
        }

        static float Blend(float previous, float current, float ratio)
        {
            // IL2CPP emits separate subss/mulss/addss. Keep those rounding points
            // when Mono otherwise evaluates the Vector3.Lerp expression at wider precision.
            float delta = (float)((double)current - previous);
            float step = (float)((double)delta * ratio);
            return (float)((double)previous + step);
        }

        static Vector3 Blend(Vector3 previous, Vector3 current, float ratio)
            => new Vector3(Blend(previous.x, current.x, ratio), Blend(previous.y, current.y, ratio), Blend(previous.z, current.z, ratio));

        public void BeforeApplyDrivenKey(bool interpolate, float ratio)
        {
            NeedsInterpolation = interpolate;
            if (interpolate)
            {
                ratio = Mathf.Clamp01(ratio);
                Transform.localPosition = Blend(AnimationPosition, Transform.localPosition, ratio);
                Transform.localScale = Blend(AnimationScale, Transform.localScale, ratio);
            }
            else
            {
                AnimationPosition = Transform.localPosition;
                AnimationScale = Transform.localScale;
            }
            AnimatedPose = MakeParameter(Transform);
        }

        public void AfterApplyDrivenKey()
        {
            if (!NeedsInterpolation && Transform.localScale.x > AnimationScale.x)
            {
                AnimationScale = Transform.localScale;
                AnimatedPose = MakeParameter(Transform);
            }
            var value = MakeParameter(Transform);
            value.z = AnimatedPose.z; value.w = AnimatedPose.w;
            FinalPoses[WriteIndex] = value;
        }
    }
}
