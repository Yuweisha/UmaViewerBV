using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    [Serializable]
    public class LiveTimelineKeyCharaCollisionData : LiveTimelineKeyWithInterpolate
    {
        public override LiveTimelineKeyDataType dataType => LiveTimelineKeyDataType.CharaCollision;
        public bool IsEnable;
        public Vector3 Position, Angle;
        public CySpringCollisionData.CollisionType CollisionType = CySpringCollisionData.CollisionType.Plane;
        public float Radius;
        public Vector3 Offset, Offset2;
        public bool IsInner, IsApplyHead = true, IsApplyTail = true;
        public LiveCharaPositionFlag CharacterFlag;
        [NonSerialized] public Quaternion Rotation = Quaternion.identity;
        public override void OnLoad(LiveTimelineControl control) { Rotation = Quaternion.Euler(Angle); }
    }
    [Serializable] public class LiveTimelineKeyCharaCollisionDataList : LiveTimelineKeyDataListTemplate<LiveTimelineKeyCharaCollisionData> { }
    [Serializable]
    public class LiveTimelineCharaCollisionData : ILiveTimelineGroupDataWithName
    {
        public LiveTimelineKeyCharaCollisionDataList keys = new LiveTimelineKeyCharaCollisionDataList();
        public LiveTimelineCharaCollisionData() : base("CharaCollision") { }
        public override ILiveTimelineKeyDataList GetKeyList() => keys;
    }
    public partial class LiveTimelineWorkSheet
    {
        [SerializeField] public List<LiveTimelineCharaCollisionData> CharaCollisionDataList;
    }

    // One persistent component/runtime identity per authored collision track.
    public sealed class LiveCharaCollisionState
    {
        public readonly GameObject Object;
        public readonly CySpringCollisionRuntimeData Data;
        public int KeyFrame = -1;
        public bool IsEnable, ApplyHead, ApplyTail;
        public int CharacterFlag;
        public bool HasGeometry;
        public Vector3 LocalPosition;
        public Quaternion LocalRotation = Quaternion.identity;
        public LiveCharaCollisionState(Transform parent, int index)
        {
            Object = new GameObject("LiveCharaCollision" + index);
            Object.transform.parent = parent;
            Data = new CySpringCollisionRuntimeData { IsWorld = true, FilterSpringNodeExclusive = true };
            Data.Initialize(Object.transform);
        }
    }
    public sealed class LiveCharaCollisionRuntime : IDisposable
    {
        public readonly LiveCharaCollisionState[] States;
        // API leaf seam used by the independent native fixture. Actual playback
        // always uses the real Transform.lossyScale unless a host supplies one.
        public Func<Transform, Vector3> LossyScale;
        public Action<int, CySpringController.Parts, CySpringCollisionRuntimeData[]> Apply;
        static readonly Quaternion PlaneUp = Quaternion.Euler(-90f, 0f, 0f);
        static float F(float x) => BitConverter.ToSingle(BitConverter.GetBytes(x), 0);
        static float L(float a, float b, float t) => F(a + F(F(b - a) * t));
        static Vector3 L(Vector3 a, Vector3 b, float t) => new Vector3(L(a.x,b.x,t),L(a.y,b.y,t),L(a.z,b.z,t));
        // Original 0x4eca188 operation order (PlaneUp * sampled), preserving
        // each scalar SSE rounding rather than regrouping Unity's operator.
        static Quaternion PlaneRotation(Quaternion q)
        {
            var p = PlaneUp;
            return new Quaternion(
                F(F(F(F(q.x*p.w)+F(q.w*p.x))+F(q.z*p.y))-F(q.y*p.z)),
                F(F(F(F(q.w*p.y)+F(q.y*p.w))+F(q.x*p.z))-F(q.z*p.x)),
                F(F(F(F(q.w*p.z)+F(q.z*p.w))+F(q.y*p.x))-F(q.x*p.y)),
                F(F(F(F(q.w*p.w)-F(q.x*p.x))-F(q.y*p.y))-F(q.z*p.z)));
        }
        public LiveCharaCollisionRuntime(Transform parent, int count)
        {
            States = new LiveCharaCollisionState[count];
            for(int i=0;i<count;i++) States[i]=new LiveCharaCollisionState(parent,i);
        }
        public bool Evaluate(IList<LiveTimelineCharaCollisionData> groups, int frame, TimelinePlayerMode mode)
        {
            if(groups.Count!=States.Length)throw new InvalidOperationException("Collision track count changed without reset");
            bool changed=false;
            for(int i=0;i<groups.Count;i++)
            {
                var keys=groups[i]?.keys;
                if(keys==null||keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable)||!keys.EnablePlayModeTimeline(mode))continue;
                LiveTimelineControl.FindTimelineKey(out var current,out var next,keys,frame);
                if(current==null)continue;
                var key=(LiveTimelineKeyCharaCollisionData)current;
                var state=States[i];
                state.IsEnable=key.IsEnable;state.ApplyHead=key.IsApplyHead;state.ApplyTail=key.IsApplyTail;state.CharacterFlag=(int)key.CharacterFlag;
                var position=key.Position;var offset=key.Offset;var offset2=key.Offset2;var rotation=key.Rotation;float radius=key.Radius;
                if(next is LiveTimelineKeyCharaCollisionData end && end.IsInterpolateKey())
                {
                    float t=LiveTimelineControl.CalculateInterpolationValue(key,end,frame);
                    position=L(position,end.Position,t);offset=L(offset,end.Offset,t);offset2=L(offset2,end.Offset2,t);radius=L(radius,end.Radius,t);
                    rotation=Quaternion.Lerp(rotation,end.Rotation,t);
                }
                if(((int)key.attribute&0x10000)!=0&&key.CollisionType==CySpringCollisionData.CollisionType.Plane)rotation=PlaneRotation(rotation);
                state.LocalPosition=new Vector3(F(position.x+offset.x),F(position.y+offset.y),F(position.z+offset.z));state.LocalRotation=rotation;state.HasGeometry=true;
                state.Object.transform.localPosition=state.LocalPosition;state.Object.transform.localRotation=rotation;
                var scale=LossyScale!=null?LossyScale(state.Object.transform):state.Object.transform.lossyScale;
                var delta=new Vector3(F(F(offset2.x-offset.x)*scale.x),F(F(offset2.y-offset.y)*scale.y),F(F(offset2.z-offset.z)*scale.z));
                state.Data.CollisionType=key.CollisionType;state.Data.IsInner=key.IsInner;
                state.Data.SetTimelineGeometry(radius,delta);
                changed|=state.KeyFrame!=key.frame;state.KeyFrame=key.frame;
            }
            if(changed)
                for(int slot=0;slot<20;slot++)
                {
                    var body=new List<CySpringCollisionRuntimeData>();var head=new List<CySpringCollisionRuntimeData>();var tail=new List<CySpringCollisionRuntimeData>();
                    foreach(var s in States)if(s.IsEnable&&(s.CharacterFlag&(1<<slot))!=0)
                    {body.Add(s.Data);if(s.ApplyHead)head.Add(s.Data);if(s.ApplyTail)tail.Add(s.Data);}
                    // Model iteration belongs to the host; body/head/tail order
                    // for every model is preserved by the control adapter.
                    Apply?.Invoke(slot,CySpringController.Parts.Body,body.ToArray());
                    Apply?.Invoke(slot,CySpringController.Parts.Head,head.ToArray());
                    Apply?.Invoke(slot,CySpringController.Parts.Tail,tail.ToArray());
                }
            return changed;
        }
        public void Dispose()
        {
            foreach(var state in States)if(state.Object!=null)
            {if(Application.isPlaying)UnityEngine.Object.Destroy(state.Object);else UnityEngine.Object.DestroyImmediate(state.Object);}
        }
    }
    public partial class LiveTimelineControl
    {
        LiveCharaCollisionRuntime _charaCollisionRuntime;
        LiveTimelineWorkSheet _charaCollisionSheet;
        public LiveCharaCollisionRuntime CharaCollisionRuntime => _charaCollisionRuntime;
        public Func<int,IEnumerable<CySpringController>> CharaCollisionModelResolver;
        public event Action<int,CySpringController,CySpringController.Parts,CySpringCollisionRuntimeData[]> OnCharaCollisionApplied;
        public void ResetCharaCollisionConsumers()
        {
            _charaCollisionRuntime?.Dispose();_charaCollisionRuntime=null;_charaCollisionSheet=null;
        }
        public void InitializeCharaCollisionConsumers(LiveTimelineWorkSheet sheet)
        {
            ResetCharaCollisionConsumers();
            if(sheet?.CharaCollisionDataList==null)return;
            _charaCollisionSheet=sheet;
            _charaCollisionRuntime=new LiveCharaCollisionRuntime(Director.instance!=null?Director.instance.transform:transform,sheet.CharaCollisionDataList.Count);
        }
        void OnDestroy() { ResetCharaCollisionConsumers(); }
        void AlterUpdate_CharaCollision(LiveTimelineWorkSheet sheet,int frame)
        {
            if(sheet?.CharaCollisionDataList==null)return;
            if(_charaCollisionRuntime==null||_charaCollisionSheet!=sheet)
            {
                InitializeCharaCollisionConsumers(sheet);
            }
            var pending=new CySpringCollisionRuntimeData[20][][];
            _charaCollisionRuntime.Apply=(slot,part,data)=>
            {
                if(pending[slot]==null)pending[slot]=new CySpringCollisionRuntimeData[3][];
                pending[slot][(int)part]=data;
            };
            if(!_charaCollisionRuntime.Evaluate(sheet.CharaCollisionDataList,frame,_playMode))return;
            for(int slot=0;slot<20;slot++)
            {
                var models=CharaCollisionModelResolver!=null?CharaCollisionModelResolver(slot):LiveCharaNodeTimeline.ResolveAssembledModels(Director.instance,slot);
                if(models==null)continue;
                foreach(var model in models)if(model!=null)
                    foreach(var part in new[]{CySpringController.Parts.Body,CySpringController.Parts.Head,CySpringController.Parts.Tail})
                    {
                        var data=(CySpringCollisionRuntimeData[])pending[slot][(int)part].Clone();
                        switch(part)
                        {
                            case CySpringController.Parts.Body:model.ApplyEnvCySpringCollisionToBody(data);break;
                            case CySpringController.Parts.Head:model.ApplyEnvCySpringCollisionToHead(data);break;
                            case CySpringController.Parts.Tail:model.ApplyEnvCySpringCollisionToTail(data);break;
                        }
                        OnCharaCollisionApplied?.Invoke(slot,model,part,data);
                    }
            }
        }
    }
}
