using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Gallop.Live.Cutt;
using UnityEngine;

namespace Gallop.Live
{
    // LiveFaceController submission plus programmable mouth-weight transition.
    // Actual corpus Pattern keys all use Linear; other modes retain native trig form.
    public sealed class LiveLipSyncMouthState
    {
        public const int MouthCount=57;
        readonly float[] previous=new float[MouthCount],target=new float[MouthCount],current=new float[MouthCount];
        FaceParts[] cached;
        float duration,currentTime;
        DrivenKeyComponent.InterpolateType interpolation;
        bool initialized,patternOwner=true;
        public bool HasSubmission=>initialized&&patternOwner;
        public bool HasTarget=>initialized;
        public void SetPatternOwner(bool value){patternOwner=value;}
        public float CurrentTime=>currentTime;
        public IReadOnlyList<float> Weights=>current;

        public static FaceParts[] BuildParts(LiveTimelineKeyLipSyncPatternData key)
        {
            if(key==null)throw new ArgumentNullException(nameof(key));
            if(key.IsEnabledFaceParts)
            {
                var source=key.facialPartsDataArray;
                if(source==null)throw new NullReferenceException();
                var result=new FaceParts[source.Length];
                for(int i=0;i<source.Length;i++)result[i]=new FaceParts{_faceParts=LiveLipSyncMouthTable.Part(source[i].FacialPartsId),
                    _weight=CySpringWindParam.NativeMul(source[i].WeightPer,.01f)};
                return result;
            }
            if(!LiveLipSyncMouthTable.Facial(key.facialId,out var part))return null;
            part._weight=CySpringWindParam.NativeMul(part._weight,Mathf.Clamp01(CySpringWindParam.NativeMul(key.weight,.01f)));
            return new[]{part};
        }

        public bool Submit(FaceParts[] parts,float durationTime,DrivenKeyComponent.InterpolateType mode,IReadOnlyList<float> visibleWeights,bool force=false)
        {
            if(parts==null)return false;
            bool same=!force && initialized && duration==durationTime && interpolation==mode && cached.Length==parts.Length;
            if(same)for(int i=0;i<parts.Length;i++)
                    if(cached[i]._faceParts!=parts[i]._faceParts || !Gallop.Math.IsFloatEqualLight(cached[i]._weight,parts[i]._weight)) {same=false;break;}
            if(same)return false;
            cached=(FaceParts[])parts.Clone();duration=durationTime;interpolation=mode;currentTime=0f;
            Array.Clear(target,0,target.Length);
            for(int i=0;i<MouthCount;i++)previous[i]=current[i]=visibleWeights!=null && i<visibleWeights.Count ? visibleWeights[i] : 0f;
            foreach(var part in parts)if(part._faceParts>0)target[part._faceParts]=part._weight;
            if(durationTime<=0f)Array.Copy(target,current,MouthCount);
            initialized=true;return true;
        }

        // LipSync.SetMouth enters DrivenKey directly: it has no ordinary
        // LiveFaceController submission gate and retains the group's current
        // interpolation kind. Repeated terminal close commands restart it.
        public void SubmitDirect(FaceParts[] parts,float weight,float durationTime,IReadOnlyList<float> visibleWeights)
        {
            var scaled=new FaceParts[parts.Length];
            for(int i=0;i<parts.Length;i++)
            {scaled[i]=parts[i];scaled[i]._weight=CySpringWindParam.NativeMul(parts[i]._weight,weight);}
            Submit(scaled,durationTime,interpolation,visibleWeights,true);patternOwner=false;
        }

        public void Advance(float elapsedTime)
        {
            if(!initialized || !(duration>currentTime))return;
            currentTime=CySpringWindParam.NativeAdd(currentTime,elapsedTime);
            float t=InterpolateRate(interpolation,currentTime,duration);
            for(int i=1;i<MouthCount;i++)if(previous[i]!=target[i])current[i]=CySpringWindParam.NativeLerp(previous[i],target[i],t);
        }

        public void Invalidate(){initialized=false;cached=null;}

        static float InterpolateRate(DrivenKeyComponent.InterpolateType mode,float time,float length)
        {
            return LiveFacialInterpolation.Rate(mode,time,length);
        }
    }
}
