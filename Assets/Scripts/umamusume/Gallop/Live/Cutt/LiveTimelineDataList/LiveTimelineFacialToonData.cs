using System;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    [Serializable]
    public class LiveTimelineKeyFacialToonData : LiveTimelineKeyWithInterpolate
    {
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.FacialToon;
        public float CheekPretenseThreshold;
        public float NosePretenseThreshold;
        public float CylinderBlend;
        public float HairNormalBlend;
        public LiveTimelineDefine.FacialToonLightType UseOriginalDirectionalLight;
        public Vector3 OriginalDirectionalLightDir;
        public float EyeToonStep;
        public float EyeToonFeather;
        public float EyeSaturation;
    }
    [Serializable]
    public class LiveTimelineKeyFacialToonDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyFacialToonData> { }
    [Serializable]
    public class LiveTimelineFacialToonData
    {
        public LiveTimelineKeyFacialToonDataList centerKeys, left1Keys, right1Keys, left2Keys, right2Keys;
        public LiveTimelineKeyFacialToonDataList motion5Keys, motion6Keys, motion7Keys, motion8Keys, motion9Keys;
        public LiveTimelineKeyFacialToonDataList motion10Keys, motion11Keys, motion12Keys, motion13Keys, motion14Keys;
        public LiveTimelineKeyFacialToonDataList motion15Keys, motion16Keys, motion17Keys, motion18Keys, motion19Keys;
        [NonSerialized] ILiveTimelineKeyDataList[] _cacheDataList;
        public ILiveTimelineKeyDataList[] GetKeyListArray()
        {
            if(_cacheDataList == null) _cacheDataList = new ILiveTimelineKeyDataList[] {
                centerKeys,left1Keys,right1Keys,left2Keys,right2Keys,
                motion5Keys,motion6Keys,motion7Keys,motion8Keys,motion9Keys,
                motion10Keys,motion11Keys,motion12Keys,motion13Keys,motion14Keys,
                motion15Keys,motion16Keys,motion17Keys,motion18Keys,motion19Keys };
            return _cacheDataList;
        }
    }
    public struct FacialToonUpdateInfo
    {
        public LiveCharaPositionFlag CharacterPos;
        public float CheekPretenseThreshold, NosePretenseThreshold, CylinderBlend, HairNormalBlend;
        public LiveTimelineDefine.FacialToonLightType UseOriginalDirectionalLight;
        public Vector3 OriginalDirectionalLightDir;
        public float EyeToonStep, EyeToonFeather, EyeSaturation;
    }
    public delegate void FacialToonUpdateInfoDelegate(ref FacialToonUpdateInfo updateInfo);
    public partial class LiveTimelineWorkSheet
    {
        [SerializeField] public LiveTimelineFacialToonData facialToonSet;
    }
    public partial class LiveTimelineControl
    {
        public event FacialToonUpdateInfoDelegate OnUpdateFacialToon;
        static float FacialToonLerp(float a,float b,float t) => MonitorFloat32(MonitorFloat32(MonitorFloat32(b-a)*t)+a);
        // Original parent 4ecf8f0; original order CameraMotion -> FacialToon -> AdditionalLight in 4ee8610.
        private void AlterUpdate_FacialToon(LiveTimelineWorkSheet sheet,int frame)
        {
            if(sheet==null || sheet.facialToonSet==null || OnUpdateFacialToon==null)return;
            var lists=sheet.facialToonSet.GetKeyListArray();
            var indices=data.characterSettings.motionSequenceIndices;
            for(int motionSlot=0;motionSlot<lists.Length;motionSlot++)
            {
                var keys=lists[motionSlot];
                if(keys==null || keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) || !keys.EnablePlayModeTimeline(_playMode) || keys.Count<=0)continue;
                FindTimelineKey(out var currentBase,out var nextBase,keys,frame);
                if(!(currentBase is LiveTimelineKeyFacialToonData current))continue;
                var next=nextBase as LiveTimelineKeyFacialToonData;
                uint mask=0;
                for(int physicalSlot=0;physicalSlot<indices.Length;physicalSlot++)if(indices[physicalSlot]==motionSlot)mask|=1u<<(physicalSlot&31);
                FacialToonUpdateInfo info=new FacialToonUpdateInfo {
                    CharacterPos=(LiveCharaPositionFlag)mask,
                    CheekPretenseThreshold=current.CheekPretenseThreshold,NosePretenseThreshold=current.NosePretenseThreshold,
                    CylinderBlend=current.CylinderBlend,HairNormalBlend=current.HairNormalBlend,
                    UseOriginalDirectionalLight=current.UseOriginalDirectionalLight,OriginalDirectionalLightDir=current.OriginalDirectionalLightDir,
                    EyeToonStep=current.EyeToonStep,EyeToonFeather=current.EyeToonFeather,EyeSaturation=current.EyeSaturation };
                if(next!=null && next.IsInterpolateKey())
                {
                    float t=CalculateInterpolationValue(current,next,frame);
                    info.CheekPretenseThreshold=FacialToonLerp(current.CheekPretenseThreshold,next.CheekPretenseThreshold,t);
                    info.NosePretenseThreshold=FacialToonLerp(current.NosePretenseThreshold,next.NosePretenseThreshold,t);
                    info.CylinderBlend=FacialToonLerp(current.CylinderBlend,next.CylinderBlend,t);
                    info.HairNormalBlend=FacialToonLerp(current.HairNormalBlend,next.HairNormalBlend,t);
                    info.OriginalDirectionalLightDir=new Vector3(
                        FacialToonLerp(current.OriginalDirectionalLightDir.x,next.OriginalDirectionalLightDir.x,t),
                        FacialToonLerp(current.OriginalDirectionalLightDir.y,next.OriginalDirectionalLightDir.y,t),
                        FacialToonLerp(current.OriginalDirectionalLightDir.z,next.OriginalDirectionalLightDir.z,t));
                    info.EyeToonStep=FacialToonLerp(current.EyeToonStep,next.EyeToonStep,t);
                    info.EyeToonFeather=FacialToonLerp(current.EyeToonFeather,next.EyeToonFeather,t);
                    info.EyeSaturation=FacialToonLerp(current.EyeSaturation,next.EyeSaturation,t);
                }
                OnUpdateFacialToon(ref info);
            }
        }
    }
}
