using System;
using System.Collections.Generic;

namespace Gallop.Live
{
    public enum LiveModelCallback
    {
        UpdatePre,
        Update,
        UpdatePost,
        PreLate,
        IK1,
        IK2,
        PostJob,
        Late,
        AfterLate,
        AfterSpring
    }

    public sealed class LiveModelPhaseOwner
    {
        readonly Func<bool> alive, activeSelf;
        readonly Func<LiveModelCallback, int> callback;
        public bool IsRegisterModelUpdater, IsRegisterRequested, IsUnregisterRequested;
        public int UpdateOrder;
        public bool IsAlive => alive();
        public bool ActiveSelf => activeSelf();

        public LiveModelPhaseOwner(Func<bool> isAlive, Func<bool> isActiveSelf, Func<LiveModelCallback, int> invoke)
        {
            alive = isAlive;
            activeSelf = isActiveSelf;
            callback = invoke;
        }

        internal int Invoke(LiveModelCallback phase)
        {

            int result = callback(phase);

            return result;
        }
    }

    // ModelControllerUpdater's registry and phase order. The owner delegates
    // supply real Unity lifetime/activeSelf and the model's component callbacks.
    public sealed class LiveModelPhaseRegistry
    {
        LiveModelPhaseOwner[] execute = new LiveModelPhaseOwner[32];
        readonly Queue<LiveModelPhaseOwner> register = new Queue<LiveModelPhaseOwner>();
        readonly Queue<LiveModelPhaseOwner> unregister = new Queue<LiveModelPhaseOwner>();
        readonly Queue<LiveModelPhaseOwner> fastRegister = new Queue<LiveModelPhaseOwner>();
        public int Tail { get; private set; }
        public int Phase { get; private set; }
        public bool Busy { get; private set; }
        public bool RemoveDirty { get; private set; }
        public bool OrderDirty { get; private set; }

        public Action ScheduleBatchedJobs;
        public LiveModelPhaseOwner[] ExecuteArray => execute;
        public IEnumerable<LiveModelPhaseOwner> RegisterQueue => register;
        public IEnumerable<LiveModelPhaseOwner> UnregisterQueue => unregister;
        public IEnumerable<LiveModelPhaseOwner> FastRegisterQueue => fastRegister;

        static bool Alive(LiveModelPhaseOwner owner) => owner != null && owner.IsAlive;
        static bool CanExecute(LiveModelPhaseOwner owner) => Alive(owner) && owner.IsRegisterModelUpdater && owner.ActiveSelf;
        static bool Same(LiveModelPhaseOwner a, LiveModelPhaseOwner b)
        {
            if (ReferenceEquals(a, b))
                return true;
            if (a == null)
                return !Alive(b);
            if (b == null)
                return !Alive(a);
            return false;
        }

        public static int CompareOwners(LiveModelPhaseOwner a, LiveModelPhaseOwner b)
        {
            if (!Alive(a))
                return 1;
            if (!Alive(b))
                return -1;
            return unchecked(a.UpdateOrder - b.UpdateOrder);
        }

        static void Request(LiveModelPhaseOwner owner, Queue<LiveModelPhaseOwner> queue, bool adding)
        {
            owner.IsRegisterRequested = adding;
            owner.IsUnregisterRequested = !adding;
            if (!queue.Contains(owner))
                queue.Enqueue(owner);
        }

        public void AddItem(LiveModelPhaseOwner owner) => Request(owner, register, true);
        public void RemoveItem(LiveModelPhaseOwner owner) => Request(owner, unregister, false);
        public void SetUpdateOrder(LiveModelPhaseOwner owner, int order)
        {
            if (owner.UpdateOrder == order)
                return;
            owner.UpdateOrder = order;
            OrderDirty = true;
        }

        public void NeedUpdateOrderSort() => OrderDirty = true;
        void RegisterItems(Queue<LiveModelPhaseOwner> queue)
        {
            int count = queue.Count;
            if (count == 0)
                return;
            if (Tail + count >= execute.Length)
                Array.Resize(ref execute, unchecked((execute.Length + count) * 2));
            int added = 0;
            for (int i = 0; i < count; i++)
            {
                var owner = queue.Dequeue();
                if (!owner.IsRegisterRequested)
                    continue;
                owner.IsRegisterModelUpdater = true;
                owner.IsRegisterRequested = false;
                bool found = false;
                for (int j = 0; j < Tail; j++)
                    if (Same(execute[j], owner))
                    {
                        found = true;
                        break;
                    }

                if (!found)
                    execute[Tail + added++] = owner;
            }

            Tail += added;
        }

        public void UpdateDelayedArray()
        {
            Busy = true;
            RegisterItems(register);
            int count = unregister.Count;
            for (int i = 0; i < count; i++)
            {
                var owner = unregister.Dequeue();
                if (!owner.IsUnregisterRequested)
                    continue;
                owner.IsUnregisterRequested = false;
                owner.IsRegisterModelUpdater = false;
            }

            RemoveDirty |= count > 0;
            Busy = false;
        }

        void DispatchMissedUpdate(Queue<LiveModelPhaseOwner> queue)
        {
            if (Phase == 6 || queue.Count == 0)
                return;
            foreach (var owner in queue)
            {
                // Catch-up deliberately has neither the usual owner gate nor
                // IK1/IK2/PostJob/AfterSpring callbacks in the original.
                if (Phase >= 1)
                    owner.Invoke(LiveModelCallback.UpdatePre);
                if (Phase >= 2)
                    owner.Invoke(LiveModelCallback.Update);
                if (Phase >= 3)
                    owner.Invoke(LiveModelCallback.UpdatePost);
                if (Phase >= 4)
                    owner.Invoke(LiveModelCallback.PreLate);
                if (Phase >= 5)
                    owner.Invoke(LiveModelCallback.Late);
            }
        }

        public void AddItemImmediate(LiveModelPhaseOwner owner)
        {
            Request(owner, fastRegister, true);
            Busy = true;
            DispatchMissedUpdate(fastRegister);
            RegisterItems(fastRegister);
            Busy = false;
        }

        public void UpdateInterruptedArray()
        {
            Busy = true;
            DispatchMissedUpdate(fastRegister);
            RegisterItems(fastRegister);
            Busy = false;
        }

        public void UpdateSort()
        {
            Busy = true;
            if (RemoveDirty)
            {
                int oldTail = Tail, last = Tail, index = 0;
                while (index < last)
                {
                    if (!Alive(execute[index]) || !execute[index].IsRegisterModelUpdater)
                    {
                        while (last > index && (!Alive(execute[last]) || !execute[last].IsRegisterModelUpdater))
                            last--;
                        if (index == last)
                        {
                            execute[index] = null;
                            break;
                        }

                        execute[index] = execute[last];
                        execute[last] = null;
                        last--;
                    }

                    index++;
                }

                int first = 0;
                while (first < execute.Length && Alive(execute[first]))
                    first++;
                Tail = first;
                for (int i = first + 1; i < oldTail; i++)
                    execute[i] = null;
                RemoveDirty = false;
            }

            if (OrderDirty)
            {

                Array.Sort(execute, CompareOwners);

                OrderDirty = false;
            }

            Busy = false;
        }

        void Dispatch(LiveModelCallback callback)
        {
            for (int i = 0; i < Tail; i++)
            {
                var owner = execute[i];
                if (CanExecute(owner))
                    owner.Invoke(callback);
            }
        }

        public void AlterUpdatePre()
        {
            UpdateDelayedArray();
            UpdateSort();
            Dispatch(LiveModelCallback.UpdatePre);
            Phase = 1;
        }

        public void AlterUpdate()
        {
            UpdateDelayedArray();
            UpdateSort();
            Dispatch(LiveModelCallback.Update);
            Phase = 2;
        }

        public void AlterUpdatePost()
        {
            UpdateSort();
            Dispatch(LiveModelCallback.UpdatePost);
            Phase = 3;
        }

        public void AlterLateUpdatePre()
        {
            {
                UpdateSort();
                Dispatch(LiveModelCallback.PreLate);
                Dispatch(LiveModelCallback.IK1);
                Dispatch(LiveModelCallback.IK2);
                Phase = 4;
            }
        }

        public void AlterLateUpdate()
        {
            {
                UpdateSort();
                int jobs = 0;
                for (int i = 0; i < Tail; i++)
                {
                    var owner = execute[i];
                    if (CanExecute(owner))
                        jobs = unchecked(jobs + owner.Invoke(LiveModelCallback.PostJob));
                }

                if (jobs > 0)
                    ScheduleBatchedJobs?.Invoke();
                Dispatch(LiveModelCallback.Late);
                Phase = 5;
            }
        }

        public void AlterLateUpdatePost()
        {
            {
                UpdateSort();
                Dispatch(LiveModelCallback.AfterLate);
                Dispatch(LiveModelCallback.AfterSpring);
                Phase = 6;
            }
        }
    }
}
