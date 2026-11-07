using UnityEngine;

namespace Gallop
{
    // TextCommon.SetupDefaultColor 0x5652c40 / set_DefaultColor 0x5654820.
    public sealed class TextCommonState
    {
        public Color DefaultColor;
        public Color MulColor;
        public bool IsSetupedDefaultColor;

        public void SetupDefaultColor(Color current)
        {
            if (IsSetupedDefaultColor) return;
            DefaultColor = current;
            IsSetupedDefaultColor = true;
        }


        public void SetMulColor(Color color, System.Collections.Generic.IEnumerable<TextCommonState> subTexts,
            System.Action<TextCommonState, Color> applyToSubText, System.Action update)
        {
            MulColor = color;
            if (subTexts != null)
                foreach (var sub in subTexts)
                    applyToSubText?.Invoke(sub, color);
            update?.Invoke();
        }

        public static string GetFullText(string baseText, string subText, bool subAlive)
        {
            return subAlive && subText != null ? baseText + subText : baseText;
        }
        public Color GetDefaultColor() => DefaultColor;
        public Color GetMulColor() => MulColor;

        public void SetDefaultColor(Color color)
        {
            DefaultColor = color;
        }
    }
}
