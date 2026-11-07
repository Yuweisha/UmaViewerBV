using System;

namespace Gallop.Live
{
    // StageController.UpdateAnalyzer 0x4eb6f00: writes xmm1 argument to element+0x74.
    public static class AnalyzerConsumer
    {
        public static bool Apply(float value, AnalyzerElement[] elements)
        {
            if (elements == null) return false;
            for (int i = 0; i < elements.Length; i++)
            {
                if (elements[i] == null) throw new NullReferenceException("analyzer element");
                elements[i].value = value;
            }
            return true;
        }

        public sealed class AnalyzerElement
        {
            public float value;
        }
    }
}
