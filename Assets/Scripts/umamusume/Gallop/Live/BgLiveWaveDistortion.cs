using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Gallop
{
    // Stage's existing BgLiveWave collection must also see this source subclass.
    public sealed class BgLiveWaveDistortion : BgLiveWave
    {
        [Serializable]
        public sealed class TextureScroll
        {
            [SerializeField] Vector2 _tiling = Vector2.one;
            [SerializeField] Vector2 _offset;
            [SerializeField] Vector2 _scrollSpeed;
            [SerializeField] float _scrollInterval = 1f;

            [MethodImpl(MethodImplOptions.NoInlining)]
            static float F(float value) => BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(value));

            internal void Update(Material material, string texture, float time)
            {
                var offset = _offset;
                if (_scrollInterval > 1e-6f)
                {
                    float step = (int)F(F(time * 60f) / _scrollInterval);
                    offset.x = F(_offset.x + F(step * _scrollSpeed.x));
                    offset.y = F(_offset.y + F(step * _scrollSpeed.y));
                }
                // Native fmod retains the sign of negative scroll offsets.
                offset.x %= 1f;
                offset.y %= 1f;
                material.SetTextureScale(texture, _tiling);
                material.SetTextureOffset(texture, offset);
            }
        }

        [SerializeField] TextureScroll _mainTextureScroll = new TextureScroll();
        [SerializeField] TextureScroll _subTextureScroll = new TextureScroll();
        [SerializeField] TextureScroll _distortionTextureScroll = new TextureScroll();

        public override void AlterUpdate(float liveCurrentTime)
        {
            if (!IsInitialized) return;
            base.AlterUpdate(liveCurrentTime);
            _mainTextureScroll.Update(WaveMaterial, "_MainTex", liveCurrentTime);
            _subTextureScroll.Update(WaveMaterial, "_SubTexture", liveCurrentTime);
            _distortionTextureScroll.Update(WaveMaterial, "_DistortionTex", liveCurrentTime);
        }
    }
}
