using System.Collections.Generic;

namespace Gallop.Live.Cutt
{
    // Producer reconstruction of AlterUpdate_FlashPlayer 0x4ed0170. Interface
    // slots resolved from the catalog (ILiveFlashController): 3 PlayLabel,
    // 4 PlayIn, 5 PlayIn00, 6 PlayOut, 7 PlayEnd.
    

    public static class FlashEffectProducer
    {
        public static bool Apply(LiveTimelineKeyFlashPlayerDataList keys,int frame,float currentTime,TimelinePlayerMode playMode,ILiveFlashController controller,LiveChampionsTextController champions=null)
        {
            if(keys==null)return false;
            if(keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable)||!keys.EnablePlayModeTimeline(playMode))return false;
            LiveTimelineControl.FindTimelineKey(out var current,out _,keys,frame);
            if(!(current is LiveTimelineKeyFlashPlayerData key))return false;
            float elapsed=LiveTimelineControl.MonitorFloat32(currentTime-key.frame/60f);
            if(!key.UseActionLabel&&key._actionType==LiveFlashPlayerActionType.PlayIn)champions?.PlayIn(elapsed,key);
            string label=key.UseActionLabel?key.ActionLabel:
                key._actionType==LiveFlashPlayerActionType.PlayIn?"in":
                key._actionType==LiveFlashPlayerActionType.PlayIn00?"in00":
                key._actionType==LiveFlashPlayerActionType.PlayOut?"out":
                key._actionType==LiveFlashPlayerActionType.PlayEnd?"end":null;
            if(label!=null&&controller!=null&&controller.IsInitialized)controller.PlayLabel(label,elapsed,key);
            return true;
        }
        
    }
}
