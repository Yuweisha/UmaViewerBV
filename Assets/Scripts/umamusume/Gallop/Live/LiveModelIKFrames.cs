using System;
using UnityEngine;

namespace Gallop.Live
{
    public sealed partial class LiveModelIKRuntime
    {

        static void WriteLocalPosition(Transform target, Vector3 position)
        {

            target.localPosition = position;
        }

        static void CopyWorldTransform(Transform source, Transform target)
        {
            Vector3 position = source.position;
            Quaternion rotation = source.rotation;

            target.SetPositionAndRotation(position, rotation);
        }

        // These are component callbacks. The production registry owns the
        // Enabled/pause gate; direct callback calls retain original behavior.
        public void PreLateUpdateIK1()
        {
            var u = Updater;
            if (u == null)
                return;
            if (u.Kind == 2)
            {
                float minimum = BitConverter.Int32BitsToSingle(unchecked((int)0x3f53b646));
                float my = ModeF(ModeF(ModeF(u.MyScale - minimum) / .325f) * .1625f);
                float other = ModeF(ModeF(ModeF(u.CenterScale - minimum) / .325f) * .1625f);
                float scale = ModeF(ModeF(other + u.MyScale) - my);
                Vector3 delta = u.MyPosition.localPosition - u.CenterPosition.localPosition;
                Vector3 center = u.CenterPosition.localPosition;
                WriteLocalPosition(u.MyPosition, new Vector3(ModeF(ModeF(delta.x * scale) + center.x), ModeF(ModeF(delta.y * scale) + center.y), ModeF(ModeF(delta.z * scale) + center.z)));
                if (u.LeftAttachEnabled)
                    CopyWorldTransform(u.LeftAttach, u.LeftIKTarget);
                if (u.RightAttachEnabled)
                    CopyWorldTransform(u.RightAttach, u.RightIKTarget);
            }
            else if (u.Kind == 5 && (u.LeftAttachEnabled || u.RightAttachEnabled))
            {
                Vector3 slide = Vector3.zero;
                GetSlidePos(ref slide);
                WriteLocalPosition(u.MyPosition, slide);
                if (u.LeftAttachEnabled)
                    CopyWorldTransform(u.LeftAttach, u.LeftIKTarget);
                if (u.RightAttachEnabled)
                    CopyWorldTransform(u.RightAttach, u.RightIKTarget);
            }
        }

        public void PreLateUpdateIK2()
        {
            var u = Updater;
            if (u == null)
                return;
            if (u.Kind == 1)
            {
                ModeCollision?.UpdatePosture();
                foreach (var limb in ModeLimbs)
                    limb.Solve(null);
            }
            else if (u.Kind == 4)
            {
                ModeCollision?.UpdatePosture();
                if (u.LeftAttachEnabled)
                {
                    CopyWorldTransform(u.LeftAttach, LeftTarget);
                    ModeLimbs[0].Solve(null);
                }

                if (u.RightAttachEnabled)
                {
                    CopyWorldTransform(u.RightAttach, RightTarget);
                    ModeLimbs[1].Solve(null);
                }
            }
            else if (u.Kind == 5)
            {
                u.HasOriginalPosition = false;
                u.HasSlidePosition = false;
                if (ModeLimbs != null)
                {
                    ModeCollision?.UpdatePosture();
                    foreach (var limb in ModeLimbs)
                        limb.Solve(null);
                }
            }
        }

        void MakeOriginalPosition()
        {
            var u = Updater;
            if (u.HasOriginalPosition)
                return;
            u.OriginalPosition = u.MyPosition.localPosition;
            u.HasOriginalPosition = true;
        }

        public void GetOriginalPos(ref Vector3 original)
        {
            if (Updater == null || Updater.Kind != 5)
                return;
            MakeOriginalPosition();
            original = Updater.OriginalPosition;
        }

        public void GetSlidePos(ref Vector3 slide)
        {
            var u = Updater;
            if (u == null || u.Kind != 5)
                return;
            if (u.CenterIK == null)
            {
                slide = u.MyPosition.localPosition;
                return;
            }

            u.CenterIK.GetSlidePos(ref slide);
            MakeOriginalPosition();
            if (!u.HasSlidePosition)
            {
                Vector3 center = Vector3.zero;
                u.CenterIK.GetOriginalPos(ref center);
                Vector3 delta = u.OriginalPosition - center;
                u.SlidePosition = new Vector3(ModeF(delta.x * u.SlideScale), ModeF(delta.y * u.SlideScale), ModeF(delta.z * u.SlideScale));
                u.HasSlidePosition = true;
            }

            slide = new Vector3(ModeF(u.SlidePosition.x + slide.x), ModeF(u.SlidePosition.y + slide.y), ModeF(u.SlidePosition.z + slide.z));
        }
    }
}
