using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using Gallop.RenderPipeline;

namespace Gallop.Live.Cutt
{
    [Serializable]
    public class LiveTimelineKeyTransmittedLightData:LiveTimelineKeyWithInterpolate
    {
        public override LiveTimelineKeyDataType dataType=>LiveTimelineKeyDataType.TransmittedLight;
        public int Iterations=1;public float Intensity=1,Threshold,BlurSpread;public int BlendMode=1;
        public bool IsEnabled=>((int)attribute&0x10000)!=0;
    }
    [Serializable] public class LiveTimelineKeyTransmittedLightDataList:LiveTimelineKeyDataListTemplate<LiveTimelineKeyTransmittedLightData>{}
    [Serializable] public class LiveTimelineKeyMultiCameraTransmittedLightData:LiveTimelineKeyTransmittedLightData
    {public override LiveTimelineKeyDataType dataType=>LiveTimelineKeyDataType.MultiCameraTransmittedLight;}
    [Serializable] public class LiveTimelineKeyMultiCameraTransmittedLightDataList:LiveTimelineKeyDataListTemplate<LiveTimelineKeyMultiCameraTransmittedLightData>{}
    [Serializable] public class LiveTimelineMultiCameraTransmittedLightData:LiveTimelineMultiCameraPostEffect
    {
        public LiveTimelineKeyMultiCameraTransmittedLightDataList keys=new LiveTimelineKeyMultiCameraTransmittedLightDataList();
        public LiveTimelineMultiCameraTransmittedLightData():base("TransmittedLight"){}
        public override ILiveTimelineKeyDataList GetKeyList()=>keys;
    }
    public static class LiveTransmittedLightTimeline
    {
        public struct UpdateInfo {public bool IsEnabled;public int Iterations;public float Intensity,Threshold,BlurSpread;public int BlendMode;}
        [MethodImpl(MethodImplOptions.NoInlining)] static float F(float v)=>BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(v));
        public static float Interpolation(LiveTimelineKeyWithInterpolate a,LiveTimelineKeyWithInterpolate b,int frame)
        {
            float duration=(float)unchecked(b.frame-a.frame),elapsed=(float)unchecked(frame-a.frame);
            switch(b.interpolateType)
            {
                case LiveCameraInterpolateType.Linear:return Mathf.Clamp01(F(elapsed/duration));
                case LiveCameraInterpolateType.Curve:return b.curve.Evaluate(Mathf.Clamp01(F(elapsed/duration)));
                case LiveCameraInterpolateType.Ease:return LiveTimelineEasing.GetValue(b.easingType,elapsed,0,1,duration);
                default:return 0;
            }
        }
        public static UpdateInfo SampleAt(LiveTimelineKeyTransmittedLightData a,LiveTimelineKeyTransmittedLightData b,float t)
        {
            if(a==null)throw new NullReferenceException();
            var p=new UpdateInfo{IsEnabled=a.IsEnabled,Iterations=a.Iterations,Intensity=a.Intensity,Threshold=a.Threshold,BlurSpread=a.BlurSpread,BlendMode=a.BlendMode};
            if(b==null||!b.IsInterpolateKey())return p;
            p.Intensity=F(a.Intensity+F(F(b.Intensity-a.Intensity)*t));p.Threshold=F(a.Threshold+F(F(b.Threshold-a.Threshold)*t));p.BlurSpread=F(a.BlurSpread+F(F(b.BlurSpread-a.BlurSpread)*t));return p;
        }
        public static bool TryEvaluate<T>(LiveTimelineKeyDataListTemplate<T> keys,int frame,TimelinePlayerMode mode,out UpdateInfo info) where T:LiveTimelineKeyTransmittedLightData
        {
            info=default;if(keys==null)throw new NullReferenceException();
            if(keys.Count==0||keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable)||!keys.EnablePlayModeTimeline(mode))return false;
            LiveTimelineControl.FindTimelineKey(out var cur,out var next,keys,frame);
            if(cur==null)return false;
            var a=(LiveTimelineKeyTransmittedLightData)cur;var b=next as LiveTimelineKeyTransmittedLightData;
            info=SampleAt(a,b,b!=null&&b.IsInterpolateKey()?Interpolation(a,b,frame):0);return true;
        }
        public static void Apply(ref TransmittedLightPass.Parameter p,UpdateInfo info)
        {p.IsEnable=info.IsEnabled;p.Iterations=info.Iterations;p.Intensity=info.Intensity;p.Threshold=info.Threshold;p.BlurSpread=info.BlurSpread;p.BlendMode=info.BlendMode;}
        public static bool ApplyToCameraArray(GallopImageEffect[] effects,int active,int motion,UpdateInfo info)
        {
            if(effects==null)throw new NullReferenceException();int index=unchecked(active-motion);
            if(index<0||index>=effects.Length)return false;var e=effects[index];if(e==null)return false;Apply(ref e.TransmittedLightParam,info);return true;
        }
        public static bool ApplyToMultiCamera(MultiCamera[] cameras,int index,UpdateInfo info)
        {
            if(cameras==null||index<0||index>=cameras.Length)return false;
            var c=cameras[index];if(c==null)throw new NullReferenceException();Apply(ref c.PostEffectParameter.TransmittedLight,info);return true;
        }
    }
}
