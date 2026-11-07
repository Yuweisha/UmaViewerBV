using System.Collections.Generic;
using Gallop.Live.Cutt;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gallop.Live
{
    // Source prefab identity and lifecycle: 2.32.0 4feb370..4fec530.
    public sealed class ParticleController : MonoBehaviour
    {
        public enum Status { Stop, Playing, Pause }
        sealed class ParticleData
        {
            public ParticleSystem ParticleSystem;
            public float StartEmissionRate;
            public Renderer Renderer;
        }
        [SerializeField] float _playSpeed = 1f;
        ParticleData[] _particleDataArray;
        readonly List<Material> _instanceMaterialList = new List<Material>();
        readonly Dictionary<int, ParticleData> _particleDictionary = new Dictionary<int, ParticleData>();
        UnityEngine.MaterialPropertyBlock _materialPropertyBlock;
        int _nameHash;
        float _timer;
        bool _isPause, _isInitialized;
        public bool PauseOnStart { get; set; } = true;
        public Status playStatus { get; private set; }

        public void Initialize()
        {
            var systems = GetComponentsInChildren<ParticleSystem>();
            if (systems.Length == 0) return;
            _particleDataArray = new ParticleData[systems.Length];
            for (int i = 0; i < systems.Length; i++)
            {
                var data = new ParticleData { ParticleSystem = systems[i], Renderer = systems[i].GetComponent<Renderer>() };
                if (data.Renderer != null && data.Renderer.sharedMaterial != null)
                {
                    var material = Instantiate(data.Renderer.sharedMaterial);
                    data.Renderer.material = material;
                    _instanceMaterialList.Add(material);
                }
                _particleDataArray[i] = data;
                _particleDictionary.Add(FNVHash.Generate(systems[i].name), data);
            }
            PlayEmit();
            if (PauseOnStart) PauseEmit();
            _materialPropertyBlock = new UnityEngine.MaterialPropertyBlock();
            _nameHash = FNVHash.Generate(gameObject.name.Replace("(Clone)", ""));
            _timer = 0f;
            _isInitialized = true;
        }

        void PlayEmit()
        {
            if (_particleDataArray == null || playStatus != Status.Stop) return;
            foreach (var data in _particleDataArray)
            {
                var particle = data.ParticleSystem;
                if (particle.isPlaying) continue;
                var main = particle.main;
                main.simulationSpeed = _playSpeed;
                data.StartEmissionRate = particle.emission.rateOverTime.constantMax;
                particle.Play();
            }
            playStatus = Status.Playing;
        }

        void PauseEmit()
        {
            if (_particleDataArray == null || playStatus != Status.Playing) return;
            foreach (var data in _particleDataArray)
                if (data.ParticleSystem.isPlaying) SetEmission(data, 0f);
            playStatus = Status.Pause;
        }

        public void ResumeEmit()
        {
            if (_particleDataArray == null || playStatus != Status.Pause) return;
            foreach (var data in _particleDataArray)
                if (data.ParticleSystem.isPlaying) SetEmission(data, data.StartEmissionRate);
            playStatus = Status.Playing;
        }

        public void StopEmit()
        {
            if (_particleDataArray == null || playStatus == Status.Stop) return;
            foreach (var data in _particleDataArray)
                if (data.ParticleSystem.isPlaying) data.ParticleSystem.Stop();
            playStatus = Status.Stop;
        }

        public void Pause()
        {
            if (_particleDataArray == null || playStatus == Status.Stop || _isPause) return;
            foreach (var data in _particleDataArray) data.ParticleSystem.Pause();
            _isPause = true;
        }

        public void Resume()
        {
            if (_particleDataArray == null || playStatus == Status.Stop || !_isPause) return;
            foreach (var data in _particleDataArray) data.ParticleSystem.Play();
            _isPause = false;
        }

        static void SetEmission(ParticleData data, float value)
        {
            var emission = data.ParticleSystem.emission;
            var rate = emission.rateOverTime;
            rate.constant = value;
            emission.rateOverTime = rate;
        }

        internal void UpdateFromTimeline(int nameHash, float emissionRate)
        {
            if (nameHash == _nameHash)
            {
                if (_particleDataArray != null)
                    foreach (var data in _particleDataArray) SetEmission(data, emissionRate);
            }
            else if (_particleDictionary.TryGetValue(nameHash, out var data)) SetEmission(data, emissionRate);
        }

        internal void UpdateGroupFromTimeline(int nameHash, Vector2 darkLight)
        {
            if (nameHash != _nameHash || _particleDataArray == null) return;
            _materialPropertyBlock.SetFloat("_FlickerLightRate", darkLight.y);
            _materialPropertyBlock.SetFloat("_FlickerDarkRate", darkLight.x);
        }

        public void AlterUpdate(float deltaTime)
        {
            if (!_isInitialized || _particleDataArray == null || playStatus == Status.Stop) return;
            _timer += _isPause ? 0f : deltaTime;
            _materialPropertyBlock.SetFloat("_Timer", _timer);
            foreach (var data in _particleDataArray) data.Renderer.SetPropertyBlock(_materialPropertyBlock);
        }

        void OnDestroy()
        {
            foreach (var material in _instanceMaterialList) CoreUtils.Destroy(material);
            _instanceMaterialList.Clear();
        }
    }
}
