using System;
using Gallop.Live.Cutt;

namespace Gallop.Live
{
    public interface ILiveLipSyncOwner
    {
        bool HasOwner {get;}
        float DeltaTime {get;}
        float EngineDeltaTime {get;}
        FaceParts[] StoredMouth {get;}
        void SetMouth(FaceParts[] parts,float weight,float duration);
    }
    // Original Model.Component.LipSync. Shape resources and the shared
    // programmable mouth group belong to the actual owner adapter.
    public sealed class LiveLipSyncRuntime
    {
        readonly ILiveLipSyncOwner owner;
        static readonly FaceParts[] BaseMouth={new FaceParts{_faceParts=0,_weight=1}};
        public bool Enabled;
        public float TimeMax=.13f,OpenTransitionScale=1f,Timer;
        public float MaxWeightDecibel=-12f,MinWeightDecibel=-25f,MinWeight=.7f,VowelWeightMin=.1f,CloseWeightDecibel=-50f;
        public int Step,SetMouthId,SetMouthId2,CurrentIndex,UpdatableTime;
        public FaceParts[] OpenMouth=BaseMouth,CloseMouth=BaseMouth,CloseTarget=BaseMouth;
        static float F(float value)=>LiveTimelineControl.MonitorFloat32(value);
        public LiveLipSyncRuntime(ILiveLipSyncOwner owner){this.owner=owner;}
        bool FrameSkip(float duration)=>owner.EngineDeltaTime>F(duration*.5f);
        void Set(FaceParts[] parts,float weight,float duration)=>owner.SetMouth(parts,weight,duration);
        public void SetMouthType(FaceParts[] parts,bool open,FaceParts[] inverse)
        {OpenMouth=open?parts:inverse;CloseMouth=open?inverse:parts;}
        public float GetWeight(int volume)
        {
            if(volume>=30)
            {
                float db=LipSyncDataFile.ConvertVolumeToDecibel(volume);
                if(db<MaxWeightDecibel)
                {if(db>=MinWeightDecibel)return MinWeight;Step=4;}
            }
            return 1f;
        }
        public void Play(int mouthId,int volume,bool enableSync=true,bool immediate=false)
        {
            var close=CloseMouth;
            if(mouthId<0){Step=immediate?5:3;close=owner.StoredMouth;}
            else if(mouthId==0||volume<30)Step=4;
            float weight=1f;
            if(volume>=30)
            {
                float db=LipSyncDataFile.ConvertVolumeToDecibel(volume);
                if(db<MaxWeightDecibel)
                {if(db>=MinWeightDecibel)weight=MinWeight;else Step=4;}
            }
            if(!enableSync){OpenProcess(OpenMouth,close,weight);return;}
            Process(OpenMouth,close,weight);
        }
        public void Stop(bool immediate=false){Play(-1,0,true,immediate);UpdatableTime=0;}
        public void OpenProcess(FaceParts[] open,FaceParts[] close,float weight)
        {CloseTarget=close;Set(open,weight,TimeMax);Timer=TimeMax;Step=1;}
        public void Process(FaceParts[] open,FaceParts[] close,float weight)
        {
            if(!owner.HasOwner)return;
            float delta=owner.DeltaTime;
            switch(Step)
            {
                case 0:CloseTarget=close;Set(open,weight,F(OpenTransitionScale*TimeMax));Timer=0f;Step=1;break;
                case 1:
                    Timer=F(delta+Timer);
                    if(Timer>=TimeMax){Set(CloseTarget,1f,TimeMax);Step=2;}
                    break;
                case 2:Timer=F(Timer-delta);if(0f>Timer)Step=0;break;
                case 3:case 4:Set(close,1f,.1f);Timer=.1f;Step=2;break;
                case 5:Set(close,1f,0f);Timer=0f;Step=0;break;
            }
        }
        public bool IsClose(LipSyncDataFile.LipSyncData row)
        {
            if(CloseWeightDecibel>row.Decibel)return true;
            foreach(float value in row.VowelWeightArray)if(!(VowelWeightMin>value))return false;
            return true;
        }
        public bool IsCloseRows(LipSyncDataFile.LipSyncData[] rows,int index,int count=2)
        {
            for(int checkedRows=0;;checkedRows++)
            {if(!IsClose(rows[index]))return false;index++;if(checkedRows+1>=count||index>=rows.Length)return true;}
        }
        public bool IsCloseWindow(LipSyncDataFile.LipSyncData[] rows,int index,int range)
        {
            int end=unchecked(rows[index].Time+range),closed=0;
            for(;;)
            {
                if(IsClose(rows[index])){if(closed>=2)return true;closed++;}else closed=0;
                index++;if(index>=rows.Length||rows[index].Time>=end)return false;
            }
        }
        public void Play(LipSyncDataFile file,int index,bool enableSync=true,bool useFixedElapsed=false,float fixedElapsed=0f)
        {
            if(file.version==2)PlayVowels(file.datas,index,useFixedElapsed,fixedElapsed);
            else Play(file.datas[index].MouthId,file.datas[index].Volume,enableSync,false);
        }
        public void PlayVowels(LipSyncDataFile.LipSyncData[] rows,int index,bool useFixedElapsed=false,float fixedElapsed=0f)
        {
            var row=rows[index];if(CurrentIndex>index)UpdatableTime=0;
            float duration=useFixedElapsed?F(fixedElapsed*3f):.1f;
            int closeDelay=useFixedElapsed?unchecked((int)F(F(fixedElapsed*3f)*1000f)):100;
            void Close()
            {
                bool skip=FrameSkip(duration);Set(CloseMouth,1f,skip?0f:duration);
                SetMouthId=SetMouthId2=0;UpdatableTime=unchecked(row.Time+(skip?0:closeDelay));
            }
            if(IsCloseRows(rows,index)&&SetMouthId!=0){Close();return;}
            if(row.MouthId>=1&&row.MouthId<=5)
            {
                // The original reads this entry before any timing gate.
                float weight=row.VowelWeightArray[row.MouthId-1];
                if(SetMouthId!=0)
                {
                    if(row.Time>=UpdatableTime&&(SetMouthId!=row.MouthId||SetMouthId2!=row.MouthId2)&&!IsCloseWindow(rows,index,166))Close();
                }
                else if(row.Time>=UpdatableTime)
                {
                    const float openDuration=.13333334f;bool skip=FrameSkip(openDuration);
                    Set(OpenMouth,F(F(weight*.4f)+.6f),skip?0f:openDuration);
                    SetMouthId=row.MouthId;SetMouthId2=row.MouthId2;UpdatableTime=unchecked(row.Time+(skip?0:133));
                }
            }
            else if(SetMouthId!=0&&row.Time>=UpdatableTime)Close();
            CurrentIndex=index;
        }
    }
}
