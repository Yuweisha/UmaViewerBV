using Gallop;

public partial class UmaContainerCharacter
{
    public void InitializeLiveMiniPhysics() => LoadLiveNativePhysics();
    public void ResetLiveMiniSpringScale(int parts)
    {
        _cySpringController?.SetScale((CySpringController.Parts)parts);
    }
    public void EvaluateLiveMiniPhysics(float delta)
    {
        if (LiveModelComponentsPaused || !EnablePhysics || !_cySpringLoaded || _cySpringController == null) return;
        _cySpringController.BeginSimulation(delta,false);
        _cySpringController.EndSimulation();
    }
}
