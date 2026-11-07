using System;
using System.Collections.Generic;
using System.Linq;

namespace Gallop.Live.Cutt
{
    // Original SetupKeyRandomMotionSystemTextId and its body-motion grouping.
    // The master accessor and shuffle return values are the input boundaries.
    public static class LiveSystemTextRandomMotion
    {
        public static LiveSystemTextCharacter ResolveTarget(int timelineIndex,int[] motionSequence,
            IReadOnlyList<LiveSystemTextCharacter> characters)
        {
            for(int slot=0;slot<motionSequence.Length;slot++)
                if(motionSequence[slot]==timelineIndex&&slot<characters.Count&&characters[slot].CharacterId!=-1)
                    return characters[slot];
            return new LiveSystemTextCharacter(-1,-1);
        }
        public static void Shuffle(List<int> values)
        {
            for(int count=values.Count;count>1;count--)
            {
                int index=LiveRandomBoundary.RangeInt(0,count),last=count-1,value=values[index];
                values[index]=values[last];values[last]=value;
            }
        }
        static bool TryGetMotion(ILiveSystemTextMaster master,int characterId,int voiceId,bool excludeUnique,
            out LiveSystemTextData text,out LiveSystemTextMotionSet motion)
        {
            text=master.GetText(characterId,voiceId);motion=null;
            if(text==null)return false;
            motion=master.GetMotionSet(text.MotionSet);if(motion==null)return false;
            if(excludeUnique)
            {
                if(motion.BodyMotionType==2)return false;
                if(text.MotionSecondSet>0)
                {
                    var second=master.GetMotionSet(text.MotionSecondSet);
                    if(second!=null&&second.BodyMotionType==2)return false;
                }
            }
            return true;
        }
        public static Dictionary<string,List<LiveSystemTextData>> CreateBodyMotionDictionary(
            ILiveSystemTextMaster master,int characterId,IEnumerable<int> voiceIds,bool excludeUnique)
        {
            var result=new Dictionary<string,List<LiveSystemTextData>>();
            foreach(int voiceId in voiceIds)
            {
                if(!TryGetMotion(master,characterId,voiceId,excludeUnique,out var text,out var motion))continue;
                if(!result.TryGetValue(motion.BodyMotion,out var list)){list=new List<LiveSystemTextData>();result.Add(motion.BodyMotion,list);}
                list.Add(text);
            }
            return result;
        }
        public static bool HasRequiredMobMotionCount(LiveTimelineCharaMotSeqData data,ILiveSystemTextMaster master,int characterId)
        {
            var indices=new HashSet<int>();
            foreach(var key in data.keys.thisList)if(key._useRandomMotionSystemText)indices.Add(key._randomMotionGroupIndex);
            foreach(int index in indices)
            {
                var groups=data.RandomEntrySystemTextGroups;
                if(index<0||index>=groups.Length)continue;
                var ids=groups[index]?.SystemTextIdArray;if(ids==null||ids.Length==0)continue;
                int count=0;
                // CountBodyMotionKind increments for each eligible text row;
                // it does not take the number of distinct body-motion strings.
                foreach(int id in ids)if(TryGetMotion(master,characterId,id,true,out _,out _))count++;
                if(count<3)return false;
            }
            return true;
        }
        public static int CreateOverrideCharacterId(LiveTimelineCharaMotSeqData data,ILiveSystemTextMaster master,
            LiveSystemTextCharacter character,int timelineIndex,List<int> playable=null,Action<List<int>> shuffle=null)
        {
            if(playable==null){playable=master.GetPlayableCharacterIds();(shuffle??Shuffle)(playable);}
            if(!character.IsMob)return -1;
            int personality=master.GetPersonality(character);
            var candidates=playable.Where(id=>master.GetPersonality(new LiveSystemTextCharacter(id,0))==personality).ToList();
            if(candidates.Count==0)return 1001;
            int index=timelineIndex%candidates.Count;
            while(candidates.Count>0)
            {
                int candidate=candidates[index];
                if(HasRequiredMobMotionCount(data,master,candidate))return candidate;
                candidates.RemoveAt(index);
                if(candidates.Count>0)index%=candidates.Count;
            }
            return 1001;
        }
        public static void Setup(LiveTimelineCharaMotSeqData data,int timelineIndex,LiveSystemTextCharacter character,
            ILiveSystemTextMaster master,IDictionary<(int,int),int> overrides,int overrideCharacterId=-1,Action<List<int>> shuffle=null)
        {
            if(timelineIndex<0||character.CharacterId==-1)return;
            if(character.IsMob)
            {
                var identity=(character.CharacterId,character.MobId);
                if(!overrides.TryGetValue(identity,out int chosen))
                {
                    chosen=overrideCharacterId!=-1?overrideCharacterId:CreateOverrideCharacterId(data,master,character,0,null,shuffle);
                    overrides[identity]=chosen;
                }
                data.SystemTextMotionOverrideCharacterId=chosen;
            }
            else data.SystemTextMotionOverrideCharacterId=-1;
            var groups=data.RandomEntrySystemTextGroups;
            if(groups==null||groups.Length==0)return;
            var keys=data.keys.thisList.Where(key=>key._useRandomMotionSystemText).ToList();
            if(keys.Count==0)return;
            int target=character.IsMob&&data.SystemTextMotionOverrideCharacterId!=-1?data.SystemTextMotionOverrideCharacterId:character.CharacterId;
            int total=0;
            foreach(var group in groups)
            {
                group.IsValid=false;group.BodyMotionDictionary=null;group.BodyMotionKeys=Array.Empty<string>();
                group.SystemTextIndexCounters=Array.Empty<int>();group.BodyMotionIndexCounter=0;
                if(group.SystemTextIdArray==null||group.SystemTextIdArray.Length==0)continue;
                var ids=new List<int>(group.SystemTextIdArray);(shuffle??Shuffle)(ids);
                group.BodyMotionDictionary=CreateBodyMotionDictionary(master,target,ids,character.IsMob);
                if(group.BodyMotionDictionary.Count==0)continue;
                group.IsValid=true;
                foreach(var list in group.BodyMotionDictionary.Values)total+=list.Count;
            }
            if(total<=0)return;
            foreach(var group in groups)
            {
                if(!group.IsValid)continue;
                group.BodyMotionKeys=group.BodyMotionDictionary.Keys.ToArray();
                group.SystemTextIndexCounters=new int[group.BodyMotionKeys.Length];
            }
            string previous=string.Empty;
            for(int keyIndex=0;keyIndex<keys.Count;keyIndex++)
            {
                var key=keys[keyIndex];int groupIndex=key._randomMotionGroupIndex;
                if(groupIndex<0||groupIndex>=groups.Length)continue;
                var group=groups[groupIndex];if(!group.IsValid)continue;
                int bucket=0;string body=string.Empty;
                for(int attempt=0;attempt<(keyIndex==0?1:group.BodyMotionKeys.Length);attempt++)
                {
                    bucket=group.BodyMotionIndexCounter%group.BodyMotionKeys.Length;
                    body=group.BodyMotionKeys[bucket];group.BodyMotionIndexCounter=unchecked(group.BodyMotionIndexCounter+1);
                    if(keyIndex==0||body!=previous)break;
                }
                var texts=group.BodyMotionDictionary[body];
                int textIndex=group.SystemTextIndexCounters[bucket]%texts.Count;
                group.SystemTextIndexCounters[bucket]=unchecked(group.SystemTextIndexCounters[bucket]+1);
                key.RandomMotionSystemTextId=texts[textIndex].VoiceId;previous=body;
            }
        }
    }
}
