using System.Collections.Generic;
using Gallop.Live;
using UnityEngine;

public partial class UmaContainerCharacter
{
    public readonly List<LiveEmissiveScrollRuntime> LiveEmissiveScrollComponents=new List<LiveEmissiveScrollRuntime>();
    // Original CharacterObject constructor enables timescale; pause defaults false.
    public bool LiveTimescaleEnabled=true;
    public bool LiveModelComponentsPaused;
    public void SetLiveComponentsPaused(bool paused)
    {
        LiveModelComponentsPaused=paused;
        foreach(var component in LiveEmissiveScrollComponents)component.Paused=paused;
        if(LiveModelIK!=null)
        {
            // Native component OnPause stores these flags. The IK callbacks
            // deliberately retain their own original enable/phase gates.
            LiveModelIK.Paused=paused;
            LiveModelIK.Limb.Paused=paused;
        }
        if(LiveSystemTextLimbIK!=null)LiveSystemTextLimbIK.Paused=paused;
        if(LiveEyeBlink!=null)LiveEyeBlink.Owner.Paused=paused;
    }
    float? _liveModelOriginalDeltaTime;
    public float? LiveModelOriginalDeltaTime=>_liveModelOriginalDeltaTime;

    internal void InstallLiveEmissiveScroll(LiveRendererHolder holder)
    {
        LiveEmissiveScrollComponents.Clear();
        // ModelController.InitializeModelComponent applies body first, then head.
        foreach(var data in new[]{BodyShaderEffectData,HeadShaderEffectData})
        {
            if(data==null)continue;
            var component=LiveEmissiveScrollRuntime.Apply(data.Settings,holder.MaterialArray,
                LiveDirtBindings?.Body0,LiveDirtBindings?.Body);
            if(component!=null)LiveEmissiveScrollComponents.Add(component);
        }
    }

    public void UpdateLiveEmissiveTiming(float baseTimescale,float clockDelta,bool updateDelta)
    {
        if(updateDelta)_liveModelOriginalDeltaTime=clockDelta;
        float scale=LiveTimescaleEnabled?baseTimescale:1f;
        // ModelController.SetTimeScale also forwards the selected model rate
        // to the loaded spring controller, independently of emissive effects.
        if (_cySpringLoaded && _cySpringController != null) _cySpringController.TimeScale=scale;
        foreach(var component in LiveEmissiveScrollComponents)component.TimeScale=scale;
    }

    public void AdvanceLiveEmissiveScroll(float engineDelta)
    {
        float delta=_liveModelOriginalDeltaTime??engineDelta;
        foreach(var component in LiveEmissiveScrollComponents)
        {
            component.Paused=LiveModelComponentsPaused;
            component.AfterLateUpdate(true,delta);
        }
    }

    void UpdatePreviewShaderEffects(float delta)
    {
        if(IsLive)return;
        if(HeadShaderEffectData!=null)HeadShaderEffectData.updateUV(delta);
        if(BodyShaderEffectData!=null)BodyShaderEffectData.updateUV(delta);
    }
}
