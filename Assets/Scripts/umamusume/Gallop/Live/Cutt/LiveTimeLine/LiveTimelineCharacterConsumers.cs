using System;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    public partial class LiveTimelineControl
    {
        LiveMotionNoiseRuntime _motionNoise;
        Transform[][] _nodeScaleTargets;
        public LiveMotionNoiseRuntime MotionNoise=>_motionNoise;
        public int NodeScaleDispatchCount {get;private set;}
        public NodeScaleUpdateInfo LastNodeScale {get;private set;}
        void InitializeCharacterConsumers()
        {
            _motionNoise=new LiveMotionNoiseRuntime();
            _motionNoise.UpdateCySpring=UpdateCharaMotionNoiseCySpring;
        }
        void UpdateCharaMotionNoiseCySpring(LiveTimelineKeyCharaMotionNoiseData key,bool perItem)
        {
            var cast=Director.instance.CharaContainerScript;
            for(int slot=0;slot<cast.Count&&slot<20;slot++)
            {
                bool tail=key==null||key.GetEnableCySpring(slot,perItem,true);
                bool skirt=key==null||key.GetEnableCySpring(slot,perItem,false);
                var spring=cast[slot].GetComponent<CySpringController>();
                if(spring!=null)
                {
                    spring.SetEnableCySpringBone(tail,CySpringController.Parts.Tail);
                    spring.SetEnableLiveSkirtCySpring(skirt);
                    int kind=key!=null&&slot<key.cySpringGravityScaleType.Length?key.cySpringGravityScaleType[slot]:0;
                    spring.ApplyLiveMotionNoiseGravity(kind,kind==0?null:key.cySpringGravityScaleBoneName[slot],
                        kind==0?0f:key.cySpringGravityScale[slot],kind==0?Vector4.zero:new Vector4(
                            key.cySpringExpressionKneeCollisionRadius[slot],key.cySpringExpressionAnkleCollisionRadius[slot],
                            key.cySpringExpressionInfluenceAngle[slot],key.cySpringExpressionInfluenceMaxAngle[slot]));
                }
            }
        }
        void InitializeNodeScaleTargets()
        {
            _nodeScaleTargets=new Transform[20][];
            var director=Director.instance;
            for(int slot=0;slot<director.CharaContainerScript.Count&&slot<20;slot++)
            {
                var actor=director.CharaContainerScript[slot];
                if(actor.LiveLocator==null)throw new InvalidOperationException("NodeScale requires initialized character locators");
                var bones=actor.LiveLocator.Bones;
                // Original OnUpdateNodeScale 0x4e2eed0 reads model offsets
                // 0x688/0x690/0x698/0x6a0. These are wrist/hand attachment
                // pairs, not the head/neck/chest/waist cache at 0x668..0x680.
                bones.TryGetValue("Wrist_L",out var leftWrist);
                bones.TryGetValue("Hand_Attach_L",out var leftAttach);
                bones.TryGetValue("Wrist_R",out var rightWrist);
                bones.TryGetValue("Hand_Attach_R",out var rightAttach);
                _nodeScaleTargets[slot]=new[]{leftWrist,leftAttach,rightWrist,rightAttach};
            }
        }
        void AlterUpdate_CharaMotionNoise()
        {
            var keys=CurrentWorkSheet.charaMotionNoiseKeys;
            if(keys==null)return;
            if(_motionNoise==null)InitializeCharacterConsumers();
            var key=keys.FindKeyCached(_currentFrame,true).key as LiveTimelineKeyCharaMotionNoiseData;
            _motionNoise.Evaluate(key,keys,_deltaTime);
        }
        void AlterUpdate_NodeScale(LiveTimelineWorkSheet sheet,int frame)
        {
            if(sheet.nodeScaleList==null)return;
            if(_nodeScaleTargets==null)InitializeNodeScaleTargets();
            foreach(var group in sheet.nodeScaleList)
                if(LiveNodeScaleTimeline.Evaluate(group,frame,_playMode,out var info))
                {
                    LiveNodeScaleTimeline.Apply(info,slot=>_nodeScaleTargets[slot]);
                    LastNodeScale=info;NodeScaleDispatchCount++;
                }
        }
    }
}
