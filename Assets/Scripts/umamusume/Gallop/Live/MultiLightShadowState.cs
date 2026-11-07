using System;

namespace Gallop.Live
{
    public struct MultiLightShadowParamInfo
    {
        public byte b0;
        public int v4;
        public int vC;
        public int v1C;
        public int v20;
        public byte b24;
        public byte b25;
        public int v28;
        public int v38;
        public int v48;
        public byte b5C;
    }

    public sealed class MultiLightShadowState
    {
        public byte B20;
        public int X24;
        public int X2C;
        public float[] X30;
        public int X40;
        public int X44;
        public byte B48;
        public byte B49;
        public int[] X4C;
        public int[] X5C;
        public int X6C;
        public byte B80;

        public bool Apply(bool flag, in MultiLightShadowParamInfo info, object target, float[] quaternion,
            Action<object> finalCall)
        {
            if (!flag) return false;
            if (target == null) throw new NullReferenceException("multi light target");
            B20 = info.b0;
            X24 = info.v4;
            X2C = info.vC;
            X30 = quaternion;
            X40 = info.v1C;
            X44 = info.v20;
            B48 = info.b24;
            B49 = info.b25;
            X4C = new[] { info.v28, 0, 0, 0 };
            X5C = new[] { info.v38, 0, 0, 0 };
            X6C = info.v48;
            B80 = info.b5C;
            finalCall?.Invoke(target);
            return true;
        }
    }
}
