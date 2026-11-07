using System;
using System.Collections.Generic;

namespace Gallop.Live
{
    public static class LiveMiniSelection
    {
        public sealed class Rule
        {
            public int Mode,PositionId,CharaId,DressId,DressColorId;
        }
        public sealed class CharacterInput
        {
            public int CharaId;
            public int[] DressIds,DressColorIds;
        }
        public sealed class MiniCharacterInfo
        {
            public int CharaId,MobId,DressId,DressColorId;
        }
        // Original Live3DSettings.SetupMiniChara4e68360. Shuffle is requested once
        // before clearing the load list, even for position-only input. Its RNG
        // source/order belongs to MasterCharaData and is an explicit caller input.
        public static void Apply(IReadOnlyList<Rule> rules,CharacterInput[] characters,int characterCount,Func<int[]> shuffleCharacters,Func<int,int?> dressColorCount,List<MiniCharacterInfo> output)
        {
            if(rules.Count<=0)return;int[] shuffled=shuffleCharacters();output.Clear();
            for(int ordinal=0;ordinal<rules.Count;ordinal++)
            {
                var rule=rules[ordinal];int chara=-1,dress=-1,color=-1;
                if(rule.Mode==1)
                {
                    int position=rule.PositionId-1;if(position<0||position>=characterCount)continue;var source=characters[position];chara=source.CharaId;dress=source.DressIds[0];color=source.DressColorIds[0];if(rule.CharaId>-1)chara=rule.CharaId;if(rule.DressId>-1)dress=rule.DressId;if(rule.DressColorId>=0)color=rule.DressColorId;
                }
                else if(rule.Mode==2){chara=rule.CharaId;dress=rule.DressId;color=rule.DressColorId;}
                if(chara==1&&shuffled.Length>0)chara=shuffled[ordinal%shuffled.Length];
                if(chara<1000||chara>9999||dress<=-1)continue;int? count=dressColorCount(dress);if(!count.HasValue)continue;if(color<0||(color>0&&count.Value<=1))color=0;else if(color>=count.Value&&color>0)color%=count.Value;
                output.Add(new MiniCharacterInfo{CharaId=chara,MobId=0,DressId=dress,DressColorId=color});
            }
        }
    }
}
