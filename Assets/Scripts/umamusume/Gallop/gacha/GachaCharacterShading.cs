using System;
using UnityEngine;
using static Gallop.CutIn.Cutt.CutInTimelineWorkSheet;
using static Gallop.ShaderManager.ShaderKinds;
using Kind = Gallop.ShaderManager.ShaderKinds;
using Object = UnityEngine.Object;

internal sealed class GachaCharacterShading : IDisposable
{
    private readonly HologramKey[] holograms;
    private readonly ScreenMappingKey[] mappings;
    private readonly RendererMaterials[] renderers;

    public GachaCharacterShading(Renderer[] renderers, HologramKey[] holograms, ScreenMappingKey[] mappings)
    {
        this.holograms = holograms;
        this.mappings = mappings;
        this.renderers = Array.ConvertAll(renderers, renderer => new RendererMaterials(renderer));
    }

    public void Sample(int frame, float time)
    {
        GachaCutInPlayer.GetKeyInterval(holograms, frame, out var holo, out var nextHolo);
        GachaCutInPlayer.GetKeyInterval(mappings, frame, out var mapping, out var nextMapping);
        float holoBlend = Blend(holo, nextHolo, nextHolo?._params, frame);
        float mappingBlend = Blend(mapping, nextMapping, nextMapping?._params, frame);
        foreach (var renderer in renderers)
            renderer.Sample(holo, nextHolo, holoBlend, mapping, nextMapping, mappingBlend, time);
    }

    private static float Blend(Key first, Key next, Interpolation[] parameters, int frame)
    {
        return first != null && next != null && parameters is { Length: > 0 } && parameters[0]._interpolateType > 0
            ? parameters[0].WeightBetween(first, next, frame) : 0f;
    }

    public void Dispose()
    {
        foreach (var renderer in renderers)
            renderer.Dispose();
    }

    private sealed class RendererMaterials
    {
        private readonly Renderer renderer;
        private readonly Material[] originals, materials, variants;
        private readonly Part[] parts;

        public RendererMaterials(Renderer renderer)
        {
            this.renderer = renderer;
            originals = renderer.sharedMaterials;
            materials = (Material[])originals.Clone();
            variants = new Material[originals.Length];
            parts = Array.ConvertAll(originals, material => PartFor(renderer, material));
        }

        public void Sample(HologramKey holo, HologramKey nextHolo, float holoBlend,
            ScreenMappingKey mapping, ScreenMappingKey nextMapping, float mappingBlend, float time)
        {
            bool changed = false;
            for (int index = 0; index < originals.Length; index++)
            {
                var original = originals[index];
                if (!original)
                    continue;
                var part = parts[index];
                var parameters = MappingFor(part, mapping);
                var material = original;
                if (holo?.IsEnable == true || parameters?.IsEnabled == true)
                {
                    var kind = holo?.IsEnable == true
                        ? holo.IsHologramOnly ? HologramToonOnly : HologramShader(part)
                        : MappingShader(part);
                    var shader = Gallop.ShaderManager.GetShader(kind);
                    if (shader)
                    {
                        material = variants[index] ??= new Material(original);
                        if (material.shader != shader)
                        {
                            material.shader = shader;
                            material.renderQueue = shader.renderQueue;
                        }
                        if (holo?.IsEnable == true)
                            ApplyHologram(material, holo, nextHolo ?? holo, holoBlend, time);
                        else
                            ApplyMapping(material, parameters, MappingFor(part, nextMapping) ?? parameters,
                                mappingBlend, mapping);
                    }
                }
                changed |= materials[index] != material;
                materials[index] = material;
            }
            if (changed)
                renderer.sharedMaterials = materials;
        }

        public void Dispose()
        {
            if (renderer)
                renderer.sharedMaterials = originals;
            foreach (var variant in variants)
                if (variant)
                    Object.Destroy(variant);
        }
    }

    private enum Part { Body, Face, Eye, Hair, Mayu, Tail, Attachment, Head }

    private static Part PartFor(Renderer renderer, Material material)
    {
        string shader = material && material.shader ? material.shader.name : string.Empty;
        if (shader.Contains("Mayu", StringComparison.OrdinalIgnoreCase)) return Part.Mayu;
        if (shader.Contains("Eye", StringComparison.OrdinalIgnoreCase)) return Part.Eye;
        if (shader.Contains("Face", StringComparison.OrdinalIgnoreCase)) return Part.Face;
        string name = renderer.name;
        if (name.Contains("tail", StringComparison.OrdinalIgnoreCase)) return Part.Tail;
        if (name.Contains("attach", StringComparison.OrdinalIgnoreCase)) return Part.Attachment;
        if (name.Contains("headobj", StringComparison.OrdinalIgnoreCase)) return Part.Head;
        if (shader.Contains("Hair", StringComparison.OrdinalIgnoreCase)) return Part.Hair;
        return Part.Body;
    }

    private static Kind HologramShader(Part part) => part switch
    {
        Part.Face => CharacterHologramToonFaceTSERA,
        Part.Eye => CharacterHologramToonEyeTA,
        Part.Hair => CharacterHologramToonHairTSERA,
        Part.Mayu => CharacterHologramToonMayuA,
        Part.Tail => CharacterHologramToonColorTSERA,
        _ => CharacterHologramToonTSERA
    };

    private static Kind MappingShader(Part part) => part switch
    {
        Part.Face => CharacterScreenMappingToonFaceTSER,
        Part.Eye => CharacterScreenMappingToonEyeT,
        Part.Hair => CharacterScreenMappingToonHairTSER,
        Part.Mayu => CharacterScreenMappingToonMayu,
        Part.Tail => CharacterScreenMappingToonColorTSER,
        _ => CharacterScreenMappingToonTSER
    };

    private static ScreenMappingParam MappingFor(Part part, ScreenMappingKey key) =>
        key == null ? null : part switch
        {
            Part.Body when key.UsesBodyParam => key.BodyParam,
            Part.Face when key.UsesFaceParam => key.FaceParam,
            Part.Eye or Part.Mayu when key.UsesEyeParam => key.EyeParam,
            Part.Hair when key.UsesHairParam => key.HairParam,
            Part.Tail when key.UsesTailParam => key.TailParam,
            Part.Attachment when key.UsesAttachmentParam => key.AttachmentParam,
            Part.Head when key.UsesHeadObjParam => key.HeadObjParam,
            _ => key.AllParam
        };

    private static void ApplyMapping(Material material, ScreenMappingParam a, ScreenMappingParam b,
        float blend, ScreenMappingKey key)
    {
        material.SetColor("_ScreenMappingColor", Color.Lerp(a.Color, b.Color, blend));
        material.SetFloat("_ScreenMappingColorPower", Mathf.Lerp(a.ColorPower, b.ColorPower, blend));
        material.SetFloat("_ScreenMappingBlendMode", a.BlendMode);
        material.SetFloat("_ScreenMappingPatternType", a.PatternType);
        material.SetFloat("_ScreenMappingPatternReverse", a.IsReversePattern ? 1f : 0f);
        material.SetFloat("_ScreenMappingPatternInterval", Mathf.Lerp(a.PatternInterval, b.PatternInterval, blend));
        material.SetFloat("_ScreenMappingPatternLength", Mathf.Lerp(a.PatternLength, b.PatternLength, blend));
        material.SetFloat("_ScreenMappingAppliesNoseShadow", key.AppliesNoseShadow ? 1f : 0f);
        material.SetFloat("_ScreenMappingAppliesMouthShadow", key.AppliesMouthShadow ? 1f : 0f);
    }

    private static void ApplyHologram(Material material, HologramKey a, HologramKey b, float blend, float time)
    {
        float flicker = Mathf.Lerp(a.FlickerSpeed, b.FlickerSpeed, blend);
        float flickerIntensity = Mathf.Lerp(a.FlickerIntensity, b.FlickerIntensity, blend);
        material.SetFloat("_ColorFlickerSpeed", 1f - (Mathf.Sin(time * flicker) + 1f) * Mathf.PerlinNoise(time, 0f) * flickerIntensity);
        material.SetVector("_Direction", Vector3.Lerp(a.Direction, b.Direction, blend));
        material.SetFloat("_ScanTiling", Mathf.Lerp(a.ScanTiling, b.ScanTiling, blend));
        material.SetFloat("_ScanSpeed", Mathf.Lerp(a.ScanSpeed, b.ScanSpeed, blend));
        material.SetFloat("_Grain", Mathf.Lerp(a.Grain, b.Grain, blend));
        material.SetFloat("_RimPowerHolo", Mathf.Lerp(a.RimPower, b.RimPower, blend));
        material.SetFloat("_RimHoloIntensity", Mathf.Lerp(a.RimIntensity, b.RimIntensity, blend));
        material.SetFloat("_GlowTiling", Mathf.Lerp(a.GlowTiling, b.GlowTiling, blend));
        material.SetFloat("_GlowSpeed", Mathf.Lerp(a.GlowSpeed, b.GlowSpeed, blend));
        material.SetFloat("_GlitchSpeed", Mathf.Lerp(a.GlitchSpeed, b.GlitchSpeed, blend));
        material.SetFloat("_GlitchIntensity", Mathf.Lerp(a.GlitchIntensity, b.GlitchIntensity, blend));
        material.SetFloat("_IsZeroOneGlitch", a.GlitchType == 1 ? 1f : 0f);
        material.SetFloat("_HologramEmission", Mathf.Lerp(a.BaseEmission, b.BaseEmission, blend));
        material.SetFloat("_HologramAlpha", Mathf.Lerp(a.HologramAlpha, b.HologramAlpha, blend));
        material.SetColor("_HologramBaseColor", Color.Lerp(a.HologramBaseColor, b.HologramBaseColor, blend));
        material.SetColor("_HologramRimColor", Color.Lerp(a.HologramRimColor, b.HologramRimColor, blend));
        material.SetTexture("_HologramNoiseTex", a.NoiseTex);
        material.SetFloat("_HologramGlowIntensity", Mathf.Lerp(a.HologramGlowIntensity, b.HologramGlowIntensity, blend));
        material.SetFloat("_HologramVoxel", a.VoxelEnable ? 1f : 0f);
        material.SetFloat("_HologramVoxelSmoothness", Mathf.Lerp(a.VoxelSmoothness, b.VoxelSmoothness, blend));
        material.SetFloat("_HologramVoxelIntensity", Mathf.Lerp(a.VoxelIntensity, b.VoxelIntensity, blend));
        material.SetFloat("_GlitchNoiseIntensity", Mathf.Lerp(a.GlitchNoiseIntensity, b.GlitchNoiseIntensity, blend));
        material.SetFloat("_AppTime", time);
    }
}
