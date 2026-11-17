using System;
using System.Collections.Generic;
using System.Linq;
using Gallop;
using Gallop.Live;
using UnityEngine;

// Autonomous ordinary-preview face owner; it does not enter the Live timeline.
internal sealed class PreviewIdleFaceController : IDisposable
{
    readonly UmaContainerCharacter actor;
    readonly PreviewIdleMotionData data;
    readonly List<UmaDatabaseEntry> earResources = new List<UmaDatabaseEntry>();
    readonly List<DrivenKeyAnimation> ears = new List<DrivenKeyAnimation>();
    readonly LiveEyeBlinkClock blink = new LiveEyeBlinkClock();
    LiveEarWeightTransition earWeights = new LiveEarWeightTransition();
    LiveEarAnimationPlayer earPlayer;
    GameObject sampler;
    UmaDatabaseEntry blinkResource;
    EyeBlinkSettingData blinkSetting;
    float earWait;
    int previousEar = -1, blinkEye = -1;
    bool disposed, earPoseApplied, blinkPoseApplied;

    public PreviewIdleFaceController(UmaContainerCharacter actor, PreviewIdleMotionData data)
    {
        this.actor = actor ?? throw new ArgumentNullException(nameof(actor));
        this.data = data ?? throw new ArgumentNullException(nameof(data));
        blink.AutoBlink = true;
        blink.AutoDouble = true;
        blink.RandomRange = UnityEngine.Random.Range;
        blink.FaceStep += (step, duration) =>
        {
            if (step == 0 || step == 3) blinkEye = 1;
            else if (step == 1) blinkEye = 2;
            else if (step == 4) blinkEye = -1;
        };
        try { LoadBlinkSetting(); LoadEars(); Reset(); }
        catch { Dispose(); throw; }
    }

    void LoadBlinkSetting()
    {
        if (!UmaViewerMain.Instance.AbList.TryGetValue("3d/animator/ast_eyeblink_set", out var entry))
            return; // The ordinary controller's documented default is 3–5 s.
        try
        {
            blinkResource = entry;
            var bundle = UmaAssetManager.LoadAssetBundle(entry, false, true);
            if (bundle == null) throw new InvalidOperationException("Could not load " + entry.Name);
            blinkSetting = bundle.LoadAllAssets<EyeBlinkSettingData>().Single();
            blink.HasSetting = true;
            blink.ForceCloseTime = blinkSetting.ForceEyeCloseTime;
        }
        catch (Exception exception)
        {
            if (blinkResource != null) UmaAssetManager.UnloadAssetBundle(blinkResource, false);
            blinkResource = null;
            blinkSetting = null;
            Debug.LogWarning("Preview idle blink uses default settings: " + exception.Message);
        }
    }

    AssetBundle AcquireEar(UmaDatabaseEntry entry)
    {
        earResources.Add(entry);
        var bundle = UmaAssetManager.LoadAssetBundle(entry, false, true);
        if (bundle == null) throw new InvalidOperationException("Could not load " + entry.Name);
        return bundle;
    }

    void LoadEars()
    {
        if (data.EarMotions == null || data.EarMotions.Length == 0) return;
        try
        {
            var entry = UmaViewerMain.Instance.AbList["3d/animator/drivenkeylocator"];
            var prefab = AcquireEar(entry).LoadAllAssets<GameObject>().Single();
            sampler = UnityEngine.Object.Instantiate(prefab);
            sampler.hideFlags = HideFlags.HideAndDontSave;
            sampler.name = "Preview idle ear sampler";
            earPlayer = new LiveEarAnimationPlayer(sampler);
            foreach (var motion in data.EarMotions)
            {
                var animation = AcquireEar(motion).LoadAllAssets<DrivenKeyAnimation>().Single();
                if (animation.clip == null || animation.animationEarType == null || animation.animationEarType.Length > 2)
                    throw new InvalidOperationException("Invalid ear animation: " + motion.Name);
                foreach (var side in animation.animationEarType)
                    if (side == null || side.faceType == null || side.count < 0 || side.count > side.faceType.Length || side.faceType.Any(type => (int)type < 0 || (int)type > 16))
                        throw new InvalidOperationException("Invalid ear animation types: " + motion.Name);
                ears.Add(animation);
            }
        }
        catch (Exception exception)
        {
            ReleaseEars();
            Debug.LogWarning("Preview idle random ears disabled: " + exception.Message);
        }
    }

    bool SuppressBlink()
    {
        if (actor.FaceOverrideData != null && actor.FaceOverrideData.Enable && actor.FaceOverrideData.IsDisableEyeBlink) return true;
        var ignored = blinkSetting != null ? blinkSetting.IgnoreEyeBlinkType : null;
        return actor.FaceDrivenKeyTarget.EyeMorphs.Exists(morph => morph.weight > 0.0001f &&
            (morph.index == 2 || morph.index == 5 || morph.index == 8 || morph.index == 16 ||
             morph.index == 22 || morph.index == 27 || morph.index == 28 ||
             (ignored != null && Array.IndexOf(ignored, (FaceEyeType)morph.index) >= 0)));
    }

    public void Tick(float delta)
    {
        if (disposed || delta <= 0f || actor.FaceDrivenKeyTarget == null) return;
        var face = actor.FaceDrivenKeyTarget;
        bool suppressed = SuppressBlink();
        if (suppressed)
        {
            ResetBlink();
            if (blinkPoseApplied) face.ApplyPreviewIdleBlink(-1);
            blinkPoseApplied = false;
        }
        else
        {
            blink.Update(delta, false);
            if (blinkEye >= 0 || blinkPoseApplied) face.ApplyPreviewIdleBlink(blinkEye);
            blinkPoseApplied = blinkEye >= 0;
        }

        if (earPlayer == null) return;
        if (face.HasPreviewManualEar || (actor.FaceOverrideData != null && actor.FaceOverrideData.Enable))
        {
            earPlayer.Clear();
            if (earPoseApplied) face.ChangeMorphEar();
            earPoseApplied = false;
            earWait = NextEarWait();
            return;
        }
        if (!earPlayer.Playing)
        {
            earWait -= delta;
            if (earWait <= 0f)
            {
                int selected = ears.Count == 1 ? 0 : UnityEngine.Random.Range(0, ears.Count - (previousEar >= 0 ? 1 : 0));
                if (previousEar >= 0 && ears.Count > 1 && selected >= previousEar) selected++;
                previousEar = selected;
                earPlayer.Play(ears[selected], earWeights, 0.2f);
                earWait = NextEarWait();
            }
        }
        earPlayer.Tick(delta, earWeights);
        earWeights.Advance(delta, DrivenKeyComponent.InterpolateType.Linear);
        if (earPlayer.Animation != null)
        {
            face.ApplyPreviewIdleEar(earWeights);
            earPoseApplied = true;
        }
    }

    float NextEarWait() => UnityEngine.Random.Range(data.EarInterval.x, data.EarInterval.y);

    void ResetBlink()
    {
        blink.Step = 5; blink.DoubleStep = blink.SingleCount = 0;
        blink.TimeCount = 0f; blink.TimeMax = UnityEngine.Random.Range(3f, 5f);
        blink.Force = blink.WaitOpen = false; blinkEye = -1;
    }

    public void Reset()
    {
        if (disposed) return;
        ResetBlink();
        earPlayer?.Clear();
        earWeights = new LiveEarWeightTransition();
        previousEar = -1; earWait = NextEarWait();
        var face = actor.FaceDrivenKeyTarget;
        if (face != null)
        {
            if (blinkPoseApplied) face.ApplyPreviewIdleBlink(-1);
            if (earPoseApplied) face.ChangeMorphEar();
        }
        blinkPoseApplied = earPoseApplied = false;
    }

    void ReleaseEars()
    {
        earPlayer?.Dispose(); earPlayer = null;
        if (sampler != null)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(sampler);
            else UnityEngine.Object.DestroyImmediate(sampler);
        }
        sampler = null;
        foreach (var entry in earResources) UmaAssetManager.UnloadAssetBundle(entry, false);
        earResources.Clear(); ears.Clear();
    }

    public void Dispose()
    {
        if (disposed) return;
        Reset(); disposed = true;
        ReleaseEars();
        if (blinkResource != null) UmaAssetManager.UnloadAssetBundle(blinkResource, false);
        blinkResource = null;
    }
}
