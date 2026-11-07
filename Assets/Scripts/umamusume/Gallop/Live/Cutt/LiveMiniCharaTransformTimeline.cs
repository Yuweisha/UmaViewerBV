using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live
{
    public interface ILiveMiniTransformPlayer
    {
        Transform RootTransform {get;}
        float BodyScale {get;set;}
        void SetVisible(bool visible);
        void SetLayerIndex(int index);
        void ResetCyspringScale(int parts);
    }
}
namespace Gallop.Live.Cutt
{
    [Serializable] public sealed class LiveTimelineKeyMiniCharaTransformData : LiveTimelineKeyWithInterpolate
    {
        public bool Visible;
        public Vector3 Position,Rotate;
        public float BodyScale=1f;
        public LiveTimelineDefine.MiniCharaLayerIndex LayerIndex=LiveTimelineDefine.MiniCharaLayerIndex.CHAR;
        public bool IsLayout2d,IsAttachChara;
        public int AttachIndex;
        public string AttachNodeName=string.Empty;
        public bool ShouldInheritParentScale;
        public override LiveTimelineKeyDataType dataType=>LiveTimelineKeyDataType.MiniCharaTransform;
    }
    [Serializable] public sealed class LiveTimelineKeyMiniCharaTransformDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyMiniCharaTransformData> { }
    [Serializable] public sealed class LiveTimelineMiniCharaTransformData : ILiveTimelineGroupData
    {
        public LiveTimelineKeyMiniCharaTransformDataList keys=new LiveTimelineKeyMiniCharaTransformDataList();
        [SerializeField] int _index;
        public int Index {get=>_index;set=>_index=value;}
        public override ILiveTimelineKeyDataList GetKeyList()=>keys;
    }
    public partial class LiveTimelineWorkSheet {[SerializeField] public List<LiveTimelineMiniCharaTransformData> MiniCharaTransformDataList;}
    public partial class LiveTimelineControl
    {
        public Func<int,ILiveMiniTransformPlayer> MiniTransformPlayerResolver {get;set;}
#if UNITY_EDITOR
        public event Action<int,string,Vector3,Quaternion> OnMiniCharaTransformWrite;
#endif
        static Vector3 MiniTransformLerp(Vector3 a,Vector3 b,float t)=>new Vector3(MiniColorLerp(a.x,b.x,t),MiniColorLerp(a.y,b.y,t),MiniColorLerp(a.z,b.z,t));
        // Original4ed78f0. Real Mini model rendering/physics remain behind the
        // player boundary; the transform math, parenting and order are here.
        public void AlterUpdate_MiniCharaTransform(LiveTimelineWorkSheet sheet,int frame,float currentTime)
        {
            if(sheet.MiniCharaTransformDataList==null)return;
            var director=Director.instance;
            foreach(var group in sheet.MiniCharaTransformDataList)
            {
                var keys=group.keys;if(keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable)||!keys.EnablePlayModeTimeline(_playMode))continue;var found=keys.FindKeyCached(frame,true);if(found.key==null)continue;
                var player=MiniTransformPlayerResolver?.Invoke(group.Index);if(player==null)continue;var a=(LiveTimelineKeyMiniCharaTransformData)found.key;var b=keys.At(found.index+1)as LiveTimelineKeyMiniCharaTransformData;player.SetVisible(a.Visible);
                var position=a.Position;var rotation=Quaternion.Euler(a.Rotate);float scale=a.BodyScale;
                if(b!=null&&b.IsInterpolateKey()){float t=CalculateInterpolationValueAtTime(a,b,currentTime);position=MiniTransformLerp(position,b.Position,t);rotation=Quaternion.Lerp(rotation,Quaternion.Euler(b.Rotate),t);scale=MiniColorLerp(scale,b.BodyScale,t);}
                var parent=director.MiniCharaRootTransform;
                if(a.IsAttachChara&&!a.IsLayout2d)
                {
                    var actor=director.ResolvePhysicalFacialCharacter(a.AttachIndex);if(actor!=null&&actor.Body!=null&&actor.LiveLocator!=null&&actor.LiveLocator.Bones.TryGetValue(a.AttachNodeName,out var node)&&node!=null){parent=node;if(!a.ShouldInheritParentScale)scale=MonitorFloat32(scale/node.lossyScale.y);}
                }
                var root=player.RootTransform;if(root.parent!=parent)
                {
                    root.SetParent(parent,false);
#if UNITY_EDITOR
                    OnMiniCharaTransformWrite?.Invoke(group.Index,"parent",Vector3.zero,Quaternion.identity);
#endif
                }
                bool layout=a.IsLayout2d&&!a.IsAttachChara&&director.MiniCharaCamera!=null;
                if(layout){position=director.MiniCharaCamera.ViewportToWorldPoint(position);player.SetLayerIndex(20);}else player.SetLayerIndex(1);
                root.SetLocalPositionAndRotation(position,rotation);
#if UNITY_EDITOR
                OnMiniCharaTransformWrite?.Invoke(group.Index,"pose",position,rotation);
#endif
                var localScale=new Vector3(scale,scale,scale);root.localScale=localScale;
#if UNITY_EDITOR
                OnMiniCharaTransformWrite?.Invoke(group.Index,"scale",localScale,Quaternion.identity);
#endif
                player.BodyScale=scale;player.ResetCyspringScale(0);player.ResetCyspringScale(1);player.ResetCyspringScale(2);
                int layer=(int)a.LayerIndex;player.SetLayerIndex(layout?20:layer==15||layer==16?layer:1);
            }
        }
    }
}
