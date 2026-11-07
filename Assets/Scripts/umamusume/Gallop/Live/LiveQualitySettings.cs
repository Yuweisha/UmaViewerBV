namespace Gallop.Live
{
    // Original static constructor 0x5887190 and quality setter 0x4af4b70.
    public static class LiveQualitySettings
    {
        public static bool IsMonitor = true;
        public static bool IsRealShadow = true;
        public static bool IsProjector = true;
        public static bool IsReflection = true;
        public static bool IsMonitorCamera = true;
        public static bool IsLensflare = true;
        public static bool IsMirrorScan = true;
        public static bool IsCameraShake = true;
        public static bool IsMob2D = true;

        public static void SetGameQualityLiveSetting(bool isNormal)
        {
            IsMonitor = IsRealShadow = IsProjector = IsReflection = IsMonitorCamera =
                IsLensflare = IsMirrorScan = IsCameraShake = IsMob2D = isNormal;
        }
    }
}
