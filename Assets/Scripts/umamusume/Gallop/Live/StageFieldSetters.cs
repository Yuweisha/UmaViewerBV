namespace Gallop.Live
{
    // Five StageController one-field setters share the same write + barrier shape:
    // 0x1f6ca50 -> +0x210, 0x1f195f0 -> +0x188, 0x4d6880 -> +0xd8,
    // 0x4d6860 -> +0xd0, 0x4ee990 -> +0x120.
    public sealed class StageFieldSetters
    {
        public object ProjectorControllers;
        public object AudienceAnimations;
        public object MirrorScanLightMaterial;
        public object MirrorScanLightTextures;
        public object UVMovieContextArray;

        public void SetProjectorControllers(object value) { ProjectorControllers = value; }
        public void SetAudienceAnimations(object value) { AudienceAnimations = value; }
        public void SetMirrorScanLightMaterial(object value) { MirrorScanLightMaterial = value; }
        public void SetMirrorScanLightTextures(object value) { MirrorScanLightTextures = value; }
        public void SetUVMovieContextArray(object value) { UVMovieContextArray = value; }
    }
}
