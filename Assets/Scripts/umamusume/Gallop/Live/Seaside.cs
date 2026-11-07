using UnityEngine;

namespace Gallop
{
    public sealed class Seaside : MonoBehaviour
    {
        int _globalTimePropertyID;
        Renderer _renderer;
        UnityEngine.MaterialPropertyBlock _materialBlock;

        void OnEnable()
        {
            if (_materialBlock == null) _materialBlock = new UnityEngine.MaterialPropertyBlock();
            _globalTimePropertyID = Shader.PropertyToID("_SurfWavePhase");
            var renderer = GetComponent<Renderer>();
            if (renderer != null) _renderer = renderer;
        }

        void OnWillRenderObject()
        {
            if (_renderer == null) return;
            _materialBlock.SetFloat(_globalTimePropertyID, (float)((double)Time.realtimeSinceStartup / 20.0));
            _renderer.SetPropertyBlock(_materialBlock);
        }

        public void SetCurrentTime(float time) => _materialBlock.SetFloat("_AppTime", time);
        public void SetMaterialPropertyBlock(UnityEngine.MaterialPropertyBlock block) => _materialBlock = block;
    }
}
