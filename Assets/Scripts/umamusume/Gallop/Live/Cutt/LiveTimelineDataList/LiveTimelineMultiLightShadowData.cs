using System;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    [Serializable]
    public class LiveTimelineKeyData_MultiLightShadow : LiveTimelineKeyWithInterpolate
    {
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.MultiLightShadow;
        public bool IsUseParam;
        public Vector3 ShadowCenterPosition,ShadowForward;
        public float ShadowFadeStart=100f,ShadowFadeLength=100f;
        public bool IsUseShadowFront,IsShadowNearStart=true;
        public Color ShadowStartColor=new Color(0,0,0,1),ShadowEndColor=new Color(0,0,0,1);
        public float ShadowDistance=15f;
        public bool IsDarkestShadowOnly;
    }
    [Serializable]
    public class LiveTimelineKeyMultiLightShadowDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyData_MultiLightShadow> { }
    public struct MultiLightShadowUpdateInfo
    {
        public bool IsUseParam;
        public Vector3 ShadowCenterPosition,ShadowForward;
        public float ShadowFadeStart,ShadowFadeLength;
        public bool IsUseShadowFront,IsShadowNearStart;
        public Color ShadowStartColor,ShadowEndColor;
        public float ShadowDistance;
        // Original payload contains this field; original Live key does not.
        // Parent0x4ede050 leaves it zero on every dispatch.
        public Color FillColor;
        public bool IsDarkestShadowOnly;
    }
    public delegate void MultiLightShadowParamInfoDelegate(ref MultiLightShadowUpdateInfo updateInfo);
    public partial class LiveTimelineWorkSheet
    {
        [SerializeField] public LiveTimelineKeyMultiLightShadowDataList MultiLightShadowKeys;
    }
    public partial class LiveTimelineControl
    {
        public event MultiLightShadowParamInfoDelegate OnUpdateMultiLightShadowParam;
        static float MultiShadowLerp(float a,float b,float t)=>MonitorFloat32(MonitorFloat32(MonitorFloat32(b-a)*t)+a);
        static Vector3 MultiShadowLerp(Vector3 a,Vector3 b,float t)=>new Vector3(MultiShadowLerp(a.x,b.x,t),MultiShadowLerp(a.y,b.y,t),MultiShadowLerp(a.z,b.z,t));
        static Color MultiShadowLerp(Color a,Color b,float t)=>new Color(MultiShadowLerp(a.r,b.r,t),MultiShadowLerp(a.g,b.g,t),MultiShadowLerp(a.b,b.b,t),MultiShadowLerp(a.a,b.a,t));
        // Complete original parent0x4ede050. Callback absence is the first gate;
        // disabled/play-mode/no-current still publish the zeroed payload.
        private void AlterUpdate_MultiLightShadow(LiveTimelineWorkSheet sheet,int frame)
        {
            if(OnUpdateMultiLightShadowParam==null)return;
            if(sheet==null||sheet.MultiLightShadowKeys==null)throw new NullReferenceException("Original MultiLightShadow list is absent");
            var keys=sheet.MultiLightShadowKeys;var info=new MultiLightShadowUpdateInfo();
            if(!keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable)&&keys.EnablePlayModeTimeline(_playMode))
            {
                FindTimelineKey(out var currentBase,out var nextBase,keys,frame);
                if(currentBase!=null)
                {
                    var current=currentBase as LiveTimelineKeyData_MultiLightShadow;
                    if(current==null)throw new InvalidCastException("Original MultiLightShadow key type");
                    info.IsUseParam=current.IsUseParam;info.ShadowCenterPosition=current.ShadowCenterPosition;info.ShadowForward=current.ShadowForward;
                    info.ShadowFadeStart=current.ShadowFadeStart;info.ShadowFadeLength=current.ShadowFadeLength;
                    info.IsUseShadowFront=current.IsUseShadowFront;info.IsShadowNearStart=current.IsShadowNearStart;
                    info.ShadowStartColor=current.ShadowStartColor;info.ShadowEndColor=current.ShadowEndColor;
                    info.ShadowDistance=current.ShadowDistance;info.IsDarkestShadowOnly=current.IsDarkestShadowOnly;
                    var next=nextBase as LiveTimelineKeyData_MultiLightShadow;
                    if(next!=null&&next.IsInterpolateKey())
                    {
                        float t=CalculateInterpolationValue(current,next,frame);
                        info.ShadowCenterPosition=MultiShadowLerp(current.ShadowCenterPosition,next.ShadowCenterPosition,t);
                        info.ShadowForward=MultiShadowLerp(current.ShadowForward,next.ShadowForward,t);
                        info.ShadowFadeStart=MultiShadowLerp(current.ShadowFadeStart,next.ShadowFadeStart,t);
                        info.ShadowFadeLength=MultiShadowLerp(current.ShadowFadeLength,next.ShadowFadeLength,t);
                        info.ShadowStartColor=MultiShadowLerp(current.ShadowStartColor,next.ShadowStartColor,t);
                        info.ShadowEndColor=MultiShadowLerp(current.ShadowEndColor,next.ShadowEndColor,t);
                        info.ShadowDistance=MultiShadowLerp(current.ShadowDistance,next.ShadowDistance,t);
                    }
                }
            }
            OnUpdateMultiLightShadowParam(ref info);
        }
    }
}
