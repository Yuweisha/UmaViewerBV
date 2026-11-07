using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using RootMotion.FinalIK;

namespace Gallop.Live.Cutt
{
    public class LiveTimelineMotionSequence
    {
        private ILiveTimelineMSQTarget _target; // 0x10
        private Animation[] _animationArray; // 0x18
        private int _targetIndex; // 0x20
        private LiveTimelineKeyCharaMotionSeqDataList[] _keyArray; // 0x28
        private LiveTimelineKeyCharaMotionSeqDataList _currentKey; // 0x30
        private int _indexOfPlayingKey; // 0x38
        private LiveTimelineControl _timelineControl; // 0x40
        private float _heightRate; // 0x48
        private LiveTimelineDefine.SheetIndex _currentSheetIndex; // 0x4C
        //private LivePlayableAnimator[] _playableAnimatorArray; // 0x50
        private int _systemTextOverrideCharacterId = -1; // 0x58
        private const float OVERRIDE_ANIMATOR_FACIAL_WAIT = 1;
        private int _prevFrame; // 0x5C
        private float _prevFrameAnimationTime; // 0x60
        //Testing
        private Transform _tempTarget;
        private Animation _tempAnim;
        private UmaContainerCharacter _timescaleOwner;
        private BipedIK _sampleIk;
        private bool _resetIkBeforeSample;
        private bool _needChange;
        private LiveTimelineKeyCharaMotionData _prevKey;
        private AnimationClip _targetAnim = null;
        private bool _motionSetup = false;
        private LiveLegacyMotionPlayback _legacyPlayback;
        private LiveTimelineKeyCharaMotionData _interpolatedRunKey;
#if UNITY_EDITOR
        public static bool EditorLegacyKeyChanged;
#endif
        private int _curIndex = -1;
        private int _prevIndex = -1;
        public int charaIndex = -1;
        public void Initialize(Transform target, int targetIndex, int seqDataIndex, LiveTimelineControl timelineControl, List<AnimationClip> animclips = null)
        {
            var animations = Director.instance.charaAnims;
            if (targetIndex < 0 || targetIndex >= animations.Count || animations[targetIndex] == null)
                return;
            _tempTarget = target;
            _targetIndex = targetIndex;
            charaIndex = targetIndex;
            _tempAnim = animations[targetIndex];
            _timescaleOwner = Director.instance.ResolvePhysicalFacialCharacter(targetIndex);
            _timescaleOwner?.InitializeLiveAnimationMode(_tempAnim);
            if (animclips != null)
            {
                if (targetIndex >= animclips.Count)
                    return;
                _targetAnim = animclips[targetIndex];
                if (_targetAnim != null)
                    _tempAnim.AddClip(_targetAnim, _targetAnim.name);
                return;
            }

            _keyArray = timelineControl._keyArray;
            if (seqDataIndex < 0 || seqDataIndex >= _keyArray.Length)
                return;
            _currentKey = _keyArray[seqDataIndex];
            _systemTextOverrideCharacterId = timelineControl.data.worksheetList[0].charaMotSeqList[seqDataIndex].SystemTextMotionOverrideCharacterId;
            _sampleIk = _tempAnim.GetComponent<BipedIK>();
            if (_sampleIk != null)
            {
                // SolverManager.Update otherwise restores the bind pose after our manual sample.
                _resetIkBeforeSample = _sampleIk.fixTransforms;
                if (_resetIkBeforeSample)
                    _sampleIk.fixTransforms = false;
            }

            if (_currentKey != null)
            {
                foreach (var key in _currentKey.thisList)
                    SetupAnimationClip(key);
                if (_timescaleOwner != null && !_timescaleOwner.IsMini && _currentKey.thisList.Exists(key => key._isUseRunMode))
                    _timescaleOwner.PrepareLiveRunResources();
                if (_timescaleOwner != null && !_timescaleOwner.IsMini)
                    _timescaleOwner.PrepareLiveSystemTextBodyResources(_currentKey, _systemTextOverrideCharacterId);
            }

            _tempAnim.wrapMode = WrapMode.Clamp;
            _tempAnim.enabled = false;
        }

        private void SetupAnimationClip(LiveTimelineKeyCharaMotionData key)
        {
            // Native SetupAnimationClip 0x4f246b0 uses each loaded clip's
            // engine name as its StateName and initializes the state weight1.
            if (key == null || string.IsNullOrEmpty(key.motionName))
                return;
            // Native LoadAnimationClip (0x1ae4780) uses the existing clip triplet first.
            if (key.clip == null)
            {
                key.clip = LoadLiveAnimationClip(key.motionName, key.clip);
                key.clip2 = LoadLiveAnimationClip(key.motionName2, key.clip2);
                key.clip3 = LoadLiveAnimationClip(key.motionName3, key.clip3);
            }

            var swapContext = Director.instance.MotionSwapContext;
            if (swapContext != null)
            {
                swapContext.LoadClip = name => LoadLiveAnimationClip(name, null);
                swapContext.Load(key);
            }

            if (key.clip == null)
                return;
            if (key.clip != null)
            {
                key.StateName = key.clip.name;
                _tempAnim.AddClip(key.clip, key.StateName);
                _tempAnim[key.StateName].weight = 1f;
            }

            if (key.clip2 != null)
            {
                key.StateName2 = key.clip2.name;
                _tempAnim.AddClip(key.clip2, key.StateName2);
                _tempAnim[key.StateName2].weight = 1f;
            }

            if (key.clip3 != null)
            {
                key.StateName3 = key.clip3.name;
                _tempAnim.AddClip(key.clip3, key.StateName3);
                _tempAnim[key.StateName3].weight = 1f;
            }

            if (key.SwapMotionDataArray != null)
                foreach (var swap in key.SwapMotionDataArray)
                    if (swap.IsEnabledSwapAnimationClip && _tempAnim.GetClip(swap.SwapAnimationClipName) == null)
                    {
                        _tempAnim.AddClip(swap.SwapAnimationClip, swap.SwapAnimationClipName);
                        _tempAnim[swap.SwapAnimationClipName].weight = 1f;
                    }
        }

        private static AnimationClip LoadLiveAnimationClip(string motionName, AnimationClip existingClip)
        {
            if (existingClip != null || string.IsNullOrEmpty(motionName))
                return existingClip;
            // ResourcePath.GetLiveAnimationClipPath: original body motion, not a substitute action.
            string path = "3d/motion/live/body/" + motionName;
            var entry = LiveEffectController.FindResourceEntry(path);
            if (entry == null)
            {
                Debug.LogError("[LiveTimelineMotionSequence] Motion resource not found: " + path);
                return null;
            }

            var bundle = UmaAssetManager.LoadAssetBundle(entry);
            if (bundle == null)
            {
                Debug.LogError("[LiveTimelineMotionSequence] Motion bundle load failed: " + path);
                return null;
            }

            string assetName = Path.GetFileNameWithoutExtension(entry.Name);
            var clip = bundle.LoadAsset<AnimationClip>(assetName);
            if (clip == null)
            {
                foreach (string asset in bundle.GetAllAssetNames())
                {
                    if (!string.Equals(Path.GetFileNameWithoutExtension(asset), assetName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    clip = bundle.LoadAsset<AnimationClip>(asset);
                    break;
                }
            }

            if (clip == null)
                Debug.LogError("[LiveTimelineMotionSequence] Motion clip load failed: " + path + " (" + assetName + ")");
            return clip;
        }

        public float LastNoiseAddTime { get; private set; }
        public float LastAnimationTime { get; private set; }

        public void AlterUpdate(float currentTime, LiveTimelineKeyTimescaleDataList timescaleKeys, float noiseAddTime = 0f)
        {
            LastNoiseAddTime = noiseAddTime;
            var director = Director.instance;
            if (Director.instance.liveMode == 0)
            {
                if (_targetAnim != null)
                {
                    if (director.sliderControl.is_Touched || director.IsRecordVMD)
                    {
                        _tempAnim[_targetAnim.name].time = currentTime;
                        _tempAnim.Play(_targetAnim.name);
                        _motionSetup = false;
                    }
                    else if (!_motionSetup)
                    {
                        _tempAnim[_targetAnim.name].time = currentTime;
                        _tempAnim.Play(_targetAnim.name);
                        _motionSetup = true;
                    }
                }

                return;
            }

            if (_currentKey != null && _tempAnim != null)
            {
                // Original AlterUpdate 0x4f22620 selects the first authored
                // interval containing the integer frame, including its end.
                // It does not hold the latest key indefinitely or use the
                // seconds-based cursor shared by the legacy Viewer sampler.
                if (_currentKey.HasAttribute(LiveTimelineKeyDataListAttr.Disable))
                    return;
                int nativeFrame = director._liveTimelineControl.CurrentFrame;
                int previousIndex = _curIndex;
                _curIndex = -1;
                LiveTimelineKeyCharaMotionData arg = null;
                for (int index = 0; index < _currentKey.Count; index++)
                {
                    var candidate = _currentKey.thisList[index];
                    if (candidate.frame > nativeFrame)
                        break;
                    if (nativeFrame > unchecked(candidate.frame + candidate.playFrameLength))
                        continue;
                    if (arg == null)
                    {
                        arg = candidate;
                        _curIndex = index;
                    }
                }

                // Original AlterUpdate invokes target.SetEnableTimescale before
                // Legacy/Playable dispatch, even if the clip cannot be loaded.
                // Gaps restore enabled=true; a disabled track keeps prior state.
                if (_timescaleOwner != null)
                    _timescaleOwner.LiveTimescaleEnabled = arg == null || !arg.IsTimescaleDisabled;
                if (arg == null)
                    return;
                if (arg.UseOverrideSysTextAnimation)
                {
                    _tempAnim.Stop();
                    int head = arg.isMotionHeadFrameAll ? arg.motionHeadFrame : arg.motionHeadFrameSeparetes[charaIndex];
                    NativeLiveMotionTime.Calculate(out float motionTime, out float motionFrameTime, arg.frame, arg.playSpeed, head, arg.IsTimescaleDisabled, 1f / 60f, nativeFrame, currentTime, timescaleKeys, _prevFrame, _prevFrameAnimationTime);
                    _prevFrame = nativeFrame;
                    _prevFrameAnimationTime = motionFrameTime;
                    LastAnimationTime = motionTime;
                    if (_timescaleOwner != null && !_timescaleOwner.IsMini)
                        _timescaleOwner.UpdateLiveSystemTextAnimation(arg, motionTime, _systemTextOverrideCharacterId, head);
                    _prevIndex = _curIndex;
                    return;
                }

                if (arg.UseOverrideAnimation && !arg.UseOverrideSysTextAnimation && arg._isUseRunMode)
                {
                    // Original StopLiveAnimation precedes the missing-model gate.
                    _tempAnim.Stop();
                    int runHead = arg.isMotionHeadFrameAll ? arg.motionHeadFrame : arg.motionHeadFrameSeparetes[charaIndex];
                    NativeLiveMotionTime.Calculate(out float runTime, out float runFrameTime, arg.frame, arg.playSpeed, runHead, arg.IsTimescaleDisabled, 1f / 60f, nativeFrame, currentTime, timescaleKeys, _prevFrame, _prevFrameAnimationTime);
                    _prevFrame = nativeFrame;
                    _prevFrameAnimationTime = runFrameTime;
                    LastAnimationTime = runTime;
                    var runKey = arg;
                    if (_curIndex + 1 < _currentKey.Count)
                    {
                        var next = _currentKey.thisList[_curIndex + 1];
                        if (next.IsInterpolateKey())
                        {
                            if (_interpolatedRunKey == null)
                                _interpolatedRunKey = new LiveTimelineKeyCharaMotionData();
                            next.Interpolate(arg, ref _interpolatedRunKey, LiveTimelineControl.CalculateInterpolationValue(arg, next, nativeFrame));
                            runKey = _interpolatedRunKey;
                        }
                    }

                    if (_timescaleOwner != null && !_timescaleOwner.IsMini)
                        _timescaleOwner.UpdateLiveRunAnimation(runKey, runTime);
                    _prevIndex = _curIndex;
                    return;
                }

                if (string.IsNullOrEmpty(arg.motionName) || arg.clip == null)
                    return;
                AnimationClip anim = arg.clip;
                if (_timescaleOwner != null && !arg.UseOverrideAnimation)
                {
                    if (_legacyPlayback == null)
                    {
                        _legacyPlayback = new LiveLegacyMotionPlayback
                        {
                            Models = new ILiveLegacyMotionModel[]
                            {
                                new LiveLegacyUnityModel(_timescaleOwner)
                            },
                            Animations = new ILiveLegacyAnimation[]
                            {
                                new LiveLegacyUnityAnimation(_tempAnim)
                            },
                            HeightRate = _timescaleOwner.LiveModelIK != null ? _timescaleOwner.LiveModelIK.InitialHeightRate : 0f,
                            SetCurrentState = state => _timescaleOwner.LiveMSQCurrentAnimState = state != null ? ((LiveLegacyUnityAnimation.State)state).AnimationState : null,
                            SetCurrentStartTime = value => _timescaleOwner.LiveMSQCurrentAnimStartTime = value
                        };
                        _legacyPlayback.GetIKRates = (ref float a, ref float b, ref float c) => _timescaleOwner.LiveModelIK?.GetAnimRate(ref a, ref b, ref c);
                    }

                    _legacyPlayback.CalculateAnimationTime = () =>
                    {
                        NativeLiveMotionTime.Calculate(out float time, out float frameTime, arg.frame, arg.playSpeed, arg.isMotionHeadFrameAll ? arg.motionHeadFrame : arg.motionHeadFrameSeparetes[charaIndex], arg.IsTimescaleDisabled, 1f / 60f, nativeFrame, currentTime, timescaleKeys, _prevFrame, _prevFrameAnimationTime);
                        _prevFrame = nativeFrame;
                        _prevFrameAnimationTime = frameTime;
                        LastAnimationTime = LiveTimelineControl.MonitorFloat32(time + noiseAddTime);
                        return LastAnimationTime;
                    };
                    var key = new LiveLegacyMotionPlayback.Key
                    {
                        Frame = arg.frame,
                        Loop = arg.loop,
                        MotionName = arg.motionName,
                        Clip = arg.clip,
                        Clip2 = arg.clip2 != null ? arg.clip2 : null,
                        Clip3 = arg.clip3 != null ? arg.clip3 : null,
                        StateName = arg.StateName,
                        StateName2 = arg.StateName2,
                        StateName3 = arg.StateName3
                    };
                    if (arg.SwapMotionDataArray != null)
                    {
                        key.Swaps = new LiveLegacyMotionPlayback.Swap[arg.SwapMotionDataArray.Length];
                        for (int i = 0; i < key.Swaps.Length; i++)
                        {
                            var swap = arg.SwapMotionDataArray[i];
                            key.Swaps[i] = new LiveLegacyMotionPlayback.Swap
                            {
                                TargetMotion = (int)swap.TargetMotion,
                                Active = swap.IsEnabledSwapAnimationClip,
                                StateName = swap.SwapAnimationClipName
                            };
                        }
                    }

                    bool changed = previousIndex != _curIndex;
#if UNITY_EDITOR
                    EditorLegacyKeyChanged = changed;
#endif
                    _legacyPlayback.Update(key, 1f / 60f, changed, changed);

                    _tempAnim.Sample();

                    _prevIndex = _curIndex;
                    return;
                }

                int headFrame = arg.isMotionHeadFrameAll ? arg.motionHeadFrame : arg.motionHeadFrameSeparetes[charaIndex];
                NativeLiveMotionTime.Calculate(out float currentAnimationTime, out float frameTime, arg.frame, arg.playSpeed, headFrame, arg.IsTimescaleDisabled, 1f / 60f, nativeFrame, currentTime, timescaleKeys, _prevFrame, _prevFrameAnimationTime);
                _prevFrame = nativeFrame;
                _prevFrameAnimationTime = frameTime;
                currentAnimationTime = LiveTimelineControl.MonitorFloat32(currentAnimationTime + noiseAddTime);
                LastAnimationTime = currentAnimationTime;
                if (anim)
                {
                    if (_resetIkBeforeSample && _sampleIk != null && _sampleIk.isActiveAndEnabled)
                    {
                        _sampleIk.solvers.lookAt.FixTransforms();
                        foreach (var limb in _sampleIk.solvers.limbs)
                            limb.FixTransforms();
                    }

                    var state = _tempAnim[anim.name];
                    // Original PlayAnimState 0x4f256a0 keeps raw clip time.
                    // Non-looping negative time switches to Loop after the
                    // engine time setter/getter; clamping freezes real1176
                    // negative-head motions at the first pose.
                    state.wrapMode = arg.loop ? WrapMode.Loop : WrapMode.ClampForever;
                    state.enabled = true;
                    state.weight = 1;
                    state.time = currentAnimationTime;
                    if (!arg.loop && state.time < 0f)
                        state.wrapMode = WrapMode.Loop;
                    state.speed = 0f;
                    _tempAnim.Sample();
                    state.enabled = false;
                }

                _prevIndex = _curIndex;
            }
        }
    }
}
