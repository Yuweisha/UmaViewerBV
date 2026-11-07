using System;
using Gallop.Live.Cutt;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Gallop.Live
{
    // The original SimplePlayableAnimator layout is three two-input mixers
    // connected at weight1; layer1 is additive. Race uses layers0 and2.
    public sealed class LiveRunUnityPlayable:ILiveSimplePlayableEngine,ILiveAnimationModePlayable,IDisposable
    {
        sealed class Handle{public AnimationClipPlayable Value;}
        readonly PlayableGraph graph;
        readonly AnimationLayerMixerPlayable layers;
        readonly AnimationMixerPlayable[] mixers=new AnimationMixerPlayable[3];
        readonly Animator animator;
        public readonly LiveRunMotionRuntime Motion;
        public readonly LiveSimplePlayableRuntime Simple;
        public bool IsRaceMode{get;private set;}
        float speed=1f;
        public float Speed{get=>speed;set{speed=value;Simple.Speed=value;}}
        public LiveRunUnityPlayable(Animator animator)
        {
            this.animator=animator;Motion=new LiveRunMotionRuntime(this);Simple=new LiveSimplePlayableRuntime(this);
            graph=PlayableGraph.Create();graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            layers=AnimationLayerMixerPlayable.Create(graph,3);
            for(int i=0;i<3;i++){mixers[i]=AnimationMixerPlayable.Create(graph,2);layers.ConnectInput(i,mixers[i],0,1f);}
            layers.SetLayerAdditive(1,true);
            var output=AnimationPlayableOutput.Create(graph,"Animation",animator);output.SetSourcePlayable(layers);graph.Play();
        }
        public bool SameClip(object a,object b)=>(AnimationClip)a==(AnimationClip)b;
        public bool HasClip(object clip)=>(AnimationClip)clip!=null;
        public bool IsValid(object p)=>p is Handle h&&h.Value.IsValid();
        public object Create(object clip)=>new Handle{Value=AnimationClipPlayable.Create(graph,(AnimationClip)clip)};
        public void Destroy(object p)=>((Handle)p).Value.Destroy();
        public void Disconnect(int mixer,int port)=>graph.Disconnect(mixers[mixer],port);
        public void Connect(object p,int mixer,int port)=>graph.Connect(((Handle)p).Value,0,mixers[mixer],port);
        public void SetWeight(int mixer,int port,float v)=>mixers[mixer].SetInputWeight(port,v);
        public float GetWeight(int mixer,int port)=>mixers[mixer].GetInputWeight(port);
        public float Length(object clip)=>((AnimationClip)clip).length;
        public void SetTime(object p,double time)=>((Handle)p).Value.SetTime(time);
        public void Rebind()=>animator.Rebind();
        public bool Enabled{set=>animator.enabled=value;}
        public void SetRaceMode(Action callback){IsRaceMode=true;Speed=0f;callback?.Invoke();}
        public void ResetRaceMode(){IsRaceMode=false;Motion.ResetRaceClips();Speed=1f;}
#if UNITY_EDITOR
        public bool EditorGraphValid=>graph.IsValid();
        public double EditorTime(object p)=>((Handle)p).Value.GetTime();
        public float EditorWeight(int mixer,int port)=>mixers[mixer].GetInputWeight(port);
#endif
        public void Evaluate()=>graph.Evaluate(0f);
        public void UpdateSimpleMotion(float deltaTime)
        {
            Simple.Update(deltaTime);
            if(Simple.Disconnected&&animator.cullingMode!=AnimatorCullingMode.AlwaysAnimate)
            {layers.Pause();graph.Evaluate();layers.Play();}
        }
        public void Dispose(){if(graph.IsValid())graph.Destroy();}
    }
}
