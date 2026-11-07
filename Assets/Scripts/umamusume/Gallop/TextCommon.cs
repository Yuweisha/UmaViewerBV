using UnityEngine;
using UnityEngine.UI;

namespace Gallop
{
    // Reconstructed shell of Gallop.TextCommon for the title UI.
    // Title only needs the Text surface; outline/gradient/icon-tag features of the
    // original 170+ method class are not implemented here.
    [AddComponentMenu("UI/TextCommon")]
    public class TextCommon : Text
    {
        public void SetText(string value)
        {
            text = value;
        }
    }
}
