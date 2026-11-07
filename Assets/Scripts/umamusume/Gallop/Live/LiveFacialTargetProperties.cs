using System.Collections.Generic;

namespace Gallop.Live
{
    public static class LiveFacialTargetProperties
    {
        // Executed DrivenKeyComponent.SCALE_BONE_ARRAY; ordinary full-size
        // initialization classifies the last target path segment by this list.
        static readonly HashSet<string> ScaleBones=new HashSet<string>{
            "Eye_L","Eye_R","Tooth_up","Tooth_bottom","Tongue",
            "Eye_sub_01_L","Eye_sub_02_L","Eye_sub_03_L",
            "Eye_sub_01_R","Eye_sub_02_R","Eye_sub_03_R"};
        static readonly Dictionary<string,bool> ScalePaths=new Dictionary<string,bool>(System.StringComparer.Ordinal);
        public static bool IsScaleBone(string path)
        {
            if(path==null)return false;
            if(ScalePaths.TryGetValue(path,out bool result))return result;
            int slash=path.LastIndexOf('/');
            result=ScaleBones.Contains(slash<0?path:path.Substring(slash+1));
            ScalePaths.Add(path,result);
            return result;
        }
    }
}
