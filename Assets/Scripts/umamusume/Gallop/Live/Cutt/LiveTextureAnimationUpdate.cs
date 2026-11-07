using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    // Info layout mirrored from StageController.UpdateMaterialTextureAnimaionCommon
    // 0x4eba3d0: +0 elapsedTime, +0x10 textureName, +0x18 useMainTexture,
    // +0x1c startU, +0x20 startV, +0x24 scaleU, +0x28 scaleV, +0x2c speedU,
    // +0x30 speedV, +0x34 duration.
    public struct TextureAnimationUpdateInfo
    {
        public float ElapsedTime;
        public string TextureName;
        public bool UseMainTexture;
        public float StartU, StartV;
        public float ScaleU, ScaleV;
        public float SpeedU, SpeedV;
        public float Duration;
        public int LabelId;
        public int TextureId;
    }

    // Managed reconstruction of the native material update body. The native code
    // computes frame = trunc(elapsed*60/duration) and repeats (start + frame*speed)
    // into [0,1); scale is the authored scale pair. useMainTexture selects
    // mainTextureScale/Offset instead of named SetTextureScale/Offset.
    public static class LiveTextureAnimationUpdate
    {
        public static void Apply(Material material, in TextureAnimationUpdateInfo info)
        {
            if (material == null) return;
            int frame = info.Duration != 0f ? (int)(info.ElapsedTime * 60f / info.Duration) : 0;
            float u = Mathf.Repeat(info.StartU + frame * info.SpeedU, 1f);
            float v = Mathf.Repeat(info.StartV + frame * info.SpeedV, 1f);
            Vector2 scale = new Vector2(info.ScaleU, info.ScaleV);
            Vector2 offset = new Vector2(u, v);
            if (!info.UseMainTexture)
            {
                if (!string.IsNullOrEmpty(info.TextureName))
                {
                    material.SetTextureScale(info.TextureName, scale);
                    material.SetTextureOffset(info.TextureName, offset);
                }
                else
                {
                    material.mainTextureScale = scale;
                    material.mainTextureOffset = offset;
                }
                return;
            }
            material.mainTextureScale = scale;
            material.mainTextureOffset = offset;
        }
    }
}

namespace Gallop.Live.Cutt
{
    // Dispatch reconstruction of StageController.UpdateTextureAnimation 0x4ebeb90:
    // 1) _instanceMaterialDictionary[labelId] hit -> update every material in the list,
    //    even when the list is empty (native returns immediately);
    // 2) otherwise wash-light list indexed by LabelToIDDictionary.getID(labelId);
    // 3) otherwise foot-light list through UpdateMaterialTextureAnimaion 0x4eba610,
    //    which resolves the same label id a second time;
    // 4) otherwise the movie-material fallback 0x4ebcd00.
    public static class LiveTextureAnimationConsumer
    {
        public static void Dispatch(int labelId, in TextureAnimationUpdateInfo info,
            Dictionary<int, List<Material>> instanceMaterialDictionary,
            IList<Material> washLightMaterials, IList<Material> footLightMaterials,
            Func<int, int?> resolveLabelIndex, Action<TextureAnimationUpdateInfo> movieFallback)
            => Dispatch(labelId, info, instanceMaterialDictionary, washLightMaterials,
                footLightMaterials, resolveLabelIndex, resolveLabelIndex, movieFallback);

        public static void Dispatch(int labelId, in TextureAnimationUpdateInfo info,
            Dictionary<int, List<Material>> instanceMaterialDictionary,
            IList<Material> washLightMaterials, IList<Material> footLightMaterials,
            Func<int, int?> resolveWashIndex, Func<int, int?> resolveFootIndex,
            Action<TextureAnimationUpdateInfo> movieFallback)
        {
            if (instanceMaterialDictionary == null) throw new NullReferenceException("stage instance material dictionary");
            if (instanceMaterialDictionary.TryGetValue(labelId, out var list))
            {
                if (list == null) return;
                for (int i = 0; i < list.Count; i++) LiveTextureAnimationUpdate.Apply(list[i], info);
                return;
            }
            int? washIndex = resolveWashIndex?.Invoke(labelId);
            if (washLightMaterials != null && washIndex.HasValue && washIndex.Value >= 0 &&
                washIndex.Value < washLightMaterials.Count)
            {
                LiveTextureAnimationUpdate.Apply(washLightMaterials[washIndex.Value], info);
                return;
            }
            int? footIndex = resolveFootIndex?.Invoke(labelId);
            if (footLightMaterials != null && footIndex.HasValue && footIndex.Value >= 0 &&
                footIndex.Value < footLightMaterials.Count)
            {
                LiveTextureAnimationUpdate.Apply(footLightMaterials[footIndex.Value], info);
                return;
            }
            movieFallback?.Invoke(info);
        }

        // Original UpdateMaterialTextureAnimaion 0x4eba610: materials or label
        // dictionary null -> false; label id resolved through LabelToIDDictionary;
        // out-of-range index -> false; otherwise update that array slot and return true.
        public static bool UpdateMaterialTextureAnimaion(IList<Material> materials,
            in TextureAnimationUpdateInfo info, Func<int, int?> resolveLabelIndex)
        {
            if (materials == null || resolveLabelIndex == null) return false;
            int? index = resolveLabelIndex(info.LabelId);
            if (!index.HasValue || index.Value < 0 || index.Value >= materials.Count) return false;
            LiveTextureAnimationUpdate.Apply(materials[index.Value], info);
            return true;
        }

        // Direct dictionary entry point, original UpdateMaterialTextureAnimaionDictionary
        // 0x4eba500: null dictionary throws; a miss returns false; a hit (even an empty
        // list) updates all materials and returns true.
        public static bool UpdateDictionary(in TextureAnimationUpdateInfo info,
            Dictionary<int, List<Material>> instanceMaterialDictionary)
        {
            if (instanceMaterialDictionary == null) throw new NullReferenceException("stage instance material dictionary");
            if (!instanceMaterialDictionary.TryGetValue(info.LabelId, out var list)) return false;
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++) LiveTextureAnimationUpdate.Apply(list[i], info);
            return true;
        }
    }
}

namespace Gallop.Live.Cutt
{
    // Producer reconstruction of AlterUpdate_TextureAnimationControl 0x4ee4500:
    // per group: attribute/play-mode/first-frame gates, key fetch, and when the
    // next key is interpolating, lerp scrollSpeed.x/y and scrollInterval only.
    // offset/tiling/textureName/useMain/textureID stay on the current key; time is
    // (frame - key.frame)/60.
    public static class LiveTextureAnimationProducer
    {
        // Original uses subss/mulss/addss in sequence; round after each operation so
        // editor/Mono extended precision cannot perturb the low bit.
        static float LerpFloat32(float a, float b, float t)
        {
            float delta = LiveTimelineControl.MonitorFloat32(b - a);
            float scaled = LiveTimelineControl.MonitorFloat32(delta * t);
            return LiveTimelineControl.MonitorFloat32(a + scaled);
        }

        public static bool Evaluate(LiveTimelineTextureAnimationData group, int frame,
            TimelinePlayerMode playMode, out TextureAnimationUpdateInfo info)
        {
            info = default;
            if (group == null || group.keys == null) return false;
            var keys = group.keys;
            if (keys.Count == 0 || keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable)) return false;
            if (!keys.EnablePlayModeTimeline(playMode)) return false;
            if (frame < keys.At(0).frame) return false;
            LiveTimelineControl.FindTimelineKey(out var cur, out var next, keys, frame);
            if (!(cur is LiveTimelineKeyTextureAnimationData current)) return false;
            var nextKey = next as LiveTimelineKeyTextureAnimationData;
            float speedU = current.scrollSpeed.x;
            float speedV = current.scrollSpeed.y;
            float duration = current.scrollInterval;
            if (nextKey != null && nextKey.IsInterpolateKey())
            {
                float t = LiveTimelineControl.CalculateInterpolationValue(current, nextKey, frame);
                speedU = LerpFloat32(current.scrollSpeed.x, nextKey.scrollSpeed.x, t);
                speedV = LerpFloat32(current.scrollSpeed.y, nextKey.scrollSpeed.y, t);
                duration = LerpFloat32(current.scrollInterval, nextKey.scrollInterval, t);
            }
            info = new TextureAnimationUpdateInfo
            {
                ElapsedTime = (frame - current.frame) / 60f,
                TextureName = current.textureName,
                UseMainTexture = current.textureNameEmpty,
                StartU = current.offset.x,
                StartV = current.offset.y,
                ScaleU = current.tiling.x,
                ScaleV = current.tiling.y,
                SpeedU = speedU,
                SpeedV = speedV,
                Duration = duration,
                LabelId = group.nameHash,
                TextureId = current.textureID,
            };
            return true;
        }
    }
}
