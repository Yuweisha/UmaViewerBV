using System;
using UnityEngine;
namespace Gallop.Live.Cutt
{
    [Serializable]
    public class LiveTimelineKeyCharaWind : LiveTimelineKeyWithInterpolate
    {
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.CharaWind;
        public bool IsEnableWind;
        public CySpringWindParam WindParam = new CySpringWindParam();
    }
    [Serializable]
    public class LiveTimelineKeyCharaWindDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyCharaWind> { }
    [Serializable]
    public class LiveTimelineCharaWindData
    {
        public LiveTimelineKeyCharaWindDataList centerKeys;
        public LiveTimelineKeyCharaWindDataList left1Keys;
        public LiveTimelineKeyCharaWindDataList right1Keys;
        public LiveTimelineKeyCharaWindDataList left2Keys;
        public LiveTimelineKeyCharaWindDataList right2Keys;
        public LiveTimelineKeyCharaWindDataList place06Keys;
        public LiveTimelineKeyCharaWindDataList place07Keys;
        public LiveTimelineKeyCharaWindDataList place08Keys;
        public LiveTimelineKeyCharaWindDataList place09Keys;
        public LiveTimelineKeyCharaWindDataList place10Keys;
        public LiveTimelineKeyCharaWindDataList place11Keys;
        public LiveTimelineKeyCharaWindDataList place12Keys;
        public LiveTimelineKeyCharaWindDataList place13Keys;
        public LiveTimelineKeyCharaWindDataList place14Keys;
        public LiveTimelineKeyCharaWindDataList place15Keys;
        public LiveTimelineKeyCharaWindDataList place16Keys;
        public LiveTimelineKeyCharaWindDataList place17Keys;
        public LiveTimelineKeyCharaWindDataList place18Keys;
        public LiveTimelineKeyCharaWindDataList place19Keys;
        public LiveTimelineKeyCharaWindDataList place20Keys;
        [NonSerialized] ILiveTimelineKeyDataList[] _cacheDataList;
        public ILiveTimelineKeyDataList[] GetKeyListArray()
        {
            if(_cacheDataList==null)_cacheDataList=new ILiveTimelineKeyDataList[] {centerKeys,left1Keys,right1Keys,left2Keys,right2Keys,place06Keys,place07Keys,place08Keys,place09Keys,place10Keys,place11Keys,place12Keys,place13Keys,place14Keys,place15Keys,place16Keys,place17Keys,place18Keys,place19Keys,place20Keys };
            return _cacheDataList;
        }
    }
    public partial class LiveTimelineWorkSheet
    {
        [SerializeField] public LiveTimelineCharaWindData charaWind;
    }
    public partial class LiveTimelineControl
    {
        // Physical character slot, enabled flag, original current-key parameter.
        // Each model consumer must copy this shared transient parameter.
        public event Action<int,bool,CySpringWindParam> OnUpdateCharaWind;
        readonly CySpringWindParam _nativeWindParameter = new CySpringWindParam();
        private void AlterUpdate_CharaWind(LiveTimelineWorkSheet sheet,int characterObjectCount)
        {
            if(sheet?.charaWind==null)return;
            var lists=sheet.charaWind.GetKeyListArray();int count=System.Math.Min(lists.Length,characterObjectCount);
            for(int physicalSlot=0;physicalSlot<count;physicalSlot++)
            {
                var keys=lists[physicalSlot];
                if(keys==null || keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) || !keys.EnablePlayModeTimeline(_playMode) || keys.Count<=0)continue;
                FindTimelineKey(out var currentBase,out var nextBase,keys,_currentFrame);
                if(!(currentBase is LiveTimelineKeyCharaWind current))continue;
                if(!current.IsEnableWind){OnUpdateCharaWind?.Invoke(physicalSlot,false,null);continue;}
                if(nextBase is LiveTimelineKeyCharaWind next && next.IsInterpolateKey())
                    CySpringWindParam.SetInterp(current.WindParam,next.WindParam,_nativeWindParameter,CalculateInterpolationValue(current,next,_currentFrame));
                else _nativeWindParameter.Copy(current.WindParam);
                // Original computes the temporary interpolation above, then
                // 0x4ecbf35 loads current-key +0x38 for Model.ApplyWindData.
                OnUpdateCharaWind?.Invoke(physicalSlot,true,current.WindParam);
            }
        }
    }
}
