using System;
using UnityEngine;

namespace Gallop
{
    public abstract class SwapMotionDataBase:ScriptableObject
    {
        [Serializable] public abstract class SwapDataBase{public int DressId;}
        // The actual source typetree serializes the derived typed array only.
        [NonSerialized] public SwapDataBase[] SwapDataArray;
    }
    // Original data asset reused by the Live Run loader. This restores its
    // serialized schema; it does not install any race gameplay controller.
    public class RaceSwapMotionData:SwapMotionDataBase
    {
        [Serializable] public class SwapData:SwapDataBase{public int Type;public int Category;}
        public new SwapData[] SwapDataArray;
    }
}
