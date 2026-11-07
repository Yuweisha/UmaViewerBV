using UnityEngine;
using Gallop.RenderPipeline;

namespace Gallop.Live
{
    public struct FakeLightParam
    {
        public Vector3 StartPosition, EndPosition;
        public float StartRadius, EndRadius;
        public Color LightColor;
        public float StartIntensity, EndIntensity;
        public bool IsActive;
    }

    public struct FakeLightUpdateInfo
    {
        public bool IsEnabled;
        public float RadiusScale, IntensityScale;
    }

    // Local source geometry and occlusion, matching the 2.32.0 source boundary.
    public sealed class FakeLightSource : MonoBehaviour
    {
        const float MinimumRadius = 0.001f;
        Transform _startLocator, _endLocator;
        Renderer _radiusSourceRenderer;
        Mesh _radiusSourceMesh;
        Vector3[] _radiusSourceVerticesArray;
        WashLightController _washLightController;
        bool _isInitialized, _isEnabledTimelineOverride;
        float _overrideStartRadius, _overrideEndRadius, _overrideStartIntensity, _overrideEndIntensity;
        float _startIntensity = 0.5f, _endIntensity = 0.5f;
        int _raycastLayerMask = -1;
        FakeLightParam _rawFakeLightParam;
        Color _lightColor = Color.white;
        public Color LightColor { get => _lightColor; set => _lightColor = value; }

        public static bool TryGetLocators(Transform root, out Transform start, out Transform end)
        {
            start = end = null;
            if (root == null) return false;
            foreach (Transform child in root)
                if (child.name.Contains("start")) { start = child; break; }
            foreach (Transform child in root)
                if (child.name.Contains("end")) { end = child; break; }
            return start != null && end != null;
        }

        public void Initialize(Transform start, Transform end)
        {
            if (_isInitialized) return;
            _startLocator = start; _endLocator = end;
            _washLightController = GetComponent<WashLightController>();
            _radiusSourceRenderer = GetComponent<Renderer>();
            var filter = GetComponent<MeshFilter>();
            _radiusSourceMesh = _radiusSourceRenderer is MeshRenderer && filter != null ? filter.sharedMesh : null;
            if (_radiusSourceMesh != null && _washLightController == null)
                _radiusSourceVerticesArray = _radiusSourceMesh.vertices;
            _raycastLayerMask = 0;
            foreach (int index in new[] { 0, 1, 2, 7, 23, 13, 15, 16 })
                _raycastLayerMask |= GraphicSettings.GetCullingLayer((GraphicSettings.LayerIndex)index);
            _isInitialized = true;
        }

        // Original SetUpdateInfo multiplies stored overrides, retaining history.
        public void SetUpdateInfo(ref FakeLightUpdateInfo info)
        {
            _isEnabledTimelineOverride = info.IsEnabled;
            _overrideStartRadius *= info.RadiusScale;
            _overrideEndRadius *= info.RadiusScale;
            _overrideStartIntensity *= info.IntensityScale;
            _overrideEndIntensity *= info.IntensityScale;
        }

        public void CalculateRadii(out float startRadius, out float endRadius)
        {
            startRadius = endRadius = MinimumRadius;
            Vector3 start = _startLocator.position, axis = _endLocator.position - start;
            float length = axis.magnitude;
            if (_washLightController != null)
            {
                endRadius = Mathf.Max(MinimumRadius, Mathf.Max(_washLightController.ProjectionSide,
                    _washLightController.CameraProjectionSide) * length * 0.5f);
                return;
            }
            if (_radiusSourceVerticesArray == null) return;
            float a = 0f, b = 0f;
            float lengthSquared = length * length;
            if (lengthSquared <= 0f) lengthSquared = 1f;
            foreach (Vector3 vertex in _radiusSourceVerticesArray)
            {
                Vector3 relative = _radiusSourceRenderer.transform.TransformPoint(vertex) - start;
                if (length > 0f)
                {
                    float fraction = Vector3.Dot(axis, relative) / lengthSquared;
                    float radius = Vector3.Cross(axis, relative).magnitude / length;
                    if (fraction < 0.5f && radius > a) a = radius;
                    if (fraction >= 0.5f && radius > b) b = radius;
                }
                else a = b = Mathf.Max(a, relative.magnitude);
            }
            startRadius = Mathf.Max(MinimumRadius, a);
            endRadius = Mathf.Max(MinimumRadius, b);
        }

        public void UpdateRawFakeLightParam()
        {
            if (!_isInitialized || !enabled || !gameObject.activeInHierarchy ||
                _startLocator == null || _endLocator == null || _radiusSourceRenderer == null ||
                _radiusSourceMesh == null || !_radiusSourceRenderer.enabled)
            { _rawFakeLightParam.IsActive = false; return; }
            float startRadius, endRadius, startIntensity, endIntensity;
            if (_isEnabledTimelineOverride)
            {
                startRadius = Mathf.Max(MinimumRadius, _overrideStartRadius);
                endRadius = Mathf.Max(MinimumRadius, _overrideEndRadius);
                startIntensity = _overrideStartIntensity; endIntensity = _overrideEndIntensity;
            }
            else
            {
                CalculateRadii(out startRadius, out endRadius);
                startIntensity = _startIntensity; endIntensity = _endIntensity;
            }
            if (startIntensity <= 0.000001f && endIntensity <= 0.000001f)
            { _rawFakeLightParam.IsActive = false; return; }
            _rawFakeLightParam = new FakeLightParam {
                StartPosition = _startLocator.position, EndPosition = _endLocator.position,
                StartRadius = startRadius, EndRadius = endRadius,
                StartIntensity = startIntensity, EndIntensity = endIntensity,
                LightColor = LightColor, IsActive = true
            };
        }

        public void UpdateLightParamRaycast()
        {
            Vector3 delta = _rawFakeLightParam.EndPosition - _rawFakeLightParam.StartPosition;
            float length = delta.magnitude;
            if (length < 0.000001f) return;
            Vector3 direction = delta / length;
            float distance = -1f;
            if (_washLightController == null)
            {
                if (Physics.Raycast(new Ray(_rawFakeLightParam.StartPosition, direction), out var hit,
                    length, _raycastLayerMask)) distance = hit.distance;
            }
            else
            {
                if (_washLightController.IsRaycastHitChara && _washLightController.RaycastHitDistance < length)
                    distance = _washLightController.RaycastHitDistance;
                if (_washLightController.IsEnabledProjection && direction.y < -0.000001f)
                {
                    float floor = (_washLightController.ProjectionHeight - _rawFakeLightParam.StartPosition.y) / direction.y;
                    if (floor > 0f && floor < length && (distance < 0f || floor < distance)) distance = floor;
                }
            }
            if (distance < 0f) return;
            _rawFakeLightParam.EndPosition = _rawFakeLightParam.StartPosition + direction * distance;
            float t = Mathf.Clamp01(distance / length);
            _rawFakeLightParam.EndRadius = Mathf.Lerp(_rawFakeLightParam.StartRadius, _rawFakeLightParam.EndRadius, t);
            _rawFakeLightParam.EndIntensity = Mathf.Lerp(_rawFakeLightParam.StartIntensity, _rawFakeLightParam.EndIntensity, t);
        }

        public FakeLightParam GetRawFakeLightParam() => _rawFakeLightParam;
    }
}
