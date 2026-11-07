using System;

namespace Gallop
{
    public class EventSwapMotionData:SwapMotionDataBase
    {
        [Serializable] public class SwapMotionData
        {
            public int CharaId;public string MotionName,SwapMotionName;
            public uint SwapScene;public int HeightId,BodyShapeId,BustId;
        }
        [Serializable] public class SwapData:SwapDataBase{public SwapMotionData[] MotionDataArray;}
        public new SwapData[] SwapDataArray;
    }
}
