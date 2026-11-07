using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live
{
    public interface ILiveMiniColorPlayer
    {
        void SetCharaColor(Color color);
        void SetOutlineColor(Color color,Cutt.LiveTimelineDefine.OutlineColorBlend blend);
        void SetOutlineWidthForPower(float power);
        void UpdateMaterialPropertyBlock();
    }
}
namespace Gallop.Live.Cutt
{
    [Serializable] public sealed class LiveTimelineKeyMiniCharaColorData : LiveTimelineKeyWithInterpolate
    {
        public int TargetFlags;
        public LiveTimelineDefine.ColorType ColorType;
        public Color Color=Color.white;
        public float ColorPower=1f,OutlineWidthPower=1f;
        // Original StaticVariableDefine.CG3D.ModelController ctor:0x9b000c20.
        public Color OutlineColor=new Color(32f/255f,12f/255f,0f,155f/255f);
        public LiveTimelineDefine.OutlineColorBlend OutlineColorBlend;
        public bool IsSyncBlinkLight;
        public string BlinkLightName=string.Empty;
        public int BlinkLightNameHash,BlinkLightContainerIndex;
        public float BlinkLightBrightnessPower=1f;
        public bool IsAdjustedBlinkLightColor=true;
        [SerializeField] LiveTimelineKeyLoopType _loopType;
        [SerializeField] int _loopCount,_loopExecutedCount,_loopIntervalFrame;
        [SerializeField] bool _isChangeLoopInterpolate=true,_isPasteLoopUnit=true;
        public override LiveTimelineKeyDataType dataType=>LiveTimelineKeyDataType.MiniCharaColor;
    }
    [Serializable] public sealed class LiveTimelineKeyMiniCharaColorDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyMiniCharaColorData> { }
    [Serializable] public sealed class LiveTimelineMiniCharaColorData : ILiveTimelineGroupDataWithName
    {
        public LiveTimelineKeyMiniCharaColorDataList keys=new LiveTimelineKeyMiniCharaColorDataList();
        public LiveTimelineMiniCharaColorData():base("MiniCharaColor") { }
        public override ILiveTimelineKeyDataList GetKeyList()=>keys;
    }
    public partial class LiveTimelineWorkSheet {[SerializeField] public List<LiveTimelineMiniCharaColorData> MiniCharaColorDataList;}
    public partial class LiveTimelineControl
    {
        public delegate bool MiniBlinkColorResolver(int hash,int container,float brightness,bool adjusted,out Color color);
        public Func<int,ILiveMiniColorPlayer> MiniColorPlayerResolver {get;set;}
        public Func<LiveTimelineDefine.ColorType,int,Color?> MiniCharacterColorResolver {get;set;}
        public MiniBlinkColorResolver MiniBlinkLightResolver {get;set;}
        static float MiniColorLerp(float a,float b,float t)=>MonitorFloat32(MonitorFloat32(MonitorFloat32(b-a)*t)+a);
        static Color MiniColorLerp(Color a,Color b,float t)=>new Color(MiniColorLerp(a.r,b.r,t),MiniColorLerp(a.g,b.g,t),MiniColorLerp(a.b,b.b,t),MiniColorLerp(a.a,b.a,t));
        static Color MiniColorPower(Color a,float power)=>new Color(MonitorFloat32(a.r*power),MonitorFloat32(a.g*power),MonitorFloat32(a.b*power),MonitorFloat32(a.a*power));
        Color MiniConvertColor(LiveTimelineDefine.ColorType type,int index,Color direct)=>type==LiveTimelineDefine.ColorType.Direct?direct:(MiniCharacterColorResolver?.Invoke(type,index)??direct);
        Color MiniGetColor(LiveTimelineKeyMiniCharaColorData a,LiveTimelineKeyMiniCharaColorData b,float t,int index)
        {
            var raw=a.Color;var color=MiniConvertColor(a.ColorType,index,raw);var result=MiniColorPower(color,a.ColorPower);if(t<=0f)return result;
            int nextIndex=(b.TargetFlags&(1<<index))!=0?index:-1;var nextColor=MiniConvertColor(b.ColorType,nextIndex,b.IsSyncBlinkLight?raw:b.Color);float blend=Mathf.Clamp01(t);return MiniColorPower(MiniColorLerp(color,nextColor,blend),MiniColorLerp(a.ColorPower,b.ColorPower,blend));
        }
        // Original4ed6e70. Resolvers preserve the named native model/master/
        // Blink boundaries; actual two-key corpus is Direct/no Blink/no blend.
        public void AlterUpdate_MiniCharaColor(LiveTimelineWorkSheet sheet,int frame,float currentTime)
        {
            if(sheet.MiniCharaColorDataList==null)return;
            foreach(var group in sheet.MiniCharaColorDataList)
            {
                var keys=group.keys;if(keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable)||!keys.EnablePlayModeTimeline(_playMode))continue;
                var found=keys.FindKeyCached(frame,true);if(found.key==null)continue;var a=(LiveTimelineKeyMiniCharaColorData)found.key;var b=keys.At(found.index+1)as LiveTimelineKeyMiniCharaColorData;float t=0f,width=a.OutlineWidthPower;Color outline=a.OutlineColor;
                if(b!=null&&b.IsInterpolateKey()){t=CalculateInterpolationValue(a,b,frame);width=MiniColorLerp(width,b.OutlineWidthPower,t);outline=MiniColorLerp(outline,b.OutlineColor,t);}
                Color blink=Color.white;bool hasBlink=a.IsSyncBlinkLight&&MiniBlinkLightResolver!=null&&MiniBlinkLightResolver(a.BlinkLightNameHash,a.BlinkLightContainerIndex,a.BlinkLightBrightnessPower,a.IsAdjustedBlinkLightColor,out blink);
                for(int i=0;i<20;i++)if((a.TargetFlags&(1<<i))!=0)
                {
                    var player=MiniColorPlayerResolver?.Invoke(i);if(player==null)continue;player.SetCharaColor(hasBlink?blink:MiniGetColor(a,b,t,i));player.SetOutlineColor(outline,a.OutlineColorBlend);player.SetOutlineWidthForPower(width);player.UpdateMaterialPropertyBlock();
                }
            }
        }
    }
}
