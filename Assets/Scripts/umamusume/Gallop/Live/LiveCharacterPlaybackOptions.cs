namespace Gallop.Live
{
    // RegisterDownload0x4e329b9..4e32a39 copies each array independently and
    // leaves an out-of-range field unchanged. New CharacterData flags are false.
    public sealed class LiveCharacterPlaybackOptions
    {
        public bool IsWet, IsDirt;
        public int EyeReflectionTextureSizeRate;
        public void Apply(Cutt.LiveTimelineCharacterSettings settings,int slot)
        {
            if(settings==null||slot<0)return;
            if(settings.useWetTexture!=null&&slot<settings.useWetTexture.Length)IsWet=settings.useWetTexture[slot];
            if(settings.UseDirtTextureArray!=null&&slot<settings.UseDirtTextureArray.Length)IsDirt=settings.UseDirtTextureArray[slot];
            if(settings.EyeReflectionTextureSizeRateArray!=null&&slot<settings.EyeReflectionTextureSizeRateArray.Length)
                EyeReflectionTextureSizeRate=settings.EyeReflectionTextureSizeRateArray[slot];
        }
    }
}
