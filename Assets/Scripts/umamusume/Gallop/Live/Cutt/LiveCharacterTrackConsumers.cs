using System;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    [Serializable]
    public class LiveTimelineKeyCharaMotionNoiseData:LiveTimelineKey
    {
        public override LiveTimelineKeyDataType dataType=>LiveTimelineKeyDataType.CharaMotionNoise;
        public float sideChrMotNoiseBaseBias,sideChrMotNoiseRange=.07f,sideChrMotNoiseFrequency=10;
        public float backChrMotNoiseBaseBias,backChrMotNoiseRange=.0875f,backChrMotNoiseFrequency=10;
        public int[] NoiseTypeArray=new int[20];
        public float[] NoiseBaseBiasArray=new float[20],NoiseRangeArray=new float[20],NoiseFrequencyArray=new float[20];
        public bool[] EnableCySpringTailArray=Array.Empty<bool>(),EnableCySpringSkirtArray=Array.Empty<bool>();
        public bool isNegativeCheck;
        public int[] cySpringGravityScaleType=new int[20];
        public float[] cySpringGravityScale=new float[20];
        public string[] cySpringGravityScaleBoneName=new string[20];
        public float[] cySpringExpressionKneeCollisionRadius=new float[20],cySpringExpressionAnkleCollisionRadius=new float[20];
        public float[] cySpringExpressionInfluenceAngle=new float[20],cySpringExpressionInfluenceMaxAngle=new float[20];
        public bool Has(int flag)=>((int)attribute&flag)!=0;
        public bool GetEnableCySpring(int slot,bool perItem,bool tail)
        {
            if((uint)slot>=20)return false;
            if(perItem)return (tail?EnableCySpringTailArray:EnableCySpringSkirtArray)[slot];
            return slot>=5||!Has(1<<((tail?21:26)+slot));
        }
        public bool TryGetNoiseParam(int slot,out float bias,out float range,out float frequency)
        {
            bias=range=frequency=0;
            if((uint)slot>=20)return false;
            switch(NoiseTypeArray[slot])
            {
                case 1:bias=NoiseBaseBiasArray[slot];range=NoiseRangeArray[slot];frequency=NoiseFrequencyArray[slot];break;
                case 2:bias=sideChrMotNoiseBaseBias;range=sideChrMotNoiseRange;frequency=sideChrMotNoiseFrequency;break;
                case 3:bias=backChrMotNoiseBaseBias;range=backChrMotNoiseRange;frequency=backChrMotNoiseFrequency;break;
            }
            bias=Mathf.Max(0,bias);
            return range>1e-6f&&frequency>1e-6f;
        }
    }
    [Serializable]
    public class LiveTimelineKeyCharaMotionNoiseDataList:LiveTimelineKeyDataListTemplate<LiveTimelineKeyCharaMotionNoiseData>
    {
        [SerializeField] bool _usePerItemSettingsForAll;
        [SerializeField] float _offsetTimeFactor=10;
        public bool UsePerItemSettingsForAll {get=>_usePerItemSettingsForAll;set=>_usePerItemSettingsForAll=value;}
        public float OffsetTimeFactor {get=>_offsetTimeFactor;set=>_offsetTimeFactor=value;}
        [NonSerialized] public bool IsInitializedOffsetTime;
    }
    public sealed class LiveMotionNoiseRuntime
    {
        public readonly float[] Initial=new float[20],Time=new float[20],Offset=new float[20];
        float sideBias,sideRange=.07f,sideFrequency=10,backBias,backRange=.0875f,backFrequency=10;
        readonly Func<int,int,int> random;
        readonly Func<float,float> sin;
        public LiveTimelineKeyCharaMotionNoiseData LastKey {get;private set;}
        public Action<LiveTimelineKeyCharaMotionNoiseData,bool> UpdateCySpring;
        public int LastKeyFrame=>LastKey!=null?LastKey.frame:-1;
        public LiveMotionNoiseRuntime(Func<int,int,int> random=null,Func<float,float> sin=null)
        {
            this.random=random??LiveRandomBoundary.RangeInt;this.sin=sin??Mathf.Sin;
            Initialize();
        }
        static float F(float value)=>LiveTimelineControl.MonitorFloat32(value);
        float[] BaseNoise()
        {
            var values=new float[20];
            for(int slot=0;slot<20;slot++)values[slot]=F(slot/19f);
            for(int slot=19;slot>1;slot--)
            {int other=random(1,slot+1);float saved=values[slot];values[slot]=values[other];values[other]=saved;}
            return values;
        }
        void Initialize()
        {
            var values=BaseNoise();
            for(int slot=0;slot<20;slot++)Time[slot]=Initial[slot]=F((slot==0?0:slot<3?sideFrequency:backFrequency)*values[slot]);
        }
        public void Evaluate(LiveTimelineKeyCharaMotionNoiseData key,LiveTimelineKeyCharaMotionNoiseDataList keys,float delta)
        {
            LastKey=key;
            if(keys.UsePerItemSettingsForAll)
            {
                if(key==null){UpdateCySpring?.Invoke(null,true);return;}
                if(!keys.IsInitializedOffsetTime)
                {
                    var values=BaseNoise();
                    for(int slot=0;slot<20;slot++)Time[slot]=Initial[slot]=F(keys.OffsetTimeFactor*values[slot]);
                    keys.IsInitializedOffsetTime=true;
                }
                for(int slot=0;slot<20;slot++)
                {
                    float value=0;
                    if(key.TryGetNoiseParam(slot,out float bias,out float range,out float frequency))
                    {
                        Time[slot]=F(Time[slot]+delta);
                        value=F(F(F(sin(F(F(6.2831855f/frequency)*Time[slot]))+1)*range)+bias);
                        if(!(value>0))value=0;
                    }
                    Offset[slot]=value;
                }
                UpdateCySpring?.Invoke(key,true);
                return;
            }
            if(key!=null&&!key.Has(0x10000))
            {
                if(sideBias!=key.sideChrMotNoiseBaseBias||sideFrequency!=key.sideChrMotNoiseFrequency||
                    backBias!=key.backChrMotNoiseBaseBias||backFrequency!=key.backChrMotNoiseFrequency)
                {
                    sideBias=key.sideChrMotNoiseBaseBias;sideFrequency=key.sideChrMotNoiseFrequency;
                    backBias=key.backChrMotNoiseBaseBias;backFrequency=key.backChrMotNoiseFrequency;Initialize();
                }
                sideRange=key.sideChrMotNoiseRange;backRange=key.backChrMotNoiseRange;
            }
            UpdateCySpring?.Invoke(key,false);
            for(int slot=0;slot<20;slot++)
            {
                float value=0;
                if(slot!=0&&(key==null||!key.Has(0x10000)))
                {
                    Time[slot]=F(Time[slot]+delta);
                    if(slot>=5||key==null||key.Has(1<<(16+slot)))
                    {
                        float frequency=slot<5?sideFrequency:backFrequency;
                        float wave=F(sin(F(F(6.2831855f/frequency)*Time[slot]))*(slot<5?sideRange:backRange));
                        if((key==null||key.isNegativeCheck)&&wave<0)wave=F(wave*-1);
                        value=F((slot<5?sideBias:backBias)+wave);
                        if(value<0)value=0;
                    }
                }
                Offset[slot]=value;
            }
        }
    }
    [Serializable]
    public class LiveTimelineKeyNodeScaleData:LiveTimelineKeyWithInterpolate
    {
        public override LiveTimelineKeyDataType dataType=>LiveTimelineKeyDataType.NodeScale;
        public int characterFlag,targetFlag,sizeType,scaleRatePer;
    }
    [Serializable] public class LiveTimelineKeyNodeScaleDataList:LiveTimelineKeyDataListTemplate<LiveTimelineKeyNodeScaleData>{}
    [Serializable]
    public class LiveTimelineNodeScaleData:ILiveTimelineGroupDataWithName
    {
        public LiveTimelineKeyNodeScaleDataList keys=new LiveTimelineKeyNodeScaleDataList();
        public LiveTimelineNodeScaleData():base("NodeScale"){}
    }
    public struct NodeScaleUpdateInfo
    {
        public int characterFlag,targetFlag;
        public float scaleRate;
    }
    public static class LiveNodeScaleTimeline
    {
        static float Rate(LiveTimelineKeyNodeScaleData key)=>key.sizeType==0?key.scaleRatePer/100f:new[]{0f,1f,.9f}[key.sizeType];
        public static bool Evaluate(LiveTimelineNodeScaleData group,int frame,TimelinePlayerMode mode,out NodeScaleUpdateInfo info)
        {
            info=default;
            var keys=group?.keys;
            if(keys==null||keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable)||!keys.EnablePlayModeTimeline(mode))return false;
            LiveTimelineControl.FindTimelineKey(out var current,out var next,keys,frame);
            if(!(current is LiveTimelineKeyNodeScaleData key))return false;
            int flag=key.characterFlag;
            if(group.name=="NodeScaleCenter")flag=1;
            else if(group.name=="NodeScaleLeft")flag=2;
            else if(group.name=="NodeScaleRight")flag=4;
            else if(group.name=="NodeScaleBack")flag=0xffff8;
            else if(group.name=="NodeScaleOther")flag=0xffffe;
            float rate=Rate(key);
            if(next is LiveTimelineKeyNodeScaleData following&&following.IsInterpolateKey())
                rate=LiveTimelineControl.MonitorFloat32(rate+LiveTimelineControl.MonitorFloat32((Rate(following)-rate)*LiveTimelineControl.CalculateInterpolationValue(key,following,frame)));
            info=new NodeScaleUpdateInfo{characterFlag=flag,targetFlag=key.targetFlag,scaleRate=rate};
            return true;
        }
        public static void Apply(NodeScaleUpdateInfo info,Func<int,Transform[]> nodes)
        {
            float rate=info.scaleRate>0?info.scaleRate:0,inverse=info.scaleRate>0?1f/info.scaleRate:0;
            for(int slot=0;slot<20;slot++)
                if((info.characterFlag&(1<<slot))!=0)
                {
                    var target=nodes(slot);if(target==null)continue;
                    if((info.targetFlag&1)!=0){if(target[0]!=null)target[0].localScale=Vector3.one*rate;if(target[1]!=null)target[1].localScale=Vector3.one*inverse;}
                    if((info.targetFlag&2)!=0){if(target[2]!=null)target[2].localScale=Vector3.one*rate;if(target[3]!=null)target[3].localScale=Vector3.one*inverse;}
                }
        }
    }
}
