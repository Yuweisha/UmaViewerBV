using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Gallop.Live
{
    // Original Live3DSettings.Setup 4e68900 and TryAddVariationIdList 4e96920.
    // This list is independent of the worksheet variant ID.
    public static class LiveVariationSelection
    {
        public struct Character {public int CharaId;public int[] DressIds;}
        public struct Rule {public int PositionId,CharaId,DressId,VariationId;}

        public static List<Rule> ParseRules(string csv)
        {
            var result=new List<Rule>();if(string.IsNullOrEmpty(csv))return result;
            using(var reader=new StringReader(csv))
            {
                // Original parser's first CSV row is a header and its four
                // numeric columns are positional, all must be nonnegative.
                reader.ReadLine();string line;
                while((line=reader.ReadLine())!=null)
                {
                    var cells=line.Split(',');if(cells.Length<4)continue;var values=new int[4];bool valid=true;
                    for(int i=0;i<4;i++)valid&=int.TryParse(cells[i],NumberStyles.Integer,CultureInfo.InvariantCulture,out values[i])&&values[i]>=0;
                    if(valid)result.Add(new Rule{PositionId=values[0],CharaId=values[1],DressId=values[2],VariationId=values[3]});
                }
            }
            return result;
        }

        public static bool TryAddVariationId(List<int> selected,IReadOnlyList<Rule> rules,int positionId,int charaId,int[] dressIds)
        {
            if(selected==null)throw new ArgumentNullException(nameof(selected));
            if(rules==null)return false;
            foreach(var rule in rules)
            {
                if(rule.PositionId>0&&rule.PositionId!=positionId)continue;
                if(rule.CharaId>0&&rule.CharaId!=charaId)continue;
                if(rule.DressId>0&&dressIds!=null&&Array.IndexOf(dressIds,rule.DressId)<0)continue;
                if(selected.Contains(rule.VariationId))continue;
                selected.Add(rule.VariationId);return true;
            }
            return false;
        }

        public static List<int> Select(int explicitLiveVariationId,int defaultPattern,int min,int max,bool useCharaData,
            IReadOnlyList<Character> characters,IReadOnlyList<Rule> rules,Func<int,int,int> randomRange)
        {
            var selected=new List<int>();
            if(explicitLiveVariationId>=0){selected.Add(explicitLiveVariationId);return selected;}
            if(useCharaData&&characters!=null)
                for(int index=0;index<characters.Count&&index<20;index++)
                    TryAddVariationId(selected,rules,index+1,characters[index].CharaId,characters[index].DressIds);
            if(selected.Count>0)return selected;
            if(defaultPattern!=1){selected.Add(0);return selected;}
            int low=min<1?0:min,high=max<1?0:max;
            if(low>high){int swap=low;low=high;high=swap;}
            if(low==high)selected.Add(low);
            else
            {
                if(randomRange==null)throw new ArgumentNullException(nameof(randomRange));
                selected.Add(randomRange(low,unchecked(high+1)));
            }
            return selected;
        }
    }
}
