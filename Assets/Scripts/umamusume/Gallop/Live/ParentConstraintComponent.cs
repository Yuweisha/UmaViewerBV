using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop
{
    public static class ConstraintUtil
    {
        public sealed class Source
        {
            public Transform Transform {get;set;}
            public float Weight {get;set;}=1f;
        }
    }
    public class ConstraintBehaviour : MonoBehaviour
    {
        public enum UpdateTiming { LateUpdatePre=0,LateUpdateBeforeModel=1 }
        UpdateTiming _updateTiming;
        Transform _cachedTransform;
        bool _isUpdated;
        public Transform CachedTransform=>_cachedTransform;
        protected virtual void OnEnable(){if(ConstraintUpdater.Instance!=null)ConstraintUpdater.Instance.AddItem(this);}
        protected virtual void OnDisable(){if(ConstraintUpdater.Instance!=null)ConstraintUpdater.Instance.RemoveItem(this);}
        public void SetUpdateTiming(UpdateTiming timing){if(!_isUpdated)_updateTiming=timing;}
        public void SetupTransform(){if(!_isUpdated)_cachedTransform=transform;}
        public void AlterLateUpdatePre(){if(_updateTiming==UpdateTiming.LateUpdatePre){SetupTransform();AlterUpdate();_isUpdated=true;}}
        public void AlterLateUpdateBeforeModel(){if(_updateTiming==UpdateTiming.LateUpdateBeforeModel){SetupTransform();AlterUpdate();_isUpdated=true;}}
        protected virtual void AlterUpdate() { }
        // Entry points for the host UpdateManager adapter. Installation into the
        // Director frame flow is separate from the component's arithmetic.
        public static void Dispatch(UpdateTiming timing)
        {
            var updater=ConstraintUpdater.Instance;if(updater==null)return;
            if(timing==UpdateTiming.LateUpdatePre)updater.AlterLateUpdatePre();else updater.AlterLateUpdateBeforeModel();
        }
    }
    public sealed class ParentConstraintComponent : ConstraintBehaviour
    {
        float _weight=1f;
        bool _isInfluenceOfSourceScale;
        readonly List<ConstraintUtil.Source> _sourceList=new List<ConstraintUtil.Source>();
        public float Weight=>_weight;
        public bool InfluenceOfSourceScale=>_isInfluenceOfSourceScale;
        public IReadOnlyList<ConstraintUtil.Source> Sources=>_sourceList;
        public event Action<string,Vector4> TransformWrite;
#if UNITY_EDITOR
        public static Action<string,Vector4> ArithmeticObservation;
#endif
        static float F(float x)=>Live.Cutt.LiveTimelineControl.MonitorFloat32(x);
        static float Add(float a,float b)=>F(a+b);
        static float Mul(float a,float b)=>F(a*b);
        static float Div(float a,float b)=>F(a/b);
        static Vector3 AddWeighted(Vector3 sum,Vector3 value,float weight)=>new Vector3(Add(sum.x,Mul(value.x,weight)),Add(sum.y,Mul(value.y,weight)),Add(sum.z,Mul(value.z,weight)));
        static Vector3 Divide(Vector3 v,float d)=>new Vector3(Div(v.x,d),Div(v.y,d),Div(v.z,d));
        static float Magnitude(Vector3 v)=>F((float)System.Math.Sqrt(Add(Add(Mul(v.y,v.y),Mul(v.x,v.x)),Mul(v.z,v.z))));
        static Vector3 Normalize(Vector3 v){float length=Magnitude(v);return length>1e-5f?Divide(v,length):Vector3.zero;}
        static float Lerp(float a,float b,float t)=>Add(Mul(F(b-a),t),a);
        static Vector3 Lerp(Vector3 a,Vector3 b,float t)=>new Vector3(Lerp(a.x,b.x,t),Lerp(a.y,b.y,t),Lerp(a.z,b.z,t));
        public void SetEnable(bool value)=>enabled=value;
        public void SetWeight(float value)=>_weight=value<0f?0f:value>1f?1f:value;
        public void SetInfluenceOfSourceScale(bool value)=>_isInfluenceOfSourceScale=value;
        public void SetSource(IReadOnlyList<ConstraintUtil.Source> sources)
        {
            int count=sources.Count;
            if(_sourceList.Count>count)_sourceList.RemoveRange(count,_sourceList.Count-count);
            while(_sourceList.Count<count)_sourceList.Add(new ConstraintUtil.Source());
            for(int i=0;i<count;i++)if(sources[i]!=null){_sourceList[i].Transform=sources[i].Transform;_sourceList[i].Weight=sources[i].Weight;}
        }
        static bool TryGetWeightedAverageRotation(IReadOnlyList<ConstraintUtil.Source> sources,float totalWeight,out Quaternion rotation)
        {
            rotation=Quaternion.identity;Quaternion reference=Quaternion.identity;bool found=false;
            foreach(var source in sources)if(source.Transform!=null){reference=source.Transform.rotation;found=true;break;}
            if(!found)return false;
            Vector3 sum=Vector3.zero;
            foreach(var source in sources)
            {
                if(source.Weight<1e-6f||source.Transform==null)continue;
                var q=source.Transform.rotation;
                float dot=Add(Add(Add(Mul(reference.x,q.x),Mul(reference.y,q.y)),Mul(reference.z,q.z)),Mul(reference.w,q.w));
                if(dot<0f)q=new Quaternion(-q.x,-q.y,-q.z,-q.w);
                q.ToAngleAxis(out float degrees,out var axis);
                float radians=Mul(degrees,0.0174532924f);
                if(radians<1e-6f)continue;
                float angleWeight=Mul(Div(source.Weight,totalWeight),Mul(radians,0.5f));sum=AddWeighted(sum,Normalize(axis),angleWeight);
            }
            float magnitude=Magnitude(sum);if(magnitude<1e-6f)return true;
            var normalized=Normalize(sum);float sx=Mathf.Sin(magnitude),sy=Mathf.Sin(magnitude),sz=Mathf.Sin(magnitude);float x=Mul(sx,normalized.x),y=Mul(sy,normalized.y),z=Mul(sz,normalized.z),w=Mathf.Cos(magnitude);
#if UNITY_EDITOR
            ArithmeticObservation?.Invoke("sum",sum);ArithmeticObservation?.Invoke("magnitude",new Vector4(magnitude,0,0,0));ArithmeticObservation?.Invoke("normalized",normalized);ArithmeticObservation?.Invoke("trig",new Vector4(sx,sy,sz,w));
#endif
            float length=Mathf.Sqrt(Add(Add(Add(Mul(x,x),Mul(y,y)),Mul(z,z)),Mul(w,w)));
            rotation=length<Mathf.Epsilon?Quaternion.identity:new Quaternion(Div(x,length),Div(y,length),Div(z,length),Div(w,length));return true;
        }
        protected override void AlterUpdate()
        {
            if(_sourceList.Count<1)return;Vector3 sum=Vector3.zero;float totalWeight=0f;
            foreach(var source in _sourceList)if(source.Transform!=null){sum=AddWeighted(sum,source.Transform.position,source.Weight);totalWeight=Add(totalWeight,source.Weight);}
            if(totalWeight<1e-6f)return;
            var target=CachedTransform;var p=Lerp(target.position,Divide(sum,totalWeight),Mathf.Clamp01(_weight));target.position=p;TransformWrite?.Invoke("position",p);
            if(TryGetWeightedAverageRotation(_sourceList,totalWeight,out var q))
            {q=Quaternion.Slerp(target.rotation,q,_weight);target.rotation=q;TransformWrite?.Invoke("rotation",new Vector4(q.x,q.y,q.z,q.w));}
            if(!_isInfluenceOfSourceScale)return;
            sum=Vector3.zero;foreach(var source in _sourceList)if(source.Transform!=null)sum=AddWeighted(sum,source.Transform.lossyScale,source.Weight);
            var scale=Divide(sum,totalWeight);var parentScale=target.parent!=null?target.parent.lossyScale:Vector3.one;
            scale=new Vector3(Div(scale.x,parentScale.x),Div(scale.y,parentScale.y),Div(scale.z,parentScale.z));scale=Lerp(target.localScale,scale,Mathf.Clamp01(_weight));target.localScale=scale;TransformWrite?.Invoke("localScale",scale);
        }
    }
}
