using System;
using UnityEngine;

namespace Gallop.Live
{
    // Minimal managed counterparts for the component types referenced by
    // MiniModelController.InitializePartsHolder 0x33cc1d0 and
    // InitializeCheekObject 0x33cb670.
    public sealed class LiveInitializeHolder
    {
        public bool IsSyncLoad;
        public Texture Cheek0;
        public Texture Cheek1;
    }

    public sealed class LiveCheekOwner
    {
        public readonly Texture Cheek0;
        public readonly Texture Cheek1;
        public LiveCheekOwner(Texture cheek0,Texture cheek1){Cheek0=cheek0;Cheek1=cheek1;}
    }

    public sealed class LiveCharaProp
    {
        public int InstanceId;
    }

    public sealed class LiveFacialUpdater
    {
        public int InstanceId;
    }

    public sealed class LiveParentConstraintData
    {
        public object[] Settings = System.Array.Empty<object>();
        public object PayloadId;
    }

    public sealed class LiveTransformParentConstraint
    {
        public object LastSetting;
        public int InitCalls;
        public void Init(object setting){LastSetting=setting;InitCalls++;}
    }

    public interface ILiveAssetHolder
    {
        bool IsExist(string assetName);
        object Get(string assetName);
    }

    public interface ILiveResourceLoader
    {
        object LoadOnHash(string path,int hash);
    }

    public sealed class LiveCharaBoneGroup
    {
        public int Hash;
        public object Payload;
    }

    public sealed class LiveSetupContext
    {
        public object BodyObject;
        public object HeadObject;
        public object TailObject;
        public bool IsSyncLoad;
        public object BuildInfo;
        public object BodyAssetPath;
        public object HeadAssetPath;
        public object MeshHandle;
    }

    public sealed class LiveCySpringUpdaterContext
    {
        public int ControllerId;
        public int SkirtControllerId;
    }

    public sealed class LiveCySpringUpdater
    {
        public LiveCySpringUpdaterContext Context;
        public int Updates;
        public LiveCySpringUpdater(LiveCySpringUpdaterContext context){Context=context;}
        public void UpdateContext(LiveCySpringUpdaterContext context){Context=context;Updates++;}
    }

    public sealed class LivePartsHolderComponent
    {
        public int ZekkenColorIndex;
        public Func<object> GetPartsDataImpl;
        public LivePartsHolderComponent(int zekkenColorIndex){ZekkenColorIndex=zekkenColorIndex;}
        public object GetPartsData()=>GetPartsDataImpl!=null?GetPartsDataImpl():null;
    }

    public interface ILiveModelComponentHost
    {
        T AddModelComponent<T>(T component) where T:class;
        T AddModelComponent<T>() where T:class,new();
        T GetModelComponent<T>() where T:class;
        void DestroyModelComponent<T>(T component) where T:class;
    }

    // Reconstructed owner orchestration. Engine creation/registration is injected
    // through the host so a real Unity fixture can record the same events.
    public sealed class LiveMiniModelOwner
    {
        public MaterialPropertyBlock MaterialBlock {get;private set;}
        public LivePartsHolderComponent PartsHolder {get;private set;}
        public object PartsData {get;private set;}
        public LiveCharaProp PropHolder {get;private set;}
        public LiveMiniCheekRuntime CheekController {get;private set;}
        public bool IsCheek {get;private set;}
        public LiveCySpringUpdater CySpringUpdater {get;private set;}
        public LiveFacialUpdater FacialUpdater {get;private set;}
        public object[] ParentConstraintData {get;private set;}
        public bool ParentConstraintInitialized {get;private set;}
        public LiveCharaBoneGroup CharaBoneGroup {get;private set;}
        public object BuildInfo {get;private set;}
        public object HeadObject {get;private set;}
        public object BodyObject {get;private set;}
        public object TailObject {get;private set;}
        public object MeshHandle {get;private set;}
        public object HeadAssetHolder {get;private set;}
        public object BodyAssetHolder {get;private set;}
        public bool InitCacheTransformCalled {get;private set;}
        public bool InitializeModelComponentSystemCalled {get;private set;}
        public object AttachmentTextureSwapController {get;private set;}
        public bool IsCharaBoneGroupLoaded {get;private set;}
        public object CySpringController {get;private set;}
        public object CySpringOwner {get;private set;}

        public void InitializePartsHolder(ILiveModelComponentHost host)
        {
            MaterialBlock=new MaterialPropertyBlock();
            var context=new LivePartsHolderComponent(4); // Context.ZekkenColorIndex=4
            PartsHolder=host.AddModelComponent(context);
            if(PartsHolder!=null)
            {
                PartsData=PartsHolder.GetPartsData();
                PropHolder=host.AddModelComponent<LiveCharaProp>();
            }
        }

        public void InitializeCheekObject(ILiveModelComponentHost host,Transform facialLocator,
            Func<Transform,LiveCheekOwner,bool,int,LiveMiniCheekRuntime> createController,
            Action<LiveMiniCheekRuntime> linkFacial)
        {
            if(CheekController!=null)return;
            var initHolder=host.GetModelComponent<LiveInitializeHolder>();
            var owner=new LiveCheekOwner(initHolder.Cheek0,initHolder.Cheek1);
            var controller=createController(facialLocator,owner,true,1);
            CheekController=host.AddModelComponent(controller);
            IsCheek=false;
            if(CheekController!=null&&CheekController.HasRendererMaterialArray)
            {
                IsCheek=true;
                linkFacial?.Invoke(CheekController);
            }
            else if(CheekController!=null)host.DestroyModelComponent(CheekController);
        }

        // Original MiniModelController.Setup 0x33cf490:
        // context field copy, head/body AssetHolder lookup, base helpers, inline
        // parts holder init, AttachmentTextureSwapController, tail ParentConstraint.
        public void Setup(ILiveModelComponentHost host,LiveSetupContext context,
            Func<object,object> getAssetHolder,Action initCacheTransform,Action initializeModelComponentSystem,
            Func<object> addAttachment,object[] parentConstraintData,ILiveAssetHolder headHolder,ILiveAssetHolder bodyHolder)
        {
            BuildInfo=context.BuildInfo;HeadObject=context.HeadObject;BodyObject=context.BodyObject;
            TailObject=context.TailObject;MeshHandle=context.MeshHandle;
            if(context.HeadObject!=null)HeadAssetHolder=getAssetHolder(context.HeadObject);
            if(context.BodyObject!=null)BodyAssetHolder=getAssetHolder(context.BodyObject);
            initCacheTransform();InitCacheTransformCalled=true;
            initializeModelComponentSystem();InitializeModelComponentSystemCalled=true;
            InitializePartsHolder(host);
            AttachmentTextureSwapController=addAttachment();
            InitializeParentConstraint(host,parentConstraintData,headHolder,bodyHolder);
        }

        // Original MiniModelController.LoadCharaBoneGroup 0x33cc510:
        // early-out when already loaded, load "3d/Cutt/CharaBoneGroup" by the
        // buildInfo hash and mark loaded when the object is non-null.
        public void LoadCharaBoneGroup(ILiveResourceLoader loader,int buildInfoHash,bool buildInfoPresent)
        {
            if(IsCharaBoneGroupLoaded)return;
            if(!buildInfoPresent)throw new NullReferenceException();
            var group=(LiveCharaBoneGroup)loader.LoadOnHash("3d/Cutt/CharaBoneGroup",buildInfoHash);
            CharaBoneGroup=group;
            IsCharaBoneGroupLoaded=group!=null;
        }

        // Original MiniModelController.CreateCySpringObject 0x33c7d00:
        // owner = new CySpringOwnerImpl(this); controller = AddController(gameObject, FindTransform("Hip"), owner).
        public void CreateCySpringObject(Func<object> gameObject,Func<string,object> findTransform,
            Func<object,object> createOwner,Func<object,object,object,object> addController)
        {
            var go=gameObject();
            var hip=findTransform("Hip");
            var owner=createOwner(this);
            CySpringOwner=owner;
            CySpringController=addController(go,hip,owner);
        }

        // Original MiniModelController color methods:
        // SetCharaColor 0x33cda00 (_CharaColor=17), SetEmissiveColor 0x33cdd00
        // (_EmissiveColor=28), SetSolidColor 0x33ce610 (_MainTex=42, white texture).
        public void SetCharaColor(Color color)
        {
            MaterialBlock.SetColor(ShaderManager.GetPropertyId(ShaderManager.PropertyId._CharaColor),color);
        }

        public void SetEmissiveColor(Color color)
        {
            MaterialBlock.SetColor(ShaderManager.GetPropertyId(ShaderManager.PropertyId._EmissiveColor),color);
        }

        public void SetSolidColor()
        {
            MaterialBlock.SetTexture(ShaderManager.GetPropertyId(ShaderManager.PropertyId._MainTex),Texture2D.whiteTexture);
        }

        // Original MiniModelController.InitializeParentConstraint 0x33cbe80:
        // head/body TransformParentConstraintData payloads replace data[0]/data[1],
        // then every payload setting is initialized through TransformParentConstraint.
        public void InitializeParentConstraint(ILiveModelComponentHost host,object[] data,ILiveAssetHolder headHolder,ILiveAssetHolder bodyHolder)
        {
            data[0]=headHolder!=null&&headHolder.IsExist("parent_constraint")?headHolder.Get("parent_constraint"):null;
            data[1]=bodyHolder!=null&&bodyHolder.IsExist("parent_constraint")?bodyHolder.Get("parent_constraint"):null;
            ParentConstraintData=data;
            foreach(object node in data)
            {
                var payload=(LiveParentConstraintData)node;
                foreach(object setting in payload.Settings)
                {
                    var constraint=host.AddModelComponent<LiveTransformParentConstraint>();
                    constraint.Init(setting);
                }
                ParentConstraintInitialized=true;
            }
        }

        // Original MiniModelController.InitializeModelComponent 0x33cbac0:
        // FacialUpdater is registered first; the inline Cheek branch is gated by
        // _buildInfo (kind==0xd && count>0 skips) and an existing CheekController;
        // the tail runs InitializeCySpringUpdater.
        public void InitializeModelComponent(ILiveModelComponentHost host,bool hasBuildInfo,int buildInfoKind,int buildInfoCount,
            Transform facialLocator,Func<Transform,LiveCheekOwner,bool,int,LiveMiniCheekRuntime> createController,
            Action<LiveMiniCheekRuntime> linkFacial,int cySpringControllerId,int skirtControllerId)
        {
            FacialUpdater=host.AddModelComponent<LiveFacialUpdater>();
            if(hasBuildInfo&&!(buildInfoKind==0xd&&buildInfoCount>0)&&CheekController==null)
                InitializeCheekObject(host,facialLocator,createController,linkFacial);
            InitializeCySpringUpdater(host,cySpringControllerId,skirtControllerId);
        }

        // Original MiniModelController.InitializeCySpringUpdater 0x33cb8c0:
        // create the updater with a {CySpringController, SkirtController} context,
        // or update the existing updater with the same context.
        public void InitializeCySpringUpdater(ILiveModelComponentHost host,int cySpringControllerId,int skirtControllerId)
        {
            var context=new LiveCySpringUpdaterContext{ControllerId=cySpringControllerId,SkirtControllerId=skirtControllerId};
            if(CySpringUpdater==null)CySpringUpdater=host.AddModelComponent(new LiveCySpringUpdater(context));
            else CySpringUpdater.UpdateContext(context);
        }
    }
}
