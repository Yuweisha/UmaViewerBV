using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    [Serializable]
    public class LiveTimelineKeyWaveObjectData : LiveTimelineKeyWithInterpolate
    {
        public bool IsWorldDir;
        public Vector3 WaveDir=Vector3.right;
        public float WaveFreq,WaveSpeed,WaveSize;
        public override LiveTimelineKeyDataType dataType=>LiveTimelineKeyDataType.WaveObject;
    }
    [Serializable] public class LiveTimelineKeyWaveObjectDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyWaveObjectData> { }
    [Serializable]
    public class LiveTimelineWaveObjectData : ILiveTimelineGroupDataWithName
    {
        public LiveTimelineKeyWaveObjectDataList keys=new LiveTimelineKeyWaveObjectDataList();
        public LiveTimelineWaveObjectData():base("Wave Object") { }
        public override ILiveTimelineKeyDataList GetKeyList()=>keys;
    }
    public struct WaveObjectUpdateInfo
    {
        public int TimelineNameHash,KeyIndex;
        public bool IsWorldDir;
        public Vector3 WaveDir;
        public float WaveFreq,WaveSpeed,WaveSize;
    }
    public delegate void WaveObjectUpdateInfoDelegate(ref WaveObjectUpdateInfo info);
    public partial class LiveTimelineWorkSheet
    {
        [SerializeField] public List<LiveTimelineWaveObjectData> waveObjectList;
    }
    public partial class LiveTimelineControl
    {
        public event WaveObjectUpdateInfoDelegate OnUpdateWaveObject;
        static float WaveLerp(float a,float b,float t)=>MonitorFloat32(MonitorFloat32(MonitorFloat32(b-a)*t)+a);
        public void AlterUpdate_WaveObject(LiveTimelineWorkSheet sheet,int frame)
        {
            if(OnUpdateWaveObject==null)return;
            foreach(var group in sheet.waveObjectList)
            {
                var keys=group.keys;
                if(keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable)||!keys.EnablePlayModeTimeline(_playMode))continue;
                var found=keys.FindKeyCached(frame,true);
                if(found.key==null)continue;
                var current=(LiveTimelineKeyWaveObjectData)found.key;var next=keys.At(found.index+1)as LiveTimelineKeyWaveObjectData;
                var info=new WaveObjectUpdateInfo{TimelineNameHash=group.nameHash,KeyIndex=found.index,IsWorldDir=current.IsWorldDir,WaveDir=current.WaveDir,WaveFreq=current.WaveFreq,WaveSpeed=current.WaveSpeed,WaveSize=current.WaveSize};
                if(next!=null&&next.IsInterpolateKey())
                {
                    float t=CalculateInterpolationValue(current,next,frame);info.KeyIndex=-1;
                    info.WaveDir=new Vector3(WaveLerp(current.WaveDir.x,next.WaveDir.x,t),WaveLerp(current.WaveDir.y,next.WaveDir.y,t),WaveLerp(current.WaveDir.z,next.WaveDir.z,t));
                    info.WaveFreq=WaveLerp(current.WaveFreq,next.WaveFreq,t);info.WaveSpeed=WaveLerp(current.WaveSpeed,next.WaveSpeed,t);info.WaveSize=WaveLerp(current.WaveSize,next.WaveSize,t);
                }
                OnUpdateWaveObject(ref info);
            }
        }
    }
}
