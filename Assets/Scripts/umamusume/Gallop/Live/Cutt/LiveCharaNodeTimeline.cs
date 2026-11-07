using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    [Serializable]
    public class LiveTimelineKeyCharaNodeData : LiveTimelineKeyWithInterpolate
    {
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.CharaNode;
        public LiveCharaPositionFlag PositionFlag;
        public bool EnableHeadCySpring=true,EnableEarCySpring=true,EnableBodyCySpring=true,EnableSkirtCySpring=true,EnableTailCySpring=true;
        public List<string> TargetCySpringBornNameList=new List<string>();
        public List<bool> TargetCySpringBornEnableList=new List<bool>();
    }
    [Serializable] public class LiveTimelineKeyCharaNodeDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyCharaNodeData> { }
    [Serializable]
    public class LiveTimelineCharaNodeData : ILiveTimelineGroupDataWithName
    {
        public LiveTimelineKeyCharaNodeDataList keys=new LiveTimelineKeyCharaNodeDataList();
        public LiveTimelineCharaNodeData():base("CharaNode") { }
        public override ILiveTimelineKeyDataList GetKeyList()=>keys;
    }
    public partial class LiveTimelineWorkSheet
    {
        [SerializeField] public List<LiveTimelineCharaNodeData> CharaNodeDataList;
    }

    // Per-model adapter for original 0x575d750 cache and 0x4ecb160 writes.
    // Explicit cache constructor also represents the already-built native model
    // cache when a host provides its own model-controller ownership bridge.
    public sealed class LiveCharaNodeModel
    {
        public CySpringController Controller { get; }
        public CySpringRootBone[] EarRoots { get; }
        public CySpringRootBone[] TailRoots { get; }
        public CySpringRootBone[] LongSkirtRoots { get; }
        public CySpringRootBone[] MediumSkirtRoots { get; }
        public bool? LastHead {get;private set;}
        public bool? LastEar {get;private set;}
        public bool? LastBody {get;private set;}
        public bool? LastTail {get;private set;}
        public bool? LastSkirt {get;private set;}
        readonly CySpringRootBone[] headPart,bodyPart,tailPart;
        public LiveCharaNodeModel(CySpringController controller):this(controller,
            controller.FindRootSpring("Sp_He_Ear",CySpringController.Parts.Head,true),
            controller.FindRootSpring("Sp_Hi_Tail0_B_00",CySpringController.Parts.Tail,false),
            controller.FindRootSpring(new[]{"Sp_Hi_LSkirt","Sp_Hi_CSkirt"},CySpringController.Parts.Body,true),
            controller.FindRootSpring("Sp_Hi_MSkirt",CySpringController.Parts.Body,true)) { }
        public LiveCharaNodeModel(CySpringController controller,CySpringRootBone[] ears,CySpringRootBone[] tails,CySpringRootBone[] longSkirt,CySpringRootBone[] mediumSkirt)
        {
            Controller=controller??throw new ArgumentNullException(nameof(controller));
            EarRoots=ears??Array.Empty<CySpringRootBone>();TailRoots=tails??Array.Empty<CySpringRootBone>();
            LongSkirtRoots=longSkirt??Array.Empty<CySpringRootBone>();MediumSkirtRoots=mediumSkirt??Array.Empty<CySpringRootBone>();
            headPart=controller.GetRootSpringArray(CySpringController.Parts.Head);
            bodyPart=controller.GetRootSpringArray(CySpringController.Parts.Body);
            tailPart=controller.GetRootSpringArray(CySpringController.Parts.Tail);
        }
        public bool MatchesCurrentParts()=>ReferenceEquals(headPart,Controller.GetRootSpringArray(CySpringController.Parts.Head))&&
            ReferenceEquals(bodyPart,Controller.GetRootSpringArray(CySpringController.Parts.Body))&&ReferenceEquals(tailPart,Controller.GetRootSpringArray(CySpringController.Parts.Tail));
        static void Set(CySpringRootBone[] roots,bool enabled)
        {foreach(var root in roots)if(root!=null)root.SetShouldReflectCalcResult(enabled);}
        public void Apply(LiveTimelineKeyCharaNodeData key)
        {
            Controller.SetEnableCySpringBone(key.EnableHeadCySpring,CySpringController.Parts.Head);LastHead=key.EnableHeadCySpring;
            Set(EarRoots,key.EnableEarCySpring);LastEar=key.EnableEarCySpring;
            Controller.SetEnableCySpringBone(key.EnableBodyCySpring,CySpringController.Parts.Body);LastBody=key.EnableBodyCySpring;
            // Original tail setter only writes the first exact cached root.
            if(TailRoots.Length>0&&TailRoots[0]!=null)TailRoots[0].SetShouldReflectCalcResult(key.EnableTailCySpring);LastTail=key.EnableTailCySpring;
            Set(LongSkirtRoots,key.EnableSkirtCySpring);Set(MediumSkirtRoots,key.EnableSkirtCySpring);LastSkirt=key.EnableSkirtCySpring;
            if(key.TargetCySpringBornNameList==null||key.TargetCySpringBornEnableList==null)throw new NullReferenceException("Original CharaNode target lists");
            int count=System.Math.Min(key.TargetCySpringBornNameList.Count,key.TargetCySpringBornEnableList.Count);
            for(int i=0;i<count;i++)Controller.SetEnableCySpringBone(key.TargetCySpringBornEnableList[i],key.TargetCySpringBornNameList[i],false);
        }
    }
    public static class LiveCharaNodeTimeline
    {
        // Builder parents final UmaContainerCharacter models under physical
        // Director.charaObjs slots; its cast list can omit empty slots.
        public static IEnumerable<CySpringController> ResolveAssembledModels(Director director,int slot)
        {
            if(director==null||slot<0||slot>=20||director.charaObjs==null||slot>=director.charaObjs.Count)yield break;
            var physicalRoot=director.charaObjs[slot];if(physicalRoot==null||director.CharaContainerScript==null)yield break;
            foreach(var actor in director.CharaContainerScript)
                if(actor!=null&&actor.transform.IsChildOf(physicalRoot))
                {
                    var controller=actor.GetComponent<CySpringController>();
                    if(controller!=null)yield return controller;
                }
        }
        public static int Apply(LiveTimelineCharaNodeData group,int frame,TimelinePlayerMode mode,Func<int,IEnumerable<LiveCharaNodeModel>> models,Action<int,LiveCharaNodeModel,LiveTimelineKeyCharaNodeData> observed=null)
        {
            var keys=group?.keys;
            if(keys==null||keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable)||!keys.EnablePlayModeTimeline(mode))return 0;
            LiveTimelineControl.FindTimelineKey(out var current,out var next,keys,frame);
            if(current==null)return 0;
            if(!(current is LiveTimelineKeyCharaNodeData key))throw new InvalidCastException("Original CharaNode key type");
            int count=0;
            for(int slot=0;slot<20;slot++)if(((int)key.PositionFlag&(1<<slot))!=0)
            {
                var owners=models(slot);if(owners==null)continue;
                foreach(var owner in owners)if(owner!=null&&owner.Controller!=null)
                {owner.Apply(key);count++;observed?.Invoke(slot,owner,key);}
            }
            return count;
        }
    }
    public partial class LiveTimelineControl
    {
        readonly Dictionary<CySpringController,LiveCharaNodeModel> _charaNodeModelCache=new Dictionary<CySpringController,LiveCharaNodeModel>();
        // A physical-slot resolver can supply every original model controller.
        // The default bridge contains the currently assembled Viewer model only;
        // reservedModels remain a separate, unimplemented ownership boundary.
        public Func<int,IEnumerable<CySpringController>> CharaNodeModelResolver;
        public event Action<int,LiveCharaNodeModel,LiveTimelineKeyCharaNodeData> OnCharaNodeApplied;
        public int CharaNodeDispatchCount {get;private set;}
        public void ResetCharaNodeConsumers(){_charaNodeModelCache.Clear();CharaNodeDispatchCount=0;}
        IEnumerable<LiveCharaNodeModel> ResolveCharaNodeModels(int slot)
        {
            if(CharaNodeModelResolver!=null)
            {
                var provided=CharaNodeModelResolver(slot);if(provided==null)yield break;
                foreach(var spring in provided)
                    if(spring!=null)yield return GetCharaNodeModel(spring);
                yield break;
            }
            foreach(var current in LiveCharaNodeTimeline.ResolveAssembledModels(Director.instance,slot))
                yield return GetCharaNodeModel(current);
        }
        LiveCharaNodeModel GetCharaNodeModel(CySpringController controller)
        {
            if(!_charaNodeModelCache.TryGetValue(controller,out var model)||!model.MatchesCurrentParts())
                _charaNodeModelCache[controller]=model=new LiveCharaNodeModel(controller);
            return model;
        }
        private void AlterUpdate_CharaNode(LiveTimelineWorkSheet sheet,int frame)
        {
            if(sheet?.CharaNodeDataList==null)return;
            foreach(var group in sheet.CharaNodeDataList)
                CharaNodeDispatchCount+=LiveCharaNodeTimeline.Apply(group,frame,_playMode,ResolveCharaNodeModels,
                    (slot,model,key)=>OnCharaNodeApplied?.Invoke(slot,model,key));
        }
    }
}

