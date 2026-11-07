using System;
using System.Globalization;
using UnityEngine;

namespace Gallop.Live
{
    // CharacterBuildInfo and master data supplied by the actual model loader.
    public sealed class LiveDirtTextureContext
    {
        public int CharacterId, MobId, HeadSubId, DressId;
        public int ControllerType = 4, Gender = 2, AttachmentId = -1, MobFace;
        public bool HasDress = true, PersonalDress, GeneralDress;
        public int BodyType = 100, BodySub, UseGender, BodyShape, IsDirt = 1;
        public string AttachmentSwapTextureName = "";
    }

    public static class LiveDirtTextureLoader
    {
        static string F(string format, params object[] values) => string.Format(CultureInfo.InvariantCulture, format, values);

        public static string BodyPath(LiveDirtTextureContext c)
        {
            if(c.BodyType >= 100)
                return F("3d/Chara/Body/bdy{0:0000}_{1:00}/Textures/tex_bdy{0:0000}_{1:00}_dirt",c.CharacterId,c.BodySub);
            if(c.DressId == 1)
                return F("3d/Chara/Body/bdy{0:0000}_{1:00}/Textures/offline/tex_bdy{0:0000}_{1:00}_00_waku0_dirt",c.BodyType,c.BodySub);
            if(c.PersonalDress)
                return F("3d/Chara/Body/bdy{0:0000}_{1:00}_{2:0000}/Textures/tex_bdy{0:0000}_{1:00}_{2:0000}00_0_0_dirt",c.BodyType,c.BodySub,c.CharacterId);
            int parts = c.UseGender == 2 ? (c.Gender == 1 ? 1 : c.Gender == 2 ? 2 : 0) : 0;
            if(c.BodyShape == 1)parts += 3;
            return F("3d/Chara/Body/bdy{0:0000}_{1:00}/Textures/tex_bdy{0:0000}_{1:00}_{2:D2}_0_0_dirt",c.BodyType,c.BodySub,parts);
        }

        public static string FacePath(LiveDirtTextureContext c)
        {
            bool mob=c.ControllerType==13 ? c.MobId>0 : c.CharacterId==1;
            return mob
                ? F("3d/Chara/Head/chr0001_00/Textures/Face/tex_chr0001_00_face{0:000}_0_dirt",c.MobFace)
                : F("3d/Chara/Head/chr{0:0000}_{1:00}/Textures/tex_chr{0:0000}_{1:00}_face_dirt",c.CharacterId,c.HeadSubId);
        }

        public static string SkinPath(LiveDirtTextureContext c) => c.PersonalDress
            ? F("3d/Chara/Body/bdy{0:0000}_{1:00}_{2:0000}/Textures/offline/tex_bdy{0:0000}_{1:00}_{2:0000}_00_0_0_00_dirt",c.BodyType,c.BodySub,c.CharacterId)
            : F("3d/Chara/Body/bdy{0:0000}_{1:00}/Textures/offline/tex_bdy{0:0000}_{1:00}_00_0_0_00_dirt",c.BodyType,c.BodySub);

        public static string AttachmentPath(LiveDirtTextureContext c)
        {
            if(c.AttachmentId<0 || (c.ControllerType==13 && c.MobId>0) || (c.HasDress && c.BodyType>=100))return "";
            return F("3d/Chara/Attach/attach{0:D4}_{1:D2}/Textures/tex_attach{0:D4}_{1:D2}{2}_dirt",c.CharacterId,c.AttachmentId,c.AttachmentSwapTextureName);
        }

        // Preserve an existing array when the original Editable method does not load.
        public static Texture[] Load(LiveDirtTextureContext c, Func<string,Texture> load, Texture[] previous=null)
        {
            if(!c.HasDress || c.IsDirt==0)return previous;
            var result=new Texture[5];
            result[0]=load(BodyPath(c));
            result[1]=load(FacePath(c));
            if(c.DressId==1)
            {
                // Original request order is zekken, then skin; array order differs.
                result[3]=load(F("3d/Chara/Body/bdy{0:0000}_00/Textures/zekken/tex_bdy{0:0000}_00_zekken0_0_dirt",c.DressId));
                result[2]=load(SkinPath(c));
            }
            string attachment=AttachmentPath(c);
            if(!string.IsNullOrEmpty(attachment))result[4]=load(attachment);
            return result;
        }
    }
}
