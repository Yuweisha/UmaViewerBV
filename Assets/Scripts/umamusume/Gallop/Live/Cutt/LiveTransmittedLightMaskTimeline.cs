using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    [Serializable] public sealed class GameObjectIdentifierInHierarchy
    {
        [SerializeField] string _hierarchyPath;
        public string HierarchyPath=>_hierarchyPath;
        public GameObjectIdentifierInHierarchy(string hierarchyPath){_hierarchyPath=hierarchyPath;}
    }
    [Serializable] public sealed class LiveTransmittedLightMaskScaleKeyValue
    {
        static readonly Regex LightRegex=new Regex(@"light(?<index>\d\d\d)_");
        [SerializeField] GameObjectIdentifierInHierarchy _gameObjectIdentifier;
        [SerializeField] float _maskScale;
        int _lightContainerIndex=-1;
        public GameObjectIdentifierInHierarchy GameObjectIdentifier=>_gameObjectIdentifier;
        public float MaskScale=>_maskScale;
        public int LightContainerIndex=>_lightContainerIndex;
        public LiveTransmittedLightMaskScaleKeyValue(GameObjectIdentifierInHierarchy identifier,float maskScale)
        {_gameObjectIdentifier=identifier;_maskScale=maskScale;SetupLightContainerIndex();}
        public void SetMaskScale(float scale)=>_maskScale=scale;
        public void SetupLightContainerIndex()
        {
            _lightContainerIndex=-1;
            if(_gameObjectIdentifier==null||string.IsNullOrEmpty(_gameObjectIdentifier.HierarchyPath))return;
            var split=_gameObjectIdentifier.HierarchyPath.Split('/');var match=LightRegex.Match(split[split.Length-1]);
            if(match.Success&&int.TryParse(match.Groups[1].Value,out int index))_lightContainerIndex=index;
        }
    }
    [Serializable] public class LiveTimelineKeyTransmittedLightMaskData:LiveTimelineKeyWithInterpolate
    {
        [SerializeField] int _currentVersion;
        [SerializeField] int _lightContainerCount;
        [SerializeField] float[] MaskScaleArray=Array.Empty<float>();
        public List<LiveTransmittedLightMaskScaleKeyValue> _maskScaleKeyValueList=new List<LiveTransmittedLightMaskScaleKeyValue>();
        [SerializeField] bool _isBlinkLightColorPowerLinked;
        public override LiveTimelineKeyDataType dataType=>LiveTimelineKeyDataType.TransmittedLightMask;
        public int LightContainerCount=>_lightContainerCount;
        public bool IsBlinkLightColorPowerLinked=>_isBlinkLightColorPowerLinked;
        public float GetMaskScale(int index,int timelineNameHash)
        {
            if(_currentVersion==0)return MaskScaleArray[index];
            if(_currentVersion!=1)return 0;
            float scale=_maskScaleKeyValueList[index].MaskScale;
            if(_isBlinkLightColorPowerLinked&&_maskScaleKeyValueList[index].LightContainerIndex>=0&&
                LiveTimelineControl.TryGetBlinkLightColorPower(out float power,timelineNameHash,_maskScaleKeyValueList[index].LightContainerIndex))scale*=power;
            return scale;
        }
        public override void OnLoad(LiveTimelineControl control)
        {base.OnLoad(control);foreach(var value in _maskScaleKeyValueList)value?.SetupLightContainerIndex();}
    }
    [Serializable] public class LiveTimelineKeyTransmittedLightMaskDataList:LiveTimelineKeyDataListTemplate<LiveTimelineKeyTransmittedLightMaskData>{}
    [Serializable] public class LiveTimelineTransmittedLightMaskData:ILiveTimelineGroupDataWithName
    {
        public LiveTimelineKeyTransmittedLightMaskDataList keys=new LiveTimelineKeyTransmittedLightMaskDataList();
        public LiveTimelineTransmittedLightMaskData():base("TransmittedLightMask"){}
        public override ILiveTimelineKeyDataList GetKeyList()=>keys;
    }
    public struct TransmittedLightMaskUpdateInfo {public int TimelineNameHash;public float[] MaskScaleArray;}
    public delegate void TransmittedLightMaskUpdateInfoDelegate(ref TransmittedLightMaskUpdateInfo info,int nameHash);

    public partial class LiveTimelineControl
    {
        public delegate bool MaskBlinkPowerResolver(out float power,int nameHash,int containerIndex);
        public static MaskBlinkPowerResolver TransmittedMaskBlinkPowerLookup {get;set;}
        public event TransmittedLightMaskUpdateInfoDelegate OnUpdateTransmittedLightMask;
        readonly List<TransmittedLightMaskUpdateInfo> _transmittedLightMaskUpdateInfoList=new List<TransmittedLightMaskUpdateInfo>();
        public static bool TryGetBlinkLightColorPower(out float colorPower,int nameHash,int containerIndex)
        {
            if(TransmittedMaskBlinkPowerLookup!=null)return TransmittedMaskBlinkPowerLookup(out colorPower,nameHash,containerIndex);
            colorPower=0;var director=Director.instance;
            if(director==null||!director.IsEnabledStageController)return false;
            var stage=director._stageController;if(stage==null)throw new NullReferenceException();
            var driver=stage.GetComponent<StageBlinkLightDriver>();
            return driver!=null&&driver.TryGetCurrentBlinkColorPowerByTimelineHash(nameHash,containerIndex,out colorPower);
        }
        public void AddTransmittedLightMaskController(int nameHash,TransmittedLightMaskController controller)
        {
            int index=_transmittedLightMaskUpdateInfoList.FindIndex(v=>v.TimelineNameHash==nameHash);
            var info=index<0?new TransmittedLightMaskUpdateInfo{TimelineNameHash=nameHash}:_transmittedLightMaskUpdateInfoList[index];
            info.MaskScaleArray=new float[controller.LightContainerCount];
            // Native duplicate branch modifies a value-type local and never
            // writes it back to the list; preserve the existing buffer identity.
            if(index<0)_transmittedLightMaskUpdateInfoList.Add(info);
        }
        [MethodImpl(MethodImplOptions.NoInlining)] static float MaskFloat(float v)=>BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(v));
        public void AlterUpdate_TransmittedLightMask(LiveTimelineWorkSheet sheet,int frame)
        {
            if(OnUpdateTransmittedLightMask==null)return;
            foreach(var group in sheet.transmittedLightMaskDataList)
            {
                if(group.keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable)||!group.keys.EnablePlayModeTimeline(_playMode))continue;
                int index=_transmittedLightMaskUpdateInfoList.FindIndex(v=>v.TimelineNameHash==group.nameHash);if(index<0)continue;
                FindTimelineKey(out var cur,out var next,group.keys,frame);if(cur==null)continue;
                var a=(LiveTimelineKeyTransmittedLightMaskData)cur;var b=next as LiveTimelineKeyTransmittedLightMaskData;
                var info=_transmittedLightMaskUpdateInfoList[index];
                if(info.MaskScaleArray==null||a.LightContainerCount!=info.MaskScaleArray.Length)continue;
                bool interpolate=b!=null&&b.IsInterpolateKey();float t=interpolate?LiveTransmittedLightTimeline.Interpolation(a,b,frame):0;
                for(int i=0;i<a.LightContainerCount;i++)
                {
                    float av=a.GetMaskScale(i,group.nameHash);
                    info.MaskScaleArray[i]=interpolate?MaskFloat(av+MaskFloat(MaskFloat(b.GetMaskScale(i,group.nameHash)-av)*t)):av;
                }
                OnUpdateTransmittedLightMask.Invoke(ref info,group.nameHash);
            }
        }
    }
}
