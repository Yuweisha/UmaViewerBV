namespace Gallop.Live
{
    // Field getters mirrored from StageController offsets; values are owned externally.
    public sealed class StageFieldGetters
    {
        public object EffectController;
        public object ParticleControllerArray;
        public object MusicJacketTexture;
        public object MusicImageTexture;
        public object Live2dBackGroundController;
        public object EnvCollisionArray;
        public bool IsActiveMirrorReflection;
        public object CachedTransform;

        public object GetEffectController() => EffectController;
        public object GetParticleControllerArray() => ParticleControllerArray;
        public object GetMusicJacketTexture() => MusicJacketTexture;
        public object GetMusicImageTexture() => MusicImageTexture;
        public object GetLive2dBackGroundController() => Live2dBackGroundController;
        public object GetEnvCollisionArray() => EnvCollisionArray;
        public bool GetIsActiveMirrorReflection() => IsActiveMirrorReflection;
        public object GetCachedTransform() => CachedTransform;
        public void PreUpdateAllTimeline() { }
    }
}
