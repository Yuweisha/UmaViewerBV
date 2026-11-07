using System.Collections.Generic;
using Gallop.Live.Cutt;
using UnityEngine;

namespace Gallop.Live
{
    public sealed class FakeLightReceiver
    {
        public bool IsEnabled, IsOutline;
        public float Rate;
        public Color ReceiveColor;
        public int BlendMode;
        public readonly FakeLightSource[] Sources = new FakeLightSource[2];
        public readonly float[] Intensities = new float[2], Distances = new float[2];
        public int Count { get; private set; }
        public bool WasApplied;

        public void SetReceiveSetting(bool enabled, bool outline, float rate, Color receiveColor, int blendMode)
        {
            IsEnabled = enabled; IsOutline = outline; Rate = Mathf.Max(0f, rate);
            ReceiveColor = receiveColor; BlendMode = blendMode;
        }

        public void ClearValidLights()
        {
            Sources[0] = Sources[1] = null;
            Count = 0;
        }

        public static float DistanceToSegmentSquared(Vector3 point, Vector3 start, Vector3 end)
        {
            Vector3 delta = end - start;
            float denominator = delta.y * delta.y + delta.x * delta.x + delta.z * delta.z;
            float t = denominator < 0.000001f ? 0f : Mathf.Clamp01(
                ((point.y - start.y) * delta.y + (point.x - start.x) * delta.x +
                 (point.z - start.z) * delta.z) / denominator);
            Vector3 difference = start + delta * t - point;
            return difference.y * difference.y + difference.x * difference.x + difference.z * difference.z;
        }

        public void UpdateFakeLight(FakeLightSource source, Vector3 start, Vector3 end,
            float intensity, Vector3 characterPosition)
        {
            if (source == null) return;
            float distance = DistanceToSegmentSquared(characterPosition, start, end);
            int slot = Count;
            if (slot == 2)
            {
                slot = Intensities[1] > Intensities[0] ||
                    (Intensities[1] == Intensities[0] && Distances[0] > Distances[1]) ? 0 : 1;
                if (!(intensity > Intensities[slot] ||
                    intensity == Intensities[slot] && distance < Distances[slot])) return;
            }
            else Count++;
            Sources[slot] = source; Intensities[slot] = intensity; Distances[slot] = distance;
        }

        public static bool IntersectsCapsuleAABB(Vector3 start, Vector3 end,
            float startRadius, float endRadius, Bounds bounds)
        {
            bounds.Expand(Mathf.Max(startRadius, endRadius) * 2f);
            Vector3 minimum = bounds.min, maximum = bounds.max, delta = end - start;
            float low = 0f, high = 1f;
            for (int axis = 0; axis < 3; axis++)
            {
                float value = delta[axis];
                if (Mathf.Abs(value) < 0.000001f)
                {
                    if (start[axis] < minimum[axis] || start[axis] > maximum[axis]) return false;
                }
                else
                {
                    float a = (minimum[axis] - start[axis]) / value;
                    float b = (maximum[axis] - start[axis]) / value;
                    if (a > b) { float swap = a; a = b; b = swap; }
                    low = Mathf.Max(low, a); high = Mathf.Min(high, b);
                    if (low > high) return false;
                }
            }
            return true;
        }

        public static void SetMaterialLight(MaterialPropertyBlock block, int index,
            in FakeLightParam light, float rate, Color receiveColor, int blendMode)
        {
            string suffix = index == 0 ? "0" : "1";
            block.SetVector("_FakeLightStartPos" + suffix, light.IsActive
                ? new Vector4(light.StartPosition.x, light.StartPosition.y, light.StartPosition.z, 1f) : Vector4.zero);
            block.SetVector("_FakeLightEndPos" + suffix, light.IsActive
                ? new Vector4(light.EndPosition.x, light.EndPosition.y, light.EndPosition.z, 1f) : Vector4.zero);
            block.SetFloat("_FakeLightStartRadius" + suffix, light.StartRadius);
            block.SetFloat("_FakeLightEndRadius" + suffix, light.EndRadius);
            block.SetFloat("_FakeLightStartIntensity" + suffix, light.StartIntensity);
            block.SetFloat("_FakeLightEndIntensity" + suffix, light.EndIntensity);
            block.SetColor("_FakeLightColor" + suffix, light.LightColor);
            block.SetFloat("_FakeLightRate" + suffix, rate);
            block.SetColor("_FakeLightReceiveColor" + suffix, receiveColor);
            block.SetFloat("_FakeLightBlendMode" + suffix, blendMode);
        }

        public void Apply(IList<Renderer> renderers, MaterialPropertyBlock block)
        {
            if (!IsEnabled && !WasApplied) return;
            var values = new FakeLightParam[2];
            for (int i = 0; i < 2; i++)
            {
                if (IsEnabled && i < Count && Sources[i] != null)
                {
                    Sources[i].UpdateLightParamRaycast();
                    values[i] = Sources[i].GetRawFakeLightParam();
                }
                else values[i].LightColor = Color.white;
            }
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null) continue;
                renderer.GetPropertyBlock(block);
                for (int i = 0; i < 2; i++)
                    SetMaterialLight(block, i, in values[i], IsEnabled && i < Count ? Rate : 0f,
                        IsEnabled && i < Count ? ReceiveColor : Color.white,
                        IsEnabled && i < Count ? BlendMode : 0);
                block.SetFloat("_IsFakeLightOutline", IsEnabled && Count > 0 && IsOutline ? 1f : 0f);
                renderer.SetPropertyBlock(block);
            }
            WasApplied = IsEnabled;
        }
    }
}
