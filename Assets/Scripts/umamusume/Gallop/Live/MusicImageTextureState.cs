namespace Gallop.Live
{
    // StageController.set_MusicImageTexture 0x4ec0050:
    // replace only when the existing texture is null/destroyed.
    public static class MusicImageTextureState
    {
        public static object Set(object oldTexture, bool oldDestroyed, object newTexture)
        {
            return oldDestroyed ? newTexture : oldTexture;
        }
    }
}
