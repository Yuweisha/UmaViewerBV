using Gallop;
using Gallop.Live;
using Gallop.Live.Cutt;
using UnityEngine;

public partial class UmaContainerCharacter
{
    LiveCharacterLightingRuntime _liveCharacterLighting;
    GameObject _lightingHead,_lightingHair,_lightingBody,_lightingTail;
    Vector3 _liveHeadBaseOffsets;
    public LiveCharacterLightingRuntime LiveCharacterLighting=>_liveCharacterLighting;
    public Vector3 LiveHeadLightingBaseOffsets=>_liveHeadBaseOffsets;
    public Vector3 LiveHeadLightingOffsets=>new Vector3(
        LiveTimelineControl.MonitorFloat32(_liveHeadBaseOffsets.x*(LiveScaleModel!=null?LiveScaleModel.HeadScale:BodyScale)),
        LiveTimelineControl.MonitorFloat32(_liveHeadBaseOffsets.y*(LiveScaleModel!=null?LiveScaleModel.HeadScale:BodyScale)),
        LiveTimelineControl.MonitorFloat32(_liveHeadBaseOffsets.z*(LiveScaleModel!=null?LiveScaleModel.HeadScale:BodyScale)));
    LiveCharacterLightingRuntime EnsureLiveCharacterLighting()
    {
        if(_liveCharacterLighting==null)_liveCharacterLighting=new LiveCharacterLightingRuntime();
        if(_lightingHead!=Head||_lightingHair!=Hair||_lightingBody!=Body||_lightingTail!=Tail||_liveCharacterLighting.Bindings.Count==0)
        {
            if(_lightingHead!=Head)_liveHeadBaseOffsets=Vector3.zero;
            _lightingHead=Head;_lightingHair=Hair;_lightingBody=Body;_lightingTail=Tail;
            _liveCharacterLighting.Bind(Renderers);
            var holder=Head!=null?Head.GetComponent<AssetHolder>():null;
            _liveHeadBaseOffsets=ReadLiveHeadLightingBaseOffsets(holder,_liveHeadBaseOffsets);
        }
        return _liveCharacterLighting;
    }
    public static Vector3 ReadLiveHeadLightingBaseOffsets(AssetHolder holder,Vector3 previous)
    {
        bool foundY=false,foundZ=false,foundTube=false;
        if(holder!=null&&holder._assetTableValue?.list!=null)
            foreach(var value in holder._assetTableValue.list)
            {
                if(value==null)continue;
                switch(value.Key)
                {
                    case "head_center_offset_y":if(!foundY){previous.x=value.Value;foundY=true;}break;
                    case "head_center_offset_z":if(!foundZ){previous.y=value.Value;foundZ=true;}break;
                    case "head_tube_center_offset_z":if(!foundTube){previous.z=value.Value;foundTube=true;}break;
                }
            }
        return previous;
    }
    public void SetLiveFacialToon(ref FacialToonUpdateInfo info)=>EnsureLiveCharacterLighting().SetFacial(ref info);
    public void SetLiveGlobalLightRotation(Quaternion rotation)=>EnsureLiveCharacterLighting().SetGlobal(rotation);
    public void UpdateLiveFacialToonMaterialRuntime()
    {
            {
        if(!IsLive||IsMini||_liveCharacterLighting==null)return;
        var runtime=EnsureLiveCharacterLighting();
        var node=HeadToonBaseTransform!=null?HeadToonBaseTransform:(HeadBone!=null?HeadBone.transform:null);
        if(node==null)return;
        // Original LiveModel.Init enables scale adjust; Model.SetScale multiplies
        // all three base offsets by the final Position local scale.
        var offsets=LiveHeadLightingOffsets;
        runtime.Apply(node.position,node.up,node.forward,node.rotation,offsets);
    
            }
        }
}
