using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Gallop
{
    [Serializable]
    public class CySpringWindParam
    {
        [SerializeField] private bool _isEnableVertical;
        [SerializeField] private bool _isEnableHorizontal;

        [SerializeField] private float _verticalCycle;
        [SerializeField] private float _horizontalCycle;

        [SerializeField] private float _verticalAngleWidth;
        [SerializeField] private float _horizontalAngleWidth;

        [SerializeField] private int _partsMask;

        [SerializeField] private float[] _powerScaleArray;

        [SerializeField] private Vector3 _direction;
        [SerializeField] private Vector3 _right;

        [SerializeField] private bool _isLocalDirection;

        public bool IsEnableVertical
        {
            get { return _isEnableVertical; }
            set { _isEnableVertical = value; }
        }

        public bool IsEnableHorizontal
        {
            get { return _isEnableHorizontal; }
            set { _isEnableHorizontal = value; }
        }

        public float VerticalCycle
        {
            get { return _verticalCycle; }
            set { _verticalCycle = value; }
        }

        public float HorizontalCycle
        {
            get { return _horizontalCycle; }
            set { _horizontalCycle = value; }
        }

        public float VerticalAngleWidth
        {
            get { return _verticalAngleWidth; }
            set { _verticalAngleWidth = value; }
        }

        public float HorizontalAngleWidth
        {
            get { return _horizontalAngleWidth; }
            set { _horizontalAngleWidth = value; }
        }

        public int PartsMask
        {
            get { return _partsMask; }
            set { _partsMask = value; }
        }

        public float[] PowerScaleArray
        {
            get { return _powerScaleArray; }
            set { _powerScaleArray = value; }
        }

        public Vector3 Direction
        {
            get { return _direction; }
            set { _direction = value; _right = BuildRightFromDirection(value); }
        }

        public Vector3 Right
        {
            get { return _right; }
            set { _right = value; }
        }

        public bool IsLocalDirection
        {
            get { return _isLocalDirection; }
            set { _isLocalDirection = value; }
        }

        public CySpringWindParam()
        {
            Initialize();
        }

        public CySpringWindParam(
            bool isEnableV,
            bool isEnableH,
            float vCycle,
            float hCycle,
            float vAngleWidth,
            float hAngleWidth,
            Vector3 direction,
            int partsMask,
            float[] powerScaleArray)
        {
            Initialize();

            _isEnableVertical = isEnableV;
            _isEnableHorizontal = isEnableH;

            _verticalCycle = vCycle;
            _horizontalCycle = hCycle;

            _verticalAngleWidth = vAngleWidth;
            _horizontalAngleWidth = hAngleWidth;

            Direction = direction;

            _partsMask = partsMask;
            _powerScaleArray = ClonePowerScaleArray(powerScaleArray);
        }

        public CySpringWindParam(CySpringWindParam other)
        {
            Initialize();
            Copy(other);
        }

        public void Initialize()
        {
            _isEnableVertical = true;
            _isEnableHorizontal = true;

            _verticalCycle = 1f;
            _horizontalCycle = 1f;

            _verticalAngleWidth = 10f;
            _horizontalAngleWidth = 10f;

            _partsMask = int.MaxValue;

            _powerScaleArray = new float[]
            {
                1f, 1f, 1f
            };

            _direction = Vector3.forward;
            _right = Vector3.right;

            _isLocalDirection = false;
        }

        public void ResetParam()
        {
            _isEnableVertical = _isEnableHorizontal = true;
            _verticalCycle = _horizontalCycle = 1f;
            _verticalAngleWidth = _horizontalAngleWidth = 10f;
            Direction = Vector3.forward;
            _isLocalDirection = false; _partsMask = int.MaxValue;
            for (int i=0;i<_powerScaleArray.Length;i++) _powerScaleArray[i] = 1f;
        }

        public void GetDirection(Quaternion parentRotation, ref Vector3 direction, ref Vector3 right)
        {
            direction = _isLocalDirection ? parentRotation * _direction : _direction;
            right = _isLocalDirection ? BuildRightFromDirection(direction) : _right;
        }

        public static void SetInterp(
            CySpringWindParam prevParam,
            CySpringWindParam nextParam,
            CySpringWindParam outPatam,
            float t)
        {
            if (prevParam == null || nextParam == null || outPatam == null)
                throw new NullReferenceException();

            outPatam.IsEnableVertical = nextParam.IsEnableVertical;
            outPatam.IsEnableHorizontal = nextParam.IsEnableHorizontal;

            outPatam.VerticalCycle = NativeLerp(prevParam.VerticalCycle, nextParam.VerticalCycle, t);
            outPatam.HorizontalCycle = NativeLerp(prevParam.HorizontalCycle, nextParam.HorizontalCycle, t);

            outPatam.VerticalAngleWidth = NativeLerp(prevParam.VerticalAngleWidth, nextParam.VerticalAngleWidth, t);
            outPatam.HorizontalAngleWidth = NativeLerp(prevParam.HorizontalAngleWidth, nextParam.HorizontalAngleWidth, t);

            outPatam.PartsMask = nextParam.PartsMask;
            outPatam.Direction = new Vector3(NativeLerp(prevParam.Direction.x,nextParam.Direction.x,t),
                NativeLerp(prevParam.Direction.y,nextParam.Direction.y,t), NativeLerp(prevParam.Direction.z,nextParam.Direction.z,t));
            outPatam.IsLocalDirection = nextParam.IsLocalDirection;
            for (int i=0;i<3;i++) outPatam._powerScaleArray[i] = NativeLerp(prevParam._powerScaleArray[i],nextParam._powerScaleArray[i],t);
        }

        public void Copy(CySpringWindParam src)
        {
            if (src == null)
                throw new NullReferenceException();

            _isEnableVertical = src.IsEnableVertical;
            _isEnableHorizontal = src.IsEnableHorizontal;

            _verticalCycle = src.VerticalCycle;
            _horizontalCycle = src.HorizontalCycle;

            _verticalAngleWidth = src.VerticalAngleWidth;
            _horizontalAngleWidth = src.HorizontalAngleWidth;

            _partsMask = src.PartsMask;

            for (int i=0;i<_powerScaleArray.Length;i++) _powerScaleArray[i] = src._powerScaleArray[i];

            _direction = src.Direction;
            _right = src.Right;

            _isLocalDirection = src.IsLocalDirection;
        }

        private static Vector3 SafeNormalize(Vector3 value, Vector3 fallback)
        {
            if (value.sqrMagnitude <= 0.000001f)
                return fallback;

            return value.normalized;
        }

        private static Vector3 BuildRightFromDirection(Vector3 direction)
        {
            return new Vector3(NativeSub(NativeMul(direction.z,1f),NativeMul(direction.y,0f)),
                NativeSub(NativeMul(direction.x,0f),NativeMul(direction.z,0f)),
                NativeSub(NativeMul(direction.y,0f),NativeMul(direction.x,1f)));
        }

        // Native SSE rounds every operation; Mono can retain wider intermediates.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static float NativeAdd(float a,float b) => BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(a+b));
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static float NativeSub(float a,float b) => BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(a-b));
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static float NativeMul(float a,float b) => BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(a*b));
        internal static float NativeLerp(float a,float b,float t)
        {
            t = t < 0f ? 0f : t <= 1f ? t : 1f;
            return NativeAdd(NativeMul(NativeSub(b,a),t),a);
        }

        private static float[] ClonePowerScaleArray(float[] src)
        {
            if (src == null || src.Length == 0)
            {
                return new float[]
                {
                    1f, 1f, 1f
                };
            }

            float[] dst = new float[src.Length];

            for (int i = 0; i < src.Length; i++)
                dst[i] = src[i];

            return dst;
        }

        private static float[] InterpPowerScaleArray(float[] prev, float[] next, float t)
        {
            if (prev == null && next == null)
            {
                return new float[]
                {
                    1f, 1f, 1f
                };
            }

            if (prev == null)
                return ClonePowerScaleArray(next);

            if (next == null)
                return ClonePowerScaleArray(prev);

            int length = Mathf.Max(prev.Length, next.Length);
            float[] result = new float[length];

            for (int i = 0; i < length; i++)
            {
                float a = i < prev.Length ? prev[i] : 1f;
                float b = i < next.Length ? next[i] : 1f;

                result[i] = Mathf.Lerp(a, b, t);
            }

            return result;
        }
    }
}
