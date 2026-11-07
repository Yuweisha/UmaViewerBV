using System;
using IK;
using UnityEngine;
using Gallop.Live.Cutt;

namespace Gallop.Live
{
    // Original IKCollision Create/Combine/SetupRuntimeParameter and posture/
    // sphere-target consumers. Model registry and the limb solver are separate:
    // this class must not implicitly install an EventTimelineLimbIK in Live.
    public sealed class LiveIKCollisionRuntime
    {
        public struct CollisionInfo
        {
            public Transform ParentTransform;
            public IKCollisionData.RuntimeParameter Parameter;
            public Vector3 WorldPosition;
        }

        public bool IsInitialized { get; private set; }
        public CollisionInfo[] Collisions { get; private set; }
        public readonly float OwnerScale;
        public readonly float BodyScaleRatio;
        public readonly IKCollisionData.Parameter.Scene SceneType;
        static float F(float value)=>LiveTimelineControl.MonitorFloat32(value);

        LiveIKCollisionRuntime(float ownerScale,float bodyScaleRatio,IKCollisionData.Parameter.Scene scene)
        {OwnerScale=ownerScale;BodyScaleRatio=bodyScaleRatio;SceneType=scene;}

        public static LiveIKCollisionRuntime Create(IKCollisionData body,IKCollisionData head,
            float totalScale,float bodyScale,IKCollisionData.Parameter.Scene scene,Func<string,Transform> findTransform)
        {
            var collision=new LiveIKCollisionRuntime(totalScale,F(totalScale/bodyScale),scene);
            if(body!=null)collision.Combine(body,findTransform);
            if(head!=null)collision.Combine(head,findTransform);
            // Original distinguishes no assets (null array, valid object) from
            // assets with no matching entries (allocated empty array, null).
            return collision.Collisions!=null&&collision.Collisions.Length==0?null:collision;
        }

        public void Combine(IKCollisionData data,Func<string,Transform> findTransform)
        {
            var parameters=data.ParameterArray;
            int prior=Collisions?.Length??0,count=prior;
            foreach(var parameter in parameters)if((parameter.SceneType&SceneType)!=0)count++;
            var combined=new CollisionInfo[count];
            if(Collisions!=null)Array.Copy(Collisions,combined,prior);
            Collisions=combined;
            foreach(var source in parameters)
            {
                if((source.SceneType&SceneType)==0)continue;
                var info=new CollisionInfo{ParentTransform=findTransform(source.LinkBoneName)};
                IKCollisionData.RuntimeParameter.InitializeFromParameter(ref info.Parameter,source,data.IsHeightScale);
                float scale=data.IsHeightScale?OwnerScale:BodyScaleRatio;
                info.Parameter.Param1=F(scale*source.Param1);
                info.Parameter.Offset=new Vector3(F(scale*source.Offset.x),F(scale*source.Offset.y),F(scale*source.Offset.z));
                Collisions[prior++]=info;
            }
            IsInitialized=true;
        }

        public void UpdatePosture()
        {
            if(!IsInitialized)return;
            for(int i=0;i<Collisions.Length;i++)
            {
                var info=Collisions[i];
                Vector3 position=info.ParentTransform.position;
                Vector3 offset=Rotate(info.ParentTransform.rotation,info.Parameter.Offset);
                // Original adds rotated offset, without TransformPoint's scale.
                info.WorldPosition=new Vector3(F(position.x+offset.x),F(position.y+offset.y),F(position.z+offset.z));
                Collisions[i]=info;
            }
        }

        public void Release(){Collisions=null;IsInitialized=false;}

        static Vector3 Rotate(Quaternion q,Vector3 v)
        {
            // Float32 instruction order of original Quaternion.op_Multiply
            // 0x5e47520; current Unity's compiled operator can round differently.
            float x2=F(q.x+q.x),y2=F(q.y+q.y),z2=F(q.z+q.z);
            float xx=F(x2*q.x),yy=F(y2*q.y),zz=F(z2*q.z);
            float xy=F(y2*q.x),xz=F(z2*q.x),yz=F(z2*q.y);
            float xw=F(x2*q.w),yw=F(y2*q.w),zw=F(z2*q.w);
            return new Vector3(
                F(F(F(F(1f-F(zz+yy))*v.x)+F(F(xy-zw)*v.y))+F(F(yw+xz)*v.z)),
                F(F(F(F(1f-F(zz+xx))*v.y)+F(F(zw+xy)*v.x))+F(F(yz-xw)*v.z)),
                F(F(F(F(xw+yz)*v.y)+F(F(xz-yw)*v.x))+F(F(1f-F(yy+xx))*v.z)));
        }

        public Vector3 UpdateTargetPoint(Vector3 target)
        {
            if(!IsInitialized)return target;
            foreach(var info in Collisions)
            {
                if(info.Parameter.Type!=IKCollisionData.Parameter.CollisionType.Sphere)continue;
                Vector3 delta=target-info.WorldPosition;
                float length2=F(F(F(delta.y*delta.y)+F(delta.x*delta.x))+F(delta.z*delta.z));
                float radius=info.Parameter.Radius;
                if(!(F(radius*radius)>length2))continue;
                // Original 0x6d0380 uses three divss instructions. Current
                // Unity Vector3 division can multiply by a shared reciprocal,
                // which changes the sphere boundary result by one ULP.
                float length=F((float)System.Math.Sqrt(length2));
                Vector3 normal=length>1e-5f?new Vector3(F(delta.x/length),F(delta.y/length),F(delta.z/length)):Vector3.zero;
                target=new Vector3(F(F(radius*normal.x)+info.WorldPosition.x),
                    F(F(radius*normal.y)+info.WorldPosition.y),F(F(radius*normal.z)+info.WorldPosition.z));
            }
            return target;
        }
    }
}
