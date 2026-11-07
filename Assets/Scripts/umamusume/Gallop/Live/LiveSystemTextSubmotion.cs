using Gallop.Live.Cutt;

namespace Gallop.Live
{
    public interface ILiveSystemTextSubmotionPlayer
    {
        int Mode{get;}
        int TailState{get;set;}
        bool TryTail(int id,bool second,out object clip);
        void SetTailClip(object clip);
        void CrossFadeTail(float duration);
        void StopTail();
        object EarClip(int id,bool second);
        void PlayEar(object clip,float duration);
    }
    public static class LiveSystemTextSubmotion
    {
        public static string EarPath(LiveSystemTextMotionSet motion)=>motion.EarMotion=="EarIdle"?null:
            "3d/Motion/Event/Facial/Type00/anm_eve_type00_"+motion.EarMotion+"_driven";
        public static string TailPath(LiveSystemTextMotionSet motion)=>string.IsNullOrEmpty(motion.TailMotion)?null:
            "3d/Motion/Event/Body/Type00/Tail/anm_eve_type00_"+motion.TailMotion;
        public static void Play(ILiveSystemTextSubmotionPlayer player,int id,LiveSystemTextMotionSet motion,bool second)
        {
            if(TailPath(motion)!=null)
            {
                if(player.Mode==1&&player.TryTail(id,second,out var clip))
                {player.SetTailClip(clip);player.CrossFadeTail(.5f);player.TailState=motion.TailMotionType==1?2:1;}
            }
            else if(player.TailState>0)player.StopTail();
            if(EarPath(motion)!=null)player.PlayEar(player.EarClip(id,second),second?.2f:0f);
        }
    }
}
