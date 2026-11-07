using UnityEngine;

namespace Gallop.Live
{
    // Native UVScrollLightUpdateInfo layout (catalog offsets):
    // 0x00 TimelineNameHash, 0x04 progressTime, 0x08 mulColor0, 0x18 mulColor1,
    // 0x28 colorPower, 0x2c scrollOffsetX/Y, 0x34 scrollSpeedX/Y, 0x40 texture, 0x48 isEnabledTexture.
    public struct NativeUVScrollLightUpdateInfo
    {
        public int TimelineNameHash;
        public float progressTime;
        public Color mulColor0;
        public Color mulColor1;
        public float colorPower;
        public float scrollOffsetX;
        public float scrollOffsetY;
        public float scrollSpeedX;
        public float scrollSpeedY;
        public Texture2D texture;
        public bool isEnabledTexture;
    }

    public sealed class UVScrollLightControllerState
    {
        public Color MulColor0;
        public Color MulColor1;
        public float ColorPower;
        public float ScrollOffsetX;
        public float ScrollOffsetY;
        public float ScrollSpeedX;
        public float ScrollSpeedY;
        public float ElapsedTime;
        public Texture2D Texture;
        public bool IsEnabledTexture;

        // UpdateInfo 0x4f10be0 copies the native UVScrollLightUpdateInfo into the controller.
        public void UpdateInfo(in NativeUVScrollLightUpdateInfo info)
        {
            MulColor0 = info.mulColor0;
            MulColor1 = info.mulColor1;
            ColorPower = info.colorPower;
            ScrollOffsetX = info.scrollOffsetX;
            ScrollOffsetY = info.scrollOffsetY;
            ScrollSpeedX = info.scrollSpeedX;
            ScrollSpeedY = info.scrollSpeedY;
            ElapsedTime = info.progressTime;
            Texture = info.texture;
            IsEnabledTexture = info.isEnabledTexture;
        }
    }
}
