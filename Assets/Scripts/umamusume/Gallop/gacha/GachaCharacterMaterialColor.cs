using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Gallop.CutIn.Cutt.CutInTimelineWorkSheet;

internal sealed class GachaCharacterMaterialColor
{
    private static readonly int SpecularColorId = Shader.PropertyToID("_SpecularColor");
    private static readonly int SpecularPowerId = Shader.PropertyToID("_SpecularPower");
    private const int NoseThreshold = 1, NoseVisibility = 2, Cylinder = 3;
    private static readonly Detail[] FaceDetails =
    {
        new Detail("_CheekPretenseThreshold", .775f, key => key.CheekPretenseThreshold),
        new Detail("_NosePretenseThreshold", .775f, key => key.NosePretenseThreshold),
        new Detail("_NoseVisibility", 0f, null),
        new Detail("_CylinderBlend", 0f, key => key.CylinderBlend),
        new Detail("_HairNormalBlend", 1f, key => key.HairNormalBlend)
    };

    private static readonly Detail[] Details =
    {
        new Detail("_ToonStep", .5f, key => key.ToonStep),
        new Detail("_ToonFeather", .0001f, key => key.ToonFeather),
        new Detail("_EnvRate", .5f, key => key.EnvRate),
        new Detail("_EnvBias", 1f, key => key.EnvBias),
        new Detail("_Silhouette", 0f, key => key.Silhouette, key => key.EyeSilhouette),
        new Detail("_Saturation", 1f, key => key.Saturation, key => key.EyeSaturation)
    };

    private readonly CharacterColor[] keys;
    private readonly Detail[] details;
    private readonly DetailState[] states;
    private readonly float[] faceValues = new float[FaceDetails.Length];
    private readonly Renderer[] renderers;
    private readonly Slot[][] slots;
    private readonly List<Material> activeMaterials = new List<Material>();
    private readonly bool hasShaderVariants;
    private int sampledFrame = -1, nextKey;
    private bool dirty, restoreColorValue, restorePowerValue;
    private bool hasFaceSample;
    private bool specularColorWritten, specularPowerWritten;
    private Color? specularColor;
    private float? specularPower;

    public static GachaCharacterMaterialColor Create(Renderer[] renderers, CharacterColor[] keys, bool hasShaderVariants)
    {
        var details = Details.Where(detail => keys.Any(key => detail.Field(key)?._isValid == true ||
            detail.EyeField?.Invoke(key)?._isValid == true)).ToArray();
        bool color = keys.Any(key => key.SpecularColor?._isValid == true || key.RestoreSpecularColor?._isValid == true);
        bool power = keys.Any(key => key.SpecularPower?._isValid == true || key.RestoreSpecularPower?._isValid == true);
        return keys.Length != 0
            ? new GachaCharacterMaterialColor(renderers, keys, details, color, power, hasShaderVariants)
            : null;
    }

    private GachaCharacterMaterialColor(Renderer[] renderers, CharacterColor[] keys, Detail[] details,
        bool color, bool power, bool hasShaderVariants)
    {
        this.renderers = renderers;
        this.keys = keys;
        this.details = details;
        this.hasShaderVariants = hasShaderVariants;
        states = new DetailState[details.Length];
        bool hasHair = renderers.Any(renderer => renderer.name == "M_Hair");
        slots = renderers.Select(renderer => renderer.materials.Select((material, index) =>
        {
            // These are the four material groups cached by ModelController for specular restore.
            bool specular = renderer.name is "M_Face" or "M_Body" ||
                index == 0 && (renderer.name is "M_Hair" or "M_Tail");
            return new Slot(material, details, GetFacePart(renderer.name, material, index, hasHair),
                specular && color, specular && power);
        }).ToArray()).ToArray();
    }

    private enum FacePart { None, Skin, Hair, Mayu, Eye }

    private static FacePart GetFacePart(string renderer, Material material, int index, bool hasHair)
    {
        if (!material)
            return FacePart.None;
        if (renderer == "M_Eye" || material.shader != null && material.shader.name.Contains("ToonEye", StringComparison.Ordinal))
            return FacePart.Eye;

        if (renderer == "M_Face" &&
            material.name.Replace(" (Instance)", "").EndsWith("_face", StringComparison.Ordinal))
            return FacePart.Skin;
        if (index == 0 && hasHair && renderer.Contains("M_Face_Prop", StringComparison.Ordinal))
            return FacePart.Skin;
        if (index == 0 && (renderer == "M_Hair" || renderer.Contains("M_Hair_Prop", StringComparison.Ordinal)))
            return FacePart.Hair;
        if (Gallop.Live.LiveModelRenderQueue.TryAlpha(renderer, out _, out var part) &&
            part == Gallop.Live.LiveModelRenderQueue.Part.Hair && index < 2)
            return FacePart.Hair;
        return renderer == "M_Mayu" && index == 0 ? FacePart.Mayu : FacePart.None;
    }

    public void Sample(int frame)
    {
        if (frame < sampledFrame)
        {
            Restore();
            Array.Clear(states, 0, states.Length);
            sampledFrame = -1;
            nextKey = 0;
            restoreColorValue = restorePowerValue = false;
            specularColorWritten = specularPowerWritten = false;
            specularColor = null;
            specularPower = null;
            hasFaceSample = false;
        }

        // The validity edge and specular restore values depend on previous samples.
        // Advance the material state across skipped source frames, including a seek into a curve.
        for (int source = Mathf.Max(sampledFrame + 1, keys[0]._frame); source <= frame; source++)
        {
            while (nextKey < keys.Length && keys[nextKey]._frame <= source)
                nextKey++;
            var current = keys[nextKey - 1];
            var next = nextKey < keys.Length ? keys[nextKey] : null;
            bool interpolate = next?._params?.FirstOrDefault()?._interpolateType > 0;
            float weight = interpolate ? Interpolation.NativeWeightBetween(current, next, next._params, 0, source) : 0f;
            var policy = interpolate ? next : current;

            // The model reads the numeric facial sample even when the destination key is invalid.
            for (int i = 0; i < FaceDetails.Length; i++)
            {
                var detail = FaceDetails[i];
                float value = i == NoseVisibility
                    ? (faceValues[NoseThreshold] > 1e-6f ? 1f : 0f)
                    : SampleFloat(detail.Field, detail.Default);
                dirty |= !hasFaceSample || faceValues[i] != value;
                faceValues[i] = value;
            }
            hasFaceSample = true;

            for (int i = 0; i < details.Length; i++)
            {
                var detail = details[i];
                ref var state = ref states[i];
                bool valid = detail.Field(policy)?._isValid == true;
                if (valid || state.Valid)
                {
                    float value = SampleFloat(detail.Field, detail.Default);
                    dirty |= state.Value != value;
                    state.Value = value;
                    if (detail.EyeField != null)
                        state.EyeValue = value;
                }
                state.Valid = valid;
                if (detail.EyeField?.Invoke(policy)?._isValid == true)
                    state.EyeValue = Mathf.Clamp01(SampleFloat(detail.EyeField, detail.Default));
            }

            // KeyBool interpolation leaves the scratch value intact. Only a direct
            // sample copies the value. During interpolation Power supplies the restore validity bit.
            if (!interpolate)
            {
                restoreColorValue = current.RestoreSpecularColor is { _isValid: true, _value: true };
                restorePowerValue = current.RestoreSpecularPower is { _isValid: true, _value: true };
            }
            if (restoreColorValue && policy.RestoreSpecularColor?._isValid == true)
                SetSpecularColor(null);
            else if (policy.SpecularColor?._isValid == true)
                SetSpecularColor(interpolate
                    ? Color.Lerp(current.SpecularColor?._value ?? default, policy.SpecularColor._value, weight)
                    : policy.SpecularColor._value);

            var powerRestoreValidity = interpolate ? policy.SpecularPower?._isValid : policy.RestoreSpecularPower?._isValid;
            if (restorePowerValue && powerRestoreValidity == true)
                SetSpecularPower(null);
            else if (policy.SpecularPower?._isValid == true)
                SetSpecularPower(interpolate
                    ? Mathf.Lerp(current.SpecularPower?._value ?? 0f, policy.SpecularPower._value, weight)
                    : policy.SpecularPower._value);

            float SampleFloat(Func<CharacterColor, FloatValue> field, float fallback)
            {
                float from = field(current)?.Or(fallback) ?? fallback;
                return interpolate ? Mathf.Lerp(from, field(next)?.Or(fallback) ?? fallback, weight) : from;
            }
        }
        sampledFrame = frame;
    }

    private void SetSpecularColor(Color? value)
    {
        dirty |= !specularColorWritten || !Nullable.Equals(specularColor, value);
        specularColor = value;
        specularColorWritten = true;
    }

    private void SetSpecularPower(float? value)
    {
        dirty |= !specularPowerWritten || specularPower != value;
        specularPower = value;
        specularPowerWritten = true;
    }

    public float? GetOriginalFloat(int renderer, int slot, int property)
    {
        for (int i = 0; i < details.Length; i++)
            if (details[i].Id == property)
                return slots[renderer][slot].OriginalFloats[i];
        return null;
    }

    public bool TryGetEyeValue(int property, float fallback, out float value)
    {
        for (int i = 0; i < details.Length; i++)
        {
            if (details[i].Id != property || details[i].EyeField == null)
                continue;
            value = states[i].EyeValue ?? fallback;
            return true;
        }
        value = 0f;
        return false;
    }

    public void Apply()
    {
        if (dirty || hasShaderVariants)
            ApplyToMaterials(restore: false);
        dirty = false;
    }

    public void Restore() => ApplyToMaterials(restore: true);

    private void ApplyToMaterials(bool restore)
    {
        for (int r = 0; r < renderers.Length; r++)
        {
            if (hasShaderVariants)
                renderers[r].GetSharedMaterials(activeMaterials);
            for (int i = 0; i < slots[r].Length; i++)
            {
                var slot = slots[r][i];
                Write(slot.OriginalMaterial, slot);
                // Mapping and hologram materials can be cached across changes to the base shader.
                if (hasShaderVariants && i < activeMaterials.Count && activeMaterials[i] != slot.OriginalMaterial)
                    Write(activeMaterials[i], slot);
            }
        }

        void Write(Material material, Slot slot)
        {
            if (!material)
                return;
            for (int i = 0; i < details.Length; i++)
            {
                float? value = restore ? slot.OriginalFloats[i] : states[i].Value;
                if (value.HasValue && material.HasProperty(details[i].Id))
                    material.SetFloat(details[i].Id, value.Value);
            }
            if (slot.OriginalFaceFloats != null && hasFaceSample)
                for (int i = 0; i < FaceDetails.Length; i++)
                {
                    var original = slot.OriginalFaceFloats[i];
                    if (original.HasValue && material.HasProperty(FaceDetails[i].Id))
                        material.SetFloat(FaceDetails[i].Id, restore ? original.Value : faceValues[i]);
                }
            if (slot.OriginalSpecularColor.HasValue && (restore || specularColorWritten) && material.HasProperty(SpecularColorId))
                material.SetColor(SpecularColorId, (restore ? null : specularColor) ?? slot.OriginalSpecularColor.Value);
            if (slot.OriginalSpecularPower.HasValue && (restore || specularPowerWritten) && material.HasProperty(SpecularPowerId))
                material.SetFloat(SpecularPowerId, (restore ? null : specularPower) ?? slot.OriginalSpecularPower.Value);
        }
    }

    private readonly struct Detail
    {
        public readonly int Id;
        public readonly float Default;
        public readonly Func<CharacterColor, FloatValue> Field, EyeField;

        public Detail(string property, float fallback, Func<CharacterColor, FloatValue> field,
            Func<CharacterColor, FloatValue> eyeField = null)
        {
            Id = Shader.PropertyToID(property);
            Default = fallback;
            Field = field;
            EyeField = eyeField;
        }
    }

    private struct DetailState
    {
        public bool Valid;
        public float? Value, EyeValue;
    }

    private sealed class Slot
    {
        public readonly Material OriginalMaterial;
        public readonly float?[] OriginalFloats;
        public readonly float?[] OriginalFaceFloats;
        public readonly Color? OriginalSpecularColor;
        public readonly float? OriginalSpecularPower;

        public Slot(Material material, Detail[] details, FacePart facePart, bool color, bool power)
        {
            OriginalMaterial = material;
            OriginalFloats = new float?[details.Length];
            if (!material)
                return;
            for (int i = 0; i < details.Length; i++)
                if (material.HasProperty(details[i].Id))
                    OriginalFloats[i] = material.GetFloat(details[i].Id);
            if (facePart != FacePart.None)
            {
                OriginalFaceFloats = new float?[FaceDetails.Length];
                for (int i = 0; i < FaceDetails.Length; i++)
                {
                    if ((facePart is FacePart.Mayu or FacePart.Eye) && i != Cylinder || i == NoseVisibility && facePart != FacePart.Skin)
                        continue;
                    int property = FaceDetails[i].Id;
                    if (material.HasProperty(property))
                        OriginalFaceFloats[i] = material.GetFloat(property);
                }
            }
            if (color && material.HasProperty(SpecularColorId))
                OriginalSpecularColor = material.GetColor(SpecularColorId);
            if (power && material.HasProperty(SpecularPowerId))
                OriginalSpecularPower = material.GetFloat(SpecularPowerId);
        }
    }
}
