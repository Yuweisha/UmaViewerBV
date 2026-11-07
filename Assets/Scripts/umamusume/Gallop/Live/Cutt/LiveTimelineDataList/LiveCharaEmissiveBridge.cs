using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    // Collects the chara prop renderer materials and applies the audited
    // emissive interface calls to each of them. Material sources are supplied
    // explicitly; the production container wiring remains open.
    public sealed class LiveCharaEmissiveBridge
    {
        private readonly List<LiveCharaPropRendererAccessor> _accessors = new List<LiveCharaPropRendererAccessor>();

        public int Count => _accessors.Count;

        public void AddMaterial(Material material)
        {
            if (material != null) _accessors.Add(new LiveCharaPropRendererAccessor(material));
        }

        public void AddMaterials(IEnumerable<Material> materials)
        {
            if (materials == null) return;
            foreach (var material in materials) AddMaterial(material);
        }

        public void SetColor(Color color)
        {
            for (int i = 0; i < _accessors.Count; i++) _accessors[i].SetEmissiveColor(color);
        }

        public void SetIntensity(float value)
        {
            for (int i = 0; i < _accessors.Count; i++) _accessors[i].SetEmissiveIntensity(value);
        }

        public void SetSoftEdge(float rimPower, float rimIntensity, bool isCenter)
        {
            for (int i = 0; i < _accessors.Count; i++) _accessors[i].SetEmissiveRim(rimPower, rimIntensity, isCenter);
        }
    }
}
