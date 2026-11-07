using System;
using System.Collections.Generic;
using System.Linq;
using Gallop.Live.Cutt;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Gallop.Live
{
    // Loaded-model adapter for EyeHighlightController. Pose encoding and part
    // destinations follow 518f850/5191060/5190f90/51913d0; state->clip mapping
    // follows the original 5881790 cctor. Owns only the Eyes graph and its loads.
    public sealed class LiveEyeHighlightController : IDisposable
    {
        public static readonly string[] NodeNames = {
            "eye_base_info_l", "eye_big_info_l", "eye_small_info_l", "eye_base_info_r",
            "eye_big_info_r", "eye_small_info_r", "eye_kira_info" };
        public readonly Vector4[] MainParam = new Vector4[2];
        public readonly Vector4[] HighParam1 = { new Vector4(0,0,0,1), new Vector4(0,0,0,1), Vector4.zero };
        public readonly Vector4[] HighParam2 = { new Vector4(0,0,0,1), new Vector4(0,0,0,1) };
        public readonly LiveEyeHighlightPart[] Parts;
        public int State { get; private set; }
        public float PlayTime { get; private set; }
        public float PupilScale = 1f;
        public int TimelineTearyType { get; private set; }
        public Gallop.Model.Component.EyeHighlightController.TimeComparisonType ComparisonType;
        public bool Interpolating { get; private set; }
        public float InterpolationDuration { get; private set; }
        public float InterpolationTime { get; private set; }
        public float Speed { get; private set; } = 1f;
        // Original IsSuppressEyeBlink 518fdb0 tests PlayAttribute bit 2.
        public bool SuppressEyeBlink => State == 2;
        public string[] LoadedClipNames => clips.Select(clip => clip == null ? null : clip.name).ToArray();
        public IReadOnlyList<Material> TargetMaterials => materials;
        readonly UmaContainerCharacter actor;
        readonly List<Material> materials;
        readonly Transform locator;
        readonly Transform[] nodes;
        readonly Vector3[] defaultPositions, defaultScales;
        readonly Quaternion[] defaultRotations;
        readonly AnimationClip[] clips = new AnimationClip[2];
        readonly List<UmaDatabaseEntry> acquired = new List<UmaDatabaseEntry>();
        readonly Func<int, Vector2> interpolateTimes;
        readonly Func<int, AnimationClip> clipLoader;
        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        readonly AnimationClipPlayable[] playables = new AnimationClipPlayable[2];
        readonly Animator animator;
        readonly bool addedAnimator;
        bool prepared, disposed;
        int locatorType;
        int activeAssetId = -1;
        float lastTime = float.NaN;

        public LiveEyeHighlightController(UmaContainerCharacter actor, Transform eyes, List<Material> materials,
            Func<int, AnimationClip> clipLoader = null, Func<int, Vector2> interpolateTimes = null)
        {
            this.actor = actor; this.materials = materials;
            this.clipLoader = clipLoader ?? LoadClip;
            this.interpolateTimes = interpolateTimes ?? (id => UmaDatabaseController.Instance.ReadEyeHighlightInterpolate(id));
            nodes = new Transform[7]; defaultPositions = new Vector3[7]; defaultScales = new Vector3[7]; defaultRotations = new Quaternion[7];
            var all = eyes.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < nodes.Length; i++)
            {
                nodes[i] = all.FirstOrDefault(t => string.Equals(t.name, NodeNames[i], StringComparison.OrdinalIgnoreCase));
                if (nodes[i] == null) throw new InvalidOperationException("Missing eye-highlight node: " + NodeNames[i]);
                defaultPositions[i] = nodes[i].localPosition; defaultScales[i] = nodes[i].localScale; defaultRotations[i] = nodes[i].localRotation;
            }
            // Native part-object storage differs from authored node order; destinations are explicit.
            Parts = new[] {
                new LiveEyeHighlightPart(nodes[0], MainParam, 0), new LiveEyeHighlightPart(nodes[1], HighParam1, 0),
                new LiveEyeHighlightPart(nodes[2], HighParam1, 1), new LiveEyeHighlightPart(nodes[6], HighParam1, 2),
                new LiveEyeHighlightPart(nodes[3], MainParam, 1), new LiveEyeHighlightPart(nodes[4], HighParam2, 0),
                new LiveEyeHighlightPart(nodes[5], HighParam2, 1) };
            locator = actor.FaceDrivenKeyTarget?.DrivenKeyLocator?.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name == "Cheek_Ctrl");
            animator = eyes.GetComponent<Animator>(); addedAnimator = animator == null;
            if (addedAnimator) animator = eyes.gameObject.AddComponent<Animator>();
            if (actor.UmaAnimator != null) animator.avatar = actor.UmaAnimator.avatar;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            graph = PlayableGraph.Create("Live eye highlight"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer = AnimationMixerPlayable.Create(graph, 2);
            var output = AnimationPlayableOutput.Create(graph, "Eyes", animator); output.SetSourcePlayable(mixer);
            graph.Play();
            DefaultEyeMaterial();
        }

        public static LiveEyeHighlightController Create(UmaContainerCharacter actor)
        {
            if (actor == null || actor.IsMini) return null;
            var eyes = actor.transform.Find("Eyes");
            var materials = LiveEyeMaterialBinder.FindEyeMaterials(actor.transform);
            if (eyes == null || materials.Count == 0) return null;
            return new LiveEyeHighlightController(actor, eyes, materials);
        }

        AnimationClip LoadClip(int assetId)
        {
            string key = "anm_eye" + assetId.ToString("D2");
            var holder = actor.Head == null ? null : actor.Head.GetComponent<AssetHolder>();
            var direct = holder == null ? null : holder.Get<AnimationClip>(key);
            if (direct != null) return direct;
            string head = actor.LiveHeadResourceName;
            if (string.IsNullOrEmpty(head)) throw new InvalidOperationException("Eye-highlight head resource identity absent");
            int slash = head.LastIndexOf('/'); string directory = head.Substring(0, slash);
            string model = head.Substring(slash + 1).Replace("pfb_", "");
            string path = directory + "/facial/anm_" + model + "_facial_eye" + assetId.ToString("D2");
            if (!UmaViewerMain.Instance.AbList.TryGetValue(path, out var entry))
                throw new InvalidOperationException("Missing authored eye-highlight clip: " + path);
            var bundle = UmaAssetManager.LoadAssetBundle(entry, false, true);
            if (bundle == null) throw new InvalidOperationException("Could not load eye-highlight bundle: " + path);
            acquired.Add(entry);
            var clip = bundle.LoadAllAssets<AnimationClip>().FirstOrDefault();
            if (clip == null) throw new InvalidOperationException("Missing AnimationClip in " + path);
            return clip;
        }

        public void DefaultEyeMaterial()
        {
            var main = new Vector4[2];
            var high1 = new[] { new Vector4(0,0,0,1), new Vector4(0,0,0,1), Vector4.zero };
            var high2 = new[] { new Vector4(0,0,0,1), new Vector4(0,0,0,1) };
            foreach (var material in materials)
            {
                material.SetVectorArray("_MainParam", main); material.SetVectorArray("_HighParam1", high1); material.SetVectorArray("_HighParam2", high2);
            }
        }

        public void ApplyMaterial()
        {
            foreach (var material in materials)
            {
                material.SetVectorArray("_MainParam", MainParam); material.SetVectorArray("_HighParam1", HighParam1); material.SetVectorArray("_HighParam2", HighParam2);
                material.SetFloat("_EyePupliScale", PupilScale);
            }
        }

        public void Play(int state, bool immediate = false, float speed = 1f)
        {
            if (state < 0 || state > 2) throw new ArgumentOutOfRangeException(nameof(state));
            if (state == State) { Speed = speed; return; }
            if (state == 0) { Stop(); return; }
            int assetId = state == 1 ? 1 : 0;
            if (clips[assetId] == null)
            {
                clips[assetId] = clipLoader(assetId);
                if (clips[assetId] == null) throw new InvalidOperationException("Eye-highlight clip loader returned null for " + assetId);
                playables[assetId] = AnimationClipPlayable.Create(graph, clips[assetId]);
                playables[assetId].SetApplyFootIK(false); playables[assetId].SetApplyPlayableIK(false); playables[assetId].SetSpeed(0);
                graph.Connect(playables[assetId], 0, mixer, assetId);
            }
            var incoming = interpolateTimes(state); var outgoing = interpolateTimes(State);
            // Original Play treats a change from an already active animation as
            // immediate. Entering from NoAnim may interpolate using master in_time.
            bool skipInterpolation = immediate || State != 0;
            InterpolationDuration = skipInterpolation ? 0 : Mathf.Max(incoming.x, outgoing.y);
            InterpolationTime = 0; Interpolating = !skipInterpolation;
            State = state; PlayTime = 0; Speed = speed;
            activeAssetId = assetId;
            mixer.SetInputWeight(assetId, 1f); mixer.SetInputWeight(1 - assetId, 0f);
        }

        public void Stop()
        {
            if (State == 0) return;
            if (Interpolating)
            {
                Interpolating = false; InterpolationDuration = InterpolationTime = 0;
            }
            else
            {
                var incoming = interpolateTimes(0); var outgoing = interpolateTimes(State);
                Interpolating = true; InterpolationDuration = Mathf.Max(incoming.x, outgoing.y); InterpolationTime = 0;
            }
            State = 0; PlayTime = 0;
            activeAssetId = -1;
            mixer.SetInputWeight(0, 0f); mixer.SetInputWeight(1, 0f);
        }

        public void SetTimelineKey(LiveTimelineKeyFacialEffectData key)
        {
            if (key == null || ((int)key.attribute & LiveTimelineKeyFacialEffectData.kAttrTeary) == 0) return;
            int previous = TimelineTearyType; TimelineTearyType = key.tearyType;
            ComparisonType = key.TwinkleTimeComparisonType;
            if (key.tearyType <= 0) { if (State != 0) Stop(); }
            else if (key.tearyType != previous && key.tearyType < 3) Play(key.tearyType);
        }

        public void BeforeTimeline(float time, float delta)
        {
            if (disposed) return;
            if (!float.IsNaN(lastTime) && time < lastTime)
            { Stop(); Interpolating = false; InterpolationTime = InterpolationDuration = 0; TimelineTearyType = 0; locatorType = 0; }
            lastTime = time;
            if (State == 0)
            {
                for (int i = 0; i < nodes.Length; i++)
                { nodes[i].localPosition = defaultPositions[i]; nodes[i].localScale = defaultScales[i]; nodes[i].localRotation = defaultRotations[i]; }
            }
            else
            {
                PlayTime += Mathf.Max(0, delta) * Speed;
                int assetId = State == 1 ? 1 : 0;
                playables[assetId].SetTime(PlayTime); graph.Evaluate(0f);
            }
            UpdatePlaybackState();
            if (Interpolating)
            {
                InterpolationTime += Mathf.Max(0, delta);
                float ratio = InterpolationDuration > 0 ? InterpolationTime / InterpolationDuration : 1f;
                float threshold = ComparisonType == Gallop.Model.Component.EyeHighlightController.TimeComparisonType.ApproximatelyGreaterThan ? 0.999999f : 1f;
                if (ratio > threshold) { Interpolating = false; InterpolationTime = InterpolationDuration = 0; }
            }
            float interpolation = InterpolationDuration > 0 ? InterpolationTime / InterpolationDuration : 1f;
            foreach (var part in Parts) part.BeforeApplyDrivenKey(Interpolating, interpolation);
            prepared = true;
        }

        void UpdatePlaybackState()
        {
            // Original Update 5190d60: Teary has Loop bit 1; Twinkle is
            // non-looping. The raw normalized time must retain whole cycles,
            // and a different animator state must not end this animation.
            if (State != 2 || activeAssetId != 0 || clips[0] == null) return;
            float normalized = PlayTime / clips[0].length;
            if (normalized >= 1f) Stop();
        }

        public void AfterTimeline()
        {
            if (!prepared || disposed) return;
            foreach (var part in Parts) part.AfterApplyDrivenKey();
            if (actor.FaceDrivenKeyTarget != null) PupilScale = actor.FaceDrivenKeyTarget.LiveEyePupilScale;
            ApplyMaterial();
            if (locator != null)
            {
                int type = Mathf.RoundToInt(Mathf.Max(0, locator.localPosition.z * 100f));
                if (type != locatorType) { locatorType = type; if (type < 3) Play(type); }
            }
            prepared = false;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (graph.IsValid()) graph.Destroy();
            foreach (var entry in acquired) UmaAssetManager.UnloadAssetBundle(entry, false);
            acquired.Clear();
            if (addedAnimator && animator != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(animator);
                else UnityEngine.Object.DestroyImmediate(animator);
            }
        }
    }
}
