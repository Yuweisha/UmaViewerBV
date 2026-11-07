using System;
using System.Collections.Generic;
using Gallop.Live.Cutt;
using UnityEngine;

namespace Gallop.Live
{
    public class StageWashLightDriver : MonoBehaviour
    {
        public bool verboseLog = false;
        public bool includeInactive = true;
        public int generateLightPrefixMax = 99;

        private LiveTimelineControl _ctl;
        private StageController _stage;

        private readonly Dictionary<int, WashLightController> _controllerMap =
            new Dictionary<int, WashLightController>(256);

        private readonly List<WashLightController> _controllers =
            new List<WashLightController>(256);

        private readonly Plane[] _frustumPlanes = new Plane[6];

        private Camera _cachedCamera;

        private void OnEnable()
        {
            BindIfPossible();
        }

        private void OnDisable()
        {
            Unbind();
        }

        private void LateUpdate()
        {
            // The Live owner dispatches WashLight before FakeLight/LensFlare,
            // matching StageController.AlterLateUpdate @ 4ff8a3f/4ff8b05.
            if (Director.instance != null && Director.instance._isLiveSetup) return;
            AlterLateUpdateForLive(ResolveCamera());
        }

        internal void AlterLateUpdateForLive(Camera camera)
        {
            if (_ctl == null || _stage == null)
                BindIfPossible();

            if (_controllers.Count <= 0)
                return;

            if (camera == null)
                return;

            GeometryUtility.CalculateFrustumPlanes(camera, _frustumPlanes);

            Vector3 cameraPosition = camera.transform.position;

            for (int i = 0; i < _controllers.Count; i++)
            {
                WashLightController controller = _controllers[i];
                if (controller == null)
                    continue;

                if (!controller.IsInitialized)
                    controller.Initialize();

                controller.AlterLateUpdate(cameraPosition, _frustumPlanes);
            }
        }

        private void BindIfPossible()
        {
            Director dir = Director.instance;
            if (!dir)
                return;

            LiveTimelineControl ctl = dir._liveTimelineControl;
            StageController stage = dir._stageController;

            if (ctl == null || stage == null)
                return;

            if (_ctl == ctl && _stage == stage)
                return;

            Unbind();

            _ctl = ctl;
            _stage = stage;

            _ctl.OnUpdateWashLight += OnWashLight;

            RebuildCache();

            if (verboseLog)
                Debug.Log("[StageWashLightDriver] bound");
        }

        private void Unbind()
        {
            if (_ctl != null)
                _ctl.OnUpdateWashLight -= OnWashLight;

            _ctl = null;
            _stage = null;
            _cachedCamera = null;

            _controllerMap.Clear();
            _controllers.Clear();
        }

        private void OnWashLight(ref WashLightUpdateInfo updateInfo)
        {
            // Native Stage.UpdateWashLight 0x500cc60 checks this flag before
            // looking up NameHash; an all-settings group normally has hash 0.
            if (updateInfo.IsAllSettings)
            {
                foreach (var target in _controllers)
                {
                    if (target == null) continue;
                    if (!target.IsInitialized) target.Initialize();
                    target.SetUpdateInfo(ref updateInfo);
                }
                return;
            }
            if (updateInfo.NameHash == 0)
                return;

            // Native StageController.UpdateWashLight 0x500cc60 does a single
            // Dictionary.TryGetValue and returns on a miss. It never rebuilds
            // the controller cache from the per-frame update callback.
            if (!_controllerMap.TryGetValue(updateInfo.NameHash, out WashLightController controller) || controller == null)
                return;

            if (!controller.IsInitialized)
                controller.Initialize();

            controller.SetUpdateInfo(ref updateInfo);
        }

        public void RebuildCache()
        {
            _controllerMap.Clear();
            _controllers.Clear();

            if (_stage == null)
                return;

            WashLightController[] controllers = _stage.GetComponentsInChildren<WashLightController>(includeInactive);

            for (int i = 0; i < controllers.Length; i++)
            {
                WashLightController controller = controllers[i];
                if (controller == null)
                    continue;

                if (!controller.IsInitialized)
                    controller.Initialize();

                _controllers.Add(controller);
                RegisterNameVariants(controller.gameObject.name, controller);
            }

            if (verboseLog)
                Debug.Log("[StageWashLightDriver] cached controllers = " + _controllers.Count +
                          ", hash entries = " + _controllerMap.Count);
        }

        private void RegisterNameVariants(string objectName, WashLightController controller)
        {
            if (string.IsNullOrEmpty(objectName) || controller == null)
                return;

            // Native CollectWashLightController 0x4ff9ba0 keys the cache with
            // FNVHash.Generate(controller.name) only. Unity's Instantiate may
            // append "(Clone)"; keep that one alias so stage/timeline names match.
            AddHash(objectName, controller);
            AddHash(objectName.Replace("(Clone)", ""), controller);
        }

        private void AddHash(string name, WashLightController controller)
        {
            if (string.IsNullOrEmpty(name) || controller == null)
                return;

            int hash = FNVHash.Generate(name);

            if (!_controllerMap.ContainsKey(hash))
                _controllerMap.Add(hash, controller);
        }


        private Camera ResolveCamera()
        {
            if (_ctl != null && _ctl.data != null && _ctl.data.worksheetList != null && _ctl.data.worksheetList.Count > 0)
            {
                int cameraIndex = _ctl.data.worksheetList[0].targetCameraIndex;
                CacheCamera cacheCamera = _ctl.GetCamera(cameraIndex);

                if (cacheCamera != null && cacheCamera.camera != null)
                    return cacheCamera.camera;
            }

            if (_cachedCamera != null)
                return _cachedCamera;

            if (Camera.main != null)
            {
                _cachedCamera = Camera.main;
                return _cachedCamera;
            }

            Camera[] cameras = FindObjectsOfType<Camera>(true);
            if (cameras != null && cameras.Length > 0)
            {
                _cachedCamera = cameras[0];
                return _cachedCamera;
            }

            return null;
        }
    }
}
