using UnityEngine;

namespace Gallop.Live
{
    // Scene 4 branch of GraphicSettings.GetVirtualResolution3D (0x4af1c80),
    // Gallop.Screen (0x4b57200/0x4b56d30), and Monitor texture sizing.
    public static class LiveMonitorResolution
    {
        public static Vector2Int ScreenReference(Vector2Int window, bool landscapeUI, bool vertical)
        {
            if (landscapeUI) return vertical ? new Vector2Int(1440, 1920) : new Vector2Int(1920, 1080);
            return window.x > window.y ? new Vector2Int(1920, 1080) : new Vector2Int(1080, 1920);
        }

        public static Vector2Int Calculate(Vector2Int resolution, Vector2Int screenReference,
            int graphicsQuality, int graphics3DQuality, float flexibleScale, float resolutionScale,
            bool allowWideInPortrait, bool forcedWide)
        {
            int width = resolution.x, height = resolution.y;
            bool portrait = width < height;
            bool wide = allowWideInPortrait || forcedWide;
            float ratio = (float)screenReference.x / width;
            if ((!portrait && (float)height / width <= 0.5625f) ||
                (portrait && (float)width / height > 0.5625f && !wide))
                ratio = (float)screenReference.y / height;
            if (graphics3DQuality == 3)
            {
                float w = width * ratio, h = height * ratio;
                float rate = 2160f / Mathf.Min(w, h);
                width = Mathf.RoundToInt(rate * w);
                height = Mathf.RoundToInt(rate * h);
            }
            else if (graphics3DQuality == 1 || graphics3DQuality == 2 || graphicsQuality <= 2)
            {
                width = Mathf.RoundToInt(width * ratio * flexibleScale);
                height = Mathf.RoundToInt(height * ratio * flexibleScale);
            }
            width = Mathf.RoundToInt(width * resolutionScale);
            height = Mathf.RoundToInt(height * resolutionScale);
            if (forcedWide || (wide && portrait))
            {
                int side = Mathf.RoundToInt(width * 0.0625f);
                width = side * 16;
                height = side * 9;
            }
            return new Vector2Int(width, height);
        }

        public static float ClampWidthRate(float value) => Mathf.Clamp(value, 0.25f, 1f);

        public static Vector2Int TextureSize(Vector2Int virtualResolution, float widthRate, bool normalQuality)
        {
            int width = (int)(virtualResolution.x * widthRate);
            int height = unchecked(width * 720) / 1280;
            if (!normalQuality)
            {
                width = Mathf.RoundToInt(width * 0.6f);
                height = Mathf.RoundToInt(height * 0.6f);
            }
            return new Vector2Int(width, height);
        }

        public static Vector2Int CurrentVirtualResolution()
        {
            // Fixed offline Live4K policy supplies the original mode3 selector.
            // A larger capture descriptor alone does not install this owner.
            var screen = ScreenReference(new Vector2Int(Screen.width, Screen.height), false, false);
            return Calculate(screen, screen, 4, (int)GraphicSettings.Current3DQuality, 1f, 1f, true, false);
        }
    }
}
