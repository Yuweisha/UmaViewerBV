using System;

namespace Gallop.Live
{
    public sealed class LiveStartSettings { public int StartFrame; }
    public sealed class LiveStartContext { public LiveStartSettings LiveSettings; }
    public struct LiveBgmStartRequest
    {
        public string CueSheetName;
        public int CueId,StopType;
        public bool Loop,CrossFade;
        public float Volume,FadeInTime,FadeOutTime,StartTime;
    }
    public struct LiveSongStartRequest
    {
        public object SongPartAccessor;
        public float StartTime,FadeInTime,SongVolumeRate;
        public bool Loop;
        public string CueName;
        public int StopType;
        public Action OnComplete;
    }
    public interface ILiveStartServices
    {
        bool GetAudioManager();
        int GetCurrentSheetType();
        LiveStartContext GetCurrentContext();
        bool UseSubInstance {get;}
        string GetSkitSound(bool after);
        object SongPartAccessor {get;}
        void PlayBgm(LiveBgmStartRequest request);
        void PlaySong(LiveSongStartRequest request);
        void CreateNameTexture();
        void StartPreLiveSkit();
        void StartMainLive();
        void StartAfterLiveSkit();
    }
    // Full original SetupLiveStart dispatch. Audio/context/name texture are
    // explicit original-subsystem boundaries, not empty host replacements.
    public sealed class LiveStartControl
    {
        public LiveTimeController Clock;
        public bool IsLiveSetup;
        readonly ILiveStartServices _source;
        public LiveStartControl(ILiveStartServices source){_source=source??throw new ArgumentNullException(nameof(source));}
        public static float StartTimeFromFrame(int frame)=>frame>0?(float)frame/60f:0f;
        public static LiveSongStartRequest MainSongRequest(object part,float start,Action complete)
            =>new LiveSongStartRequest{SongPartAccessor=part,StartTime=start,Loop=false,FadeInTime=0f,SongVolumeRate=1f,CueName=null,StopType=0,OnComplete=complete};
        public void SetupLiveStart(Action onComplete)
        {
            bool audio=_source.GetAudioManager();
            int sheet=_source.GetCurrentSheetType();
            var context=_source.GetCurrentContext();
            int frame=context.LiveSettings.StartFrame;
            // The first read gates the path; native does not clamp the re-read.
            float start=frame>0?(float)_source.GetCurrentContext().LiveSettings.StartFrame/60f:0f;
            if(!IsLiveSetup)
            {
                if(sheet==0||sheet==2)
                {
                    string cue=_source.GetSkitSound(sheet!=0);
                    if(!audio)throw new NullReferenceException();
                    _source.PlayBgm(new LiveBgmStartRequest{CueSheetName=cue,CueId=0,Loop=false,Volume=1f,FadeInTime=0f,FadeOutTime=.5f,StartTime=0f,CrossFade=false,StopType=0});
                }
                else if(sheet==1&&!_source.UseSubInstance)
                {
                    if(!audio)throw new NullReferenceException();
                    _source.PlaySong(MainSongRequest(_source.SongPartAccessor,start,onComplete));
                }
            }
            Clock.StartCalcElapsedTime();
            if(!IsLiveSetup)_source.CreateNameTexture();
            switch(sheet){case 0:_source.StartPreLiveSkit();break;case 1:_source.StartMainLive();break;case 2:_source.StartAfterLiveSkit();break;}
            IsLiveSetup=true;
            onComplete?.Invoke();
        }
    }
}
