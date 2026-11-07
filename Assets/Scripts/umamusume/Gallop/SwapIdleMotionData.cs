using System;

namespace Gallop
{
    public class SwapIdleMotionData:SwapMotionDataBase
    {
        [Serializable] public class SwapMotionData{public int TargetView;public string SwapIdleMotionName;}
        [Serializable] public class SwapData:SwapDataBase{public int CharaId;public SwapMotionData[] MotionDataArray;}
        public new SwapData[] SwapDataArray;
    }
}
