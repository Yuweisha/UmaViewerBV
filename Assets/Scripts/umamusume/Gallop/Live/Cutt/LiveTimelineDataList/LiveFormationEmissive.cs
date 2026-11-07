using UnityEngine;

namespace Gallop.Live.Cutt
{
    // Original parent 0x4ed09e0/0x4ed16e5 and locator child 0x4ef79d0:
    // IsEmissiveColor is the only emissive gate (saved IsEmissiveIntensity /
    // IsEmissiveSoftEdge are not read); disabled state resets to
    // Color.black, scroll 0/0, intensity/rim 1/1/1 and center false.
    // Enabled values are unbounded single-precision interpolations.
    public static class LiveFormationEmissive
    {
        public static void Compute(LiveTimelineKeyFormationOffsetData cur,
            LiveTimelineKeyFormationOffsetData next, float ratio, out Color color,
            out float scrollTime, out float scrollEnergy, out float intensity,
            out float rimPower, out float rimIntensity, out bool isCenter)
        {
            if (!cur.IsEmissiveColor)
            {
                color = Color.black;
                scrollTime = 0f;
                scrollEnergy = 0f;
                intensity = 1f;
                rimPower = 1f;
                rimIntensity = 1f;
                isCenter = false;
                return;
            }
            color = cur.EmissiveColor;
            scrollTime = cur.EmissiveScrollTimeScale;
            scrollEnergy = cur.EmissiveScrollEnergyScale;
            intensity = cur.EmissiveIntensity;
            rimPower = cur.EmissiveRimPower;
            rimIntensity = cur.EmissiveRimIntensity;
            isCenter = cur.IsEmissiveCenter;
            if (next == null || !next.IsInterpolateKey()) return;
            color = Color.LerpUnclamped(color, next.EmissiveColor, ratio);
            scrollTime = Mathf.LerpUnclamped(scrollTime, next.EmissiveScrollTimeScale, ratio);
            scrollEnergy = Mathf.LerpUnclamped(scrollEnergy, next.EmissiveScrollEnergyScale, ratio);
            intensity = Mathf.LerpUnclamped(intensity, next.EmissiveIntensity, ratio);
            rimPower = Mathf.LerpUnclamped(rimPower, next.EmissiveRimPower, ratio);
            rimIntensity = Mathf.LerpUnclamped(rimIntensity, next.EmissiveRimIntensity, ratio);
        }
    }
}
