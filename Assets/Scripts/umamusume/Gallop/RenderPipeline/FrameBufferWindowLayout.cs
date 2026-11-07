using UnityEngine;

namespace Gallop.RenderPipeline
{
    // LandscapeUIManager/Screen boundary. UI orientation and game orientation
    // are distinct native inputs; a render texture's aspect cannot replace them.
    public static class FrameBufferWindowLayout
    {
        public static bool IsSplitWindow(bool landscapeUI, bool verticalGame) => landscapeUI && verticalGame;
        public static int WindowsWidthMax(bool landscapeUI, bool verticalGame) => landscapeUI || !verticalGame ? 1920 : 1080;
        public static float WindowScaleRate(int windowWidth, bool landscapeUI, bool verticalGame) =>
            windowWidth / (float)WindowsWidthMax(landscapeUI, verticalGame);
        // Exact float constants in native FrameBufferRenderPass ctor 0x5212ed0.
        public static Rect Left(float scale) => Scale(new Rect(0, 0, 148, 1080), scale);
        public static Rect Center(float scale) => Scale(new Rect(148, 0, 810, 1080), scale);
        public static Rect Right(float scale) => Scale(new Rect(958, 0, 962, 1080), scale);
        public static Rect Full(float scale, bool landscapeUI) =>
            Scale(new Rect(0, 0, landscapeUI ? 1920 : 1080, 1080), scale);
        static Rect Scale(Rect r, float s) => new Rect(r.x * s, r.y * s, r.width * s, r.height * s);
    }
}
