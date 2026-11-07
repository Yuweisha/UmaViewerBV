using System;
using System.Collections.Generic;
using Gallop.Live;
using Gallop.Live.Cutt;
using UnityEngine;

public partial class UmaContainerCharacter
{
    public LiveSystemTextBodyClipCache LiveSystemTextBodyClips { get; private set; }
    public LiveSystemTextMotionSwap.Build LiveSystemTextBuild { get; private set; }
    public LiveSystemTextBodyRuntime LiveSystemTextBody { get; private set; }

    AnimationClip LoadLiveSystemTextBodyClip(string path)
    {
        var entry = LiveEffectController.FindResourceEntry(path);
        AnimationClip clip = null;
        if (entry != null)
        {
            foreach (var dependency in UmaAssetManager.SearchAB(UmaViewerMain.Instance, entry))
                if (!LoadedAssets.Contains(dependency))
                    LoadedAssets.Add(dependency);
            var bundle = UmaAssetManager.LoadAssetBundle(entry);
            if (bundle != null)
            {
                var clips = bundle.LoadAllAssets<AnimationClip>();
                if (clips.Length > 0)
                    clip = clips[0];
            }
        }

        if (clip == null)
            Debug.LogError("[LiveSystemText] Body motion resource unavailable: " + path);

        return clip;
    }

    public void PrepareLiveSystemTextBodyResources(LiveTimelineKeyCharaMotionSeqDataList keys, int overrideCharacterId)
    {
        var master = UmaDatabaseController.Instance.LiveSystemTextMasterData;
        int originalCharacter = CharaEntry.IsMob ? 1 : CharaEntry.Id;
        int character = overrideCharacterId != -1 ? overrideCharacterId : originalCharacter;
        var primary = new Dictionary<int, LiveSystemTextMotionSet>();
        var secondary = new Dictionary<int, LiveSystemTextMotionSet>();
        foreach (var key in keys.thisList)
        {
            int id = key.MotionSysTextId;
            if (id <= 0)
                continue;
            var text = master.GetText(character, id);
            if (text == null)
                continue;
            var motion = master.GetMotionSet(text.MotionSet);
            if (motion == null)
                continue;
            primary[id] = motion;
            var second = text.MotionSecondSet > 0 ? master.GetMotionSet(text.MotionSecondSet) : null;
            if (key.UseSecondMotion && second != null && second.Id != motion.Id)
                secondary[id] = second;
        }

        if (primary.Count == 0)
            return;
        if (LiveSystemTextBodyClips == null)
        {
            int Field(string normal, string mob) => Convert.ToInt32(CharaData[CharaEntry.IsMob ? mob : normal]);
            LiveSystemTextBuild = new LiveSystemTextMotionSwap.Build
            {
                CharacterId = originalCharacter,
                DressId = LiveMasterDressId,
                // MasterCharaAndMobAccessor.InitMob fixes Height=1, Shape=0.
                HeightId = CharaEntry.IsMob ? 1 : Field("height", "chara_height"),
                BodyShapeId = CharaEntry.IsMob ? 0 : Field("shape", "shape"),
                BustId = Field("bust", "chara_bust_size"),
                ViewId = 200,
                Scene = 1
            };
            LiveSystemTextBodyClips = new LiveSystemTextBodyClipCache(LiveSystemTextMotionSwap.Load(LiveSystemTextBuild), LoadLiveSystemTextBodyClip);
        }

        LiveSystemTextBodyClips.Clear();
        foreach (var pair in primary)
            LiveSystemTextBodyClips.Load(pair.Key, pair.Value, character, false);
        foreach (var pair in secondary)
            LiveSystemTextBodyClips.Load(pair.Key, pair.Value, character, true);
        LiveSystemTextBodyClips.LoadIdle(UmaDatabaseController.Instance.ReadSystemTextIdleMotion(originalCharacter, CharaEntry.IsMob), originalCharacter);
        PrepareLiveSystemTextFaceResources(character, primary, secondary);
        PrepareLiveSystemTextSubmotionResources(primary, secondary);
        PrepareLiveSystemTextIK();

    }

    sealed class SystemTextBodyPlayer : ILiveSystemTextBodyPlayer
    {
        readonly UmaContainerCharacter actor;
        public SystemTextBodyPlayer(UmaContainerCharacter actor)
        {
            this.actor = actor;
        }

        public int Mode { get => actor.LiveAnimationMode.CurrentMode; set => actor.LiveAnimationMode.CurrentMode = value; }

        public void StopLegacy() => actor.liveLegacyAnimation.Stop();
        public void ResetRace() => actor.LiveRunPlayer.ResetRaceMode();
        public void SetCullingMode(int mode) => actor.UmaAnimator.cullingMode = (AnimatorCullingMode)mode;
        public void PreparePlayer()
        {
            if (actor.LiveAnimationMode.Player == null)
            {
                var player = new LiveRunUnityPlayable(actor.UmaAnimator);
                player.Motion.RaceClips = actor.LiveRunClips;
                actor.LiveAnimationMode.Player = player;
            }
            else
                actor.LiveAnimationMode.Player.Rebind();
        }

        public void EnablePlayer() => actor.LiveRunPlayer.Enabled = true;
        public bool HasClip(object clip) => (AnimationClip)clip != null;
        public float Length(object clip) => ((AnimationClip)clip).length;
        public void SetClip(int state, object clip) => actor.LiveRunPlayer.Simple.SetClip(state, clip);
        public int GetState() => actor.LiveRunPlayer.Simple.GetState(0);
        public void ToggleMainState() => actor.LiveRunPlayer.Simple.UseMainState = !actor.LiveRunPlayer.Simple.UseMainState;
        public void Play(int state, float time) => actor.LiveRunPlayer.Simple.Play(0, state, time);
        public void CrossFade(int state, float time, float duration) => actor.LiveRunPlayer.Simple.CrossFade(0, state, time, duration);
        public void SetPlayTime(float time) => actor.LiveRunPlayer.Simple.SetPlayTime(0, time);
        public void UpdateMotion(float delta) => actor.LiveRunPlayer.UpdateSimpleMotion(delta);
        public void RequestBlink() => actor.LiveEyeBlink?.Owner.RequestBlink(true);
    }

    public void UpdateLiveSystemTextAnimation(LiveTimelineKeyCharaMotionData key, float time, int overrideCharacterId, int motionHeadFrame = 0)
    {
        bool change = LiveAnimationMode.CurrentMode != 1 || LiveAnimationMode.PreviousSystemTextId != key.MotionSysTextId;

        if (change)
        {
            int character = overrideCharacterId != -1 ? overrideCharacterId : CharaEntry.IsMob ? 1 : CharaEntry.Id;
            var master = UmaDatabaseController.Instance.LiveSystemTextMasterData;
            var text = master.GetText(character, key.MotionSysTextId);
            var motion = text != null ? master.GetMotionSet(text.MotionSet) : null;
            if (motion != null)
            {
                if (LiveAnimationMode.CurrentMode != 1)
                    LiveAnimationMode.BeginSystemTextAnimation(LiveModelIK.Enabled, LiveModelIK.Limb.Enabled, LiveModelIK.Limb.ResetTargets);
                if (key.useOverrideMotionFacial && !LiveAnimationMode.UseOverrideFacialMotion)
                    LiveSystemTextFace?.Begin();
                Position.localPosition = Vector3.zero;
                BeginLiveSystemTextIK();
                if (LiveSystemTextBody == null)
                    LiveSystemTextBody = new LiveSystemTextBodyRuntime(new SystemTextBodyPlayer(this), (id, second) =>
                    {
                        var cache = second ? LiveSystemTextBodyClips.Secondary : LiveSystemTextBodyClips.Primary;
                        return cache.TryGetValue(id, out var clips) ? clips : null;
                    }, (id, clips) =>
                    {
                        var result = new List<AnimationClip>();
                        foreach (var clip in clips)
                            result.Add((AnimationClip)clip);
                        LiveSystemTextBodyClips.Secondary[id] = result;
                    }, set =>
                    {
                        bool add = false;
                        return LiveSystemTextBodyResources.GetPaths(set, character, new List<string>(), ref add);
                    });
                LiveSystemTextBody.IdleClips = LiveSystemTextBodyClips.IdleClips;
                LiveSystemTextBody.IdleMotion = LiveSystemTextBodyClips.IdleMotion;
                var second = text.MotionSecondSet > 0 ? master.GetMotionSet(text.MotionSecondSet) : null;
                if (!key.UseSecondMotion || second == null || second.Id == motion.Id)
                    second = null;
                void BeginCommon(LiveSystemTextMotionSet faceMotion, bool isSecond)
                {
                    LiveAnimationMode.UseOverrideFacialMotion = key.useOverrideMotionFacial;
                    if (key.useOverrideMotionFacial)
                    {
                        PlayLiveSystemTextSubmotion(key.MotionSysTextId, faceMotion, isSecond);
                        StartLiveSystemTextFace(text, faceMotion, character, key.MotionSysTextId, motionHeadFrame, isSecond, key.EyeTraceSmoothness);
                    }
                    else
                        StopLiveSystemTextFace();
                    LiveAnimationMode.PreviousSystemTextId = key.MotionSysTextId;
                    if (_cySpringLoaded)
                    {
                        _cySpringController.Reset();
                        _cySpringController.ReserveWarmingUpCySpring(1f, 0);
                    }
                }

                LiveSystemTextBody.Setup(key.MotionSysTextId, motion, second, second != null ? text.MotionSecondStart * .001f : 0f, () =>
                {
                    SecondLiveSystemTextIK();
                    BeginCommon(LiveSystemTextBody.CurrentMotion, true);
                });
                LiveAnimationMode.PreviousSystemTextAnimationMotionSec = 0f;
                BeginCommon(motion, false);
            }
        }

        if (LiveSystemTextBody != null)
        {
            float delta = LiveAnimationMode.CurrentMode == 1 ? Mathf.Max(0f, LiveTimelineControl.MonitorFloat32(time - LiveAnimationMode.PreviousSystemTextAnimationMotionSec)) : 0f;
            float effectiveDelta = LiveSystemTextBody.Update(time, delta);
            LiveAnimationMode.PreviousSystemTextAnimationMotionSec = time;
            LiveSystemTextIK?.UpdateMotion(LiveSystemTextBody.CurrentMotion.BodyMotion, ref LiveAnimationMode.PreviousSystemTextBodyMotionName, effectiveDelta);
#if UNITY_EDITOR
            if (LiveSystemTextIK != null)
                ;
#endif
            LiveAnimationMode.CurrentMotionSet = LiveSystemTextBody.CurrentMotion;
        }

    }
}
