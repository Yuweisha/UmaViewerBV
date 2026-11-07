using System;
using System.Collections.Generic;

namespace Gallop.Live
{
    public interface ILiveBlackBoardTargetServices<TTarget, TMaterial>
        where TTarget : class where TMaterial : class
    {
        TMaterial GetSharedMaterial(TTarget target);
        TMaterial CopyMaterial(TMaterial material);
        void SetSharedMaterial(TTarget target, TMaterial material);
    }
    public static class LiveBlackBoardTargetControl<TTarget, TMaterial>
        where TTarget : class where TMaterial : class
    {
        public sealed class TargetMesh
        {
            public TTarget Renderer;
            public TMaterial Material;
        }
        public static void SetTarget(TTarget target, List<TargetMesh> targets,
            ILiveBlackBoardTargetServices<TTarget, TMaterial> source)
        {
            var entry = new TargetMesh { Renderer = target };
            if (ReferenceEquals(entry.Renderer, null)) throw new NullReferenceException();
            entry.Material = source.CopyMaterial(source.GetSharedMaterial(entry.Renderer));
            source.SetSharedMaterial(entry.Renderer, entry.Material);
            targets.Add(entry);
        }
    }
}
