using Gallop.Live.Cutt;
using Gallop.RenderPipeline;
using UnityEngine;

namespace Gallop
{
    public class BgLiveWave : MonoBehaviour
    {
        Renderer _renderer;
        Material _waveMaterial;
        int _lastKeyIndex=-1,_nameHash=-1;
        bool _isInitialized;
        public bool IsUpdateRealTime {get;set;}
        public bool IsInitialized=>_isInitialized;
        public Material WaveMaterial=>_waveMaterial;
        public int LastKeyIndex=>_lastKeyIndex;
#if UNITY_EDITOR
        public event System.Action<string,float[]> MaterialWrite;
#endif
        public void Initialize()
        {
            if(_isInitialized)return;
            _renderer=GetComponent<Renderer>();
            if(_renderer==null)return;
            _waveMaterial=RenderUtils.GetMaterial(_renderer);
            _nameHash=FNVHash.Generate(gameObject.name);_isInitialized=true;
        }
        void Float(string property,float value)
        {
            _renderer.material.SetFloat(property,value);
#if UNITY_EDITOR
            MaterialWrite?.Invoke(property,new[]{value});
#endif
        }
        public void UpdateParam(ref WaveObjectUpdateInfo info)
        {
            if(!_isInitialized||(info.KeyIndex!=-1&&_lastKeyIndex==info.KeyIndex))return;
            // Original53e51d8 updates the cache before comparing the name hash.
            _lastKeyIndex=info.KeyIndex;
            if(info.TimelineNameHash!=_nameHash)return;
            Float("_IsWorldWave",info.IsWorldDir?1f:0f);
            var direction=new Vector4(info.WaveDir.x,info.WaveDir.y,info.WaveDir.z,0f);
            _renderer.material.SetVector("_WaveDir",direction);
#if UNITY_EDITOR
            MaterialWrite?.Invoke("_WaveDir",new[]{direction.x,direction.y,direction.z,direction.w});
#endif
            Float("_WaveFreq",info.WaveFreq);Float("_WaveSpeed",info.WaveSpeed);Float("_WaveSize",info.WaveSize);
        }
        public virtual void AlterUpdate(float liveCurrentTime)
        {if(_isInitialized)Float("_AppTime",liveCurrentTime);}
        protected virtual void OnDestroy()
        {RenderUtils.Destroy(ref _waveMaterial);_isInitialized=false;}
    }
}
