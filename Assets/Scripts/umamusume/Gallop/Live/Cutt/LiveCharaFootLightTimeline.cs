using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    [Serializable] public class LiveTimelineKeyCharaFootLightData:LiveTimelineKeyWithInterpolate
    {
        public override LiveTimelineKeyDataType dataType=>LiveTimelineKeyDataType.CharaFootLight;
        public LiveCharaPositionFlag positionFlag;
        public float[] hightMax=new float[20];
        public Color[] lightColor=new Color[20];
        public LiveDefine.HightLightBlendMode[] LightBlendModeArray=new LiveDefine.HightLightBlendMode[20];
        public LiveDefine.Easing[] EasingArray=new LiveDefine.Easing[20];
        static void Resize<T>(ref T[] array,T fill)
        {
            int count=array!=null?array.Length:0;if(count==20)return;
            Array.Resize(ref array,20);for(int i=count;i<20;i++)array[i]=fill;
        }
        public override void OnLoad(LiveTimelineControl control)
        {
            // Native 0x4f08df0 preserves existing entries and supplies defaults
            // only for new positions when bringing all four arrays to size20.
            Resize(ref hightMax,1f);Resize(ref lightColor,Color.clear);
            Resize(ref LightBlendModeArray,default(LiveDefine.HightLightBlendMode));Resize(ref EasingArray,default(LiveDefine.Easing));
        }
    }
    [Serializable] public class LiveTimelineKeyCharaFootLightDataList:LiveTimelineKeyDataListTemplate<LiveTimelineKeyCharaFootLightData>{}
    [Serializable] public struct CharaFootLightUpdateInfo
    {
        public int CharacterIndex;
        public float hightMax;
        public Color lightColor;
        public LiveDefine.HightLightBlendMode LightBlendMode;
        public LiveDefine.Easing Easing;
        public static CharaFootLightUpdateInfo Default(int index)=>new CharaFootLightUpdateInfo{CharacterIndex=index,hightMax=1f,lightColor=Color.clear};
    }
    public static class LiveCharaFootLightTimeline
    {
        [MethodImpl(MethodImplOptions.NoInlining)] static float F(float x)=>BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(x));
        static float L(float a,float b,float t)=>F(a+F(F(b-a)*t));
        public static void Evaluate(LiveTimelineKeyCharaFootLightDataList keys,int frame,Action<CharaFootLightUpdateInfo> emit)
        {
            if(keys==null||emit==null)return;
            if(keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable))
            {for(int i=0;i<20;i++)emit(CharaFootLightUpdateInfo.Default(i));return;}
            // Original 0x4ec2030 has no play-mode gate. Disabled lists reset all
            // 20 positions; a selector with no current key leaves prior state.
            LiveTimelineControl.FindTimelineKey(out var current,out var next,keys,frame);
            var a=current as LiveTimelineKeyCharaFootLightData;var b=next as LiveTimelineKeyCharaFootLightData;
            if(a==null)return;
            bool interpolate=b!=null&&b.IsInterpolateKey();
            for(int i=0;i<20;i++)
            {
                var info=CharaFootLightUpdateInfo.Default(i);
                if(((int)a.positionFlag&(1<<i))!=0)
                {
                    float t=interpolate?LiveTimelineControl.CalculateInterpolationValue(a,b,frame):0;
                    info.hightMax=interpolate?L(a.hightMax[i],b.hightMax[i],t):a.hightMax[i];
                    Color color=a.lightColor[i];
                    if(interpolate){var end=b.lightColor[i];color=new Color(L(color.r,end.r,t),L(color.g,end.g,t),L(color.b,end.b,t),L(color.a,end.a,t));}
                    info.lightColor=color;info.LightBlendMode=a.LightBlendModeArray[i];info.Easing=a.EasingArray[i];
                }
                emit(info);
            }
        }
    }
    public partial class LiveTimelineControl
    {
        public event Action<CharaFootLightUpdateInfo> OnUpdateCharaFootLight;
        internal void AlterLateUpdate_CharaFootLight(LiveTimelineWorkSheet sheet,int frame)
            =>LiveCharaFootLightTimeline.Evaluate(sheet.charaFootLightKeys,frame,OnUpdateCharaFootLight);
    }
}
