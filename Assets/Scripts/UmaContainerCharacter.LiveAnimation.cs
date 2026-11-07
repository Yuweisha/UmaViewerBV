using System;
using Gallop;
using Gallop.Live;
using Gallop.Live.Cutt;
using UnityEngine;

public partial class UmaContainerCharacter
{
    public LiveAnimationModeRuntime LiveAnimationMode { get; private set; }

    public LiveSystemTextFaceRuntime LiveSystemTextFace;
    public LiveTransformCanceler LiveMotionCanceler { get; private set; }
    public LiveRunUnityPlayable LiveRunPlayer => LiveAnimationMode?.Player as LiveRunUnityPlayable;
    public LiveRunMotionIdentity LiveRunIdentity { get; private set; }
    public AnimationClip[] LiveRunClips { get; private set; }

    Animation liveLegacyAnimation;

    sealed class LiveAnimationOwner : ILiveAnimationModeOwner
    {
        readonly UmaContainerCharacter actor;
        public LiveAnimationOwner(UmaContainerCharacter value)
        {
            actor = value;
        }

        public bool HasCanceler => actor.LiveMotionCanceler != null;
        public bool HasFace => actor.FaceDrivenKeyTarget != null;
        public int ClothCategory => (int)actor.LiveClothAssets.ClothCategory;

        public void AddCanceler() => actor.LiveMotionCanceler = new LiveTransformCanceler(actor.transform, () => actor.LiveTotalScale, () => actor._cySpringController?.RefreshHierarchyAfterMerge())
        {
            Enabled = true
        };
        public void ResetCancelParameter() => actor.LiveMotionCanceler.ResetCancelParameter();
        public void ZeroPositionNode() => actor.Position.localPosition = Vector3.zero;
        public void StopLegacyAnimation() => actor.liveLegacyAnimation.Stop();
        public ILiveAnimationModePlayable CreatePlayable()
        {
            var player = new LiveRunUnityPlayable(actor.UmaAnimator);
            player.Motion.RaceClips = actor.LiveRunClips;
            return player;
        }

        public void SetCullingMode(int mode) => actor.UmaAnimator.cullingMode = (AnimatorCullingMode)mode;
        public void ResetCySpring()
        {
            if (actor._cySpringLoaded)
                actor._cySpringController.Reset();
        }

        public void ReconstructCySpring(int category) => actor.ReconstructLiveCySpring(category);
        // Ordinary faces have no initialized SystemText state. Once that
        // consumer is installed, mode changes execute its complete restoration.
        public void StopSystemTextFacial() => actor.StopLiveSystemTextFace();
        public void SetLiveIKEnabled(bool enabled) => actor.LiveModelIK.Enabled = enabled;
        public void SetLiveLimbIKEnabled(bool enabled) => actor.LiveModelIK.Limb.Enabled = enabled;
    }

    public void InitializeLiveAnimationMode(Animation animation)
    {
        if (!IsLive || IsMini || LiveAnimationMode != null)
            return;
        if (animation == null || animation.gameObject != gameObject || UmaAnimator == null || UmaAnimator.gameObject != gameObject)
            throw new InvalidOperationException("Live Animation and Animator must belong to the assembled character root.");
        liveLegacyAnimation = animation;
        InitializeLiveClothResources();
        LiveAnimationMode = new LiveAnimationModeRuntime(new LiveAnimationOwner(this));
    }

    Transform ResolveLiveCancelTarget(int target)
    {
        switch (target)
        {
            case 1:
                return LiveModelIK.NodeCache.TryGetValue("Hip", out var hip) ? hip : null;
            case 2:
                return Body != null ? Body.GetComponent<AssetHolder>()?.Get<GameObject>("waist")?.transform : null;
            case 3:
                return HeadBone != null ? HeadBone.transform : null;
            case 4:
                return LiveModelIK.NodeCache.TryGetValue("Neck", out var neck) ? neck : null;
            case 5:
                return LiveModelIK.NodeCache.TryGetValue("Chest", out var chest) ? chest : null;
            default:
                return null;
        }
    }

    public void PrepareLiveRunResources()
    {
        if (LiveRunClips == null)
        {
            LiveRunIdentity = UmaDatabaseController.Instance.ReadLiveRunMotionIdentity(CharaEntry);
            LiveRunClips = LiveRunMotionResources.LoadClips(LiveRunIdentity, LiveMasterDressId, LiveRunMotionResources.LoadTable());
            if (LiveRunPlayer != null)
                LiveRunPlayer.Motion.RaceClips = LiveRunClips;
        }
    }

    public void UpdateLiveRunAnimation(LiveTimelineKeyCharaMotionData key, float motionTime)
    {
        PrepareLiveRunResources();
        if (LiveAnimationMode.CurrentMode != 2)
        {
            LiveAnimationMode.ChangeRaceAnimation();
            if (LiveSystemTextBody != null)
                LiveSystemTextBody.CurrentMotion = null;
        }

        LiveMotionCanceler.UpdateParameters((int)key._cancelOffsetCheckTarget, ResolveLiveCancelTarget, key._isCancelX, key._isCancelY, key._isCancelZ, key._cancelRate, key._cancelFixPos, key._isCancelScaleAveragePos);
        LiveRunPlayer.Motion.Update(LiveRunIdentity.RunningType, key._runModeMotionSpeed, key._runModeBlendRate, motionTime);

    }

    public void ChangeLiveLegacyAnimation()
    {
        LiveAnimationMode.ChangeLegacyAnimation();
        if (LiveSystemTextBody != null)
            LiveSystemTextBody.CurrentMotion = null;
    }

    void ReleaseLiveAnimation()
    {
        var player = LiveRunPlayer;
        player?.Dispose();
#if UNITY_EDITOR
        if (player != null)
            ;
#endif
    }
}
