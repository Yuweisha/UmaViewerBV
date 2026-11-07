namespace Gallop.Live.Cutt
{
    public interface ILiveSimplePlayableEngine:ILiveRunPlayableEngine
    {
        float GetWeight(int mixer,int port);
    }
    // Original SimplePlayableAnimator: eleven clip slots, three two-input
    // layers, and separate main/secondary slots for states0..4.
    public sealed class LiveSimplePlayableRuntime
    {
        public sealed class ClipState
        {
            public int Index;public object Clip,Playable;public bool Changed;
        }
        public sealed class LayerState
        {
            public ClipState CurrentPlayable;
            public bool IsAdd;
            public int Current=-1,Previous=-1;
            public float Time,PreviousTime,BlendTime,BlendElapsed,BlendWeight=1f,BlendTimeScale=1f;
            public readonly int[] Connected={-1,-1};
        }
        readonly ILiveSimplePlayableEngine engine;
        public readonly ClipState[] Clips=new ClipState[11];
        public readonly LayerState[] Layers={new LayerState(),new LayerState{IsAdd=true},new LayerState()};
        public bool UseMainState=true;
        public float Speed=1f;
        public bool Disconnected{get;private set;}
        static float F(float value)=>LiveTimelineControl.MonitorFloat32(value);
        public LiveSimplePlayableRuntime(ILiveSimplePlayableEngine engine)
        {this.engine=engine;for(int i=0;i<Clips.Length;i++)Clips[i]=new ClipState{Index=i};}
        int Index(int state)=>state==5?10:unchecked(state*2+(UseMainState?0:1));
        public void SetClip(int state,object clip)
        {
            int index=Index(state);if(index<0||index>=Clips.Length)return;
            var value=Clips[index];if(engine.IsValid(value.Playable))engine.Destroy(value.Playable);
            value.Clip=clip;if(engine.HasClip(clip))value.Playable=engine.Create(clip);value.Changed=true;
        }
        public int GetState(int layer)
        {int state=Layers[layer].Current;return state>=0&&state<=10?state/2:-1;}
        void PlayIndex(int layer,int index,float time,float blendTime)
        {
            var value=Layers[layer];value.BlendElapsed=0f;value.BlendTime=blendTime;
            value.BlendWeight=blendTime>0f?0f:1f;value.Previous=value.Current;value.PreviousTime=value.Time;value.Time=time;value.Current=index;
        }
        public void Play(int layer,int state,float time)=>PlayIndex(layer,Index(state),time,0f);
        public void CrossFade(int layer,int state,float time,float duration)=>PlayIndex(layer,Index(state),time,duration);
        public void SetPlayTime(int layer,float time)=>Layers[layer].Time=time;
        public void StopTailAnimation()
        {
            var layer=Layers[2];
            if(layer.Current>=0&&layer.Current<Clips.Length)
            {
                layer.Current=-1;layer.CurrentPlayable=null;
                for(int port=0;port<2;port++){engine.Disconnect(2,port);layer.Connected[port]=-1;}
            }
            else for(int port=0;port<2;port++)if(engine.GetWeight(2,port)>0f)engine.SetWeight(2,port,0f);
        }
        void UpdateState(float delta)
        {
            foreach(var layer in Layers){layer.Time=F(delta+layer.Time);layer.PreviousTime=F(delta+layer.PreviousTime);}
            var first=Layers[0];
            if(first.Current>=0&&first.Current<Clips.Length&&engine.HasClip(Clips[first.Current].Clip)&&
                !(engine.Length(Clips[first.Current].Clip)>first.Time))
            {
                if(first.Current==2)PlayIndex(0,4,0f,0f);else if(first.Current==3)PlayIndex(0,5,0f,0f);
            }
            foreach(var layer in Layers)
            {
                if(0f>=layer.BlendTime||0f>=layer.BlendTimeScale)continue;
                layer.BlendElapsed=F(F(delta*layer.BlendTimeScale)+layer.BlendElapsed);
                if(!(layer.BlendTime>layer.BlendElapsed)){layer.BlendWeight=1f;layer.BlendTime=0f;}
                else layer.BlendWeight=F(layer.BlendElapsed/layer.BlendTime);
            }
        }
        bool Different(int connected,ClipState state)
        {
            if(state==null)return connected!=-1;
            if(connected==state.Index&&!state.Changed)return false;
            state.Changed=false;return true;
        }
        bool NeedConnect(LayerState layer,ClipState current,ClipState previous,bool crossFade)
        {
            if(Different(layer.Connected[0],current))return true;
            if(crossFade)return Different(layer.Connected[1],previous);
            if(layer.Connected[1]==-1)return false;
            if(previous!=null)previous.Changed=false;return true;
        }
        void PlayMotion(int mixer,LayerState layer,ClipState clip,int port,float time,bool connect)
        {
            if(connect){engine.Connect(clip.Playable,mixer,port);layer.Connected[port]=clip.Index;}
            engine.SetTime(clip.Playable,time);
        }
        void UpdateLayer(int index)
        {
            var layer=Layers[index];layer.CurrentPlayable=null;
            if(layer.Current<0||layer.Current>=Clips.Length||!engine.HasClip(Clips[layer.Current].Clip))
            {
                for(int port=0;port<2;port++)if(engine.GetWeight(index,port)>0f)engine.SetWeight(index,port,0f);
                return;
            }
            var current=Clips[layer.Current];var previous=layer.Previous!=-1?Clips[layer.Previous]:null;
            layer.CurrentPlayable=current;
            bool crossFade=previous!=null&&1f>layer.BlendWeight&&engine.HasClip(previous.Clip)&&!engine.SameClip(previous.Clip,current.Clip);
            if(layer.IsAdd)crossFade=false;
            bool connect=NeedConnect(layer,current,previous,crossFade);Disconnected|=connect;
            if(connect)for(int port=0;port<2;port++){engine.Disconnect(index,port);layer.Connected[port]=-1;}
            float weight=crossFade?layer.BlendWeight:1f;
            engine.SetWeight(index,0,weight);engine.SetWeight(index,1,F(1f-weight));
            PlayMotion(index,layer,current,0,layer.Time,connect);
            if(crossFade)PlayMotion(index,layer,previous,1,layer.PreviousTime,connect);
        }
        public void Update(float deltaTime)
        {
            Disconnected=false;UpdateState(F(Speed*deltaTime));
            for(int i=0;i<Layers.Length;i++)UpdateLayer(i);
        }
    }
}
