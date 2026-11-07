using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    [Serializable]
    public class LiveTimelineKeyFacialNoiseData : LiveTimelineKey
    {
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.FacialNoise;
        public int EnableCharacterBitFlag;
        public bool IsNoiseDisableAll() => ((int)attribute & 0x10000) != 0;
        public bool IsNoiseEnableLeft() => ((int)attribute & 0x20000) != 0;
        public bool IsNoiseEnableRight() => ((int)attribute & 0x40000) != 0;
        public bool IsNoiseEnable4th() => ((int)attribute & 0x80000) != 0;
        public bool IsNoiseEnable5th() => ((int)attribute & 0x100000) != 0;
        public bool IsCharacterNoiseEnable(int index) => (EnableCharacterBitFlag & (1 << index)) != 0;
    }
    [Serializable] public class LiveTimelineKeyFacialNoiseDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyFacialNoiseData> { }
    [Serializable]
    public class LiveTimelineKeyTailMotionData : LiveTimelineKey
    {
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.TailMotion;
        public bool UseTailRandomMotion;
        public int MotionIndex;
        public float PlaySpeed = 1f;
        public bool IsTimescaleDisabled;
    }
    [Serializable]
    public class LiveTimelineKeyTailMotionDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyTailMotionData>
    {
        [NonSerialized] public int NativePreviousFrame;
        [NonSerialized] public float NativeFrameAnimationTime;
        // Original getter collects authored candidates; it consumes no RNG.
        public HashSet<int> GetRandomTailMotionIndex()
        {
            var result = new HashSet<int>();
            foreach(var key in thisList)if(key != null && key.UseTailRandomMotion)result.Add(key.MotionIndex);
            return result;
        }
    }
    public partial class LiveTimelineWorkSheet
    {
        [SerializeField] public LiveTimelineKeyFacialNoiseDataList facialNoiseKeys;
        [SerializeField] public LiveTimelineKeyFacialEyeTrackDataList other4EyeTrackKeys;
    }
    public partial class LiveTimelineControl
    {
        float _facialNoiseLimitTimeNative = .5f;
        public float facialNoiseLimitTime
        {
            get => _facialNoiseLimitTimeNative;
            set
            {
                float previous = _facialNoiseLimitTimeNative;
                _facialNoiseLimitTimeNative = value;
                // Native ucomiss also reinitializes unordered (NaN) values.
                if (value != previous) InitializeFacialNoiseNative();
            }
        }
        readonly float[] _facialNoiseOffsetsNative = new float[20];
        readonly int[] _facialNoiseFramesNative = new int[20];
        float _facialNoiseTimeNative;
        public IReadOnlyList<float> FacialNoiseOffsetsNative => _facialNoiseOffsetsNative;
        public IReadOnlyList<int> FacialNoiseFramesNative => _facialNoiseFramesNative;
        // Root calls after Data.OnLoad, before InitializeCharacterConsumers,
        // whose MotionNoise constructor consumes the next distinct 18 draws.
        public void InitializeFacialNoiseNative(Func<int,int,int> random = null)
        {
            random = random ?? LiveRandomBoundary.RangeInt;
            var values = new float[20];
            for(int i=0;i<20;i++)
            {
                float v=MonitorFloat32(MonitorFloat32(i/19f)-.5f);
                values[i]=MonitorFloat32(MonitorFloat32(v+v)*facialNoiseLimitTime);
            }
            for(int i=19;i>1;i--)
            {
                int j=random(0,i+1);float value=values[i];values[i]=values[j];values[j]=value;
            }
            values[0]=0f;Array.Copy(values,_facialNoiseOffsetsNative,20);
        }
        // Root calls in the Update clock prelude, including disabled-sheet
        // updates. This must precede every facial sampler, without more RNG.
        public void AdvanceFacialNoiseClockNative(float time)
        {
            _facialNoiseTimeNative=time;
            for(int i=0;i<20;i++)
                _facialNoiseFramesNative[i]=unchecked((int)System.Math.Floor((double)MonitorFloat32(MonitorFloat32(_facialNoiseOffsetsNative[i]+time)*60f)));
        }
        public float GetFacialNoiseCurrentTimeNative(int physicalSlot,Func<int,int,int> random = null)
        {
            int index=physicalSlot;
            if(index>=20)index=(random??LiveRandomBoundary.RangeInt)(0,20);
            return MonitorFloat32(_facialNoiseTimeNative+_facialNoiseOffsetsNative[index]);
        }
        public int GetFacialNoiseNative(LiveTimelineKeyFacialNoiseData key,int currentFrame,int physicalSlot)
        {
            int mode=(int)data.FacialLineType;
            if(mode==0)
            {
                if(physicalSlot==0)return currentFrame;
                if(physicalSlot<5 && ((int)key.attribute&(1<<(physicalSlot+16)))==0)return currentFrame;
                return _facialNoiseFramesNative[physicalSlot];
            }
            return mode==1 && key.IsCharacterNoiseEnable(physicalSlot)?_facialNoiseFramesNative[physicalSlot]:currentFrame;
        }
        public LiveTimelineFacialData GetFacialDataNative(LiveTimelineWorkSheet sheet,int physicalSlot)
        {
            int mode=(int)data.FacialLineType;
            if(mode==0)
            {
                var indices=data.characterSettings.motionSequenceIndices;
                if(physicalSlot>=indices.Length)return null;
                int set=indices[physicalSlot];
                if(set==0)return sheet.facial1Set;
                if(set<1||set>19)return null;
                return sheet.other4FacialArray[set-1];
            }
            if(mode==1)return physicalSlot==0?sheet.facial1Set:sheet.other4FacialArray[physicalSlot-1];
            return null;
        }
        bool FacialNativeEnabled(ILiveTimelineKeyDataList keys)
        {
            if(keys==null)throw new NullReferenceException("Original facial key list");
            return !keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable)&&keys.EnablePlayModeTimeline(_playMode);
        }
        FindKeyResult FacialNativePick(ILiveTimelineKeyDataList keys,int frame)=>keys.FindKeyCached(frame,true);
        void FacialNativeFace(ref FacialDataUpdateInfo info,LiveTimelineKeyFacialFaceDataList keys,int frame)
        {
            if(!FacialNativeEnabled(keys)){info.facePrev=info.faceCur=info.faceNext=null;return;}
            var pick=FacialNativePick(keys,frame);info.faceCur=pick.key as LiveTimelineKeyFacialFaceData;
            info.faceNext=keys.At(pick.index+1)as LiveTimelineKeyFacialFaceData;info.facePrev=keys.At(pick.index-1)as LiveTimelineKeyFacialFaceData;info.faceKeyIndex=pick.index;
        }
        void FacialNativeMouth(ref FacialDataUpdateInfo info,LiveTimelineKeyFacialMouthDataList keys,int frame)
        {
            if(!FacialNativeEnabled(keys)){info.mouth=info.mouthPrev=info.mouthCur=info.mouthNext=null;return;}
            var pick=FacialNativePick(keys,frame);info.mouth=info.mouthCur=pick.key as LiveTimelineKeyFacialMouthData;info.mouthKeyIndex=pick.index;
            info.mouthPrev=keys.At(pick.index-1)as LiveTimelineKeyFacialMouthData;info.mouthNext=keys.At(pick.index+1)as LiveTimelineKeyFacialMouthData;
        }
        void FacialNativeEye(ref FacialDataUpdateInfo info,LiveTimelineKeyFacialEyeDataList keys,int frame)
        {
            if(!FacialNativeEnabled(keys)){info.eyePrev=info.eyeCur=info.eyeNext=null;return;}
            var pick=FacialNativePick(keys,frame);info.eyeCur=pick.key as LiveTimelineKeyFacialEyeData;info.eyeKeyIndex=pick.index;
            info.eyePrev=keys.At(pick.index-1)as LiveTimelineKeyFacialEyeData;info.eyeNext=keys.At(pick.index+1)as LiveTimelineKeyFacialEyeData;
        }
        void FacialNativeEyebrow(ref FacialDataUpdateInfo info,LiveTimelineKeyFacialEyebrowDataList keys,int frame)
        {
            if(!FacialNativeEnabled(keys)){info.eyebrow=info.eyebrowPrev=info.eyebrowCur=info.eyebrowNext=null;return;}
            var pick=FacialNativePick(keys,frame);info.eyebrow=info.eyebrowCur=pick.key as LiveTimelineKeyFacialEyebrowData;info.eyebrowKeyIndex=pick.index;
            info.eyebrowPrev=keys.At(pick.index-1)as LiveTimelineKeyFacialEyebrowData;info.eyebrowNext=keys.At(pick.index+1)as LiveTimelineKeyFacialEyebrowData;
        }
        void FacialNativeEar(ref FacialDataUpdateInfo info,LiveTimelineKeyFacialEarDataList keys,int frame)
        {
            if(!FacialNativeEnabled(keys)){info.ear=info.earPrev=info.earCur=info.earNext=null;return;}
            var pick=FacialNativePick(keys,frame);info.ear=info.earCur=pick.key as LiveTimelineKeyFacialEarData;info.earKeyIndex=pick.index;
            info.earPrev=keys.At(pick.index-1)as LiveTimelineKeyFacialEarData;info.earNext=keys.At(pick.index+1)as LiveTimelineKeyFacialEarData;
        }
        void FacialNativeEffect(ref FacialDataUpdateInfo info,LiveTimelineKeyFacialEffectDataList keys,int frame)
        {
            if(!FacialNativeEnabled(keys)){info.effect=null;return;}
            var pick=FacialNativePick(keys,frame);info.effect=pick.key as LiveTimelineKeyFacialEffectData;info.effectKeyIndex=pick.index;
        }
        void FacialNativeEyeTrack(ref FacialDataUpdateInfo info,LiveTimelineKeyFacialEyeTrackDataList keys,int frame)
        {
            if(!FacialNativeEnabled(keys)){info.eyeTrack=info.eyeTrackPrev=info.eyeTrackCur=info.eyeTrackNext=null;return;}
            var pick=FacialNativePick(keys,frame);info.eyeTrack=info.eyeTrackCur=pick.key as LiveTimelineKeyFacialEyeTrackData;info.eyeTrackKeyIndex=pick.index;
            info.eyeTrackPrev=keys.At(pick.index-1)as LiveTimelineKeyFacialEyeTrackData;info.eyeTrackNext=keys.At(pick.index+1)as LiveTimelineKeyFacialEyeTrackData;
        }
        void FacialNativeTail(ref FacialDataUpdateInfo info,LiveTimelineKeyTailMotionDataList keys,int frame,LiveTimelineKeyTimescaleDataList timescaleKeys)
        {
            if(!FacialNativeEnabled(keys)){info.tail=null;return;}
            var pick=FacialNativePick(keys,frame);info.tail=pick.key as LiveTimelineKeyTailMotionData;info.tailKeyIndex=pick.index;
            if(info.tail!=null&&info.tail.UseTailRandomMotion)
            {
                const float spf=.0166666675f;
                int current=unchecked((int)MonitorFloat32(_facialNoiseTimeNative/spf));
                NativeLiveMotionTime.Calculate(out info.tailMotionSec,out keys.NativeFrameAnimationTime,
                    info.tail.frame,info.tail.PlaySpeed,0,info.tail.IsTimescaleDisabled,spf,current,_facialNoiseTimeNative,
                    timescaleKeys,keys.NativePreviousFrame,keys.NativeFrameAnimationTime);
                keys.NativePreviousFrame=current;
            }
        }
        // Complete original 0x4ecf250 control flow. Original consumers receive
        // canonical fields; compatibility Prev/Cur/Next fields support Viewer
        // FaceDrivenKeyTarget without claiming its downstream blending parity.
        public void AlterUpdate_FacialDataNative(LiveTimelineWorkSheet sheet,int currentFrame)
        {
            if(OnUpdateFacial==null)return;
            var info=default(FacialDataUpdateInfo);
            if(sheet.other4EyeTrackKeys!=null)FacialNativeEyeTrack(ref info,sheet.other4EyeTrackKeys,currentFrame);
            var noise=sheet.facialNoiseKeys.FindKeyCached(currentFrame,true).key as LiveTimelineKeyFacialNoiseData;
            for(int slot=0;slot<20;slot++)
            {
                var facial=GetFacialDataNative(sheet,slot);if(facial==null)continue;
                int frame=noise!=null&&!noise.IsNoiseDisableAll()?GetFacialNoiseNative(noise,currentFrame,slot):currentFrame;
                FacialNativeFace(ref info,facial.faceKeys,frame);FacialNativeMouth(ref info,facial.mouthKeys,frame);
                FacialNativeEye(ref info,facial.eyeKeys,frame);FacialNativeEyebrow(ref info,facial.eyebrowKeys,frame);
                FacialNativeEar(ref info,facial.earKeys,frame);FacialNativeEffect(ref info,facial.effectKeys,frame);FacialNativeTail(ref info,facial.tailMotionKeys,frame,sheet.timescaleKeys);
                if(slot==0)FacialNativeEyeTrack(ref info,facial.eyeTrackKeys,frame);
                else
                {
                    info.eyeTrack=info.eyeTrackCur=null;
                    var own=facial.eyeTrackKeys.FindKeyCached(frame,true).key;
                    if(own==null||((int)own.attribute&0x10000)!=0)
                    {
                        if(sheet.other4EyeTrackKeys!=null)FacialNativeEyeTrack(ref info,sheet.other4EyeTrackKeys,currentFrame);
                    }
                    else FacialNativeEyeTrack(ref info,facial.eyeTrackKeys,frame);
                }
                OnUpdateFacial(info,GetFacialNoiseCurrentTimeNative(slot),slot);
            }
        }
    }
}
