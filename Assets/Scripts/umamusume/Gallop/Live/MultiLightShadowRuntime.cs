using System;
using System.Collections.Generic;
using Gallop.Live.Cutt;
using Gallop.RenderPipeline;
using UnityEngine;

namespace Gallop
{
    // Original component0x3625a20/0x3625b80. Materials are the actual renderer
    // instances supplied by Stage setup, never a replacement shader or MPB.
    public sealed class MultiLightShadowController : MonoBehaviour
    {
        public bool IsUseParam;
        public Vector3 ShadowCenterPos;
        public Quaternion ShadowRotation=Quaternion.identity;
        public float ShadowFadeStart=100f,ShadowFadeLength=100f;
        public bool IsUseShadowFront,IsShadowNearStart=true;
        public Color ShadowStartColor=new Color(0,0,0,1),ShadowEndColor=new Color(0,0,0,1);
        public float ShadowDistance=15f;
        public Color FillColor=Color.white;
        public bool IsDarkestShadowOnly;
        Renderer[] _rendererArray;
        Material[] _materialArray;
        bool _isInitialized;
        public bool IsInitialized=>_isInitialized;
        public IReadOnlyList<Renderer> Renderers=>_rendererArray;
        public IReadOnlyList<Material> Materials=>_materialArray;
        public void Initialize(Renderer[] renderers,Material[] materials)
        {_rendererArray=renderers;_materialArray=materials;_isInitialized=true;}
        public void Reset()
        {IsUseParam=false;_rendererArray=null;_materialArray=null;_isInitialized=false;CameraData.GlobalMaxShadowDistance=15f;}
        void OnDestroy(){CameraData.GlobalMaxShadowDistance=15f;}
        public void UpdateParamater()
        {
            if(!_isInitialized)return;
            if(_materialArray==null)throw new NullReferenceException("Original MultiLightShadow material array");
            foreach(var material in _materialArray)
            {
                if(material==null)throw new NullReferenceException("Original MultiLightShadow material");
                if(IsUseParam)
                {
                    material.EnableKeyword("USE_SHADOW_PARAM");
                    material.SetVector(ShaderManager.GetPropertyId(ShaderManager.PropertyId._ShadowFadeCenter),new Vector4(ShadowCenterPos.x,ShadowCenterPos.y,ShadowCenterPos.z,0));
                    Vector3 front=ShadowRotation*Vector3.forward;
                    material.SetVector(ShaderManager.GetPropertyId(ShaderManager.PropertyId._ShadowFadeFront),new Vector4(front.x,front.y,front.z,0));
                    material.SetColor(ShaderManager.GetPropertyId(ShaderManager.PropertyId._MulColor2),FillColor);
                    material.SetVector(ShaderManager.GetPropertyId(ShaderManager.PropertyId._ShadowFadeParam),new Vector4(ShadowFadeStart,ShadowFadeLength,IsUseShadowFront?1:0,IsShadowNearStart?1:0));
                    material.SetColor(ShaderManager.GetPropertyId(ShaderManager.PropertyId._ShadowStartColor),ShadowStartColor);
                    material.SetColor(ShaderManager.GetPropertyId(ShaderManager.PropertyId._ShadowEndColor),ShadowEndColor);
                }
                else material.DisableKeyword("USE_SHADOW_PARAM");
                material.SetFloat(ShaderManager.GetPropertyId(ShaderManager.PropertyId._AdditionalLightShadowRenderType),IsDarkestShadowOnly?1:0);
            }
            if(IsUseParam)CameraData.GlobalMaxShadowDistance=ShadowDistance;
        }
    }
}
namespace Gallop.Live
{
    // Original StageController.SetupMultiLightShadow0x4eb3c70 and update0x4ebcf40.
    // Construct after Stage clones its authored children. Root binds the event.
    public sealed class MultiLightShadowRuntime : IDisposable
    {
        MultiLightShadowController controller;
        public MultiLightShadowController Controller=>controller;
        public bool IsInstalled=>controller!=null;
        public MultiLightShadowRuntime(Component stageOwner)
        {
            if(stageOwner==null)throw new ArgumentNullException(nameof(stageOwner));
            var renderers=new List<Renderer>();var materials=new List<Material>();
            foreach(var renderer in stageOwner.GetComponentsInChildren<Renderer>(true))
            {
                if(renderer==null||renderer.sharedMaterial==null||renderer.sharedMaterial.shader==null)
                    throw new NullReferenceException("Original Stage renderer/material/shader");
                if(!renderer.sharedMaterial.shader.name.Contains("DefaultShadow"))continue;
                renderers.Add(renderer);materials.Add(RenderUtils.GetMaterial(renderer));
            }
            if(renderers.Count==0||materials.Count==0)return;
            controller=stageOwner.GetComponent<MultiLightShadowController>();
            if(controller==null)controller=stageOwner.gameObject.AddComponent<MultiLightShadowController>();
            controller.Initialize(renderers.ToArray(),materials.ToArray());
        }
        public void Apply(ref MultiLightShadowUpdateInfo value)
        {
            if(controller==null)return;
            controller.IsUseParam=value.IsUseParam;controller.ShadowCenterPos=value.ShadowCenterPosition;
            controller.ShadowRotation=Quaternion.Euler(value.ShadowForward);
            controller.ShadowFadeStart=value.ShadowFadeStart;controller.ShadowFadeLength=value.ShadowFadeLength;
            controller.IsUseShadowFront=value.IsUseShadowFront;controller.IsShadowNearStart=value.IsShadowNearStart;
            controller.ShadowStartColor=value.ShadowStartColor;controller.ShadowEndColor=value.ShadowEndColor;
            controller.ShadowDistance=value.ShadowDistance;controller.IsDarkestShadowOnly=value.IsDarkestShadowOnly;
            // Stage original does not transfer payload.FillColor to component.
            controller.UpdateParamater();
        }
        public void Dispose()
        {
            if(controller==null)return;
            if(Application.isPlaying)UnityEngine.Object.Destroy(controller);else UnityEngine.Object.DestroyImmediate(controller);
            controller=null;
        }
    }
}
