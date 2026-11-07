using System.Collections.Generic;

namespace Gallop.Live.Cutt
{
    public static class LiveSystemTextBodyResources
    {
        // SimpleModelController.GetBodyMotionName 0x362e6b0. This appends to
        // the caller's list; it does not replace it or clear a failed request.
        public static bool GetPaths(LiveSystemTextMotionSet motion,int buildCharacterId,List<string> paths,ref bool isAddMotion)
        {
            if(motion.BodyMotionPlayType==3)
            {
                paths.Add("3d/Motion/Event/Body/Type00/Plus/anm_eve_type00_"+motion.BodyMotion);
                isAddMotion=true;return true;
            }
            isAddMotion=false;
            bool unique=motion.BodyMotionType==2;
            if(!unique&&motion.BodyMotionType!=1&&motion.BodyMotionType!=3)return false;
            bool mirror=motion.BodyMotion.Contains("_mirror");
            string name=motion.BodyMotion.Replace("_mirror",string.Empty);
            switch(motion.BodyMotionPlayType)
            {
                case 1:paths.Add(Path(motion,buildCharacterId,name,string.Empty,mirror,unique));break;
                case 2:
                    paths.Add(Path(motion,buildCharacterId,name,"_S",mirror,unique));
                    paths.Add(Path(motion,buildCharacterId,name,"_loop",mirror,unique));
                    paths.Add(Path(motion,buildCharacterId,name,"_E",mirror,unique));break;
                case 4:paths.Add(Path(motion,buildCharacterId,name,"_loop",mirror,unique));break;
                default:return false;
            }
            if(unique)paths.Add(Path(motion,buildCharacterId,name,"_pose",mirror,true));
            return true;
        }
        static string Path(LiveSystemTextMotionSet motion,int characterId,string name,string suffix,bool mirror,bool unique)
        {
            string path;
            if(unique)path="3d/Motion/Event/Body/Chara/chr"+characterId.ToString("D4")+"_00/anm_eve_chr"+characterId.ToString("D4")+"_00_"+name+suffix;
            else
            {
                string directory=motion.BodyMotionPathSegment==1?"3d/Motion/Event/Body/Type00/":
                    motion.BodyMotionPathSegment==2?"3d/Motion/TrainingMenu/Body/Type00/":string.Empty;
                string prefix=motion.BodyMotionPathSegment==1?"anm_eve_type":motion.BodyMotionPathSegment==2?"anm_trm_type":string.Empty;
                path=directory+prefix+"00_"+name+suffix;
            }
            return mirror?path+"_mirror":path;
        }
        public static bool GetCommonPaths(string name,int playType,int segment,bool mirror,List<string> paths)
        {
            var motion=new LiveSystemTextMotionSet{BodyMotionPathSegment=segment};
            switch(playType)
            {
                case 1:paths.Add(Path(motion,0,name,string.Empty,mirror,false));break;
                case 2:
                    paths.Add(Path(motion,0,name,"_S",mirror,false));
                    paths.Add(Path(motion,0,name,"_loop",mirror,false));
                    paths.Add(Path(motion,0,name,"_E",mirror,false));break;
                case 4:paths.Add(Path(motion,0,name,"_loop",mirror,false));break;
                default:return false;
            }
            return true;
        }
    }
}
