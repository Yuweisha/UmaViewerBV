using System;
using System.Collections.Generic;
using UnityEngine;
using Gallop.Live.Cutt;
using Gallop.Live.Cyalume;

namespace Gallop.Live
{
    public partial class StageController
    {
        [SerializeField] private GameObject[] _audienceObjects;
        [SerializeField] private AnimationClip[] _audienceAnimations;
        private LiveAudienceController[] _audienceControllerArray;
        private Vector4[] _audienceWorldPositionArray;
        private LiveTimelineControl audienceOwner;

        public IReadOnlyList<LiveAudienceController> AudienceControllers => _audienceControllerArray;
        public IReadOnlyList<Vector4> AudienceWorldPositions => _audienceWorldPositionArray;

        private void SetupAudienceObject(LiveTimelineControl control)
        {
            if (audienceOwner == control) return;
            ReleaseAudienceObjects();
            audienceOwner = control;
            if (control == null || _audienceObjects == null || _audienceObjects.Length == 0) return;
            var settings = LiveSettingsResourceMap.Load(Director.instance.live.MusicId);
            if (settings.AudienceAnimationNames.Count != 0)
            {
                _audienceAnimations = new AnimationClip[settings.AudienceAnimationNames.Count];
                for (int i = 0; i < _audienceAnimations.Length; i++)
                    _audienceAnimations[i] = LiveFlashResourceUtility.LoadOnView<AnimationClip>(
                        "3d/env/live/common/cyalume_audience/motions/" + settings.AudienceAnimationNames[i]);
            }
            var instances = new List<LiveAudienceController>();
            // Native traverses selected sheets in SheetIndex order and groups in
            // authored order. Duplicate prefab names deliberately create instances.
            for (int sheetIndex = 0; sheetIndex < 3; sheetIndex++)
            {
                int index = control.GetSheetIndex((LiveTimelineDefine.SheetIndex)sheetIndex);
                if (index < 0) continue;
                var sheet = control.data.GetWorkSheet(index);
                if (sheet?.audienceList == null) continue;
                foreach (var group in sheet.audienceList)
                {
                    if (group == null) continue;
                    group.UpdateStatus();
                    var prefab = ResolveAudiencePrefab(group);
                    if (prefab == null)
                        Debug.LogWarning("[StageAudience] Timeline audience has no prefab at authored ObjectIndex=" + group.ObjectIndex + ": " + group.name);
                    instances.Add(prefab != null ? new LiveAudienceController(prefab, transform, _audienceAnimations) : null);
                }
            }
            _audienceControllerArray = instances.ToArray();
            _audienceWorldPositionArray = new Vector4[instances.Count];
        }

        private GameObject ResolveAudiencePrefab(LiveTimelineAudienceData group)
        {
            int index = group.ObjectIndex;
            return _audienceObjects != null && index >= 0 && index < _audienceObjects.Length
                ? _audienceObjects[index] : null;
        }

        public void EvaluateAudienceTimeline(LiveTimelineControl control)
        {
            SetupAudienceObject(control);
            var sheet = control.CurrentWorkSheet;
            if (sheet?.audienceList == null || _audienceControllerArray == null) return;
            int frame = control.CurrentFrame;
            var camera = control.GetCamera(sheet.targetCameraIndex)?.cacheTransform;
            Quaternion rotation = camera != null ? camera.rotation : Quaternion.identity;
            for (int index = 0; index < sheet.audienceList.Count && index < _audienceControllerArray.Length; index++)
            {
                var keys = sheet.audienceList[index]?.keys;
                if (keys == null || keys.Count == 0 || keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) ||
                    !keys.EnablePlayModeTimeline(control.PlayMode)) continue;
                LiveTimelineControl.FindTimelineKey(out var current, out var next, keys, frame);
                var a = current as LiveTimelineKeyAudienceData;
                if (a == null) continue;
                var b = next as LiveTimelineKeyAudienceData;
                float ratio = b != null && b.IsInterpolateKey() ? LiveTimelineControl.CalculateInterpolationValue(a, b, frame) : 0f;
                var sample = LiveAudienceTimeline.Sample(a, b, ratio, frame, rotation);
                var controller = _audienceControllerArray[index];
                if (controller == null) { _audienceWorldPositionArray[index] = Vector4.zero; continue; }
                controller.UpdateInfo(sample, () =>
                {
                    var provider = GetComponentInParent<CyalumePlaybackProvider>() ?? FindObjectOfType<CyalumePlaybackProvider>();
                    if (provider == null || !provider.TryGetCurrentState(control.currentLiveTime, out _, out _, out _, out int choreography)) return null;
                    return (int?)choreography;
                });
                _audienceWorldPositionArray[index] = controller.WorldPosition;
            }
        }

        private void ReleaseAudienceObjects()
        {
            if (_audienceControllerArray != null)
                foreach (var controller in _audienceControllerArray) controller?.Dispose();
            _audienceControllerArray = null; _audienceWorldPositionArray = null; audienceOwner = null;
        }
    }
}
