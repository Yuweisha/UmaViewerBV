using System;
namespace Gallop.Live.Cutt
{
    [Serializable] public class LiveTimelineKeyMultiCameraPostEffectDOFData:LiveTimelineKeyPostEffectDOFData
    {public override LiveTimelineKeyDataType dataType=>LiveTimelineKeyDataType.MultiCameraPostEffectDOF;}
    [Serializable] public class LiveTimelineKeyMultiCameraPostEffectDOFDataList:LiveTimelineKeyDataListTemplate<LiveTimelineKeyMultiCameraPostEffectDOFData>{}
    [Serializable] public class LiveTimelineMultiCameraPostEffectDOFData:LiveTimelineMultiCameraPostEffect
    {
        public LiveTimelineKeyMultiCameraPostEffectDOFDataList keys=new LiveTimelineKeyMultiCameraPostEffectDOFDataList();
        public LiveTimelineMultiCameraPostEffectDOFData():base("DOF"){}
        public override ILiveTimelineKeyDataList GetKeyList()=>keys;
    }
    [Serializable] public class LiveTimelineKeyMultiCameraTiltShiftData:LiveTimelineKeyTiltShiftData
    {public override LiveTimelineKeyDataType dataType=>LiveTimelineKeyDataType.MultiCameraTiltShift;}
    [Serializable] public class LiveTimelineKeyMultiCameraTiltShiftDataList:LiveTimelineKeyDataListTemplate<LiveTimelineKeyMultiCameraTiltShiftData>{}
    [Serializable] public class LiveTimelineMultiCameraTiltShiftData:LiveTimelineMultiCameraPostEffect
    {
        public LiveTimelineKeyMultiCameraTiltShiftDataList keys;
        public LiveTimelineMultiCameraTiltShiftData():base("TiltShift"){}
        public override ILiveTimelineKeyDataList GetKeyList()=>keys;
    }
    [Serializable] public class LiveTimelineKeyMultiCameraRadialBlurData:LiveTimelineKeyRadialBlurData
    {public override LiveTimelineKeyDataType dataType=>LiveTimelineKeyDataType.MultiCameraRadialBlur;}
    [Serializable] public class LiveTimelineKeyMultiCameraRadialBlurDataList:LiveTimelineKeyDataListTemplate<LiveTimelineKeyMultiCameraRadialBlurData>{}
    [Serializable] public class LiveTimelineMultiCameraRadialBlurData:LiveTimelineMultiCameraPostEffect
    {
        public LiveTimelineKeyMultiCameraRadialBlurDataList keys;
        public LiveTimelineMultiCameraRadialBlurData():base("MultiCamera-RadialBlur"){}
        public override ILiveTimelineKeyDataList GetKeyList()=>keys;
    }
}
