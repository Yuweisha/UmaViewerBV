using UnityEngine;
using Gallop.RenderPipeline;

namespace Gallop.Live
{
    // Serialized identity and fields come from the authored common spotlight prefab.
    // This is a ground-following mesh, not Spotlight3d or a generated Light cookie.
    [DisallowMultipleComponent]
    public sealed class FollowSpotLightController : MonoBehaviour
    {
        [SerializeField] private Transform _rootTransform;
        [SerializeField] private Transform _locationTransform;
        private static readonly int ColorId = Shader.PropertyToID("_MulColor0");
        private static readonly int PowerId = Shader.PropertyToID("_ColorPower");
        private Renderer[] _renderers;
        private Material[][] _materials;
        private Material[][] _additionTemplates,_multiplyTemplates;
        private bool _illuminated;
        private Color _liveColor;
        private float _livePower;
        private LiveDefine.LightBlendMode _liveBlendMode;

        public void Bind(Transform root, Transform location)
        {
            _rootTransform = root;
            _locationTransform = location;
            _renderers = GetComponentsInChildren<Renderer>(true);
            _materials = new Material[_renderers.Length][];
            _additionTemplates = new Material[_renderers.Length][];
            _multiplyTemplates = new Material[_renderers.Length][];
            // Native Initialize 0x4e7fff0; cctor 0x58831b0 stores (1.7, 1, 1.7).
            // This is the renderer child, independently of the animated controller scale.
            for (int index=0;index<_renderers.Length;index++)
            {
                var renderer=_renderers[index];
                renderer.transform.localScale = new Vector3(1.7f, 1f, 1.7f);
                renderer.sortingOrder = 110;
                renderer.SetPropertyBlock(null);
                // Director.LoadCharacter 0x4e7015a replaces the prefab shader with
                // StageLightBlend, then prepares Addition/Multiply material templates.
                _materials[index]=renderer.materials;
                _additionTemplates[index]=new Material[_materials[index].Length];
                _multiplyTemplates[index]=new Material[_materials[index].Length];
                for(int m=0;m<_materials[index].Length;m++)
                {
                    var material=_materials[index][m];
                    material.shader=ShaderManager.GetShader(ShaderManager.ShaderKinds.StageLightBlend);
                    material.renderQueue=3004; // UpdateSpotLightSettingData 0x4e3feb0.
                    _additionTemplates[index][m]=new Material(material);
                    _multiplyTemplates[index][m]=new Material(material);
                    LiveDefine.TrySetLightBlendModeMaterialProperty(LiveDefine.LightBlendMode.Addition,_additionTemplates[index][m]);
                    LiveDefine.TrySetLightBlendModeMaterialProperty(LiveDefine.LightBlendMode.Multiply,_multiplyTemplates[index][m]);
                    material.SetColor(ColorId,Color.white);
                    material.SetFloat(PowerId,0f);
                }
            }
            ApplyLight(Color.white, 0f, 1f);
        }

        public void ApplyLight(Color color, float power, float scale, LiveDefine.LightBlendMode mode=LiveDefine.LightBlendMode.Addition)
        {
            _illuminated = power > 0f;
            _liveColor=color;
            _livePower=power;
            _liveBlendMode=mode;
            // Native SetScale 0x4e802d0 preserves Y and assigns X/Z directly.
            var localScale = transform.localScale;
            localScale.x = localScale.z = scale;
            transform.localScale = localScale;
        }

        public void SetCharacterVisible(bool visible)
        {
            foreach (var renderer in _renderers)
                renderer.enabled = visible && _illuminated;
        }

        public void AlterLateUpdate(Vector3 groundPosition)
        {
            // Original 0x4e7fc90 selects the blend template every late pass,
            // but only moves and uploads color/power while power is positive.
            var templates=_liveBlendMode==LiveDefine.LightBlendMode.Multiply?_multiplyTemplates:_additionTemplates;
            for(int r=0;r<_materials.Length;r++)
                for(int m=0;m<_materials[r].Length;m++)
                    _materials[r][m].CopyPropertiesFromMaterial(templates[r][m]);
            foreach(var renderer in _renderers)renderer.enabled=_illuminated;
            if(!_illuminated)return;
            // Native BindCharaEffect 0x4e770aa resolves the authored Position_light
            // for X/Z; LocationTransform supplies the model root's Y.
            if(_rootTransform != null)
            {
                groundPosition.x=_rootTransform.position.x;
                groundPosition.z=_rootTransform.position.z;
            }
            if(_locationTransform != null)groundPosition.y=_locationTransform.position.y;
            transform.position = groundPosition;
            foreach(var array in _materials)
                foreach(var material in array)
                {
                    material.SetColor(ColorId,_liveColor);
                    material.SetFloat(PowerId,_livePower);
                }
        }
        private void OnDestroy()
        {
            if(_materials==null)return;
            foreach(var array in _materials)foreach(var material in array)if(material!=null)Destroy(material);
            foreach(var array in _additionTemplates)foreach(var material in array)if(material!=null)Destroy(material);
            foreach(var array in _multiplyTemplates)foreach(var material in array)if(material!=null)Destroy(material);
        }
    }
}
