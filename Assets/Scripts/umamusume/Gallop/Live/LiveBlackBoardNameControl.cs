using System;

namespace Gallop.Live
{
    // Original MakeBlackBoardNameTexture (4eaf720). Engine/font installation
    // remains an explicit service; matching uses the asset, targeting uses its clone.
    public interface ILiveBlackBoardNameServices<TObject, TTarget, TTexture>
        where TObject : class where TTarget : class where TTexture : class
    {
        bool ObjectEquals(TObject a, TObject b);
        TTexture Construct();
        void Initialize(TTexture texture);
        void SetAtlas(TTexture texture, TObject atlas);
        TTarget GetComponentInChildren(TObject instance);
        void SetTarget(TTexture texture, TTarget target);
    }

    public sealed class LiveBlackBoardNameControl<TObject, TTarget, TTexture>
        where TObject : class where TTarget : class where TTexture : class
    {
        public TObject[] NamePrefabs;
        public TObject Atlas;
        public TTexture Texture;
        readonly ILiveBlackBoardNameServices<TObject, TTarget, TTexture> source;
        public LiveBlackBoardNameControl(ILiveBlackBoardNameServices<TObject, TTarget, TTexture> source)
        { this.source = source; }

        public void Make(TObject instance, TObject original)
        {
            if (NamePrefabs == null || source.ObjectEquals(Atlas, null)) return;
            // Re-read the original fields after each engine/service call. Duplicate
            // references intentionally register again; the native loop does not break.
            for (int i = 0; i < NamePrefabs.Length; i++)
            {
                if (!source.ObjectEquals(original, NamePrefabs[i])) continue;
                if (Texture == null)
                {
                    Texture = source.Construct();
                    if (Texture == null) throw new NullReferenceException();
                    source.Initialize(Texture);
                    if (Texture == null) throw new NullReferenceException();
                    source.SetAtlas(Texture, Atlas);
                }
                if (ReferenceEquals(instance, null)) throw new NullReferenceException();
                TTarget target = source.GetComponentInChildren(instance);
                if (Texture == null) throw new NullReferenceException();
                source.SetTarget(Texture, target);
            }
        }
    }
}
