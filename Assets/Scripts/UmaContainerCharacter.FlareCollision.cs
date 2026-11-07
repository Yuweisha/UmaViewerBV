using Gallop.Live;

public partial class UmaContainerCharacter
{
    // Call only after LoadNormalUma/LoadMobUma completed their skeleton merges.
    public void InstallLiveFlareCollisions()
    {
        if(!IsLive || IsMini)return;
        LiveCharacterFlareCollisionRuntime.Schedule(this);
    }
}
