using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Gallop.Live.Cutt;

namespace Gallop.Live
{
    // Mini Live calls DrivenKeyComponent.PlayEar with duration=0. This restores
    // that ear clip/layer branch; target weights and bone output have their own
    // consumer. The general face/blend API is not hosted by this class.
    public sealed class LiveMiniEarClipRuntime : ILiveMiniEarAnimation,IDisposable
    {
        readonly Animator animator;
        PlayableGraph graph;
        readonly AnimationMixerPlayable[] mixers=new AnimationMixerPlayable[2];
        AnimationClipPlayable earPlayable;
        public DrivenKeyAnimation Animation {get;private set;}
        public bool IsPlayingEarAnimation {get;private set;}
        public bool IsEarPlayFrame {get;private set;}
        public bool RandomEarEnabled {get;private set;}=true;
        public readonly float[] LayerTime=new float[2];
        public readonly float[] LayerSpeed=new float[2];
        public readonly bool[] ManualTime=new bool[2];
        bool forceDisconnected;
        public event Action<string,AnimationClip,double> EngineWrite;
        public LiveMiniEarClipRuntime(Animator animator)
        {
            this.animator=animator;graph=PlayableGraph.Create();graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);var layers=AnimationLayerMixerPlayable.Create(graph,2);
            for(int i=0;i<2;i++){mixers[i]=AnimationMixerPlayable.Create(graph,1);graph.Connect(mixers[i],0,layers,i);layers.SetInputWeight(i,1f);}
            var output=AnimationPlayableOutput.Create(graph,"Animation",animator);output.SetSourcePlayable(layers);graph.Play();
        }
        static float F(float value)=>LiveTimelineControl.MonitorFloat32(value);
        public void PlayEar(DrivenKeyAnimation animation,float duration,float normalizedTime,bool forcePrevWeight)
        {
            if(animation==null)return;if(duration!=0||forcePrevWeight)throw new NotSupportedException("Mini Live zero-duration ear channel");Animation=animation;if(!animator.gameObject.activeSelf)return;
            var clip=animation.clip;clip.wrapMode=clip.isLooping?WrapMode.Loop:WrapMode.Once;EngineWrite?.Invoke("AnimationClip.wrapMode",clip,(int)clip.wrapMode);
            if(earPlayable.IsValid()){graph.Disconnect(mixers[1],0);earPlayable.Destroy();}
            earPlayable=AnimationClipPlayable.Create(graph,clip);EngineWrite?.Invoke("AnimationClipPlayable.Create",clip,0);forceDisconnected=true;LayerTime[1]=F(clip.length*normalizedTime);animator.enabled=true;EngineWrite?.Invoke("Animator.enabled",null,1);
            for(int i=0;i<2;i++)LayerSpeed[i]=ManualTime[i]?0f:1f;IsPlayingEarAnimation=true;IsEarPlayFrame=true;RandomEarEnabled=false;
        }
        public void SetEarTime(float time){if(animator.gameObject.activeSelf){LayerTime[1]=time;IsPlayingEarAnimation=true;IsEarPlayFrame=true;}}
        public void SetDrivenKeySpeed(float speed){for(int i=0;i<2;i++)LayerSpeed[i]=speed;}
        public double CurrentPlayableTime=>earPlayable.IsValid()?earPlayable.GetTime():0;
        public AnimationClip CurrentPlayableClip=>earPlayable.IsValid()?earPlayable.GetAnimationClip():null;
        public void UpdateAndEvaluate(float deltaTime)
        {
            if(animator.enabled)
            {
                for(int i=0;i<2;i++)LayerTime[i]=F(LayerTime[i]+F(deltaTime*LayerSpeed[i]));mixers[0].SetInputWeight(0,0);EngineWrite?.Invoke("Mixer0.SetInputWeight",null,0);
                if(Animation!=null&&earPlayable.IsValid())
                {
                    if(forceDisconnected){graph.Disconnect(mixers[1],0);EngineWrite?.Invoke("Mixer1.Disconnect",null,0);forceDisconnected=false;}
                    mixers[1].SetInputWeight(0,1);EngineWrite?.Invoke("Mixer1.SetInputWeight",null,1);
                    // Every authored Mini call sets a new clip and forces reconnect.
                    if(!mixers[1].GetInput(0).IsValid()){graph.Connect(earPlayable,0,mixers[1],0);EngineWrite?.Invoke("Mixer1.Connect",Animation.clip,0);}
                    earPlayable.SetTime((double)LayerTime[1]);EngineWrite?.Invoke("Playable.SetTime",Animation.clip,(double)LayerTime[1]);
                }
                else{mixers[1].SetInputWeight(0,0);EngineWrite?.Invoke("Mixer1.SetInputWeight",null,0);}
            }
            graph.Evaluate();EngineWrite?.Invoke("PlayableGraph.Evaluate",null,0);
        }
        public void Dispose(){if(graph.IsValid())graph.Destroy();}
    }
}
