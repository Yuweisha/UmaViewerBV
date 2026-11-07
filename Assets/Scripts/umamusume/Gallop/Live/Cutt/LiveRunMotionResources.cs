using System.Globalization;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    public readonly struct LiveRunMotionIdentity
    {
        public readonly int RunningType,Personality;
        public LiveRunMotionIdentity(int runningType,int personality){RunningType=runningType;Personality=personality;}
    }
    public static class LiveRunMotionResources
    {
        static readonly string[] Prefixes={"_run05_{0}","_run02_{0}","_run03_{0}"};
        public static string[] GetPaths(int personality,int runningType,int dressId,RaceSwapMotionData table)
        {
            int folder=personality,motionType=personality;
            if(table!=null)foreach(var row in table.SwapDataArray)
                if(row.DressId==dressId&&(row.Category&(1<<1))!=0){folder=99;motionType=row.Type;break;}
            string style=runningType==2?"pitch":runningType==3?"stride":"base";var paths=new string[3];
            for(int i=0;i<3;i++)paths[i]=string.Format(CultureInfo.InvariantCulture,"3d/Motion/RaceMain/Body/Type{0:00}/anm_{1}_type{3:00}{2}",folder,"rac",string.Format(CultureInfo.InvariantCulture,Prefixes[i],style),motionType);
            return paths;
        }
        public static RaceSwapMotionData LoadTable()
        {
            var db=UmaDatabaseController.Instance;
            return db.MetaEntries.TryGetValue("race/racemain/ast_race_swap_motion",out var entry)?entry.Get<RaceSwapMotionData>():null;
        }
        public static AnimationClip[] LoadClips(LiveRunMotionIdentity identity,int dressId,RaceSwapMotionData table)
        {
            string[] paths=GetPaths(identity.Personality,identity.RunningType,dressId,table);var result=new AnimationClip[paths.Length];
            for(int i=0;i<paths.Length;i++)
            {
                var entry=LiveEffectController.FindResourceEntry(paths[i]);
                if(entry!=null)result[i]=entry.Get<AnimationClip>();
                if(result[i]==null)Debug.LogError("[LiveRunMotionResources] Missing original Live Run resource: "+paths[i]);
            }
            return result;
        }
    }
}
