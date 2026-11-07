using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    // Original AlterUpdate_GlobalFog 4ed1900 -> Director 4e2e870.
    // Selection uses the existing Viewer list/cache bridge; sampling below is
    // the original integer-frame path, not the camera's seconds interpolation.
    public static class LiveGlobalFogTimeline
    {
        public delegate bool BlinkColorResolver(int nameHash,int containerIndex,out Color raw);
        public struct UpdateInfo
        {
            public bool IsDistance,IsHeight,UseRadialDistance;
            public float StartDistance,Height,HeightDensity;
            public Color Color;
            public int FogMode;
            public float ExpDensity,Start,End;
        }
        [MethodImpl(MethodImplOptions.NoInlining)] static float F(float v)=>BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(v));
        static float Lerp(float a,float b,float t)=>F(a+F(F(b-a)*t));
        public static float Interpolation(LiveTimelineKeyGlobalFogData a,LiveTimelineKeyGlobalFogData b,int frame)
        {
            switch(b.interpolateType)
            {
                case LiveCameraInterpolateType.Linear:
                    return Mathf.Clamp01(F((float)unchecked(frame-a.frame)/(float)unchecked(b.frame-a.frame)));
                case LiveCameraInterpolateType.Curve:
                    return b.curve.Evaluate(Mathf.Clamp01(F((float)unchecked(frame-a.frame)/(float)unchecked(b.frame-a.frame))));
                case LiveCameraInterpolateType.Ease:
                    return LiveTimelineEasing.GetValue(b.easingType,(float)unchecked(frame-a.frame),0,1,(float)unchecked(b.frame-a.frame));
                default:return 0;
            }
        }
        public static UpdateInfo Sample(LiveTimelineKeyGlobalFogData a,LiveTimelineKeyGlobalFogData b,int frame,BlinkColorResolver blink=null)
        {
            if(a==null)throw new NullReferenceException();
            Color color=a.color;
            if(a.IsSyncBlinkLight&&blink!=null&&blink(a.BlinkLightNameHash,a.BlinkLightContainerIndex,out var raw))
            {
                raw=LivePostFilmTimeline.AdjustBlinkRgb(raw,a.BlinkLightBrightnessPower,a.IsAdjustedBlinkLightColor);
                color.r=raw.r;color.g=raw.g;color.b=raw.b;
            }
            var p=new UpdateInfo{IsDistance=a.isDistance,IsHeight=a.isHeight,UseRadialDistance=a.useRadialDistance,StartDistance=a.startDistance,Height=a.height,HeightDensity=a.heightDensity,Color=color,FogMode=a.fogMode,ExpDensity=a.expDensity,Start=a.start,End=a.end};
            if(b==null||!b.IsInterpolateKey())return p;
            float t=Interpolation(a,b,frame);
            p.IsDistance=b.isDistance;p.IsHeight=b.isHeight;p.UseRadialDistance=b.useRadialDistance;p.FogMode=b.fogMode;
            p.StartDistance=Lerp(a.startDistance,b.startDistance,t);p.Height=Lerp(a.height,b.height,t);p.HeightDensity=Lerp(a.heightDensity,b.heightDensity,t);
            if(!b.IsSyncBlinkLight)p.Color=new Color(Lerp(color.r,b.color.r,t),Lerp(color.g,b.color.g,t),Lerp(color.b,b.color.b,t),Lerp(color.a,b.color.a,t));
            p.ExpDensity=Lerp(a.expDensity,b.expDensity,t);p.Start=Lerp(a.start,b.start,t);p.End=Lerp(a.end,b.end,t);return p;
        }
        public static bool TryEvaluate(LiveTimelineKeyGlobalFogDataList keys,int frame,TimelinePlayerMode mode,out UpdateInfo info,BlinkColorResolver blink=null)
        {
            info=default;
            if(keys==null||keys.Count==0||keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable)||!keys.EnablePlayModeTimeline(mode))return false;
            LiveTimelineControl.FindTimelineKey(out var a,out var b,keys,frame);
            if(a==null)return false;
            info=Sample(a as LiveTimelineKeyGlobalFogData,b as LiveTimelineKeyGlobalFogData,frame,blink);return true;
        }
        public static void Apply(GallopImageEffect effect,UpdateInfo info)
        {
            if(effect==null)throw new NullReferenceException();
            // Preserve the original eleven getter/write operations. Do not
            // replace the whole parameter, reset camera/event/FOV, or alter
            // RenderSettings (a different fog path used by scene materials).
            effect.GlobalFogParam.IsUseRadialDistance=info.UseRadialDistance;
            effect.GlobalFogParam.IsHeightFog=info.IsHeight;
            effect.GlobalFogParam.IsDistanceFog=info.IsDistance;
            effect.GlobalFogParam.StartDistance=info.StartDistance;
            effect.GlobalFogParam.FogHeight=info.Height;
            effect.GlobalFogParam.FogHeightDensity=info.HeightDensity;
            effect.GlobalFogParam.FogColor=info.Color;
            effect.GlobalFogParam.SceneFogMode=(FogMode)info.FogMode;
            effect.GlobalFogParam.SceneFogDensity=info.ExpDensity;
            effect.GlobalFogParam.SceneFogStart=info.Start;
            effect.GlobalFogParam.SceneFogEnd=info.End;
        }
        public static bool ApplyToCameraArray(GallopImageEffect[] effects,int activeIndex,int motionIndex,UpdateInfo info)
        {
            if(effects==null)throw new NullReferenceException();
            int index=unchecked(activeIndex-motionIndex);
            if(index<0||index>=effects.Length)return false;
            Apply(effects[index],info);return true;
        }
    }
}
