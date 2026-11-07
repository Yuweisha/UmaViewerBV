using UnityEngine;

namespace Gallop.Live
{
    // Field spelling/defaults are the shadow000 prefab TypeTree. The authored cross mesh
    // is already enabled; its optional CustomProjector is authored disabled.
    [DisallowMultipleComponent]
    public sealed class FakeShadowController : MonoBehaviour
    {
        [SerializeField] private float _maxAlpha = 1f;
        [SerializeField] private float _shadowPower = 0.75f;
        [SerializeField] private Color _color = Color.black;
        [SerializeField] private Transform _rootTransform;
        [SerializeField] private Transform _locationTransform;
        [SerializeField] private float _crossHeight = 2f;

        private static readonly int IntensityId = Shader.PropertyToID("_ShadowIntensity");
        private static readonly int ColorId = Shader.PropertyToID("_ShadowColor");
        private Renderer _renderer;
        private Material[] _materials;
        private LightProjection _projection;
        private bool _useProjection;
        private Vector3 _position;
        private Vector3 _offsetPos;
        private Vector3 _scale = Vector3.one;
        private float _y;

        public void Bind(Transform root, Transform location)
        {
            _rootTransform = root;
            _locationTransform = location;
            // Initialize 0x4e7f300: each foot owns its material state.
            _renderer = GetComponentInChildren<Renderer>();
            _projection = GetComponentInChildren<LightProjection>();
            if (_projection != null) _projection.Initialize();
            _materials = _renderer != null ? _renderer.materials : new Material[0];
            foreach (var material in _materials) material.SetFloat(IntensityId, 1f);
        }

        public void ApplyShadow(Color color, float power, bool projector)
        {
            // CharacterObject.UpdateShadowFactor 0x4e7b290. Alpha is also the
            // maximum opacity; power is independently multiplied after clamping.
            _maxAlpha = color.a;
            _shadowPower = power;
            _color = color;
            bool useProjection = projector && _projection != null;
            if (_useProjection == useProjection) return;
            // set_ShadowType 0x4e7fb00 transfers current visibility, not a forced enable.
            if (_renderer != null)
            {
                if (useProjection) _projection.Projector.enabled = _renderer.enabled;
                else _renderer.enabled = _projection.Projector.enabled;
            }
            if (_projection != null && !useProjection) _projection.Projector.enabled = false;
            if (_renderer != null && useProjection) _renderer.enabled = false;
            _useProjection = useProjection;
        }

        public void AlterLateUpdate(Vector3 groundPosition)
        {
            if (_rootTransform == null) return;
            // AlterLateUpdate 0x4e7ece0: ankle X/Z, PositionNode Y. The fade
            // reference includes _y but deliberately excludes the authored offset.
            Vector3 root = _rootTransform.position;
            _position.x = root.x;
            _position.z = root.z;
            float planeY = _y;
            if (_locationTransform != null)
            {
                _position.y = _locationTransform.position.y;
                planeY += _position.y;
            }
            _position += _offsetPos;
            transform.SetPositionAndRotation(_position, Quaternion.identity);
            transform.localScale = _scale;
            float alpha = _maxAlpha - Mathf.Clamp((root.y - planeY) * _crossHeight, 0f, _maxAlpha);
            int quantized = (int)(alpha * _shadowPower * 64f);
            alpha = quantized * (1f / 64f);
            if (_useProjection)
            {
                var material = _projection.Projector.Material;
                material.SetColor(ColorId, _color);
                material.SetFloat(IntensityId, alpha);
            }
            else if (_renderer != null && _renderer.enabled)
            {
                foreach (var material in _materials)
                {
                    material.SetColor(ColorId, _color);
                    material.SetFloat(IntensityId, alpha);
                }
                _renderer.sortingOrder = quantized + 100;
            }
        }

        private void OnDestroy()
        {
            if (_materials != null)
                foreach (var material in _materials) if (material != null) Destroy(material);
        }
    }
}
