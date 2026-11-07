using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Gallop.Live
{
    // FadeGui MonoBehaviour wrapper around the native-verified LiveFadeGui state.
    [AddComponentMenu("Live/FadeGui")]
    public class FadeGui : MonoBehaviour
    {
        public float DefaultDuration;
        public bool LetsFadeIn;
        public bool LetsFadeOut;
        public float MinAlpha;
        public float MaxAlpha = 1f;
        public bool DisableWhenFinish;
        public MaskableGraphic[] Widgets;
        public UIGradation[] Gradations;

        public readonly LiveFadeGui State = new LiveFadeGui();

        sealed class WidgetAdapter : IFadeGuiWidget
        {
            readonly MaskableGraphic graphic;
            public WidgetAdapter(MaskableGraphic graphic) { this.graphic = graphic; }
            public Color Color { get => graphic.color; set => graphic.color = value; }
        }

        sealed class GradationAdapter : IFadeGuiGradation
        {
            readonly UIGradation gradation;
            public GradationAdapter(UIGradation gradation) { this.gradation = gradation; }
            public void SetAlpha(float alpha) => gradation.SetAlpha(alpha);
        }

        void Awake()
        {
            SyncState();
            State.IsInitialized = true;
        }

        void SyncState()
        {
            State.DefaultDuration = DefaultDuration;
            State.LetsFadeIn = LetsFadeIn;
            State.LetsFadeOut = LetsFadeOut;
            State.MinAlpha = MinAlpha;
            State.MaxAlpha = MaxAlpha;
            State.DisableWhenFinish = DisableWhenFinish;
            State.Widgets.Clear();
            State.Gradations.Clear();
            if (Widgets != null)
                for (int i = 0; i < Widgets.Length; i++)
                    if (Widgets[i] != null) State.Widgets.Add(new WidgetAdapter(Widgets[i]));
            if (Gradations != null)
                for (int i = 0; i < Gradations.Length; i++)
                    if (Gradations[i] != null) State.Gradations.Add(new GradationAdapter(Gradations[i]));
        }

        public void FadeIn(float duration, float currentTime) { SyncState(); State.FadeIn(duration, currentTime); }
        public void FadeOut(float duration, float currentTime) { SyncState(); State.FadeOut(duration, currentTime); }
        public void Disappear() { SyncState(); State.Disappear(); }
        public void SetAlpha(float alpha) { SyncState(); State.SetAlpha(alpha); }
        public void AlterUpdate(float deltaTime) { SyncState(); State.AlterUpdate(deltaTime); }
    }
}
