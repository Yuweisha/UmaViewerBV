using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;

namespace Gallop
{
    public static class LiveCpuScheduling
    {
        // Actual scene-level A/B did not show a reliable throughput gain.
        // Keep the verified parallel path available for explicit experiments.
        public static bool Enabled = false;
    }

    public partial struct CySpringNative
    {
        // Main-thread capture owns every Unity-object access. Workers only see
        // the native solver's numeric buffers and preserve root/part ordering
        // inside one character, including its shared linked-skirt arguments.
        public sealed partial class PreparedSimulation
        {
            sealed class WorkerPool : IDisposable
            {
                readonly BlockingCollection<PreparedSimulation> queue = new BlockingCollection<PreparedSimulation>(128);
                readonly Thread[] threads;
                public WorkerPool()
                {
                    int count = System.Math.Max(1, System.Math.Min(2, Environment.ProcessorCount / 4));
                    threads = new Thread[count];
                    for (int i = 0; i < count; i++)
                    {
                        threads[i] = new Thread(Consume)
                        {
                            IsBackground = true,
                            Name = "Live CPU solver " + i
                        };
                        threads[i].Start();
                    }
                }

                void Consume()
                {
                    foreach (var item in queue.GetConsumingEnumerable())
                        item.ExecuteQueued();
                }

                public void Enqueue(PreparedSimulation item) => queue.Add(item);
                public void Dispose()
                {
                    queue.CompleteAdding();
                    foreach (var thread in threads)
                        thread.Join();
                    queue.Dispose();
                }
            }

            static WorkerPool pool;
            [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
            static void ResetPool()
            {
                ShutdownPool();
                Application.quitting -= ShutdownPool;
                Application.quitting += ShutdownPool;
            }

            static void ShutdownPool()
            {
                pool?.Dispose();
                pool = null;
            }

            public struct Update
            {
                public NativeClothWorking[] Cloth;
                public int Count, SkirtIndex;
                public NativeClothCollision[] Collisions;
                public NativeRootParentWork[] Parents;
                public float Stiffness, Drag, Gravity, WindX, WindY, WindZ, WindStrength, Timescale, MoveRate, AddMoveRate, SpringRate;
                public bool Is60Fps;
            }

            readonly List<Update> updates = new List<Update>(64);
            SkirtController skirt;
            NativeSkirtWorking[] skirtWork;
            NativeSkirtArg skirtArg;
            readonly ManualResetEventSlim completed = new ManualResetEventSlim(true);
            bool pending;
            Exception failure;
            public void Begin(SkirtController owner)
            {
                Complete();
                updates.Clear();
                skirt = owner;
                skirtWork = owner != null && owner.IsEnableSkirt ? owner.NativeWorkingArray : null;
                skirtArg = owner != null ? owner.NativeArg : default;
            }

            public void Add(Update update)
            {
                updates.Add(update);
            }

            public void Schedule()
            {
                if (pool == null)
                    pool = new WorkerPool();
                failure = null;
                completed.Reset();
                pending = true;
                pool.Enqueue(this);
            }

            public void Complete()
            {
                if (!pending)
                    return;
                try
                {
                    completed.Wait();
                    if (failure != null)
                        throw new InvalidOperationException("Live CPU solver failed", failure);
                    if (skirt != null && skirtWork != null)
                        skirt.NativeArg = skirtArg;
                }
                finally
                {
                    pending = false;
                }
            }

            void ExecuteQueued()
            {
                try
                {
                    Execute();
                }
                catch (Exception error)
                {
                    failure = error;
                }
                finally
                {
                    completed.Set();
                }
            }

            void Execute()
            {
                for (int i = 0; i < updates.Count; i++)
                {
                    var u = updates[i];
                    if (u.SkirtIndex >= 0 && skirtWork != null && (uint)u.SkirtIndex < (uint)skirtWork.Length)
                        UpdateNativeClothSkirtInternal(u.Cloth, u.Count, u.Collisions, skirtWork, u.SkirtIndex, ref skirtArg, u.Parents, u.Stiffness, u.Drag, u.Gravity, u.WindX, u.WindY, u.WindZ, u.WindStrength, true, u.Timescale, u.Is60Fps, u.MoveRate, u.AddMoveRate, u.SpringRate);
                    else
                        UpdateNativeClothInternal(u.Cloth, u.Count, u.Collisions, u.Parents, u.Stiffness, u.Drag, u.Gravity, u.WindX, u.WindY, u.WindZ, u.WindStrength, true, u.Timescale, u.Is60Fps, u.MoveRate, u.AddMoveRate, u.SpringRate);
                }
            }
        }
    }
}
