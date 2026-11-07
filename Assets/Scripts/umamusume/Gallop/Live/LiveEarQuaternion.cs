using System;
using UnityEngine;

namespace Gallop.Live
{
    // Original EarWeightJob and WeightCalcJob Maya conversion: axis engine leaves followed by
    // the original scalar quaternion product order, with float32 operations.
    public static class LiveEarQuaternion
    {
        static float F(float value)=>Cutt.LiveTimelineControl.MonitorFloat32(value);
        static Quaternion Multiply(Quaternion a,Quaternion b)=>new Quaternion(
            F(F(F(F(a.x*b.w)+F(a.w*b.x))+F(a.y*b.z))-F(a.z*b.y)),
            F(F(F(F(a.y*b.w)+F(a.w*b.y))+F(a.z*b.x))-F(a.x*b.z)),
            F(F(F(F(a.z*b.w)+F(a.w*b.z))+F(a.x*b.y))-F(a.y*b.x)),
            F(F(F(F(a.w*b.w)-F(a.x*b.x))-F(a.y*b.y))-F(a.z*b.z)));
        static Quaternion Axis(Vector3 degrees,Action<Vector3,Quaternion> observer)
        {
            var value=Quaternion.Euler(degrees);
            observer?.Invoke(new Vector3(F(degrees.x*Mathf.Deg2Rad),F(degrees.y*Mathf.Deg2Rad),F(degrees.z*Mathf.Deg2Rad)),value);
            return value;
        }
        public static Quaternion FromMayaEuler(Vector3 degrees,Action<Vector3,Quaternion> observer=null)
        {
            var z=Axis(new Vector3(0,0,degrees.z),observer);
            var y=Axis(new Vector3(0,degrees.y,0),observer);
            var first=Multiply(z,y);
            return Multiply(first,Axis(new Vector3(degrees.x,0,0),observer));
        }
    }
}
