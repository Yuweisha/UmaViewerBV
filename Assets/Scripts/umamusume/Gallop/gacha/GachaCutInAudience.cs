using System;
using System.Collections.Generic;
using System.Linq;
using Gallop;
using Gallop.CutIn;
using Gallop.CutIn.Cutt;
using Gallop.Live;
using Gallop.Live.Cutt;
using UnityEngine;
using static Gallop.CutIn.Cutt.CutInTimelineWorkSheet;

internal sealed class GachaCutInAudience : IDisposable
{
    private readonly List<(LiveAudienceController Player, AudienceKey[] Keys)> entries =
        new List<(LiveAudienceController, AudienceKey[])>();
    private readonly List<Crowd> crowds = new List<Crowd>();
    private readonly CyalumeKey[] choreography;
    private readonly CrowdGroup[] cyalumeGroups, mobGroups;

    public GachaCutInAudience(IEnumerable<GameObject> backgrounds, CutInTimelineWorkSheet sheet)
    {
        choreography = GachaCutInLoader.Keys(sheet.ChoreographyCyalumeList?.FirstOrDefault()?._keys);
        cyalumeGroups = sheet.CyalumeControlList ?? Array.Empty<CrowdGroup>();
        mobGroups = sheet.MobControlList ?? Array.Empty<CrowdGroup>();
        foreach (var background in backgrounds)
        {
            foreach (var model in background.GetComponentsInChildren<CutInBgModel>(true))
            foreach (var track in sheet.AudienceList ?? Array.Empty<AudienceTrack>())
            {
                var keys = GachaCutInLoader.Keys(track._keys);
                var prefab = model.GetAudiencePrefab(track.ObjectIndex);
                if (keys.Length == 0 || !prefab)
                    continue;
                var clips = model.AudienceAnimationArray;
                var audience = new LiveAudienceController(prefab, model.transform, clips is { Length: > 0 } ? clips : null, false);
                foreach (var renderer in audience.Instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    renderer.forceMatrixRecalculationPerRender = true;
                GachaCutInLoader.SetLayer(audience.Instance);
                audience.Instance.SetActive(false);
                entries.Add((audience, keys));
            }
            if (choreography.Length == 0)
                continue;
            foreach (var holder in background.GetComponentsInChildren<AssetHolder>(true))
            {
                var cyalume = holder.Get<GameObject>("random");
                var mob = holder.Get<GameObject>("mob");
                if (cyalume || mob)
                    crowds.Add(new Crowd(holder.transform, cyalume, mob));
            }
        }
    }

    public void Sample(int frame, float sampleFrame)
    {
        foreach (var (player, keys) in entries)
        {
            GachaCutInPlayer.GetKeyInterval(keys, frame, out var current, out var next);
            if (current == null || (current._attribute & 65536) == 0)
            {
                player.Instance.SetActive(false);
                continue;
            }
            var interpolation = next?._params?.Length > 0 ? next._params[0] : null;
            float weight = interpolation?._interpolateType > 0 ? interpolation.WeightBetween(current, next, frame) : 0f;
            next ??= current;
            player.UpdateInfo(new LiveAudienceTimeline.State
            {
                RenderEnabled = true,
                ProgressTime = (frame - current._frame) / 30f,
                Rotation = Quaternion.LerpUnclamped(Quaternion.Euler(current.Rotate), Quaternion.Euler(next.Rotate), weight),
                Values = new LiveTimelineKeyAudienceData
                {
                    position = Vector3.LerpUnclamped(current.Position, next.Position, weight),
                    scale = Vector3.LerpUnclamped(current.Scale, next.Scale, weight),
                    cyalumeColor = Color.LerpUnclamped(current.CyalumeColor, next.CyalumeColor, weight),
                    cyalumeGlowColor = Color.LerpUnclamped(current.CyalumeGlowColor, next.CyalumeGlowColor, weight),
                    cyalumeGlowColorPower = Mathf.LerpUnclamped(current.CyalumeGlowColorPower, next.CyalumeGlowColorPower, weight),
                    cyalumeMaskRadius = Mathf.LerpUnclamped(current.CyalumeMaskRadius, next.CyalumeMaskRadius, weight),
                    animationSetting = current.AnimationSetting,
                    animationRootIndex = current.AnimationRootIndex,
                    animationBodyRegion = current.AnimationBodyRegion,
                    animationCategory = current.AnimationCategory,
                    animationIndex = current.AnimationIndex,
                    animationWrapMode = current.AnimationWrapMode,
                    animationSpeed = Mathf.LerpUnclamped(current.AnimationSpeed, next.AnimationSpeed, weight),
                    animationOffsetTime = current.AnimationOffsetTime
                }
            }, updateAudienceLighting: false);
        }
        if (crowds.Count == 0)
            return;
        GachaCutInPlayer.GetKeyInterval(choreography, frame, out var key, out _);
        foreach (var crowd in crowds)
            crowd.Sample(key, cyalumeGroups, mobGroups, entries, frame, sampleFrame);
    }

    public void Dispose()
    {
        foreach (var (player, _) in entries)
        {
            player.Instance.SetActive(false);
            player.Dispose();
        }
        entries.Clear();
        foreach (var crowd in crowds)
            crowd.Dispose();
        crowds.Clear();
    }

    private sealed class Crowd
    {
        private const int PatternFrames = 32;
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int MainTexStId = Shader.PropertyToID("_MainTex_ST");
        private static readonly int CyalumeMatrixId = Shader.PropertyToID("_CyalumeGroupMatrix");
        private static readonly int MobMatrixId = Shader.PropertyToID("_MobGroupMatrix");
        private static readonly int CyalumeMaskId = Shader.PropertyToID("_CyalumeMaskPosArray");
        private static readonly int MobMaskId = Shader.PropertyToID("_MaskPosArray");
        private readonly Renderer[] cyalume, mob;
        private readonly GameObject cyalumeInstance, mobInstance;
        private readonly Transform host;
        private readonly Matrix4x4[] matrices = new Matrix4x4[11];
        private readonly Vector4[] masks = new Vector4[10];
        private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();

        public Crowd(Transform host, GameObject cyalumePrefab, GameObject mobPrefab)
        {
            this.host = host;
            cyalumeInstance = Spawn(cyalumePrefab, host);
            mobInstance = Spawn(mobPrefab, host);
            cyalume = cyalumeInstance ? cyalumeInstance.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>();
            mob = mobInstance ? mobInstance.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>();
        }

        private static GameObject Spawn(GameObject prefab, Transform parent)
        {
            if (!prefab)
                return null;
            var instance = UnityEngine.Object.Instantiate(prefab, parent, false);
            GachaCutInLoader.SetLayer(instance);
            instance.SetActive(true);
            return instance;
        }

        public void Sample(CyalumeKey key, CrowdGroup[] cyalumeGroups, CrowdGroup[] mobGroups,
            List<(LiveAudienceController Player, AudienceKey[] Keys)> audience, int frame, float sampleFrame)
        {
            Array.Clear(masks, 0, masks.Length);
            int index = 0;
            foreach (var (player, _) in audience)
            {
                if (index == masks.Length)
                    break;
                if (player.Instance.activeInHierarchy && player.Instance.transform.IsChildOf(host))
                    masks[index++] = player.WorldPosition;
            }
            var texture = key?.CyalumeTexture;
            float textureOffset = key == null ? 0f : 1f - Mathf.Floor(Mathf.Repeat(
                (sampleFrame - key._frame) / 30f * (key.Bpm / 180f) * PatternFrames, PatternFrames)) / PatternFrames;
            WriteMatrices(cyalumeGroups, frame, sampleFrame);
            WriteRenderers(cyalume, CyalumeMatrixId, CyalumeMaskId, texture, textureOffset);
            WriteMatrices(mobGroups, frame, sampleFrame);
            WriteRenderers(mob, MobMatrixId, MobMaskId, null, 0f);
        }

        private void WriteMatrices(CrowdGroup[] groups, int frame, float sampleFrame)
        {
            for (int i = 0; i < matrices.Length; i++)
                matrices[i] = Matrix4x4.identity;
            foreach (var group in groups)
            {
                var track = group._keys;
                if (track == null || (uint)track._groupId >= matrices.Length)
                    continue;
                GachaCutInPlayer.GetKeyInterval(GachaCutInLoader.Keys(track), frame, out var from, out var to);
                if (from == null)
                    continue;
                var interpolation = to?._params?.FirstOrDefault();
                float weight = interpolation?._interpolateType > 0 ? interpolation.WeightBetween(from, to, sampleFrame) : 0f;
                to ??= from;
                matrices[track._groupId] = Matrix4x4.TRS(
                    Vector3.LerpUnclamped(from.Position, to.Position, weight),
                    Quaternion.SlerpUnclamped(Quaternion.Euler(from.Angle), Quaternion.Euler(to.Angle), weight),
                    Vector3.LerpUnclamped(from.Scale, to.Scale, weight));
            }
        }

        private void WriteRenderers(Renderer[] renderers, int matrixId, int maskId, Texture2D texture, float offset)
        {
            foreach (var renderer in renderers)
            {
                renderer.GetPropertyBlock(block);
                block.SetMatrixArray(matrixId, matrices);
                block.SetVectorArray(maskId, masks);
                if (texture)
                {
                    block.SetTexture(MainTexId, texture);
                    var scale = renderer.sharedMaterial.GetTextureScale(MainTexId);
                    block.SetVector(MainTexStId, new Vector4(scale.x, scale.y, 0f, offset));
                }
                renderer.SetPropertyBlock(block);
            }
        }

        public void Dispose()
        {
            if (cyalumeInstance)
                UnityEngine.Object.Destroy(cyalumeInstance);
            if (mobInstance)
                UnityEngine.Object.Destroy(mobInstance);
        }
    }
}
