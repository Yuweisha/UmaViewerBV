using UnityEngine;

namespace Gallop
{
    internal static class TimelineParticlePhysics
    {
        internal static readonly Vector3 NativeGravity = new Vector3(0f, -9.81f, 0f);

        internal static float GravityModifierScale(Vector3 projectGravity)
        {
            return projectGravity.x == 0f && projectGravity.z == 0f && projectGravity.y < 0f
                ? NativeGravity.y / projectGravity.y : 1f;
        }

        internal static void ScaleGravityModifier(ParticleSystem.MainModule main, float scale)
        {
            if (scale == 1f) return;
            var gravity = main.gravityModifier;
            switch (gravity.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    if (gravity.constant == 0f) return;
                    gravity.constant *= scale;
                    break;
                case ParticleSystemCurveMode.TwoConstants:
                    if (gravity.constantMin == 0f && gravity.constantMax == 0f) return;
                    gravity.constantMin *= scale;
                    gravity.constantMax *= scale;
                    break;
                case ParticleSystemCurveMode.Curve:
                case ParticleSystemCurveMode.TwoCurves:
                    if (gravity.curveMultiplier == 0f) return;
                    gravity.curveMultiplier *= scale;
                    break;
                default:
                    return;
            }
            main.gravityModifier = gravity;
        }
    }
}
