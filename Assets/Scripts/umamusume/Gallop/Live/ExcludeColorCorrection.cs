using System.Collections.Generic;
using Gallop.Live.Cutt;
using Gallop.RenderPipeline;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gallop
{
    // Managed reconstruction of Gallop.ExcludeColorCorrection data/state layer
    // (0x56858a0 ctor, 0x5684d80 AddExcludeColorCorrection, 0x56853e0
    // TryGetCharacterObject, 0x5685160 CopyFromParentCamera and the active/restore
    // half of 0x5685550). The two camera-event-callback render passes
    // (0x5684e70) remain a separate stage.
    public sealed class ExcludeColorCorrection
    {
        private Camera _colorCorrectionCamera;
        private GameObject _colorCorrectionCameraObject;
        private Transform _colorCorrectionCameraTransform;
        private bool _excludeActive;
        private int _excludeColorCorrectionCRPId = -1;
        private int _undoCullingResultsCRPId = -1;
        private readonly List<int> _excludeColorCorrectionModelIndexList = new List<int>(32);
        private readonly List<int> _excludeColorCorrectionShadowIndexList = new List<int>(32);
        private int _backupParentCameraCullingMask;
        private CameraClearFlags _cachedCameraClearFlags;
        private int _cachedCameraCullingMask;
        private DepthTextureMode _cachedCameraDepthTextureMode;
        public SimpleColorCorrectionPass.Parameter PreColorCorrectionParameter;
        private readonly IReadOnlyList<UmaContainerCharacter> _characterObjectList;
        private readonly Camera _parentCamera;
        private CullingResults _cullingResults;

        public Camera ColorCorrectionCamera => _colorCorrectionCamera;
        public GameObject ColorCorrectionCameraObject => _colorCorrectionCameraObject;
        public Transform ColorCorrectionCameraTransform => _colorCorrectionCameraTransform;
        public bool IsExcludeActive => _excludeActive;
        public IReadOnlyList<int> ExcludeModelIndices => _excludeColorCorrectionModelIndexList;
        public IReadOnlyList<int> ExcludeShadowIndices => _excludeColorCorrectionShadowIndexList;
        public Transform ParentTransform { get; }
        public int ExcludeColorCorrectionPassId => _excludeColorCorrectionCRPId;
        public int UndoCullingResultsPassId => _undoCullingResultsCRPId;

        public ExcludeColorCorrection(Transform parent, IReadOnlyList<UmaContainerCharacter> characters)
            : this(parent, characters, null) { }

        public ExcludeColorCorrection(Transform parent, IReadOnlyList<UmaContainerCharacter> characters, Camera parentCamera)
        {
            ParentTransform = parent;
            _parentCamera = parentCamera;
            _characterObjectList = characters;
            _colorCorrectionCameraObject = new GameObject("ColorCorrectionCamera");
            _colorCorrectionCameraTransform = _colorCorrectionCameraObject.transform;
            _colorCorrectionCameraTransform.SetParent(parent, false);
            _colorCorrectionCamera = _colorCorrectionCameraObject.AddComponent<Camera>();
            _colorCorrectionCamera.enabled = false;
        }

        // Bit i set -> character/shadow index list contains i (0..31, ascending).
        public void AddExcludeColorCorrection(uint characterFlags, uint shadowFlags)
        {
            _excludeColorCorrectionModelIndexList.Clear();
            _excludeColorCorrectionShadowIndexList.Clear();
            for (int bit = 0; bit < 32; bit++)
            {
                uint mask = 1u << bit;
                if ((characterFlags & mask) != 0) _excludeColorCorrectionModelIndexList.Add(bit);
                if ((shadowFlags & mask) != 0) _excludeColorCorrectionShadowIndexList.Add(bit);
            }
        }

        public bool TryGetCharacterObject(int index, out UmaContainerCharacter character)
        {
            character = null;
            if (index < 0 || _characterObjectList == null || index >= _characterObjectList.Count) return false;
            character = _characterObjectList[index];
            return character != null;
        }

        public void CopyFromParentCamera(Camera parent)
        {
            if (parent == null || _colorCorrectionCamera == null) return;
            _colorCorrectionCamera.CopyFrom(parent);
            if (_cachedCameraCullingMask != 0) _colorCorrectionCamera.cullingMask = _cachedCameraCullingMask;
            if (_cachedCameraClearFlags != 0) _colorCorrectionCamera.clearFlags = _cachedCameraClearFlags;
            _colorCorrectionCamera.depthTextureMode = _cachedCameraDepthTextureMode;
        }

        // Native OnUpdatePreColorCorrection 0x5685220: retain the authored
        // parameter, rebuild both index lists from the flag words and update the
        // color-correction camera state.
        public void OnUpdatePreColorCorrection(PreColorCorrectionUpdateInfo info, Camera parentCamera)
        {
            PreColorCorrectionParameter = info.Parameter;
            AddExcludeColorCorrection(info.CharacterFlags, info.ShadowFlags);
            SetExcludeActive(true, parentCamera != null ? parentCamera : _parentCamera);
        }

        // Active/restore half of 0x5685550. Pass registration is not part of this
        // stage; pass ids stay -1 until the two callbacks are reconstructed.
        public void SetExcludeActive(bool requested, Camera parentCamera)
        {
            bool canExclude = PreColorCorrectionParameter.Enabled && (requested || _excludeColorCorrectionModelIndexList.Count > 0);
            if (canExclude)
            {
                if (!_excludeActive)
                {
                    if (parentCamera != null && _colorCorrectionCamera != null)
                    {
                        _cachedCameraClearFlags = parentCamera.clearFlags;
                        _cachedCameraCullingMask = parentCamera.cullingMask;
                        _cachedCameraDepthTextureMode = parentCamera.depthTextureMode;
                        _backupParentCameraCullingMask = parentCamera.cullingMask;
                        CopyFromParentCamera(parentCamera);
                    }
                    _colorCorrectionCameraObject.SetActive(true);
                    _colorCorrectionCamera.enabled = true;
                    _excludeActive = true;
                }
                return;
            }
            if (!_excludeActive) return;
            _excludeActive = false;
            if (_colorCorrectionCamera != null) _colorCorrectionCamera.enabled = false;
            if (parentCamera != null)
            {
                parentCamera.cullingMask = _backupParentCameraCullingMask;
                parentCamera.clearFlags = _cachedCameraClearFlags;
                parentCamera.depthTextureMode = _cachedCameraDepthTextureMode;
            }
        }
    }
}
