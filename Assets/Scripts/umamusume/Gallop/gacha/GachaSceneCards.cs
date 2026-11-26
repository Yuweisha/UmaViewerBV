using System;
using System.Collections.Generic;
using Gallop;
using UnityEngine;
using UnityEngine.Rendering;
using static Gallop.CutIn.Cutt.CutInTimelineWorkSheet;
using Object = UnityEngine.Object;

internal sealed class GachaSceneCards : IDisposable
{
    private static readonly int MaskTextureId = Shader.PropertyToID("_MaskTex");
    private static readonly Vector3 ThumbnailScale = new Vector3(.77f, 1f, 1f) * .68f;
    private static readonly Vector3 FullCardScale = new Vector3(.4725f, .63f, .35f);

    private readonly Transform root;
    private readonly Slot[] slots;

    public static GachaSceneCards Create(Transform parent, SceneCardGroup[] groups,
        IReadOnlyList<(int Id, Texture2D Texture, bool Thumbnail)> art, Texture2D thumbMask, Texture2D fullMask)
    {
        var shader = ShaderManager.GetShader(ShaderManager.ShaderKinds.SpriteCutoff);
        return shader ? new GachaSceneCards(parent, groups, art, thumbMask, fullMask, shader) : null;
    }

    private GachaSceneCards(Transform parent, SceneCardGroup[] groups,
        IReadOnlyList<(int Id, Texture2D Texture, bool Thumbnail)> art, Texture2D thumbMask, Texture2D fullMask, Shader shader)
    {
        root = new GameObject("CutIn scene cards").transform;
        root.SetParent(parent, false);
        // SkillCutInSupportCard's prefab root rotates its authored slot space by 180 degrees.
        root.localRotation = Quaternion.Euler(0f, 180f, 0f);
        var quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        slots = new Slot[art.Count];
        for (int i = 0; i < slots.Length; i++)
        {
            var (id, texture, thumbnail) = art[i];
            var instance = new GameObject($"CutIn scene card {id}", typeof(MeshFilter), typeof(MeshRenderer));
            instance.SetActive(false);
            instance.layer = GachaCutInLoader.PreviewLayer;
            instance.transform.SetParent(root, false);
            instance.GetComponent<MeshFilter>().sharedMesh = quad;
            var material = new Material(shader) { mainTexture = texture };
            var renderer = instance.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = material;
            slots[i] = new Slot(instance, material, thumbnail ? thumbMask : fullMask,
                thumbnail ? ThumbnailScale : FullCardScale, GachaCutInLoader.Keys(groups[i]?._keys));
        }
    }

    public void Sample(int frame)
    {
        foreach (var slot in slots)
        {
            var keys = slot.Keys;
            int index = 0;
            while (index < keys.Length && keys[index]._frame <= frame)
                index++;
            var current = index > 0 ? keys[index - 1] : null;
            slot.Instance.SetActive(current?.IsVisible == true);
            if (!slot.Instance.activeSelf)
                continue;

            var next = index < keys.Length ? keys[index] : current;
            var tween = next != current && next._params is { Length: > 0 } ? next._params[0] : null;
            float weight = tween?._interpolateType > 0 ? tween.WeightBetween(current, next, frame) : 0f;
            var rotation = Quaternion.SlerpUnclamped(Quaternion.Euler(current.Angle), Quaternion.Euler(next.Angle), weight);
            slot.Instance.transform.SetLocalPositionAndRotation(
                Vector3.LerpUnclamped(current.Position, next.Position, weight), rotation);
            slot.Instance.transform.localScale = Vector3.Scale(slot.BaseScale,
                Vector3.LerpUnclamped(current.Scale, next.Scale, weight));
            slot.Material.SetTexture(MaskTextureId, current.UseMask && slot.Mask ? slot.Mask : Texture2D.whiteTexture);
        }
    }

    public void Dispose()
    {
        root.gameObject.SetActive(false);
        foreach (var slot in slots)
        {
            Object.Destroy(slot.Material);
        }
        Object.Destroy(root.gameObject);
    }

    private readonly struct Slot
    {
        public readonly GameObject Instance;
        public readonly Material Material;
        public readonly Texture2D Mask;
        public readonly Vector3 BaseScale;
        public readonly SceneCardKey[] Keys;

        public Slot(GameObject instance, Material material, Texture2D mask, Vector3 baseScale, SceneCardKey[] keys)
        {
            Instance = instance;
            Material = material;
            Mask = mask;
            BaseScale = baseScale;
            Keys = keys;
        }
    }
}
