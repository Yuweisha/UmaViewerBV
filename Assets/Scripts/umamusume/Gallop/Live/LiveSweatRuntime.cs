using System;
using System.Collections.Generic;
using System.Linq;
using Gallop;
using Gallop.Live.Cutt;
using UnityEngine;
using Object=UnityEngine.Object;
namespace Gallop.Live
{
    public sealed class LiveSweatRuntime:IDisposable
    {
        readonly GameObject root;
        readonly GameObject[] models=new GameObject[5];
        readonly Transform[] transforms=new Transform[5],parents=new Transform[5];
        readonly Material[][] materials=new Material[5][];
        readonly int[] shuffled=new int[5],enabled=new int[5];
        static readonly int ColorId=Shader.PropertyToID("_Color"),PowerId=Shader.PropertyToID("_ColorPower"),
            StrengthId=Shader.PropertyToID("_Strength"),ThresholdId=Shader.PropertyToID("_Threshold");
        readonly float[] powers={1,1,1,1,1};
        public int[] ShuffledIndices=>(int[])shuffled.Clone();
        public int[] EnabledIndices=>(int[])enabled.Clone();
        public GameObject Root=>root;
        float controllerAlpha=1f;
        public float ControllerAlpha=>controllerAlpha;
        // Original gate: only an actually present head omit-setting is queried.
        // This is supplied from the model's original Head AssetHolder, not dress data.
        public static bool IsOmitted(Gallop.CharaHeadModelOmitSetting source)=>source!=null&&source.IsSweat;
        public static LiveSweatRuntime Create(Transform head,GameObject locatorPrefab,GameObject modelPrefab,
            Gallop.CharaHeadModelOmitSetting omitSetting)
        {
            if(IsOmitted(omitSetting))return null;
            return new LiveSweatRuntime(head,locatorPrefab,modelPrefab);
        }
        LiveSweatRuntime(Transform head,GameObject locatorPrefab,GameObject modelPrefab)
        {
            root=Object.Instantiate(locatorPrefab);
            for(int i=0;i<5;i++)
            {
                // Original1fef800 uses GetChild(i), retaining authored index order.
                var parent=root.transform.GetChild(i);parents[i]=parent;
                models[i]=Object.Instantiate(modelPrefab);transforms[i]=models[i].transform;
                transforms[i].SetParent(parent,false);
                var renderer=models[i].GetComponent<Renderer>();
                materials[i]=renderer.materials;
                // Runtime defaults are white/power1/strength1/threshold0, not
                // the material asset's authored gray/strength.8/threshold.25.
                shuffled[i]=i;enabled[i]=-1;
            }
            root.transform.SetParent(head,false);
            // Original OnInitialize, after prefab/material setup and head attach,
            // before initial HideAll. Exactly200 calls on the shared scene stream.
            for(int iteration=0;iteration<100;iteration++)
            {
                int a=LiveRandomBoundary.RangeInt(0,5),b=LiveRandomBoundary.RangeInt(0,5);
                if(a!=b){int value=shuffled[a];shuffled[a]=shuffled[b];shuffled[b]=value;}
            }
            for(int i=0;i<5;i++)models[i].SetActive(false);
        }
        public void SetLayer(int layer)
        {
            // Native519d600 applies SetLayerRecursively to each instantiated
            // sweat model; the authored locator root/parents are not changed.
            foreach(var model in models)foreach(var node in model.GetComponentsInChildren<Transform>(true))node.gameObject.layer=layer;
        }
        public void SetRenderQueue(int queue)
        {foreach(var set in materials)foreach(var material in set)material.renderQueue=queue;}
        public void SetStencil(int comparison,int operation,int reference)
        {
            foreach(var set in materials)foreach(var material in set)
            {material.SetInt("_StencilComp",comparison);material.SetInt("_StencilOp",operation);material.SetInt("_StencilMask",reference);}
        }
        public void SetCharacterFade(bool isFade,float characterAlpha)
        {controllerAlpha=isFade?characterAlpha:1f;UpdateMaterials();}
        public void Apply(SweatLocatorUpdateInfo payload)
        {
            for(int i=0;i<5;i++)
            {
                var part=payload.locatorInfo[i];var model=models[i];
                if(model.activeSelf!=part.isVisible)model.SetActive(part.isVisible);
                if(model.activeSelf)
                {
                    if(part.offsetType==LiveTimelineKeySweatLocatorData.LocatorType.Local)transforms[i].localPosition=part.offset;
                    else if(part.offsetType==LiveTimelineKeySweatLocatorData.LocatorType.World&&parents[i]!=null)
                    {
                        var position=parents[i].position;
                        for(int axis=0;axis<3;axis++)position[axis]=CySpringWindParam.NativeAdd(position[axis],part.offset[axis]);
                        transforms[i].position=position;
                    }
                    // Public API under test; do not claim it matches original
                    // Internal_FromEulerRad until the engine oracle is replayed.
                    transforms[i].localRotation=Quaternion.Euler(part.offsetAngle);
                }
                powers[i]=payload.alpha;
            }
            UpdateMaterials();
        }
        void UpdateMaterials()
        {
            for(int i=0;i<5;i++)foreach(var material in materials[i])
            {
                material.SetColor(ColorId,Color.white);
                material.SetFloat(PowerId,CySpringWindParam.NativeMul(powers[i],controllerAlpha));
                material.SetFloat(StrengthId,1f);material.SetFloat(ThresholdId,0f);
            }
        }
        public GameObject Model(int index)=>models[index];
        public Material[] Materials(int index)=>materials[index];
        // Original CharacterObject 4e7b660 returns at the first absent model
        // controller. The current Viewer installs only its assembled model;
        // native reserved-dress creation and teardown remain a separate gap.
        public static void ApplyModels(IReadOnlyList<LiveSweatRuntime> controllers,SweatLocatorUpdateInfo info)
        {
            for(int i=0;i<controllers.Count;i++)
            {var controller=controllers[i];if(controller==null)return;controller.Apply(info);}
        }
        public void Dispose()
        {
            foreach(var set in materials)if(set!=null)foreach(var material in set)if(material!=null)Object.Destroy(material);
            if(root!=null)Object.Destroy(root);
        }
    }
}
public partial class UmaContainerCharacter
{
    public Gallop.Live.LiveSweatRuntime LiveSweat {get;private set;}
    public int LiveSweatPhysicalOwner {get;private set;}=-1;
    public string LiveSweatInstallationStatus {get;private set;}="NotInstalled";
    public string LiveSweatLocatorResource {get;private set;}
    public const string LiveSweatModelResource="3d/effect/sweat/pfb_sweat_0000";
    readonly List<Gallop.Live.LiveSweatRuntime> liveSweatModels=new List<Gallop.Live.LiveSweatRuntime>();
    public IReadOnlyList<Gallop.Live.LiveSweatRuntime> LiveSweatModels=>liveSweatModels;
    // Invoked after the model's actual head fallback and skeleton merge.
    public void InstallLiveSweat(int physicalOwner)
    {
        ReleaseLiveSweat();LiveSweatLocatorResource=null;
        LiveSweatPhysicalOwner=physicalOwner;
        if(!IsLive||IsMini){LiveSweatInstallationStatus="NotApplicable";return;}
        var holder=Head.GetComponent<AssetHolder>();
        var omit=holder!=null?holder.Get<Gallop.CharaHeadModelOmitSetting>("omit_setting"):null;
        if(Gallop.Live.LiveSweatRuntime.IsOmitted(omit)){LiveSweatInstallationStatus="OriginalHeadOmitSweat";return;}
        string prefabName=Head.name.Replace("(Clone)","").Trim();
        var source=LoadedAssets.Where(entry=>entry!=null&&entry.Name.StartsWith("3d/chara/head/",StringComparison.Ordinal)&&
            entry.Name.EndsWith("/"+prefabName,StringComparison.Ordinal)).ToArray();
        if(source.Length!=1)
        {LiveSweatInstallationStatus="UnresolvedOriginalHeadContext";Debug.LogWarning("[LiveSweat] "+LiveSweatInstallationStatus+": "+prefabName,this);return;}
        string[] segments=source[0].Name.Split('/');string context=segments[segments.Length-2];
        if(!context.StartsWith("chr",StringComparison.Ordinal))
        {LiveSweatInstallationStatus="UnresolvedOriginalHeadContext";return;}
        // Original34b9a50 uses a third faceKindId parameter for mob/audience;
        // retain the exact selected head's faceNNN suffix, including mob heads.
        LiveSweatLocatorResource="3d/chara/sweat/"+context+"/"+prefabName+"_sweatlocator";
        var main=UmaViewerMain.Instance;
        if(!main.AbList.TryGetValue(LiveSweatLocatorResource,out var locatorEntry))
        {LiveSweatInstallationStatus="OriginalLocatorResourceAbsent";return;}
        var modelEntry=main.AbList[LiveSweatModelResource];
        foreach(var entry in UmaAssetManager.SearchAB(main,locatorEntry).Concat(UmaAssetManager.SearchAB(main,modelEntry)))
            if(!LoadedAssets.Contains(entry))LoadedAssets.Add(entry);
        InstallLiveSweat(locatorEntry.Get<GameObject>(),modelEntry.Get<GameObject>(),omit);
    }
    public void InstallLiveSweat(GameObject locatorPrefab,GameObject modelPrefab,Gallop.CharaHeadModelOmitSetting omitSetting)
    {
        ReleaseLiveSweat();
        LiveSweat=Gallop.Live.LiveSweatRuntime.Create(HeadBone.transform,locatorPrefab,modelPrefab,omitSetting);
        if(LiveSweat!=null)
        {
            // Original CharacterObject.BindResource4e775a3..4e775b5 resolves
            // LayerIndex14. The actual Unity layer number is project-defined.
            LiveSweat.SetLayer(GraphicSettings.GetLayer(GraphicSettings.LayerIndex.LayerCharacter3D_NotReflect));
            liveSweatModels.Add(LiveSweat);
        }
        LiveSweatInstallationStatus=LiveSweat!=null?"InstalledOriginalAssets":"OriginalHeadOmitSweat";
    }
    public void ApplyLiveSweat(SweatLocatorUpdateInfo info)=>Gallop.Live.LiveSweatRuntime.ApplyModels(liveSweatModels,info);
    public void SetLiveSweatCharacterFade(bool isFade,float characterAlpha)
    {foreach(var controller in liveSweatModels)controller?.SetCharacterFade(isFade,characterAlpha);}
    public void ReleaseLiveSweat()
    {foreach(var controller in liveSweatModels)controller?.Dispose();liveSweatModels.Clear();LiveSweat=null;}
}
