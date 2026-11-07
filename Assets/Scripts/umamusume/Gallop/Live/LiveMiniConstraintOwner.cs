using UnityEngine;

namespace Gallop.Live
{
    // Original Director.MiniCharaController4e69a30/4e69960 ownership contract.
    // Model/animation fields are host adapters until full MiniModelController
    // initialization is ported; this owner does not pretend to initialize it.
    public sealed class LiveMiniConstraintOwner
    {
        public GameObject ModelObject {get;private set;}
        public Component ModelController {get;private set;}
        public ILiveMiniAnimationPlayer AnimationController {get;private set;}
        public ParentConstraintComponent ParentConstraintComponent {get;private set;}
        public LiveMiniConstraintOwner(GameObject model,Component modelController,ILiveMiniAnimationPlayer animation)
        {
            ModelObject=model;ModelController=modelController;AnimationController=animation;
            ParentConstraintComponent=model.AddComponent<ParentConstraintComponent>();ParentConstraintComponent.SetUpdateTiming(ConstraintBehaviour.UpdateTiming.LateUpdatePre);ParentConstraintComponent.enabled=false;
        }
        public void Destroy()
        {
            ParentConstraintComponent=null;AnimationController=null;ModelController=null;
            if(ModelObject!=null){Object.Destroy(ModelObject);ModelObject=null;}
        }
    }
}
