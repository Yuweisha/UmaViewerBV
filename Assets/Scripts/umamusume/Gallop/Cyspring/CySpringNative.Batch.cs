using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Gallop
{
    public partial struct CySpringNative
    {
        // Switched on only after measured scene-level validation.
        public static bool BatchDispatchEnabled = false;
        [StructLayout(LayoutKind.Sequential)]
        struct NativeBatchJob
        {
            public IntPtr Cloth, Collisions, Parents, Skirt, SkirtArgs;
            public int Count, Flags;
            public float Stiffness, Drag, Gravity, WindX, WindY, WindZ, WindStrength;
            public float Timescale, Move, AddMove, Spring;
        }

        [DllImport(DLL_NAME, EntryPoint = "NativeClothBatchUpdate", CallingConvention = CallingConvention.Cdecl)]
        static extern void NativeClothBatchUpdate(IntPtr jobs, int count);
        sealed class BatchBufferPins
        {
            struct Buffer
            {
                public GCHandle Handle;
                public IntPtr Pointer;
            }

            readonly Dictionary<Array, Buffer> buffers = new Dictionary<Array, Buffer>(96);
            public int Count => buffers.Count;

            public IntPtr Pin(Array value)
            {
                if (value == null || value.Length == 0)
                    return IntPtr.Zero;
                if (buffers.TryGetValue(value, out var existing))
                    return existing.Pointer;
                var handle = GCHandle.Alloc(value, GCHandleType.Pinned);
                try
                {
                    var result = new Buffer
                    {
                        Handle = handle,
                        Pointer = handle.AddrOfPinnedObject()
                    };
                    buffers.Add(value, result);
                    return result.Pointer;
                }
                catch
                {
                    handle.Free();
                    throw;
                }
            }

            public void Release()
            {
                foreach (var item in buffers.Values)
                    item.Handle.Free();
                buffers.Clear();
            }
        }

        public sealed partial class PreparedSimulation
        {
            readonly BatchBufferPins batchPins = new BatchBufferPins();
            readonly NativeSkirtArg[] batchSkirtArg = new NativeSkirtArg[1];
            NativeBatchJob[] batchJobs = new NativeBatchJob[64];
            static readonly int skirtWorkStride = Marshal.SizeOf<NativeSkirtWorking>();
            static PreparedSimulation()
            {
                CheckBatchLayout();
            }

            static void CheckBatchLayout()
            {
                if (Marshal.SizeOf<NativeBatchJob>() != 96 || Marshal.OffsetOf<NativeBatchJob>(nameof(NativeBatchJob.Timescale)).ToInt32() != 76)
                    throw new InvalidOperationException("UmaSpringNative batch ABI differs from its 96-byte native layout");
            }

            public void RunImmediate()
            {
                Complete();
                ExecuteBatch();
                if (skirt != null && skirtWork != null)
                    skirt.NativeArg = skirtArg;
            }

            void ExecuteBatch()
            {
                if (updates.Count == 0)
                    return;
                if (batchJobs.Length < updates.Count)
                    Array.Resize(ref batchJobs, System.Math.Max(updates.Count, batchJobs.Length * 2));
                batchSkirtArg[0] = skirtArg;
                int jobCount = 0, bones = 0, pins = 0;
                try
                {
                    for (int i = 0; i < updates.Count; i++)
                    {
                        var u = updates[i];
                        if (u.Cloth == null || u.Cloth.Length == 0 || u.Count <= 0)
                            continue;
                        bool linked = u.SkirtIndex >= 0 && skirtWork != null && (uint)u.SkirtIndex < (uint)skirtWork.Length;
                        int count = System.Math.Min(u.Count, u.Cloth.Length);
                        batchJobs[jobCount++] = new NativeBatchJob
                        {
                            Cloth = batchPins.Pin(u.Cloth),
                            Collisions = batchPins.Pin(u.Collisions),
                            Parents = batchPins.Pin(u.Parents),
                            Skirt = linked ? IntPtr.Add(batchPins.Pin(skirtWork), u.SkirtIndex * skirtWorkStride) : IntPtr.Zero,
                            SkirtArgs = linked ? batchPins.Pin(batchSkirtArg) : IntPtr.Zero,
                            Count = count,
                            Flags = 1 | (u.Is60Fps ? 2 : 0),
                            Stiffness = u.Stiffness,
                            Drag = u.Drag,
                            Gravity = u.Gravity,
                            WindX = u.WindX,
                            WindY = u.WindY,
                            WindZ = u.WindZ,
                            WindStrength = u.WindStrength,
                            Timescale = u.Timescale,
                            Move = u.MoveRate,
                            AddMove = u.AddMoveRate,
                            Spring = u.SpringRate
                        };
                        bones += count;
                    }

                    if (jobCount == 0)
                        return;
                    var pointer = batchPins.Pin(batchJobs);
                    pins = batchPins.Count;
                    NativeClothBatchUpdate(pointer, jobCount);
                    Interlocked.Add(ref _successfulCalls, jobCount);
                    skirtArg = batchSkirtArg[0];
                }
                finally
                {
                    batchPins.Release();
                }
            }
        }
    }
}
