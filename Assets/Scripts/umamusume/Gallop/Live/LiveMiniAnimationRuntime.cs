using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Gallop.Live
{
    public interface ILiveMiniFacialAnimation
    {
        void OverrideAnimation(string name,AnimationClip clip);
        void Play(string name,int layer,float normalizedTime);
        bool AnimationEnabled {get;set;}
    }
    public interface ILiveMiniEarAnimation
    {
        void PlayEar(DrivenKeyAnimation animation,float duration,float offset,bool blend);
        void SetEarTime(float time);
        void SetDrivenKeySpeed(float speed);
    }
    // Native MinimumPlayableAnimator's body/position subset. Graph construction
    // follows PlayableAnimator ctor (no mixer in Minimum.OnInitialize).
    public sealed class LiveMiniClipPlayer : IDisposable
    {
        readonly PlayableGraph graph;
        readonly AnimationPlayableOutput output;
        AnimationClipPlayable playable;
        public bool IsLoop {get;set;}
        public bool IsValid=>playable.IsValid();
        public AnimationClip Clip=>IsValid?playable.GetAnimationClip():null;
        public double CurrentTime=>IsValid?playable.GetTime():0d;
        public Animator Animator {get;}
#if UNITY_EDITOR
        public event Action<string,AnimationClip,double> EngineWrite;
#endif
        public LiveMiniClipPlayer(Animator animator)
        {
            Animator=animator;if(animator==null)return;
            graph=PlayableGraph.Create();graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);output=AnimationPlayableOutput.Create(graph,"Animation",animator);graph.Play();
        }
        public void SetSpeed(float speed)
        {
            if(!IsValid)return;playable.SetSpeed(speed);
#if UNITY_EDITOR
            EngineWrite?.Invoke("Playable.SetSpeed",Clip,speed);
#endif
        }
        public void PlayAnimationClip(AnimationClip clip,float time)
        {
            if(clip!=null&&(!IsValid||Clip!=clip))
            {
                if(IsValid)
                {
#if UNITY_EDITOR
                    EngineWrite?.Invoke("Playable.Destroy",Clip,0);
#endif
                    playable.Destroy();
                }
                playable=AnimationClipPlayable.Create(graph,clip);
#if UNITY_EDITOR
                EngineWrite?.Invoke("AnimationClipPlayable.Create",clip,0);
#endif
                output.SetSourcePlayable(playable);
#if UNITY_EDITOR
                EngineWrite?.Invoke("PlayableOutput.SetSource",clip,0);
#endif
            }
            if(!IsValid)return;
            float value=IsLoop?Cutt.LiveTimelineControl.MonitorFloat32(time%Clip.length):time;playable.SetTime(value);
#if UNITY_EDITOR
            EngineWrite?.Invoke("Playable.SetTime",Clip,value);
#endif
        }
        public void Evaluate()=>graph.Evaluate(0f);
        public void Dispose(){if(IsValid)playable.Destroy();if(graph.IsValid())graph.Destroy();}
    }
    // Original MiniAnimationController51a85b0/51a8730/51a85e0/51a88c0.
    // Facial/ear sinks are the real model-controller boundary, not synthesized
    // animation outputs. A loaded owner must install those separately.
    public sealed class LiveMiniAnimationRuntime : ILiveMiniAnimationPlayer,IDisposable
    {
        public LiveMiniClipPlayer Body {get;}
        public LiveMiniClipPlayer Position {get;}
        readonly ILiveMiniFacialAnimation facial;
        readonly ILiveMiniEarAnimation ear;
        public LiveMiniAnimationRuntime(Animator body,Animator position,ILiveMiniFacialAnimation facial,ILiveMiniEarAnimation ear)
        {
            this.facial=facial;this.ear=ear;Body=new LiveMiniClipPlayer(body);Position=new LiveMiniClipPlayer(position);body.cullingMode=AnimatorCullingMode.AlwaysAnimate;position.cullingMode=AnimatorCullingMode.AlwaysAnimate;Body.SetSpeed(0f);Position.SetSpeed(0f);
        }
        static float F(float v)=>Cutt.LiveTimelineControl.MonitorFloat32(v);
        public void PlayBodyMotion(AnimationClip clip,float time,bool loop){Body.IsLoop=loop;Body.PlayAnimationClip(clip,time);}
        public void PlayPositionMotion(AnimationClip clip,float time,bool loop){Position.IsLoop=loop;Position.PlayAnimationClip(clip,time);}
        public void PlayFacialMotion(DrivenKeyAnimation animation,float time,bool loop)
        {
            if(animation==null||animation.clip==null)return;float length=animation.clip.length;time=Mathf.Max(time,0f);float normalized=time<=length?F(time/length):loop?F(F(time%length)/length):1f;facial.OverrideAnimation("anm_motion1",animation.clip);facial.Play("motion1",-1,normalized);facial.AnimationEnabled=false;
        }
        public void PlayEarMotion(DrivenKeyAnimation animation,float time,bool loop)
        {
            if(animation==null||animation.clip==null)return;float length=animation.clip.length;time=Mathf.Max(time,0f);if(time>length)time=loop?F(time%length):length;ear.PlayEar(animation,0f,0f,false);ear.SetEarTime(time);ear.SetDrivenKeySpeed(0f);
        }
        public void Dispose(){Body.Dispose();Position.Dispose();}
    }
}
