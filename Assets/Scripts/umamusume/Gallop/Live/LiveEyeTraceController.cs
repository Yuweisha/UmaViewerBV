using System;
using System.Linq;
using Gallop.Live.Cutt;
using UnityEngine;

namespace Gallop.Live
{
    // Loaded-model adapter for CharacterObject.LateUpdateEyeTrace (4e78610),
    // EyeTargetPositionSetter (585e330), EyeTraceController (5194160/5192850)
    // and EyeTraceObject.AlterLateUpdate (5194f30). Uses the authored eye nodes;
    // it does not convert gaze into the facial XRange/YRange morph weights.
    public sealed class LiveEyeTraceController : IDisposable
    {
        public sealed class Eye
        {

            public Transform Transform, Locator;
            public Vector2 Min, Max, Offset, TargetOffset, PreviousOffset;
            public Vector2 InitialMin, InitialMax;
            public float Scale { get; private set; } = 1f;

            public float RangeFactor = 1f;
            public void InitializeRange(Vector2 min, Vector2 max)
            {
                InitialMin = min;
                InitialMax = max;
                RecalculateRange();
            }

            void RecalculateRange()
            {
                float value = LiveTimelineControl.MonitorFloat32(Scale * RangeFactor);
                Min = new Vector2(LiveTimelineControl.MonitorFloat32(InitialMin.x * value), LiveTimelineControl.MonitorFloat32(InitialMin.y * value));
                Max = new Vector2(LiveTimelineControl.MonitorFloat32(InitialMax.x * value), LiveTimelineControl.MonitorFloat32(InitialMax.y * value));
            }

            public void SetScale(float scale)
            {
                Scale = scale > .001f ? scale : .001f;
                RecalculateRange();
            }

            public float DelayRate = .85f, Duration, Elapsed;
            public bool DurationEnabled;
            public bool UseEyeLineEasing;
            public DrivenKeyComponent.InterpolateType Interpolate;
            public GameObject TargetObject;
            public Vector3 TargetPosition;
            public bool IsDefault;
            public float VerticalRate, HorizontalRate;
            public bool HighlightEnabled, HighlightDowner, HighlightRight;
            public float HighlightScaleX = 10f, HighlightScaleY = 30f;
            public Vector2 HighlightOffset;
            public void SetDuration(float duration, DrivenKeyComponent.InterpolateType interpolate)
            {
                PreviousOffset = Offset;
                Duration = duration;
                Elapsed = 0;
                DurationEnabled = true;
                Interpolate = interpolate;
            }

            public void ClearDuration()
            {
                DurationEnabled = false;
                Duration = Elapsed = 0;
            }

            // Original blink step3/4: current offset becomes duration origin;
            // zero duration and one-frame easing restore tracking after closure.
            public void AfterBlink(bool valid)
            {
                if (valid)
                {
                    DurationEnabled = true;
                    PreviousOffset = Offset;
                    Duration = Elapsed = 0;
                    Interpolate = DrivenKeyComponent.InterpolateType.Linear;
                }

                UseEyeLineEasing = true;
            }

            static float F(float value) => LiveTimelineControl.MonitorFloat32(value);
            static Vector3 NormalizeDirection(Vector3 value)
            {
                float length = (float)System.Math.Sqrt(F(F(F(value.y * value.y) + F(value.x * value.x)) + F(value.z * value.z)));
                return length > 1e-5f ? new Vector3(F(value.x / length), F(value.y / length), F(value.z / length)) : Vector3.zero;
            }

            static float Dot(Vector3 a, Vector3 b) => F(F(F(a.y * b.y) + F(a.x * b.x)) + F(a.z * b.z));
            static Vector2 Lerp(Vector2 a, Vector2 b, float ratio)
            {
                ratio = Mathf.Clamp01(ratio);
                return new Vector2(F(a.x + F(F(b.x - a.x) * ratio)), F(a.y + F(F(b.y - a.y) * ratio)));
            }

            Vector3 EaseDirection(Vector3 direction, Vector3 forward)
            {
                if (DurationEnabled && !UseEyeLineEasing)
                    return direction;
                var value = Vector3.RotateTowards(direction, forward, .08726646f, 0);

                return value;
            }

            public void Update(Vector3 target, bool isDefault, float vertical, float horizontal, float delta)
            {

                Vector3 initialEyePosition = Transform.position;
                Vector2 value;
                if (isDefault)
                {
                    var local = Locator.InverseTransformPoint(Transform.position);
                    value = new Vector2(local.x, local.y);
                }
                else
                {
                    var basis = Locator.parent;
                    var direction = NormalizeDirection(target - Locator.position);
                    direction = EaseDirection(direction, basis.forward);
                    value = new Vector2(Mathf.Clamp(F(F(Dot(direction, basis.right) * .02f) * Scale), Min.x, Max.x), Mathf.Clamp(F(F(Dot(direction, basis.up) * .02f) * Scale), Min.y, Max.y));
                }

                value.y = Rate(value.y, Min.y, Max.y, vertical);
                value.x = Rate(value.x, Min.x, Max.x, horizontal);
                TargetOffset = value;
                if (DurationEnabled)
                {
                    Elapsed = F(Elapsed + delta);
                    float ratio = 1;
                    if (Duration > 0 && Duration > Elapsed)
                    {
                        ratio = Elapsed / Duration;
                        switch (Interpolate)
                        {
                            case DrivenKeyComponent.InterpolateType.EaseIn:
                                ratio = 1 - Mathf.Cos(ratio * Mathf.PI * .5f);
                                break;
                            case DrivenKeyComponent.InterpolateType.EaseOut:
                                ratio = Mathf.Sin(ratio * Mathf.PI * .5f);
                                break;
                            case DrivenKeyComponent.InterpolateType.EaseInOut:
                                ratio *= 2;
                                ratio = ratio < 1 ? (1 - Mathf.Cos(ratio * Mathf.PI * .5f)) * .5f : Mathf.Sin((ratio - 1) * Mathf.PI * .5f) * .5f + .5f;
                                break;
                        }
                    }
                    else
                        ClearDuration();
                    Offset = Lerp(PreviousOffset, value, ratio);
                }
                else
                    Offset = Lerp(Offset, value, F(delta * DelayRate));
                UseEyeLineEasing = false;
                var right = Locator.right;
                var up = Locator.up;
                var originPosition = Locator.position;
                var position = new Vector3(F(F(F(right.x * Offset.x) + F(up.x * Offset.y)) + originPosition.x), F(F(F(right.y * Offset.x) + F(up.y * Offset.y)) + originPosition.y), F(F(F(right.z * Offset.x) + F(up.z * Offset.y)) + originPosition.z));

                Transform.position = position;
                if (HighlightEnabled)
                {
                    var origin = initialEyePosition - Locator.position;
                    float F(float v) => LiveTimelineControl.MonitorFloat32(v);
                    float Highlight(float offset, float previous, float bound, float scale, bool positive, bool negate)
                    {
                        if (positive ? offset <= 0f : offset >= 0f)
                            return 0f;
                        float part = offset;
                        if (positive ? previous > 0f : previous < 0f)
                        {
                            part = positive ? Mathf.Max(0f, F(offset - previous)) : Mathf.Min(0f, F(offset - previous));
                            bound = F(bound - previous);
                        }

                        return F(F(F(part / bound) * (negate ? -part : part)) * scale);
                    }

                    var localScale = Transform.localScale;
                    HighlightOffset = new Vector2(F(Highlight(Offset.x, origin.x, HighlightRight ? Max.x : Min.x, F(HighlightScaleX / Scale), HighlightRight, true) * localScale.x), F(Highlight(Offset.y, origin.y, HighlightDowner ? Min.y : Max.y, F(HighlightScaleY / Scale), !HighlightDowner, false) * localScale.y));
                }

            }

            static float Rate(float value, float min, float max, float rate) => rate > 0 ? F(value + F(F(max - value) * rate)) : rate < 0 ? F(value - F(F(min - value) * rate)) : value;
        }

        public readonly Eye Left, Right;
        public struct StateParameter
        {
            public bool IsEnable, IsEnabledDurationTime, IsEnableHighlightOffset, IsHighlightDowner, IsHighlightRight, IsDefault;
            public float DeltaTimeFactor, HighlightOffsetScaleX, HighlightOffsetScaleY;
            public GameObject TargetObject;
            public Vector3 TargetPosition;
            public float LeftVerticalRate, LeftHorizontalRate, RightVerticalRate, RightHorizontalRate, DelayRate;
        }

        public bool Enabled = true, IsValid = true, IsEnabledDurationTime;
        public float DeltaTimeFactor = 1f, HighlightOffsetScaleX = 10f, HighlightOffsetScaleY = 30f;
        public bool IsEnableHighlightOffset, IsHighlightDowner, IsHighlightRight;
        public bool SystemTextActive { get; private set; }
        public GameObject DefaultTargetObject { get; private set; }

        public static readonly Vector3 DefaultTargetPosition = new Vector3(0, 1, 10);
        readonly bool ownsTarget;
        System.Collections.Generic.List<Material> eyeMaterials;
        readonly UmaContainerCharacter _character;
        LiveTimelineKeyFacialEyeTrackData _key;
        int _durationKeyFrame = -1;
        Vector3 _target;
        bool _isDefault;
        public Vector3 Target => _target;
        public LiveTimelineKeyFacialEyeTrackData Key => _key;

        public LiveEyeTraceController(UmaContainerCharacter character, Eye left, Eye right, GameObject defaultTarget = null, bool ownsTarget = false)
        {
            _character = character;
            Left = left;
            Right = right;
            DefaultTargetObject = defaultTarget;
            this.ownsTarget = ownsTarget;
        }

        public StateParameter StoreState() => new StateParameter
        {
            IsEnable = Enabled,
            DeltaTimeFactor = DeltaTimeFactor,
            IsEnabledDurationTime = IsEnabledDurationTime,
            IsEnableHighlightOffset = IsEnableHighlightOffset,
            HighlightOffsetScaleX = HighlightOffsetScaleX,
            HighlightOffsetScaleY = HighlightOffsetScaleY,
            IsHighlightDowner = IsHighlightDowner,
            IsHighlightRight = IsHighlightRight,
            IsDefault = IsValid && Left.IsDefault && Right.IsDefault,
            TargetObject = Left.TargetObject,
            TargetPosition = Left.TargetPosition,
            LeftVerticalRate = Left.VerticalRate,
            LeftHorizontalRate = Left.HorizontalRate,
            RightVerticalRate = Right.VerticalRate,
            RightHorizontalRate = Right.HorizontalRate,
            DelayRate = Left.DelayRate
        };
        public void RestoreState(StateParameter state)
        {
            Enabled = state.IsEnable;
            DeltaTimeFactor = state.DeltaTimeFactor;
            IsEnabledDurationTime = Left.DurationEnabled = Right.DurationEnabled = state.IsEnabledDurationTime;
            SetHighlightEnabled(state.IsEnableHighlightOffset);
            HighlightOffsetScaleX = Left.HighlightScaleX = Right.HighlightScaleX = state.HighlightOffsetScaleX;
            HighlightOffsetScaleY = Left.HighlightScaleY = Right.HighlightScaleY = state.HighlightOffsetScaleY;
            IsHighlightDowner = Left.HighlightDowner = Right.HighlightDowner = state.IsHighlightDowner;
            IsHighlightRight = Left.HighlightRight = Right.HighlightRight = state.IsHighlightRight;
            SetDefault(state.IsDefault);
            Left.TargetObject = Right.TargetObject = state.TargetObject;
            Left.TargetPosition = Right.TargetPosition = state.TargetPosition;
            Left.VerticalRate = Mathf.Clamp(state.LeftVerticalRate, -1f, 1f);
            Left.HorizontalRate = Mathf.Clamp(state.LeftHorizontalRate, -1f, 1f);
            Right.VerticalRate = Mathf.Clamp(state.RightVerticalRate, -1f, 1f);
            Right.HorizontalRate = Mathf.Clamp(state.RightHorizontalRate, -1f, 1f);
            SetDelayRate(state.DelayRate);
            SystemTextActive = false;
        }

        public void SetHighlightEnabled(bool value)
        {
            IsEnableHighlightOffset = Left.HighlightEnabled = Right.HighlightEnabled = value;
            if (!value)
                Left.HighlightOffset = Right.HighlightOffset = Vector2.zero;
        }

        public void SetDefault(bool value)
        {
            if (IsValid)
                Left.IsDefault = Right.IsDefault = value;
        }

        public void SetDelayRate(float value)
        {
            if (IsValid)
                Left.DelayRate = Right.DelayRate = value;
        }

        public void ResetTargetObject() => Left.TargetObject = Right.TargetObject = DefaultTargetObject;
        public void ResetForSystemText()
        {
            ResetTargetObject();
            if (DefaultTargetObject != null)
                DefaultTargetObject.transform.localPosition = DefaultTargetPosition;
            Left.VerticalRate = Right.VerticalRate = Left.HorizontalRate = Right.HorizontalRate = 0;
            SetDelayRate(.85f);
            SystemTextActive = true;
        }

        public void ClearDuration()
        {
            if (IsValid)
            {
                IsEnabledDurationTime = false;
                Left.ClearDuration();
                Right.ClearDuration();
            }
        }

        public void SetDurationZero()
        {
            if (!IsValid)
                return;
            IsEnabledDurationTime = true;
            Left.SetDuration(0, DrivenKeyComponent.InterpolateType.Linear);
            Right.SetDuration(0, DrivenKeyComponent.InterpolateType.Linear);
        }

        public void Dispose()
        {
            if (ownsTarget && DefaultTargetObject != null)
                UnityEngine.Object.Destroy(DefaultTargetObject);
            DefaultTargetObject = null;
        }

        public void AfterBlink()
        {
            if (IsValid)
                IsEnabledDurationTime = true;
            Left.AfterBlink(IsValid);
            Right.AfterBlink(IsValid);
        }

        public void SetScale(float scale)
        {
            Left.SetScale(scale);
            Right.SetScale(scale);
        }

        public static LiveEyeTraceController Create(UmaContainerCharacter character)
        {
            var holder = character.Head != null ? character.Head.GetComponent<AssetHolder>() : null;
            if (holder == null)
                return null;
            var available = character.FaceDrivenKeyTarget;
            if (available == null || available.LeftEyeXrange == null || available.RightEyeXrange == null || available.LeftEyeYrange == null || available.RightEyeYrange == null)
                return null;
            var left = holder.Get<GameObject>("eye_l")?.transform;
            var right = holder.Get<GameObject>("eye_r")?.transform;
            var ll = holder.Get<GameObject>("eye_locator_l")?.transform;
            var rl = holder.Get<GameObject>("eye_locator_r")?.transform;
            if (left == null || right == null || ll == null || rl == null)
                return null;
            var face = character.FaceDrivenKeyTarget;
            // InitializeEyeRange (4ac5aa0..4ac61d1): group 1 provides the
            // inner/upper LEFT sample, group 0 the outer/lower RIGHT sample.
            // The opposite eye is mirrored in X. These groups are not two
            // interchangeable symmetric ranges and the Y signs are identical.
            var inner = face.LeftEyeXrange.trsArray.FirstOrDefault(t => t.transform == left);
            var outer = face.RightEyeXrange.trsArray.FirstOrDefault(t => t.transform == right);
            var upper = face.LeftEyeYrange.trsArray.FirstOrDefault(t => t.transform == left);
            var lower = face.RightEyeYrange.trsArray.FirstOrDefault(t => t.transform == right);
            if (inner == null || outer == null || upper == null || lower == null)
                return null;
            float minY = Mathf.Min(upper._position.y, lower._position.y);
            float maxY = Mathf.Max(upper._position.y, lower._position.y);
            var defaultTarget = new GameObject("eyeTargetObject");
            defaultTarget.transform.SetParent(character.transform, false);
            defaultTarget.transform.localPosition = DefaultTargetPosition;
            var result = new LiveEyeTraceController(character, new Eye { Transform = left, Locator = ll, Min = new Vector2(Mathf.Min(inner._position.x, -outer._position.x), minY), Max = new Vector2(Mathf.Max(inner._position.x, -outer._position.x), maxY) }, new Eye { Transform = right, Locator = rl, Min = new Vector2(Mathf.Min(-inner._position.x, outer._position.x), minY), Max = new Vector2(Mathf.Max(-inner._position.x, outer._position.x), maxY) }, defaultTarget, true);
            result.Left.InitializeRange(result.Left.Min, result.Left.Max);
            result.Right.InitializeRange(result.Right.Min, result.Right.Max);
            result.eyeMaterials = LiveEyeMaterialBinder.FindEyeMaterials(character.transform);
            return result;
        }

        public void SetKey(LiveTimelineKeyFacialEyeTrackData key)
        {
            if (key == null || SystemTextActive)
                return;
            _key = key;
            // Director.<InitializeTimeline>b__474_3: speed is the delay rate
            // input, not an angle interpolation time. 0x20000 owns duration.
            Left.DelayRate = Right.DelayRate = Mathf.Clamp01(key.speedRatePer * .3f / 100f);
            if (((int)key.attribute & 0x20000) == 0)
            {
                ClearDuration();
                _durationKeyFrame = -1;
            }
            else if (_durationKeyFrame != key.frame)
            {
                Left.SetDuration(key.time * (1f / 60f), key.interpolateType);
                Right.SetDuration(key.time * (1f / 60f), key.interpolateType);
                IsEnabledDurationTime = true;
                _durationKeyFrame = key.frame;
            }
        }

        Director _pendingDirector;
        float _pendingDelta;
        bool _pending;
        public void AlterCameraLateUpdate(Director director, float deltaTime)
        {
            _pendingDirector = director;
            _pendingDelta = deltaTime;
            _pending = true;
        }

        public void AfterEndCySpringSimulation()
        {
            if (!_pending)
                return;
            _pending = false;
            ApplyFinalGaze(_pendingDirector, _pendingDelta);
        }

        void ApplyFinalGaze(Director director, float deltaTime)
        {
            if (!IsValid || !Enabled)
                return;
            if (SystemTextActive)
            {
                ApplyEyes(deltaTime);
                return;
            }

            if (_key == null || director == null)
                return;
            var arena = director._liveTimelineControl.data.StageArenaPosition;
            int kind = (int)_key.targetType;
            Vector3 world = Vector3.zero;
            bool shorten = false;
            switch (kind)
            {
                case 0:
                    _isDefault = false;
                    _target = _character.transform.TransformPoint(arena);
                    break;
                case 1:
                    _isDefault = false;
                    world = director.MainRenderCamera.transform.position;
                    shorten = true;
                    break;
                case 40:
                    _isDefault = false;
                    _target = director.transform.position + director.transform.rotation * _key.DirectPosition;
                    break;
                case 41:
                    _isDefault = false;
                    _target = _character.Position.position + arena;
                    break;
                default:
                    world = Vector3.zero;
                    if (kind >= 3 && kind <= 22)
                    {
                        _isDefault = false;
                        int slot = kind - 3;
                        if (slot < director.CharaContainerScript.Count)
                        {
                            world = director.CharaContainerScript[slot].HeadBone.transform.position;
                            shorten = true;
                        }
                    }
                    else if (kind >= 30 && kind <= 33)
                    {
                        _isDefault = false;
                        world = director._liveTimelineControl.GetMultiCameraWorldPosition(kind - 30);
                        shorten = true;
                    }
                    else
                        _isDefault = true;
                    break;
            }

            if (shorten)
                _target = _character.HeadBone.transform.position + (world - _character.HeadBone.transform.position).normalized * 10f;
            Left.TargetObject = Right.TargetObject = null;
            Left.TargetPosition = Right.TargetPosition = _target;
            SetDefault(_isDefault);
            Left.VerticalRate = Right.VerticalRate = _key.verticalRatePer / 100f;
            Left.HorizontalRate = Right.HorizontalRate = _key.horizontalRatePer / 100f;
            ApplyEyes(deltaTime);
        }

        void ApplyEyes(float deltaTime)
        {
            // Original controller uses seconds for an active duration, otherwise
            // normalizes by DeltaTimeFactor/targetFrameRate (factor ctor = 1).
            float delta = GetDeltaTimeRate(deltaTime, Application.targetFrameRate);
            foreach (var eye in new[]
            {
                Left,
                Right
            }

            )
            {
                if (eye.TargetObject != null)
                    eye.TargetPosition = eye.TargetObject.transform.position;
                var target = eye.TargetPosition;
                eye.Update(target, eye.IsDefault, eye.VerticalRate, eye.HorizontalRate, delta);
            }

            _target = Left.TargetObject != null ? Left.TargetObject.transform.position : Left.TargetPosition;
            if (eyeMaterials != null)
            {
                var offset = new Vector4(Left.HighlightOffset.x, Left.HighlightOffset.y, Right.HighlightOffset.x, Right.HighlightOffset.y);
                foreach (var material in eyeMaterials)
                    material.SetVector("_Offset", offset);
            }
        }

        public float GetDeltaTimeRate(float deltaTime, int targetFrameRate)
        {
            if (Left.DurationEnabled || Right.DurationEnabled)
                return deltaTime;
            // The display cap can be -1 during offline export or unlimited
            // playback. It is not a simulation rate: a negative rate freezes
            // Vector2.Lerp and leaves only the after-blink snaps updating gaze.
            if (targetFrameRate <= 0)
                targetFrameRate = Time.captureDeltaTime > 0f ? Mathf.Max(1, Mathf.RoundToInt(1f / Time.captureDeltaTime)) : (Config.Instance?.TargetFrameRate ?? 0) > 0 ? Config.Instance.TargetFrameRate : 60;
            return LiveTimelineControl.MonitorFloat32(deltaTime / LiveTimelineControl.MonitorFloat32(DeltaTimeFactor / targetFrameRate));
        }
    }
}
