using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Gallop.Live
{
    // Material identities must come from the PartsHolder registration, not all renderers.
    public sealed class LiveDirtMaterialBindings
    {
        public bool BodyEnabled, FaceEnabled, AttachmentEnabled, BillboardEnabled, AlphaEnabled;
        public Material[] Body, Billboard, Alpha;
        public Material Face, Attachment;
        public readonly List<BodyStyle> BodyStyles = new List<BodyStyle>();
        public readonly List<Material> AttachmentStyles = new List<Material>();
        public struct BodyStyle
        {
            public Material Alpha, CastShadow;
        }

        public Texture Body0, Body1, Body2, FaceTexture, AttachmentTexture;
        public bool IsUseDirtTexture;
        static int CompareBodyMaterial(Material a, Material b)
        {
            var left = a.name.Split('_');
            var right = b.name.Split('_');
            return int.TryParse(left[left.Length - 1], out int x) && int.TryParse(right[right.Length - 1], out int y) ? unchecked(x - y) : 0;
        }

        public static LiveDirtMaterialBindings Register(Renderer[] renderers, Material[][] materials, int characterId, int headSubId, int bodyModelSubId, int dressBodyType, int dressBodySub, bool generalDress)
        {
            var result = new LiveDirtMaterialBindings();
            var billboards = new List<Material>();
            var alpha = new List<Material>();
            bool faceRegistered = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                string name = renderers[i].name;
                Material[] group = materials[i];
                if (name.Contains("M_Body"))
                {
                    if (!result.BodyEnabled && name == "M_Body")
                    {
                        result.Body = group;
                        Array.Sort(result.Body, CompareBodyMaterial);
                        result.BodyEnabled = true;
                    }
                    else if (name.Contains("M_Body_Alpha"))
                    {
                        if (!int.TryParse(name.Substring("M_Body_Alpha".Length), out _))
                            continue;
                        int id = generalDress ? dressBodyType : characterId, sub = generalDress ? dressBodySub : bodyModelSubId;
                        string baseName = string.Format(CultureInfo.InvariantCulture, "mtl_bdy{0:0000}_{1:00}", id, sub);
                        Material style = null, shadow = null;
                        foreach (var material in group)
                        {
                            if (material.name.Contains(baseName + "_Alpha"))
                                style = material;
                            else if (material.name == baseName)
                                shadow = material;
                        }

                        if (style == null)
                            style = group.Length == 0 ? null : group[0];
                        result.BodyStyles.Add(new BodyStyle { Alpha = style, CastShadow = shadow });
                    }
                    else if (name.Contains("M_Body_Prop"))
                    {
                        alpha.Add(group.Length == 0 ? null : group[0]);
                        result.AlphaEnabled = true;
                    }
                    else if (name == "M_Body_Billboard")
                    {
                        billboards.Add(group.Length == 0 ? null : group[0]);
                        result.BillboardEnabled = true;
                    }
                }
                else if (name.Contains("M_Face"))
                {
                    if (name.Contains("M_Face_Prop") || name != "M_Face" || faceRegistered)
                        continue;
                    faceRegistered = true;
                    string faceName = string.Format(CultureInfo.InvariantCulture, "mtl_chr{0:0000}_{1:00}_face", characterId, headSubId);
                    foreach (var material in group)
                        if (material.name == faceName)
                        {
                            result.Face = material;
                            result.FaceEnabled = true;
                        }
                }
                else if (name.Contains("M_Hair"))
                    continue;
                else if (name.Contains("M_Attach"))
                {
                    if (result.Attachment == null && name == "M_Attach")
                    {
                        result.Attachment = group.Length == 0 ? null : group[0];
                        result.AttachmentEnabled = true;
                    }
                    else if (name.Contains("M_Attach_Alpha") && int.TryParse(name.Substring("M_Attach_Alpha".Length), out _))
                        result.AttachmentStyles.Add(group.Length == 0 ? null : group[0]);
                }
            }

            result.Billboard = billboards.ToArray();
            result.Alpha = alpha.ToArray();
            return result;
        }

        void Write(Material material, int property, Texture texture)
        {
            if (ReferenceEquals(material, null))
                throw new NullReferenceException("Missing registered Dirt material");
            material.SetTexture(property, texture);

        }

        void ApplyBody0(Texture texture, int property)
        {
            if (!BodyEnabled)
                return;
            if (Body == null)
                throw new NullReferenceException("Missing registered body material array");
            if (Body.Length == 0)
                return;
            Write(Body[0], property, texture);
            foreach (var style in BodyStyles)
            {
                if (style.Alpha != null)
                    Write(style.Alpha, property, texture);
                if (style.CastShadow != null)
                    Write(style.CastShadow, property, texture);
            }

            if (BillboardEnabled)
                foreach (var material in Billboard)
                    Write(material, property, texture);
            if (AlphaEnabled)
                foreach (var material in Alpha)
                    Write(material, property, texture);
        }

        // ModelLoader.SetWetDirtTexture with no wet array. Null dirt skips installation.
        public void Install(Texture[] textures)
        {
            if (textures == null)
                return;
            int property = ShaderManager.GetPropertyId(ShaderManager.PropertyId._DirtTex);
            Texture body0 = textures[0], body1 = textures[2], body2 = textures[3], face = textures[1], attachment = textures[4];
            ApplyBody0(body0, property);
            if (BodyEnabled && Body.Length > 1)
                Write(Body[1], property, body1);
            if (BodyEnabled && Body.Length > 2)
                Write(Body[2], property, body2);
            if (FaceEnabled)
                Write(Face, property, face);
            if (AttachmentEnabled && attachment != null)
            {
                Write(Attachment, property, attachment);
                foreach (var material in AttachmentStyles)
                    Write(material, property, attachment);
            }

            Body0 = body0;
            Body1 = body1;
            Body2 = body2;
            FaceTexture = face;
            AttachmentTexture = attachment;
            IsUseDirtTexture = false;
            foreach (var texture in textures)
                if (texture != null)
                {
                    IsUseDirtTexture = true;
                    break;
                }
        }
    }
}
