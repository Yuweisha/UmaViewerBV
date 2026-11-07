using System;
using System.Collections.Generic;

namespace Gallop.Live
{
    public partial class StageController
    {
        public CySpringCollisionComponent[] EnvCollisionArray { get; private set; } = Array.Empty<CySpringCollisionComponent>();
        void SetupEnvCollision() => EnvCollisionArray = GetComponentsInChildren<CySpringCollisionComponent>();
    }

    public partial class Director
    {
        // SyncCharacterFromStage.MoveNext 4fce3d0: body and tail, never head.
        void SyncCharacterFromStage()
        {
            BindStageEnvironmentCollisions(RequireStage,
                _stageController != null ? _stageController.EnvCollisionArray : null,
                GetStageCollisionModels());
        }

        IEnumerable<CySpringController> GetStageCollisionModels()
        {
            foreach (var character in CharaContainerScript)
                if (character != null && character.LiveCySpringController != null)
                    yield return character.LiveCySpringController;
        }

        internal static void BindStageEnvironmentCollisions(bool requireStage, CySpringCollisionComponent[] components, IEnumerable<CySpringController> models)
        {
            if (!requireStage || components == null || components.Length == 0) return;
            CySpringCollisionRuntimeData[] data = null;
            foreach (var model in models)
            {
                if (model == null) continue;
                if (data == null)
                {
                    data = new CySpringCollisionRuntimeData[components.Length];
                    for (int i = 0; i < components.Length; i++)
                    {
                        if (!components[i].IsInitializeRuntimeData) components[i].Initialize();
                        data[i] = components[i].RuntimeData;
                    }
                }
                model.ApplyEnvCySpringCollisionToBody(data);
                model.ApplyEnvCySpringCollisionToTail(data);
            }
        }
    }
}
