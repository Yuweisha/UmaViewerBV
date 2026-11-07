using System;
using Gallop;
using Gallop.Live;

public partial class UmaContainerCharacter
{
    internal LiveModelScaleSink LiveScaleModel;
    public float LiveTotalScale => LiveScaleModel != null ? LiveScaleModel.TotalScale : BodyScale;

#if UNITY_EDITOR
    // Observe the actual calls below without substituting their implementation.
#endif
    internal void ApplyLiveModelScalePhysics(float total, bool updateNodes)
    {
        var spring = LiveCySpringController;
        if (spring != null)
        {
            for (int i = 0; i < 3; i++)
            {
                if (spring.IsUpdateScale)
                {
                    spring.SetScale((CySpringController.Parts)i);

                }
                else
                {
                    spring.SetScale((CySpringController.Parts)i, 1f, updateNodes);

                }
            }
        }

        if (_skirtController != null)
        {
            _skirtController.UpdateScale(total);

        }
    }
}
