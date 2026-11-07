using UnityEngine;
using Gallop;

namespace Gallop.Live.Cutt
{
    // Managed counterpart of CharaPropRendererAccessor's emissive application
    // (original property ids 28/29/30/31/32, see native-formation-consumer-semantics-01).
    public sealed class LiveCharaPropRendererAccessor
    {
        private readonly Material _material;

        public LiveCharaPropRendererAccessor(Material material)
        {
            _material = material;
        }

        public Material Material => _material;

        public void SetEmissiveColor(Color color)
        {
            if (_material == null) return;
            _material.SetColor(ShaderManager.GetPropertyId(ShaderManager.PropertyId._EmissiveColor), color);
        }

        public void SetEmissiveIntensity(float value)
        {
            if (_material == null) return;
            int id = ShaderManager.GetPropertyId(ShaderManager.PropertyId._EmissiveIntensity);
            if (_material.HasProperty(id)) _material.SetFloat(id, value);
        }

        public void SetEmissiveRim(float rimPower, float rimIntensity, bool isCenter)
        {
            if (_material == null) return;
            int power = ShaderManager.GetPropertyId(ShaderManager.PropertyId._EmissiveRimPower);
            if (_material.HasProperty(power)) _material.SetFloat(power, rimPower);
            int intensity = ShaderManager.GetPropertyId(ShaderManager.PropertyId._EmissiveRimIntensity);
            if (_material.HasProperty(intensity)) _material.SetFloat(intensity, rimIntensity);
            int center = ShaderManager.GetPropertyId(ShaderManager.PropertyId._IsEmissiveRimCenter);
            if (_material.HasProperty(center)) _material.SetFloat(center, isCenter ? 1f : 0f);
        }
    }
}
