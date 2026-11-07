using UnityEngine;

namespace Gallop.Live.Cutt
{
    // Transform portion of original locator child0x4ef79d0. Logical parent is
    // an input coordinate frame, not a request to reparent the model root.
    public static class LiveFormationLocatorTransform
    {
        static float F(float value) => LiveTimelineControl.MonitorFloat32(value);

        // Preserve the four mul/add/sub chains in0x4ef81c7..0x4ef8291.
        // Round at the same boundaries as the original scalar instructions.
        static Quaternion ParentRotation(Quaternion a, Quaternion b) => new Quaternion(
            F(F(F(F(a.w*b.x)+F(a.x*b.w))+F(a.y*b.z))-F(a.z*b.y)),
            F(F(F(F(a.w*b.y)+F(a.y*b.w))+F(a.z*b.x))-F(a.x*b.z)),
            F(F(F(F(a.w*b.z)+F(a.z*b.w))+F(a.x*b.y))-F(a.y*b.x)),
            F(F(F(F(a.w*b.w)-F(a.x*b.x))-F(a.y*b.y))-F(a.z*b.z)));

        internal static Vector3 Add(Vector3 a, Vector3 b) => new Vector3(F(a.x+b.x),F(a.y+b.y),F(a.z+b.z));
        static Vector3 Subtract(Vector3 a, Vector3 b) => new Vector3(F(a.x-b.x),F(a.y-b.y),F(a.z-b.z));
        static Vector3 Point(Matrix4x4 m, Vector3 v) => new Vector3(
            F(F(F(F(m.m00*v.x)+F(m.m01*v.y))+F(m.m02*v.z))+m.m03),
            F(F(F(F(m.m10*v.x)+F(m.m11*v.y))+F(m.m12*v.z))+m.m13),
            F(F(F(F(m.m20*v.x)+F(m.m21*v.y))+F(m.m22*v.z))+m.m23));

        internal static Vector3 Rotate(Quaternion q, Vector3 v)
        {
            float x=F(q.x*2f),y=F(q.y*2f),z=F(q.z*2f);
            float xx=F(q.x*x),yy=F(q.y*y),zz=F(q.z*z),xy=F(q.x*y),xz=F(q.x*z),yz=F(q.y*z);
            float wx=F(q.w*x),wy=F(q.w*y),wz=F(q.w*z);
            return new Vector3(
                F(F(F(F(1f-F(yy+zz))*v.x)+F(F(xy-wz)*v.y))+F(F(xz+wy)*v.z)),
                F(F(F(F(xy+wz)*v.x)+F(F(1f-F(xx+zz))*v.y))+F(F(yz-wx)*v.z)),
                F(F(F(F(xz-wy)*v.x)+F(F(yz+wx)*v.y))+F(F(1f-F(xx+yy))*v.z)));
        }

        public struct Output
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public Quaternion PositionLocalRotation;
            public bool IsWorldRotation;
        }

        public static Output Evaluate(LiveFormationSample sample, Vector3 initialPosition,
            Vector3 heightOffset, bool hasParent, Matrix4x4 parentMatrix)
        {
            var position = Add(sample.Position, heightOffset);
            position = Add(position, initialPosition);
            var rotation = sample.Rotation;
            if (hasParent)
            {
                position = Point(parentMatrix, position);
                rotation = ParentRotation(parentMatrix.rotation, rotation);
            }
            if (sample.IsWorldSpace)
            {
                position = Add(Rotate(Quaternion.Euler(0f, sample.WorldRotationY, 0f),
                    Subtract(position, sample.WorldSpaceOrigin)), sample.WorldSpaceOrigin);
                if (sample.IsLookAtWorldOrigin)
                    rotation = Quaternion.LookRotation(Subtract(sample.WorldSpaceOrigin, position));
            }
            return new Output
            {
                Position = position, Rotation = rotation,
                PositionLocalRotation = sample.LocalRotation, IsWorldRotation = hasParent
            };
        }

        public static void Apply(ILiveTimelineCharactorLocator locator, Output output)
        {
            var root = locator.liveRootTransform;
            if (root == null) return;
            root.position = output.Position;
            if (output.IsWorldRotation) root.rotation = output.Rotation;
            else root.localRotation = output.Rotation;
            locator.liveCharaPositionLocalRotation = output.PositionLocalRotation;
        }
    }
}
