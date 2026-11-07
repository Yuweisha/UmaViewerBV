using System;


namespace AnimateToUnity
{
    // AnMotion.UpdateState/UpdateTime and AnMotionParameter.UpdateMotionTime.
    // The root supplies its global clock, frame interval and source frame rate.
    public sealed class AnMotionClock
    {
        public sealed class Context
        {
            public float CurrentTime,DeltaTime,FrameTime=1f/60f,SourceFrameTime=1f/30f;
        }
        public readonly AnMotionParameter Parameter;
        public readonly Context Root;
        public int State,TimeMode,LabelIndex,NextLabelIndex;
        public string LabelName,NextLabelName;
        public float Begin,End,CurrentTime,PreviousTime,ObjectTime,ObjectTimeWithoutLastFrame,FixObjectTime,RestCurrentTime;
        public float Speed=1f;
        public Action Start,Loop,Finish,FlStart,FlLoop,FlFinish;
        public bool PendingStart,PendingFinish,PendingFlStart,PendingFlFinish;
        public Action<AnMotionClock,string> SetActions;
        const float ObjectTimeAddValue=.0001f;
        static float F(float value)=>AnFloat.Round(value);
        public AnMotionClock(AnMotionParameter parameter,Context root){Parameter=parameter;Root=root;}
        public static float Limit(float value,float min,float max)=>value>max?max:value<min?min:value;
        AnLabelParameter GetLabel(string name)
        {foreach(var label in Parameter._labelParamList)if(label._name==name)return label;return null;}
        int GetIndex(string name)
        {for(int i=0;i<Parameter._labelParamList.Length;i++)if(Parameter._labelParamList[i]._name==name)return i;return -1;}
        public bool Play(string name,float offset=0f,bool paused=false)
        {
            var label=GetLabel(name);if(label==null)return false;
            State=paused?1:0;SetLabel(label,F(label._timeRange.x+offset));PreviousTime=float.MaxValue;UpdateTime();return true;
        }
        void SetLabel(AnLabelParameter label,float time)
        {
            LabelName=label._name;LabelIndex=GetIndex(LabelName);Begin=label._timeRange.x;End=label._timeRange.y;
            NextLabelName=label._nextLabel;NextLabelIndex=GetIndex(NextLabelName);CurrentTime=time;SetActions?.Invoke(this,LabelName);
        }
        bool Invoke(Action callback)
        {float before=CurrentTime;callback();return before==CurrentTime;}
        bool InvokeStart()
        {
            if(PendingStart){PendingStart=false;if(!Invoke(Start))return false;}
            if(PendingFlStart){PendingFlStart=false;if(!Invoke(FlStart))return false;}
            return true;
        }
        bool InvokeFinish()
        {
            if(PendingFinish){PendingFinish=false;if(!Invoke(Finish))return false;}
            if(PendingFlFinish){PendingFlFinish=false;if(!Invoke(FlFinish))return false;}
            return true;
        }
        bool InvokeLoop()
        {if(Loop!=null&&!Invoke(Loop))return false;return FlLoop==null||Invoke(FlLoop);}
        public void UpdateLabels()
        {
            if(TimeMode==1&&LabelIndex==NextLabelIndex)
            {
                if(!InvokeLoop())return;
                if(Root!=null&&Begin<=Root.CurrentTime)
                {
                    float time=F(F(F(Root.CurrentTime-Begin)%F(End-Begin))+Begin);
                    SetLabel(GetLabel(LabelName),time);
                }
                return;
            }
            if(!InvokeStart())return;
            float end=End;
            if(LabelIndex>=NextLabelIndex&&NextLabelName!=""&&Root!=null)end=Limit(end,Begin,F(End-Root.SourceFrameTime));
            if(CurrentTime<end){InvokeLoop();return;}
            if(NextLabelName=="")
            {State=1;InvokeFinish();return;}
            if(!InvokeFinish())return;
            var next=GetLabel(NextLabelName);float over=F(CurrentTime-End);
            if(over<0f||(Root!=null&&over>Root.SourceFrameTime))over=0f;
            SetLabel(next,F(over+next._timeRange.x));
        }
        public void UpdateState()
        {
            if(State!=0)return;
            UpdateLabels();if(TimeMode==1&&LabelIndex==NextLabelIndex)return;
            CurrentTime=F(CurrentTime+F(Root.DeltaTime*Speed));
        }
        public void UpdateTime()
        {
            if(CurrentTime==PreviousTime)return;
            float remainder=F(CurrentTime%Root.FrameTime);RestCurrentTime=remainder;
            if(.001f>remainder&&remainder>0f)CurrentTime=F(CurrentTime-remainder);
            else if(remainder>F(Root.FrameTime-.001f)&&Root.FrameTime>remainder)
                CurrentTime=F(CurrentTime+F(Root.FrameTime-remainder));
            CurrentTime=Limit(CurrentTime,Begin,End);
            ObjectTime=Limit(CurrentTime,F(Begin+ObjectTimeAddValue),F(End-ObjectTimeAddValue));
            ObjectTimeWithoutLastFrame=Limit(CurrentTime,F(Begin+ObjectTimeAddValue),F(End-Root.SourceFrameTime));
            FixObjectTime=ObjectTimeWithoutLastFrame;
            if(NextLabelIndex!=-1&&NextLabelIndex>LabelIndex&&F(End-Begin)>=F(Root.SourceFrameTime+ObjectTimeAddValue))FixObjectTime=ObjectTime;
        }
        // Native FlashPlayer intentionally carries a shortened seek value into
        // subsequent motions when a looping label reduces it.
        public static void SetCurrentTimeAll(System.Collections.Generic.IEnumerable<AnMotionClock> motions,float seekTime)
        {
            foreach(var motion in motions)
            {
                if(motion.LabelIndex==motion.NextLabelIndex)seekTime=F(seekTime%F(motion.End-motion.Begin));
                motion.CurrentTime=F(seekTime+motion.Begin);
            }
        }
    }
}
