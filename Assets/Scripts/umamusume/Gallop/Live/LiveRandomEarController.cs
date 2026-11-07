using System;
using System.Collections.Generic;
using System.Linq;
using Gallop.Live.Cutt;
using UnityEngine;

namespace Gallop.Live
{
    // Ordinary Live random-ear owner. System-text replacement is a separate
    // path; current Live corpus uses explicit type/index selections.
    public sealed class LiveRandomEarController : IDisposable
    {
        readonly UmaContainerCharacter actor;
        readonly List<UmaDatabaseEntry> acquired = new List<UmaDatabaseEntry>();
        readonly Dictionary<EarType,DrivenKeyAnimation[]> animations = new Dictionary<EarType,DrivenKeyAnimation[]>();
        readonly GameObject sampler;
        public LiveEarAnimationPlayer Player { get; }
        public int CurrentFrame { get; private set; } = -1;
        public IReadOnlyDictionary<EarType,DrivenKeyAnimation[]> Animations => animations;
        bool disposed;

        public LiveRandomEarController(UmaContainerCharacter actor,IEnumerable<EarType> types)
        {
            this.actor=actor;
            var locatorEntry=UmaViewerMain.Instance.AbList["3d/animator/drivenkeylocator"];
            var locatorBundle=Acquire(locatorEntry);sampler=UnityEngine.Object.Instantiate(locatorBundle.LoadAllAssets<GameObject>().Single(),actor.transform);
            sampler.name="Live random ear locator sampler";Player=new LiveEarAnimationPlayer(sampler);
            foreach(var type in types.Distinct())
            {
                var loaded=new List<DrivenKeyAnimation>();
                foreach(string motion in UmaDatabaseController.Instance.ReadLiveRandomEarMotionNames(type))
                {
                    string path=("3d/Motion/Event/Facial/Type00/anm_eve_type00_"+motion+"_driven").ToLowerInvariant();
                    var entry=UmaViewerMain.Instance.AbList[path];var bundle=Acquire(entry);
                    var animation=bundle.LoadAllAssets<DrivenKeyAnimation>().Single();
                    if(animation.clip==null)throw new InvalidOperationException("Missing random-ear AnimationClip: "+entry.Name);
                    loaded.Add(animation);
                }
                if(loaded.Count>0)animations.Add(type,loaded.ToArray());
            }
        }

        AssetBundle Acquire(UmaDatabaseEntry entry)
        {
            var bundle=UmaAssetManager.LoadAssetBundle(entry,false,true);
            if(bundle==null)throw new InvalidOperationException("Could not load random-ear resource: "+entry.Name);
            acquired.Add(entry);return bundle;
        }

        public void SetTimelineKey(LiveTimelineKeyFacialEarData key)
        {
            if(key==null)return;
            var state=actor.FaceDrivenKeyTarget.LiveEarState;
            if(!key.useEarRandomMotion){state.SetRandomMotion(false);Player.Stop();CurrentFrame=-1;return;}
            if(CurrentFrame==key.frame)return;
            state.SetRandomMotion(true);
            if(!animations.TryGetValue(key.randomMotionEarType,out var choices)||choices.Length==0)return;
            Player.Play(choices[key.randomMotionIndex],state.Weights);CurrentFrame=key.frame;
        }

        public void Tick(float delta) { Player.Tick(delta,actor.FaceDrivenKeyTarget.LiveEarState.Weights); }

        public void Dispose()
        {
            if(disposed)return;disposed=true;Player.Dispose();
            if(sampler!=null){if(Application.isPlaying)UnityEngine.Object.Destroy(sampler);else UnityEngine.Object.DestroyImmediate(sampler);}
            foreach(var entry in acquired)UmaAssetManager.UnloadAssetBundle(entry,false);acquired.Clear();
        }
    }
}
