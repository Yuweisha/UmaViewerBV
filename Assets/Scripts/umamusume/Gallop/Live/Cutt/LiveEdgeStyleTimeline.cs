using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using Gallop.RenderPipeline;

namespace Gallop.Live.Cutt
{
    [Serializable] public class LiveTimelineKeyEdgeStyleData:LiveTimelineKeyWithInterpolate
    {
        public bool IsEnable;
        public Vector2 OffsetDirectionBase;
        public float OffsetScaleBase=1;
        public Vector2 OffsetDirectionExpand;
        public float OffsetScaleExpand=1;
        public bool IsCharacterListMode;
        public List<int> VisibleIndexList=new List<int>();
        private HashSet<int> _visibleIndexHashSet=new HashSet<int>();
        public int DownscaleFactor=4;
        public float MinDepthValue;
        public float MaxDepthValue=.05f;
        public Texture2D MistNoiseTexture;
        public Color MistColor=Color.white;
        public float MistDensity=1;
        public float MistIntensity=1;
        public float MistPower=1;
        public Vector2 MistScrollSpeed;
        public float MistBlurSize;
        public Texture2D SplashNoiseTexture;
        public Vector2 SplashScrollSpeed;
        public Color SplashColor=Color.white;
        public float SplashThreshold=.1f;
        public float SplashBaseRatio=.5f;
        public float SplashDensity=2;
        public float SplashIntensity=1;
        public float SplashPower=.25f;
        public bool IsSplashOuter;
        public bool IsSplashInner;
        public HashSet<int> VisibleIndexHashSet=>_visibleIndexHashSet;
        public override LiveTimelineKeyDataType dataType=>LiveTimelineKeyDataType.EdgeStyle;
        public void OnAfterDeserializeRuntime()
        {_visibleIndexHashSet.Clear();foreach(int index in VisibleIndexList)_visibleIndexHashSet.Add(index);}
        public override void OnLoad(LiveTimelineControl control){base.OnLoad(control);OnAfterDeserializeRuntime();}
    }
    [Serializable] public class LiveTimelineKeyEdgeStyleDataList:LiveTimelineKeyDataListTemplate<LiveTimelineKeyEdgeStyleData>
    {public EdgeStyleMode EdgeStyle;}
    public struct EdgeStyleUpdateInfo
    {
        public bool IsEnable;
        public bool RenderListMode;
        public HashSet<int> VisibleIndexHashSet;
        public int DownscaleFactor;
        public Vector2 OffsetDirectionBase;
        public float OffsetScaleBase;
        public Vector2 OffsetDirectionExpand;
        public float OffsetScaleExpand;
        public float MinDepthValue;
        public float MaxDepthValue;
        public Texture2D MistNoiseTexture;
        public Color MistColor;
        public float MistIntensity;
        public float MistDensity;
        public Vector2 MistScrollSpeed;
        public float MistBlurSize;
        public float MistPower;
        public Texture2D SplashNoiseTexture;
        public Vector2 SplashScrollSpeed;
        public Color SplashColor;
        public float SplashThreshold;
        public float SplashBaseRatio;
        public float SplashIntensity;
        public float SplashDensity;
        public float SplashPower;
        public bool IsSplashOuter;
        public bool IsSplashInner;
    }
    public delegate void EdgeStyleDelegate(ref EdgeStyleUpdateInfo info);
    public static class LiveEdgeStyleTimeline
    {
        [MethodImpl(MethodImplOptions.NoInlining)] static float F(float v)=>BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(v));
        static float L(float a,float b,float t)=>F(a+F(F(b-a)*t));
        static Vector2 L(Vector2 a,Vector2 b,float t)=>new Vector2(L(a.x,b.x,t),L(a.y,b.y,t));
        static Color L(Color a,Color b,float t)=>new Color(L(a.r,b.r,t),L(a.g,b.g,t),L(a.b,b.b,t),L(a.a,b.a,t));
        public static EdgeStyleUpdateInfo SampleAt(LiveTimelineKeyEdgeStyleData a,LiveTimelineKeyEdgeStyleData b,float t)
        {
            if(a==null)throw new NullReferenceException();
            bool blend=b!=null&&b.IsInterpolateKey();
            return new EdgeStyleUpdateInfo
            {
                IsEnable=a.IsEnable,
                RenderListMode=a.IsCharacterListMode,
                VisibleIndexHashSet=a.VisibleIndexHashSet,
                DownscaleFactor=a.DownscaleFactor,
                OffsetDirectionBase=blend?L(a.OffsetDirectionBase,b.OffsetDirectionBase,t):a.OffsetDirectionBase,
                OffsetScaleBase=blend?L(a.OffsetScaleBase,b.OffsetScaleBase,t):a.OffsetScaleBase,
                OffsetDirectionExpand=blend?L(a.OffsetDirectionExpand,b.OffsetDirectionExpand,t):a.OffsetDirectionExpand,
                OffsetScaleExpand=blend?L(a.OffsetScaleExpand,b.OffsetScaleExpand,t):a.OffsetScaleExpand,
                MinDepthValue=blend?L(a.MinDepthValue,b.MinDepthValue,t):a.MinDepthValue,
                MaxDepthValue=blend?L(a.MaxDepthValue,b.MaxDepthValue,t):a.MaxDepthValue,
                MistNoiseTexture=a.MistNoiseTexture,
                MistColor=blend?L(a.MistColor,b.MistColor,t):a.MistColor,
                MistIntensity=blend?L(a.MistIntensity,b.MistIntensity,t):a.MistIntensity,
                MistDensity=blend?L(a.MistDensity,b.MistDensity,t):a.MistDensity,
                MistScrollSpeed=blend?L(a.MistScrollSpeed,b.MistScrollSpeed,t):a.MistScrollSpeed,
                MistBlurSize=blend?L(a.MistBlurSize,b.MistBlurSize,t):a.MistBlurSize,
                MistPower=blend?L(a.MistPower,b.MistPower,t):a.MistPower,
                SplashNoiseTexture=a.SplashNoiseTexture,
                SplashScrollSpeed=blend?L(a.SplashScrollSpeed,b.SplashScrollSpeed,t):a.SplashScrollSpeed,
                SplashColor=blend?L(a.SplashColor,b.SplashColor,t):a.SplashColor,
                SplashThreshold=blend?L(a.SplashThreshold,b.SplashThreshold,t):a.SplashThreshold,
                SplashBaseRatio=blend?L(a.SplashBaseRatio,b.SplashBaseRatio,t):a.SplashBaseRatio,
                SplashIntensity=blend?L(a.SplashIntensity,b.SplashIntensity,t):a.SplashIntensity,
                SplashDensity=blend?L(a.SplashDensity,b.SplashDensity,t):a.SplashDensity,
                SplashPower=blend?L(a.SplashPower,b.SplashPower,t):a.SplashPower,
                IsSplashOuter=a.IsSplashOuter,
                IsSplashInner=a.IsSplashInner,
            };
        }
        public static bool ApplyToCameraArray(GallopImageEffect[] effects,int active,int motion,float time,EdgeStyleUpdateInfo info)
        {
            if(effects==null)throw new NullReferenceException();int index=unchecked(active-motion);
            if(index<0||index>=effects.Length)return false;var effect=effects[index] as LiveImageEffect;
            if(effect==null||effect.CurrentEdgeStyle!=EdgeStyleMode.RainSplash)return false;
            ref var p=ref effect.RainSplashParam;
            p.IsEnable=info.IsEnable;p.IsUseRendererList=info.RenderListMode;
            if(info.RenderListMode&&p.RendererInfoDictionary!=null)
                foreach(int key in p.RendererInfoDictionary.Keys)
                    p.RendererInfoDictionary[key].IsVisible=info.VisibleIndexHashSet.Contains(key);
            p.AppTime=time;
            p.DownscaleFactor=info.DownscaleFactor;
            p.OffsetDirectionBase=info.OffsetDirectionBase;
            p.OffsetScaleBase=info.OffsetScaleBase;
            p.OffsetDirectionExpand=info.OffsetDirectionExpand;
            p.OffsetScaleExpand=info.OffsetScaleExpand;
            p.MinDepthValue=info.MinDepthValue;
            p.MaxDepthValue=info.MaxDepthValue;
            p.MistNoiseTexture=info.MistNoiseTexture;
            p.MistColor=info.MistColor;
            p.MistIntensity=info.MistIntensity;
            p.MistDensity=info.MistDensity;
            p.MistScrollSpeed=info.MistScrollSpeed;
            p.MistBlurSize=info.MistBlurSize;
            p.MistPower=info.MistPower;
            p.SplashNoiseTexture=info.SplashNoiseTexture;
            p.SplashScrollSpeed=info.SplashScrollSpeed;
            p.SplashColor=info.SplashColor;
            p.SplashThreshold=info.SplashThreshold;
            p.SplashBaseRatio=info.SplashBaseRatio;
            p.SplashIntensity=info.SplashIntensity;
            p.SplashDensity=info.SplashDensity;
            p.SplashPower=info.SplashPower;
            p.IsSplashOuter=info.IsSplashOuter;
            p.IsSplashInner=info.IsSplashInner;
            return true;
        }
    }
    public partial class LiveTimelineControl
    {
        public event EdgeStyleDelegate OnUpdateEdgeStyle;
        public void AlterUpdate_EdgeStyle(LiveTimelineWorkSheet sheet,int frame)
        {
            if(OnUpdateEdgeStyle==null)return;
            var keys=sheet.edgeStyleKeys;
            if(keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable)||!keys.EnablePlayModeTimeline(_playMode))return;
            FindTimelineKey(out var current,out var next,keys,frame);if(current==null)return;
            var a=current as LiveTimelineKeyEdgeStyleData;var b=next as LiveTimelineKeyEdgeStyleData;
            var info=LiveEdgeStyleTimeline.SampleAt(a,b,b!=null&&b.IsInterpolateKey()?LiveTransmittedLightTimeline.Interpolation(a,b,frame):0);
            OnUpdateEdgeStyle.Invoke(ref info);
        }
    }
}
