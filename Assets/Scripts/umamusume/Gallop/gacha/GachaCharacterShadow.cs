using System;
using Gallop.Live;
using UnityEngine;
using UnityEngine.Rendering;
using static Gallop.CutIn.Cutt.CutInTimelineWorkSheet;
using Object = UnityEngine.Object;

internal sealed class GachaCharacterShadow : IDisposable
{
    private const int Run = 1, Stop = 2, MultiLight = 3, Cross = 4;
    private const float GroundOffset = 0.001f;
    private static readonly int ShadowColor = Shader.PropertyToID("_ShadowColor");
    private static readonly Color DefaultColor = new Color(0.2f, 0.2f, 0.2f, 1f);

    private readonly UmaContainerCharacter actor;
    private readonly CharacterShadowKey[] keys;
    private readonly GameObject instance;
    private readonly Renderer[] renderers;
    private readonly Material running, stopped;
    private readonly FakeShadowController[] crosses;
    private readonly Renderer[] casters;
    private readonly ShadowCastingMode[] originalCasting;
    private int mode = -1;

    public GachaCharacterShadow(UmaContainerCharacter actor, CharacterShadowKey[] keys,
        GameObject prefab, Material runMaterial, Material stopMaterial, GameObject crossPrefab)
    {
        this.actor = actor;
        this.keys = keys;
        casters = actor.GetComponentsInChildren<Renderer>(true);
        originalCasting = Array.ConvertAll(casters, renderer => renderer.shadowCastingMode);
        if (prefab && runMaterial && stopMaterial)
        {
            instance = Object.Instantiate(prefab, actor.transform, false);
            instance.SetActive(false);
            GachaCutInLoader.SetLayer(instance);
            renderers = instance.GetComponentsInChildren<Renderer>(true);
            running = new Material(runMaterial);
            stopped = new Material(stopMaterial);
        }
        if (crossPrefab)
        {
            var bones = actor.GetComponentsInChildren<Transform>(true);
            crosses = new[] { BindFoot(crossPrefab, bones, "Ankle_L"), BindFoot(crossPrefab, bones, "Ankle_R") };
        }
    }

    public static bool HasFootShadow(CharacterShadowKey key) => key._shadowType == Run || key._shadowType == Stop;
    public static bool HasCrossShadow(CharacterShadowKey key) => key._shadowType == Cross;

    private FakeShadowController BindFoot(GameObject prefab, Transform[] bones, string ankle)
    {
        var shadow = Object.Instantiate(prefab, actor.transform, false);
        GachaCutInLoader.SetLayer(shadow);
        var controller = shadow.GetComponent<FakeShadowController>();
        if (controller)
        {
            var foot = Array.Find(bones, node => node.name == ankle);
            controller.Bind(foot ? foot : actor.Position, actor.Position);
            shadow.SetActive(false);
            return controller;
        }
        shadow.SetActive(false);
        Object.Destroy(shadow);
        return null;
    }

    public void Sample(int frame, float clipLength)
    {
        var state = new State { Scale = Vector3.one, Loop = 0.23f, Color = DefaultColor, Power = 0.75f };
        int index = 0;
        while (index < keys.Length && keys[index]._frame <= frame)
        {
            var key = keys[index++];
            state.Apply(key, key, 1f, false);
        }
        if (index == 0)
            return;
        if (index < keys.Length)
        {
            var previous = keys[index - 1];
            var next = keys[index];
            var settings = next._params is { Length: > 0 } ? next._params[0] : null;
            if (settings != null && settings._interpolateType != 0)
                state.Apply(previous, next, settings.WeightBetween(previous, next, frame), true);
        }

        bool modeChanged = mode != state.Type;
        if (modeChanged)
        {
            var casting = state.Type == MultiLight ? ShadowCastingMode.On : ShadowCastingMode.Off;
            foreach (var renderer in casters)
                renderer.shadowCastingMode = casting;
            mode = state.Type;
        }

        if (crosses != null)
            foreach (var cross in crosses)
            {
                if (!cross)
                    continue;
                bool crossVisible = state.Type == Cross;
                cross.gameObject.SetActive(crossVisible);
                if (crossVisible)
                {
                    cross.ApplyCutIn(state.Color, state.Power, state.Offset, state.Scale);
                    cross.AlterLateUpdate(actor.Position.position);
                }
            }
        if (!instance)
            return;

        bool moving = state.Type == Run;
        bool visible = moving || state.Type == Stop;
        instance.SetActive(visible);
        if (!visible)
            return;

        var material = moving ? running : stopped;
        if (modeChanged)
        {
            foreach (var renderer in renderers)
                renderer.sharedMaterial = material;
        }
        material.SetColor(ShadowColor, state.Color);
        material.renderQueue = state.Queue ?? -1;

        var offset = state.Offset + new Vector3(0f, GroundOffset, 0.2f);
        var scale = state.Scale * 0.8f;
        var angles = state.Angles;
        if (moving && state.Loop > 0f)
        {
            var motion = actor.UmaAnimator.GetCurrentAnimatorStateInfo(0);
            float elapsed = Mathf.Repeat(motion.normalizedTime, 1f) * clipLength;
            float phase = Mathf.Repeat(elapsed, state.Loop) / state.Loop;
            float stride = 1f - Mathf.Abs(Mathf.Repeat(phase * 2f, 1f) * 2f - 1f);
            offset.z = state.Offset.z + stride * 0.2f;
            scale = Vector3.Scale(state.Scale, new Vector3(1.3f, 1f, 0.7f + stride * 0.7f));
            if (phase >= 0.5f)
                angles.z += 180f;
        }

        var anchor = state.HipLink && actor.UpBodyBone ? actor.UpBodyBone.transform : null;
        if (anchor)
        {
            Vector3 position = anchor.position + anchor.rotation * state.Offset;
            position.y = GroundOffset + (!moving && state.HeightLink ? actor.transform.position.y : 0f);
            instance.transform.position = position;
            if (!moving)
                scale = state.Scale;
        }
        else
            instance.transform.localPosition = offset;
        instance.transform.localRotation = Quaternion.Euler(angles);
        instance.transform.localScale = scale;
    }

    public void Dispose()
    {
        for (int i = 0; i < casters.Length; ++i)
            if (casters[i])
                casters[i].shadowCastingMode = originalCasting[i];
        if (instance)
        {
            instance.SetActive(false);
            Object.Destroy(instance);
        }
        if (crosses != null)
            foreach (var cross in crosses)
                if (cross)
                {
                    cross.gameObject.SetActive(false);
                    Object.Destroy(cross.gameObject);
                }
        Object.Destroy(stopped);
        Object.Destroy(running);
    }

    private struct State
    {
        public int Type;
        public int? Queue;
        public Color Color;
        public Vector3 Offset, Scale, Angles;
        public float Loop, Power;
        public bool HipLink, HeightLink;

        public void Apply(CharacterShadowKey previous, CharacterShadowKey next, float weight, bool interpolated)
        {
            Type = next._shadowType;
            HipLink = next._hipLink;
            HeightLink = !interpolated && next._heightLink;
            Color = next._color?._isValid == true
                ? UnityEngine.Color.Lerp(previous._color._value, next._color._value, weight) : DefaultColor;
            if (next._offset?._isValid == true)
                Offset = Vector3.Lerp(previous._offset._value, next._offset._value, weight);
            if (next._scale?._isValid == true)
                Scale = Vector3.Lerp(previous._scale._value, next._scale._value, weight);
            if (next._angles?._isValid == true || next._angle?._isValid == true)
                Angles = Vector3.Lerp(AnglesFor(previous), AnglesFor(next), weight);
            if (next._loopTime?._isValid == true)
                Loop = Mathf.Lerp(previous._loopTime._value, next._loopTime._value, weight);
            if (next._shadowPower?._isValid == true)
                Power = Mathf.Lerp(previous._shadowPower._value, next._shadowPower._value, weight);
            if (next._renderQueue?._isValid == true)
                Queue = Mathf.RoundToInt(Mathf.Lerp(previous._renderQueue._value, next._renderQueue._value, weight));
        }

        private static Vector3 AnglesFor(CharacterShadowKey key)
        {
            var angles = key._angles?._value ?? Vector3.zero;
            if (key._angle?._isValid == true)
                angles.x = key._angle._value;
            return angles;
        }
    }
}
