using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live
{
    // CUTT ids address the original serialized Animation state order. Preserve
    // the native controller cache and state sampling instead of wrapping clip time twice.
    public sealed class StageSourceAnimation
    {
        readonly GameObject owner;
        readonly Animation animation;
        readonly List<AnimationClip> clips = new List<AnimationClip>();
        readonly List<string> stateNames = new List<string>();
        int lastAnimationId = -1;
        float lastAnimationTime = -1f;
        public StageSourceAnimation(GameObject owner)
        {
            this.owner = owner;
            if (!owner.scene.IsValid()) throw new InvalidOperationException("Cannot sample a prefab asset");
            animation = owner.GetComponent<Animation>();
            if (!animation) throw new InvalidOperationException("Missing source Animation: " + owner.name);
            foreach (AnimationState state in animation)
            {
                clips.Add(state.clip);
                stateNames.Add(state.name);
            }
            // Deterministic CUTT sampling owns the clock, including paused seeks.
            animation.Stop();
            animation.enabled = false;
        }
        public string Sample(int id, float time, WrapMode wrap)
        {
            // Native invalid ids preserve the preceding controller state.
            if (id < 0 || id >= clips.Count) return null;
            var clip = clips[id];
            if (!clip) throw new InvalidOperationException("Null source clip");
            if (id == lastAnimationId && Mathf.Approximately(time, lastAnimationTime))
                return stateNames[id];
            if (id != lastAnimationId && lastAnimationId >= 0 &&
                animation.IsPlaying(stateNames[lastAnimationId]))
                animation.Stop(stateNames[lastAnimationId]);
            lastAnimationId = id;
            string name = stateNames[id];
            animation.Play(name);
            lastAnimationTime = time;
            var state = animation[name];
            state.time = time;
            state.enabled = true;
            animation.wrapMode = wrap;
            animation.Sample();
            state.enabled = false;
            return name;
        }
    }
}
