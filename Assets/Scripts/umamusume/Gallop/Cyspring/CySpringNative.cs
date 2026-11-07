using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Gallop
{
    public partial struct CySpringNative
    {
#if (UNITY_IOS || UNITY_IPHONE) && !UNITY_EDITOR
        private const string DLL_NAME = "__Internal";
#else
        private const string DLL_NAME = "UmaSpringNative";
#endif
        public static bool isNative = true;
        private static long _successfulCalls;
        public static long SuccessfulCalls => System.Threading.Interlocked.Read(ref _successfulCalls);

        public NativeClothWorking _clothWorking;
        public static float SpringRate = 1.0f;
        public static bool UseNativePlugin
        {
            get
            {
                return isNative;
            }

            set
            {
                isNative = value;
            }
        }

        public static int TargetFrameRate
        {
            get
            {
                if (Config.Instance != null)
                    return Config.Instance.GetTargetFrameRate();
                return Application.targetFrameRate == 30 ? 30 : 60;
            }
        }

        public static bool Is60FpsPhysics
        {
            get
            {
                return TargetFrameRate == 60;
            }
        }

        [DllImport(DLL_NAME, EntryPoint = "NativeClothUpdate", CallingConvention = CallingConvention.Cdecl)]
        private static extern void NativeClothUpdate(IntPtr cond, int nCond, IntPtr collisions, IntPtr pRootParentWork, float stiffnessForceRate, float dragForceRate, float gravityRate, float windX, float windY, float windZ, float windStrength, bool bCollisionSwitch, float timescale, bool is60FPS, float moveRate, float addMoveRate, float springRate);
        [DllImport(DLL_NAME, EntryPoint = "NativeClothSkirtUpdate", CallingConvention = CallingConvention.Cdecl)]
        private static extern void NativeClothSkirtUpdate(IntPtr cond, int nCond, IntPtr collisions, IntPtr pWorking, ref NativeSkirtArg pArg, IntPtr pRootParentWork, float stiffnessForceRate, float dragForceRate, float gravityRate, float windX, float windY, float windZ, float windStrength, bool bCollisionSwitch, float timescale, bool is60FPS, float moveRate, float addMoveRate, float springRate);
        [DllImport(DLL_NAME, EntryPoint = "NativeSkirtUpdate", CallingConvention = CallingConvention.Cdecl)]
        private static extern void NativeSkirtUpdate(IntPtr pWorking, ref NativeSkirtArg pArg);
        public static void UpdateNativeCloth(NativeClothWorking[] clothWorkingArray, int clothWorkingCount, NativeClothCollision[] collisionArray, int linkSkirtIndex, SkirtController skirtCtrl, NativeRootParentWork[] nativeRootParentArray, float stiffnessForceRate, float dragForceRate, float gravityRate, float windX, float windY, float windZ, float windStrength, bool bCollisionSwitch, float timescale = 1.0f, bool is60FPS = false, float moveRate = 1.0f, float addMoveRate = 1.0f, float springRate = 1.0f)
        {
            if (clothWorkingArray == null || clothWorkingArray.Length == 0)
                return;
            if (clothWorkingCount <= 0)
                return;
            if (clothWorkingCount > clothWorkingArray.Length)
                clothWorkingCount = clothWorkingArray.Length;
            if (!isNative)
                return;
            if (linkSkirtIndex >= 0 && skirtCtrl != null && skirtCtrl.IsEnableSkirt)
            {
                NativeSkirtWorking[] skirtWorkingArray = skirtCtrl.NativeWorkingArray;
                if (skirtWorkingArray != null && (uint)linkSkirtIndex < (uint)skirtWorkingArray.Length)
                {
                    NativeSkirtArg skirtArg = skirtCtrl.NativeArg;
                    UpdateNativeClothSkirtInternal(clothWorkingArray, clothWorkingCount, collisionArray, skirtWorkingArray, linkSkirtIndex, ref skirtArg, nativeRootParentArray, stiffnessForceRate, dragForceRate, gravityRate, windX, windY, windZ, windStrength, bCollisionSwitch, timescale, is60FPS, moveRate, addMoveRate, springRate);
                    skirtCtrl.NativeArg = skirtArg;
                    return;
                }
            }

            UpdateNativeClothInternal(clothWorkingArray, clothWorkingCount, collisionArray, nativeRootParentArray, stiffnessForceRate, dragForceRate, gravityRate, windX, windY, windZ, windStrength, bCollisionSwitch, timescale, is60FPS, moveRate, addMoveRate, springRate);
        }

        private static void UpdateNativeClothInternal(NativeClothWorking[] clothWorkingArray, int nClothWorking, NativeClothCollision[] collisionArray, NativeRootParentWork[] rootParentWorkArray, float stiffnessForceRate, float dragForceRate, float gravityRate, float windX, float windY, float windZ, float windStrength, bool bCollisionSwitch, float timescale, bool is60FPS, float moveRate, float addMoveRate, float springRate)
        {
            PinnedArray<NativeClothWorking> clothPin = default;
            PinnedArray<NativeClothCollision> collisionPin = default;
            PinnedArray<NativeRootParentWork> parentPin = default;
            try
            {
                clothPin = new PinnedArray<NativeClothWorking>(clothWorkingArray);
                collisionPin = new PinnedArray<NativeClothCollision>(collisionArray);
                parentPin = new PinnedArray<NativeRootParentWork>(rootParentWorkArray);
                if (clothPin.Ptr == IntPtr.Zero)
                    return;
                NativeClothUpdate(clothPin.Ptr, nClothWorking, collisionPin.Ptr, parentPin.Ptr, stiffnessForceRate, dragForceRate, gravityRate, windX, windY, windZ, windStrength, bCollisionSwitch, timescale, is60FPS, moveRate, addMoveRate, springRate);
                System.Threading.Interlocked.Increment(ref _successfulCalls);
            }
            finally
            {
                parentPin.Dispose();
                collisionPin.Dispose();
                clothPin.Dispose();
            }
        }

        private static void UpdateNativeClothSkirtInternal(NativeClothWorking[] clothWorkingArray, int nClothWorking, NativeClothCollision[] collisionArray, NativeSkirtWorking[] skirtWorkingArray, int skirtWorkingIndex, ref NativeSkirtArg arg, NativeRootParentWork[] rootParentWorkArray, float stiffnessForceRate, float dragForceRate, float gravityRate, float windX, float windY, float windZ, float windStrength, bool bCollisionSwitch, float timescale, bool is60FPS, float moveRate, float addMoveRate, float springRate)
        {
            if (clothWorkingArray == null || clothWorkingArray.Length == 0)
                return;
            if (skirtWorkingArray == null || skirtWorkingArray.Length == 0)
                return;
            if ((uint)skirtWorkingIndex >= (uint)skirtWorkingArray.Length)
                return;
            PinnedArray<NativeClothWorking> clothPin = default;
            PinnedArray<NativeClothCollision> collisionPin = default;
            PinnedArray<NativeRootParentWork> parentPin = default;
            PinnedArray<NativeSkirtWorking> skirtWorkPin = default;
            try
            {
                clothPin = new PinnedArray<NativeClothWorking>(clothWorkingArray);
                collisionPin = new PinnedArray<NativeClothCollision>(collisionArray);
                parentPin = new PinnedArray<NativeRootParentWork>(rootParentWorkArray);
                skirtWorkPin = new PinnedArray<NativeSkirtWorking>(skirtWorkingArray);
                IntPtr clothPtr = clothPin.Ptr;
                IntPtr collisionPtr = collisionPin.Ptr;
                IntPtr parentPtr = parentPin.Ptr;
                IntPtr workingPtr = skirtWorkPin.ElementPtr(skirtWorkingIndex);
                if (clothPtr == IntPtr.Zero || workingPtr == IntPtr.Zero)
                    return;
                NativeClothSkirtUpdate(clothPtr, nClothWorking, collisionPtr, workingPtr, ref arg, parentPtr, stiffnessForceRate, dragForceRate, gravityRate, windX, windY, windZ, windStrength, bCollisionSwitch, timescale, is60FPS, moveRate, addMoveRate, springRate);
                System.Threading.Interlocked.Increment(ref _successfulCalls);
            }
            finally
            {
                skirtWorkPin.Dispose();
                parentPin.Dispose();
                collisionPin.Dispose();
                clothPin.Dispose();
            }
        }

        public static void UpdateNativeClothNoSkirt(NativeClothWorking[] clothWorkingArray, int clothWorkingCount, NativeClothCollision[] collisionArray, NativeRootParentWork[] rootParentWorkArray, float stiffnessForceRate, float dragForceRate, float gravityRate, float windX, float windY, float windZ, float windStrength, bool bCollisionSwitch, float timescale = 1.0f, bool is60FPS = false, float moveRate = 1.0f, float addMoveRate = 1.0f, float springRate = 1.0f)
        {
            UpdateNativeCloth(clothWorkingArray, clothWorkingCount, collisionArray, -1, null, rootParentWorkArray, stiffnessForceRate, dragForceRate, gravityRate, windX, windY, windZ, windStrength, bCollisionSwitch, timescale, is60FPS, moveRate, addMoveRate, springRate);
        }

        /// <summary>
        /// Official SkirtController.UpdateSkirt path:
        /// working pointer = NativeWorkingArray + index * sizeof(NativeSkirtWorking)
        /// arg pointer     = NativeArg
        /// </summary>
        public static void UpdateSkirtNativePluginOne(NativeSkirtWorking[] workingArray, int workingIndex, ref NativeSkirtArg arg)
        {
            if (!isNative)
                return;
            if (workingArray == null || workingArray.Length == 0)
                return;
            if ((uint)workingIndex >= (uint)workingArray.Length)
                return;
            PinnedArray<NativeSkirtWorking> workingPin = default;
            try
            {
                workingPin = new PinnedArray<NativeSkirtWorking>(workingArray);
                IntPtr workingPtr = workingPin.ElementPtr(workingIndex);
                if (workingPtr == IntPtr.Zero)
                    return;
                NativeSkirtUpdate(workingPtr, ref arg);
                System.Threading.Interlocked.Increment(ref _successfulCalls);
            }
            finally
            {
                workingPin.Dispose();
            }
        }

        /// <summary>
        /// Compatibility wrapper.
        /// Prefer the array + index overload when calling from SkirtController.UpdateSkirt,
        /// because official code passes the address of NativeWorkingArray[index].
        /// </summary>
        public static void UpdateSkirtNativePluginOne(ref NativeSkirtWorking working, ref NativeSkirtArg arg)
        {
            if (!isNative)
                return;
            NativeSkirtWorking[] tempWorkingArray = new NativeSkirtWorking[1];
            tempWorkingArray[0] = working;
            UpdateSkirtNativePluginOne(tempWorkingArray, 0, ref arg);
            working = tempWorkingArray[0];
        }

        private struct PinnedArray<T> : IDisposable where T : struct
        {
            private GCHandle _handle;
            private static readonly int _elementSize = Marshal.SizeOf(typeof(T));
            public IntPtr Ptr { get; private set; }
            public int Length { get; private set; }

            public PinnedArray(T[] array)
            {
                this = default;
                if (array == null || array.Length == 0)
                {
                    Ptr = IntPtr.Zero;
                    Length = 0;
                    return;
                }

                _handle = GCHandle.Alloc(array, GCHandleType.Pinned);
                Ptr = _handle.AddrOfPinnedObject();
                Length = array.Length;
            }

            public IntPtr ElementPtr(int index)
            {
                if (Ptr == IntPtr.Zero)
                    return IntPtr.Zero;
                if ((uint)index >= (uint)Length)
                    return IntPtr.Zero;
                return IntPtr.Add(Ptr, _elementSize * index);
            }

            public void Dispose()
            {
                if (_handle.IsAllocated)
                    _handle.Free();
                Ptr = IntPtr.Zero;
                Length = 0;
            }
        }

        public static Vector3 RoundAngle(Vector3 angle)
        {
            return new Vector3(RoundAngle(angle.x), RoundAngle(angle.y), RoundAngle(angle.z));
        }

        public static float RoundAngle(float angle)
        {
            while (angle > 180.0f)
                angle -= 360.0f;
            while (angle < -180.0f)
                angle += 360.0f;
            return angle;
        }

        public static Vector3 MakePositive(Vector3 angle)
        {
            return new Vector3(MakePositive(angle.x), MakePositive(angle.y), MakePositive(angle.z));
        }

        public static float MakePositive(float angle)
        {
            while (angle < 0.0f)
                angle += 360.0f;
            while (angle >= 360.0f)
                angle -= 360.0f;
            return angle;
        }
    }
}
