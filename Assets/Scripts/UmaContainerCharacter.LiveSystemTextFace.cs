using System;
using System.Collections.Generic;
using Gallop;
using Gallop.Live;
using Gallop.Live.Cutt;
using UnityEngine;

public partial class UmaContainerCharacter
{
    public LiveLipSyncRuntime LiveSystemTextLip { get; private set; }
    public LiveSystemTextModelFace LiveSystemTextModelFace { get; private set; }
    public LiveSystemTextDrivenKeyPlayer LiveSystemTextDrivenKey { get; private set; }

    readonly Dictionary<int, DrivenKeyAnimation>[] systemFacialClips =
    {
        new Dictionary<int, DrivenKeyAnimation>(),
        new Dictionary<int, DrivenKeyAnimation>()
    };
    readonly Dictionary<string, LipSyncDataFile> systemLipFiles = new Dictionary<string, LipSyncDataFile>();
    SystemTextFaceOwner systemFaceOwner;

    sealed class SystemTextFaceOwner : ILiveSystemTextFacialPlayback, ILiveLipSyncOwner
    {
        readonly UmaContainerCharacter actor;
        public SystemTextFaceOwner(UmaContainerCharacter actor)
        {
            this.actor = actor;
        }

        FaceDrivenKeyTarget Face => actor.FaceDrivenKeyTarget;

        LiveEyeTraceController Eye => Face.LiveEyeTrace;

        public bool MouthEnabled;
        public bool HasOwner => actor != null;
        public float DeltaTime => actor.LiveModelOriginalDeltaTime ?? Time.deltaTime;
        public float EngineDeltaTime => Time.deltaTime;
        public FaceParts[] StoredMouth => actor.LiveSystemTextModelFace.Stored._mouth;

        public void SetMouth(FaceParts[] parts, float weight, float duration)
        {
            Face.SubmitLiveSystemTextMouth(parts, weight, duration);

        }

        public bool HasEyeTrace => Eye != null;

        public object StoreEyeTrace() => Eye.StoreState();
        public void ResetEyeTraceForSystemText() => Eye.ResetForSystemText();
        public void RestoreEyeTrace(object state) => Eye.RestoreState((LiveEyeTraceController.StateParameter)state);
        public bool AutoBlink => actor.LiveEyeBlink?.Owner.Clock.AutoBlink ?? false;

        public void SetAutoBlink(bool enabled, bool doubleBlink)
        {
            var blink = actor.LiveEyeBlink?.Owner.Clock;
            if (blink != null)
            {
                blink.AutoBlink = enabled;
                blink.AutoDouble = doubleBlink;
            }
        }

        public object OtherLipSync => Face;

        public bool GetLipSyncEnabled(object component) => component is LiveLipSyncRuntime lip ? lip.Enabled : ReferenceEquals(component, this) ? MouthEnabled : Face.OrdinaryLiveLipSyncEnabled;
        public void SetLipSyncEnabled(object component, bool enabled)
        {
            if (component is LiveLipSyncRuntime lip)
                lip.Enabled = enabled;
            else if (ReferenceEquals(component, this))
                MouthEnabled = enabled;
            else
                Face.OrdinaryLiveLipSyncEnabled = enabled;
        }

        public object DrivenKey => Face;

        public void StopFaceLayer(object component) => actor.LiveSystemTextDrivenKey?.Stop();
        public void ClearFaceClip(object component)
        {
            actor.LiveSystemTextDrivenKey?.Clear();
            actor.LiveSystemTextEar?.Clear();
        }

        public void ResetEyeBlinkStatus()
        {
            var blink = actor.LiveEyeBlink?.Owner;
            if (blink != null)
            {
                blink.Clock.WaitOpen = false;
                blink.CanBlink = true;
            }

            Face.RestoreLiveSystemTextSources();
        }

        public FaceTypeData GetFaceType(string label) => UmaDatabaseController.Instance.LiveSystemTextFaceParts.GetFace(label);
        public void SetFace(FaceTypeData face, float duration)
        {
            actor.LiveSystemTextModelFace.Set(face, duration);

            var blink = actor.LiveEyeBlink?.Owner;
            if (blink != null)
            {
                blink.CanBlink = true;
                blink.Clock.WaitOpen = LiveEyeBlinkOwner.CheckIgnore(actor.LiveSystemTextModelFace.Stored._eyeR, actor.LiveSystemTextModelFace.Stored._eyeL, new[] { 5, 8, 2, 16, 22 });
            }
        }

        public bool HasFacialMotion(LiveSystemTextMotionSet motion, int characterId) => motion != null && motion.FaceType == "None";
        public void PlayDrivenKey(int id, bool second)
        {
            if (!actor.systemFacialClips[second ? 1 : 0].TryGetValue(id, out var clip) || clip == null)
                return;
            if (actor.LiveSystemTextDrivenKey == null)
            {
                var entry = LiveEffectController.FindResourceEntry("3d/animator/drivenkeylocator");
                var bundle = actor.AcquireSystemTextResource(entry);
                actor.LiveSystemTextDrivenKey = new LiveSystemTextDrivenKeyPlayer(UnityEngine.Object.Instantiate(bundle.LoadAllAssets<GameObject>()[0], actor.transform));
            }

            actor.LiveSystemTextDrivenKey.Play(clip);
            var blink = actor.LiveEyeBlink?.Owner;
            if (blink != null)
            {
                blink.Clear();
                blink.CanBlink = false;
            }
        }

        public LipSyncDataFile LoadLipSync(LiveSystemTextData text, int characterId) => actor.LoadSystemTextLip(text, characterId);
        public void UpdateMouthSet(FaceTypeData face) => actor.liveSystemMouthSet.Update(actor.LiveSystemTextLip, face);
        public void CloseMouthId(string label) => LiveLipSyncMouthSet.CloseMouthIdFromFaceType(actor.LiveSystemTextLip, label);
        public void SetEyeDefault(bool value) => Eye.SetDefault(value);
        public void ClearEyeDuration() => Eye.ClearDuration();
        public void ZeroEyeDuration() => Eye.SetDurationZero();
        public void SetCheek(bool enabled, int type) => Face.SetLiveSystemTextCheek(enabled, type);
        public void PlayLipSync(LipSyncDataFile file, int index)
        {

            actor.LiveSystemTextLip.Play(file, index);
        }

        public void StopLipSync()
        {

            actor.LiveSystemTextLip.Stop();
        }
    }

    LiveLipSyncMouthSet liveSystemMouthSet;
    AssetBundle AcquireSystemTextResource(UmaDatabaseEntry entry)
    {
        if (entry == null)
            return null;
        foreach (var dependency in UmaAssetManager.SearchAB(UmaViewerMain.Instance, entry))
            if (!LoadedAssets.Contains(dependency))
                LoadedAssets.Add(dependency);
        return UmaAssetManager.LoadAssetBundle(entry);
    }

    LipSyncDataFile LoadSystemTextLip(LiveSystemTextData text, int characterId)
    {
        if (string.IsNullOrEmpty(text.LipSyncData))
            return null;
        string path = "lipsync/system/" + characterId.ToString("D4") + "/" + text.LipSyncData;
        if (systemLipFiles.TryGetValue(path, out var cached))
            return cached;
        var bundle = AcquireSystemTextResource(LiveEffectController.FindResourceEntry(path));
        LipSyncDataFile file = null;
        if (bundle != null)
        {
            var assets = bundle.LoadAllAssets<TextAsset>();
            if (assets.Length > 0)
            {
                var files = new List<LipSyncDataFile>();
                LipSyncDataFile.SetLipSyncPackFile(assets[0], files);
                if (files.Count > 0)
                    file = files[0];
            }
        }

        systemLipFiles.Add(path, file);
        return file;
    }

    void PrepareLiveSystemTextFaceResources(int characterId, Dictionary<int, LiveSystemTextMotionSet> primary, Dictionary<int, LiveSystemTextMotionSet> secondary)
    {
        if (systemFaceOwner == null)
        {
            systemFaceOwner = new SystemTextFaceOwner(this);
            LiveSystemTextModelFace = new LiveSystemTextModelFace(UmaDatabaseController.Instance.LiveSystemTextFaceParts, FaceDrivenKeyTarget.SubmitLiveSystemTextFace);
            int original = CharaEntry.IsMob ? 1 : CharaEntry.Id;
            liveSystemMouthSet = UmaDatabaseController.Instance.ReadLiveLipSyncMouthSet(original);
            LiveSystemTextLip = new LiveLipSyncRuntime(systemFaceOwner);
            LiveSystemTextFace = new LiveSystemTextFaceRuntime(systemFaceOwner)
            {
                Initialized = true,
                SystemLipSync = LiveSystemTextLip,
                SystemLipSyncMouth = systemFaceOwner
            };
        }

        var sets = new[]
        {
            primary,
            secondary
        };
        for (int side = 0; side < 2; side++)
        {
            systemFacialClips[side].Clear();
            foreach (var pair in sets[side])
            {
                if (pair.Value.FaceType != "None")
                    continue;
                string path = "3d/Motion/Event/Facial/Chara/chr" + characterId.ToString("D4") + "_00/anm_eve_chr" + characterId.ToString("D4") + "_00_" + pair.Value.BodyMotion + "_face_driven";
                var bundle = AcquireSystemTextResource(LiveEffectController.FindResourceEntry(path));
                if (bundle != null)
                {
                    var clips = bundle.LoadAllAssets<DrivenKeyAnimation>();
                    if (clips.Length > 0)
                        systemFacialClips[side].Add(pair.Key, clips[0]);
                }
            }
        }
    }

    public void StartLiveSystemTextFace(LiveSystemTextData text, LiveSystemTextMotionSet motion, int characterId, int id, int head, bool second, bool smooth)
    {

        LiveSystemTextFace.Start(text, motion, characterId, id, Director.instance._liveCurrentTime, head, second, smooth);
    }

    public void StopLiveSystemTextFace()
    {
        bool wasActive = LiveSystemTextFace?.Active == true;
        LiveSystemTextFace?.Stop();
#if UNITY_EDITOR
        if (wasActive)
            ;
#endif
    }
}
