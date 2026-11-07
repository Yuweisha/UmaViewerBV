using System.Globalization;

namespace Gallop.Live
{
    public static class LiveMiniBuildPaths
    {
        public static int GetMiniBustType(int bust)=>bust==0||bust==1?0:bust==3||bust==4?2:1;
        public static int GetBodyPartsId(int gender,bool useGender,int shape)
        {int result=useGender&&(gender==1||gender==2)?gender:0;if(shape==1)result+=3;return result;}
        public static string Body(int chara,int bust,int gender,int bodyType,int bodySub,bool useGender,int shape)
        {
            if(bodyType>=100)return string.Format(CultureInfo.InvariantCulture,"3d/Chara/Mini/Body/mbdy{0:D4}_{1:D2}/pfb_mbdy{0:D4}_{1:D2}",chara,bodySub);
            return string.Format(CultureInfo.InvariantCulture,"3d/Chara/Mini/Body/mbdy{0:D4}_{1:D2}/pfb_mbdy{0:D4}_{1:D2}_{2:D2}_{3:D1}",bodyType,bodySub,GetBodyPartsId(gender,useGender,shape),GetMiniBustType(bust));
        }
        public static string Hair(int chara,int headSub)=>string.Format(CultureInfo.InvariantCulture,"3d/Chara/Mini/Head/mchr{0:D4}_{1:D2}/pfb_mchr{0:D4}_{1:D2}_hair",chara,headSub);
        public const string Face="3d/Chara/Mini/Head/mchr0001_00/pfb_mchr0001_00_face0";
        public static string CheekTexture(int controllerType,int mobId,int skin,int index)
        {
            if(controllerType==13&&mobId>0)return null;
            return string.Format(CultureInfo.InvariantCulture,"3d/Chara/Mini/Head/mchr0001_00/Textures/tex_mchr0001_00_face0_{0:D1}_cheek{1:D1}",skin,index);
        }
        public static string ViewerCostumeId(int gender,int bodyType,int bodySub,bool useGender,int shape)=>bodyType>=100?bodySub.ToString("D2",CultureInfo.InvariantCulture):string.Format(CultureInfo.InvariantCulture,"{0:D4}_{1:D2}_{2:D2}",bodyType,bodySub,GetBodyPartsId(gender,useGender,shape));
    }
}
