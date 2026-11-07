using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop
{
    [Serializable] public sealed class FaceTypeSet {public int[] faceType;public int count;}
    [Serializable] public sealed class EarTypeSet {public EarType[] faceType;public int count;}
    public sealed class DrivenKeyAnimation : ScriptableObject
    {
        public AnimationClip clip;
        public FaceTypeSet[] animationFaceType;
        public EarTypeSet[] animationEarType;
        [SerializeField] bool _isCheckEyeRange;
        public bool IsCheckEyeRange=>_isCheckEyeRange;
    }
}
namespace Gallop.Live
{
    // MiniAnimationController's four native call contracts. Player/Playable,
    // facial and ear implementations and the actual Mini owner are separate.
    public interface ILiveMiniAnimationPlayer
    {
        void PlayBodyMotion(AnimationClip clip,float time,bool loop);
        void PlayFacialMotion(DrivenKeyAnimation clip,float time,bool loop);
        void PlayEarMotion(DrivenKeyAnimation clip,float time,bool loop);
        void PlayPositionMotion(AnimationClip clip,float time,bool loop);
    }
}
namespace Gallop.Live.Cutt
{
    [Serializable] public sealed class LiveTimelineKeyMiniCharaMotionData : LiveTimelineKey
    {
        public AnimationClip BodyAnimationClip;
        public int BodyHeadFrame;
        public float BodyPlaySpeed=1f;
        public DrivenKeyAnimation FacialDrivenKeyAnimation;
        public int FacialHeadFrame;
        public float FacialPlaySpeed=1f;
        public DrivenKeyAnimation EarDrivenKeyAnimation;
        public int EarHeadFrame;
        public float EarPlaySpeed=1f;
        public AnimationClip PositionAnimationClip;
        public int PositionHeadFrame;
        public float PositionPlaySpeed=1f;
        public bool Loop;
        public override LiveTimelineKeyDataType dataType=>LiveTimelineKeyDataType.MiniCharaMotion;
    }
    [Serializable] public sealed class LiveTimelineKeyMiniCharaMotionDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyMiniCharaMotionData> { }
    [Serializable] public sealed class LiveTimelineMiniCharaMotionData : ILiveTimelineGroupData
    {
        public LiveTimelineKeyMiniCharaMotionDataList keys=new LiveTimelineKeyMiniCharaMotionDataList();
        [SerializeField] int _index;
        public int Index {get=>_index;set=>_index=value;}
        public override ILiveTimelineKeyDataList GetKeyList()=>keys;
    }
    public partial class LiveTimelineWorkSheet {[SerializeField] public List<LiveTimelineMiniCharaMotionData> MiniCharaMotionDataList;}
    public partial class LiveTimelineControl
    {
        public Func<int,ILiveMiniAnimationPlayer> MiniAnimationPlayerResolver {get;set;}
        static float MiniMotionTime(float elapsed,int head,float speed)=>MonitorFloat32(MonitorFloat32(elapsed*speed)+MonitorFloat32(head/60f));
        // Original4ed7450. The resolver is the host's original Mini controller
        // lookup boundary, not a replacement for any of its consumer bodies.
        public void AlterUpdate_MiniCharaMotion(LiveTimelineWorkSheet sheet,int frame,float currentTime)
        {
            if(sheet.MiniCharaMotionDataList==null)return;
            foreach(var group in sheet.MiniCharaMotionDataList)
            {
                var keys=group.keys;if(keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable)||!keys.EnablePlayModeTimeline(_playMode))continue;
                var found=keys.FindKeyCached(frame,true);if(found.key==null)continue;var key=(LiveTimelineKeyMiniCharaMotionData)found.key;float elapsed=MonitorFloat32(currentTime-MonitorFloat32(key.frame/60f));var player=MiniAnimationPlayerResolver?.Invoke(group.Index);if(player==null)continue;
                if(key.BodyAnimationClip!=null)player.PlayBodyMotion(key.BodyAnimationClip,MiniMotionTime(elapsed,key.BodyHeadFrame,key.BodyPlaySpeed),key.Loop);
                if(key.FacialDrivenKeyAnimation!=null)player.PlayFacialMotion(key.FacialDrivenKeyAnimation,MiniMotionTime(elapsed,key.FacialHeadFrame,key.FacialPlaySpeed),key.Loop);
                if(key.EarDrivenKeyAnimation!=null)player.PlayEarMotion(key.EarDrivenKeyAnimation,MiniMotionTime(elapsed,key.EarHeadFrame,key.EarPlaySpeed),key.Loop);
                if(key.PositionAnimationClip!=null)player.PlayPositionMotion(key.PositionAnimationClip,MiniMotionTime(elapsed,key.PositionHeadFrame,key.PositionPlaySpeed),key.Loop);
            }
        }
    }
}
