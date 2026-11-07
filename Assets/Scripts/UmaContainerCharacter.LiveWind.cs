using System;
using Gallop;

public partial class UmaContainerCharacter
{
    internal CySpringController LiveCySpringController => _cySpringLoaded ? _cySpringController : null;

    // Original ModelController.ApplyWindData 529a190 / EndWind 529b320.
    // Director dispatches physical actor slots, including reserved model instances.
    public void SetLiveWind(bool enabled, CySpringWindParam parameter)
    {
        if (!_cySpringLoaded) return;
        if (_cySpringController == null) throw new NullReferenceException();
        if (!enabled) { _cySpringController.EndWind(); return; }
        if (parameter == null) throw new NullReferenceException();
        for (int i=0;i<3;i++)
        {
            var part = (CySpringController.Parts)i;
            _cySpringController.SetHasWind((parameter.PartsMask & (1 << i)) != 0,part);
            _cySpringController.BeginWind(parameter,part);
        }
    }
}
