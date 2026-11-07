using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    // Original LoadBodySystemMotionClipList and PreloadIdleMotion keep
    // different null-clip policies. Their caches belong to one model owner.
    public sealed class LiveSystemTextBodyClipCache
    {
        public readonly Dictionary<int,List<AnimationClip>> Primary=new Dictionary<int,List<AnimationClip>>();
        public readonly Dictionary<int,List<AnimationClip>> Secondary=new Dictionary<int,List<AnimationClip>>();
        public List<AnimationClip> IdleClips{get;private set;}
        public LiveSystemTextMotionSet IdleMotion{get;private set;}
        readonly LiveSystemTextMotionSwap swap;
        readonly Func<string,AnimationClip> load;
        public LiveSystemTextBodyClipCache(LiveSystemTextMotionSwap swap,Func<string,AnimationClip> load)
        {this.swap=swap;this.load=load;}
        public void Clear(){Primary.Clear();Secondary.Clear();}
        public void Load(int systemTextId,LiveSystemTextMotionSet motion,int buildCharacterId,bool second)
        {
            var cache=second?Secondary:Primary;if(cache.ContainsKey(systemTextId))return;
            var paths=new List<string>();bool add=false;
            if(!LiveSystemTextBodyResources.GetPaths(motion,buildCharacterId,paths,ref add))return;
            swap.Apply(motion,paths);var clips=new List<AnimationClip>();
            foreach(string path in paths){var clip=load(path);if(clip!=null)clips.Add(clip);}
            if(clips.Count>0)cache[systemTextId]=clips;
        }
        public void LoadIdle(LiveSystemTextMotionSet motion,int buildCharacterId)
        {
            IdleClips=null;IdleMotion=null;if(motion==null)return;
            var paths=new List<string>();bool add=false;
            if(!LiveSystemTextBodyResources.GetPaths(motion,buildCharacterId,paths,ref add))return;
            swap.Apply(motion,paths);var clips=new List<AnimationClip>();
            foreach(string path in paths)clips.Add(load(path));
            if(clips.Count>0){IdleClips=clips;IdleMotion=motion;}
        }
    }
}
