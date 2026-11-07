using System.Collections.Generic;
using Gallop;
using Gallop.Live;
using Gallop.Live.Cutt;
using UnityEngine;

public partial class UmaContainerCharacter
{
    public LiveEarAnimationPlayer LiveSystemTextEar { get; private set; }

    public int LiveSystemTextTailState;
    readonly Dictionary<int, DrivenKeyAnimation>[] systemEarClips =
    {
        new Dictionary<int, DrivenKeyAnimation>(),
        new Dictionary<int, DrivenKeyAnimation>()
    };
    readonly Dictionary<int, AnimationClip>[] systemTailClips =
    {
        new Dictionary<int, AnimationClip>(),
        new Dictionary<int, AnimationClip>()
    };
    GameObject systemEarLocator;
    sealed class SystemTextSubmotionPlayer : ILiveSystemTextSubmotionPlayer
    {
        readonly UmaContainerCharacter actor;
        public SystemTextSubmotionPlayer(UmaContainerCharacter actor)
        {
            this.actor = actor;
        }

        public int Mode => actor.LiveAnimationMode.CurrentMode;
        public int TailState { get => actor.LiveSystemTextTailState; set => actor.LiveSystemTextTailState = value; }

        public bool TryTail(int id, bool second, out object clip)
        {
            bool found = actor.systemTailClips[second ? 1 : 0].TryGetValue(id, out var value);
            clip = value;
            return found;
        }

        public void SetTailClip(object clip) => actor.LiveRunPlayer.Simple.SetClip(4, clip);
        public void CrossFadeTail(float duration) => actor.LiveRunPlayer.Simple.CrossFade(2, 4, 0f, duration);
        public void StopTail()
        {
            actor.LiveRunPlayer.Simple.StopTailAnimation();
            TailState = 0;
        }

        public object EarClip(int id, bool second) => actor.systemEarClips[second ? 1 : 0].TryGetValue(id, out var clip) ? clip : null;
        public void PlayEar(object clip, float duration)
        {
            var animation = (DrivenKeyAnimation)clip;
            if (animation == null)
                return;
            if (actor.LiveSystemTextEar == null)
            {
                var bundle = actor.AcquireSystemTextResource(LiveEffectController.FindResourceEntry("3d/animator/drivenkeylocator"));
                actor.systemEarLocator = Object.Instantiate(bundle.LoadAllAssets<GameObject>()[0], actor.transform);
                actor.systemEarLocator.name = "Live SystemText ear locator sampler";
                actor.LiveSystemTextEar = new LiveEarAnimationPlayer(actor.systemEarLocator);
            }

            actor.LiveSystemTextEar.Play(animation, actor.FaceDrivenKeyTarget.LiveEarState.Weights, duration);

        }
    }

    void PrepareLiveSystemTextSubmotionResources(Dictionary<int, LiveSystemTextMotionSet> primary, Dictionary<int, LiveSystemTextMotionSet> secondary)
    {
        var sets = new[]
        {
            primary,
            secondary
        };
        for (int side = 0; side < 2; side++)
        {
            systemEarClips[side].Clear();
            systemTailClips[side].Clear();
            foreach (var pair in sets[side])
            {
                string path = LiveSystemTextSubmotion.EarPath(pair.Value);
                if (path != null)
                {
                    var bundle = AcquireSystemTextResource(LiveEffectController.FindResourceEntry(path));
                    if (bundle != null)
                    {
                        var clips = bundle.LoadAllAssets<DrivenKeyAnimation>();
                        if (clips.Length > 0)
                            systemEarClips[side].Add(pair.Key, clips[0]);
                    }
                }

                path = LiveSystemTextSubmotion.TailPath(pair.Value);
                if (path != null)
                {
                    var bundle = AcquireSystemTextResource(LiveEffectController.FindResourceEntry(path));
                    if (bundle != null)
                    {
                        var clips = bundle.LoadAllAssets<AnimationClip>();
                        if (clips.Length > 0)
                            systemTailClips[side].Add(pair.Key, clips[0]);
                    }
                }
            }
        }
    }

    void PlayLiveSystemTextSubmotion(int id, LiveSystemTextMotionSet motion, bool second) => LiveSystemTextSubmotion.Play(new SystemTextSubmotionPlayer(this), id, motion, second);
    void ReleaseLiveSystemTextSubmotion()
    {
        LiveSystemTextEar?.Dispose();
        if (systemEarLocator != null)
            Object.Destroy(systemEarLocator);
    }
}
