using System;
using Gallop.Live.Cutt;
using UnityEngine;

namespace Gallop.Live
{
    public partial class StageController
    {
        private ProjectorController[] _projectorControllerArray;
        private Material _mirrorScanLightMaterial;
        private Texture[] _mirrorScanLightTextures;
        public int LegacyProjectorControllerCount=>_projectorControllerArray?.Length??0;
        public string LegacyProjectorInitializationBoundary { get; private set; }
        private void BindLegacyProjector(LiveTimelineControl control)
        {
            _projectorControllerArray=GetComponentsInChildren<ProjectorController>();
            if(_projectorControllerArray.Length==0)
                LegacyProjectorInitializationBoundary="No legacy ProjectorController in this Stage";
            control.OnUpdateProjector-=UpdateProjector;
            control.OnUpdateProjector+=UpdateProjector;
        }
        private void UnbindLegacyProjector(LiveTimelineControl control)
        {if(control!=null)control.OnUpdateProjector-=UpdateProjector;}
        public void UpdateProjector(ref ProjectorUpdateInfo info)
        {
            if(_projectorControllerArray==null)return;
            foreach(var controller in _projectorControllerArray)
                if(controller.IsInitialized&&controller.ProjectorNameHash==info.nameHash)controller.UpdateStatus(ref info);
        }
        public void InitializeLegacyProjectorStageResources(int musicId)
        {
            const string sourcePath="3d/env/live/common/mirrorscanlight/materials/mtl_env_live_cmn_mslight000";
            var entry=UmaViewerMain.Instance.AbList[sourcePath];
            var source=entry.Get<Material>();
            if(!source)throw new InvalidOperationException("Original mirror scan material is missing");
            var shader=ShaderManager.GetShader(ShaderManager.ShaderKinds.StageLightBlend);
            if(!shader)throw new InvalidOperationException("Original StageLightBlend shader is not loaded");
            if(_mirrorScanLightMaterial)Destroy(_mirrorScanLightMaterial);
            _mirrorScanLightMaterial=Instantiate(source);_mirrorScanLightMaterial.shader=shader;
            var settings=LiveSettingsResourceMap.Load(musicId);
            _mirrorScanLightTextures=new Texture[settings.MirrorScanTextureIds.Count];
            for(int i=0;i<_mirrorScanLightTextures.Length;i++)
            {
                string path="3d/env/live/common/mirrorscanlight/textures/tex_env_live_cmn_mslight"+
                    settings.MirrorScanTextureIds[i].ToString("000",System.Globalization.CultureInfo.InvariantCulture);
                _mirrorScanLightTextures[i]=UmaViewerMain.Instance.AbList[path].Get<Texture2D>();
            }
        }
        private void InitializeLegacyProjectorStagePart(GameObject original,GameObject instance)
        {
            // Original StartStageObject4eb4d59/4eb5136: the ordinary part clone
            // owns the AssetHolder and all dynamically added child controllers.
            if(!original.name.Contains("_mslight_controller")||!IsProjectorStageEnabled)return;
            if(!_mirrorScanLightMaterial)
            {
                var director=Director.instance;
                if(!director||director.live==null)throw new InvalidOperationException("Legacy projector Stage requires its original song resources");
                InitializeLegacyProjectorStageResources(director.live.MusicId);
            }
            var holder=instance.GetComponent<AssetHolder>();
            if(!holder)return;
            InitializeLegacyProjectorOwner(instance.transform,holder,_mirrorScanLightMaterial,_mirrorScanLightTextures);
        }
        private void CompleteLegacyProjectorStageInitialization()
        {
            _projectorControllerArray=GetComponentsInChildren<ProjectorController>();
            foreach(var controller in _projectorControllerArray)controller.ProjectorTextureArray=_mirrorScanLightTextures;
            LegacyProjectorInitializationBoundary=_projectorControllerArray.Length==0?"No legacy ProjectorController in this Stage":null;
        }
        private void ReleaseLegacyProjectorStageResources()
        {
            if(_mirrorScanLightMaterial)Destroy(_mirrorScanLightMaterial);
            _mirrorScanLightMaterial=null;_mirrorScanLightTextures=null;_projectorControllerArray=null;
        }
        // Explicit owner adapter: callers must supply original, proven Stage holder,
        // material and ordered textures. No resource guessing or global name scan.
        public ProjectorController[] InitializeLegacyProjectorOwner(Transform parent,AssetHolder holder,Material sourceMaterial,Texture[] textures)
        {
            if(!parent||!holder)throw new ArgumentNullException("Original legacy Stage parent/AssetHolder");
            int count=holder._assetTable.list.Count;
            var controllers=new System.Collections.Generic.List<ProjectorController>();
            int layer=GraphicSettings.GetLayer(GraphicSettings.LayerIndex.LayerBackground3D_NotReflect);
            for(int i=0;i<count;i++)
            {
                string key="mslight"+i.ToString("000",System.Globalization.CultureInfo.InvariantCulture);
                var prefab=holder.Get<GameObject>(key);
                if(!prefab)continue;
                var instance=Instantiate(prefab,parent,false);
                foreach(var transform in instance.GetComponentsInChildren<Transform>(true))transform.gameObject.layer=layer;
                var controller=instance.AddComponent<ProjectorController>();
                controller.Initialize(key);controller.SetMaterial(sourceMaterial,3020+i);controller.ProjectorTextureArray=textures;
                controllers.Add(controller);
            }
            LegacyProjectorInitializationBoundary=null;return controllers.ToArray();
        }
    }
}
