using System;
using Gallop.Live.Cutt;

namespace Gallop.Live
{
    // UpdateEye's blink-request bookkeeping. Model callbacks remain explicit
    // so requests precede the ordinary eye submission, as in the original.
    public sealed class LiveEyeBlinkTimeline
    {
        public int BlinkRno,CurrentStart=-1,AutoStep;
        public bool Repress,UpdateAuto;
        public float AutoCount,AutoInterval;
        public Action ResetEyeCurrentTime,RequestBlink;
        public Func<bool> IsBlinking;
        public Func<float,float,float> RandomRange;

        public static LiveTimelineKey Select(LiveTimelineKey face,LiveTimelineKey eye)
        {
            bool hasFace=LiveFacialPartsResolver.IsActive(face),hasEye=LiveFacialPartsResolver.IsActive(eye);
            return hasEye&&(!hasFace||eye.frame>=face.frame)?eye:hasFace?face:null;
        }
        public bool Update(LiveTimelineKey face,LiveTimelineKey eye)
        {
            var selected=Select(face,eye);if(selected==null)return false;
            bool blink=((int)selected.attribute&0x20000)!=0;
            // Original UpdateEye writes true at 4e91c7b for a selected key.
            Repress=true;
            if(CurrentStart!=selected.frame)
            {
                BlinkRno=0;
                if(blink)ResetEyeCurrentTime?.Invoke();
            }
            if(blink)
            {
                if(BlinkRno==0){BlinkRno=1;RequestBlink?.Invoke();}
                else if(BlinkRno==1&&!IsBlinking())BlinkRno=2;
                if(BlinkRno<=1)Repress=true;
            }
            if(UpdateAuto)
            {
                if(AutoStep==0)AutoCount=0;
                else if(AutoStep==1&&!IsBlinking())
                {AutoStep=0;AutoCount=0;AutoInterval=RandomRange(3f,5f);}
            }
            CurrentStart=selected.frame;return true;
        }
    }
}
