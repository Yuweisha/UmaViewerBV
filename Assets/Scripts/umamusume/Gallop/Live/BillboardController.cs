using UnityEngine;

namespace Gallop.Live
{
    // Serialized component identity and fields match the stage/spotlight prefabs.
    public class BillboardController : MonoBehaviour
    {
        [SerializeField] private Transform _targetCameraTransform;
        [SerializeField] private int _rotationType;
        [SerializeField] private bool _isInversedForward;
        private Transform _transform;
        private bool _isEnabled;
        private bool _isInitialized;

        public Transform TargetCameraTransform { get => _targetCameraTransform; set => _targetCameraTransform = value; }
        public void Initialize(Transform cameraTransform)
        {
            _transform = transform;
            _targetCameraTransform = cameraTransform;
            _isEnabled = cameraTransform != null;
            _isInitialized = true;
        }

        // Installed native 0x4e62720; explicitly dispatched by the owner.
        public void AlterLateUpdate()
        {
            if (!_isInitialized || !_isEnabled || !enabled || _targetCameraTransform == null) return;
            Vector3 direction = _targetCameraTransform.position - _transform.position;
            if (_isInversedForward) direction = -direction;
            if (direction.sqrMagnitude < 1f) return;
            if (_rotationType == 2)
            {
                _transform.LookAt(_targetCameraTransform);
                if (_isInversedForward) _transform.Rotate(0f, 180f, 0f, Space.Self);
                return;
            }
            if (_rotationType != 0 && _rotationType != 1) return;
            Vector3 up = _transform.rotation * Vector3.up;
            direction -= up * Vector3.Dot(direction, up);
            if (direction.sqrMagnitude < 1e-10f) return;
            Quaternion rotation = Quaternion.LookRotation(direction, up);
            if (_rotationType == 1) rotation = Quaternion.Euler(0f, rotation.eulerAngles.y, 0f);
            if (Quaternion.Angle(rotation, _transform.rotation) > 0.1f) _transform.rotation = rotation;
        }
    }
}
