using System.Collections.Generic;
using Gallop.Live.Cutt;
using UnityEngine;

namespace Gallop.Live
{
    // Legacy mesh/Animation projector. This is independent of LightProjection/CustomProjector.
    public class ProjectorController : MonoBehaviour
    {
        private string _projectorName;
        private int _projectorNameHash;
        private Texture[] _projectorTextureArray;
        private Transform _transform;
        private Renderer[] _rendererArray;
        private Material _material;
        private int _renderQueue;
        private Animation _animation;
        private string[] _animationClipNameArray;
        private bool _isEnabledRendererArray,_isEnabledMaterial,_isInitialized;
        public int ProjectorNameHash=>_projectorNameHash;
        public bool IsInitialized=>_isInitialized;
        public Texture[] ProjectorTextureArray { set=>_projectorTextureArray=value; }
        public void Initialize(string projectorName)
        {
            _projectorName=projectorName;_projectorNameHash=FNVHash.Generate(projectorName);
            _transform=transform;_rendererArray=GetComponentsInChildren<Renderer>();
            _isEnabledRendererArray=_rendererArray!=null&&_rendererArray.Length>0;
            if(_isEnabledRendererArray)foreach(var renderer in _rendererArray)renderer.enabled=false;
            _animation=GetComponent<Animation>();
            if(_animation&&_animation.GetClipCount()>0)
            {
                var names=new List<string>();
                foreach(AnimationState state in _animation)names.Add(state.clip.name);
                _animationClipNameArray=names.ToArray();
            }
            _isInitialized=true;
        }
        public void SetMaterial(Material source,int renderQueue)
        {
            if(!_isInitialized||!_isEnabledRendererArray||!source)return;
            _material=Instantiate(source);
            _renderQueue=renderQueue;
            if(_material)_material.renderQueue=renderQueue;
            foreach(var renderer in _rendererArray)
            {
                var previous=renderer.material;
                if(previous){Destroy(previous);renderer.sharedMaterial=null;}
                renderer.sharedMaterial=_material;
            }
            _isEnabledMaterial=true;
        }
        public bool UpdateMaterial(ref ProjectorUpdateInfo info,Texture texture)
        {
            if(!_isInitialized||!_isEnabledRendererArray||!texture)return false;
            if(_isEnabledMaterial)LiveDefine.TrySetLightBlendModeMaterialProperty(info.LightBlendMode,_material);
            _material.SetTexture("_MainTex",texture);
            _material.SetColor("_MulColor0",info.color1);
            _material.SetFloat("_ColorPower",info.power);
            foreach(var renderer in _rendererArray)renderer.sharedMaterial=_material;
            return true;
        }
        public void UpdateStatus(ref ProjectorUpdateInfo info)
        {
            if(!_isInitialized||info.nameHash!=_projectorNameHash)return;
            _transform.localPosition=info.position;
            _transform.localEulerAngles=new Vector3(0f,info.rotate%360f,0f);
            _transform.localScale=new Vector3(info.size.x,1f,info.size.y);
            bool material=false,animation=false;
            if(_projectorTextureArray!=null&&info.materialId>=0&&info.materialId<_projectorTextureArray.Length)
                material=UpdateMaterial(ref info,_projectorTextureArray[info.materialId]);
            // Native reads clip-array length when motionId>=0, including its null failure boundary.
            if(_isInitialized&&info.motionId>=0&&info.motionId<_animationClipNameArray.Length)
            {
                string name=_animationClipNameArray[info.motionId];
                if(!_animation.IsPlaying(name)){_animation.Stop();_animation.Play(name);}
                var state=_animation[name];state.time=CySpringWindParam.NativeMul(info.speed,info.progressTime);
                state.enabled=true;_animation.Sample();state.enabled=false;animation=true;
            }
            if(_isInitialized&&_isEnabledRendererArray)
                foreach(var renderer in _rendererArray)renderer.enabled=material&&animation;
        }
        private void OnDestroy()
        {
            if(_material){Destroy(_material);_material=null;}
            if(_isEnabledRendererArray)
                foreach(var renderer in _rendererArray)if(renderer)
                {var material=renderer.material;if(material){Destroy(material);renderer.sharedMaterial=null;}}
            _isEnabledRendererArray=false;_isInitialized=false;
        }
    }
}
