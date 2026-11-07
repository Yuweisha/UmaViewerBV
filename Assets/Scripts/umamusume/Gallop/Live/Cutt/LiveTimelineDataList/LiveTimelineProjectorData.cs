using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    [Serializable]
    public class LiveTimelineKeyProjectorData : LiveTimelineKeyWithInterpolate
    {
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.Projector;
        public int motionID, materialID;
        public float speed=1f;
        public Color color1=Color.white;
        public float power=1f;
        public Vector3 position;
        public float rotate;
        public Vector2 size=Vector2.one;
        public LiveDefine.LightBlendMode LightBlendMode;
        public LiveTimelineKeyLoopType loopType;
        public int loopCount, loopExecutedCount, loopIntervalFrame;
        public bool isPasteLoopUnit=true, isChangeLoopInterpolate=true;
        public bool IsAnimContinuous() => ((int)attribute & 0x10000)!=0;
    }
    [Serializable] public class LiveTimelineKeyProjectorDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyProjectorData> {}
    [Serializable]
    public class LiveTimelineProjectorData : ILiveTimelineGroupDataWithName
    {
        public LiveTimelineKeyProjectorDataList keys=new LiveTimelineKeyProjectorDataList();
        public LiveTimelineProjectorData():base("Projector") {}
        public override ILiveTimelineKeyDataList GetKeyList()=>keys;
    }
    [StructLayout(LayoutKind.Sequential,Pack=4)]
    public struct ProjectorUpdateInfo
    {
        public float progressTime;
        public int nameHash, motionId, materialId;
        public float speed;
        public Color color1;
        public float power;
        public Vector3 position;
        public float rotate;
        public Vector2 size;
        public LiveDefine.LightBlendMode LightBlendMode;
    }
    public delegate void ProjectorUpdateInfoDelegate(ref ProjectorUpdateInfo info);
    public partial class LiveTimelineWorkSheet
    { [SerializeField] public List<LiveTimelineProjectorData> projecterList; }
}
