using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    

    [System.Serializable]
    public class LiveTimelineKeyCharaMotionData : LiveTimelineKeyWithInterpolate
    {
        public override LiveTimelineKeyDataType dataType
        {
            get
            {
                return LiveTimelineKeyDataType.CharaMotionSequecne;
            }
        }
        public enum TargetMotionType { None = 0, AnimationClip1 = 1, AnimationClip2 = 2, AnimationClip3 = 3 }
        public enum DressConditionType { None = 0, Moesode = 1 }

        [System.Serializable]
        public class SwapMotionData
        {
            [System.Serializable]
            public class AndConditionData
            {
                public int TargetOrder;
                public bool ActiveCharaId;
                public int CharaId;
                public bool ActiveDressId;
                public int DressId;
                public DressConditionType DressCondition;
            }

            public TargetMotionType TargetMotion;
            public int TargetOrder;
            public bool ActiveCharaId;
            public int CharaId;
            public bool ActiveDressId;
            public int DressId;
            public DressConditionType DressCondition;
            public string SwapMotionName;
            public AndConditionData[] AndConditionDataArray;
            [System.NonSerialized] public AnimationClip SwapAnimationClip;
            [System.NonSerialized] public string SwapAnimationClipName;
            [System.NonSerialized] public bool IsEnabledSwapAnimationClip;
        }

        public string motionName;
        public string motionName2;
        public string motionName3;
        public AnimationClip clip;
        public AnimationClip clip2;
        public AnimationClip clip3;
        public SwapMotionData[] SwapMotionDataArray;
        public int overrideMotionSysTextId;
        public bool _useRandomMotionSystemText;
        public int _randomMotionGroupIndex;
        public int RandomMotionSystemTextId { get; set; }
        public int MotionSysTextId => _useRandomMotionSystemText ? RandomMotionSystemTextId : overrideMotionSysTextId;
        public bool UseOverrideSysTextAnimation => MotionSysTextId > 0;
        public bool UseOverrideAnimation => UseOverrideSysTextAnimation || _isUseRunMode;
        public bool useOverrideMotionFacial;
        public bool UseSecondMotion;
        public bool EyeTraceSmoothness;
        public int motionHeadFrame;
        public int playFrameLength;
        public float playSpeed;
        public bool loop;
        [SerializeField] private bool _isTimescaleDisabled;
        public bool IsTimescaleDisabled => _isTimescaleDisabled;
        public bool isMotionHeadFrameAll;
        public int[] motionHeadFrameSeparetes;
        public string StateName;
        public string StateName2;
        public string StateName3;
        public bool _isUseRunMode;
        public float _runModeMotionSpeed;
        public float _runModeBlendRate;
        public bool _isUsePrevAnimationTime;
        public bool _isCancelX;
        public bool _isCancelY;
        public bool _isCancelZ;
        public float _cancelRate;
        public bool _isCancelScaleAveragePos;
        public Gallop.Character.CourseCharacterMotionParam.CancelTransform _cancelOffsetCheckTarget;
        public Vector3 _cancelFixPos;

        // Native 0x4f090c0/0x4f090f0: this is the next key. A non-Run
        // boundary changes only the output Run flag; the other fields survive.
        public void Interpolate(LiveTimelineKeyCharaMotionData previous, ref LiveTimelineKeyCharaMotionData output, float t)
        {
            output._isUseRunMode = previous._isUseRunMode && _isUseRunMode;
            if (!output._isUseRunMode) return;
            output._runModeMotionSpeed = RunLerp(previous._runModeMotionSpeed, _runModeMotionSpeed, t);
            output._runModeBlendRate = RunLerp(previous._runModeBlendRate, _runModeBlendRate, t);
            output._isCancelX = previous._isCancelX && _isCancelX;
            output._isCancelY = previous._isCancelY && _isCancelY;
            output._isCancelZ = previous._isCancelZ && _isCancelZ;
            output._cancelRate = RunLerp(previous._cancelRate, _cancelRate, t);
            output._isCancelScaleAveragePos = previous._isCancelScaleAveragePos && _isCancelScaleAveragePos;
            output._cancelOffsetCheckTarget = _cancelOffsetCheckTarget;
            output._cancelFixPos = new Vector3(
                RunLerp(previous._cancelFixPos.x, _cancelFixPos.x, t),
                RunLerp(previous._cancelFixPos.y, _cancelFixPos.y, t),
                RunLerp(previous._cancelFixPos.z, _cancelFixPos.z, t));
        }
        static float RunLerp(float previous, float next, float t)
        {
            t = Mathf.Clamp01(t);
            return LiveTimelineControl.MonitorFloat32(previous + LiveTimelineControl.MonitorFloat32(
                LiveTimelineControl.MonitorFloat32(next - previous) * t));
        }
    }

    [System.Serializable]
    public class LiveTimelineKeyCharaMotionSeqDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyCharaMotionData>
    {

    }

    [System.Serializable]
    public class LiveTimelineCharaMotSeqData : ILiveTimelineGroupData
    {
        public LiveTimelineKeyCharaMotionSeqDataList keys;
        [SerializeField]
        private bool _existsOverrideMotionSet;
        [SerializeField, Newtonsoft.Json.JsonProperty("_existsOverrideSystemTextMotion")]
        private bool _existsOverrideSystemTextMotion;
        [SerializeField, Newtonsoft.Json.JsonProperty("_existsRunModeKey")]
        private bool _existsRunModeKey;
        [SerializeField, Newtonsoft.Json.JsonProperty("_randomEntrySystemTextGroupArray")]
        private RandomEntrySystemTextGroup[] _randomEntrySystemTextGroupArray;
        public bool ExistsOverrideSystemTextMotion => _existsOverrideSystemTextMotion;
        public bool ExistsRunModeKey => _existsRunModeKey;
        public int RandomEntrySystemTextGroupCount => _randomEntrySystemTextGroupArray == null ? 0 : _randomEntrySystemTextGroupArray.Length;
        public RandomEntrySystemTextGroup[] RandomEntrySystemTextGroups=>_randomEntrySystemTextGroupArray;
        [System.NonSerialized] public int SystemTextMotionOverrideCharacterId=-1;

        [System.Serializable]
        public class RandomEntrySystemTextGroup
        {
            [SerializeField, Newtonsoft.Json.JsonProperty("_systemTextIdArray")]
            private int[] _systemTextIdArray;
            public int[] SystemTextIdArray => _systemTextIdArray;
            [System.NonSerialized] public bool IsValid;
            [System.NonSerialized] public Dictionary<string,List<LiveSystemTextData>> BodyMotionDictionary;
            [System.NonSerialized] public string[] BodyMotionKeys=System.Array.Empty<string>();
            [System.NonSerialized] public int BodyMotionIndexCounter;
            [System.NonSerialized] public int[] SystemTextIndexCounters=System.Array.Empty<int>();
        }
    }
}
