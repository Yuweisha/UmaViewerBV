using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    public class LiveTimelineCharaLocator : ILiveTimelineCharactorLocator
    {
        public UmaContainerCharacter UmaContainer;
        public Dictionary<string, Transform> Bones;
        private readonly Transform _formationRoot;

        private LiveCharaPosition _liveCharaStandingPosition;
        private Vector3 _liveCharaInitialPosition = Vector3.zero;

        private bool _liveCharaVisible = true;
        private Transform _liveParentDefaultTransform;
        private Transform _liveParentTransform;

        private int _liveCharaHeightLevel = 0;
        private readonly float _liveCharaHeightValue;
        private float _liveCharaHeightRatioBase = 1.0f;
        private float _liveCharaHeightRatio = 1.0f;
        private Vector3 _liveCharaFormationHeightRateOffset = Vector3.zero;

        private bool _isPositionNodePositionAddParent = false;
        private bool _isCastShadow = true;
        private Renderer[] _shadowRenderers;
        private float _cySpringRate = 1.0f;
        private int _previousFormationWarmupFrame;
        public LiveCharaEmissiveBridge EmissiveBridge { get; } = new LiveCharaEmissiveBridge();
        public LiveCharacterEmissiveDispatcher EmissiveModels { get; } = new LiveCharacterEmissiveDispatcher();
        public LiveCharacterScaleDispatcher ScaleModels { get; } = new LiveCharacterScaleDispatcher();

        public void AddCharaPropMaterial(Material material) => EmissiveBridge.AddMaterial(material);

        // Original Formation model lifecycle 0x4ef7760 forwards key DirtEnergyB/G/R
        // into ModelController.SetDirtParameter for each available model.
        public void SetDirtParameter(float foot, float lower, float upper)
        {
            EmissiveModels.SetDirtRate(foot, lower, upper);
        }

        public void SetDirtParameter(float foot,float lower,float upper,
            Color dirt,Color rimSpecular,Color toon)
            => EmissiveModels.SetDirtParameter(foot,lower,upper,dirt,rimSpecular,toon);

        private Transform _targetHead, _targetWaist, _targetChest;
        private Transform _targetLeftWrist, _targetLeftAttach, _targetRightWrist, _targetRightAttach;
        private float HeadHeght;
        private float WaistHeght;
        private float ChestHeght;

        // Formation slots can carry stage attachments without loading a visible model.
        // Keep the model root separate from its Position node, as on loaded actors.
        public LiveTimelineCharaLocator(Transform formationRoot)
        {
            _formationRoot = formationRoot;
            var position = new GameObject("Position").transform;
            position.SetParent(formationRoot, false);
            Bones = new Dictionary<string, Transform> { ["Position"] = position };
        }

        public LiveTimelineCharaLocator(UmaContainerCharacter umaContainer)
        {
            UmaContainer = umaContainer;
            // Original initialized CharacterData.HeightWithoutHeel is copied
            // into CharacterObject at BindResource; animation scale is unrelated.
            // This loaded-model bridge excludes native dress/loading lifecycle.
            _liveCharaHeightValue = ResolveLoadedCharacterHeight(umaContainer.CharaData, umaContainer.IsMob);
            LiveTimelineControl.UpdateCharactorLocator(this);
            Bones = umaContainer.LiveModelIK!=null?umaContainer.LiveModelIK.NodeCache:new Dictionary<string, Transform>();

            foreach (Transform bone in umaContainer.GetComponentsInChildren<Transform>(true))
            {
                if (!Bones.ContainsKey(bone.name))
                {
                    Bones.Add(bone.name, bone);
                }
            }

            InitializeEmissiveModels(umaContainer);

            // Stage.StartUpdateTimeline supplies the authored logical frame.
            // Original constructor's initial position is zero until the
            // timeline stand-position initializer installs its local position.
            _liveParentDefaultTransform = null;
            _liveParentTransform = null;
            _liveCharaInitialPosition = Vector3.zero;
            InitializeTargetNodes();

            Debug.Log($"InfoChara {UmaContainer.CharaEntry.Name}: {HeadHeght}, {WaistHeght}, {ChestHeght}");
        }

        // First model-set wiring: one loaded model over the character container,
        // using the already reconstructed RendererHolder.OnInitialize semantics
        // (GetComponentsInChildren<Renderer>(true)). Reserved-dress, mirror and
        // child-prop separation still needs the real CharacterObject registration.
        private void InitializeEmissiveModels(UmaContainerCharacter container)
        {
            if (container == null) return;
            try
            {
                var holder = new LiveRendererHolder();
                holder.OnInitialize(container);
                container.InstallLiveDirtMaterials(holder);
                container.InstallLiveEmissiveScroll(holder);
                var renderers = holder.RendererArray;
                ScaleModels.AddModel(container,holder);
                if (renderers == null || renderers.Length == 0) return;
                var sink = EmissiveModels.AddModel();
                sink.Renderers.AddRange(renderers);
                sink.ScrollComponents.AddRange(container.LiveEmissiveScrollComponents);
                // CharacterObject.SetModelObject0x4e7a244 checks the actual
                // build-info dirt texture array before AddDirtComponent.
                sink.HasDirtController=container.IsUseLiveDirtTexture();
                sink.Dirt.Enabled=sink.HasDirtController;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Emissive model init skipped: " + exception.Message);
            }
        }

        // Loaded-model subset of original LiveModelController.UpdateCachedTransforms.
        // Waist is an authored AssetHolder reference, not a name-search fallback.
        // HipOffsetY originates from the authored body value only when present.
        // Full component scheduling/transform application remains separate.
        private void InitializeTargetNodes()
        {
            _targetHead = GetBone("Head");
            _targetChest = GetBone("Chest");
            var holder = UmaContainer != null && UmaContainer.Body != null ? UmaContainer.Body.GetComponent<Gallop.AssetHolder>() : null;
            var waist = holder != null ? holder.Get<GameObject>("waist") : null;
            _targetWaist = waist != null ? waist.transform : null;
            _targetLeftWrist = GetBone("Wrist_L");
            _targetLeftAttach = GetBone("Hand_Attach_L");
            _targetRightWrist = GetBone("Wrist_R");
            _targetRightAttach = GetBone("Hand_Attach_R");
            // Original caches world Y before animation; no local inverse, body
            // scale multiplier or head padding is baked into these three values.
            HeadHeght = _targetHead != null ? _targetHead.position.y : 0f;
            ChestHeght = _targetChest != null ? _targetChest.position.y : 0f;
            WaistHeght = _targetWaist != null ? _targetWaist.position.y : 0f;
            if (TryGetLoadedHipOffsetY(holder, out float hipOffset))
            {
                float delta = LiveTimelineControl.MonitorFloat32(hipOffset * UmaContainer.BodyScale);
                HeadHeght = LiveTimelineControl.MonitorFloat32(HeadHeght + delta);
                ChestHeght = LiveTimelineControl.MonitorFloat32(ChestHeght + delta);
                WaistHeght = LiveTimelineControl.MonitorFloat32(WaistHeght + delta);
            }
        }

        internal static bool TryGetLoadedHipOffsetY(Gallop.AssetHolder holder, out float value)
        {
            value = 0f;
            if (holder == null || holder._assetTableValue?.list == null) return false;
            foreach (var item in holder._assetTableValue.list)
            {
                if (item == null || item.Key != "root_offset_ty_value") continue;
                value = item.Value;
                return true;
            }
            return false;
        }

        internal static Vector3 NativeHeadTarget(Vector3 position, Vector3 up, float ratio)
        {
            float amount = LiveTimelineControl.MonitorFloat32(ratio * 0.1f);
            return position + up * amount;
        }

        private Vector3 TargetPosition(Transform target) => UmaContainer != null && target != null ? target.position : Vector3.zero;

        private Vector3 ConstHeightPosition(float height, bool head)
        {
            Vector3 value = _liveCharaInitialPosition;
            if (UmaContainer != null)
                value.y = head ? LiveTimelineControl.MonitorFloat32(height + LiveTimelineControl.MonitorFloat32(_liveCharaHeightRatioBase * 0.1f)) : height;
            return value;
        }

        private Vector3 InitialHeightPosition(float height, bool head)
        {
            if (UmaContainer == null) return Vector3.zero;
            return new Vector3(0f, head ? LiveTimelineControl.MonitorFloat32(height + LiveTimelineControl.MonitorFloat32(_liveCharaHeightRatioBase * 0.1f)) : height, 0f);
        }

        private Transform GetBone(string name)
        {
            if (Bones != null && Bones.TryGetValue(name, out Transform bone))
            {
                return bone;
            }

            return null;
        }

        private Transform PositionTransform
        {
            get
            {
                Transform t = GetBone("Position");
                if (t != null)
                {
                    return t;
                }

                return UmaContainer != null ? UmaContainer.transform : null;
            }
        }

        private Vector3 GetBonePosition(string name)
        {
            Transform t = GetBone(name);
            if (t != null)
            {
                return t.position;
            }

            return liveCharaPosition;
        }

        public bool liveCharaVisible
        {
            get => _liveCharaVisible;
            set
            {
                _liveCharaVisible = value;

                // 先只记录状态，不强制 SetActive。
                // 如果这里直接 UmaContainer.gameObject.SetActive(value)，可能会影响官方 timeline/physics 初始化。
            }
        }

        public LiveCharaPosition liveCharaStandingPosition
        {
            get => _liveCharaStandingPosition;
            set => _liveCharaStandingPosition = value;
        }

        public Vector3 liveCharaInitialPosition
        {
            get => _liveCharaInitialPosition;
            set => _liveCharaInitialPosition = value;
        }

        public Vector3 liveCharaPosition
        {
            get
            {
                return PositionTransform != null ? PositionTransform.position : Vector3.zero;
            }
        }

        public Quaternion liveCharaPositionLocalRotation
        {
            get
            {
                Transform t = PositionTransform;
                return t != null ? t.localRotation : Quaternion.identity;
            }
            set
            {
                Transform t = PositionTransform;
                if (t != null)
                {
                    t.localRotation = value;
                }
            }
        }

        public Vector3 liveCharaHeadPosition => UmaContainer != null && _targetHead != null
            ? NativeHeadTarget(_targetHead.position, _targetHead.up, _liveCharaHeightRatioBase) : Vector3.zero;

        public Vector3 liveCharaWaistPosition => TargetPosition(_targetWaist);
        public Vector3 liveCharaChestPosition => TargetPosition(_targetChest);
        public Vector3 liveCharaLeftHandWristPosition => UmaContainer != null && _targetLeftWrist != null
            ? _targetLeftWrist.position : liveCharaWaistPosition + new Vector3(0.5f, 0f, 0f);
        public Vector3 liveCharaLeftHandAttachPosition => UmaContainer != null && _targetLeftAttach != null
            ? _targetLeftAttach.position : liveCharaWaistPosition + new Vector3(0.5f, 0f, 0f);
        public Vector3 liveCharaRightHandWristPosition => UmaContainer != null && _targetRightWrist != null
            ? _targetRightWrist.position : liveCharaWaistPosition + new Vector3(-0.5f, 0f, 0f);
        public Vector3 liveCharaRightHandAttachPosition => UmaContainer != null && _targetRightAttach != null
            ? _targetRightAttach.position : liveCharaWaistPosition + new Vector3(-0.5f, 0f, 0f);
        public Vector3 liveCharaFootPosition => liveCharaPosition;
        public Vector3 liveCharaConstHeightHeadPosition => ConstHeightPosition(HeadHeght, true);
        public Vector3 liveCharaConstHeightWaistPosition => ConstHeightPosition(WaistHeght, false);
        public Vector3 liveCharaConstHeightChestPosition => ConstHeightPosition(ChestHeght, false);
        public Vector3 liveCharaInitialHeightHeadPosition => InitialHeightPosition(HeadHeght, true);
        public Vector3 liveCharaInitialHeightWaistPosition => InitialHeightPosition(WaistHeght, false);
        public Vector3 liveCharaInitialHeightChestPosition => InitialHeightPosition(ChestHeght, false);

        public Vector3 liveCharaScale
        {
            get
            {
                Transform t = PositionTransform;
                return t != null ? t.localScale : Vector3.one;
            }
        }

        public Transform liveParentDefaultTransform
        {
            get => _liveParentDefaultTransform;
            set => _liveParentDefaultTransform = value;
        }

        public Transform liveParentTransform
        {
            get => _liveParentTransform;
            set => _liveParentTransform = value;
        }

        public Transform liveRootTransform
        {
            get
            {
                if (UmaContainer != null)
                {
                    return UmaContainer.transform;
                }

                return _formationRoot != null ? _formationRoot : PositionTransform;
            }
        }

        public int liveCharaHeightLevel
        {
            get => _liveCharaHeightLevel;
            set => _liveCharaHeightLevel = value;
        }

        internal static float ResolveLoadedCharacterHeight(System.Data.DataRow row, bool isMob)
        {
            int value = row == null ? 0 : Convert.ToInt32(row[isMob ? "chara_height" : "scale"]);
            return value > 0 ? LiveTimelineControl.MonitorFloat32(value) : 160f;
        }

        public float liveCharaHeightValue
        {
            get
            {
                return _liveCharaHeightValue;
            }
        }

        public float liveCharaHeightRatioBase
        {
            get => _liveCharaHeightRatioBase;
            set => _liveCharaHeightRatioBase = value;
        }

        public float liveCharaHeightRatio
        {
            get => _liveCharaHeightRatio;
            set => _liveCharaHeightRatio = value;
        }

        public Vector3 liveCharaFormationHeightRateOffset
        {
            get => _liveCharaFormationHeightRateOffset;
            set => _liveCharaFormationHeightRateOffset = value;
        }

        public bool IsPositionNodePositionAddParent
        {
            get => _isPositionNodePositionAddParent;
            set => _isPositionNodePositionAddParent = value;
        }

        public void AlterModelPositionAddParent()
        {
            foreach (var model in ScaleModels.Models)
                model.PositionAdd.AlterLateUpdate(model.Owner.transform, model.Owner.Position,
                    IsPositionNodePositionAddParent);
        }

        public bool IsCastShadow
        {
            get => _isCastShadow;
            set
            {
                // Native CharacterObject -> ModelController -> CharaPartsHolder
                // applies the timeline switch to model parts (0x4e7dcf0 / 0x5171510).
                if (_shadowRenderers == null && UmaContainer != null)
                    _shadowRenderers = UmaContainer.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                _isCastShadow = value;
                if (_shadowRenderers == null) return;
                var mode = value ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                foreach (var renderer in _shadowRenderers)
                    if (renderer != null && renderer.shadowCastingMode != mode) renderer.shadowCastingMode = mode;
            }
        }

        public float CySpringRate
        {
            get => _cySpringRate;
            set
            {
                _cySpringRate = value;
                foreach (var model in ScaleModels.Models)
                    model.Owner.SetLiveFormationCySpringRate(value);
            }
        }

        // Current native UpdateCharacterObject (0x505f260): the marker belongs
        // to the character, defaults to zero, and is cleared only by a non-warm key.
        internal void ApplyFormationCySpringWarmup(LiveTimelineKeyFormationOffsetData key,
            Action<int, float> reserveProps)
        {
            if (!key.warmUpCySpring)
            {
                _previousFormationWarmupFrame = 0;
                return;
            }
            if (_previousFormationWarmupFrame == key.frame) return;
            for (int dress = 0; dress < ScaleModels.Models.Count; dress++)
            {
                ScaleModels.Models[dress].Owner.ReserveLiveFormationCySpring(key.CySpringWarmUpTime);
                reserveProps?.Invoke(dress, key.PropCySpringWarmUpTime);
            }
            _previousFormationWarmupFrame = key.frame;
        }

        public int LayerIndex
        {
            set
            {
                if (UmaContainer == null)
                {
                    return;
                }

                // Original SetLiveLayerIndex 0x575d170 caches the raw int.
                // GetLayer/ExternalLateUpdate resolves [CHAR,_0,_1] and mirror
                // visibility together; indices outside0..2 fall back to CHAR.
                UmaContainer.LiveMirrorModelLayerIndex=value;
            }
        }

        public Color EmissiveColor
        {
            // Character-level path: CharacterObject.set_EmissiveColor 0x4e7dba0
            // loops the reserved models and each ModelController writes its
            // MaterialPropertyBlock (property id 28). The explicit material
            // bridge remains a diagnostic-only sink.
            set
            {
                EmissiveModels.SetColor(value);
                EmissiveBridge.SetColor(value);
            }
        }

        public void SetEmissiveScale(float speed, float energyScale)
        {
            EmissiveModels.SetScroll(speed, energyScale);
        }

        public void SetEmissiveIntensityParam(float emissiveIntensity)
        {
            EmissiveModels.SetIntensity(emissiveIntensity);
            EmissiveBridge.SetIntensity(emissiveIntensity);
        }

        public void SetEmissiveSoftEdgeParam(float emissiveRimPower, float emissiveRimIntensity, bool isemissiveCenter)
        {
            EmissiveModels.SetSoftEdge(emissiveRimPower, emissiveRimIntensity, isemissiveCenter);
            EmissiveBridge.SetSoftEdge(emissiveRimPower, emissiveRimIntensity, isemissiveCenter);
        }

        public void SetScaleFactor(float scale)
        {
            ScaleModels.SetFactor(scale);
        }
    }
}
