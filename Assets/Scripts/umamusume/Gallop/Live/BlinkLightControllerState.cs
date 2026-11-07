using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live
{
    public interface IBlinkLightTarget
    {
        bool HasColor0 { get; }
        bool HasColor1 { get; }
    }

    public sealed class BlinkLightControllerState
    {
        public bool IsInitialized;
        public bool UpdateFlag;   // native +0x26
        public byte BlendMode;    // native +0x25
        public int LightBlendMode; // +0x28
        public float Pattern;      // +0x2c
        public float ColorType;    // +0x30
        public int Power;          // +0x20
        public Color[] Color0;     // copied from info.color0Array
        public Color[] Color1;
        public bool[] ReverseHue;
        public readonly List<object> Renderers = new List<object>();
        public readonly List<IBlinkLightTarget> Targets = new List<IBlinkLightTarget>();
        public readonly List<string> Calls = new List<string>();

        public void UpdateInfo(in Cutt.BlinkLightUpdateInfo info)
        {
            if (!IsInitialized) return;
            LightBlendMode = (int)info.LightBlendMode;
            Pattern = (float)info.pattern;
            ColorType = (float)info.colorType;
            Color0 = Copy(info.color0Array);
            Color1 = Copy(info.color1Array);
            ReverseHue = Copy(info.isReverseHueArray);
            Calls.Add("copy:" + (Color0?.Length ?? 0) + "/" + (Color1?.Length ?? 0) + "/" + (ReverseHue?.Length ?? 0));
            for (int i = 0; i < Renderers.Count; i++)
                Calls.Add("sub:" + i + ":" + Power);
            byte mode = info.UseWashLightBlendMode ? (byte)1 : (byte)0;
            if (!UpdateFlag || BlendMode != mode)
            {
                BlendMode = mode;
                UpdateFlag = true;
                for (int i = 0; i < Targets.Count; i++)
                {
                    if (Targets[i].HasColor0) Calls.Add("target:" + i + ":0:" + mode);
                    if (Targets[i].HasColor1) Calls.Add("target:" + i + ":1:" + mode);
                }
            }
        }

        static Color[] Copy(Color[] src)
        {
            if (src == null) return null;
            var dst = new Color[src.Length];
            src.CopyTo(dst, 0);
            return dst;
        }

        static bool[] Copy(bool[] src)
        {
            if (src == null) return null;
            var dst = new bool[src.Length];
            src.CopyTo(dst, 0);
            return dst;
        }
    }
}
