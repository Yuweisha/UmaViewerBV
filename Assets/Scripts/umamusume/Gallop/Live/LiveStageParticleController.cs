using System;
using Gallop.Live.Cutt;
using UnityEngine;
namespace Gallop.Live
{
    public sealed class LiveStageParticleController : IDisposable
    {
        ParticleController[] controllers;
        public LiveStageParticleController(Transform stageRoot)
        {
            controllers = stageRoot != null ? stageRoot.GetComponentsInChildren<ParticleController>() : Array.Empty<ParticleController>();
            foreach (var controller in controllers) controller.Initialize();
        }
        public void Pause(bool paused)
        {
            foreach (var controller in controllers)
                if (controller != null) { if (paused) controller.Pause(); else controller.Resume(); }
        }
        public void AlterUpdate(float deltaTime)
        {
            foreach (var controller in controllers)
                if (controller != null) controller.AlterUpdate(deltaTime);
        }
        public void Update(LiveTimelineParticleData group, float emissionRate)
        {
            if (group == null) return;
            int hash = FNVHash.Generate(group.name);
            foreach (var controller in controllers)
                if (controller != null) controller.UpdateFromTimeline(hash, emissionRate);
        }
        // Evaluator's x=dark, y=light are shader values, not an emission range.
        public void UpdateGroup(LiveTimelineParticleGroupData group, Vector2 darkLight)
        {
            if (group == null) return;
            int hash = FNVHash.Generate(group.name);
            foreach (var controller in controllers)
                if (controller != null) controller.UpdateGroupFromTimeline(hash, darkLight);
        }
        public void Dispose() => controllers = Array.Empty<ParticleController>();
    }
}
