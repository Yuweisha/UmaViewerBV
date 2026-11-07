using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Gallop.Live
{
    // Facial layer on its own DrivenKey locator. Ear playback has a separate
    // owner; this sampler writes only the original five facial weight groups.
    public sealed class LiveSystemTextDrivenKeyPlayer:IDisposable
    {
        readonly GameObject locator;
        readonly PlayableGraph graph;
        readonly AnimationMixerPlayable mixer;
        readonly Dictionary<string,Transform> nodes=new Dictionary<string,Transform>();
        AnimationClipPlayable playable;
        public DrivenKeyAnimation Animation{get;private set;}
        public float Time{get;private set;}
        public bool Playing{get;private set;}
        public IReadOnlyDictionary<string,Transform> Nodes=>nodes;
        public LiveSystemTextDrivenKeyPlayer(GameObject locator)
        {
            this.locator=locator;foreach(var node in locator.GetComponentsInChildren<Transform>(true))nodes[node.name]=node;
            var animator=locator.GetComponent<Animator>();animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            graph=PlayableGraph.Create("Live SystemText facial");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer=AnimationMixerPlayable.Create(graph,1);var output=AnimationPlayableOutput.Create(graph,"facial",animator);output.SetSourcePlayable(mixer);graph.Play();
        }
        public void Play(DrivenKeyAnimation animation)
        {
            if(playable.IsValid()){graph.Disconnect(mixer,0);graph.DestroyPlayable(playable);}
            Animation=animation;Time=0;Playing=true;animation.clip.wrapMode=animation.clip.isLooping?WrapMode.Loop:WrapMode.Once;
            playable=AnimationClipPlayable.Create(graph,animation.clip);playable.SetApplyFootIK(false);playable.SetApplyPlayableIK(false);playable.SetSpeed(0);
            graph.Connect(playable,0,mixer,0);mixer.SetInputWeight(0,1);
        }
        public void Tick(float delta,Gallop.FaceDrivenKeyTarget target)
        {
            if(Animation==null||!Playing)return;
            Time=(float)((double)Time+delta);playable.SetTime(Time);graph.Evaluate(0);
            target.ApplyLiveSystemTextAnimation(Animation,nodes);
        }
        public void Stop(){Playing=false;}
        public void Clear(){Animation=null;Playing=false;mixer.SetInputWeight(0,0);}
        public void Dispose(){if(graph.IsValid())graph.Destroy();if(locator!=null)UnityEngine.Object.Destroy(locator);}
    }
}
