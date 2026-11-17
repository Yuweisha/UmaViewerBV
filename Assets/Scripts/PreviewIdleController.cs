using System;
using System.Collections.Generic;
using System.Linq;
using Gallop;
using UnityEngine;

/// <summary>Ordinary character idle owners, evaluated after the body and before CySpring.</summary>
internal sealed class PreviewIdleController : IDisposable
{
    readonly UmaContainerCharacter actor;
    readonly PreviewIdleMotionData data;
    readonly List<UmaDatabaseEntry> resources = new List<UmaDatabaseEntry>();
    readonly List<AnimationClip> tails = new List<AnimationClip>();
    readonly List<TailBinding> bindings = new List<TailBinding>();
    readonly List<CySpringBoneBase> springs = new List<CySpringBoneBase>();
    readonly List<float> springRatios = new List<float>();
    GameObject sampler;
    AnimationClip tail;
    float wait, time;
    int previous = -1;
    bool suspended, faceEnabled, disposed;
    readonly PreviewIdleFaceController face;

    sealed class TailBinding
    {
        public Transform Target, Sample;
        public Vector3 Position, Scale;
        public Quaternion Rotation;
    }

    public PreviewIdleController(UmaContainerCharacter actor, PreviewIdleMotionData data)
    {
        this.actor = actor;
        this.data = data;
        try
        {
            if (actor.FaceDrivenKeyTarget != null) face = new PreviewIdleFaceController(actor, data);
            LoadTail();
            wait = NextWait();
        }
        catch { Dispose(); throw; }
    }

    void LoadTail()
    {
        var root = actor.transform.Find("Position/Hip/Tail_Ctrl");
        if (root == null || data.TailMotions.Length == 0) return;
        try
        {
            foreach (var entry in data.TailMotions)
            {
                resources.Add(entry);
                var bundle = UmaAssetManager.LoadAssetBundle(entry, false, true);
                if (bundle == null) throw new InvalidOperationException("Could not load " + entry.Name);
                var clip = bundle.LoadAllAssets<AnimationClip>().Single();
                if (clip.length > 0f) tails.Add(clip);
            }
            // Only a plain Transform hierarchy is sampled. No model, material,
            // body animation or face locator is duplicated or evaluated here.
            sampler = new GameObject("Preview idle tail sampler") { hideFlags = HideFlags.HideAndDontSave };
            var ancestors = new Stack<Transform>();
            for (var node = root.parent; node != actor.transform; node = node.parent) ancestors.Push(node);
            Transform parent = sampler.transform;
            while (ancestors.Count > 0)
            {
                var source = ancestors.Pop();
                var node = new GameObject(source.name).transform;
                node.SetParent(parent, false); parent = node;
            }
            CloneTail(root, parent);
            // The original random-tail path blends out the animated top of the
            // spring chain, leaving the following tail segments simulated.
            actor.LiveCySpringController?.FindSpring(springs,
                name => name == "Sp_Hi_Tail0_B_00" || name == "Sp_Hi_Tail0_B_01",
                CySpringController.Parts.Tail, false);
            foreach (var spring in springs) springRatios.Add(spring.DynamicsRatio);
        }
        catch (Exception exception)
        {
            ReleaseTail();
            Debug.LogWarning("Preview idle random tail disabled: " + exception.Message);
        }
    }

    void CloneTail(Transform source, Transform parent)
    {
        var node = new GameObject(source.name).transform;
        node.SetParent(parent, false);
        bindings.Add(new TailBinding { Target = source, Sample = node });
        foreach (Transform child in source) CloneTail(child, node);
    }

    public void Tick(float delta, bool allowFace)
    {
        if (disposed || delta <= 0f) return;
        suspended = false;
        if (allowFace) face?.Tick(delta);
        else if (faceEnabled) face?.Reset();
        faceEnabled = allowFace;
        if (tails.Count == 0) return;
        if (tail == null)
        {
            wait -= delta;
            if (wait > 0f) return;
            int selected = tails.Count == 1 ? 0 : UnityEngine.Random.Range(0, tails.Count - (previous >= 0 ? 1 : 0));
            if (previous >= 0 && tails.Count > 1 && selected >= previous) selected++;
            previous = selected; tail = tails[selected]; time = 0f;
        }
        time = Mathf.Min(time + delta, tail.length);
        float inWeight = Mathf.Clamp01(time / 0.2f);
        float physicsWeight = Mathf.Max(1f - Mathf.Clamp01(time / 0.5f),
            Mathf.Clamp01((time - (tail.length - 0.5f)) / 0.5f));
        for (int i = 0; i < springs.Count; i++) springs[i].DynamicsRatio = springRatios[i] * physicsWeight;
        foreach (var binding in bindings)
        {
            binding.Position = binding.Target.localPosition;
            binding.Rotation = binding.Target.localRotation;
            binding.Scale = binding.Target.localScale;
            binding.Sample.localPosition = binding.Position;
            binding.Sample.localRotation = binding.Rotation;
            binding.Sample.localScale = binding.Scale;
        }
        tail.SampleAnimation(sampler, time);
        foreach (var binding in bindings)
        {
            binding.Target.localPosition = Vector3.Lerp(binding.Position, binding.Sample.localPosition, inWeight);
            binding.Target.localRotation = Quaternion.Slerp(binding.Rotation, binding.Sample.localRotation, inWeight);
            binding.Target.localScale = Vector3.Lerp(binding.Scale, binding.Sample.localScale, inWeight);
        }
        if (time >= tail.length)
        {
            tail = null; wait = NextWait(); RestorePhysics();
        }
    }

    float NextWait() => UnityEngine.Random.Range(data.TailInterval.x, data.TailInterval.y);

    void RestorePhysics()
    {
        for (int i = 0; i < springs.Count; i++) springs[i].DynamicsRatio = springRatios[i];
    }

    public void Suspend()
    {
        if (disposed || suspended) return;
        suspended = true; face?.Reset(); faceEnabled = false;
        tail = null; wait = NextWait(); RestorePhysics();
    }

    void ReleaseTail()
    {
        RestorePhysics(); springs.Clear(); springRatios.Clear();
        if (sampler != null)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(sampler);
            else UnityEngine.Object.DestroyImmediate(sampler);
        }
        sampler = null; bindings.Clear(); tails.Clear();
        foreach (var entry in resources) UmaAssetManager.UnloadAssetBundle(entry, false);
        resources.Clear();
    }

    public void Dispose()
    {
        if (disposed) return;
        face?.Dispose(); ReleaseTail(); disposed = true;
    }
}
