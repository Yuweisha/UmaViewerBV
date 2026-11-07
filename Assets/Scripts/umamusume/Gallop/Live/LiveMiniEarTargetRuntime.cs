using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Gallop.Live
{
    // Non-override ear targets: original EarWeightJob CalcTRS/CalcEarTRS and
    // WeightGroupBase SetTRS/AddTRS, then the force-apply bone setter branch.
    public sealed class LiveMiniEarTargetRuntime
    {
        public struct Result
        {
            public float Weight;
            public Vector3 Position,Scale,Rotation;
            public Quaternion MayaRotation;
        }
        readonly DrivenKeyTarget target;
        readonly float scale;
        readonly Transform[] bones;
        public Result[] Results {get;}=new Result[6];
        public event Action<Vector3,Quaternion> QuaternionLeaf;
        public event Action<Transform,Vector3,Quaternion> BoneWrite;
        public LiveMiniEarTargetRuntime(DrivenKeyTarget target,float scale,Transform root)
        {
            this.target=target;this.scale=scale;
            if(target._targetFaces.Any(face=>face._faceGroupInfo.Any(group=>group._trsArray.Any(trs=>trs.IsOverrideTarget))))throw new NotSupportedException("Override target ear job");
            bones=target._targetFaces[0]._faceGroupInfo.SelectMany(group=>group._trsArray).Select(trs=>root.GetComponentsInChildren<Transform>(true).Single(node=>node.name==trs._path)).ToArray();
        }
        static float F(float value)=>Cutt.LiveTimelineControl.MonitorFloat32(value);
        static Vector3 Mul(Vector3 value,float amount)=>new Vector3(F(value.x*amount),F(value.y*amount),F(value.z*amount));
        static Vector3 Add(Vector3 a,Vector3 b)=>new Vector3(F(a.x+b.x),F(a.y+b.y),F(a.z+b.z));
        Quaternion Maya(Vector3 degrees)
        {return LiveEarQuaternion.FromMayaEuler(degrees,QuaternionLeaf);}
        public void Calculate(LiveMiniEarWeightRuntime weights)
        {
            Array.Clear(Results,0,Results.Length);
            foreach(var ear in weights.BlendEarTypes)for(int group=0;group<2;group++)for(int bone=0;bone<3;bone++)
            {
                int index=group*3+bone;float weight=weights.CommonWeights[(int)ear*6+index];if(weight<=0)continue;var trs=target._targetFaces[(int)ear]._faceGroupInfo[group]._trsArray[bone];var result=Results[index];var position=Mul(Mul(trs._position,scale),weight);var rotation=Mul(trs._rotation,weight);
                if(ear==EarType.Base){result.Weight=weight;result.Position=position;result.Rotation=rotation;if(trs._isValidScaleTransform)result.Scale=Mul(trs._scale,weight);}
                else{result.Weight=F(result.Weight+weight);result.Position=Add(result.Position,position);result.Rotation=Add(result.Rotation,rotation);if(trs._isValidScaleTransform)result.Scale=Add(result.Scale,Mul(trs._scale,weight));}
                result.MayaRotation=Maya(result.Rotation);Results[index]=result;
            }
        }
        public void ApplyForce()
        {
            for(int i=0;i<bones.Length;i++)
            {
                var result=Results[i];bones[i].SetLocalPositionAndRotation(result.Position,result.MayaRotation);BoneWrite?.Invoke(bones[i],result.Position,result.MayaRotation);var trs=target._targetFaces[0]._faceGroupInfo[i/3]._trsArray[i%3];if(trs._isValidScaleTransform)bones[i].localScale=result.Scale;
            }
        }
    }
}
