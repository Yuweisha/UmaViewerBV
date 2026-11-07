using System;
using System.Collections.Generic;

namespace Gallop.Live
{
    // Managed reconstruction of RenderingManager.beginCamera 0x523c480 and
    // afterCamera 0x523c210 (single camera, delegate slots present).
    public sealed class LiveCameraDispatchData
    {
        public bool SkipManagerAndOwnerCallbacks; // [data+0xa8] == 1
    }

    public sealed class LiveBeginFrameData
    {
        public bool HasAnchor;                  // [data+0xb0] != null
        public bool SkipAllCallbacks;           // [data+0xa8] == 1
        public bool HasOwnerBeginFrameDelegate; // [data+0x1b0] != null
    }

    public interface ILiveBeginFrameHost
    {
        object GetCameraData(object camera);
        object BeginFrame(object data);
        void MarkBeginHolder(object data);
        bool Handler(object begin);
        object CandidateList(object begin);
        void AddCandidate(object list, object camera);
        void OwnerBeginFrame(object data, object camera);
        void OnBeforeRendering(object data, object begin);
        void ClearBeginList();
    }

    public interface ILiveRenderingManagerHost
    {
        object GetCameraData(object camera);
        void ManagerBegin(object argument, object camera);
        void OwnerBegin(object argument, object camera);
        void OnBeforeCameraRendering();
        void OwnerAfterBegin(object argument, object camera);
        void ProjectorUpdate(object camera);
        void ManagerEnd(object argument, object camera);
        void OwnerEnd(object argument, object camera);
        void OnEndCameraRendering();
    }

    public sealed class LiveRenderingManagerDispatch
    {
        public readonly List<string> Events = new List<string>();

        public void OnBeginFrame(ILiveBeginFrameHost host, IReadOnlyList<object> cameras, object argument)
        {
            if (cameras == null) return;
            for (int i = 0; i < cameras.Count; i++)
            {
                var camera = cameras[i];
                Events.Add("GetCameraData");
                var data = host.GetCameraData(camera) as LiveBeginFrameData;
                if (data == null) continue;
                if (!data.HasAnchor) continue;
                if (data.SkipAllCallbacks) continue;
                var begin = host.BeginFrame(data);
                Events.Add("0x523bc20");
                if (!ReferenceEquals(begin, data))
                {
                    host.MarkBeginHolder(data);
                    if (host.Handler(begin))
                    {
                        Events.Add("0x51c9210");
                        var list = host.CandidateList(begin);
                        Events.Add("0x5d02480");
                        host.AddCandidate(list, camera);
                        Events.Add("ListAdd");
                    }
                }
                if (data.HasOwnerBeginFrameDelegate)
                {
                    host.OwnerBeginFrame(data, camera);
                    Events.Add("delegate:ownerBeginFrame");
                }
                host.OnBeforeRendering(data, begin);
                Events.Add("OnBeforeRendering");
            }
            host.ClearBeginList();
            Events.Add("ClearBeginList");
        }

        public void BeginCamera(ILiveRenderingManagerHost host, object camera, object argument, LiveCameraDispatchData data, bool hasProjector)
        {
            Events.Add("GetCameraData");
            var actual = (LiveCameraDispatchData)host.GetCameraData(camera);
            if (actual != null)
            {
                if (!actual.SkipManagerAndOwnerCallbacks)
                {
                    host.ManagerBegin(argument, camera); Events.Add("managerBegin");
                    host.OwnerBegin(argument, camera); Events.Add("ownerBegin");
                }
                host.OnBeforeCameraRendering(); Events.Add("OnBeforeCameraRendering");
                if (!actual.SkipManagerAndOwnerCallbacks)
                {
                    host.OwnerAfterBegin(argument, camera); Events.Add("ownerAfterBegin");
                }
            }
            if (hasProjector)
            {
                host.ProjectorUpdate(camera); Events.Add("ProjectorManager.Update");
            }
        }

        public void AfterCamera(ILiveRenderingManagerHost host, object camera, object argument, LiveCameraDispatchData data)
        {
            Events.Add("GetCameraData");
            var actual = (LiveCameraDispatchData)host.GetCameraData(camera);
            if (actual == null) return;
            if (!actual.SkipManagerAndOwnerCallbacks)
            {
                host.ManagerEnd(argument, camera); Events.Add("managerEnd");
                host.OwnerEnd(argument, camera); Events.Add("ownerEnd");
            }
            host.OnEndCameraRendering(); Events.Add("OnEndCameraRendering");
        }
    }
}
