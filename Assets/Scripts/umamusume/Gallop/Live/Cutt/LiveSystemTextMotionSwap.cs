using System;
using System.Collections.Generic;

namespace Gallop.Live.Cutt
{
    public sealed class LiveSystemTextMotionSwap
    {
        public struct Build
        {
            public int CharacterId,DressId,HeightId,BodyShapeId,BustId,ViewId;
            public uint Scene;
        }
        public SwapIdleMotionData.SwapMotionData IdleMotion{get;private set;}
        public readonly Dictionary<string,EventSwapMotionData.SwapMotionData> Events=new Dictionary<string,EventSwapMotionData.SwapMotionData>();
        readonly Func<string,int?> eventMotionType;
        public static LiveSystemTextMotionSwap Load(Build build)
        {
            var idle=LiveEffectController.FindResourceEntry("3d/Animator/ast_swap_idle_motion");
            var events=LiveEffectController.FindResourceEntry("3d/Animator/ast_event_swap_motion");
            return new LiveSystemTextMotionSwap(idle!=null?idle.Get<SwapIdleMotionData>():null,
                events!=null?events.Get<EventSwapMotionData>():null,build,UmaDatabaseController.Instance.ReadSystemTextEventMotionType);
        }
        public LiveSystemTextMotionSwap(SwapIdleMotionData idle,EventSwapMotionData events,Build build,Func<string,int?> lookup)
        {
            eventMotionType=lookup;
            if(idle!=null)
            {
                var predicates=new Predicate<SwapIdleMotionData.SwapData>[]
                {
                    r=>r.DressId==build.DressId&&r.CharaId==build.CharacterId,
                    r=>r.DressId==build.DressId&&r.CharaId<=0,
                    r=>r.DressId<=0&&r.CharaId==build.CharacterId
                };
                foreach(var predicate in predicates)
                {
                    // Original TryGetMotion only examines the first matching
                    // row in each pass, then searches that row for the view.
                    var row=Array.Find(idle.SwapDataArray,predicate);
                    if(row==null)continue;
                    IdleMotion=Array.Find(row.MotionDataArray,r=>r.TargetView==build.ViewId);
                    if(IdleMotion!=null)break;
                }
            }
            if(events==null)return;
            foreach(var row in events.SwapDataArray)
                if(row.DressId!=-1&&row.DressId==build.DressId)
                    foreach(var motion in row.MotionDataArray)if(Matches(motion,build))Add(motion);
            foreach(var row in events.SwapDataArray)
                if(row.DressId==-1)
                    foreach(var motion in row.MotionDataArray)
                        if(Matches(motion,build)&&!Events.ContainsKey(motion.MotionName))Add(motion);
        }
        static bool Matches(EventSwapMotionData.SwapMotionData motion,Build b)=>
            (motion.CharaId==-1||motion.CharaId==b.CharacterId)&&(motion.HeightId==-1||motion.HeightId==b.HeightId)&&
            (motion.BodyShapeId==-1||motion.BodyShapeId==b.BodyShapeId)&&(motion.BustId==-1||motion.BustId==b.BustId)&&
            (motion.SwapScene&b.Scene)!=0;
        static int Priority(EventSwapMotionData.SwapMotionData motion)=>(motion.CharaId!=-1?100:0)+
            (motion.BustId!=-1?1:0)+(motion.BodyShapeId!=-1?1:0)+(motion.HeightId!=-1?1:0);
        void Add(EventSwapMotionData.SwapMotionData motion)
        {
            if(Events.TryGetValue(motion.MotionName,out var previous))
            {
                if(Priority(previous)<Priority(motion))
                {
                    // Native stores the alias and its primary in one shared
                    // array. Updating either replaces both entries.
                    var aliases=new List<string>();
                    foreach(var pair in Events)if(ReferenceEquals(pair.Value,previous))aliases.Add(pair.Key);
                    foreach(string alias in aliases)Events[alias]=motion;
                }
                return;
            }
            Events.Add(motion.MotionName,motion);
            if(motion.SwapScene==uint.MaxValue||(motion.SwapScene&4)==0)Events.Add(motion.MotionName+"_mirror",motion);
        }
        public void Apply(LiveSystemTextMotionSet motion,List<string> paths)
        {
            string selected=string.Empty;
            if(IdleMotion!=null)
            {
                selected=IdleMotion.SwapIdleMotionName;
                if(eventMotionType(selected)==-2)
                    for(int i=0;i<paths.Count;i++)paths[i]=paths[i].Replace("idle01",selected);
                else
                {
                    var swap=new LiveSystemTextMotionSet{BodyMotion=selected,BodyMotionType=1,BodyMotionPathSegment=motion.BodyMotionPathSegment,
                        BodyMotionPlayType=motion.BodyMotionPlayType};
                    // Mirror comes from the original motion, not the swap name.
                    bool mirror=motion.BodyMotion.Contains("_mirror");
                    var replacements=new List<string>();
                    LiveSystemTextBodyResources.GetCommonPaths(swap.BodyMotion,swap.BodyMotionPlayType,swap.BodyMotionPathSegment,mirror,replacements);
                    for(int i=0;i<paths.Count;i++)if(paths[i].Contains("idle01"))paths[i]=replacements[i];
                }
            }
            if(string.IsNullOrEmpty(selected))selected=motion.BodyMotion;
            if(Events.TryGetValue(selected,out var entry))
                for(int i=0;i<paths.Count;i++)paths[i]=paths[i].Replace(entry.MotionName,entry.SwapMotionName);
        }
    }
}
