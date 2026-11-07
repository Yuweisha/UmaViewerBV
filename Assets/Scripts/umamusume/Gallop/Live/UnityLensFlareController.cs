using UnityEngine;

namespace Gallop.Live
{
    // Serialized angle owner; other responsibilities stay in the existing adapter.
    public sealed class UnityLensFlareController : MonoBehaviour
    {
        [SerializeField] float _enableAngleDegree;
        float _enableAngleCos;
        public Color Color { get; set; } = Color.white;
        public bool IsEnabledShaderColor { get; set; } = true;
        public bool IsAutoBrightness { get; set; } = true;
        public bool IsUseLensFlare { get; private set; } = true;

        // Original UseLensFlare @ 677290 stores the per-controller gate.
        public void UseLensFlare(bool isUse) => IsUseLensFlare = isUse;

        internal void CalcEnableAngleCos()
            => _enableAngleCos = Mathf.Cos(Mathf.Clamp01(_enableAngleDegree / 180f) * Mathf.PI);

        internal float ApplyEnableAngle(float dot, float brightness)
            => _enableAngleDegree > 0f && brightness > 0f && _enableAngleCos > dot ? 0f : brightness;
    }
}
