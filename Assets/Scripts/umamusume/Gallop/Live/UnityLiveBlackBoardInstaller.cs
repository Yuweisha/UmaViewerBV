using System;

namespace Gallop.Live
{
    // Reproduces the native-generated camera/canvas; requires bindings to the
    // game's UI hierarchy, layer table and AtlasReference conversion.
    public sealed class UnityLiveBlackBoardInstaller
    {
        readonly UnityLiveBlackBoardResourceServices source;
        readonly UnityLiveBlackBoardGlyphServices glyphSource;
        public UnityLiveBlackBoardInstaller(UnityLiveBlackBoardResourceServices source)
        { this.source = source ?? throw new ArgumentNullException(nameof(source)); }
        public UnityLiveBlackBoardInstaller(UnityLiveBlackBoardResourceServices source, UnityLiveBlackBoardGlyphServices glyphSource) : this(source)
        { this.glyphSource = glyphSource ?? throw new ArgumentNullException(nameof(glyphSource)); }
        public UnityLiveBlackBoardNameServices CreateNameServices() => new UnityLiveBlackBoardNameServices(
            () => new LiveBlackBoardTargets(null),
            t => { t.Resources = new LiveBlackBoardResources<object>(source); t.Resources.Initialize(); },
            (t, atlas) => t.Resources.SetAtlas(atlas));
        public void UnloadNameTextureResource(LiveBlackBoardTargets texture) => texture.Resources.UnloadNameTextureResource();
        public UnityEngine.Texture2D CreateNameTexture(LiveBlackBoardTargets texture, string name)
        {
            if (glyphSource == null) throw new NotSupportedException("Original AtlasReference/ImageCommon and shader/layer bindings are not installed");
            return (UnityEngine.Texture2D)new LiveBlackBoardGlyphControl<object>(glyphSource)
                { Resources = texture.Resources, TextScale = texture.TextScale }.CreateNameTexture(name);
        }
    }
}
