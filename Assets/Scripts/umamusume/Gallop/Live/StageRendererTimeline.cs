using Gallop.Live.Cutt;
using UnityEngine;

namespace Gallop.Live
{
    public partial class StageController
    {
        void BindRendererTimeline(LiveTimelineControl control)
        {
            control.OnUpdateRenderer-=UpdateRenderer;
            control.OnUpdateRenderer+=UpdateRenderer;
        }
        void UnbindRendererTimeline(LiveTimelineControl control)
        {
            if(control!=null)control.OnUpdateRenderer-=UpdateRenderer;
        }
#if UNITY_EDITOR
        public event System.Action<Renderer,bool> RendererTimelineWrite;
#endif
        void WriteRendererTimeline(Renderer renderer,bool value)
        {
            SetStageRendererEnabled(renderer,value);
#if UNITY_EDITOR
            RendererTimelineWrite?.Invoke(renderer,value);
#endif
        }
        // Original4ebdee0 chooses the first enabled unit, preserving child order
        // and duplicate writes. A matched empty unit still suppresses fallback.
        public void UpdateRenderer(ref RendererUpdateInfo info)
        {
            var unit=FindStageObjectUnit(info.TimelineNameHash);
            if(unit!=null)
            {
                foreach(int child in unit.ChildObjectNameHashArray)UpdateRendererByHash(child,info.renderEnable);
                return;
            }
            UpdateRendererByHash(info.TimelineNameHash,info.renderEnable);
        }
        // Original4ebdfc0: root dictionary success owns the whole branch, even
        // when its renderer array is null. Work fallback stores one Renderer.
        void UpdateRendererByHash(int hash,bool value)
        {
            if(_stageObjectRenderers.TryGetValue(hash,out var roots))
            {
                if(roots!=null)foreach(var renderer in roots)WriteRendererTimeline(renderer,value);
                return;
            }
            if(!_objectsWorkInfoDictionary.TryGetValue(hash,out var works))return;
            foreach(var work in works)
            {
                // RegisterObjectTransform captures GetComponents<Renderer> in
                // component order; original work stores GetComponent<Renderer>.
                if(work.renderers!=null&&work.renderers.Length>0&&work.renderers[0]!=null)WriteRendererTimeline(work.renderers[0],value);
            }
        }
    }
}
