using System;
using Gallop;
using UnityEngine;
using UnityEngine.Rendering;
using static Gallop.CutIn.Cutt.CutInTimelineWorkSheet;
using Object = UnityEngine.Object;

internal sealed class GachaMaskCamera : IDisposable
{
    public const int OverlayLayer = 31;
    public const int ActorLayer = 27;
    public const int EffectLayer = 28;
    public const int ActorLayers = (1 << ActorLayer) | (1 << EffectLayer);
    private static readonly int MaskTexture = Shader.PropertyToID("_MaskTex");
    private static readonly int Alpha = Shader.PropertyToID("_Alpha");
    private static readonly int Cutoff = Shader.PropertyToID("_Cutoff");
    private readonly Camera main;
    private readonly Transform plane;
    private readonly GameObject root;
    private readonly RenderTexture target;
    private readonly Material material;
    private readonly MaskCameraTrack track;

    public Camera Camera { get; }

    public static GachaMaskCamera Create(Camera main, Transform parent, MaskCameraTrack track)
    {
        if (track?.thisList is not { Length: > 0 })
            return null;
        Shader shader = ShaderManager.GetShader(ShaderManager.ShaderKinds.SpriteCutoff);
        return shader ? new GachaMaskCamera(main, parent, track, shader) : null;
    }

    private GachaMaskCamera(Camera main, Transform parent, MaskCameraTrack track, Shader shader)
    {
        this.main = main;
        this.track = track;
        root = new GameObject("CutIn camera mask");
        root.transform.SetParent(parent, false);
        root.SetActive(false);

        var cameraObject = new GameObject("Mask camera");
        cameraObject.transform.SetParent(root.transform, false);
        Camera = cameraObject.AddComponent<Camera>();
        Camera.CopyFrom(main);
        Camera.clearFlags = CameraClearFlags.SolidColor;
        Camera.backgroundColor = new Color(1f, 1f, 1f, 0f);
        Camera.nearClipPlane = 0.3f;
        Camera.farClipPlane = 1000f;
        Camera.orthographic = false;
        Camera.depth = main.depth - 1f;
        Camera.cullingMask = ActorLayers;
        Camera.allowMSAA = false;
        Camera.allowHDR = false;
        Camera.enabled = true;
        // Visual enhancement for enlarged mask-camera images in high-resolution output.
        // Authored 512x512 inputs match JP data; this is a quality override, not a data fix.
        bool upscale = track.MaskWidth == 512 && track.MaskHeight == 512;
        target = new RenderTexture(upscale ? 2048 : track.MaskWidth, upscale ? 2048 : track.MaskHeight, 24);
        target.Create();
        Camera.targetTexture = target;
        Camera.aspect = (float)track.MaskWidth / track.MaskHeight;

        var quad = new GameObject("Mask", typeof(MeshFilter), typeof(MeshRenderer));
        quad.layer = OverlayLayer;
        plane = quad.transform;
        plane.SetParent(root.transform, false);
        quad.GetComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        var renderer = quad.GetComponent<MeshRenderer>();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        material = new Material(shader);
        material.mainTexture = target;
        renderer.sharedMaterial = material;
    }

    public void Sample(int frame)
    {
        var keys = track.thisList;
        int index = 0;
        while (index < keys.Length && keys[index]._frame <= frame)
            index++;
        var current = index > 0 ? keys[index - 1] : null;
        root.SetActive(current?.IsEnable == true);
        if (!root.activeSelf)
            return;

        var next = index < keys.Length ? keys[index] : current;
        var settings = next._params is { Length: > 0 } ? next._params[0] : null;
        float blend = next != current && settings?._interpolateType > 0
            ? settings.WeightBetween(current, next, frame) : 0f;
        Camera.fieldOfView = Mathf.Lerp(current.FieldOfView, next.FieldOfView, blend);
        Camera.transform.SetLocalPositionAndRotation(
            Vector3.Lerp(current.CameraPosition, next.CameraPosition, blend),
            Quaternion.Slerp(Quaternion.Euler(current.CameraAngle), Quaternion.Euler(next.CameraAngle), blend));
        plane.SetPositionAndRotation(main.transform.TransformPoint(Vector3.Lerp(current.Position, next.Position, blend)),
            main.transform.rotation * Quaternion.Slerp(Quaternion.Euler(current.Angle), Quaternion.Euler(next.Angle), blend));
        plane.localScale = Vector3.Lerp(current.Scale, next.Scale, blend);
        material.renderQueue = current.RenderQueue;
        material.SetTexture(MaskTexture, current.MaskTexture ? current.MaskTexture : track.MaskTexture);
        material.SetFloat(Alpha, Mathf.Lerp(current.Alpha, next.Alpha, blend));
        material.SetFloat(Cutoff, Mathf.Lerp(current.Cuttoff, next.Cuttoff, blend));
    }

    public void Render()
    {
        if (root.activeSelf)
            Camera.Render();
    }

    public void Dispose()
    {
        root.SetActive(false);
        Camera.targetTexture = null;
        target.Release();
        Object.Destroy(target);
        Object.Destroy(material);
        Object.Destroy(root);
    }
}
