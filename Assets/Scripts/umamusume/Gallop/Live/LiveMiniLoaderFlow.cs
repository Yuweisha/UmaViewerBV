using System;
using System.Collections.Generic;

namespace Gallop.Live
{
    // Managed reconstruction of the Mini loader callback 0x4e73830 and the
    // IEnumerator.MoveNext state machine 0x4e71430.
    public sealed class LiveMiniLoadRegistration
    {
        public object Model;
        public object Controller;
        public object AddedComponent;
        public object SecondComponent;
    }

    public interface ILiveMiniLoadHost
    {
        object GetTransform(object model);
        void SetParent(object child, object parent);
        object GetMiniModelController(object model);
        void Initialize(object controller);
        object AddModelComponent(object controller);
        object GetSecondComponent(object model);
        void PrepareSecondComponent(object component);
        void Register(int index, LiveMiniLoadRegistration registration);
    }

    public sealed class LiveMiniLoaderFlow
    {
        public readonly List<string> Events = new List<string>();
        public int LastIndex;
        public LiveMiniLoadRegistration LastRegistration;

        public void LoadCallback(ILiveMiniLoadHost host, object model, int index, object parent)
        {
            if (model == null) throw new NullReferenceException();
            var child = host.GetTransform(model);
            Events.Add("GetTransform");
            host.SetParent(child, parent);
            Events.Add("SetParent");
            var controller = host.GetMiniModelController(model);
            Events.Add("GetComponent<MiniModelController>");
            if (controller == null) throw new NullReferenceException();
            host.Initialize(controller);
            Events.Add("Initialize");
            Events.Add("newobj");
            var registration = new LiveMiniLoadRegistration { Model = model, Controller = controller };
            Events.Add("registration-ctor");
            registration.AddedComponent = host.AddModelComponent(controller);
            Events.Add("AddModelComponent");
            registration.SecondComponent = host.GetSecondComponent(model);
            Events.Add("GetComponent<Second>");
            host.PrepareSecondComponent(registration.SecondComponent);
            Events.Add("0x3eda480");
            Events.Add("0x5e54970");
            host.Register(index, registration);
            Events.Add("TailDirector");
            LastIndex = index;
            LastRegistration = registration;
        }

        // state is passed and mutated like IEnumerator current field.
        public bool MoveNext(ref int state, bool hasContext, object current, Action<object> onCurrent)
        {
            if (state != 0) return false;
            state = -1;
            if (!hasContext) return false;
            Events.Add("newobj");
            Events.Add("iterator-ctor");
            Events.Add("0x33b8160");
            Events.Add("GetCurrent");
            if (current != null)
            {
                onCurrent?.Invoke(current);
                Events.Add("delegate(current)");
                Events.Add("SetActive");
            }
            return false;
        }
    }
}
