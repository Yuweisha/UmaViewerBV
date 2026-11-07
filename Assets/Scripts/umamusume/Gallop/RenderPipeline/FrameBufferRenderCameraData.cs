using UnityEngine;

namespace Gallop.RenderPipeline
{
    // Original derived owner, not offsets reinterpreted on base CameraData.
    public sealed class FrameBufferRenderCameraData : CameraData
    {
        public RenderTexture ScreenTexture;
        public RenderTexture ScreenTextureForRightUI;
        public RenderTexture ScreenTextureForLeftUI;
        public RenderTexture ScreenTextureForGamepadSoftwareCursor;
    }
}
