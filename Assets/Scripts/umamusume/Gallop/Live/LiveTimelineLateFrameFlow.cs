using System;
using UnityEngine;

namespace Gallop.Live
{
    public interface ILiveTimelineLateFrameServices
    {
        bool DataAlive { get; }
        bool IsNowAlterUpdate { set; }
        bool ExcludeColorCorrection { set; }
        bool SelectWorksheet();
        bool WorksheetEnabled { get; }
        void SelectTargetCache();
        bool TargetCachePresent { get; }
        void Dispatch(string name, int index = -1);
        bool CameraMotionActive { get; }
        Vector3 LookAt { get; }
        Vector3 PositionLayer { get; }
        Vector3 LookAtLayer { get; }
        Vector3 ReadCameraPosition();
        Quaternion ReadCameraRotation();
        Vector3 RotateVector(Quaternion rotation, Vector3 value);
        void WriteCameraPosition(Vector3 value);
        void CameraLookAt(Vector3 value);
        bool MonitorInitialized { get; }
        int MonitorRollCount { get; }
        void RotateMonitor(int index);
        bool MultiInitialized { get; }
        bool MultiCachePresent { get; }
        int UpdateMultiCameraIndex { get; set; }
        void CopyMultiCameraToMain();
        bool PostEnabled { get; }
        bool DirectorPresent { get; }
        bool SkipFade { get; }
        int CameraScriptCount { get; }
        bool CameraScriptAlive(int index);
        void UpdateCameraScript(int index);
    }

    public sealed class LiveTimelineLateFrameFlow
    {
        readonly ILiveTimelineLateFrameServices source;
        public LiveTimelineLateFrameFlow(ILiveTimelineLateFrameServices source)
        { this.source=source??throw new ArgumentNullException(nameof(source)); }
        void Call(string name,int index=-1)=>source.Dispatch(name,index);

        public void AlterLateUpdate()
        {
            if(!source.DataAlive)return;
            source.IsNowAlterUpdate=true;
            source.ExcludeColorCorrection=false;
            if(!source.SelectWorksheet())throw new NullReferenceException("Worksheet");
            if(!source.WorksheetEnabled)return;
            source.SelectTargetCache();
            Call("AlterUpdate_Spotlight3d");
            Call("AlterUpdate_CharaNode");
            Call("AlterUpdate_NodeScale");
            Call("AlterUpdate_CharaNodeOffset");
            Call("AlterUpdate_Effect");
            Call("AlterUpdate_CameraLayer");
            Call("AlterLateUpdate_CameraMotion");
            bool motion=source.CameraMotionActive;
            Call("AlterUpdate_CameraPos");
            if(!motion)
            {
                Call("AlterUpdate_CameraLookAt");
                if(source.TargetCachePresent)
                {
                    Vector3 position=source.ReadCameraPosition();
                    Quaternion rotation=source.ReadCameraRotation();
                    Vector3 offset=source.RotateVector(rotation,source.PositionLayer);
                    source.WriteCameraPosition(position+offset);
                    rotation=source.ReadCameraRotation();
                    offset=source.RotateVector(rotation,source.LookAtLayer);
                    source.CameraLookAt(source.LookAt+offset);
                }
                Call("AlterUpdate_CameraRoll");
            }
            Call("AlterUpdate_CameraFov");
            Call("AlterUpdate_EffectScale");
            Call("AlterLateUpdate_ObjectControl");
            Call("AlterLateUpdate_CharaFootLight");
            Call("AlterLateUpdate_LightProjection");
            Call("AlterUpdate_GlobalLight");
            Call("AlterLateUpdate_PropControl");
            Call("AlterUpdate_HandShakeCamera");
            if(source.MonitorInitialized)
            {
                Call("AlterUpdate_MonitorCameraLayer");
                Call("AlterUpdate_MonitorCameraPosition");
                Call("AlterUpdate_MonitorCameraLookAt");
                for(int index=0;index<source.MonitorRollCount;index++)source.RotateMonitor(index);
            }
            if(source.MultiInitialized&&source.MultiCachePresent)
            {
                source.UpdateMultiCameraIndex=-1;
                Call("AlterUpdate_MultiCameraLayer");
                Call("AlterUpdate_MultiCameraPosition");
                Call("AlterUpdate_MultiCameraLookAt");
                if(source.UpdateMultiCameraIndex!=-1&&source.TargetCachePresent)source.CopyMultiCameraToMain();
            }
            Call("AlterUpdate_EyeCameraPosition");
            Call("AlterUpdate_EyeCameraLookAt");
            if(source.PostEnabled)
            {
                Call("AlterUpdate_PostEffect_DOF");
                Call("AlterUpdate_PostEffect_BloomDiffusion");
                Call("AlterUpdate_RadialBlur");
            }
            for(int index=0;index<3;index++)Call("AlterUpdate_PostFilm",index);
            for(int index=0;index<3;index++)Call("AlterUpdate_MultiCameraPostFilm",index);
            if(source.PostEnabled)
            {
                Call("AlterUpdate_MultiCameraPostEffect_DOF");
                Call("AlterUpdate_MultiCameraPostEffect_BloomDiffusion");
                Call("AlterUpdate_MultiCameraRadialBlur");
                Call("AlterUpdate_MultiCameraTransmittedLight");
                Call("AlterUpdate_Fluctuation");
                Call("AlterUpdate_ToneCurve");
                Call("AlterUpdate_Exposure");
                Call("AlterUpdate_Vortex");
                Call("AlterUpdate_TransmittedLight");
                Call("AlterUpdate_LensDistortion");
            }
            if(!source.DirectorPresent||!source.SkipFade)Call("AlterUpdate_Fade");
            for(int index=0;index<source.CameraScriptCount;index++)
                if(source.CameraScriptAlive(index))source.UpdateCameraScript(index);
            source.IsNowAlterUpdate=false;
        }
    }
}
