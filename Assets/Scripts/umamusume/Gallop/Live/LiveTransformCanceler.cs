using System;
using Gallop.Live.Cutt;
using UnityEngine;

namespace Gallop.Live
{
    // LiveModelController's calcType2 path only. Race/event calcType0/1 are
    // separate native consumers and are not installed by this Live adapter.
    public sealed class LiveTransformCanceler
    {
        public bool Enabled;
        public Transform OffsetCheckTarget;
        public bool IsCancelX, IsCancelY, IsCancelZ;
        public float CancelRate = 1f;
        public Vector3 CancelFixPos;
        public bool IsScaleAveragePos, ForceResetCancelTargetPosition;
        public readonly Transform CancelTarget;
        public int PreviousOffsetCheckTarget { get; private set; }

        readonly Func<float> totalScale;

        public LiveTransformCanceler(Transform owner, Func<float> getTotalScale, Action refreshSpring = null)
        {
            CancelTarget = owner;
            totalScale = getTotalScale;
            refreshSpring?.Invoke();
        }

        static float F(float x) => LiveTimelineControl.MonitorFloat32(x);
        static Vector3 Add(Vector3 a, Vector3 b) => new Vector3(F(a.x + b.x), F(a.y + b.y), F(a.z + b.z));
        static Vector3 Sub(Vector3 a, Vector3 b) => new Vector3(F(a.x - b.x), F(a.y - b.y), F(a.z - b.z));
        static float Lerp(float a, float b, float t) => F(a + F(F(b - a) * Mathf.Clamp01(t)));
        void Write(Vector3 value)
        {
            CancelTarget.localPosition = value;

        }

        // 0x575eb30 caches the requested enum even if resolution returns null.
        public void UpdateParameters(int target, Func<int, Transform> resolve, bool x, bool y, bool z, float rate, Vector3 fix, bool scaleAverage)
        {
            if (PreviousOffsetCheckTarget != target)
            {
                OffsetCheckTarget = resolve(target);
                PreviousOffsetCheckTarget = target;
            }

            IsCancelX = x;
            IsCancelY = y;
            IsCancelZ = z;
            CancelRate = rate;
            CancelFixPos = fix;
            IsScaleAveragePos = scaleAverage;
        }

        public void ResetCancelParameter()
        {
            IsCancelX = IsCancelY = IsCancelZ = false;
            CancelRate = 1f;
            CancelFixPos = Vector3.zero;
        // Original reset retains target, cached enum, scale-average and force-reset.
        }

        public void PreUpdate()
        {
            // Live OnInitialize leaves resetLocalPosition false. Native PreUpdate
            // checks ForceReset and axes, without checking component Enabled.
            if (ForceResetCancelTargetPosition && (IsCancelX || IsCancelY || IsCancelZ))
                Write(Vector3.zero);
        }

        public void AfterLateUpdate() => ApplyPosition();
        public void ApplyPosition()
        {
            if (!Enabled || OffsetCheckTarget == null || CancelTarget == null || !(IsCancelX || IsCancelY || IsCancelZ))
                return;
            Vector3 check = OffsetCheckTarget.position;
            var parent = CancelTarget.parent;
            bool noParent = parent == null;
            Vector3 fix = noParent ? CancelFixPos : CancelTarget.parent.InverseTransformPoint(CancelFixPos);
            Vector3 desired = Add(CancelTarget.position, fix);
            if (IsScaleAveragePos && totalScale != null)
            {
                float scale = totalScale();
                var scaled = new Vector3(F(CancelFixPos.x * scale), F(CancelFixPos.y * scale), F(CancelFixPos.z * scale));
                fix = noParent ? scaled : CancelTarget.parent.InverseTransformPoint(scaled);
                desired = Add(CancelTarget.position, fix);
            }

            Vector3 adjusted = check;
            if (IsCancelX)
                adjusted.x = Lerp(check.x, desired.x, CancelRate);
            if (IsCancelY)
                adjusted.y = Lerp(check.y, desired.y, CancelRate);
            if (IsCancelZ)
                adjusted.z = Lerp(check.z, desired.z, CancelRate);
            Vector3 delta = Sub(adjusted, check);
            // This is a POINT conversion in 0x51c3b80, including parent
            // translation. A vector conversion changes original Live behavior.
            if (!noParent)
                delta = CancelTarget.parent.InverseTransformPoint(delta);
            Write(Add(CancelTarget.localPosition, delta));
        }
    }
}
