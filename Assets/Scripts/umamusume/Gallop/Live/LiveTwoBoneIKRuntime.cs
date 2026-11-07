using System;
using UnityEngine;
using Gallop.Live.Cutt;

namespace Gallop.Live
{
    // IK.TwoBoneIK Initialize/RecalcJointDistance/Solve. Engine Transform and
    // Quaternion FromToRotation/Lerp are the explicit engine boundaries.
    public sealed class LiveTwoBoneIKRuntime
    {
        public enum EffectorRotationType
        {
            None = 0,
            World = 1,
            Local = 2
        }

        public sealed class JointInfo
        {
            public Transform Transform;
            public float Distance;
        }

        public JointInfo[] Joints { get; private set; }

        public Vector3 TargetPosition;
        public Vector3 JointTargetPosition;
        public float BlendAlpha;
        public float RotationBlendValue = 1f;
        public Quaternion EndEffectorRotation;
        public EffectorRotationType RotationType;

        void WriteRotation(Transform target, string kind, Quaternion value)
        {

            if (kind == "rotation")
                target.rotation = value;
            else
                target.localRotation = value;
        }

        static float F(float x) => LiveTimelineControl.MonitorFloat32(x);
        static Vector3 Add(Vector3 a, Vector3 b) => new Vector3(F(a.x + b.x), F(a.y + b.y), F(a.z + b.z));
        static Vector3 Sub(Vector3 a, Vector3 b) => new Vector3(F(a.x - b.x), F(a.y - b.y), F(a.z - b.z));
        static Vector3 Mul(Vector3 a, float b) => new Vector3(F(a.x * b), F(a.y * b), F(a.z * b));
        static float Dot(Vector3 a, Vector3 b) => F(F(F(a.y * b.y) + F(a.x * b.x)) + F(a.z * b.z));
        static float Length(Vector3 v) => F((float)System.Math.Sqrt(Dot(v, v)));
        static Vector3 Normal(Vector3 v)
        {
            float length = Length(v);
            return length > 1e-5f ? new Vector3(F(v.x / length), F(v.y / length), F(v.z / length)) : Vector3.zero;
        }

        static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(F(F(a.y * b.z) - F(a.z * b.y)), F(F(a.z * b.x) - F(a.x * b.z)), F(F(a.x * b.y) - F(a.y * b.x)));
        internal static Quaternion Multiply(Quaternion a, Quaternion b) => new Quaternion(F(F(F(F(a.w * b.x) + F(a.x * b.w)) + F(a.y * b.z)) - F(a.z * b.y)), F(F(F(F(a.w * b.y) + F(a.y * b.w)) + F(a.z * b.x)) - F(a.x * b.z)), F(F(F(F(a.w * b.z) + F(a.z * b.w)) + F(a.x * b.y)) - F(a.y * b.x)), F(F(F(F(a.w * b.w) - F(a.x * b.x)) - F(a.y * b.y)) - F(a.z * b.z)));
        public void Initialize(Transform endEffector)
        {
            Joints = new[]
            {
                new JointInfo(),
                new JointInfo(),
                new JointInfo()
            };
            Joints[2].Transform = endEffector;
            Joints[1].Transform = endEffector.parent;
            Joints[0].Transform = Joints[1].Transform.parent;
            RecalcJointDistance();
        }

        public void RecalcJointDistance()
        {
            for (int i = 0; i < 2; i++)
                Joints[i].Distance = Length(Sub(Joints[i].Transform.position, Joints[i + 1].Transform.position));
        }

        static Vector3 BestAxis2(Vector3 direction)
        {
            // Original Helper receives the small CROSS product at this callsite,
            // not the target direction. Preserve that unusual input contract.
            Vector3 axis = Mathf.Abs(direction.y) > Mathf.Abs(direction.x) && Mathf.Abs(direction.y) > Mathf.Abs(direction.z) ? Vector3.right : Vector3.up;
            Vector3 first = Normal(Sub(axis, Mul(direction, Dot(direction, axis))));
            return Cross(first, direction);
        }

        public void Solve()
        {
            if (BlendAlpha <= 0)
                return;

            var root = Joints[0].Transform;
            var center = Joints[1].Transform;
            var end = Joints[2].Transform;
            Vector3 rootPosition = root.position, centerPosition = center.position, endPosition = end.position;
            float first = Joints[0].Distance, second = Joints[1].Distance;
            Vector3 direction = Sub(TargetPosition, rootPosition);
            float distance = Length(direction);
            if (distance < 1e-6f)
            {
                distance = 1e-6f;
                direction = Vector3.up;
            }
            else
                direction = new Vector3(F(direction.x / distance), F(direction.y / distance), F(direction.z / distance));
            Vector3 pole = Sub(JointTargetPosition, rootPosition), bend;
            if (Dot(pole, pole) < 1e-12f)
                bend = Vector3.up;
            else
            {
                Vector3 cross = Cross(direction, pole);
                bend = Dot(cross, cross) < 1e-12f ? BestAxis2(cross) : Normal(Sub(pole, Mul(direction, Dot(pole, direction))));
            }

            float total = F(second + first);
            Vector3 goal = TargetPosition, middle;
            if (distance > F(total - 1e-6f))
            {
                goal = Add(Mul(direction, total), rootPosition);
                middle = Add(Mul(direction, first), rootPosition);
            }
            else
            {
                float denominator = F(F(first + first) * distance);
                float cosine = denominator == 0 ? 0 : F(F(F(distance * distance) + F(first * first)) - F(second * second)) / denominator;
                cosine = F(cosine);
                if (cosine > 1f || cosine < -1f)
                {
                    if (first <= second)
                    {
                        middle = Sub(rootPosition, Mul(direction, first));
                        goal = Add(Mul(direction, second), middle);
                    }
                    else
                    {
                        middle = Add(Mul(direction, first), rootPosition);
                        goal = Sub(middle, Mul(direction, second));
                    }
                }
                else
                {
                    float angle = LiveIKNativeMath.Acos(cosine), sine = LiveIKNativeMath.SinAcosAngle(angle);

                    float radius = F(sine * first);
                    float square = F(F(first * first) - F(radius * radius));
                    float projection = square > 0 ? F((float)System.Math.Sqrt(square)) : 0;
                    if (cosine < 0)
                        projection = F(projection * -1f);
                    middle = Add(Add(Mul(direction, projection), rootPosition), Mul(bend, radius));
                }
            }

            Vector3 desired = Normal(Sub(middle, rootPosition)), current = Normal(Sub(centerPosition, rootPosition));
            if (Dot(desired, current) < .999999f)
            {
                Quaternion turn = Quaternion.Lerp(Quaternion.identity, Quaternion.FromToRotation(current, desired), BlendAlpha);
                WriteRotation(root, "rotation", Multiply(turn, root.rotation));
                centerPosition = center.position;
                endPosition = end.position;
            }

            desired = Normal(Sub(goal, centerPosition));
            current = Normal(Sub(endPosition, centerPosition));
            if (Dot(desired, current) < .999999f)
            {
                Quaternion turn = Quaternion.Lerp(Quaternion.identity, Quaternion.FromToRotation(current, desired), BlendAlpha);
                WriteRotation(center, "rotation", Multiply(turn, center.rotation));
            }

            float rotationAlpha = F(BlendAlpha * RotationBlendValue);
            switch (RotationType)
            {
                case EffectorRotationType.None:
                    break;
                case EffectorRotationType.World:
                    WriteRotation(end, "rotation", Quaternion.Lerp(end.rotation, EndEffectorRotation, rotationAlpha));
                    break;
                case EffectorRotationType.Local:
                    WriteRotation(end, "localRotation", Quaternion.Lerp(end.localRotation, EndEffectorRotation, rotationAlpha));
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

        }
    }
}
