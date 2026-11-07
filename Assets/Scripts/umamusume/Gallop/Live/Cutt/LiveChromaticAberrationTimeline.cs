using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Gallop.RenderPipeline;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    [Serializable]
    public class LiveTimelineKeyChromaticAberrationData : LiveTimelineKeyWithInterpolate
    {
        public enum ChromaticAberrationType { Normal=0, Circle=1, CircleClipDirect=2, UVClamp=3 }
        public bool isEnable=true;
        public Vector2 redOffset,greenOffset,blueOffset;
        public float power=1f,clip;
        public ChromaticAberrationType effectType;
        public override LiveTimelineKeyDataType dataType=>LiveTimelineKeyDataType.ChromaticAberration;
    }
    [Serializable]
    public class LiveTimelineKeyChromaticAberrationDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyChromaticAberrationData> { }
    [Serializable]
    public class LiveTimelineChromaticAberrationData : ILiveTimelineGroupDataWithName
    {
        public LiveTimelineKeyChromaticAberrationDataList keys=new LiveTimelineKeyChromaticAberrationDataList();
        public LiveTimelineChromaticAberrationData():base("Chromatic Aberration") { }
        public override ILiveTimelineKeyDataList GetKeyList()=>keys;
    }
    public struct ChromaticAberrationUpdateInfo
    {
        public bool isEnable;
        public Vector2 redOffset,greenOffset,blueOffset;
        public float power,clip;
        public LiveTimelineKeyChromaticAberrationData.ChromaticAberrationType type;
    }
    public delegate void ChromaticAberrationUpdateInfoDelegate(ref ChromaticAberrationUpdateInfo updateInfo);
    public partial class LiveTimelineWorkSheet
    {
        [SerializeField] public List<LiveTimelineChromaticAberrationData> chromaticAberrationList;
    }
    public static class LiveChromaticAberrationTimeline
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        static float F(float value)=>BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(value));
        static float L(float a,float b,float t)=>F(F(F(b-a)*t)+a);
        static Vector2 L(Vector2 a,Vector2 b,float t)=>new Vector2(L(a.x,b.x,t),L(a.y,b.y,t));
        // Original parent4ecbfc0 interpolates only the eight numeric fields;
        // enable/type remain current even when the next key requests a blend.
        public static ChromaticAberrationUpdateInfo SampleAt(LiveTimelineKeyChromaticAberrationData a,LiveTimelineKeyChromaticAberrationData b,float t)
        {
            if(a==null)throw new NullReferenceException("Original Chromatic current key");
            bool blend=b!=null&&b.IsInterpolateKey();
            return new ChromaticAberrationUpdateInfo {
                isEnable=a.isEnable,type=a.effectType,
                redOffset=blend?L(a.redOffset,b.redOffset,t):a.redOffset,
                greenOffset=blend?L(a.greenOffset,b.greenOffset,t):a.greenOffset,
                blueOffset=blend?L(a.blueOffset,b.blueOffset,t):a.blueOffset,
                power=blend?L(a.power,b.power,t):a.power,
                clip=blend?L(a.clip,b.clip,t):a.clip
            };
        }
        // Original Director4e2dce0 + SetOffset52160a0 + power/clip setters.
        // Preserve both offset.w, MixParameter.xy and authored pass event.
        public static void Apply(ref ChromaticAberrationPass.Parameter p,ref ChromaticAberrationUpdateInfo info)
        {
            p.IsEnable=info.isEnable;
            p.ColorOffsetX.x=F(info.redOffset.x*.01f);
            p.ColorOffsetX.y=F(info.greenOffset.x*.01f);
            p.ColorOffsetX.z=F(info.blueOffset.x*.01f);
            p.ColorOffsetY.x=F(info.redOffset.y*.01f);
            p.ColorOffsetY.y=F(info.greenOffset.y*.01f);
            p.ColorOffsetY.z=F(info.blueOffset.y*.01f);
            p.MixParameter.z=info.power;p.MixParameter.w=info.clip;p.Type=(int)info.type;
        }
    }
    public partial class LiveTimelineControl
    {
        public event ChromaticAberrationUpdateInfoDelegate OnUpdateChromaticAberration;
        public void AlterUpdate_ChromaticAberration(LiveTimelineWorkSheet sheet,int frame)
        {
            if(OnUpdateChromaticAberration==null)return;
            if(sheet==null||sheet.chromaticAberrationList==null)throw new NullReferenceException("Original Chromatic group list");
            foreach(var group in sheet.chromaticAberrationList)
            {
                if(group==null||group.keys==null)throw new NullReferenceException("Original Chromatic group keys");
                var keys=group.keys;
                if(keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable)||!keys.EnablePlayModeTimeline(_playMode))continue;
                FindTimelineKey(out var current,out var next,keys,frame);
                if(current==null)continue;
                var a=current as LiveTimelineKeyChromaticAberrationData;
                var b=next as LiveTimelineKeyChromaticAberrationData;
                if(a==null)throw new InvalidCastException("Original Chromatic current key type");
                float t=b!=null&&b.IsInterpolateKey()?CalculateInterpolationValue(a,b,frame):0f;
                var info=LiveChromaticAberrationTimeline.SampleAt(a,b,t);
                OnUpdateChromaticAberration(ref info);
            }
        }
    }
}
