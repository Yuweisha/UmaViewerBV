using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    [System.Serializable]
    public class FacialPartsData
    {
        public int FacialPartsId;
        public int WeightPer;
    }

    [System.Serializable]
    public class LiveTimelineKeyLipSyncData : LiveTimelineKey
    {
        public override LiveTimelineKeyDataType dataType
        {
            get{return LiveTimelineKeyDataType.LipSync;}
        }
        public LiveCharaPositionFlag character;
        public int facialId;
        public int weight;
        public int speed;
        public int time;
        public FacialPartsData[] facialPartsDataArray; // 0x30
        public DrivenKeyComponent.InterpolateType interpolateType; // 0x38
        public List<SongPartVariationLipSyncTarget> SongPartVariationTargetFlagList;

        public LiveCharaPositionFlag GetCharacterFlags(int songPartVariationId)
        {
            // Original4f201f0: first matching override, and variation0 always
            // returns the key's default mask without reading the list.
            if(songPartVariationId!=0)
                foreach(var target in SongPartVariationTargetFlagList)
                    if(target.SongPartVariationId==songPartVariationId)return target.PositionFlag;
            return character;
        }

        [System.Serializable]
        public class SongPartVariationLipSyncTarget
        {
            public int SongPartVariationId;
            public LiveCharaPositionFlag PositionFlag;
        }
    }

    [System.Serializable]
    public class LiveTimelineKeyLipSyncDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyLipSyncData>
    {

    }
}
