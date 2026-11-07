using System;
using System.Globalization;

namespace Gallop.Live
{
    // Shared CharacterBuildInfo/dress/chara-master fields. The raw master row
    // for tail selection and the special-body directory is supplied by loader;
    // constructing these values from a viewer cast is a separate binding.
    [Serializable]
    public sealed class LiveClothBuildIdentity
    {
        public int CharaId,MobId,ControllerType,HeadSubId,BodySubId,Gender,Bust;
        public int MobHairKindId,AudienceHairKindId,MiniMobTailId,OverrideCategory=-1;
        public int DressBodyType,DressBodyTypeSub,DressUseGender,DressBodyShape;
        public int DirectoryCharacterId,CharacterTailId,DressTailId=-1,DressTailSubId;
    }
    public static class LiveClothPathBuilder
    {
        static string D(int value,int digits)=>value.ToString("D"+digits,CultureInfo.InvariantCulture);
        static string HeadDirectory(int id,int sub)=>"3d/Chara/Head/chr"+D(id,4)+"_"+D(sub,2)+"/Clothes/";
        static string BodyDirectory(LiveClothBuildIdentity b,bool special)=>"3d/Chara/Body/bdy"+(special?b.DirectoryCharacterId.ToString(CultureInfo.InvariantCulture):D(b.DressBodyType,4))+"_"+D(b.DressBodyTypeSub,2)+"/Clothes/";
        static bool Mob(LiveClothBuildIdentity b)=>b.ControllerType==13?b.MobId>0:b.CharaId==1;
        static string BodyParts(LiveClothBuildIdentity b)
        {
            int value=0;
            if(b.DressUseGender==2){if(b.Gender==1)value=1;else if(b.Gender==2)value=2;}
            if(b.DressBodyShape==1){value=3;if(b.DressUseGender==2){if(b.Gender==1)value=4;else if(b.Gender==2)value=5;}}
            return value==0?string.Empty:"_"+D(value,2);
        }
        public static void GetTailModelIds(LiveClothBuildIdentity b,out int id,out int sub)
        {
            id=sub=-1;if(b.DressTailSubId<0)return;
            if(b.DressTailId>=0){id=b.DressTailId;sub=b.DressTailSubId;return;}
            if(b.CharacterTailId>=0){id=b.CharacterTailId;sub=b.DressTailSubId;}
        }
        public static string GetSkirtParamPath(LiveClothBuildIdentity b,int category)
        {
            bool special=b.DressBodyType>=100;
            string body="ast_bdy"+D(special?b.CharaId:b.DressBodyType,4)+"_"+D(b.DressBodyTypeSub,2);
            return BodyDirectory(b,special)+body+(special?string.Empty:BodyParts(b))+"_skirt"+D(category,2);
        }
        public static LiveClothBuildPaths Build(LiveClothBuildIdentity b)
        {
            var result=new LiveClothBuildPaths{OverrideCategory=b.OverrideCategory};
            bool mini=b.ControllerType==13,mob=Mob(b),audience=b.CharaId==900,special=b.DressBodyType>=100;
            string additional=BodyParts(b);
            GetTailModelIds(b,out int sourceTail,out int sourceTailSub);
            for(int category=0;category<5;category++)
            {
                string suffix=D(category,2);
                if(mini)
                {
                    string head="mchr"+D(b.CharaId,4)+"_"+D(b.HeadSubId,2);
                    result.Head[category]="3d/Chara/Mini/Head/"+head+"/Clothes/pfb_"+head+"_cloth"+suffix;
                    result.Bust[category]=null;
                    int bodyId=mob?b.CharaId:special?b.CharaId:b.DressBodyType;
                    int bodySub=mob?b.BodySubId:b.DressBodyTypeSub;
                    string body="mbdy"+D(bodyId,4)+"_"+D(bodySub,2);
                    result.Body[category]="3d/Chara/Mini/Body/"+body+"/Clothes/pfb_"+body+(mob?string.Empty:additional)+"_cloth"+suffix;
                    int tailId=mob&&b.MiniMobTailId>0?b.MiniMobTailId:sourceTail,tailSub=mob&&b.MiniMobTailId>0?0:sourceTailSub;
                    string tail="mtail"+D(tailId,4)+"_"+D(tailSub,2);
                    result.Tail[category]=tailId>=0&&tailSub>=0?"3d/Chara/Mini/Tail/"+tail+"/Clothes/pfb_"+tail+"_cloth"+suffix:string.Empty;
                }
                else
                {
                    string head="pfb_chr"+D(b.CharaId,4)+"_"+D(b.HeadSubId,2);
                    if(mob||audience)head+="_hair"+D(mob?b.MobHairKindId:b.AudienceHairKindId,3);
                    result.Head[category]=HeadDirectory(b.CharaId,b.HeadSubId)+head+"_cloth"+suffix;
                    string body="pfb_bdy"+D(special?b.CharaId:b.DressBodyType,4)+"_"+D(b.DressBodyTypeSub,2);
                    string directory=BodyDirectory(b,special);
                    result.Bust[category]=directory+body+(special?"_bust_cloth":additional+"_bust"+D(b.Bust,1)+"_cloth")+suffix;
                    result.Body[category]=directory+body+(special?string.Empty:additional)+"_cloth"+suffix;
                    int tailId=mob?1:sourceTail,tailSub=mob?0:sourceTailSub;
                    string tail="tail"+D(tailId,4)+"_"+D(tailSub,2);
                    result.Tail[category]=!audience&&tailId>=0&&tailSub>=0?"3d/Chara/Tail/"+tail+"/Clothes/pfb_"+tail+"_cloth"+suffix:string.Empty;
                }
            }
            return result;
        }
    }
}
