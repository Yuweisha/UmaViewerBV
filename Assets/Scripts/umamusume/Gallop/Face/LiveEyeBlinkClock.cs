using System;

namespace Gallop.Live
{
    // Scalar UpdateBlink contract. Face submissions, trace controls and model
    // request/ignore gates are owned by the production host, not this clock.
    public sealed class LiveEyeBlinkClock
    {
        public int Step=5,DoubleStep,SingleCount;
        public float TimeCount,TimeMax;
        public bool AutoBlink,AutoDouble=true,Force,WaitOpen;
        public bool HasSetting;
        public float ForceCloseTime,IntervalMin=3f,IntervalMax=5f;
        public event Action<int,float> FaceStep;
        public Func<float,float,float> RandomRange;
        static float F(float value)=>Cutt.LiveTimelineControl.MonitorFloat32(value);
        public void RequestInternal(bool force)
        {Force=force;DoubleStep=2;SingleCount=0;Step=0;TimeCount=0;TimeMax=0.03f;}
        public void Update(float delta,bool suppressEyeBlink)
        {
            if(Step==3&&WaitOpen)return;
            if(Step==5&&(suppressEyeBlink||!AutoBlink))return;
            TimeCount=F(TimeCount+delta);
            if(TimeCount<TimeMax&&!Force)return;
            if(Step==2)Force=false;
            bool doubleBlink=AutoDouble&&DoubleStep==1;
            int next;float wait=0f;
            switch(Step)
            {
                case 0:next=1;break;
                case 1:next=2;wait=0.03f;if(Force&&HasSetting)wait=F(wait+ForceCloseTime);break;
                case 2:next=3;break;
                case 3:next=4;break;
                case 4:next=doubleBlink?0:5;if(!doubleBlink)wait=RandomRange(IntervalMin,IntervalMax);break;
                case 5:next=0;break;
                default:throw new InvalidOperationException("Unexpected blink step "+Step);
            }
            TimeCount=0f;TimeMax=F(wait+0.03f);
            if(AutoDouble)
            {
                if(Step==0)
                {
                    if(DoubleStep>=2)DoubleStep=0;
                    else if(DoubleStep==0)
                    {if(SingleCount<1)SingleCount++;else{DoubleStep=1;SingleCount=0;}}
                }
                else if(Step==4&&DoubleStep==1)DoubleStep=2;
            }
            FaceStep?.Invoke(Step,0.03f);
            Step=next;
        }
    }
}
