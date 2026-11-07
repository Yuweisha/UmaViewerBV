using System;
using UnityEngine;

namespace Gallop.Live
{
    // Both AudioSource.time and the main-thread DSP observation advance in
    // mixer blocks. Interpolate that epoch with the monotonic host clock;
    // constrain corrections to the actual output queue's duration.
    internal sealed class LiveSongPlaybackClock
    {
        double start, offset, duration, remainingDelay;
        double dspAnchor, realAnchor, maxSkew;
        bool scheduled, paused;
        public bool IsScheduled => scheduled;
        public void Start(double dspStart, double position, double length,
            double dspTime, double realTime, double queueDuration)
        {
            start=dspStart;offset=position;duration=length;remainingDelay=0;
            dspAnchor=dspTime;realAnchor=realTime;maxSkew=queueDuration;
            scheduled=true;paused=false;
        }
        double Interpolate(double dspTime,double realTime)
        {
            double predicted=dspAnchor+System.Math.Max(0,realTime-realAnchor);
            double corrected=System.Math.Max(dspTime-maxSkew,System.Math.Min(dspTime+maxSkew,predicted));
            dspAnchor=corrected;realAnchor=realTime;
            return corrected;
        }
        public float Read(double dspTime,double realTime)
            => (float)System.Math.Min(duration,offset+(paused?0:System.Math.Max(0,Interpolate(dspTime,realTime)-start)));
        public void Pause(double dspTime,double realTime)
        {
            if(!scheduled||paused)return;
            double now=Interpolate(dspTime,realTime);
            remainingDelay=System.Math.Max(0,start-now);
            offset=System.Math.Min(duration,offset+System.Math.Max(0,now-start));
            paused=true;
        }
        public void Resume(double dspTime,double realTime)
        {
            if(!scheduled||!paused)return;
            start=Interpolate(dspTime,realTime)+remainingDelay;paused=false;
        }
    }

    public partial class Director
    {
        internal readonly LiveSongPlaybackClock SongPlaybackClock=new LiveSongPlaybackClock();
    }

    // Main-live adapter to the viewer's decoded music AudioSource. CRI and
    // opening/ending cue lookup require their original installer separately.
    internal sealed class DirectorLiveTimeSource:ILiveTimeControllerSource
    {
        readonly Director _owner;
        internal DirectorLiveTimeSource(Director owner){_owner=owner;}
        internal AudioSource MusicSource
        {
            get
            {
                var sources=_owner.liveMusic?.sourceList;
                return sources!=null&&sources.Count>0?sources[0]:null;
            }
        }
        public bool GetDirector()=>_owner!=null;
        public bool HasDirector()=>_owner!=null;
        public bool GetAudioManager()=>MusicSource!=null&&MusicSource.clip!=null;
        public float LiveTotalTime=>_owner.totalTime;
        public int SheetIndex=>1;
        public bool IsPlayingSong=>MusicSource!=null&&MusicSource.isPlaying;
        public float SongPlaybackTime=>_owner.SongPlaybackClock.IsScheduled
            ?_owner.SongPlaybackClock.Read(AudioSettings.dspTime,Time.realtimeSinceStartupAsDouble):MusicSource.time;
        public void SelectSkitCue(bool opening)=>throw Missing();
        public bool IsBgmPlaying=>throw Missing();
        public long BgmMilliseconds=>throw Missing();
        public bool PauseSong()=>LiveSongAudioControl.Pause(_owner,true);
        public bool ResumeSong()=>LiveSongAudioControl.Pause(_owner,false);
        public void NotifyDirectorPause(bool paused)=>_owner.PauseLive(paused);
        static NotSupportedException Missing()=>new NotSupportedException("Original skit cue lookup is not installed in the main-live adapter.");
    }
}
