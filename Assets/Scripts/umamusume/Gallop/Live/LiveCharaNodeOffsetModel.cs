using System;
using System.Collections.Generic;
using Gallop.CutIn.Cutt;
using Gallop.Live.Cutt;
using UnityEngine;

namespace Gallop.Live
{
    // Loaded Viewer model adapter for original ApplyCharacterNodeData/ApplyFixedNode.
    public sealed class LiveCharaNodeOffsetModel
    {
        readonly IReadOnlyDictionary<string,Transform> transforms;
        readonly Dictionary<string,Transform> fixedNodes=new Dictionary<string,Transform>();
        public event Action<string,Quaternion> RotationWrite;
        public event Action<string,Quaternion> RotationRead;
        public LiveCharaNodeOffsetModel(IReadOnlyDictionary<string,Transform> transforms){this.transforms=transforms??throw new ArgumentNullException(nameof(transforms));}
        static float F(float value)=>LiveTimelineControl.MonitorFloat32(value);
        static float Add(float a,float b)=>F(a+b);
        static float Sub(float a,float b)=>F(a-b);
        static float Mul(float a,float b)=>F(a*b);
        public static Quaternion MultiplyNative(Quaternion a,Quaternion b)=>new Quaternion(
            Sub(Add(Add(Mul(a.w,b.x),Mul(a.x,b.w)),Mul(a.y,b.z)),Mul(a.z,b.y)),
            Sub(Add(Add(Mul(a.w,b.y),Mul(a.y,b.w)),Mul(a.z,b.x)),Mul(a.x,b.z)),
            Sub(Add(Add(Mul(a.w,b.z),Mul(a.z,b.w)),Mul(a.x,b.y)),Mul(a.y,b.x)),
            Sub(Sub(Sub(Mul(a.w,b.w),Mul(a.x,b.x)),Mul(a.y,b.y)),Mul(a.z,b.z)));
        void Fixed(string name,KeyVector3 key)
        {
            if(!key._isValid)return;
            var delta=Quaternion.Euler(key._value);
            if(!fixedNodes.TryGetValue(name,out var target))
            {transforms.TryGetValue(name,out target);if(target==null)return;}
            var current=target.localRotation;RotationRead?.Invoke(name,current);
            var rotation=MultiplyNative(delta,current);target.localRotation=rotation;fixedNodes[name]=target;RotationWrite?.Invoke(name,rotation);
        }
        public void Apply(TimelineKeyCharacterNodeData data,int personality)
        {
            CharacterNodeData selected=data;
            if(data.IsUseParamForPersonal&&data.PersonalNodeDataArray.Length!=0)selected=data.PersonalNodeDataArray[personality-1];
            Fixed("Head",selected.HeadRotateOffset);Fixed("Neck",selected.NeckRotateOffset);Fixed("Chest",selected.ChestRotateOffset);Fixed("Spine",selected.SpineRotateOffset);Fixed("Waist",selected.WaistRotateOffset);
            foreach(var named in selected.NameNodeParamArray)
            {
                transforms.TryGetValue(named.Name,out var target);if(target==null)continue;
                if(named.AddTransformPosition)target.localPosition+=named.Position;
                if(named.AddTransformRotation)target.localRotation=MultiplyNative(named.Rotation,target.localRotation);
                if(named.AddTransformScale)target.localScale=Vector3.Scale(named.Scale,target.localScale);
            }
        }
    }
}
