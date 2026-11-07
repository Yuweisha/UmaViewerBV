using System;
using UnityEngine;

namespace Gallop.Live
{
    // Serialized contract from common prop000/001 TypeTrees (assembly: umamusume).
    // Height adjustment follows the actual game Props.SetScale (Komoe RVA 0x71daed0).
    public sealed class Props : MonoBehaviour
    {
        public sealed class Constraint
        {
            ParentConstraintComponent _parentConstraint;
            internal ParentConstraintComponent Component=>_parentConstraint;
            public void Initialize(GameObject owner)
            {
                _parentConstraint=owner.AddComponent<ParentConstraintComponent>();
                _parentConstraint.SetUpdateTiming(ConstraintBehaviour.UpdateTiming.LateUpdateBeforeModel);
                _parentConstraint.enabled=false;
            }
            public void SetEnable(bool value){if(_parentConstraint!=null)_parentConstraint.SetEnable(value);}
            public void SetWeight(float value){if(_parentConstraint!=null)_parentConstraint.SetWeight(value);}
            public void SetSource(System.Collections.Generic.IReadOnlyList<ConstraintUtil.Source> value){if(_parentConstraint!=null)_parentConstraint.SetSource(value);}
            public void SetInfluenceOfSourceScale(bool value){if(_parentConstraint!=null)_parentConstraint.SetInfluenceOfSourceScale(value);}
        }
        Constraint _constraint;
        internal ParentConstraintComponent ParentConstraint=>_constraint?.Component;
        public void InitializeConstraint(){if(_constraint!=null)return;_constraint=new Constraint();_constraint.Initialize(gameObject);}
        public void ApplyConstraint(bool enable,float weight,bool influenceOfSourceScale,System.Collections.Generic.IReadOnlyList<ConstraintUtil.Source> sources)
        {
            if(_constraint==null)return;
            _constraint.SetEnable(enable);_constraint.SetWeight(weight);_constraint.SetSource(sources);_constraint.SetInfluenceOfSourceScale(influenceOfSourceScale);
        }
        [Serializable]
        public sealed class AdjustmentData
        {
            public Transform Transform;
            public Vector3 OffsetRange;
            public Vector3 TransformRate;
            public int TargetNode;
            public Vector3 TargetOffset;

            public Vector3 TargetPosition { get; internal set; }
        }

        [SerializeField] private AdjustmentData[] _adjustmentDataArray;
        [SerializeField] private bool _isInfluenceOfCharaHeight;
        [SerializeField] private GameObject[] _blinkLightRootObjectArray;
        [SerializeField] private Transform[] _stageObjectTransformArray;
        private readonly System.Collections.Generic.List<Transform> _attachNodeTransformList = new System.Collections.Generic.List<Transform>();
        private bool _isInitialized;
        internal LiveEmissiveScrollRuntime EmissiveScroll {get;private set;}
        readonly System.Collections.Generic.List<Material> _emissiveMaterialInstances=new System.Collections.Generic.List<Material>();
        readonly System.Collections.Generic.List<Renderer> _emissiveRenderers=new System.Collections.Generic.List<Renderer>();
        readonly System.Collections.Generic.List<Material[]> _emissiveOriginalMaterials=new System.Collections.Generic.List<Material[]>();

        internal void InitializeEmissiveScroll()
        {
            // Original Props reads only its root AssetHolder's named data.
            var holder=GetComponent<Gallop.AssetHolder>();var data=holder!=null?holder.Get<Gallop.CharaShaderEffectData>("chara_shader_effect"):null;
            if(data==null)return;
            bool hasScroll=false;foreach(var setting in data.Settings)if(setting.Type==Gallop.CharaShaderEffectData.EffectType.UVScrollEmissive){hasScroll=true;break;}
            if(!hasScroll)return;
            InitializeOwnedMaterials();
            var groups=new System.Collections.Generic.List<Material[]>();
            foreach (var renderer in _emissiveRenderers) groups.Add(renderer.sharedMaterials);
            EmissiveScroll=LiveEmissiveScrollRuntime.ApplyLiveProp(data.Settings,groups.ToArray(),true);
        }

        private void InitializeOwnedMaterials()
        {
            if (_emissiveRenderers.Count != 0) return;
            foreach(var renderer in GetComponentsInChildren<Renderer>(true))
            {
                var original=renderer.sharedMaterials;var materials=new Material[original.Length];
                for(int i=0;i<original.Length;i++)
                {
                    if (original[i] == null) continue;
                    materials[i]=new Material(original[i]){name=original[i].name};
                    _emissiveMaterialInstances.Add(materials[i]);
                }
                _emissiveRenderers.Add(renderer);_emissiveOriginalMaterials.Add(original);
                renderer.sharedMaterials=materials;
            }
        }

        internal void InitializeDepthWriteAlphaMeshes(bool isToonProp, bool depthWriteAlpha)
        {
            // Live Props.Initialize 0x4ff2cc1..0x4ff2dd6: this branch changes
            // the alpha mesh queue to opaque-base + authored name offset.
            if (!isToonProp || !depthWriteAlpha) return;
            InitializeOwnedMaterials();
            foreach (var renderer in GetComponentsInChildren<Renderer>())
            {
                if (!TryGetAlphaMeshQueue(renderer.gameObject.name, out int queue)) continue;
                foreach (var material in renderer.sharedMaterials)
                    if (material != null) material.renderQueue = queue;
            }
        }

        internal static bool TryGetAlphaMeshQueue(string meshName, out int queue)
        {
            foreach (string prefix in new[] { "M_Prop_Alpha", "M_ToonProp_Alpha" })
            {
                if (!meshName.Contains(prefix)) continue;
                int.TryParse(meshName.Substring(prefix.Length), System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int offset);
                queue = unchecked(2000 + offset);
                return true;
            }
            queue = 0;
            return false;
        }

        internal void SetEmissiveScrollParameters(float speed,float energyScale,float deltaTime)
        {
            if(EmissiveScroll==null)return;
            EmissiveScroll.Speed=speed;EmissiveScroll.EnergyScale=energyScale;EmissiveScroll.OtherDeltaTime=deltaTime;
        }
        internal void AfterLateUpdateEmissiveScroll()=>EmissiveScroll?.AfterLateUpdate(false,0f);

        void OnDestroy()
        {
            EmissiveScroll=null;
            for(int i=0;i<_emissiveRenderers.Count;i++)if(_emissiveRenderers[i]!=null)_emissiveRenderers[i].sharedMaterials=_emissiveOriginalMaterials[i];
            foreach(var material in _emissiveMaterialInstances)if(material!=null)
            {
                if(Application.isPlaying)Destroy(material);else DestroyImmediate(material);
            }
            _emissiveRenderers.Clear();_emissiveOriginalMaterials.Clear();_emissiveMaterialInstances.Clear();
        }

        // Attachment phase of original Props.Initialize: truncate authored count,
        // retain null slots and resolve only AssetHolder keys on the actual clone.
        // Complete renderer/CySpring/Toon/constraint initialization is separate.
        internal void InitializeAttachNodes()
        {
            _isInitialized = false;
            _attachNodeTransformList.Clear();
            var holder = GetComponent<Gallop.AssetHolder>();
            if (holder != null)
            {
                int count = unchecked((int)holder.GetValue("prop_attach_counts"));
                for (int i = 0; i < count; i++)
                {
                    var node = holder.Get<GameObject>("prop_attach_" + i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture));
                    _attachNodeTransformList.Add(node != null ? node.transform : null);
                }
            }
            _isInitialized = true;
        }

        public bool TryGetAttachNodeTransform(int index, out Transform target)
        {
            target = null;
            if (!_isInitialized || index < 0 || index >= _attachNodeTransformList.Count) return false;
            target = _attachNodeTransformList[index];
            return target != null;
        }

        internal GameObject[] BlinkLightRootObjectArray => _blinkLightRootObjectArray;
        internal Transform[] StageObjectTransformArray => _stageObjectTransformArray;

        internal void SetScale(Transform headTransform, Transform positionNode, float bodyScale,
            bool isInfluenceOfCharaHeight)
        {
            _isInfluenceOfCharaHeight = isInfluenceOfCharaHeight;
            if (_adjustmentDataArray == null || _adjustmentDataArray.Length == 0) return;

            // Native Head target: world-space Y difference only. Neither the authored
            // local position nor head X/Z participates, and the result is not clamped.
            Vector3 targetPosition = new Vector3(0f, headTransform.position.y - positionNode.position.y, 0f);
            for (int i = 0; i < _adjustmentDataArray.Length; i++)
            {
                var adjustment = _adjustmentDataArray[i];
                if (adjustment.Transform == null) continue;
                adjustment.TargetPosition = targetPosition;
                Vector3 position = _isInfluenceOfCharaHeight ? targetPosition / bodyScale : targetPosition;
                adjustment.Transform.localPosition = Vector3.Scale(
                    position + adjustment.TargetOffset - adjustment.OffsetRange, adjustment.TransformRate);
            }
        }
    }
}
