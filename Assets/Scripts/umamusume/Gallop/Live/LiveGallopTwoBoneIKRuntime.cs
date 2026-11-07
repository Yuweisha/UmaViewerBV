using UnityEngine;
using Gallop.Live.Cutt;

namespace Gallop.Live
{
    // GallopTwoBoneIK 0x551cd50/0x551cea0. The caller owns registration,
    // update timing and collision posture; this wrapper does not schedule itself.
    public sealed class LiveGallopTwoBoneIKRuntime
    {
        public bool IsInitialized { get; private set; }
        public LiveTwoBoneIKRuntime Solver { get; private set; }
        public Transform JointPole { get; private set; }
        public Transform TargetTransform;
        public Transform ParentTransform { get; private set; }
        public bool IsTargetLocal { get; private set; }
        public bool IsTargetEndEffector;
        public bool IsUseAnimationBlendAlpha;
        public float BlendValue { get=>Solver.BlendAlpha;set=>Solver.BlendAlpha=value; }

        public void Initialize(Transform endEffector,Transform jointPole,Transform targetTransform)
        {
            IsInitialized=jointPole!=null;JointPole=jointPole;TargetTransform=targetTransform;
            Solver=new LiveTwoBoneIKRuntime();Solver.Initialize(endEffector);
            Solver.RotationType=LiveTwoBoneIKRuntime.EffectorRotationType.World;
            IsTargetLocal=false;ParentTransform=null;
        }

        public void SetTargetSpace(bool isLocal,Transform parent)
        {IsTargetLocal=isLocal;ParentTransform=parent;}
        public void RecalcJointDistance(){if(Solver!=null)Solver.RecalcJointDistance();}

        static Vector3 MultiplyPoint(Matrix4x4 matrix,Vector3 point)
        {
            // Original Matrix4x4.MultiplyPoint 0x5e43f00 rounds each SSE
            // product/add and then multiplies by the rounded reciprocal W.
            float F(float x)=>LiveTimelineControl.MonitorFloat32(x);
            float Row(int row)=>F(F(F(F(matrix[row,1]*point.y)+F(matrix[row,0]*point.x))+F(matrix[row,2]*point.z))+matrix[row,3]);
            float x=Row(0),y=Row(1),z=Row(2),inverse=F(1f/Row(3));
            return new Vector3(F(inverse*x),F(inverse*y),F(inverse*z));
        }

        public void Solve(LiveIKCollisionRuntime collision)
        {
            if(!IsInitialized||TargetTransform==null)return;
            if(IsUseAnimationBlendAlpha)Solver.BlendAlpha=LiveTimelineControl.MonitorFloat32(TargetTransform.localScale.x-1f);
            if(Solver.BlendAlpha<=0)return;
            if(!IsTargetLocal)
            {
                Solver.TargetPosition=IsTargetEndEffector?Solver.Joints[2].Transform.position:TargetTransform.position;
                Solver.JointTargetPosition=JointPole.position;
                Solver.EndEffectorRotation=TargetTransform.rotation;
            }
            else
            {
                Matrix4x4 matrix=ParentTransform.localToWorldMatrix;
                Solver.TargetPosition=IsTargetEndEffector?Solver.Joints[2].Transform.position:MultiplyPoint(matrix,TargetTransform.localPosition);
                Solver.JointTargetPosition=MultiplyPoint(matrix,JointPole.localPosition);
                Solver.EndEffectorRotation=LiveTwoBoneIKRuntime.Multiply(ParentTransform.rotation,TargetTransform.localRotation);
            }
            if(collision!=null)Solver.TargetPosition=collision.UpdateTargetPoint(Solver.TargetPosition);
            Solver.Solve();
        }
    }
}
