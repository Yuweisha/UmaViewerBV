using System;

namespace Gallop.Live
{
    // Programmable-eye request and BeforeApplyDrivenKey owner contract.
    // Actual model registration and face-step weight submissions are separate.
    public sealed class LiveEyeBlinkOwner
    {
        static readonly int[] DefaultIgnore = {5,8,2,16,22};
        public readonly LiveEyeBlinkClock Clock = new LiveEyeBlinkClock();
        public bool Enabled=true,Paused,DisabledByOverride,CanBlink=true;
        public bool RequestClear,Updated;
        public FaceParts[] StoredRight,StoredLeft;
        public EyeBlinkSettingData Setting;
        public Action UpdateBlink;

        public static bool CheckIgnore(FaceParts[] right,FaceParts[] left,int[] ignored)
        {
            bool Contains(FaceParts[] parts)
            {
                if(parts==null||ignored==null)return false;
                foreach(var part in parts)
                    if(part._weight>0.000001f)
                        foreach(int id in ignored)if(part._faceParts==id)return true;
                return false;
            }
            return Contains(left)||Contains(right);
        }
        public void Clear()
        {
            Clock.TimeMax=Clock.RandomRange(Clock.IntervalMin,Clock.IntervalMax);
            Clock.Step=5;Clock.DoubleStep=0;Clock.Force=false;Clock.WaitOpen=false;RequestClear=false;
        }
        public void RequestBlink(bool force)
        {
            if(!Enabled||Paused||DisabledByOverride)return;
            if(Clock.Step==5)
            {
                if(force&&Setting!=null)
                {
                    float probability=Clock.RandomRange(0f,100f);
                    if(probability>Setting.ProbabilityOfOccurrence)return;
                    var ignored=Setting.IgnoreEyeBlinkType;
                    if(ignored!=null)
                    {
                        int[] ids=Array.ConvertAll(ignored,value=>(int)value);
                        if(CheckIgnore(StoredRight,StoredLeft,ids))return;
                    }
                }
                Clock.RequestInternal(force);
            }
            else if(force)
            {
                if(Clock.Step!=4&&Clock.Step!=1)Clear();
                Clock.Force=true;
            }
        }
        public void BeforeApplyProgrammable()
        {
            if(!Enabled||Paused||DisabledByOverride||!CanBlink)return;
            if(RequestClear)Clear();
            Updated=false;
            if(CheckIgnore(StoredRight,StoredLeft,DefaultIgnore))
            {
                if(Clock.Step==0){Clear();Clock.TimeCount=0f;}
                return;
            }
            UpdateBlink?.Invoke();Updated=true;
        }
    }
}
