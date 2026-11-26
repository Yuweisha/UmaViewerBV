using System;
using UnityEngine;

namespace Gallop
{
    public sealed class CharaTransformProcessData : ScriptableObject
    {
        public enum ProcessType { InverseYRotation_2Hierarchy, AddPosition_FromAngle }

        [Serializable]
        public sealed class TargetData
        {
            public ProcessType ProcessId;
            public string TargetTransformName;
            public int[] ValueParameterArray;
            public string[] StringParameterArray;
        }

        public TargetData[] TargetDataArray;
    }
}
