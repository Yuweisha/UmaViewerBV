using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class UmaContainerCharacter
{
    PreviewIdleController previewIdle;
    PreviewIdleMotionData previewIdleData;
    bool previewIdleSelected;
    int previewAnimationLoadDepth;

    internal IEnumerator InitializePreviewIdle()
    {
        if (IsLive || IsMini || IsMob || !previewIdleSelected) yield break;
        previewIdleData = PreviewIdleMotionData.Load(this);
        if (Config.Instance.DownloadMissingResources || Config.Instance.WorkMode == WorkMode.Standalone)
        {
            var entries = new List<UmaDatabaseEntry>(previewIdleData.EarMotions.Concat(previewIdleData.TailMotions));
            foreach (string path in new[] { "3d/animator/drivenkeylocator", "3d/animator/ast_eyeblink_set" })
                if (Main.AbList.TryGetValue(path, out var entry)) entries.Add(entry);
            var dependencies = entries.SelectMany(entry => UmaAssetManager.SearchAB(Main, entry)).Distinct().ToList();
            yield return UmaViewerDownload.DownloadAssets(dependencies, UmaSceneController.instance.LoadingProgressChange);
        }
        // MergeModel schedules the old body/head/tail placeholders for Destroy.
        // Bind the surviving tail hierarchy after those removals complete.
        yield return null;
        if (this != null && Builder.CurrentUMAContainer == this) StartPreviewIdle();
    }

    void SelectPreviewIdle(string path)
    {
        if (IsLive) return;
        previewIdleSelected = !IsMini && !IsMob && path.Contains("/event/body/chara/") && path.EndsWith("_idle01_loop", StringComparison.Ordinal);
        StopPreviewIdle();
    }

    void StartPreviewIdle()
    {
        if (!previewIdleSelected || previewIdleData == null || previewIdle != null || IsLive || IsMini) return;
        previewIdle = new PreviewIdleController(this, previewIdleData);
    }

    internal void StopPreviewIdle()
    {
        previewIdle?.Dispose(); previewIdle = null;
    }

    internal void SuspendPreviewIdle() => previewIdle?.Suspend();

    void UpdatePreviewIdle()
    {
        if (previewIdle == null) return;
        float speed = PreviewLegacyClip != null ? PreviewLegacySpeed : UmaAnimator.speed;
        if (IsLive || UI.PoseManager.PoseModeOn || speed <= 0f ||
            (PreviewLegacyClip == null && !UmaAnimator.enabled))
        {
            previewIdle.Suspend(); return;
        }
        previewIdle.Tick(Time.deltaTime * speed,
            !isAnimatorControl && previewLegacyFace == null && previewLegacyEar == null);
    }
}
