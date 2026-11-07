using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    public static class LiveLipSyncPatternCsv
    {
        public static Dictionary<int,int[]> Parse(string csv)
        {
            var result=new Dictionary<int,int[]>();
            if(string.IsNullOrEmpty(csv))return result;
            using(var reader=new StringReader(csv))
            {
                string header=reader.ReadLine();
                if(header==null)return result;
                var columns=header.TrimStart('\uFEFF').Split(',');
                int idColumn=Array.IndexOf(columns,"chara_id"),patternColumn=Array.IndexOf(columns,"lipsync_pattern");
                if(idColumn<0||patternColumn<0)throw new InvalidDataException("Missing original LipSync CSV headers");
                string line;
                while((line=reader.ReadLine())!=null)
                {
                    if(string.IsNullOrWhiteSpace(line))continue;
                    var cells=line.Split(',');
                    if(idColumn>=cells.Length||patternColumn>=cells.Length)throw new InvalidDataException("Incomplete LipSync CSV row");
                    if(!int.TryParse(cells[idColumn],NumberStyles.Integer,CultureInfo.InvariantCulture,out int id))continue;
                    var tokens=cells[patternColumn].Split('|');var patterns=new List<int>();
                    foreach(string token in tokens)if(int.TryParse(token,NumberStyles.Integer,CultureInfo.InvariantCulture,out int value))patterns.Add(value);
                    result.Add(id,patterns.ToArray());
                }
            }
            return result;
        }
    }
    [Serializable]
    public class LiveTimelineKeyLipSyncBase : LiveTimelineKey
    {
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.LipSync;
        public int facialId=8001;
        public int weight=100;
        public int speed=10;
        public int time=5;
        public FacialPartsData[] facialPartsDataArray={new FacialPartsData{FacialPartsId=0,WeightPer=100}};
        public DrivenKeyComponent.InterpolateType interpolateType;
        public bool IsEnabledFaceParts => ((int)attribute&131072)!=0;
        public override void OnLoad(LiveTimelineControl control)
        {
            base.OnLoad(control);
            if(facialPartsDataArray==null||facialPartsDataArray.Length==0)
                facialPartsDataArray=new[]{new FacialPartsData{FacialPartsId=0,WeightPer=100}};
        }
    }
    [Serializable]
    public class LiveTimelineKeyLipSyncPatternData : LiveTimelineKeyLipSyncBase
    {
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.LipSyncPattern;
        public int PatternRangeIndex {get;set;}
    }
    [Serializable]
    public class LiveTimelineKeyLipSyncPatternDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyLipSyncPatternData> { }
    [Serializable]
    public class LiveTimelineKeyLipSyncPatternRangeData : LiveTimelineKey
    {
        public enum RangeAction {Start=0,End=1}
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.LipSyncPatternRange;
        public RangeAction RangeActionType;
        public int RangeIndex=1;
        public LiveCharaPositionFlag CharacterFlag;
    }
    [Serializable]
    public class LiveTimelineKeyLipSyncPatternRangeDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyLipSyncPatternRangeData> { }
    [Serializable]
    public class LiveTimelineLipSyncPatternData : ILiveTimelineGroupData
    {
        public LiveTimelineKeyLipSyncPatternDataList keys=new LiveTimelineKeyLipSyncPatternDataList();
        [SerializeField] int _patternId;
        [SerializeField] bool _applyVariation;
        [SerializeField] int _variationId;
        public int PatternId {get=>_patternId;set=>_patternId=value;}
        public bool ApplyVariation {get=>_applyVariation;set=>_applyVariation=value;}
        public int VariationId {get=>_variationId;set=>_variationId=value;}
        public override ILiveTimelineKeyDataList GetKeyList()=>keys;
    }
    public partial class LiveTimelineWorkSheet
    {
        [SerializeField] public LiveTimelineKeyLipSyncPatternRangeDataList LipSyncPatternRangeKeys;
        [SerializeField] public List<LiveTimelineLipSyncPatternData> LipSyncPatternDataList;
    }
    public partial class LiveTimelineControl
    {
        // Physical slots are original CharacterObject slots, independent of
        // motion-sequence indices. Providers refer to actual loaded owners.
        IReadOnlyDictionary<int,int[]> _lipSyncPatternMap;
        Func<int,int?> _lipSyncPatternCharacterId;
        Func<int,int?> _lipSyncPatternContextCharacterId;
        Func<int,bool> _lipSyncPatternVariationEnabled;
        Func<int,bool> _lipSyncPatternActorExists;
        readonly int[] _lipSyncPatternIds=NewLipSyncPatternIds();
        public bool UsesLipSyncPattern {get;private set;}
        public int CurrentLipSyncPatternRangeIndex {get;private set;}=-1;
        public int CurrentLipSyncPatternRangeStartFrame {get;private set;}=-1;
        public event Action<int,LiveTimelineKeyLipSyncPatternData,float> OnUpdateLipSyncPattern;
        static int[] NewLipSyncPatternIds()
        {var result=new int[20];for(int i=0;i<result.Length;i++)result[i]=-1;return result;}
        public int GetLipSyncPatternId(int physicalSlot)=>physicalSlot>=0&&physicalSlot<20?_lipSyncPatternIds[physicalSlot]:-1;
        public void ConfigureLipSyncPatterns(IReadOnlyDictionary<int,int[]> map,Func<int,int?> characterId,
            Func<int,int?> contextCharacterId,Func<int,bool> variationEnabled,bool usesLipPattern=true,Func<int,bool> actorExists=null)
        {
            UsesLipSyncPattern=usesLipPattern;
            _lipSyncPatternMap=usesLipPattern?map:null;_lipSyncPatternCharacterId=characterId;
            _lipSyncPatternContextCharacterId=contextCharacterId;_lipSyncPatternVariationEnabled=variationEnabled;
            _lipSyncPatternActorExists=actorExists;
            CurrentLipSyncPatternRangeIndex=-1;CurrentLipSyncPatternRangeStartFrame=-1;
            for(int slot=0;slot<20;slot++)_lipSyncPatternIds[slot]=-1;
        }
        bool LipSyncPatternActorExists(int slot)=>_lipSyncPatternActorExists!=null?_lipSyncPatternActorExists(slot):_lipSyncPatternCharacterId!=null&&_lipSyncPatternCharacterId(slot).HasValue;
        void AlterUpdate_LipSyncPatternRange(LiveTimelineWorkSheet sheet,int frame)
        {
            if(_lipSyncPatternCharacterId==null&&_lipSyncPatternActorExists==null)return;
            CurrentLipSyncPatternRangeIndex=-1;CurrentLipSyncPatternRangeStartFrame=-1;
            for(int slot=0;slot<20;slot++)if(LipSyncPatternActorExists(slot))_lipSyncPatternIds[slot]=-1;
            var keys=sheet?.LipSyncPatternRangeKeys;
            if(keys==null||keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable)||!keys.EnablePlayModeTimeline(_playMode)||keys.Count<1)return;
            FindTimelineKeyCurrent(out LiveTimelineKey currentBase,keys,frame);
            if(!(currentBase is LiveTimelineKeyLipSyncPatternRangeData current)||current.RangeActionType!=LiveTimelineKeyLipSyncPatternRangeData.RangeAction.Start)return;
            CurrentLipSyncPatternRangeIndex=current.RangeIndex;CurrentLipSyncPatternRangeStartFrame=current.frame;
            for(int slot=0;slot<20;slot++)
            {
                if(((uint)current.CharacterFlag&(1u<<slot))==0)continue;
                if(!LipSyncPatternActorExists(slot))continue;
                int? id=_lipSyncPatternCharacterId?.Invoke(slot);if(!id.HasValue)continue;
                int? contextId=_lipSyncPatternContextCharacterId?.Invoke(slot);if(contextId.HasValue)id=contextId;
                if(_lipSyncPatternMap==null||_lipSyncPatternMap.Count==0)continue;
                if(!_lipSyncPatternMap.TryGetValue(id.Value,out var patterns)&&!_lipSyncPatternMap.TryGetValue(0,out patterns))continue;
                int index=current.RangeIndex-1;
                if(index>=0&&index<patterns.Length)_lipSyncPatternIds[slot]=patterns[index];
            }
        }
        void AlterUpdate_LipSyncPattern(LiveTimelineWorkSheet sheet,int frame)
        {
            if((_lipSyncPatternCharacterId==null&&_lipSyncPatternActorExists==null)||sheet?.LipSyncPatternDataList==null)return;
            for(int slot=0;slot<20;slot++)
            {
                if(!LipSyncPatternActorExists(slot)||_lipSyncPatternIds[slot]<0)continue;
                foreach(var group in sheet.LipSyncPatternDataList)
                {
                    if(group==null)continue;
                    if(group.ApplyVariation&&(_lipSyncPatternVariationEnabled==null||!_lipSyncPatternVariationEnabled(group.VariationId)))continue;
                    if(group.PatternId!=_lipSyncPatternIds[slot])continue;
                    var keys=group.keys;
                    if(keys==null||keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable)||!keys.EnablePlayModeTimeline(_playMode)||keys.Count<1)continue;
                    FindTimelineKeyCurrent(out LiveTimelineKey currentBase,keys,frame);
                    if(!(currentBase is LiveTimelineKeyLipSyncPatternData current)||current.frame<CurrentLipSyncPatternRangeStartFrame)continue;
                    current.PatternRangeIndex=CurrentLipSyncPatternRangeIndex;
                    OnUpdateLipSyncPattern?.Invoke(slot,current,LipSyncPatternDuration(current.speed,current.time));
                }
            }
        }
        public static float LipSyncPatternDuration(int speed,int time)
        {
            int frames;
            switch(speed)
            {
                case 0:frames=time;break;
                case 1:frames=3;break;
                case 10:frames=2;break;
                case 20:frames=6;break;
                case 21:frames=9;break;
                default:return 0f;
            }
            return frames*0.0166666675f;
        }
    }
}
