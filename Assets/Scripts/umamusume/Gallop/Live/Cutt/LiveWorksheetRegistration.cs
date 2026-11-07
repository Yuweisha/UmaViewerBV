using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace Gallop.Live.Cutt
{
    public sealed class LiveWorksheetRegistrationState
    {
        public readonly List<string> RegisteredListPaths=new List<string>();
        public readonly List<string> StatusGroupPaths=new List<string>();
        public readonly List<string> UnavailableNativeFields=new List<string>();
        public int KeyOnLoadCount { get; internal set; }
    }
    public partial class LiveTimelineWorkSheet
    {
        // Fixed original 4f32f60 / 4f37680 registration order, constrained by
        // execution on every real indexed worksheet. No resource name branch.
        static readonly string[] NativeKeyRootOrder={"timescaleKeys","cameraPosKeys","cameraLookAtKeys","cameraFovKeys","cameraRollKeys","cameraMotionKeys","eventKeys","cameraSwitcherKeys","ripSyncKeys","ripSync2Keys","LipSyncPatternRangeKeys","postEffectDOFKeys","postEffectBloomDiffusionKeys","cameraLayerKeys","other4EyeTrackKeys","facialNoiseKeys","charaMotionNoiseKeys","tiltShiftKeys","hatchingKeys","edgeStyleKeys","titleKeys","charaFootLightKeys","MultiLightShadowKeys","fadeKeys","handShakeCameraKeys","radialBlurKeys","postFilmKeys","postFilm2Keys","postFilm3Keys","FluctuationKeys","ToneCurveKeys","ExposureKeys","VortexKeys","TransmittedLightKeys","LensDistortionKeys","VoiceKeys","TransparentCameraKeys","flashPlayerKeys","MiniCharaCameraKeys","charaMotSeqList","bgColor1List","bgColor2List","monitorControlList","animationList","textureAnimationList","transformList","rendererList","objectList","audienceList","waveObjectList","propsList","propsAttachList","ParentConstraintDataList","projecterList","facial1Set","volumeLightKeys","WashLightList","particleList","particleGroupList","laserList","blinkLightList","uvScrollLightList","transmittedLightMaskDataList","facialToonSet","globalLightDataLists","globalFogDataLists","colorCorrectionDataLists","lightShaftsKeysLine","monitorCameraLayerKeys","preColorCorrectionDataLists","monitorCameraPosKeys","monitorCameraLookAtKeys","multiCameraLayerKeys","multiCameraPosKeys","multiCameraLookAtKeys","EyeCameraPosList","EyeCameraLookAtList","lensFlareList","environmentDataLists","MirrorReflectionDataList","sweatLocatorList","effectList","ContactEffectDataList","spotlight3dList","CharaNodeDataList","RayHitEffectDataList","nodeScaleList","chromaticAberrationList","lightProjectionList","billboardList","postFilm1MultiCameraKeys","postFilm2MultiCameraKeys","postFilm3MultiCameraKeys","postEffectBloomDiffusionMultiCameraKeys","multiCameraColorCorrectionDataLists","multiCameraTiltShiftDataLists","multiCameraRadialBlurDataLists","postEffectDOFMultiCameraKeys","MultiCameraTransmittedLightDataList","AdditionalLightList","MobControlKeys","CyalumeControlKeys","CharaCollisionDataList","LipSyncPatternDataList","CharaNodeOffsetDataList","MiniCharaTransformDataList","MiniCharaMotionDataList","MiniCharaColorDataList","charaWind","CharaPartsKeys","other4FacialArray","formationOffsetSet"};
        static readonly string[] NativeStatusRootOrder={"charaMotSeqList","bgColor1List","bgColor2List","monitorControlList","animationList","textureAnimationList","transformList","rendererList","objectList","waveObjectList","audienceList","propsList","propsAttachList","ParentConstraintDataList","LipSyncPatternDataList","projecterList","transmittedLightMaskDataList","volumeLightKeys","hdrBloomKeys","particleList","particleGroupList","WashLightList","laserList","blinkLightList","uvScrollLightList","globalLightDataLists","globalFogDataLists","colorCorrectionDataLists","preColorCorrectionDataLists","lightShaftsKeysLine","monitorCameraLayerKeys","monitorCameraPosKeys","monitorCameraLookAtKeys","multiCameraLayerKeys","multiCameraPosKeys","multiCameraLookAtKeys","lensFlareList","environmentDataLists","MirrorReflectionDataList","sweatLocatorList","effectList","ContactEffectDataList","RayHitEffectDataList","spotlight3dList","CharaNodeDataList","nodeScaleList","CharaNodeOffsetDataList","chromaticAberrationList","lightProjectionList","billboardList","postFilm1MultiCameraKeys","postFilm2MultiCameraKeys","postFilm3MultiCameraKeys","postEffectBloomDiffusionMultiCameraKeys","multiCameraColorCorrectionDataLists","multiCameraTiltShiftDataLists","multiCameraRadialBlurDataLists","postEffectDOFMultiCameraKeys","MultiCameraTransmittedLightDataList","AdditionalLightList","MobControlKeys","CyalumeControlKeys","CharaCollisionDataList","EyeCameraPosList","EyeCameraLookAtList","MiniCharaTransformDataList","MiniCharaMotionDataList","MiniCharaColorDataList"};
        static readonly string[] NativeFacialOrder={"faceKeys","eyeKeys","eyebrowKeys","eyeTrackKeys","mouthKeys","earKeys","effectKeys","tailMotionKeys"};
        static readonly string[] NativeTwentyOrder={"centerKeys","left1Keys","right1Keys","left2Keys","right2Keys"};
        static FieldInfo NativeSavedField(Type type,string name)
        {
            while(type!=null)
            {
                var field=type.GetField(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly);
                if(field!=null)return field;type=type.BaseType;
            }
            return null;
        }
        object NativeSavedValue(object owner,string name,string path,LiveWorksheetRegistrationState state)
        {
            var field=NativeSavedField(owner.GetType(),name);
            if(field==null){state.UnavailableNativeFields.Add(path);return null;}
            return field.GetValue(owner);
        }
        void RegisterList(object value,string path,LiveTimelineControl control,LiveWorksheetRegistrationState state)
        {
            if(!(value is ILiveTimelineKeyDataList keys))
            {if(value!=null)state.UnavailableNativeFields.Add(path+":unsupported-container");return;}
            state.RegisteredListPaths.Add(path);
            for(int index=0;index<keys.Count;index++)
            {
                var key=keys[index];
                if(key==null)throw new NullReferenceException("Native key registration: "+path+"["+index+"]");
                key.OnLoad(control);state.KeyOnLoadCount++;
            }
        }
        void RegisterFacial(object facial,string path,LiveTimelineControl control,LiveWorksheetRegistrationState state)
        {
            if(facial==null)throw new NullReferenceException("Native facial registration: "+path);
            foreach(string name in NativeFacialOrder)RegisterList(NativeSavedValue(facial,name,path+"."+name,state),path+"."+name,control,state);
        }
        static LiveTimelineFacialData EmptySupportedFacial()
        {
            return new LiveTimelineFacialData();
        }
        internal void InitializeNativeFacialArray()
        {
            // Original OnLoad first resizes the physical non-center facial
            // array to nineteen, then invokes all keys, then group statuses.
            if(other4FacialArray==null)throw new NullReferenceException("Native facial array");
            int originalLength=other4FacialArray.Length;
            if(originalLength!=19)
            {
                Array.Resize(ref other4FacialArray,19);
                for(int index=originalLength;index<19;index++)other4FacialArray[index]=EmptySupportedFacial();
            }
        }
        internal void InitializeRegisteredKeys(LiveTimelineControl control,LiveWorksheetRegistrationState state)
        {
            InitializeNativeFacialArray();
            foreach(string root in NativeKeyRootOrder)
            {
                object value=NativeSavedValue(this,root,root,state);
                if(value==null)continue;
                if(value is ILiveTimelineKeyDataList){RegisterList(value,root,control,state);continue;}
                if(root=="facial1Set"){RegisterFacial(value,root,control,state);continue;}
                if(root=="other4FacialArray")
                {
                    var array=(IList)value;
                    for(int index=0;index<array.Count;index++)RegisterFacial(array[index],root+"["+index+"]",control,state);
                    continue;
                }
                if(root=="formationOffsetSet"||root=="facialToonSet"||root=="charaWind"||root=="CharaPartsKeys")
                {
                    foreach(string name in NativeTwentyOrder)RegisterList(NativeSavedValue(value,name,root+"."+name,state),root+"."+name,control,state);
                    for(int index=5;index<20;index++)
                    {
                        string name=root=="facialToonSet"?"motion"+index+"Keys":"place"+(index+1).ToString("00")+"Keys";
                        RegisterList(NativeSavedValue(value,name,root+"."+name,state),root+"."+name,control,state);
                    }
                    continue;
                }
                if(value is IList groups)
                {
                    string keyName=root=="MobControlKeys"||root=="CyalumeControlKeys"?"Keys":"keys";
                    for(int index=0;index<groups.Count;index++)
                    {
                        var group=groups[index];if(group==null)throw new NullReferenceException("Native group registration: "+root);
                        string path=root+"["+index+"]."+keyName;
                        RegisterList(NativeSavedValue(group,keyName,path,state),path,control,state);
                    }
                }
                else state.UnavailableNativeFields.Add(root+":unsupported-container");
            }
            foreach(string root in NativeStatusRootOrder)
            {
                object value=NativeSavedValue(this,root,root,state);
                if(!(value is IList groups))continue;
                for(int index=0;index<groups.Count;index++)
                {
                    string path=root+"["+index+"]";
                    if(groups[index] is ILiveTimelineGroupData group){group.UpdateStatus();state.StatusGroupPaths.Add(path);}
                    else state.UnavailableNativeFields.Add(path+":unsupported-status-owner");
                }
            }
        }
    }
}
