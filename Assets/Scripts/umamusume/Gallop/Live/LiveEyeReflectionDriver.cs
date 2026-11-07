using UnityEngine;

namespace Gallop.Live
{
    // Per-character production hook for eye reflection. It owns an
    // EyeReflectionController child (camera + RenderTexture), keeps the camera at
    // the eye anchor each frame and exposes the original enable/power/mask API.
    public sealed class LiveEyeReflectionDriver : MonoBehaviour
    {
        public Transform EyeAnchor;
        public LiveEyeReflectionController Controller;
        public bool Enabled;
        public float Power = 1f;
        public int TextureSizeRate = 1;
        // Populated in Configure/EnsureController; a field initializer must not
        // call LayerMask.GetMask (Unity forbids it in MonoBehaviour constructors).
        public LiveEyeReflectionContext Context;
        public bool TimelinePose { get; private set; }
        public CacheCamera CachedCamera { get; private set; }

        public void ApplyTimelinePose(Vector3 position, bool hasLookAt, Vector3 lookAt, float roll, float scale)
        {
            TimelinePose = true;
            var cameraTransform = Controller.Camera.transform;
            cameraTransform.position = position;
            if (hasLookAt)
            {
                cameraTransform.LookAt(lookAt, Vector3.up);
                cameraTransform.Rotate(0f, 0f, roll, Space.Self);
                cameraTransform.localScale = Vector3.one * scale;
            }
        }

        public void Configure(bool enabled, float power, Texture mask, int sizeRate, Transform anchor = null)
        {
            if (anchor != null)
            {
                EyeAnchor = anchor;
            }
            Enabled = enabled;
            Power = Mathf.Max(0f, power);
            TextureSizeRate = Mathf.Max(1, sizeRate);
            EnsureController();
            var context = Context;
            context.Enabled = enabled;
            context.ReflectionPower = Power;
            context.MaskTexture = mask;
            Controller.UpdateFromContext(context);
            SyncCamera();
        }

        public void SetEnabled(bool enabled)
        {
            Enabled = enabled;
            if (Controller == null)
            {
                return;
            }
            var context = Context;
            context.Enabled = enabled;
            context.ReflectionPower = enabled ? Power : 0f;
            Controller.UpdateFromContext(context);
            SyncCamera();
        }

        void EnsureController()
        {
            if (Controller != null)
            {
                return;
            }
            if (EyeAnchor == null)
            {
                EyeAnchor = transform;
            }
            if (Context.Width <= 0 || Context.Height <= 0)
            {
                Context = LiveEyeReflectionContext.Default();
            }
            var controllerObject = new GameObject("EyeReflectionController");
            controllerObject.transform.SetParent(transform, false);
            Controller = controllerObject.AddComponent<LiveEyeReflectionController>();
            Controller.TextureSizeRate = TextureSizeRate;
            Controller.Initialize(Context, transform);
            CachedCamera = new CacheCamera(Controller.Camera);
        }

        void SyncCamera()
        {
            if (TimelinePose || Controller == null || Controller.Camera == null || EyeAnchor == null)
            {
                return;
            }
            var cameraTransform = Controller.Camera.transform;
            cameraTransform.position = EyeAnchor.position;
            cameraTransform.rotation = EyeAnchor.rotation;
        }

        void LateUpdate()
        {
            if (!Enabled || Controller == null)
            {
                return;
            }
            SyncCamera();
        }

        void OnDisable()
        {
            if (Controller != null)
            {
                Controller.enabled = false;
            }
        }

        void OnEnable()
        {
            if (Controller != null && Enabled)
            {
                Controller.enabled = true;
            }
        }
    }
}
