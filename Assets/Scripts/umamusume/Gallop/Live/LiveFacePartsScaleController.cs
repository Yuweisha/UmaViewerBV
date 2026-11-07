using System;
using System.Linq;
using UnityEngine;

namespace Gallop.Live
{
    // FacePartsScaleController.AfterApplyDrivenKey: run after authored facial
    // TRS is applied, using the stored eye submission IDs and current bones.
    public sealed class LiveFacePartsScaleController
    {
        readonly Transform head;
        readonly Transform[] mayu,offset,kubomi;
        readonly Transform[][] futae;
        readonly float doubleMin,doubleMax,kubomiMin,kubomiMax;
        readonly bool hasMayu,hasFutae,hasKubomi;
        public bool Enabled=true;
        public bool IsPlayingFaceAnimation;
        public event Action<Transform,Vector3> ScaleWrite;

        public LiveFacePartsScaleController(Transform head,Transform[] mayu,Transform[] offset,Transform[][] futae,Transform[] kubomi,float doubleMin,float doubleMax,float kubomiMin,float kubomiMax)
        {
            this.head=head;this.mayu=mayu;this.offset=offset;this.futae=futae;this.kubomi=kubomi;
            this.doubleMin=doubleMin;this.doubleMax=doubleMax;this.kubomiMin=kubomiMin;this.kubomiMax=kubomiMax;
            hasMayu=mayu.All(node=>node!=null);hasFutae=futae.All(side=>side.All(node=>node!=null));hasKubomi=kubomi.All(node=>node!=null);
        }

        public static LiveFacePartsScaleController Create(UmaContainerCharacter actor)
        {
            if(actor.IsMini)return null;
            var nodes=actor.FaceDrivenKeyTarget.Objs;
            Transform Node(string name)=>nodes.TryGetValue(name,out var node)?node:null;
            var holder=actor.GetComponentsInChildren<AssetHolder>(true).FirstOrDefault(item=>item.Get<DrivenKeyTarget>("ear_target")!=null);
            float Bound(string key)=>holder!=null&&(holder._assetTableValue?.list?.Any(pair=>pair.Key==key)??false)?holder.GetValue(key):0f;
            var mayu=new[]{Node("Eyebrow_01_L"),Node("Eyebrow_01_R")};
            return new LiveFacePartsScaleController(Node("Head_height"),mayu,mayu.Select(node=>node!=null?node.parent:null).ToArray(),
                new[]{new[]{Node("Eye_double_01_L"),Node("Eye_double_02_L")},new[]{Node("Eye_double_01_R"),Node("Eye_double_02_R")}},
                new[]{Node("Eye_sub_04_L"),Node("Eye_sub_04_R")},Bound("mayu_double_dot_diff_min"),Bound("mayu_double_dot_diff_max"),Bound("mayu_kubomi_dot_diff_min"),Bound("mayu_kubomi_dot_diff_max"));
        }

        static float F(float value)=>Cutt.LiveTimelineControl.MonitorFloat32(value);
        static float DotDifference(Vector3 mayuPosition,Vector3 target,Vector3 direction)
        {
            float y=F(F(mayuPosition.y-target.y)*direction.y);
            float x=F(F(mayuPosition.x-target.x)*direction.x);
            float z=F(F(mayuPosition.z-target.z)*direction.z);
            return F(F(y+x)+z);
        }
        void SetScale(Transform node,Vector3 value){node.localScale=value;ScaleWrite?.Invoke(node,value);}
        bool TrySetScale(Transform node,float distance,float min,float max,bool baseOnly)
        {
            if(distance<min){SetScale(node,Vector3.zero);return true;}
            if(distance<max)
            {
                float rate=min==max?0f:F(F(distance-min)/F(max-min));
                if(rate<0f)rate=0f;else if(rate>1f)rate=1f;
                var scale=baseOnly?Vector3.one:node.localScale;
                SetScale(node,new Vector3(F(scale.x*rate),F(scale.y*rate),F(scale.z*rate)));
            }
            else if(baseOnly)SetScale(node,Vector3.one);
            return false;
        }
        public void AfterApplyDrivenKey(bool leftBaseEye,bool rightBaseEye)
        {
            if(!Enabled||!hasMayu||(!hasFutae&&!hasKubomi))return;
            var direction=head.localPosition;
            for(int side=0;side<2;side++)
            {
                bool baseOnly=!IsPlayingFaceAnimation&&(side==0?leftBaseEye:rightBaseEye);
                var a=mayu[side].localPosition;var b=offset[side].localPosition;
                var position=new Vector3(F(a.x+b.x),F(a.y+b.y),F(a.z+b.z));
                if(hasFutae)
                {
                    float distance=DotDifference(position,futae[side][0].localPosition,direction);
                    foreach(var node in futae[side])TrySetScale(node,distance,doubleMin,doubleMax,baseOnly);
                }
                if(hasKubomi)TrySetScale(kubomi[side],DotDifference(position,kubomi[side].localPosition,direction),kubomiMin,kubomiMax,baseOnly);
            }
        }
    }
}
