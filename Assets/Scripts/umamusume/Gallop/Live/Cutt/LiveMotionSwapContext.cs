using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    public sealed class LiveMotionSwapCharacter
    {
        public int CharaId;
        public int[] DressIds;
    }
    public sealed class LiveMotionSwapContext
    {
        public int CharacterCount;
        public LiveMotionSwapCharacter[] Characters;
        public readonly Dictionary<int,int> DressHandTypes=new Dictionary<int,int>();
        public Func<string,AnimationClip> LoadClip;
        bool Conditions(int order,bool charActive,int charId,bool dressActive,int dressId,int dressCondition)
        {
            if(order<=0||order-1>=CharacterCount)return false;
            var character=Characters[order-1];
            if(charActive&&charId>=0&&character.CharaId!=charId)return false;
            if(dressActive&&dressId>=0&&Array.IndexOf(character.DressIds,dressId)<0)return false;
            if(dressCondition==1)
            {
                foreach(int id in character.DressIds)if(DressHandTypes.TryGetValue(id,out int hand)&&hand==1)return true;
                return false;
            }
            return true;
        }
        public bool MatchesAnd(LiveTimelineKeyCharaMotionData.SwapMotionData.AndConditionData condition)
            =>Conditions(condition.TargetOrder,condition.ActiveCharaId,condition.CharaId,condition.ActiveDressId,condition.DressId,(int)condition.DressCondition);
        public bool Matches(LiveTimelineKeyCharaMotionData.SwapMotionData swap)
        {
            if(swap==null||(int)swap.TargetMotion==0||swap.TargetOrder<=0||string.IsNullOrEmpty(swap.SwapMotionName))return false;
            if(!Conditions(swap.TargetOrder,swap.ActiveCharaId,swap.CharaId,swap.ActiveDressId,swap.DressId,(int)swap.DressCondition))return false;
            int required=(swap.ActiveCharaId&&swap.CharaId>=0?1:0)+(swap.ActiveDressId&&swap.DressId>=0?1:0)+((int)swap.DressCondition==1?1:0);
            if(required==0)return false;
            foreach(var condition in swap.AndConditionDataArray)if(!MatchesAnd(condition))return false;
            return true;
        }
        public void Load(LiveTimelineKeyCharaMotionData key)
        {
            if(key.SwapMotionDataArray==null)return;
            LiveTimelineKeyCharaMotionData.SwapMotionData selected=null;
            foreach(var swap in key.SwapMotionDataArray)
            {
                swap.SwapAnimationClip=null;swap.IsEnabledSwapAnimationClip=false;swap.SwapAnimationClipName=string.Empty;
                if(Matches(swap))selected=swap;
            }
            if(selected==null)return;
            selected.SwapAnimationClip=LoadClip(selected.SwapMotionName);
            selected.IsEnabledSwapAnimationClip=selected.SwapAnimationClip!=null;
            selected.SwapAnimationClipName=selected.IsEnabledSwapAnimationClip?selected.SwapAnimationClip.name:string.Empty;
        }
        public void ReadDressExtensions(string csv)
        {
            DressHandTypes.Clear();if(csv==null)return;
            bool header=true;
            foreach(var fields in LiveSettingsResourceMap.Rows(csv))
            {
                if(header){header=false;continue;}
                if(fields.Count<2)continue;
                if(!int.TryParse(fields[0],NumberStyles.Integer,CultureInfo.InvariantCulture,out _)
                    ||!int.TryParse(fields[1],NumberStyles.Integer,CultureInfo.InvariantCulture,out int dress)
                    ||!int.TryParse(fields[2],NumberStyles.Integer,CultureInfo.InvariantCulture,out int hand))continue;
                if(hand!=0&&hand!=1)continue;DressHandTypes.Add(dress,hand);
            }
        }
    }
}
