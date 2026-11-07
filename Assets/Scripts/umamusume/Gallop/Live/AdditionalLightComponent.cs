using System.Collections.Generic;
using Gallop.Live.Cutt;
using UnityEngine;

namespace Gallop.Live
{
    // Native 0x4f02a40..0x4f02ee0. These are real Unity lights, independent of beam meshes/flares.
    public sealed class AdditionalLightComponent
    {
        private readonly List<Light> _addLightList = new List<Light>();
        private Transform _root;
        private int _lightMask;
        private Light _viewerSun;
        private bool _ownsSun;
        public void Initialize(Transform root, int mask) { _root=root; _lightMask=mask; }
        public void UpdateAdditionalLightCount(int count)
        {
            if (count == _addLightList.Count) return;
            QualitySettings.pixelLightCount = count; // Native GraphicSettings.SetPixelLightCount 0x4af52a0.
            while (_addLightList.Count < count)
            {
                var go = new GameObject("AdditionalLight");
                go.transform.parent = _root;
                var light = go.AddComponent<Light>();
                light.enabled = false;
                light.cullingMask = _lightMask;
                _addLightList.Add(light);
            }
            for (int i=_addLightList.Count-1; i>=count; --i)
            {
                Object.Destroy(_addLightList[i].gameObject);
                _addLightList.RemoveAt(i);
            }
        }
        public void UpdateAdditionalLight(AdditionalLightUpdateInfo info)
        {
            if (info.Index < 0 || info.Index >= _addLightList.Count) return;
            var light = _addLightList[info.Index];
            light.enabled=info.IsEnable;
            if (!info.IsEnable) return;
            light.transform.SetPositionAndRotation(info.Position,Quaternion.Euler(info.Rotate));
            light.type=info.Type;
            light.range=info.Range;
            light.spotAngle=info.SpotAngle;
            light.bounceIntensity=info.IndirectMultiplier;
            light.shadows=info.ShadowType;
            light.shadowStrength=info.Strength;
            light.shadowBias=info.Bias;
            light.shadowNormalBias=info.NormalBias;
            light.shadowNearPlane=info.NearPlane;
            // Host bridge: Version2's preview sun survives the Live scene transfer.
            // Select the authored directional light for URP's main shadow pass, rather
            // than letting that unrelated preview light mask the game's only shadow light.
            if (light.type == LightType.Directional)
            {
                if (!_ownsSun) { _viewerSun=RenderSettings.sun; _ownsSun=true; }
                RenderSettings.sun=light;
            }
        }
        public void RestoreViewerSun()
        {
            if (_ownsSun) RenderSettings.sun=_viewerSun;
            _ownsSun=false;
        }
    }
    public partial class StageController
    {
        private AdditionalLightComponent _nativeAdditionalLight;
        private void BindAdditionalLight(LiveTimelineControl control)
        {
            if (_nativeAdditionalLight == null)
            {
                _nativeAdditionalLight=new AdditionalLightComponent();
                int mask=0;
                foreach (int index in new[]{1,2,7,13,14,15,16})
                    mask |= GraphicSettings.GetCullingLayer((GraphicSettings.LayerIndex)index);
                _nativeAdditionalLight.Initialize(transform,mask);
            }
            control.OnUpdateAdditionalLightCount += _nativeAdditionalLight.UpdateAdditionalLightCount;
            control.OnUpdateAdditionalLight += _nativeAdditionalLight.UpdateAdditionalLight;
        }
        private void UnbindAdditionalLight(LiveTimelineControl control)
        {
            if (_nativeAdditionalLight == null) return;
            control.OnUpdateAdditionalLightCount -= _nativeAdditionalLight.UpdateAdditionalLightCount;
            control.OnUpdateAdditionalLight -= _nativeAdditionalLight.UpdateAdditionalLight;
            _nativeAdditionalLight.RestoreViewerSun();
        }
    }
}
