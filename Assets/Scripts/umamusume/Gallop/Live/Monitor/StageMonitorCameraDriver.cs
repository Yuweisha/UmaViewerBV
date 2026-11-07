using System;
using System.Collections.Generic;
using Gallop.Live.Cutt;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gallop.Live
{
    /// <summary>Captures the authored MonitorCamera tracks for stage monitor materials.</summary>
    [DisallowMultipleComponent]
    public sealed class StageMonitorCameraDriver : MonoBehaviour
    {
        private static Func<LiveTimelineKeyCameraPositionData, LiveTimelineControl,
            LiveTimelineControl.FindTimelineConfig, Vector3> _getPosition = GetPosition;
        private BezierCalcWork _lookAtBezier;
        private sealed class Slot
        {
            public Camera camera;
            public MonitorCamera monitor;
            public CacheCamera cached;
            public RenderTexture target;
            public readonly BezierCalcWork bezier = new BezierCalcWork();
        }
        private readonly List<Slot> _slots = new List<Slot>();
        public int Count => _slots.Count;
        public Texture GetTexture(int index) => index >= 0 && index < _slots.Count &&
            _slots[index].target != null && _slots[index].target.IsCreated() ? _slots[index].target : null;
        private Slot FindSlot(Camera camera) => _slots.Find(slot => slot.camera == camera);
        private void SelectSlot(int index)
        {
            var slot = _slots[index];
            _captureCamera = slot.camera; _monitorCamera = slot.monitor; _cachedCamera = slot.cached;
            _texture = slot.monitor.monitorTexture; _targetTexture = slot.target; _lookAtBezier = slot.bezier;
        }
        private readonly List<Renderer> _screens = new List<Renderer>();
        private readonly List<bool> _screenVisibility = new List<bool>();
        private Camera _captureCamera;
        private MonitorCamera _monitorCamera;
        private LiveTimelineControl _boundMonitorTimeline;
        private MonitorCamera _boundMonitor;
        private CacheCamera _cachedCamera;
        private RenderTexture _texture; // Alias of the original MonitorCamera's owned RT.
        private RenderTexture _targetTexture; // Borrowed Director screen texture.
        private bool _screensHidden;

        // Director.CreateMonitorTexture allocates before camera enable tracks.
        // A disabled capture camera does not invalidate the borrowed screen RT.
        public Texture Texture => GetTexture(0);
        public RenderTexture CaptureTexture => _texture;
        public bool IsActive => isActiveAndEnabled && _slots.Exists(slot => slot.camera.gameObject.activeSelf &&
            slot.monitor.monitorTexture != null && slot.monitor.monitorTexture.IsCreated());

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            ReleaseCapture();
        }

        private void OnDestroy()
        {
            ReleaseCapture();
        }

        public void SetScreenRenderers(IList<Renderer> screens)
        {
            RestoreScreens();
            _screens.Clear();
            _screenVisibility.Clear();
            if (screens == null) return;
            for (int i = 0; i < screens.Count; i++)
            {
                Renderer screen = screens[i];
                if (screen == null || _screens.Contains(screen)) continue;
                _screens.Add(screen);
                _screenVisibility.Add(false);
            }
        }

        // No camera render is requested from render callbacks: monitor/reflection
        // cameras cannot recursively trigger another monitor capture. Suppress the
        // screen geometry only while this camera renders to avoid RT feedback.
        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (!IsCaptureCamera(camera) || !isActiveAndEnabled || _screensHidden) return;
            _screensHidden = true;
            for (int i = 0; i < _screens.Count; i++)
            {
                Renderer screen = _screens[i];
                if (screen == null) continue;
                _screenVisibility[i] = screen.forceRenderingOff;
                screen.forceRenderingOff = true;
            }
        }

        private void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (IsCaptureCamera(camera)) RestoreScreens();
        }

        public bool IsCaptureCamera(Camera camera) => camera != null && FindSlot(camera) != null;

        // Native installs this body on CameraData's owned end callback.
        // URP14 global endCameraRendering is after context.Submit, so the host
        // adapter runs it in a final pass after resolving the private attachment.
        // Complete game renderer event placement remains a separate boundary.
        internal void CopyCapture(ScriptableRenderContext context, Camera camera)
        {
            var slot = FindSlot(camera);
            if (slot == null || !isActiveAndEnabled) return;
            try
            {
                if (slot.target != null)
                {
                    var cmd = CommandBufferPool.Get("Live.MonitorCamera.Copy");
                    try
                    {
                        cmd.Blit(slot.monitor.monitorTexture, slot.target);
                        context.ExecuteCommandBuffer(cmd);
                    }
                    finally { CommandBufferPool.Release(cmd); }
                }
            }
            finally { RestoreScreens(); }
        }

        private void RestoreScreens()
        {
            if (!_screensHidden) return;
            for (int i = 0; i < _screens.Count; i++)
                if (_screens[i] != null) _screens[i].forceRenderingOff = _screenVisibility[i];
            _screensHidden = false;
        }

        public void ReleaseCapture()
        {
            RestoreScreens();
            foreach (var slot in _slots)
            {
                slot.camera.enabled = false;
                slot.camera.targetTexture = null;
                slot.monitor.ReleaseMonitorTexture();
                if (Application.isPlaying) Destroy(slot.camera.gameObject); else DestroyImmediate(slot.camera.gameObject);
            }
            _slots.Clear();
            _captureCamera = null; _cachedCamera = null; _monitorCamera = null;
            _texture = null; _targetTexture = null;
            _boundMonitorTimeline = null;
            _boundMonitor = null;
        }

        public void UpdateCapture(LiveTimelineControl timeline, float currentFrame, LiveTimelineData data)
        {
            RestoreScreens();
            if (!isActiveAndEnabled || timeline == null || data == null || data.worksheetList == null) return;
            LiveTimelineWorkSheet layerSheet = data.worksheetList.Find(sheet => sheet != null && sheet.monitorCameraLayerKeys != null && sheet.monitorCameraLayerKeys.Count > 0);
            timeline.PrepareMonitorCameraLayerState(layerSheet, Mathf.RoundToInt(currentFrame));
            for (int slotIndex = 0; slotIndex < _slots.Count; slotIndex++)
            {
                SelectSlot(slotIndex);
                UpdateSlot(timeline, currentFrame, data, slotIndex);
            }
            timeline.FinishMonitorCameraRoll();
            if (_slots.Count > 0) SelectSlot(0);
        }

        private void UpdateSlot(LiveTimelineControl timeline, float currentFrame, LiveTimelineData data, int slotIndex)
        {
            if (_texture == null || !_texture.IsCreated()) return;
            LiveTimelineWorkSheet positionSheet = null;
            LiveTimelineWorkSheet lookAtSheet = null;
            LiveTimelineKeyMonitorCameraPositionData positionKey = null;
            LiveTimelineKeyMonitorCameraPositionData nextPositionKey = null;
            LiveTimelineKeyMonitorCameraLookAtData lookAtKey = null;
            LiveTimelineKeyMonitorCameraLookAtData nextLookAtKey = null;
            ILiveTimelineKeyDataList positionKeys = null;
            ILiveTimelineKeyDataList lookAtKeys = null;
            for (int i = 0; i < data.worksheetList.Count; i++)
            {
                LiveTimelineWorkSheet sheet = data.worksheetList[i];
                if (sheet == null) continue;
                if (positionKey == null && sheet.monitorCameraPosKeys != null && sheet.monitorCameraPosKeys.Count > slotIndex)
                {
                    var keys = sheet.monitorCameraPosKeys[slotIndex]?.keys;
                    if (FindKeys(keys, timeline, currentFrame, out positionKey, out nextPositionKey))
                    {
                        positionSheet = sheet;
                        positionKeys = keys;
                    }
                }
                if (lookAtKey == null && sheet.monitorCameraLookAtKeys != null && sheet.monitorCameraLookAtKeys.Count > slotIndex)
                {
                    if (FindKeys(sheet.monitorCameraLookAtKeys[slotIndex]?.keys, timeline, currentFrame, out lookAtKey, out nextLookAtKey))
                    {
                        lookAtSheet = sheet;
                        lookAtKeys = sheet.monitorCameraLookAtKeys[slotIndex].keys;
                    }
                }
            }
            if (positionKey != null)
            {
                var config = new LiveTimelineControl.FindTimelineConfig
                {
                    keyType = LiveTimelineControl.FindTimelineConfig.KeyType.KeyDirect,
                    curKey = positionKey, nextKey = nextPositionKey, posKeys = positionKeys, extraCameraIndex = slotIndex
                };
                bool found = timeline.CalculateCameraPos(out Vector3 position, positionSheet, currentFrame,
                    _cachedCamera, ref config, ref _getPosition);
                timeline.ApplyMonitorCameraPositionSample(slotIndex, positionKey, nextPositionKey,
                    GetRatio(positionKey, nextPositionKey, currentFrame, timeline), found, position);
            }
            // Original wrapper requires both position/lookAt GROUPS; it does not
            // require a valid current position key before independent lookAt work.
            if (lookAtKey != null && lookAtSheet.monitorCameraPosKeys != null && lookAtSheet.monitorCameraPosKeys.Count > slotIndex)
            {
                if (nextLookAtKey != null && nextLookAtKey.IsInterpolateKey())
                    timeline.CacheFutureCameraLookAt(lookAtKey, nextLookAtKey, Mathf.RoundToInt(currentFrame), lookAtKeys);
                Vector3 lookAt = EvaluateLookAt(timeline, _captureCamera.transform.position, lookAtKey, nextLookAtKey, currentFrame, out float localScale);
                // Height-rate production restored; vector/props sampling and
                // original live clock remain explicit adapter boundaries.
                timeline.ApplyMonitorCameraLookAtSample(slotIndex, lookAtKey, lookAt, localScale);
            }
        }

        public void SetupCaptureTexture(RenderTexture target) => SetupCaptureTextures(target == null ? null : new[] { target });

        public void SetupCaptureTextures(RenderTexture[] targets)
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                var target = targets != null && i < targets.Length ? targets[i] : null;
                _slots[i].target = target;
                _slots[i].monitor.SetupCaptureTexture(target);
            }
            if (_slots.Count > 0) SelectSlot(0);
        }

        internal void BindTimelineIfNeeded(LiveTimelineControl timeline)
        {
            if (timeline == null || _slots.Count == 0 ||
                (_boundMonitorTimeline == timeline && _boundMonitor == _slots[0].monitor)) return;
            var monitors = new MonitorCamera[_slots.Count];
            for (int i = 0; i < monitors.Length; i++) monitors[i] = _slots[i].monitor;
            timeline.SetMonitorCamera(monitors);
            _boundMonitorTimeline = timeline;
            _boundMonitor = monitors[0];
        }

        public void InitializeCapture(int width, int height, int count = 1)
        {
            if (_slots.Count != count) ReleaseCapture();
            if (_slots.Count == 0)
            {
                var prefab = Resources.Load<GameObject>("Prefabs/Live/MonitorCamera");
                if (prefab == null) throw new InvalidOperationException("Original MonitorCamera prefab is not installed");
                for (int i = 0; i < count; i++)
                {
                    GameObject go = Instantiate(prefab, transform, false);
                    go.name = "MonitorCamera[" + i + "]";
                    var slot = new Slot { monitor = go.GetComponent<MonitorCamera>(), camera = go.GetComponent<Camera>() };
                    if (slot.monitor == null || slot.camera == null)
                        throw new InvalidOperationException("Original MonitorCamera component identity differs");
                    go.SetActive(false);
                    slot.camera.enabled = true;
                    slot.cached = new CacheCamera(slot.camera);
                    var cameraData = go.GetComponent<UniversalAdditionalCameraData>() ?? go.AddComponent<UniversalAdditionalCameraData>();
                    cameraData.renderType = CameraRenderType.Base;
                    cameraData.renderPostProcessing = false;
                    _slots.Add(slot);
                }
            }
            foreach (var slot in _slots)
            {
                slot.camera.targetTexture = null;
                slot.monitor.Initialize(width, height);
            }
            if (_slots.Count > 0) SelectSlot(0);
        }

        private static bool FindKeys<T>(ILiveTimelineKeyDataList keys, LiveTimelineControl timeline, float frame,
            out T current, out T next) where T : LiveTimelineKey
        {
            current = null;
            next = null;
            if (keys == null || keys.Count == 0 || keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable) ||
                !keys.EnablePlayModeTimeline(timeline.PlayMode) || frame < keys.At(0).frame) return false;
            LiveTimelineControl.FindTimelineKey(out var a, out var b, keys, frame);
            current = a as T;
            next = b as T;
            return current != null;
        }

        private static float GetRatio(LiveTimelineKey current, LiveTimelineKeyWithInterpolate next, float frame, LiveTimelineControl timeline)
        {
            if (next == null || !next.IsInterpolateKey()) return 0f;
            // Runtime seconds stay independent of the integer selection frame;
            // legacy direct sampling retains an explicit frame/60 bridge.
            return LiveTimelineControl.CalculateInterpolationValueAtTime(current, next, timeline.CameraSampleTime(frame));
        }

        private static Vector3 GetPosition(LiveTimelineKeyCameraPositionData key, LiveTimelineControl timeline,
            LiveTimelineControl.FindTimelineConfig config) => key.GetValue(timeline);

        private Vector3 EvaluateLookAt(LiveTimelineControl timeline, Vector3 position,
            LiveTimelineKeyMonitorCameraLookAtData current, LiveTimelineKeyMonitorCameraLookAtData next, float frame, out float localScale)
        {
            bool interpolate = next != null && next.IsInterpolateKey();
            float ratio = GetRatio(current, next, frame, timeline);
            Vector3 result = EvaluateLookAtValue(timeline, position, current, next, ratio);
            localScale = current.GetHeightRate(timeline);
            if (interpolate)
            {
                float endScale = next.GetHeightRate(timeline);
                float difference = LiveTimelineControl.MonitorFloat32(endScale - localScale);
                float product = LiveTimelineControl.MonitorFloat32(difference * ratio);
                localScale = LiveTimelineControl.MonitorFloat32(product + localScale);
            }
            return timeline.ApplyCameraLookAtDelay(current, result, _captureCamera != null ? _captureCamera.transform : null, frame, position);
        }

        private Vector3 EvaluateLookAtValue(LiveTimelineControl timeline, Vector3 position,
            LiveTimelineKeyMonitorCameraLookAtData current, LiveTimelineKeyMonitorCameraLookAtData next, float ratio)
        {
            Vector3 start = current.GetValue(timeline, position);
            if (next == null || !next.IsInterpolateKey()) return start;
            Vector3 end = next.GetValue(timeline, position);
            int count = next.GetBezierPointCount();
            if (count == 0) return BezierUtil.LerpUnclamped(start, end, ratio);
            if (next.necessaryToUseNewBezierCalcMethod)
            {
                _lookAtBezier.Set(start, end, count);
                _lookAtBezier.UpdatePoints(next, timeline, Vector3.zero);
                _lookAtBezier.Calc(count, ratio, out Vector3 result);
                return result;
            }
            Vector3 cp1 = next.GetBezierPoint(0, timeline, Vector3.zero);
            Vector3 cp2 = next.GetBezierPoint(1, timeline, Vector3.zero);
            Vector3 cp3 = next.GetBezierPoint(2, timeline, Vector3.zero);
            Vector3 cp4 = next.GetBezierPoint(3, timeline, Vector3.zero);
            Vector3 target;
            switch (next.GetBezierPointCount())
            {
                case 1: BezierUtil.Calc(ref start, ref end, ref cp1, ratio, out target); return target;
                case 2: BezierUtil.Calc(ref start, ref end, ref cp1, ref cp2, ratio, out target); return target;
                case 3: BezierUtil.Calc(ref start, ref end, ref cp1, ref cp2, ref cp3, ratio, out target); return target;
                case 4: BezierUtil.Calc(ref start, ref end, ref cp1, ref cp2, ref cp3, ref cp4, ratio, out target); return target;
                default: return BezierUtil.LerpUnclamped(start, end, ratio);
            }
        }

    }
}
