using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Gallop.Live
{
    // Version-pinned per-frame Director roots: 4e22820 / 4e21a30.
    // Engine/model/loader services are explicit dependencies, never silent no-ops.
    // The existing Viewer MonoBehaviour is not yet a complete implementation of
    // this contract; validate/install the whole service graph before switching it.
    public interface ILiveDirectorFrameModel
    {
        bool Alive { get; }
        float OriginalDeltaTime { set; }
        void SetTimeScale(float value);
    }
    public interface ILiveDirectorFrameCharacter
    {
        bool Alive { get; }
        bool IsTimescaleEnabled { get; }
        int ReserveDressModelCount { get; }
        bool TryGetModel(int index, out ILiveDirectorFrameModel model);
        void AlterLateUpdate();
        void AlterCameraLateUpdate();
        bool HasModelObjectArray { get; }
        ILiveDirectorFrameModel CurrentModel { get; }
    }
    public interface ILiveDirectorFrameServices
    {
        int State { get; set; }
        int LoadAssetTask { get; }
        bool IsRequestFadeOut { get; set; }
        bool DoStartLive { get; }
        bool ClothInitUpdate();
        bool IsSkipSkit { get; }
        void SetTimelineSequencialPlay(bool value);
        bool IsSheet(int index);
        bool SetCurrentSheet(int index);
        void SetupLiveStart();
        void AlterUpdateTimeline(float delta, out float timescale);
        float ClockDeltaTime { get; }
        bool TryGetCharacter(int index, out ILiveDirectorFrameCharacter character);
        int CharacterCount { get; }
        ILiveDirectorFrameCharacter CharacterAt(int index);
        void StageAlterUpdate(float timescale);
        void PropsAlterLateUpdate();
        bool TimelineAlive { get; }
        void TimelineAlterLateUpdate();
        float ClockDeltaTimePauseReset { get; }
        int ContextMusicId { get; }
        float CurrentTime { get; }
        bool UpdateHandShake(float delta, int seed, float time);
        Vector3 HandShakePosition { get; }
        Vector3 MainCameraLocalPosition { get; set; }
        bool IsTrainerCameraInitialized { get; }
        bool IsScreenModeFullPortrait { get; }
        int TrainerCameraOperation { get; }
        bool IsTrainerCameraGyroRotationByUnbiased { get; }
        void SetupTrainerCameraFromSaveData();
        void UpdateTrainerCameraSwipeRotation();
        void UpdateTrainerCameraGyroRotation();
        void UpdateTrainerCameraGyroRotationByUnbiased();
        void UpdateTrainerCameraFov();
        void UpdateTransparentCameraFromTimeline();
        void UpdateExcludeColorCorrectionCameraFromTimeline();
        void UpdateOutlineLength();
        void StageAlterLateUpdate();
        int ImageEffectCount { get; }
        float ReadWorksheetMaxFocalSize();
        void SetImageEffectMaxFocalSize(int index, float value);
        void ImageEffectAlterLateUpdate(int index);
        IList<ILiveDirectorFrameModel> ModelControllerList { get; }
        int? DepthOrderInfoLength { get; }
        void AllocateDepthOrderInfo(int count);
        bool IsUpdateModelRenderQueue { set; }
        void SetupMiniModelDepthOrderParam();
    }
    public sealed class LiveDirectorFrameFlow
    {
        readonly ILiveDirectorFrameServices source;
        public LiveDirectorFrameFlow(ILiveDirectorFrameServices source)
        { this.source=source??throw new ArgumentNullException(nameof(source)); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        static float F(float value)=>BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(value));
        public void AlterUpdate(float deltaTime,bool isUpdateDeltaTime)
        {
            if(source.State==6)
            {
                source.AlterUpdateTimeline(deltaTime,out float timescale);
                float originalDelta=source.ClockDeltaTime;
                for(int slot=0;slot<20;slot++)
                    if(source.TryGetCharacter(slot,out var character))
                    {
                        float scale=character.IsTimescaleEnabled?timescale:1f;
                        for(int index=0;index<character.ReserveDressModelCount;index++)
                            if(character.TryGetModel(index,out var model))
                            {
                                if(isUpdateDeltaTime)model.OriginalDeltaTime=originalDelta;
                                model.SetTimeScale(scale);
                            }
                    }
                source.StageAlterUpdate(timescale);
                return;
            }
            if(source.State!=2||source.LoadAssetTask!=4||source.ClothInitUpdate())return;
            if(!source.IsRequestFadeOut){source.IsRequestFadeOut=true;return;}
            if(!source.DoStartLive)return;
            if(source.IsSkipSkit)source.SetTimelineSequencialPlay(false);
            // Original settings getter is read again after IsSheet; a service
            // callback can change the context. Do not cache IsSkipSkit here.
            bool opening=source.IsSheet(0);
            int next=opening&&!source.IsSkipSkit?0:1;
            source.SetCurrentSheet(next); // Native ignores this bool at this site.
            source.State=3;
            source.SetupLiveStart();
        }
        public void AlterLateUpdate()
        {
            if(source.State!=6)return;
            int characterCount=source.CharacterCount;
            for(int i=0;i<characterCount;i++)
            {
                var character=source.CharacterAt(i);
                if(character!=null&&character.Alive)character.AlterLateUpdate();
            }
            source.PropsAlterLateUpdate();
            if(source.TimelineAlive)source.TimelineAlterLateUpdate();
            float delta=source.ClockDeltaTimePauseReset;
            int seed=source.ContextMusicId;
            if(source.UpdateHandShake(delta,seed,source.CurrentTime))
            {
                var p=source.MainCameraLocalPosition;var q=source.HandShakePosition;
                source.MainCameraLocalPosition=new Vector3(F(p.x+q.x),F(p.y+q.y),F(p.z+q.z));
            }
            if(source.IsTrainerCameraInitialized&&source.IsScreenModeFullPortrait)
            {
                source.SetupTrainerCameraFromSaveData();
                int operation=source.TrainerCameraOperation;
                if(operation==0)
                {source.UpdateTrainerCameraSwipeRotation();source.UpdateTrainerCameraFov();}
                else if(operation==1)
                {
                    if(source.IsTrainerCameraGyroRotationByUnbiased)source.UpdateTrainerCameraGyroRotationByUnbiased();
                    else source.UpdateTrainerCameraGyroRotation();
                    source.UpdateTrainerCameraFov();
                }
            }
            source.UpdateTransparentCameraFromTimeline();
            source.UpdateExcludeColorCorrectionCameraFromTimeline();
            source.UpdateOutlineLength();
            for(int i=0;i<characterCount;i++)
            {
                var character=source.CharacterAt(i);
                if(character!=null&&character.Alive)character.AlterCameraLateUpdate();
            }
            source.StageAlterLateUpdate();
            int effects=source.ImageEffectCount;
            for(int i=0;i<effects;i++)
            {
                float maximum=source.TimelineAlive?source.ReadWorksheetMaxFocalSize():30f;
                source.SetImageEffectMaxFocalSize(i,30f>maximum?30f:maximum);
                source.ImageEffectAlterLateUpdate(i);
            }
            source.ModelControllerList.Clear();
            for(int i=0;i<source.CharacterCount;i++)
            {
                var c=source.CharacterAt(i);
                if(c!=null&&c.Alive&&c.HasModelObjectArray)
                {
                    var model=c.CurrentModel;
                    if(model!=null&&model.Alive)source.ModelControllerList.Add(c.CurrentModel);
                }
            }
            if(source.DepthOrderInfoLength!=source.ModelControllerList.Count)
                source.AllocateDepthOrderInfo(source.ModelControllerList.Count);
            source.IsUpdateModelRenderQueue=source.DepthOrderInfoLength>0;
            source.SetupMiniModelDepthOrderParam();
        }
    }
}
