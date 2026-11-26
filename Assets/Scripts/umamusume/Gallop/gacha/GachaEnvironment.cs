using System;
using System.Collections.Generic;
using System.Linq;
using Gallop;
using Gallop.CutIn.Cutt;
using Gallop.RenderPipeline;
using UnityEngine;
using UnityEngine.Rendering;
using static Gallop.CutIn.Cutt.CutInTimelineWorkSheet;

internal sealed class GachaEnvironment : IDisposable
{
    private static readonly int CameraFov = Shader.PropertyToID("_GlobalCameraFov");
    private static readonly int FogColor = Shader.PropertyToID("_Global_FogColor");
    private static readonly int FogMinimum = Shader.PropertyToID("_Global_FogMinDistance");
    private static readonly int FogLength = Shader.PropertyToID("_Global_FogLength");
    private static readonly int FogDensity = Shader.PropertyToID("_Global_MaxDensity");
    private static readonly int FogHeight = Shader.PropertyToID("_Global_MaxHeight");
    private static readonly int FogWorldOrigin = Shader.PropertyToID("_Global_FogWorld_Origin");
    private static readonly int DirtColor = Shader.PropertyToID("_GlobalDirtColor");
    private static readonly int DirtRim = Shader.PropertyToID("_GlobalDirtRimSpecularColor");
    private static readonly int DirtToon = Shader.PropertyToID("_GlobalDirtToonColor");

    private readonly Camera camera;
    private float? savedCameraFov;
    private Camera maskCamera;
    private Camera monitorCamera;
    private FogKey[] keys = Array.Empty<FogKey>();
    private bool resetFogParam;
    private LightKey[] lightKeys = Array.Empty<LightKey>();
    private Light[] sceneLights = Array.Empty<Light>();
    private ReflectionKey[] reflectionKeys = Array.Empty<ReflectionKey>();
    private (MirrorReflection Mirror, MirrorReflection.Direction Direction, bool Enabled, bool CameraEnabled)[] mirrors =
        Array.Empty<(MirrorReflection, MirrorReflection.Direction, bool, bool)>();
    private ReflectionKey previousReflection;
    private int frame;
    private Globals saved;
    private bool applied;
    private LightState savedLight;
    private Light sampledLight;
    private (Light Light, AdditionalLightKey[] Keys)[] additional = Array.Empty<(Light, AdditionalLightKey[])>();
    private Light savedSun;
    private bool sunOverridden;
    private MultiLightShadowKey[] shadowKeys = Array.Empty<MultiLightShadowKey>();
    private MultiLightShadowController shadowReceiver;
    private readonly List<(Renderer Renderer, Material[] Original, Material[] Instance)> receivers =
        new List<(Renderer, Material[], Material[])>();
    private float savedShadowDistance;
    private bool shadowDistanceApplied;
    private (Color Color, Color Rim, Color Toon)? dirtColors;
    private (Color Color, Color Rim, Color Toon)? savedDirtColors;

    public GachaEnvironment(Camera camera)
    {
        this.camera = camera;
        RenderPipelineManager.beginCameraRendering += Begin;
        RenderPipelineManager.endCameraRendering += End;
    }

    public void SetTrack(CutInTimelineWorkSheet sheet, Transform cutRoot, IReadOnlyList<GameObject> backgrounds,
        Camera maskCamera, Camera monitorCamera)
    {
        this.maskCamera = maskCamera;
        this.monitorCamera = monitorCamera;
        keys = GachaCutInLoader.Keys(sheet._environment?._fogKeys);
        resetFogParam = sheet._environment?._fogKeys?.IsResetFogParam == true;
        lightKeys = GachaCutInLoader.Keys(sheet._environment?._lightKeys);
        if (lightKeys.Length > 0)
            sceneLights = UnityEngine.Object.FindObjectsOfType<Light>(true);
        reflectionKeys = GachaCutInLoader.Keys(sheet._environment?._reflectionKeys);
        if (reflectionKeys.Length > 0)
            mirrors = backgrounds.SelectMany(bg => bg.GetComponentsInChildren<MirrorReflection>(true)).Distinct()
                .Select(mirror => (mirror, mirror.Dir, mirror.enabled, mirror.MirrorCamera && mirror.MirrorCamera.enabled))
                .ToArray();
        shadowKeys = GachaCutInLoader.Keys(sheet.MultiLightShadowList);
        frame = -1;

        additional = (sheet.AdditionalLightList ?? Array.Empty<NamedTrack<AdditionalLightKey>>())
            .Select(track => GachaCutInLoader.Keys(track?._keys))
            .Where(track => track.Length > 0)
            .Select(track =>
            {
                var light = new GameObject("CutIn additional light").AddComponent<Light>();
                light.transform.SetParent(cutRoot, false);
                light.cullingMask = camera.cullingMask | (maskCamera ? maskCamera.cullingMask : 0);
                light.enabled = false;
                return (light, track);
            }).ToArray();

        var background = backgrounds.FirstOrDefault();
        if (shadowKeys.Length == 0 || !background)
            return;
        foreach (var renderer in background.GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer.receiveShadows)
                continue;
            var materials = renderer.sharedMaterials;
            var instances = Array.ConvertAll(materials, material => material ? new Material(material) : null);
            renderer.sharedMaterials = instances;
            receivers.Add((renderer, materials, instances));
        }
        if (receivers.Count == 0)
            return;
        shadowReceiver = cutRoot.gameObject.AddComponent<MultiLightShadowController>();
        shadowReceiver.ControlsGlobalDistance = false;
        shadowReceiver.Initialize(receivers.Select(receiver => receiver.Renderer).ToArray(),
            receivers.SelectMany(receiver => receiver.Instance).Where(material => material).ToArray());
    }

    public void Sample(int currentFrame)
    {
        frame = currentFrame;
        if (mirrors.Length == 0)
            return;
        GachaCutInPlayer.GetKeyInterval(reflectionKeys, frame, out var key, out _);
        if (key == previousReflection)
            return;
        previousReflection = key;
        foreach (var (mirror, direction, enabled, cameraEnabled) in mirrors)
            SetReflection(mirror, key?._direction ?? direction, key?._isEnable ?? enabled,
                key?._isEnable ?? cameraEnabled);
    }

    private static void SetReflection(MirrorReflection mirror, MirrorReflection.Direction direction, bool enabled, bool cameraEnabled)
    {
        if (!mirror)
            return;
        mirror.Dir = direction;
        mirror.enabled = enabled;
        if (mirror.MirrorCamera)
            mirror.MirrorCamera.enabled = cameraEnabled;
    }

    public void SetDirtColors(Color color, Color rim, Color toon) => dirtColors = (color, rim, toon);

    private void Begin(ScriptableRenderContext context, Camera renderingCamera)
    {
        if (renderingCamera != camera && renderingCamera != maskCamera && renderingCamera != monitorCamera)
            return;

        // JP CameraData.OnBeforeCameraRendering calls GraphicSettings.SetGlobalCameraFov.
        // The ordinary preview's distance-based value also scales outline depth bias.
        savedCameraFov = Shader.GetGlobalFloat(CameraFov);
        Shader.SetGlobalFloat(CameraFov, Mathf.Min(renderingCamera.fieldOfView / 30f, 1f));
        ApplyLight(renderingCamera);
        ApplyAdditionalLights(renderingCamera);
        ApplyShadowReceiver();
        if (dirtColors is { } dirt)
        {
            savedDirtColors = (Shader.GetGlobalColor(DirtColor), Shader.GetGlobalColor(DirtRim), Shader.GetGlobalColor(DirtToon));
            WriteDirt(dirt);
        }
        // JP TimelineKeyFogDataList also resets/defaults fog when there is no playable key.
        // Fog shaders divide by the default length and height even when density is zero.
        if (keys.Length == 0 || frame < keys[0]._frame)
        {
            saved = Globals.Read();
            if (resetFogParam)
                saved.Write();
            else
                Globals.Default().Write();
            applied = true;
            return;
        }

        saved = Globals.Read();
        // Replay CutIn keys from JP's default fog state. Preview globals use a
        // height of 10; inheriting it darkens elevated actors and the moon when
        // the timeline leaves MaxHeight invalid (JP's default height is 100).
        var fog = resetFogParam ? saved : Globals.Default();
        Vector4 maximum = fog.Minimum + fog.Length;
        int index = 0;
        while (index < keys.Length && keys[index]._frame <= frame)
        {
            var key = keys[index++];
            fog.Apply(key, key, 1f, ref maximum);
        }

        if (index < keys.Length)
        {
            var previous = keys[index - 1];
            var next = keys[index];
            var interpolation = next._params is { Length: > 0 } ? next._params[0] : null;
            if (interpolation != null && interpolation._interpolateType != 0)
                fog.Apply(previous, next, interpolation.WeightBetween(previous, next, frame), ref maximum);
        }

        fog.Length = maximum - fog.Minimum;
        fog.Length.w = Mathf.Max(0.000001f, fog.Length.w);
        applied = true;
        fog.Write();
    }

    private void ApplyLight(Camera renderingCamera)
    {
        if (lightKeys.Length == 0 || frame < lightKeys[0]._frame)
            return;
        var sun = RenderSettings.sun;
        if (!AffectsCamera(sun, renderingCamera) || sun.type != LightType.Directional)
        {
            sun = null;
            foreach (var light in sceneLights)
                if (AffectsCamera(light, renderingCamera) && light.type == LightType.Directional && light.enabled &&
                    (!sun || light.intensity > sun.intensity))
                    sun = light;
        }
        if (!sun)
            return;
        savedLight = new LightState { Rotation = sun.transform.rotation, Color = sun.color, Enabled = sun.enabled };
        var state = savedLight;
        int index = 0;
        while (index < lightKeys.Length && lightKeys[index]._frame <= frame)
        {
            var key = lightKeys[index++];
            state.Apply(key, key, 1f, false);
        }
        if (index < lightKeys.Length)
        {
            var previous = lightKeys[index - 1];
            var next = lightKeys[index];
            var interpolation = next._params is { Length: > 0 } ? next._params[0] : null;
            if (interpolation != null && interpolation._interpolateType != 0)
                state.Apply(previous, next, interpolation.WeightBetween(previous, next, frame), true);
        }
        sampledLight = sun;
        state.Write(sun);
        if (state.Enabled)
            SetMainLight(sun);
    }

    private static bool AffectsCamera(Light light, Camera renderingCamera) =>
        light && light.gameObject.activeInHierarchy && (light.cullingMask & renderingCamera.cullingMask) != 0;

    private void SetMainLight(Light light)
    {
        savedSun = RenderSettings.sun;
        RenderSettings.sun = light;
        sunOverridden = true;
    }

    private void ApplyAdditionalLights(Camera renderingCamera)
    {
        foreach (var (light, track) in additional)
        {
            var current = KeyPair(track, out var next);
            if (current?.IsEnable != true || !AffectsCamera(light, renderingCamera))
                continue;
            float blend = Weight(current, next, next?._params);
            next ??= current;
            light.type = current.Type switch { 1 => LightType.Spot, 2 => LightType.Point, _ => LightType.Directional };
            light.transform.SetPositionAndRotation(Vector3.LerpUnclamped(current.Position, next.Position, blend),
                Quaternion.Euler(Vector3.LerpUnclamped(current.Rotate, next.Rotate, blend)));
            light.range = Mathf.LerpUnclamped(current.Range, next.Range, blend);
            light.spotAngle = (int)Mathf.LerpUnclamped(current.SpotAngle, next.SpotAngle, blend);
            light.bounceIntensity = (int)Mathf.LerpUnclamped(current.IndirectMultiplier, next.IndirectMultiplier, blend);
            light.shadows = current.ShadowType;
            light.shadowStrength = Mathf.LerpUnclamped(current.Strength, next.Strength, blend);
            light.shadowBias = Mathf.LerpUnclamped(current.Bias, next.Bias, blend);
            light.shadowNormalBias = Mathf.LerpUnclamped(current.NormalBias, next.NormalBias, blend);
            light.shadowNearPlane = Mathf.LerpUnclamped(current.NearPlane, next.NearPlane, blend);
            light.enabled = true;
            if (!sunOverridden && light.type == LightType.Directional && light.shadows != LightShadows.None)
                SetMainLight(light);
        }
    }

    private void ApplyShadowReceiver()
    {
        if (!shadowReceiver)
            return;
        var current = KeyPair(shadowKeys, out var next);
        if (current == null)
            return;
        float blend = Weight(current, next, next?._params);
        next ??= current;
        shadowReceiver.IsUseParam = current.IsUseParam;
        shadowReceiver.ShadowCenterPos = Vector3.LerpUnclamped(current.ShadowCenterPosition, next.ShadowCenterPosition, blend);
        shadowReceiver.ShadowRotation = Quaternion.Euler(Vector3.LerpUnclamped(current.ShadowForward, next.ShadowForward, blend));
        shadowReceiver.ShadowFadeStart = Mathf.LerpUnclamped(current.ShadowFadeStart, next.ShadowFadeStart, blend);
        shadowReceiver.ShadowFadeLength = Mathf.LerpUnclamped(current.ShadowFadeLength, next.ShadowFadeLength, blend);
        shadowReceiver.IsUseShadowFront = current.IsUseShadowFront;
        shadowReceiver.IsShadowNearStart = current.IsShadowNearStart;
        shadowReceiver.ShadowStartColor = Color.LerpUnclamped(current.ShadowStartColor, next.ShadowStartColor, blend);
        shadowReceiver.ShadowEndColor = Color.LerpUnclamped(current.ShadowEndColor, next.ShadowEndColor, blend);
        shadowReceiver.FillColor = Color.LerpUnclamped(current.FillColor, next.FillColor, blend);
        shadowReceiver.ShadowDistance = Mathf.LerpUnclamped(current.ShadowDistance, next.ShadowDistance, blend);
        shadowReceiver.UpdateParamater();
        if (shadowReceiver.IsUseParam)
        {
            savedShadowDistance = CameraData.GlobalMaxShadowDistance;
            CameraData.GlobalMaxShadowDistance = shadowReceiver.ShadowDistance;
            shadowDistanceApplied = true;
        }
    }

    private T KeyPair<T>(T[] track, out T next) where T : Key
    {
        int index = 0;
        while (index < track.Length && track[index]._frame <= frame)
            index++;
        next = index < track.Length ? track[index] : null;
        return index > 0 ? track[index - 1] : null;
    }

    private float Weight(Key current, Key next, Interpolation[] interpolation)
    {
        return interpolation is { Length: > 0 } && interpolation[0]?._interpolateType > 0
            ? interpolation[0].WeightBetween(current, next, frame) : 0f;
    }

    private void End(ScriptableRenderContext context, Camera renderingCamera)
    {
        if (renderingCamera == camera || renderingCamera == maskCamera || renderingCamera == monitorCamera)
            Restore();
    }

    private void Restore()
    {
        if (savedCameraFov is { } fov)
        {
            Shader.SetGlobalFloat(CameraFov, fov);
            savedCameraFov = null;
        }
        if (savedDirtColors is { } dirt)
        {
            WriteDirt(dirt);
            savedDirtColors = null;
        }
        if (sunOverridden)
        {
            RenderSettings.sun = savedSun;
            sunOverridden = false;
        }
        foreach (var (light, _) in additional)
            if (light)
                light.enabled = false;
        if (shadowDistanceApplied)
        {
            CameraData.GlobalMaxShadowDistance = savedShadowDistance;
            shadowDistanceApplied = false;
        }
        if (sampledLight)
        {
            savedLight.Write(sampledLight);
            sampledLight = null;
        }
        if (!applied)
            return;
        saved.Write();
        applied = false;
    }

    public void Clear()
    {
        Restore();
        if (previousReflection != null)
            foreach (var (mirror, direction, enabled, cameraEnabled) in mirrors)
                SetReflection(mirror, direction, enabled, cameraEnabled);
        mirrors = Array.Empty<(MirrorReflection, MirrorReflection.Direction, bool, bool)>();
        previousReflection = null;
        keys = Array.Empty<FogKey>();
        resetFogParam = false;
        lightKeys = Array.Empty<LightKey>();
        sceneLights = Array.Empty<Light>();
        dirtColors = null;
        maskCamera = null;
        monitorCamera = null;
        additional = Array.Empty<(Light, AdditionalLightKey[])>();
        shadowReceiver = null;
        foreach (var (renderer, original, instances) in receivers)
        {
            if (renderer)
                renderer.sharedMaterials = original;
            foreach (var material in instances)
                UnityEngine.Object.Destroy(material);
        }
        receivers.Clear();
    }

    private static void WriteDirt((Color Color, Color Rim, Color Toon) dirt)
    {
        Shader.SetGlobalColor(DirtColor, dirt.Color);
        Shader.SetGlobalColor(DirtRim, dirt.Rim);
        Shader.SetGlobalColor(DirtToon, dirt.Toon);
    }

    public void Dispose()
    {
        RenderPipelineManager.beginCameraRendering -= Begin;
        RenderPipelineManager.endCameraRendering -= End;
        Clear();
    }

    private struct LightState
    {
        public bool Enabled;
        public Quaternion Rotation;
        public Color Color;

        public void Apply(LightKey previous, LightKey next, float weight, bool interpolated)
        {
            if (!interpolated)
            {
                if (next._isEnable)
                    Enabled = true;
                if (next._isDisable)
                    Enabled = false;
            }
            if (next._angle?._isValid == true)
                Rotation = previous._angle?._isValid == true
                    ? Quaternion.Euler(Vector3.Lerp(previous._angle._value, next._angle._value, weight))
                    : Quaternion.Slerp(Rotation, Quaternion.Euler(next._angle._value), weight);
            if (next._color?._isValid == true)
                Color = UnityEngine.Color.Lerp(Color, next._color._value, weight);
        }

        public void Write(Light light)
        {
            light.transform.rotation = Rotation;
            light.color = Color;
            light.enabled = Enabled;
        }
    }

    private struct Globals
    {
        public Color Color;
        public Vector4 Minimum, Length;
        public Vector4 WorldOrigin;
        public float Density, Height;

        public static Globals Read() => new Globals
        {
            Color = Shader.GetGlobalColor(FogColor),
            Minimum = Shader.GetGlobalVector(FogMinimum),
            Length = Shader.GetGlobalVector(FogLength),
            Density = Shader.GetGlobalFloat(FogDensity),
            Height = Shader.GetGlobalFloat(FogHeight),
            WorldOrigin = Shader.GetGlobalVector(FogWorldOrigin)
        };

        public static Globals Default() => new Globals
        {
            Color = Color.black,
            Minimum = Vector4.zero,
            Length = new Vector4(1000f, 1000f, 1000f, 1000f),
            Density = 1f,
            Height = 100f,
            WorldOrigin = Vector4.zero
        };

        public void Apply(FogKey previous, FogKey next, float weight, ref Vector4 maximum)
        {
            if (next._color?._isValid == true)
                Color = UnityEngine.Color.Lerp(previous._color._value, next._color._value, weight);
            if (next._minDistance?._isValid == true)
                Minimum = Vector4.Lerp(previous._minDistance._value, next._minDistance._value, weight);
            if (next._maxDistance?._isValid == true)
                maximum = Vector4.Lerp(previous._maxDistance._value, next._maxDistance._value, weight);
            if (next._maxDensity?._isValid == true)
                Density = 1f - Mathf.Lerp(previous._maxDensity._value, next._maxDensity._value, weight) * 0.01f;
            if (next._maxHeight?._isValid == true)
                Height = Mathf.Lerp(previous._maxHeight._value, next._maxHeight._value, weight);
        }

        public void Write()
        {
            Shader.SetGlobalColor(FogColor, Color);
            Shader.SetGlobalVector(FogMinimum, Minimum);
            Shader.SetGlobalVector(FogLength, Length);
            Shader.SetGlobalFloat(FogDensity, Density);
            Shader.SetGlobalFloat(FogHeight, Height);
            Shader.SetGlobalVector(FogWorldOrigin, WorldOrigin);
        }
    }
}
