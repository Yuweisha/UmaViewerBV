namespace Gallop.Live
{
    // StageController.get_hasMainCamera 0x4ebffe0:
    // 0x5e5c990 on this+0x360 returns true when the camera is alive.
    public static class MainCameraQuery
    {
        public static bool HasMainCamera(object camera, bool alive)
        {
            return camera != null && alive;
        }
    }
}
