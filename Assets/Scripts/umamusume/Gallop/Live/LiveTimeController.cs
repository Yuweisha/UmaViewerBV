using System;
using System.Runtime.CompilerServices;

namespace Gallop.Live
{
    // Original singleton/audio/Director calls are explicit input boundaries.
    public interface ILiveTimeControllerSource
    {
        bool GetDirector();
        bool HasDirector();
        bool GetAudioManager();
        float LiveTotalTime { get; }
        int SheetIndex { get; }
        void SelectSkitCue(bool opening);
        bool IsBgmPlaying { get; }
        long BgmMilliseconds { get; }
        bool IsPlayingSong { get; }
        float SongPlaybackTime { get; }
        bool PauseSong();
        bool ResumeSong();
        void NotifyDirectorPause(bool paused);
    }

    public sealed class LiveTimeController
    {
        private readonly ILiveTimeControllerSource _source;
        private readonly float SmoothingTargetDeltaTime=BitConverter.Int32BitsToSingle(0x3d088889);
        private readonly float SmoothingDeltaTimeThreshold=BitConverter.Int32BitsToSingle(0x3a1d4952);
        private readonly float SmoothingOvertimeClamp=BitConverter.Int32BitsToSingle(0x3d4cccce);
        private float _musicTime, _smoothMusicTime, _smoothingWorkTime, _lastMusicTimeWithoutDelay;
        private float _currentTime, _prevTime, _deltaTime, _elapsedTime;
        private bool _isEnabledSmooth=true, _doCalcElapsedTime, _isPause;
        public float MusicTime=>_musicTime;
        public float SmoothMusicTime=>_smoothMusicTime;
        public float CurrentTime=>_currentTime;
        public float DeltaTime=>_deltaTime;
        public float DeltaTimePauseReset=>_isPause?0f:_deltaTime;
        public bool IsCalcElapsedTime=>_doCalcElapsedTime;
        public bool IsPause=>_isPause;
        public LiveTimeController(ILiveTimeControllerSource source) { _source=source??throw new ArgumentNullException(nameof(source)); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static float F(float v)=>BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(v));
        private static float Abs(float v)=>BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(v)&0x7fffffff);
        private static void Require(bool present) { if(!present)throw new NullReferenceException(); }
        private static float ClampUpdateTime(float v) { if(v<0f)return 0f;if(99999f<v)return 99999f;return v; }
        internal void SetCurrentTime(float v)
        {
            // COMISS/JA permits unordered inputs to reach the native store.
            _currentTime=ClampUpdateTime(v);
        }
        public void Initialize(bool smooth = true)
        {
            _isEnabledSmooth = smooth;
            _musicTime=0f;_smoothMusicTime=0f;_smoothingWorkTime=0f;
            _lastMusicTimeWithoutDelay=0f;_currentTime=0f;_elapsedTime=0f;
        }
        public void StartCalcElapsedTime() { _doCalcElapsedTime=true;_elapsedTime=0f; }
        public void PauseLive()
        {
            _isPause=true;
            if(_source.GetAudioManager()&&_source.PauseSong())_elapsedTime=_source.SongPlaybackTime;
            _doCalcElapsedTime=false;
            if(_source.HasDirector()){Require(_source.GetDirector());_source.NotifyDirectorPause(_isPause);}
        }
        public void ResumeLive()
        {
            _isPause=false;
            if(_source.GetAudioManager()&&_source.ResumeSong())_elapsedTime=_source.SongPlaybackTime;
            _doCalcElapsedTime=true;
            if(_source.HasDirector()){Require(_source.GetDirector());_source.NotifyDirectorPause(_isPause);}
        }
        public void Update(float deltaTime)
        {
            if(_isPause)return;
            _prevTime=_currentTime;
            bool director=_source.GetDirector();
            if(_doCalcElapsedTime)
            {
                Require(director);
                float elapsed=F(_elapsedTime+deltaTime),total=_source.LiveTotalTime;
                if(total<=elapsed)elapsed=total;
                _elapsedTime=elapsed;
            }
            bool sampled=false;float music=0f;
            if(_source.GetAudioManager())
            {
                Require(director);int sheet=_source.SheetIndex;
                if(sheet==0||sheet==2)
                {
                    _source.SelectSkitCue(_source.SheetIndex==0);
                    if(_source.IsBgmPlaying){music=F((float)_source.BgmMilliseconds*0.001f);sampled=true;}
                }
                else if(sheet==1&&_source.IsPlayingSong){music=_source.SongPlaybackTime;sampled=true;}
            }
            if(sampled)
            {
                float difference=F(music-_lastMusicTimeWithoutDelay);
                _lastMusicTimeWithoutDelay=music;_musicTime=music;
                if(!_isEnabledSmooth||0.1f<Abs(F(_smoothMusicTime-music)))
                {
                    _smoothMusicTime=music;_smoothingWorkTime=0f;
                    _currentTime=ClampUpdateTime(music);
                }
                else
                {
                    float noise=F(difference-SmoothingTargetDeltaTime);
                    float overtime=-SmoothingOvertimeClamp;
                    if(overtime<=noise){overtime=noise;if(SmoothingOvertimeClamp<noise)overtime=SmoothingOvertimeClamp;}
                    float absolute=Abs(overtime);
                    if(SmoothingDeltaTimeThreshold<=absolute&&absolute!=SmoothingDeltaTimeThreshold)
                    {
                        float threshold=overtime>0f?SmoothingDeltaTimeThreshold:-SmoothingDeltaTimeThreshold;
                        bool further=overtime>0f?threshold<_smoothingWorkTime:_smoothingWorkTime<threshold;
                        if(!further&&_smoothingWorkTime!=threshold)
                        {
                            difference=F(difference-overtime);
                            _smoothingWorkTime=F(overtime+_smoothingWorkTime);
                        }
                    }
                    _smoothMusicTime=F(difference+_smoothMusicTime);
                    _currentTime=ClampUpdateTime(_smoothMusicTime);
                }
                _elapsedTime=_currentTime;
            }
            else _currentTime=ClampUpdateTime(_elapsedTime);
            _deltaTime=F(_currentTime-_prevTime);
        }
    }
}
