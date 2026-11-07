using System;
using UnityEngine;
using Newtonsoft.Json;

namespace Gallop.FootGround
{
    [Serializable]
    public class FootInfo
    {
        [SerializeField, JsonProperty("_offset")] private Vector3 _offset;
        [SerializeField, JsonProperty("_direction")] private Vector3 _direction;
        [SerializeField, JsonProperty("_releaseDistance")] private float _releaseDistance;
        [JsonProperty("_currentPosition")] private Vector3 _currentPosition;
        [JsonProperty("_previousPosition")] private Vector3 _previousPosition;
        [JsonProperty("_isGrounded")] private bool _isGrounded;
        public Vector3 Offset => _offset;
        public Vector3 Direction => _direction;
        public float ReleaseDistance => _releaseDistance;
    }
}

namespace Gallop
{
    [Serializable]
    public class CharaFootGroundData
    {
        [Serializable]
        public class TargetInfo
        {
            [SerializeField, JsonProperty("_rootBoneName")] private string _rootBoneName;
            [SerializeField, JsonProperty("_footInfo")] private Gallop.FootGround.FootInfo _footInfo;
            public string RootBoneName => _rootBoneName;
            public Gallop.FootGround.FootInfo FootInfo => _footInfo;
        }

        [SerializeField, JsonProperty("_targetInfoArray")] private TargetInfo[] _targetInfoArray;
        [SerializeField, JsonProperty("_effectDuration")] private float _effectDuration;
        [SerializeField, JsonProperty("_effectScaleMax")] private float _effectScaleMax;
        [SerializeField, JsonProperty("_effectScaleCurveByVelocity")]
        [Newtonsoft.Json.JsonConverter(typeof(Gallop.Live.Cutt.UnityAnimationCurveJsonConverter))]
        private AnimationCurve _effectScaleCurveByVelocity;
        public TargetInfo[] TargetInfoArray => _targetInfoArray;
        public float EffectDuration => _effectDuration;
        public float EffectScaleMax => _effectScaleMax;
    }
}
