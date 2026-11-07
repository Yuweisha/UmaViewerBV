using UnityEngine;

namespace Gallop.Live
{
    public struct NativeWashLightUpdateInfo
    {
        public byte value4;
        public int value8;
        public bool flagC;
        public float brightness10;
        public int value14;
    }

    public sealed class WashLightControllerState
    {
        public byte A8;
        public int AC;
        public float X80;
        public int X140;
        public Vector3[] OutColors;

        // WashLightController.Update 0x4f12fb0 + 0x4f12ff0:
        // copy fields, then outColors[i] = staticColors[i] * brightness.
        public void Update(in NativeWashLightUpdateInfo info, Vector3[] staticColors)
        {
            if (!info.flagC)
            {
                A8 = info.value4;
                AC = info.value8;
            }
            X80 = info.brightness10;
            X140 = info.value14;
            OutColors = new Vector3[staticColors.Length];
            for (int i = 0; i < staticColors.Length; i++) OutColors[i] = staticColors[i] * X80;
        }
    }
}
