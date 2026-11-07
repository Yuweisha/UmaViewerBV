using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using Gallop.Live.Cutt;
using Gallop.RenderPipeline;

namespace Gallop.Live
{
    public sealed class TransmittedLightMaskController
    {
        public sealed class RendererData
        {
            public Renderer Renderer {get;private set;}
            public int ColorIndex {get;private set;}
            public Material Material {get;private set;}
            public RendererData(Renderer renderer,int colorIndex)
            {Renderer=renderer;ColorIndex=colorIndex;Material=RenderUtils.GetMaterial(renderer);}
        }
        static readonly ShaderTagId MaskTag=new ShaderTagId("TransmittedLightMask");
        static readonly ShaderTagId LightModeTag=new ShaderTagId("LightMode");
        readonly GameObject _rootObject;
        public RendererData[] _rendererDataArray;
        float[] _maskScaleArray;
        int _maskScalePropertyId;
        public GameObject RootObject=>_rootObject;
        public int LightContainerCount {get;private set;}
        public static bool HasTransmittedLightMaskPassShader(Shader shader)
        {
            if(shader==null)return false;
            for(int i=0;i<shader.passCount;i++)if(shader.FindPassTagValue(i,LightModeTag).Equals(MaskTag))return true;
            return false;
        }
        public static bool NeedControl(GameObject prefab)
        {
            // Original overload excludes inactive descendants and checks only
            // the first shared material on each Transform's Renderer.
            foreach(var child in prefab.GetComponentsInChildren<Transform>().ToList())
                if(child.gameObject.TryGetComponent<Renderer>(out var renderer))
                {
                    var m=renderer.sharedMaterial;
                    if(HasTransmittedLightMaskPassShader(m!=null?m.shader:null))return true;
                }
            return false;
        }
        public TransmittedLightMaskController(GameObject stageObject)
        {
            _rootObject=stageObject;
            var list=new List<RendererData>();var indices=new HashSet<int>();int index=0;
            foreach(var child in stageObject.GetComponentsInChildren<Transform>().ToList())
                if(child.gameObject.TryGetComponent<Renderer>(out var renderer))
                {
                    var shared=renderer.sharedMaterial;
                    if(!HasTransmittedLightMaskPassShader(shared!=null?shared.shader:null))continue;
                    list.Add(new RendererData(renderer,index));indices.Add(index++);
                }
            if(list.Count==0)return;
            LightContainerCount=indices.Count;_rendererDataArray=list.ToArray();
            var source=_rendererDataArray[0].Material;
            for(int i=1;i<_rendererDataArray.Length;i++)RenderUtils.CopyMaterial(source,_rendererDataArray[i].Material,true);
            _maskScalePropertyId=NativeScenePostPass.PropertyId(0x196);
        }
        public void UpdateInfo(ref TransmittedLightMaskUpdateInfo info)
        {
            if(_maskScaleArray==null)_maskScaleArray=new float[info.MaskScaleArray.Length];
            // Preserve original allocation lifetime: a subsequent longer array
            // fails; a shorter copy retains the old tail.
            Array.Copy(info.MaskScaleArray,_maskScaleArray,info.MaskScaleArray.Length);
        }
        public void Update()
        {
            if(_maskScaleArray==null)return;
            foreach(var data in _rendererDataArray)data.Material.SetFloat(_maskScalePropertyId,_maskScaleArray[data.ColorIndex]);
        }
        public GameObject GetChild(int index)
        {
            if(index<0)return null;
            if(index>_rendererDataArray.Length-1)return null;
            return _rendererDataArray[index].Renderer.gameObject;
        }
        public void Release()
        {
            if(_rendererDataArray==null)return;
            foreach(var data in _rendererDataArray){var material=data.Material;RenderUtils.Destroy(ref material);}
            _rendererDataArray=null;
        }
    }
}
