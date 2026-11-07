using Gallop.Live.Cutt;

namespace Gallop.Live
{
    public partial class Director
    {
        private void SubscribeChromaticAberrationTimeline(LiveTimelineControl control)
        {control.OnUpdateChromaticAberration+=OnUpdateChromaticAberration;}
        private void UnsubscribeChromaticAberrationTimeline(LiveTimelineControl control)
        {control.OnUpdateChromaticAberration-=OnUpdateChromaticAberration;}
        private void OnUpdateChromaticAberration(ref ChromaticAberrationUpdateInfo info)
        {
            // Original InitializeCamera forms its Live3d effect list from the
            // main effect followed by kTimelineCameraIndices. The Viewer uses
            // camera slot0 for its main/motion view and these cached timeline
            // effects. Monitor/Multi/Eye cameras are not added by this adapter.
            ApplyChromaticAberrationToCamera(0,ref info);
            foreach(int index in kTimelineCameraIndices)ApplyChromaticAberrationToCamera(index,ref info);
        }
        private void ApplyChromaticAberrationToCamera(int index,ref ChromaticAberrationUpdateInfo info)
        {
            if(_cameraImageEffects==null||index<0||index>=_cameraImageEffects.Length)return;
            var effect=_cameraImageEffects[index];
            if(effect!=null)LiveChromaticAberrationTimeline.Apply(ref effect.RenderParameter.ChromaticAberration,ref info);
        }
    }
}
