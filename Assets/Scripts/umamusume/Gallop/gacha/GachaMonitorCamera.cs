using System;
using System.Collections.Generic;
using System.Linq;
using Gallop.CutIn.Cutt;
using Gallop.Live;
using UnityEngine;
using static Gallop.CutIn.Cutt.CutInTimelineWorkSheet;
using Object = UnityEngine.Object;

internal sealed class GachaMonitorCamera : IDisposable
{
    private static readonly int MainTexture = Shader.PropertyToID("_MainTex");
    private readonly GameObject root;
    private readonly Animator animator;
    private readonly AnimatorOverrideController controller;
    private readonly RenderTexture texture;
    private readonly MonitorTrack track;
    private readonly List<Receiver> receivers = new List<Receiver>();
    private CameraMotion previous;

    public Camera Camera { get; }

    public static GachaMonitorCamera Create(Camera main, Transform parent, MonitorTrack[] tracks,
        CutInTimelineData.MonitorCameraSettings settings, IEnumerable<GameObject> models)
    {
        var track = tracks?.FirstOrDefault(candidate => candidate?.MotionKeys?.thisList?.Length > 0);
        if (track == null)
            return null;
        var receivers = models.SelectMany(model => model.GetComponentsInChildren<MonitorCamera>(true))
            .Distinct().Where(marker => marker.GetComponent<Renderer>()).ToArray();
        if (receivers.Length == 0)
        {
            Debug.LogWarning("Gacha monitor camera has no authored receiver.");
            return null;
        }
        return new GachaMonitorCamera(main, parent, track, settings, receivers);
    }

    private GachaMonitorCamera(Camera main, Transform parent, MonitorTrack track,
        CutInTimelineData.MonitorCameraSettings settings, MonitorCamera[] markers)
    {
        this.track = track;
        var resolution = settings?.IsUseCustomResolution == true
            ? settings.TextureCustomResolution : new Vector2Int(320, 180);
        texture = new RenderTexture(resolution.x, resolution.y, 24);
        texture.Create();
        root = new GameObject("CutIn monitor camera");
        root.transform.SetParent(parent, false);
        Camera = root.AddComponent<Camera>();
        Camera.CopyFrom(main);
        Camera.depth = main.depth - 2f;
        Camera.cullingMask = main.cullingMask & ~(1 << GachaMaskCamera.OverlayLayer);
        Camera.clearFlags = CameraClearFlags.SolidColor;
        Camera.targetTexture = texture;
        Camera.aspect = (float)resolution.x / resolution.y;
        Camera.allowMSAA = settings?.UseMSAA_MonitorCamera == true;
        Camera.enabled = false;
        var motion = new GameObject("Monitor camera motion");
        motion.transform.SetParent(parent, false);
        animator = motion.AddComponent<Animator>();
        controller = Object.Instantiate(UmaViewerBuilder.Instance.CameraOverrideController);
        animator.runtimeAnimatorController = controller;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.speed = 0f;
        foreach (var marker in markers)
            receivers.Add(new Receiver(marker, texture));
    }

    public void Sample(int frame, float time, Func<int, (Transform Locator, float Scale)> targetResolver)
    {
        var enable = Current(track.MonitorCameraEnableKeys, frame);
        var motion = Current(track.MotionKeys, frame);
        Camera.enabled = enable?._isEnable == true && motion?._enable == true && motion._clip;
        if (!Camera.enabled)
            return;
        var source = animator.transform;
        if (previous != motion)
        {
            source.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            source.localScale = Vector3.one;
            controller["clip_1"] = motion._clip;
            previous = motion;
        }
        float elapsed = Mathf.Max(0f, time - motion._frame / 30f) * motion._playSpeed;
        if (motion._playLength > 0f)
            elapsed = Mathf.Min(elapsed, motion._playLength);
        float normalized = motion._clip.length > 0f ? (elapsed + motion._motionHeadTime) / motion._clip.length : 0f;
        animator.Play("motion_1", 0, Mathf.Clamp01(normalized));
        animator.Update(0f);
        var target = Current(track.CameraTargetKeys, frame);
        var (locator, scale) = target?._useTargetCharacter == true ? targetResolver(target._targetCharacterId) : (null, 1f);
        var parameters = Current(track.CameraParamKeys, frame);
        Quaternion rotationOffset = Quaternion.Euler(parameters?.RotationOffset?.OrZero ?? Vector3.zero);
        Vector3 position = rotationOffset * (source.localPosition * scale + (parameters?.PositionOffset?.OrZero ?? Vector3.zero));
        Quaternion rotation = rotationOffset * source.localRotation * Quaternion.Euler(0f, 180f, 0f);
        Camera.transform.SetLocalPositionAndRotation(locator ? locator.localPosition + locator.localRotation * position : position,
            locator ? locator.localRotation * rotation : rotation);
        Camera.fieldOfView = Mathf.Clamp(source.localScale.x, 1f, 179f);
        Camera.nearClipPlane = parameters?._nearClipPlane?.Or(0.1f) ?? 0.1f;
        Camera.farClipPlane = parameters?._farClipPlane?.Or(2000f) ?? 2000f;
    }

    private static T Current<T>(Track<T> track, int frame) where T : Key
    {
        var keys = GachaCutInLoader.Keys(track);
        for (int i = keys.Length - 1; i >= 0; --i)
            if (keys[i]._frame <= frame)
                return keys[i];
        return null;
    }

    public void Render()
    {
        if (Camera.enabled)
            Camera.Render();
    }

    public void Dispose()
    {
        Camera.enabled = false;
        Camera.targetTexture = null;
        foreach (var receiver in receivers)
            receiver.Restore();
        texture.Release();
        Object.Destroy(texture);
        Object.Destroy(controller);
        Object.Destroy(animator.gameObject);
        Object.Destroy(root);
    }

    private sealed class Receiver
    {
        private readonly Renderer renderer;
        private readonly MaterialPropertyBlock saved = new MaterialPropertyBlock();
        private readonly int layer;
        private readonly Camera authoredCamera;
        private readonly bool authoredCameraEnabled;

        public Receiver(MonitorCamera marker, RenderTexture texture)
        {
            renderer = marker.GetComponent<Renderer>();
            layer = renderer.gameObject.layer;
            renderer.gameObject.layer = GachaMaskCamera.OverlayLayer;
            authoredCamera = marker.targetCamera ?? marker.GetComponent<Camera>();
            if (authoredCamera)
            {
                authoredCameraEnabled = authoredCamera.enabled;
                authoredCamera.enabled = false;
            }
            renderer.GetPropertyBlock(saved);
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetTexture(MainTexture, texture);
            renderer.SetPropertyBlock(properties);
        }

        public void Restore()
        {
            if (renderer)
            {
                renderer.SetPropertyBlock(saved);
                renderer.gameObject.layer = layer;
            }
            if (authoredCamera)
                authoredCamera.enabled = authoredCameraEnabled;
        }
    }
}
