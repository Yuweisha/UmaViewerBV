using System;
using System.Runtime.CompilerServices;

namespace Gallop.Live
{
    public interface ILiveTimelineUpdateFrameServices
    {
        bool DataAlive { get; }
        void BeforeUpdate();
        float[] RandomOffsets { get; }
        int[] RandomOffsetFrames { get; }
        void UpdateDebugInfo(float delta);
        void ResetMonitorUsage();
        bool SelectWorksheet();
        bool WorksheetEnabled { get; }
        float SampleCameraTargetHeight();
        bool KeysDisabled { get; }
        bool KeysPlayModeEnabled { get; }
        int KeyCount { get; }
        float SampleTimescale(out int frame);
        void Dispatch(string name);
        bool MotionArrayPresent { get; }
        bool DetailMotionNoise { get; }
        bool MotionPresent(int index);
        void SelectMotionSheet(int index);
        void UpdateMotion(int index,int frame,float time,bool timescaleKeysPresent);
        bool LipSyncListenerPresent { get; }
        object FindLipSyncKey(int index);
        bool IsLipSyncKey(object key);
        void UpdateLipSync(object key,float time);
        bool DirectorPresent { get; }
        void AfterUpdate();
    }

    public sealed class LiveTimelineUpdateFrameFlow
    {
        readonly ILiveTimelineUpdateFrameServices source;
        public LiveTimelineUpdateFrameFlow(ILiveTimelineUpdateFrameServices source)
        { this.source=source??throw new ArgumentNullException(nameof(source)); }
        public bool IsNowAlterUpdate;
        public float CurrentTime,OldTime,Delta,DeltaRatio,Timescale,CameraTargetHeight,TransparentSize;
        public int Frame,OldFrame,TimescaleFrame;
        public bool TimescaleKeysPresent,TransparentEnabled,TransparentOrthographic,TransparentDefaultTransform;
        [MethodImpl(MethodImplOptions.NoInlining)]
        static float F(float value)=>BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(value));
        void Call(string name)=>source.Dispatch(name);
        void Calls(params string[] names){foreach(string name in names)Call(name);}

        public void AlterUpdate(float liveTime)
        {
            if(!source.DataAlive)return;
            IsNowAlterUpdate=true;
            source.BeforeUpdate();
            OldTime=CurrentTime;CurrentTime=liveTime;
            Frame=unchecked((int)System.Math.Floor((double)F(liveTime*60f)));
            OldFrame=unchecked((int)System.Math.Floor((double)F(OldTime*60f)));
            TransparentEnabled=false;TransparentOrthographic=false;
            TransparentSize=5;TransparentDefaultTransform=false;
            Delta=F(CurrentTime-OldTime);DeltaRatio=F(Delta/.0166666675f);
            var offsets=source.RandomOffsets;
            if(offsets==null)throw new NullReferenceException("Random offsets");
            for(int index=0;index<offsets.Length;index++)
                source.RandomOffsetFrames[index]=unchecked((int)System.Math.Floor((double)F(F(offsets[index]+CurrentTime)*60f)));
            source.UpdateDebugInfo(Delta);
            source.ResetMonitorUsage();
            if(!source.SelectWorksheet())throw new NullReferenceException("Worksheet");
            if(!source.WorksheetEnabled)return;
            CameraTargetHeight=source.SampleCameraTargetHeight();
            Call("AlterUpdate_Event");
            Timescale=1;TimescaleFrame=0;
            if(!source.KeysDisabled&&source.KeysPlayModeEnabled&&source.KeyCount>0)
                Timescale=source.SampleTimescale(out TimescaleFrame);
            TimescaleKeysPresent=!source.KeysDisabled&&source.KeysPlayModeEnabled&&source.KeyCount>0;
            Calls("AlterUpdate_BlinkLight","AlterUpdate_IKController");
            if(source.MotionArrayPresent)
            {
                Call(source.DetailMotionNoise?"AlterUpdate_CharaMotionNoise_Detail":"AlterUpdate_CharaMotionNoise_Basic");
                for(int index=0;index<20;index++)
                    if(source.MotionPresent(index))
                    {
                        source.SelectMotionSheet(index);
                        source.UpdateMotion(index,Frame,CurrentTime,TimescaleKeysPresent);
                    }
            }
            Calls("AlterUpdate_FacialData","AlterUpdate_LipSyncPatternRange","AlterUpdate_LipSyncPattern");
            if(source.LipSyncListenerPresent)
                for(int index=0;index<2;index++)
                    if(!source.KeysDisabled&&source.KeysPlayModeEnabled)
                    {
                        object key=source.FindLipSyncKey(index);
                        if(key!=null)source.UpdateLipSync(source.IsLipSyncKey(key)?key:null,CurrentTime);
                    }
            Calls("AlterUpdate_MiniCharaCamera","AlterUpdate_MiniCharaColor",
                "AlterUpdate_MiniCharaMotion","AlterUpdate_MiniCharaTransform",
                "AlterUpdate_BgColor1","AlterUpdate_BgColor2","AlterUpdate_VolumeLight",
                "AlterUpdate_LightShafts","AlterUpdate_ColorCorrection","AlterUpdate_PreColorCorrection",
                "AlterUpdate_MonitorControl","AlterUpdate_Projector","AlterUpdate_AnimationControl",
                "AlterUpdate_TextureAnimationControl","AlterUpdate_TransformControl","AlterUpdate_RendererControl",
                "AlterUpdate_ObjectControl","AlterUpdate_WaveObject","AlterUpdate_Audience");
            if(!source.DirectorPresent)throw new NullReferenceException("Director");
            Calls("AlterUpdate_FormationOffset","AlterUpdate_PropsControl","AlterUpdate_PropsAttachControl",
                "AlterUpdate_ParentConstraint","AlterUpdate_Particle","AlterUpdate_ParticleGroup",
                "AlterUpdate_Laser","AlterUpdate_UVScrollLight","AlterUpdate_TiltShift",
                "AlterUpdate_Hatching","AlterUpdate_EdgeStyle","AlterUpdate_ChromaticAberration",
                "AlterUpdate_Billboard","AlterUpdate_WashLight","AlterUpdate_MultiCameraColorCorrection",
                "AlterUpdate_MultiCameraTiltShift","AlterUpdate_Environment","AlterUpdate_MirrorReflection",
                "AlterUpdate_GlobalFog","AlterUpdate_SweatLocator","AlterUpdate_LensFlare",
                "AlterUpdate_FlashPlayer","AlterUpdate_Title","AlterUpdate_CameraSwitcher",
                "AlterUpdate_CameraMotion","AlterUpdate_FacialToon","AlterUpdate_AdditionalLight",
                "AlterUpdate_MultiLightShadow","AlterUpdate_MobControl","AlterUpdate_CyalumeControl");
            if(!source.DirectorPresent)throw new NullReferenceException("Director");
            Calls("AlterUpdate_CharaWind","AlterUpdate_CharaParts","AlterUpdate_CharaCollision",
                "AlterUpdate_TransmittedLightMask","AlterUpdate_Voice","AlterUpdate_TransparentCamera",
                "AlterUpdate_ContactEffect","AlterUpdate_RayHitEffect","AlterUpdate_ScreenCapture");
            source.AfterUpdate();
            IsNowAlterUpdate=false;
        }
    }
}
