using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using Gallop.RenderPipeline;

namespace Gallop.ImageEffect
{
    public static class SceneParameterMath
    {
        [MethodImpl(MethodImplOptions.NoInlining)] public static float F(float v)=>BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(v));
        public static float L(float a,float b,float t){t=t<0?0:t>1?1:t;return F(a+F(F(b-a)*t));}
        public static Vector2 L(Vector2 a,Vector2 b,float t)=>new Vector2(L(a.x,b.x,t),L(a.y,b.y,t));
        public static Vector3 L(Vector3 a,Vector3 b,float t)=>new Vector3(L(a.x,b.x,t),L(a.y,b.y,t),L(a.z,b.z,t));
        public static Vector4 L(Vector4 a,Vector4 b,float t)=>new Vector4(L(a.x,b.x,t),L(a.y,b.y,t),L(a.z,b.z,t),L(a.w,b.w,t));
    }
    [Serializable]
    public sealed class TransmittedLightParam
    {
        public bool IsEnable;public int Iterations=1;public float Intensity=1,Threshold,BlurSpread;public int BlendMode=1;
        public void Setup(TransmittedLightParam src)
        {if(src==null)throw new NullReferenceException();IsEnable=src.IsEnable;Iterations=src.Iterations;Intensity=src.Intensity;Threshold=src.Threshold;BlurSpread=src.BlurSpread;BlendMode=src.BlendMode;}
        public void Setup(TransmittedLightPass.Parameter src)
        {IsEnable=src.IsEnable;Iterations=src.Iterations;Intensity=src.Intensity;Threshold=src.Threshold;BlurSpread=src.BlurSpread;BlendMode=src.BlendMode;}
        public void Lerp(TransmittedLightParam a,TransmittedLightParam b,float t)
        {if(a==null||b==null)throw new NullReferenceException();Intensity=SceneParameterMath.L(a.Intensity,b.Intensity,t);Threshold=SceneParameterMath.L(a.Threshold,b.Threshold,t);BlurSpread=SceneParameterMath.L(a.BlurSpread,b.BlurSpread,t);}
    }
    [Serializable]
    public sealed class RainSplashParam
    {
        public bool IsEnable;public int DownscaleFactor=4;public Camera TargetCamera;
        public Vector2 OffsetDirectionBase=new Vector2(0,-.02f);public float OffsetScaleBase=1;
        public Vector2 OffsetDirectionExpand;public float OffsetScaleExpand=1,MinDepthValue,MaxDepthValue=BitConverter.Int32BitsToSingle(0x3f7ff972);
        public Texture2D MistNoiseTexture;public Color MistColor=Color.white;public float MistIntensity=1,MistDensity=1;
        public Vector2 MistScrollSpeed=new Vector2(.1f,.1f);public float MistBlurSize=1,MistPower=.5f;
        public Vector2 SplashScrollSpeed=new Vector2(.1f,.1f);public Color SplashColor=Color.white;public Texture2D SplashNoiseTexture;
        public float SplashThreshold=.005f,SplashBaseRatio=.005f,SplashDensity=300,SplashIntensity=1,SplashPower=.5f;
        public bool IsSplashOuter,IsUseRendererList;
        public bool IsValidity=>IsEnable;
        public void Setup(RainSplashParam src)
        {
            if(src==null)throw new NullReferenceException();
            OffsetDirectionBase=src.OffsetDirectionBase;OffsetScaleBase=src.OffsetScaleBase;OffsetDirectionExpand=src.OffsetDirectionExpand;OffsetScaleExpand=src.OffsetScaleExpand;
            MinDepthValue=src.MinDepthValue;MaxDepthValue=src.MaxDepthValue;MistNoiseTexture=src.MistNoiseTexture;MistColor=src.MistColor;MistIntensity=src.MistIntensity;
            MistScrollSpeed=src.MistScrollSpeed;MistBlurSize=src.MistBlurSize;MistPower=src.MistPower;SplashColor=src.SplashColor;
            SplashThreshold=src.SplashThreshold;SplashBaseRatio=src.SplashBaseRatio;SplashDensity=src.SplashDensity;SplashIntensity=src.SplashIntensity;SplashPower=src.SplashPower;IsSplashOuter=src.IsSplashOuter;IsUseRendererList=src.IsUseRendererList;
        }
        public void Setup(RainSplashPass.Parameter src)
        {
            IsEnable=src.IsEnable;OffsetDirectionBase=src.OffsetDirectionBase;OffsetScaleBase=src.OffsetScaleBase;OffsetDirectionExpand=src.OffsetDirectionExpand;OffsetScaleExpand=src.OffsetScaleExpand;
            MinDepthValue=src.MinDepthValue;MaxDepthValue=src.MaxDepthValue;MistNoiseTexture=src.MistNoiseTexture;MistColor=src.MistColor;MistIntensity=src.MistIntensity;
            MistScrollSpeed=src.MistScrollSpeed;MistBlurSize=src.MistBlurSize;MistPower=src.MistPower;SplashNoiseTexture=src.SplashNoiseTexture;SplashScrollSpeed=src.SplashScrollSpeed;SplashColor=src.SplashColor;
            SplashThreshold=src.SplashThreshold;SplashBaseRatio=src.SplashBaseRatio;SplashDensity=src.SplashDensity;SplashIntensity=src.SplashIntensity;SplashPower=src.SplashPower;IsSplashOuter=src.IsSplashOuter;IsUseRendererList=src.IsUseRendererList;
        }
        public void Lerp(RainSplashParam a,RainSplashParam b,float t)
        {
            if(a==null||b==null)throw new NullReferenceException();
            OffsetDirectionBase=SceneParameterMath.L(a.OffsetDirectionBase,b.OffsetDirectionBase,t);OffsetScaleBase=SceneParameterMath.L(a.OffsetScaleBase,b.OffsetScaleBase,t);
            OffsetDirectionExpand=SceneParameterMath.L(a.OffsetDirectionExpand,b.OffsetDirectionExpand,t);OffsetScaleExpand=SceneParameterMath.L(a.OffsetScaleExpand,b.OffsetScaleExpand,t);
            MinDepthValue=SceneParameterMath.L(a.MinDepthValue,b.MinDepthValue,t);MaxDepthValue=SceneParameterMath.L(a.MaxDepthValue,b.MaxDepthValue,t);
            MistColor=SceneParameterMath.L(a.MistColor,b.MistColor,t);MistIntensity=SceneParameterMath.L(a.MistIntensity,b.MistIntensity,t);MistScrollSpeed=SceneParameterMath.L(a.MistScrollSpeed,b.MistScrollSpeed,t);MistBlurSize=SceneParameterMath.L(a.MistBlurSize,b.MistBlurSize,t);MistPower=SceneParameterMath.L(a.MistPower,b.MistPower,t);
            SplashScrollSpeed=SceneParameterMath.L(a.SplashScrollSpeed,b.SplashScrollSpeed,t);SplashColor=SceneParameterMath.L(a.SplashColor,b.SplashColor,t);SplashThreshold=SceneParameterMath.L(a.SplashThreshold,b.SplashThreshold,t);SplashBaseRatio=SceneParameterMath.L(a.SplashBaseRatio,b.SplashBaseRatio,t);SplashIntensity=SceneParameterMath.L(a.SplashIntensity,b.SplashIntensity,t);SplashDensity=SceneParameterMath.L(a.SplashDensity,b.SplashDensity,t);SplashPower=SceneParameterMath.L(a.SplashPower,b.SplashPower,t);
        }
    }
    public static class NativeSceneParameterOperations
    {
        public static void SetUp(this ref TransmittedLightPass.Parameter p,TransmittedLightParam src)
        {if(src==null)throw new NullReferenceException();p.IsEnable=src.IsEnable;p.Iterations=src.Iterations;p.Intensity=src.Intensity;p.Threshold=src.Threshold;p.BlurSpread=src.BlurSpread;p.BlendMode=src.BlendMode;}
        public static void Setup(this ref RainSplashPass.Parameter p,RainSplashParam src)
        {
            if(src==null)throw new NullReferenceException();
            p.IsEnable=src.IsEnable;p.IsUseRendererList=src.IsUseRendererList;p.DownscaleFactor=src.DownscaleFactor;
            p.OffsetDirectionBase=src.OffsetDirectionBase;p.OffsetScaleBase=src.OffsetScaleBase;p.OffsetDirectionExpand=src.OffsetDirectionExpand;p.OffsetScaleExpand=src.OffsetScaleExpand;p.MinDepthValue=src.MinDepthValue;p.MaxDepthValue=src.MaxDepthValue;
            p.MistNoiseTexture=src.MistNoiseTexture;p.MistColor=src.MistColor;p.MistIntensity=src.MistIntensity;p.MistDensity=src.MistDensity;p.MistScrollSpeed=src.MistScrollSpeed;p.MistBlurSize=src.MistBlurSize;p.MistPower=src.MistPower;
            p.SplashNoiseTexture=src.SplashNoiseTexture;p.SplashScrollSpeed=src.SplashScrollSpeed;p.SplashColor=src.SplashColor;p.SplashThreshold=src.SplashThreshold;p.SplashBaseRatio=src.SplashBaseRatio;p.SplashDensity=src.SplashDensity;p.SplashIntensity=src.SplashIntensity;p.SplashPower=src.SplashPower;p.IsSplashOuter=src.IsSplashOuter;
        }
        public static void SetVisibleRenderInfo(this ref RainSplashPass.Parameter p,int index,bool visible)
        {if(p.RendererInfoDictionary==null)throw new NullReferenceException();if(p.RendererInfoDictionary.TryGetValue(index,out var info)){if(info==null)throw new NullReferenceException();info.IsVisible=visible;}}
        public static void UpdateTime(this ref RainSplashPass.Parameter p,float time)=>p.AppTime=time;
        public static void Setup(this AuraPass.AuraParam p,AuraPass.AuraParam src)
        {
            if(src==null)throw new NullReferenceException();p.IsEnable=src.IsEnable;p.Fresnel=src.Fresnel;p.LuminousParam=src.LuminousParam;p.DistortionParam=src.DistortionParam;p.AuraColor=src.AuraColor;p.OffsetViewDir=src.OffsetViewDir;p.AuraWidth=src.AuraWidth;p.IsEdgeOnly=src.IsEdgeOnly;
        }
        public static void Lerp(this AuraPass.AuraParam p,AuraPass.AuraParam a,AuraPass.AuraParam b,float t)
        {
            if(a==null||b==null)throw new NullReferenceException();p.Fresnel=SceneParameterMath.L(a.Fresnel,b.Fresnel,t);p.LuminousParam=SceneParameterMath.L(a.LuminousParam,b.LuminousParam,t);p.DistortionParam=SceneParameterMath.L(a.DistortionParam,b.DistortionParam,t);p.AuraColor=SceneParameterMath.L(a.AuraColor,b.AuraColor,t);p.OffsetViewDir=SceneParameterMath.L(a.OffsetViewDir,b.OffsetViewDir,t);p.AuraWidth=SceneParameterMath.L(a.AuraWidth,b.AuraWidth,t);
        }
        public static void UpdateOffset(this AuraPass.AuraParam p,float deltaTime)
        {if(p.IsEnable){var d=p.DistortionParam;d.x=SceneParameterMath.F(d.x+SceneParameterMath.F(deltaTime*d.w))%1f;p.DistortionParam=d;}}
    }
}
