using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using AnimateToUnity;
using Gallop.Live;
using UnityEngine;

namespace Gallop
{
    public sealed class FlashActionPlayer : MonoBehaviour
    {
        [Serializable] public sealed class ParticleStopParameter
        {public string LinkMotionObjectName,LinkMotionLabel;public bool IsDestroy;}
        [Serializable] public sealed class ActionParameter
        {
            public string MotionObjectName;public bool IsFullMatchMotionObjectName;public string MotionLabel;public int ActionType;
            public GameObject InstantiatePrefab;public string InstantiatePrefabPath;
            public float InstantiatePrefabDelayTime,InstantiatePrefabLifeTime;public Vector2 InstantiatePrefabPositionOffset;
            public int InstantiatePrefabSortingOrderOffset;public bool InstantiatePrefabIsAdditionalOffset;public float InstantiatePrefabParticleStopTime;
            public string InstantiatePrefabParticleStopLinkMotionObjectName,InstantiatePrefabParticleStopLinkMotionLabel;
            public bool IsDestroyInstantiatePrefabOnLinkMotion;public List<ParticleStopParameter> InstantiatePrefabParticleStopParameterList;
            public int AudioId;public ParticleSystem[] ParticleArray;
            [NonSerialized] public bool IsEnable=true,KeepPrefabInstance;
            [NonSerialized] public GameObject InstantiatePrefabInstance;
            [NonSerialized] public AnMotionInstance TargetMotion;
        }
        [SerializeField] GameObject _flashPrefab;
        [SerializeField] string _flashPrefabPath;
        [SerializeField] List<ActionParameter> _actionParameterList;
        public FlashPlayer FlashPlayer{get;private set;}
        int _playSeekLastLabelIndex=-1;
        public Action<ParticleSystem[]> OnInstantiatedParticles;
        public Action<int> OnAudio;
#if UNITY_EDITOR
        public Action<ActionParameter,float> ParticleSimulationObserver;
        public Action<string,float,string> PlayObserver;
        public Action<ActionParameter,string,int> ActionObserver;
#endif
        public IReadOnlyList<ActionParameter> Actions=>_actionParameterList;
        AnMotionInstance GetMotion(string name,bool fullMatch=false)=>FlashPlayer.Root.Motions.FirstOrDefault(x=>fullMatch?x.Transform.name==name:x.Transform.name.Contains(name));
        public void LoadFlashPlayer()
        {
            if(FlashPlayer!=null)return;
            var prefab=_flashPrefab!=null?_flashPrefab:LiveFlashResourceUtility.LoadOnView<GameObject>(_flashPrefabPath);
            if(prefab==null)throw new InvalidOperationException("Flash action prefab missing: "+_flashPrefabPath);
            FlashPlayer=new FlashPlayer(Instantiate(prefab,transform,false));
            foreach(var parameter in _actionParameterList)
            {
                if(string.IsNullOrEmpty(parameter.MotionObjectName)||string.IsNullOrEmpty(parameter.MotionLabel))continue;
                parameter.TargetMotion=GetMotion(parameter.MotionObjectName,parameter.IsFullMatchMotionObjectName);
                if(parameter.TargetMotion!=null)parameter.TargetMotion.AddAction(parameter.MotionLabel,parameter.ActionType,()=>OnAction(parameter));
            }
        }
        public void PlaySeek(string label,float time)
        {
            var action=_actionParameterList.FirstOrDefault(x=>x.MotionObjectName=="MOT_root"&&x.MotionLabel==label);
            if(action!=null)
            {
                var motion=FlashPlayer.Root.FindMotion(action.MotionObjectName);
                if(motion!=null)
                {
                    int index=Array.FindIndex(motion.Parameter._labelParamList,x=>x._name==label);
                    if(index!=_playSeekLastLabelIndex){_playSeekLastLabelIndex=index;motion.Play(action.MotionLabel);}
                }
            }
            FlashPlayer.SetCurrentTimeAllMotion(time);
        }
        public void SetKeepInstanceAll(bool keepInstance)
        {foreach(var parameter in _actionParameterList)parameter.KeepPrefabInstance=keepInstance;}
        public void Play(string label,float timeOffset=0f,string objectName=null)
        {
#if UNITY_EDITOR
            PlayObserver?.Invoke(label,timeOffset,objectName);
#endif
            if(string.IsNullOrEmpty(objectName))objectName="MOT_root";
            var action=_actionParameterList.FirstOrDefault(x=>x.MotionObjectName==objectName&&x.MotionLabel==label);
            var motion=action!=null?GetMotion(action.MotionObjectName,action.IsFullMatchMotionObjectName):FlashPlayer.Root.RootMotion;
            motion?.Play(action!=null?action.MotionLabel:label,timeOffset);
        }
        void OnAction(ActionParameter parameter)
        {
            if(!parameter.IsEnable)return;
#if UNITY_EDITOR
            ActionObserver?.Invoke(parameter,"callback",parameter.InstantiatePrefabInstance!=null?parameter.InstantiatePrefabInstance.GetInstanceID():0);
#endif
            var prefab=parameter.InstantiatePrefab!=null?parameter.InstantiatePrefab:LiveFlashResourceUtility.LoadOnView<GameObject>(parameter.InstantiatePrefabPath);
            if(prefab!=null)
            {
                if(parameter.InstantiatePrefabInstance!=null&&!parameter.KeepPrefabInstance)
                {Destroy(parameter.InstantiatePrefabInstance);parameter.InstantiatePrefabInstance=null;parameter.ParticleArray=null;}
                if(parameter.InstantiatePrefabDelayTime>0f)StartCoroutine(InstantiateDelayed(parameter,prefab));
                else InstantiateAction(parameter,prefab);
            }
            if(parameter.AudioId!=-1)OnAudio?.Invoke(parameter.AudioId);
        }
        IEnumerator InstantiateDelayed(ActionParameter parameter,GameObject prefab)
        {yield return new WaitForSecondsRealtime(parameter.InstantiatePrefabDelayTime);InstantiateAction(parameter,prefab);}
        void InstantiateAction(ActionParameter parameter,GameObject prefab)
        {
            if(parameter.TargetMotion==null)return;
            bool create=parameter.InstantiatePrefabInstance==null||!parameter.KeepPrefabInstance;
            if(parameter.InstantiatePrefabInstance==null||!parameter.KeepPrefabInstance)
                parameter.InstantiatePrefabInstance=Instantiate(prefab,parameter.TargetMotion.Transform,false);
#if UNITY_EDITOR
            ActionObserver?.Invoke(parameter,create?"create":"reuse",parameter.InstantiatePrefabInstance.GetInstanceID());
#endif
            var instance=parameter.InstantiatePrefabInstance;var target=instance.transform;
            target.localPosition=new Vector3(parameter.InstantiatePrefabPositionOffset.x,parameter.InstantiatePrefabPositionOffset.y,0f);target.localRotation=Quaternion.identity;target.localScale=Vector3.one;
            foreach(var child in instance.GetComponentsInChildren<Transform>(true))child.gameObject.layer=parameter.TargetMotion.Transform.gameObject.layer;
            if(parameter.InstantiatePrefabLifeTime>0f)Destroy(instance,parameter.InstantiatePrefabLifeTime);
            var particles=instance.GetComponentsInChildren<ParticleSystem>();
            foreach(var particle in particles)
            {
                var renderer=particle.GetComponent<Renderer>();if(renderer==null)continue;
                renderer.sortingLayerName="UI";
                renderer.sortingOrder=FlashPlayer.Root._parameter._sortOffset+parameter.InstantiatePrefabSortingOrderOffset+(parameter.InstantiatePrefabIsAdditionalOffset?renderer.sortingOrder:0);
            }
            SetStopLink(parameter,particles,parameter.InstantiatePrefabParticleStopLinkMotionObjectName,parameter.InstantiatePrefabParticleStopLinkMotionLabel,parameter.IsDestroyInstantiatePrefabOnLinkMotion);
            if(parameter.InstantiatePrefabParticleStopParameterList!=null)
                foreach(var link in parameter.InstantiatePrefabParticleStopParameterList)SetStopLink(parameter,particles,link.LinkMotionObjectName,link.LinkMotionLabel,link.IsDestroy);
            if(parameter.InstantiatePrefabParticleStopTime>0f)StartCoroutine(StopDelayed(parameter,particles));
            OnInstantiatedParticles?.Invoke(particles);parameter.ParticleArray=particles;
            foreach(var particle in particles)if(FlashPlayer.Root.Paused)particle.Pause(true);else particle.Play(true);
        }
        IEnumerator StopDelayed(ActionParameter parameter,ParticleSystem[] particles)
        {yield return new WaitForSecondsRealtime(parameter.InstantiatePrefabParticleStopTime);StopParticles(parameter,particles,false);}
        void SetStopLink(ActionParameter parameter,ParticleSystem[] particles,string motionName,string label,bool destroy)
        {
            if(string.IsNullOrEmpty(motionName)||string.IsNullOrEmpty(label))return;
            GetMotion(motionName)?.AddAction(label,0,()=>StopParticles(parameter,particles,destroy));
        }
        void StopParticles(ActionParameter parameter,ParticleSystem[] particles,bool destroy)
        {
#if UNITY_EDITOR
            ActionObserver?.Invoke(parameter,destroy?"destroy":"stop",parameter.InstantiatePrefabInstance!=null?parameter.InstantiatePrefabInstance.GetInstanceID():0);
#endif
            if(destroy)
            {
                if(parameter.InstantiatePrefabInstance!=null){Destroy(parameter.InstantiatePrefabInstance);parameter.InstantiatePrefabInstance=null;parameter.ParticleArray=null;}
                return;
            }
            foreach(var particle in particles)if(particle!=null)
            {
#if UNITY_EDITOR
                ActionObserver?.Invoke(parameter,"particleStop",parameter.InstantiatePrefabInstance!=null?parameter.InstantiatePrefabInstance.GetInstanceID():0);
#endif
                particle.Stop();
            }
        }
        public void UpdateParticle()
        {
            foreach(var parameter in _actionParameterList)
            {
                if(parameter.TargetMotion==null)parameter.TargetMotion=GetMotion(parameter.MotionObjectName,parameter.IsFullMatchMotionObjectName);
                if(parameter.TargetMotion==null||parameter.ParticleArray==null)continue;
                var label=parameter.TargetMotion.Parameter._labelParamList.FirstOrDefault(x=>x._name==parameter.MotionLabel);if(label==null)continue;
                float time=Mathf.Max(0f,parameter.TargetMotion.Clock.CurrentTime-label._timeRange.x);
                foreach(var particle in parameter.ParticleArray)if(particle!=null)
                {
#if UNITY_EDITOR
                    ParticleSimulationObserver?.Invoke(parameter,time);
#endif
                    // ActionParameter.SetParticleTime (4c14420) explicitly uses
                    // fixedTimeStep=false. Keep short, fast UI bursts on the
                    // authored seek time instead of the physics-step grid.
                    particle.Simulate(time,true,true,false);
                }
            }
        }
        public void Reset(){StopAllCoroutines();foreach(var parameter in _actionParameterList)if(parameter.InstantiatePrefabInstance!=null)Destroy(parameter.InstantiatePrefabInstance);_playSeekLastLabelIndex=-1;FlashPlayer.Reset();}
    }
}
