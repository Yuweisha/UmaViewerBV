using System;
using System.Collections.Generic;
using UnityEngine;
using Gallop.Live.Cutt;

namespace Gallop.Live
{
    /// UV 滚动灯光控制器
    /// 
    ///接收已经展平的 UVScrollLightUpdateInfo；
    ///将颜色、强度、偏移、速度、经过时间和纹理写入材质；
    public class UVScrollLightController
    {
        private readonly Material[] _materialArray;
        private readonly bool _isInstanceMaterial;

        private readonly string _mainTexProperty;
        private readonly string _mulColor0Property;
        private readonly string _mulColor1Property;
        private readonly string _colorPowerProperty;
        private readonly string _scrollOffsetXProperty;
        private readonly string _scrollOffsetYProperty;
        private readonly string _scrollSpeedXProperty;
        private readonly string _scrollSpeedYProperty;
        private readonly string _elapsedTimeProperty;

        private Color _mulColor0;
        private Color _mulColor1;
        private float _colorPower;
        private float _scrollOffsetX;
        private float _scrollOffsetY;
        private float _scrollSpeedX;
        private float _scrollSpeedY;
        private float _elapsedTime;
        private Texture2D _texture;
        private bool _isEnabledTexture;

        public UVScrollLightController()
        {
            _materialArray = Array.Empty<Material>();
            _isInstanceMaterial = false;
        }

        public UVScrollLightController(Renderer[] rendererArray)
        {
            _materialArray = CollectInstanceMaterials(rendererArray);
            _isInstanceMaterial = true;
        }

        public UVScrollLightController(
            Material[] materialArray,
            bool isInstanceMaterial,
            string mainTexProperty,
            string mulColor0Property,
            string mulColor1Property,
            string colorPowerProperty,
            string scrollOffsetXProperty,
            string scrollOffsetYProperty,
            string scrollSpeedXProperty,
            string scrollSpeedYProperty,
            string elapsedTimeProperty)
        {
            _materialArray = materialArray ?? Array.Empty<Material>();
            _isInstanceMaterial = isInstanceMaterial;

            _mainTexProperty = mainTexProperty;
            _mulColor0Property = mulColor0Property;
            _mulColor1Property = mulColor1Property;
            _colorPowerProperty = colorPowerProperty;
            _scrollOffsetXProperty = scrollOffsetXProperty;
            _scrollOffsetYProperty = scrollOffsetYProperty;
            _scrollSpeedXProperty = scrollSpeedXProperty;
            _scrollSpeedYProperty = scrollSpeedYProperty;
            _elapsedTimeProperty = elapsedTimeProperty;
        }

        public void UpdateInfo(ref UVScrollLightUpdateInfo updateInfo)
        {
            _mulColor0 = updateInfo.mulColor0;
            _mulColor1 = updateInfo.mulColor1;
            _colorPower = updateInfo.colorPower;
            _scrollOffsetX = updateInfo.scrollOffsetX;
            _scrollOffsetY = updateInfo.scrollOffsetY;
            _scrollSpeedX = updateInfo.scrollSpeedX;
            _scrollSpeedY = updateInfo.scrollSpeedY;
            _elapsedTime = updateInfo.elapsedTime;
            _texture = updateInfo.texture;
            _isEnabledTexture = updateInfo.isEnabledTexture;
        }

        public void Update()
        {
            {
            if (_materialArray == null || _materialArray.Length == 0)
                return;

            // Complete original Update 0x4f10c50 and RenderUtils consumers keep
            // shader time, speed and initial offset separate. The original GPU
            // program combines them; modifying _MainTex_ST is a different input.
            int color0=ShaderManager.GetPropertyId(ShaderManager.PropertyId._MulColor0);
            int color1=ShaderManager.GetPropertyId(ShaderManager.PropertyId._MulColor1);
            int power=ShaderManager.GetPropertyId(ShaderManager.PropertyId._ColorPower);
            int offsetX=ShaderManager.GetPropertyId(ShaderManager.PropertyId._UVScrollOffsetX);
            int offsetY=ShaderManager.GetPropertyId(ShaderManager.PropertyId._UVScrollOffsetY);
            int speedX=ShaderManager.GetPropertyId(ShaderManager.PropertyId._UVScrollX);
            int speedY=ShaderManager.GetPropertyId(ShaderManager.PropertyId._UVScrollY);
            int time=ShaderManager.GetPropertyId(ShaderManager.PropertyId._AppTime);
            int texture=ShaderManager.GetPropertyId(ShaderManager.PropertyId._MainTex);
            for (int i = 0; i < _materialArray.Length; i++)
            {
                var mat = _materialArray[i];
                if (mat == null)
                    continue;

                if (mat.HasProperty(color0))
                    mat.SetColor(color0, _mulColor0);

                if (mat.HasProperty(color1))
                    mat.SetColor(color1, _mulColor1);

                if (mat.HasProperty(power))
                    mat.SetFloat(power, _colorPower);

                // Original RenderUtils.SetFloat/SetTexture are unconditional;
                // only the two colors and power use TrySet* property gates.
                mat.SetFloat(offsetX, _scrollOffsetX);
                mat.SetFloat(offsetY, _scrollOffsetY);
                mat.SetFloat(speedX, _scrollSpeedX);
                mat.SetFloat(speedY, _scrollSpeedY);
                mat.SetFloat(time, _elapsedTime);
                if (_isEnabledTexture && _texture != null)
                    mat.SetTexture(texture, _texture);
            }
        
            }
        
}

        public void Release()
        {
            if (_isInstanceMaterial && _materialArray != null)
            {
                for (int i = 0; i < _materialArray.Length; i++)
                {
                    Material mat = _materialArray[i];
                    if (mat == null)
                        continue;

#if UNITY_EDITOR
                    if (Application.isPlaying)
                        UnityEngine.Object.Destroy(mat);
                    else
                        UnityEngine.Object.DestroyImmediate(mat);
#else
                    UnityEngine.Object.Destroy(mat);
#endif
                }
            }
        }

        public static Material[] CollectInstanceMaterials(Renderer[] rendererArray)
        {
            if (rendererArray == null || rendererArray.Length == 0)
                return Array.Empty<Material>();

            List<Material> list = new List<Material>(16);

            for (int i = 0; i < rendererArray.Length; i++)
            {
                Renderer r = rendererArray[i];
                if (r == null)
                    continue;

                Material[] mats;
                try
                {
                    mats = r.materials;
                }
                catch
                {
                    continue;
                }

                if (mats == null || mats.Length == 0)
                    continue;

                for (int j = 0; j < mats.Length; j++)
                {
                    if (mats[j] != null)
                        list.Add(mats[j]);
                }
            }

            return list.ToArray();
        }
    }
}
