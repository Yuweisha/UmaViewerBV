using System.Linq;
using UnityEngine;

namespace Gallop
{
    public class TrailCustomDataSurpporter : MonoBehaviour
    {
        private static readonly int Custom1B = Shader.PropertyToID("_TrailCustom1_b");
        private static readonly int Custom1A = Shader.PropertyToID("_TrailCustom1_a");
        private static readonly int Custom2R = Shader.PropertyToID("_TrailCustom2_r");
        private static readonly int Custom2G = Shader.PropertyToID("_TrailCustom2_g");
        private static readonly int Custom2B = Shader.PropertyToID("_TrailCustom2_b");
        private static readonly int Custom2A = Shader.PropertyToID("_TrailCustom2_a");

        [SerializeField] private float _trailCustom1_b;
        [SerializeField] private float _trailCustom1_a;
        [SerializeField] private float _trailCustom2_r;
        [SerializeField] private float _trailCustom2_g;
        [SerializeField] private float _trailCustom2_b;
        [SerializeField] private AnimationCurve _trailCustom2_a;
        private ParticleSystem particle;
        private Material[] materials;
        private Material[] ownedMaterials;
        private bool initialized;

        public float CurrentTime { get; private set; }
        public bool IsAutoUpdate { get; set; } = true;

        public void Initialize(bool isAutoUpdate = true)
        {
            Initialize(isAutoUpdate, null);
        }

        internal void InitializeForTimeline(Material[] timelineMaterials)
        {
            Initialize(false, timelineMaterials);
        }

        private void Initialize(bool isAutoUpdate, Material[] timelineMaterials)
        {
            if (initialized)
                return;
            particle = GetComponent<ParticleSystem>();
            var renderer = GetComponent<ParticleSystemRenderer>();
            if (!particle || !renderer)
                return;
            IsAutoUpdate = isAutoUpdate;
            var source = timelineMaterials ?? (ownedMaterials = renderer.materials);
            materials = source.Where(material => material && material.shader &&
                material.shader.name.Contains("DistortionPaletteCDTrail")).Distinct().ToArray();
            initialized = true;
        }

        public void AddTime(float deltaTime)
        {
            if (!initialized || deltaTime <= 0f)
                return;
            var main = particle.main;
            CurrentTime = Mathf.Repeat(CurrentTime + deltaTime * main.simulationSpeed, main.duration);
            float offset = _trailCustom2_a.Evaluate(CurrentTime / main.duration);
            foreach (var material in materials)
            {
                material.SetFloat(Custom1B, _trailCustom1_b);
                material.SetFloat(Custom1A, _trailCustom1_a);
                material.SetFloat(Custom2R, _trailCustom2_r);
                material.SetFloat(Custom2G, _trailCustom2_g);
                material.SetFloat(Custom2B, _trailCustom2_b);
                material.SetFloat(Custom2A, offset);
            }
        }

        internal void ResetTime()
        {
            CurrentTime = 0f;
        }

        private void Start()
        {
            Initialize();
        }

        private void Update()
        {
            if (initialized && IsAutoUpdate && !particle.isPaused)
                AddTime(Time.deltaTime);
        }

        private void OnDisable()
        {
            ResetTime();
        }

        private void OnDestroy()
        {
            if (ownedMaterials != null)
                foreach (var material in ownedMaterials)
                    if (material)
                        Destroy(material);
        }
    }
}
