using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live.Cutt
{
    public interface ILiveSystemTextBodyPlayer
    {
        int Mode{get;set;}
        void StopLegacy();void ResetRace();void SetCullingMode(int mode);void PreparePlayer();void EnablePlayer();
        bool HasClip(object clip);float Length(object clip);
        void SetClip(int state,object clip);int GetState();void ToggleMainState();
        void Play(int state,float time);void CrossFade(int state,float time,float duration);
        void SetPlayTime(float time);void UpdateMotion(float deltaTime);void RequestBlink();
    }
    // LiveCharacterAnimation SystemText body state. Clip/graph operations are
    // delegated to the same owner that holds the real loaded resource cache.
    public sealed class LiveSystemTextBodyRuntime
    {
        public const float BlendDuration=.23f;
        readonly ILiveSystemTextBodyPlayer player;
        readonly Func<int,bool,IReadOnlyList<object>> getClips;
        readonly Action<int,IReadOnlyList<object>> setSecondClips;
        readonly Func<LiveSystemTextMotionSet,bool> hasBodyMotion;
        public int SystemTextId,PlayType;
        public LiveSystemTextMotionSet PreviousMotion,CurrentMotion,SecondMotion,IdleMotion;
        public IReadOnlyList<object> CurrentClips,IdleClips;
        public bool SameSecondBody,PlayingSecond,TargetSecond;
        public float SecondStart,SecondBase,TargetTime;
        public int TargetState=-1;
        public Action OnSecond;
        static float F(float value)=>LiveTimelineControl.MonitorFloat32(value);
        static float MaxZero(float value)=>Mathf.Max(0f,value);
        public LiveSystemTextBodyRuntime(ILiveSystemTextBodyPlayer player,Func<int,bool,IReadOnlyList<object>> getClips,
            Action<int,IReadOnlyList<object>> setSecondClips,Func<LiveSystemTextMotionSet,bool> hasBodyMotion)
        {this.player=player;this.getClips=getClips;this.setSecondClips=setSecondClips;this.hasBodyMotion=hasBodyMotion;}
        void SetBodyClip(LiveSystemTextMotionSet motion,bool second)
        {
            if(!hasBodyMotion(motion))return;
            CurrentClips=getClips(SystemTextId,second);
            if(CurrentClips==null||CurrentClips.Count==0)return;
            CurrentMotion=motion;
            switch(motion.BodyMotionPlayType)
            {
                case 1:PlayType=1;player.SetClip(0,CurrentClips[0]);break;
                case 2:
                    if(CurrentClips.Count<3)return;
                    PlayType=2;player.SetClip(1,CurrentClips[0]);player.SetClip(2,CurrentClips[1]);player.SetClip(3,CurrentClips[2]);break;
                case 4:PlayType=4;player.SetClip(2,CurrentClips[0]);break;
            }
        }
        public void Setup(int id,LiveSystemTextMotionSet motion,LiveSystemTextMotionSet second,float secondStart,Action onSecond)
        {
            if(player.Mode==0)player.StopLegacy();else if(player.Mode==2){player.ResetRace();player.SetCullingMode(1);}
            player.PreparePlayer();player.EnablePlayer();player.Mode=1;SystemTextId=id;PreviousMotion=null;
            SecondMotion=second;SecondStart=second!=null?secondStart:0f;OnSecond=onSecond;PlayingSecond=false;
            SetBodyClip(motion,false);SecondBase=0f;TargetSecond=false;TargetState=-1;TargetTime=0f;
            if(motion.BodyMotionPlayType==1&&second==null&&IdleMotion!=null&&IdleClips!=null&&CurrentClips!=null&&
                CurrentClips.Count>0&&player.HasClip(CurrentClips[0]))
            {
                SecondMotion=IdleMotion;SecondStart=MaxZero(F(player.Length(CurrentClips[0])-BlendDuration));
                setSecondClips(id,IdleClips);
            }
            SameSecondBody=SecondMotion!=null&&SecondMotion.BodyMotion==motion.BodyMotion;
        }
        float SecondDelay(IReadOnlyList<object> clips)=>clips!=null&&clips.Count>=3?MaxZero(F(player.Length(clips[2])-BlendDuration)):0f;
        void ApplySecond(float time)
        {
            if(SecondMotion==null||PlayingSecond||CurrentMotion==null)return;
            if(SameSecondBody)
            {
                if(SecondStart>time)return;
                PreviousMotion=CurrentMotion;CurrentMotion=SecondMotion;
            }
            else
            {
                SecondBase=F(SecondStart+SecondDelay(CurrentClips));if(time<SecondBase)return;
                PreviousMotion=CurrentMotion;
                if(TargetState==-1)
                {
                    float after=MaxZero(F(time-SecondBase));
                    switch(PlayType)
                    {
                        case 1:player.Play(0,F(SecondBase-after));break;
                        case 2:player.Play(3,F(SecondDelay(CurrentClips)-after));break;
                        case 4:player.Play(2,F(SecondBase-after));break;
                    }
                }
                player.ToggleMainState();SetBodyClip(SecondMotion,true);
            }
            OnSecond?.Invoke();PlayingSecond=true;
        }
        void UpdateTarget(float time)
        {
            ApplySecond(time);
            bool second=PlayingSecond&&!SameSecondBody;
            float current=MaxZero(F(time-(second?SecondBase:0f)));
            switch(PlayType)
            {
                case 1:TargetSecond=second;TargetState=0;TargetTime=current;break;
                case 4:TargetSecond=second;TargetState=2;TargetTime=current;break;
                case 2:
                    TargetSecond=second;
                    if(SecondMotion!=null&&time>=SecondStart&&!PlayingSecond&&!SameSecondBody)
                    {TargetState=3;TargetTime=MaxZero(F(time-SecondStart));}
                    else
                    {
                        float length=player.Length(CurrentClips[0]);
                        if(length>current){TargetState=1;TargetTime=current;}
                        else{TargetState=2;TargetTime=F(current-player.Length(CurrentClips[0]));}
                    }
                    break;
            }
        }
        public float Update(float time,float deltaTime)
        {
            bool previousSecond=TargetSecond;int previousState=TargetState;
            UpdateTarget(time);
            bool changeSecond=!previousSecond&&TargetSecond,toEnd=previousState!=3&&TargetState==3;
            if(toEnd&&previousState!=2&&PlayType==2&&CurrentClips!=null&&CurrentClips.Count>0)
                player.Play(2,MaxZero(F(SecondStart-player.Length(CurrentClips[0]))));
            bool crossFade=changeSecond||toEnd;
            if(TargetState!=-1&&(player.GetState()!=TargetState||changeSecond))
            {
                if(crossFade){player.CrossFade(TargetState,0f,BlendDuration);if(TargetState==3)player.RequestBlink();}
                else player.Play(TargetState,0f);
            }
            float target=changeSecond?MaxZero(F(time-SecondBase)):TargetTime;
            if(crossFade)deltaTime=Mathf.Min(deltaTime,MaxZero(target));
            player.SetPlayTime(F(TargetTime-deltaTime));player.UpdateMotion(deltaTime);return deltaTime;
        }
    }
}
