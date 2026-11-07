using System.Collections.Generic;
using Gallop.Live.Cutt;
using UnityEngine;

namespace Gallop.Live
{
    public partial class Director
    {
        LiveParentConstraintRuntime _parentConstraintRuntime;
        ConstraintUpdater _constraintUpdater;
        public event System.Action<string> OnConstraintPhaseDispatch;
        internal void InitializeConstraintUpdater()
        {
            if(_constraintUpdater!=null)return;
            _constraintUpdater=ConstraintUpdater.Instance;
            if(_constraintUpdater==null){var host=new GameObject("Live ConstraintUpdater");host.transform.SetParent(transform,false);_constraintUpdater=host.AddComponent<ConstraintUpdater>();}
        }
        internal void AlterConstraintLateUpdatePre(){OnConstraintPhaseDispatch?.Invoke("Constraint.Pre");_constraintUpdater?.AlterLateUpdatePre();}
        internal void AlterConstraintLateUpdateBeforeModel(){OnConstraintPhaseDispatch?.Invoke("Constraint.BeforeModel");_constraintUpdater?.AlterLateUpdateBeforeModel();}
        readonly List<LiveMiniConstraintOwner> _miniConstraintOwners=new List<LiveMiniConstraintOwner>();
        internal void InitializeMiniConstraintOwners(int count)
        {ReleaseMiniConstraintOwners();for(int i=0;i<count;i++)_miniConstraintOwners.Add(null);}
        internal void InstallMiniConstraintOwner(int index,GameObject model,Component modelController,ILiveMiniAnimationPlayer animation)
        {_miniConstraintOwners[index]=new LiveMiniConstraintOwner(model,modelController,animation);}
        internal void ReleaseMiniConstraintOwners()
        {foreach(var owner in _miniConstraintOwners)owner?.Destroy();_miniConstraintOwners.Clear();}
        internal void InitializeLiveProps()
        {
            InitializeConstraintUpdater();
            // Native PropsManager tables and authored position conditions use
            // physical slots, including empty roots in a sparse story cast.
            var actors=new List<UmaContainerCharacter>(charaObjs.Count);
            var locators=new List<LiveTimelineCharaLocator>(charaObjs.Count);
            for(int i=0;i<charaObjs.Count;i++){var actor=ResolvePhysicalFacialCharacter(i);actors.Add(actor);locators.Add(actor!=null?actor.LiveLocator:null);}
            _livePropsController?.Dispose();
            _livePropsController=new LivePropsController(transform,_liveTimelineControl.data.propsSettings,actors,locators);
            _livePropsController.VariationEnabled=variation=>variation==_liveTimelineControl.SheetVariationId;
        }
        sealed class ParentConstraintBindings : ILiveParentConstraintBindings
        {
            readonly Director owner;
            public ParentConstraintBindings(Director owner){this.owner=owner;}
            public bool TryGetCurrentCharacter(int slot,out int dress)
            {dress=0;var actor=owner.ResolvePhysicalFacialCharacter(slot);return actor!=null&&actor.Body!=null;}
            public bool TryGetCharacterBone(int slot,string name,out Transform node)
            {
                var actor=owner.ResolvePhysicalFacialCharacter(slot);node=null;
                return actor!=null&&actor.LiveLocator!=null&&actor.LiveLocator.Bones.TryGetValue(name,out node);
            }
            public bool TryGetPropsNode(int slot,int dress,int prop,int hash,out Transform node)
            {
                node=null;return owner._livePropsController!=null&&owner._livePropsController.TryGetConstraintSource(owner.ResolvePhysicalFacialCharacter(slot),dress,prop,hash,out node);
            }
            public void ApplyProps(int slot,int dress,int prop,bool enable,float weight,bool influence,IReadOnlyList<ConstraintUtil.Source> sources)
            {owner._livePropsController?.ApplyParentConstraint(owner.ResolvePhysicalFacialCharacter(slot),dress,prop,enable,weight,influence,sources);}
            public ParentConstraintComponent GetMiniConstraint(int index)=>index>=0&&index<owner._miniConstraintOwners.Count?owner._miniConstraintOwners[index]?.ParentConstraintComponent:null;
            public void ApplyStage(int hash,bool enable,float weight,bool influence,IReadOnlyList<ConstraintUtil.Source> sources)
            {owner._stageController.ApplyParentConstraint(hash,enable,weight,influence,sources);}
        }
        internal void BindParentConstraint()
        {UnbindParentConstraint();_parentConstraintRuntime=new LiveParentConstraintRuntime(new ParentConstraintBindings(this));_liveTimelineControl.OnUpdateParentConstraint+=OnUpdateParentConstraint;}
        internal void UnbindParentConstraint()
        {if(_liveTimelineControl!=null)_liveTimelineControl.OnUpdateParentConstraint-=OnUpdateParentConstraint;_parentConstraintRuntime=null;}
        void OnUpdateParentConstraint(in ParentConstraintUpdateInfo info)=>_parentConstraintRuntime.Apply(in info);
        // Mini roots install before timeline binding in InitializeMiniCharacters.
    }
}
