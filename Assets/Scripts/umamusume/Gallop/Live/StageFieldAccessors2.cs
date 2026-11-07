namespace Gallop.Live
{
    // Second batch of StageController field accessors (offset identity mirrored).
    public sealed class StageFieldAccessors2
    {
        public object AudienceAnimations;
        public object ProjectorTextureDictionary;
        public object MirrorBallProjectorTextureDictionary;
        public object MirrorScanLightTextures;
        public object MirrorScanLightMaterial;
        public object UVMovieContextArray;
        public bool IsEnabledSpotlight3d;
        public bool IsInitialied;

        public object GetAudienceAnimations() => AudienceAnimations;
        public object GetProjectorTextureDictionary() => ProjectorTextureDictionary;
        public object GetMirrorBallProjectorTextureDictionary() => MirrorBallProjectorTextureDictionary;
        public object GetMirrorScanLightTextures() => MirrorScanLightTextures;
        public object GetMirrorScanLightMaterial() => MirrorScanLightMaterial;
        public object GetUVMovieContextArray() => UVMovieContextArray;
        public bool GetIsEnabledSpotlight3d() => IsEnabledSpotlight3d;
        public bool GetIsInitialied() => IsInitialied;
        public void SetIsEnabledSpotlight3d(bool value) => IsEnabledSpotlight3d = value;
    }
}
