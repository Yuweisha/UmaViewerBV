namespace Gallop.Live
{
    // StageController.SetupEnvCollision 0x4eb2a90: store the component getter result
    // into this+0x148 (write barrier), including null.
    public sealed class EnvCollisionState
    {
        public object Collider; // +0x148

        public void Setup(object collider)
        {
            Collider = collider;
        }
    }
}
