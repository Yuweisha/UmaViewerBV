using System;
using System.Collections.Generic;
using IK;
using UnityEngine;
using Gallop.Live.Cutt;

namespace Gallop.Live
{
    public sealed class LiveLimbIKRuntime
    {
        public enum UpdatePhase { LateUpdate=0,PreLateUpdateIK1=1 }
        public readonly LiveGallopTwoBoneIKRuntime[] Limbs=new LiveGallopTwoBoneIKRuntime[4];
        public LiveIKCollisionRuntime Collision;
        public UpdatePhase Phase;
        public bool Enabled=true;
        public bool Paused;

        public LiveLimbIKRuntime(Func<string,Transform> find)
        {
            string[] ends={"Wrist_L","Wrist_R","Ankle_L","Ankle_R"};
            string[] poles={"Wrist_L_Pole","Wrist_R_Pole","Foot_L_Pole","Foot_R_Pole"};
            string[] targets={"Wrist_L_Target","Wrist_R_Target","Foot_L_Target","Foot_R_Target"};
            for(int i=0;i<4;i++)
            {
                var limb=Limbs[i]=new LiveGallopTwoBoneIKRuntime();
                limb.Initialize(find(ends[i]),find(poles[i]),find(targets[i]));
            }
        }

        public void RecalcJointDistance(){foreach(var limb in Limbs)limb.RecalcJointDistance();}
        public void ResetTargets()
        {
            // GallopLimbIK.ResetTargetTransform 0x51b2700, called by
            // LiveModelController.ResetIKTargetTransform on a changed motion key.
            foreach(var limb in Limbs)
            {
                var target=limb.TargetTransform;
                target.localPosition=Vector3.zero;
                target.localRotation=Quaternion.identity;
                target.localScale=Vector3.one;
            }
        }
        public void Tick(UpdatePhase phase)
        {
            // The registry gates IsEnable. Original phase callbacks do not
            // suppress this solver merely because OnPause stored a flag.
            if(!Enabled||phase!=Phase)return;
            Collision?.UpdatePosture();
            foreach(var limb in Limbs)limb.Solve(Collision);
        }
    }

    // Loaded LiveModelController's foot-node/GallopLimbIK/LiveIK initialization.
    // Strategy updates and native component-registry scheduling remain separate.
    public sealed partial class LiveModelIKRuntime
    {
        public readonly Dictionary<string,Transform> NodeCache;
        public readonly Transform[] CreatedFootNodes;
        public readonly LiveLimbIKRuntime Limb;
        public readonly float InitialHeightRate;
        public readonly bool HasIKCollision;
        public int Type { get; private set; }
        public readonly Transform Position;
        readonly Func<float> _totalScaleProvider;
        public float TotalScale=>_totalScaleProvider();

        public LiveModelIKRuntime(Transform position,Dictionary<string,Transform> cache,
            IKCollisionData body,IKCollisionData head,float totalScale,float bodyScale,Func<float> totalScaleProvider=null)
        {
            Position=position;_totalScaleProvider=totalScaleProvider??(()=>totalScale);
            NodeCache=cache;CreatedFootNodes=AddFootNodes(position,cache);
            Transform Find(string name)=>cache.TryGetValue(name,out var node)?node:null;
            Limb=new LiveLimbIKRuntime(Find);
            Limb.Phase=LiveLimbIKRuntime.UpdatePhase.PreLateUpdateIK1;
            Limb.Enabled=false;
            InitialHeightRate=GetHeightRate(totalScale);
            var collision=LiveIKCollisionRuntime.Create(body,head,totalScale,bodyScale,IKCollisionData.Parameter.Scene.Live,Find);
            if(collision!=null)Limb.Collision=collision;
            HasIKCollision=Limb.Collision!=null;
        }

        public static float GetHeightRate(float totalScale)
        {
            float minimum=BitConverter.Int32BitsToSingle(unchecked((int)0x3f53b646));
            float range=BitConverter.Int32BitsToSingle(unchecked((int)0x3ead0e54));
            return Mathf.Clamp01(LiveTimelineControl.MonitorFloat32(
                LiveTimelineControl.MonitorFloat32(totalScale-minimum)/range));
        }

        public static Transform[] AddFootNodes(Transform parent,IDictionary<string,Transform> cache)
        {
            string[] names={"Foot_L_Pole","Foot_R_Pole","Foot_L_Target","Foot_R_Target"};
            var result=new Transform[names.Length];
            for(int i=0;i<names.Length;i++)
            {
                var node=result[i]=new GameObject(names[i]).transform;
                node.SetParent(parent,false);node.localPosition=Vector3.zero;node.localRotation=Quaternion.identity;node.localScale=Vector3.one;
                if(!cache.ContainsKey(names[i]))cache.Add(names[i],node);
            }
            return result;
        }
    }
}
