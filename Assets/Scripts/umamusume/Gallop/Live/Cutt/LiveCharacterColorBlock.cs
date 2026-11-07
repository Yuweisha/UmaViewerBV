using UnityEngine;

namespace Gallop.Live.Cutt
{
    // The character-lighting consumer of a BgColor1 timeline update. Director's
    // OnUpdateBgColor1 handler owns this write; the two-sided conformance replay
    // calls the same method so it exercises the production sequence instead of
    // a copy. Keep the field list and order identical to the original
    // StageController.UpdateCharaColor1 (0x4eb7ec0) consumer.
    public static class LiveCharacterColorBlock
    {
        // UpdateCharaColor1 0x4eb7ec0 scales the key's outlineWidthPower by the
        // constant at 0x64c4e6c (0.325f) before SetOutlineParameter 0x52a9070
        // stores it at model+0x5c8. LiveMiniColorRuntime.SetOutlineWidthForPower
        // uses the same constant.
        const float OutlineWidthBase = 0.325f;

        public static void Apply(Renderer renderer, MaterialPropertyBlock block, in BgColor1UpdateInfo info)
        {
            renderer.GetPropertyBlock(block);
            block.SetColor("_CharaColor", info.color * info.colorPower);
            block.SetColor("_ToonDarkColor", info.toonDarkColor);
            block.SetColor("_ToonBrightColor", info.toonBrightColor);
            block.SetColor("_OutlineColor", info.outlineColor);
            block.SetFloat("_OutlineWidth", info.outlineWidthPower * OutlineWidthBase);
            // Original BgColor1 producer UpdateCharaColor1 0x4eb7ec0 writes
            // _VertexColorToonPower (property 20) into the model block from the
            // key's vertexColorToonPower; the value mapping [rdi+0x58] is an
            // assumption recorded in adapter-map.json.
            block.SetFloat("_VertexColorToonPower", info.vertexColorToonPower);
            // FacialToon owns the eye's final saturation in LateUpdate; a
            // renderer-wide MPB would mask it.
            foreach (var material in renderer.sharedMaterials)
                if (material != null && material.HasProperty("_Saturation"))
                    material.SetFloat("_Saturation", info.Saturation);
            renderer.SetPropertyBlock(block);
        }
    }
}
