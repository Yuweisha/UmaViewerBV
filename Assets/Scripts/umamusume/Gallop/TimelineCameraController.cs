using UnityEngine;

namespace Gallop
{
    // Generic camera-side controller recovered from Gallop.TimelineCameraController.
    // Runtime wiring only; clip-plane writes and animator stepping are routed through
    // this component instead of bypassing the camera prefab owners.
    public class TimelineCameraController : MonoBehaviour
    {
        [SerializeField] private Camera _camera;
        [SerializeField] private Transform _cameraTransform;
        [SerializeField] private Transform _scaleOffsetTransform;
        [SerializeField] private Animator _animator;

        private AnimatorOverrideController _overrideController;

        public Camera Camera => _camera;
        public Transform CameraTransform => _cameraTransform;
        public Transform ScaleOffsetTransform => _scaleOffsetTransform;
        public Animator Animator => _animator;
        public bool IsInitialized => _camera != null;

        public void Init(Camera camera, Transform cameraTransform, Transform scaleOffsetTransform, Animator animator)
        {
            _camera = camera;
            _cameraTransform = cameraTransform != null ? cameraTransform : camera != null ? camera.transform : null;
            _scaleOffsetTransform = scaleOffsetTransform;
            _animator = animator;
            if (_animator != null && _animator.runtimeAnimatorController != null)
            {
                if (_overrideController != null && _animator.runtimeAnimatorController == _overrideController)
                    return;
                _overrideController = new AnimatorOverrideController(_animator.runtimeAnimatorController);
                _animator.runtimeAnimatorController = _overrideController;
            }
        }

        public void SetClipPlane(float near, float far)
        {
            if (_camera == null) return;
            _camera.nearClipPlane = near;
            _camera.farClipPlane = far;
        }

        public void ForceUpdate(float deltaTime = 0f)
        {
            if (_animator != null)
                _animator.Update(deltaTime);
        }

        public bool ApplyAutoOffset()
        {
            // The original owner is AutoCameraOffsetter. Aspect/safe-area offsets are
            // applied by that owner when present; keep the hook without inventing math.
            return false;
        }

        private void OnDestroy()
        {
            if (_overrideController != null)
            {
                Destroy(_overrideController);
                _overrideController = null;
            }
        }
    }
}
