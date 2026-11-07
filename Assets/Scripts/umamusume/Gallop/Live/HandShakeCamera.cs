using System;
using UnityEngine;

namespace Gallop.Live
{
    // Fixed GameAssembly: UpdateHandShakeCamera 4e808d0; Work 4e89680 /
    // 4e89750 / 4e89c10. This is a position offset, not a rotation/noise modifier.
    public sealed class HandShakeCamera
    {
        public float Power, Frequency, Rate;
        public bool UseFixedShakePattern;
        public Vector3 Position { get; private set; }
        private Work _work;
        public HandShakeCamera() { Reset(); }
        public void Reset() { _work.Initialize(); Position = Vector3.zero; }
        public bool UpdateHandShakeCamera(Transform cameraTransform, float deltaTime, int seed, float time)
        {
            _work.Power = Power; _work.Frequency = Frequency; _work.Rate = Rate;
            _work.CameraRightVector = cameraTransform.right;
            _work.DeltaTime = deltaTime; _work.Time = time; _work.Seed = seed;
            _work.UseFixedShakePattern = UseFixedShakePattern;
            bool result = _work.TryGetOffsetPosition(out var position);
            Position = position;
            return result;
        }

        public struct Work
        {
            public float Power, Frequency, Rate;
            public Vector3 CameraRightVector;
            public float DeltaTime, Time;
            public int Seed;
            public bool UseFixedShakePattern;
            private float _currentAngle;
            private Vector2 _screenPosition, _nextTarget;
            private float _duration;

            public void Initialize()
            {
                this = default;
                Rate = 1f; CameraRightVector = Vector3.right;
            }

            // Optional engine observations allow the same production work to be
            // checked against original machine code, without replacing the core.
            public bool TryGetOffsetPosition(out Vector3 offset,
                Func<float, float, float> random = null, Func<float, float, float> perlin = null)
            {
                random = random ?? LiveRandomBoundary.RangeFloat;
                perlin = perlin ?? Mathf.PerlinNoise;
                if (Power < 0.000001f || Frequency < 0.000001f || Rate < 0.000001f)
                {
                    _currentAngle = 0f; _screenPosition = _nextTarget = Vector2.zero; _duration = 0f;
                    offset = Vector3.zero;
                    return false;
                }
                float step = Mathf.Max(Power, 0.01f) * 0.01f;
                Vector2 delta = _nextTarget - _screenPosition;
                if (!UseFixedShakePattern)
                {
                    while (Magnitude(delta) <= step)
                    {
                        UpdateNextTarget(step, random, perlin);
                        delta = _nextTarget - _screenPosition;
                    }
                }
                else if (Magnitude(delta) < step)
                {
                    UpdateNextTarget(step, random, perlin);
                    delta = _nextTarget - _screenPosition;
                    if (Magnitude(delta) < step)
                    {
                        _nextTarget.x = _screenPosition.x + step * (delta.x < 0f ? -1f : 1f);
                        _nextTarget.y = _screenPosition.y + step * (delta.y < 0f ? -1f : 1f);
                        delta = _nextTarget - _screenPosition;
                    }
                }
                float angle = GetAngleDegrees(delta);
                if (float.IsNaN(angle)) angle = 0f;
                float speed = UseFixedShakePattern ? perlin(Time, (float)Seed * 2f) * 9f + 1f : random(1f, 10f);
                GetRotation(ref _currentAngle, angle, F(Frequency * speed));
                if (DeltaTime > 0.000001f)
                {
                    float distance = F(F(DeltaTime * 30f) * step);
                    _screenPosition.x = F(_screenPosition.x + F(Mathf.Sin(F(_currentAngle * 0.01745329238474369f)) * distance));
                    _screenPosition.y = F(_screenPosition.y + F(Mathf.Cos(F(F(_currentAngle * 3.1415927410125732f) / 180f)) * distance));
                }
                _duration += DeltaTime * Frequency;
                // Original deliberately does not use CameraRightVector.y here.
                offset = new Vector3(CameraRightVector.x * _screenPosition.x,
                    _screenPosition.y, CameraRightVector.z * _screenPosition.x);
                if (_duration >= 0.1f)
                {
                    _duration = 0f;
                    UpdateNextTarget(step, random, perlin);
                }
                offset *= Rate;
                return true;
            }

            private void UpdateNextTarget(float step, Func<float, float, float> random, Func<float, float, float> perlin)
            {
                if (UseFixedShakePattern)
                    _nextTarget = new Vector2((perlin(Time, Seed) - 0.5f) * 2f, (perlin(Seed, Time) - 0.5f) * 2f);
                else _nextTarget = new Vector2(random(-1f, 1f), random(-1f, 1f));
                _nextTarget *= step;
            }

            // The pre-existing Viewer GetAng(Vector2) returns only acos radians;
            // original 1fec380 normalizes twice, converts to degrees and mirrors X.
            private static float GetAngleDegrees(Vector2 v)
            {
                float length = Magnitude(v);
                Vector2 n = length > 0.00001f ? new Vector2(F(v.x / length), F(v.y / length)) : Vector2.zero;
                float angle = F(F(Mathf.Acos(F(n.y / Magnitude(n))) * 180f) / 3.1415927410125732f);
                return n.x < 0f ? F(360f - angle) : angle;
            }

            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
            private static float F(float v) => BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(v));
            private static float Magnitude(Vector2 v) => (float)System.Math.Sqrt(F(F(v.x * v.x) + F(v.y * v.y)));
            private static void GetRotation(ref float current, float next, float speed)
            {
                float difference = F(next - current);
                float wrapped = F(difference - F((float)System.Math.Floor(F(difference / 360f)) * 360f));
                wrapped = Mathf.Clamp(wrapped, 0f, 360f);
                if (wrapped > 180f) wrapped = F(wrapped - 360f);
                current = wrapped < -speed ? F(current - speed) : wrapped > speed ? F(current + speed) : next;
            }
        }
    }
}
