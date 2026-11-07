using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Gallop.Live
{
    // Separate sampler on the original DrivenKey locator hierarchy. It writes
    // ear weight banks only; it never enables Container.ProcessLocator, which
    // would overwrite independently driven eye/mouth/brow inputs.
    public sealed class LiveEarAnimationPlayer : IDisposable
    {
        readonly Transform[,] locators = new Transform[2,17];
        readonly PlayableGraph graph;
        readonly AnimationMixerPlayable earMixer;
        AnimationClipPlayable playable;
        bool reconnect;
        public DrivenKeyAnimation Animation { get; private set; }
        public float Time { get; private set; }
        public float Speed { get; private set; }
        public bool Playing { get; private set; }
        public bool FirstFrame { get; private set; }
        public double CurrentPlayableTime => playable.IsValid()?playable.GetTime():0.0;
        public Transform[,] Locators => locators;

        public LiveEarAnimationPlayer(GameObject locator)
        {
            var nodes = locator.GetComponentsInChildren<Transform>(true).ToDictionary(node=>node.name);
            for (int side=0;side<2;side++)for(int ear=0;ear<17;ear++)
                nodes.TryGetValue((side==0?"Ear_R__":"Ear_L__")+((EarType)ear),out locators[side,ear]);
            var animator=locator.GetComponent<Animator>(); animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            graph=PlayableGraph.Create("Live random ear sampler");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var layers=AnimationLayerMixerPlayable.Create(graph,2);
            var faceMixer=AnimationMixerPlayable.Create(graph,1);earMixer=AnimationMixerPlayable.Create(graph,1);
            graph.Connect(faceMixer,0,layers,0);graph.Connect(earMixer,0,layers,1);layers.SetInputWeight(0,1);layers.SetInputWeight(1,1);
            faceMixer.SetInputWeight(0,0);
            var output=AnimationPlayableOutput.Create(graph,"DrivenKey ear",animator);output.SetSourcePlayable(layers);graph.Play();
        }

        public void Play(DrivenKeyAnimation animation, LiveEarWeightTransition weights,float duration=0f)
        {
            if(animation==null)return;
            Animation=animation;Time=0f;Speed=1f;Playing=true;FirstFrame=true;
            var clip=animation.clip;clip.wrapMode=clip.isLooping?WrapMode.Loop:WrapMode.Once;
            if(playable.IsValid()){graph.Disconnect(earMixer,0);playable.Destroy();}
            playable=AnimationClipPlayable.Create(graph,clip);reconnect=true;
            weights.SetAnimation(animation,duration);
        }

        public void Stop() { Playing=false;Speed=0f; }
        public void Clear(){Stop();Animation=null;earMixer.SetInputWeight(0,0f);}

        public void Tick(float delta, LiveEarWeightTransition weights)
        {
            if (Animation!=null)
            {
                float step=(float)((double)delta*Speed);Time=(float)((double)Time+step);
                if(reconnect){graph.Disconnect(earMixer,0);graph.Connect(playable,0,earMixer,0);reconnect=false;}
                earMixer.SetInputWeight(0,1f);playable.SetTime(Time);graph.Evaluate(0f);
                if(Playing)
                {
                    weights.ApplyAnimation(Animation,locators);
                    if(!FirstFrame&&Animation.clip.wrapMode!=WrapMode.Loop&&Time/Animation.clip.length>=1f)Playing=false;
                }
            }
            FirstFrame=false;
        }

        public void Dispose() { if(graph.IsValid())graph.Destroy(); }
    }
}
