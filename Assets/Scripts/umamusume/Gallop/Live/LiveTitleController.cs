using Gallop;
using Gallop.Live.Cutt;
using System.Text;
using UnityEngine;

namespace Gallop.Live
{
    // LiveTitleController bridge reconstructed from the builtin prefab field layout
    // and native FadeIn/FadeOut/Disappear delegation.
    [AddComponentMenu("Live/LiveTitleController")]
    public class LiveTitleController : MonoBehaviour, ILiveTitleTarget
    {
        public TextCommon _titleText;
        public TextCommon _authorText;
        public ImageCommon _titleImage;
        public UIGradation _titleGradation;
        public FadeGui _fadeGui;
        public bool IsDisappear { get; private set; }
        private bool _isInitialized;

        public void Initialize(string title, string author)
            => Initialize(title, author, Color.white, Color.white);

        public void Initialize(string title, string author, Color top, Color bottom)
        {
            if (_titleText != null) _titleText.SetText(title);
            if (_authorText != null) _authorText.SetText(author);
            _titleGradation?.SetGradationColor(top, bottom);
            AdjustAuthorTextFontSize();
            _isInitialized = true;
            Disappear();
        }

        // Original 4e95340 uses UTF8 byte count and UTF16 length. It replaces
        // CR after TrimEnd, and the loop leaves the LAST line's width in xmm6.
        // Preserve that behavior rather than substituting a widest-line rule.
        public static int AuthorFontSize(string text, float width, float height, float lineSpacing)
        {
            string[] lines = text.TrimEnd().Replace("\r", string.Empty).Split('\n');
            float units = 0f;
            foreach (string line in lines)
            {
                int wide = (Encoding.UTF8.GetByteCount(line) - line.Length) / 2;
                units = (line.Length - wide) * 0.5f + wide;
            }
            int size = (int)System.Math.Floor(width / units);
            if (lines.Length > 1)
            {
                float rows = (lineSpacing - 0.5f) * 2f * (lines.Length - 1) + lines.Length;
                size = System.Math.Min(size, (int)System.Math.Floor(height / rows));
            }
            return System.Math.Min(size, 32);
        }

        void AdjustAuthorTextFontSize()
        {
            if (_authorText == null || _authorText.text.Length == 0) return;
            Rect rect = _authorText.rectTransform.rect;
            _authorText.fontSize = AuthorFontSize(_authorText.text, rect.width, rect.height, _authorText.lineSpacing);
        }

        public void FadeIn(float duration, float currentTime)
        {
            if (_fadeGui == null) return;
            IsDisappear = false;
            _fadeGui.FadeIn(duration, currentTime);
        }
        public void FadeOut(float duration, float currentTime)
        {
            if (_fadeGui == null) return;
            IsDisappear = false;
            _fadeGui.FadeOut(duration, currentTime);
        }
        public void Disappear()
        {
            if (_fadeGui == null) return;
            IsDisappear = true;
            _fadeGui.Disappear();
        }
        public void AlterUpdate(float deltaTime)
        {
            if (_isInitialized) _fadeGui?.AlterUpdate(deltaTime);
        }
    }
}
