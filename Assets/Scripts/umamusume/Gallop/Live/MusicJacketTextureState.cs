namespace Gallop.Live
{
    // StageController.set_MusicJacketTexture 0x4ec00e0: replace only when the
    // existing jacket texture is null/destroyed.
    public static class MusicJacketTextureState
    {
        public static object Set(object oldTexture, bool oldDestroyed, object newTexture)
        {
            return oldDestroyed ? newTexture : oldTexture;
        }
    }
}
