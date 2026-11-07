using Gallop.Live.Cutt;

namespace Gallop.Live
{
    public interface ILiveSystemTextFacialPlayback:ILiveSystemTextFaceOwner
    {
        FaceTypeData GetFaceType(string label);
        void SetFace(FaceTypeData face,float duration);
        bool HasFacialMotion(LiveSystemTextMotionSet motion,int characterId);
        void PlayDrivenKey(int id,bool second);
        LipSyncDataFile LoadLipSync(LiveSystemTextData text,int characterId);
        void UpdateMouthSet(FaceTypeData face);
        void CloseMouthId(string label);
        void SetEyeDefault(bool value);
        void ClearEyeDuration();
        void ZeroEyeDuration();
        void SetCheek(bool enabled,int type);
        void PlayLipSync(LipSyncDataFile file,int index);
        void StopLipSync();
    }
    public interface ILiveSystemTextFaceOwner
    {
        bool HasEyeTrace { get; }
        object StoreEyeTrace();
        void ResetEyeTraceForSystemText();
        void RestoreEyeTrace(object state);
        bool AutoBlink { get; }
        void SetAutoBlink(bool enabled, bool doubleBlink);
        object OtherLipSync { get; }
        bool GetLipSyncEnabled(object component);
        void SetLipSyncEnabled(object component, bool enabled);
        object DrivenKey { get; }
        void StopFaceLayer(object component);
        void ClearFaceClip(object component);
        void ResetEyeBlinkStatus();
    }

    // LiveFaceController.Begin/StopSystemTextFacial. Initialization binds the
    // two system-mouth components; an uninitialized ordinary face is untouched.
    public sealed class LiveSystemTextFaceRuntime
    {
        readonly ILiveSystemTextFaceOwner owner;
        public bool Initialized, Active, IsSystemTextLipSync, EyeTraceSmoothness;
        public bool ForceEye, ForceMouth, ForceEyebrow, ForceEar;
        public object FaceType, LipSyncData, SystemLipSync, SystemLipSyncMouth;
        public object RestoreEyeState, SavedOtherLipSync;
        public bool RestoreAutoBlink, RestoreOtherLipSync;
        public float LipSyncStartTime;
        public LiveSystemTextFaceRuntime(ILiveSystemTextFaceOwner value) { owner=value; }
        public void Begin()
        {
            if(Active||!Initialized)return;
            if(owner.HasEyeTrace)
            {
                RestoreEyeState=owner.StoreEyeTrace();
                owner.ResetEyeTraceForSystemText();
            }
            RestoreAutoBlink=owner.AutoBlink;
            SavedOtherLipSync=owner.OtherLipSync;
            if(SavedOtherLipSync!=null)
            {
                RestoreOtherLipSync=owner.GetLipSyncEnabled(SavedOtherLipSync);
                owner.SetLipSyncEnabled(SavedOtherLipSync,false);
            }
            Active=true;
        }
        public void Stop()
        {
            if(!Active||!Initialized)return;
            Active=false;IsSystemTextLipSync=false;
            if(owner.HasEyeTrace)owner.RestoreEyeTrace(RestoreEyeState);
            EyeTraceSmoothness=false;
            owner.SetAutoBlink(RestoreAutoBlink,true);
            var driven=owner.DrivenKey;
            if(driven!=null)owner.StopFaceLayer(driven);
            driven=owner.DrivenKey;
            if(driven!=null)owner.ClearFaceClip(driven);
            owner.ResetEyeBlinkStatus();
            if(SavedOtherLipSync!=null)owner.SetLipSyncEnabled(SavedOtherLipSync,RestoreOtherLipSync);
            LipSyncData=null;FaceType=null;
            owner.SetLipSyncEnabled(SystemLipSync,false);
            owner.SetLipSyncEnabled(SystemLipSyncMouth,false);
            ForceEye=ForceMouth=ForceEyebrow=ForceEar=true;
        }
        // LiveFaceController.StartSystemTextFacial (0x4e91270). currentTime
        // is Director global time, not body motionSec. The second motion
        // changes expression without restarting the first lip pack.
        public void Start(LiveSystemTextData text,LiveSystemTextMotionSet motion,int characterId,int id,
            float currentTime,int headFrame,bool second,bool eyeSmoothness)
        {
            if(!Initialized)return;
            var playback=(ILiveSystemTextFacialPlayback)owner;
            if(motion!=null&&!string.IsNullOrEmpty(motion.FaceType))FaceType=playback.GetFaceType(motion.FaceType);
            var face=(FaceTypeData)FaceType;
            if(face!=null)playback.SetFace(face,second ? .2f : 0f);
            if(playback.HasFacialMotion(motion,characterId))playback.PlayDrivenKey(id,second);
            if(SystemLipSync!=null&&SystemLipSyncMouth!=null)
            {
                if(!second)
                {
                    owner.SetLipSyncEnabled(SystemLipSync,true);owner.SetLipSyncEnabled(SystemLipSyncMouth,true);
                    LipSyncData=playback.LoadLipSync(text,characterId);LipSyncStartTime=currentTime;IsSystemTextLipSync=text.LipSyncFlag==0;
                }
                if(face!=null){playback.UpdateMouthSet(face);playback.CloseMouthId(face.label);}
            }
            if(motion!=null&&owner.HasEyeTrace)
            {
                playback.SetEyeDefault(motion.EyeDefault==1);
                if(headFrame<=0&&eyeSmoothness)playback.ClearEyeDuration();else playback.ZeroEyeDuration();
                EyeTraceSmoothness=eyeSmoothness;
            }
            owner.SetAutoBlink(false,true);playback.SetCheek(motion.Cheek==1,motion.Cheek);
        }
        void UpdateLipSyncRows(float milliseconds)
        {
            if(LipSyncData==null||SystemLipSync==null)return;
            var file=(LipSyncDataFile)LipSyncData;var playback=(ILiveSystemTextFacialPlayback)owner;
            for(int index=file.dataCount-1;index>=0;index--)
                if(milliseconds>=file.datas[index].Time)
                {
                    if(index==file.dataCount-1)playback.StopLipSync();
                    else if(IsSystemTextLipSync)playback.PlayLipSync(file,index);
                    break;
                }
        }
        public void Update(float currentTime)
        {
            UpdateLipSyncRows(LiveTimelineControl.MonitorFloat32(LiveTimelineControl.MonitorFloat32(currentTime-LipSyncStartTime)*1000f));
            if(owner.HasEyeTrace&&!EyeTraceSmoothness)((ILiveSystemTextFacialPlayback)owner).ZeroEyeDuration();
        }
        public void UpdateLipSync(float milliseconds)=>UpdateLipSyncRows(milliseconds);
    }
}
