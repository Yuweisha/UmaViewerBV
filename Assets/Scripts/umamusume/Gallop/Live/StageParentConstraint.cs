using System.Collections.Generic;

namespace Gallop.Live
{
    public partial class StageController
    {
        // Original4eab6c0 walks every registered work for this hash.
        public void ApplyParentConstraint(int objectHash,bool enable,float weight,bool influence,IReadOnlyList<ConstraintUtil.Source> sources)
        {
            if(!_objectsWorkInfoDictionary.TryGetValue(objectHash,out var works))return;
            foreach(var work in works)
            {
                var component=work.parentConstraint;
                if(component==null){component=work.transform.gameObject.AddComponent<ParentConstraintComponent>();component.SetUpdateTiming(ConstraintBehaviour.UpdateTiming.LateUpdateBeforeModel);work.parentConstraint=component;}
                component.SetEnable(enable);component.SetWeight(weight);component.SetSource(sources);component.SetInfluenceOfSourceScale(influence);
            }
        }
    }
}
