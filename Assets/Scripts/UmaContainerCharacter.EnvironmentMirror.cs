using System;
using System.Collections.Generic;
using Gallop;
using UnityEngine;

public partial class UmaContainerCharacter
{
    bool _liveMirrorBodyVisible=true,_liveMirrorHeadVisible=true,_liveMirrorChangedCharacterLayers;
    Renderer[] _liveMirrorRenderers=Array.Empty<Renderer>();
    int[] _liveMirrorLayerCache=Array.Empty<int>();
    readonly List<Renderer> _liveMirrorExpandableHeads=new List<Renderer>();
    readonly List<Vector3> _liveMirrorOriginalHeadSizes=new List<Vector3>();
    Vector3 _liveMirrorBodySize;
    bool _liveMirrorBoundsInitialized,_liveMirrorBoundsExpanded;
    // Original Init sets index0. Static payload [1,15,16] has the exact SHA256
    // encoded by original <PrivateImplementationDetails> field 5A906B9A... .
    public int LiveMirrorModelLayerIndex {get;set;}
    public bool LiveMirrorBodyVisible=>_liveMirrorBodyVisible;
    public bool LiveMirrorHeadVisible=>_liveMirrorHeadVisible;
    public bool LiveMirrorChangedCharacterLayers=>_liveMirrorChangedCharacterLayers;
    public bool LiveMirrorBoundsInitialized=>_liveMirrorBoundsInitialized;
    public bool LiveMirrorBoundsExpanded=>_liveMirrorBoundsExpanded;
    public IReadOnlyList<Renderer> LiveMirrorExpandableHeads=>_liveMirrorExpandableHeads;

    public void SetLiveEnvironmentMirrorVisible(bool body,bool head,bool changed)
    {
        // Original 0x575d3a0 only caches; the actual late renderer consumer below
        // resolves normal versus mirror-excluded layers for this same frame.
        _liveMirrorBodyVisible=body;_liveMirrorHeadVisible=head;_liveMirrorChangedCharacterLayers=changed;
    }
    int GetLiveEnvironmentMirrorLayer(Renderer renderer)
    {
        var index=GraphicSettings.LayerIndex.LayerCHAR;
        if(LiveMirrorModelLayerIndex==1)index=GraphicSettings.LayerIndex.LayerCharacter3D_0;
        else if(LiveMirrorModelLayerIndex==2)index=GraphicSettings.LayerIndex.LayerCharacter3D_1;
        if(!_liveMirrorChangedCharacterLayers&&(!_liveMirrorBodyVisible||(!_liveMirrorHeadVisible&&_mirrorHeadRenderers.Contains(renderer))))
            index=GraphicSettings.LayerIndex.LayerCharacter3D_NotReflect;
        return GraphicSettings.GetLayer(index);
    }
    public void UpdateLiveEnvironmentMirrorRuntime()
    {
        if(!IsLive)return;
        PrepareMirrorLayerCache();
        // Complete original ExternalLateUpdate 0x5758240 changes GameObject.layer
        // only when its renderer-index cache differs. Per-camera temporary
        // changes are separately restored by existing paired mirror callbacks.
        for(int i=0;i<_liveMirrorRenderers.Length;i++)
        {
            var renderer=_liveMirrorRenderers[i];if(!renderer)continue;
            int layer=GetLiveEnvironmentMirrorLayer(renderer);
            if(layer<0||layer==_liveMirrorLayerCache[i])continue;
            renderer.gameObject.layer=layer;_liveMirrorLayerCache[i]=layer;
        }
    }
    void RebuildLiveEnvironmentMirrorRenderers(Renderer[] renderers)
    {
        _liveMirrorRenderers=renderers;
        _liveMirrorLayerCache=new int[renderers.Length];
        for(int i=0;i<renderers.Length;i++)_liveMirrorLayerCache[i]=-1;
        InitializeLiveEnvironmentMirrorBounds();
    }
    void InitializeLiveEnvironmentMirrorBounds()
    {
        if(_liveMirrorBoundsInitialized)return;
        _liveMirrorBoundsExpanded=false;
        Renderer body=null;
        foreach(var renderer in _liveMirrorRenderers)
            if(renderer&&renderer.name=="M_Body"){body=renderer;break;}
        if(!body)return;
        _liveMirrorBodySize=body.bounds.size;
        _liveMirrorExpandableHeads.Clear();_liveMirrorOriginalHeadSizes.Clear();
        foreach(var renderer in _liveMirrorRenderers)
        {
            if(!renderer||!_mirrorHeadRenderers.Contains(renderer))continue;
            var size=renderer.bounds.size;
            if(!(size.y<_liveMirrorBodySize.y))continue;
            _liveMirrorExpandableHeads.Add(renderer);_liveMirrorOriginalHeadSizes.Add(size);
        }
        _liveMirrorBoundsInitialized=true;
    }
    public void SetLiveEnvironmentMirrorExpandedHeadBounds(bool expand)
    {
        PrepareMirrorLayerCache();
        // Native returns without changing the requested flag if initialization
        // has no M_Body. A repeated request makes no Renderer.bounds writes.
        if(!_liveMirrorBoundsInitialized||_liveMirrorBoundsExpanded==expand)return;
        _liveMirrorBoundsExpanded=expand;
        for(int i=0;i<_liveMirrorExpandableHeads.Count;i++)
        {
            var renderer=_liveMirrorExpandableHeads[i];if(!renderer)continue;
            Bounds world=renderer.bounds;
            Vector3 size=expand?_liveMirrorBodySize:_liveMirrorOriginalHeadSizes[i];
            world.extents=new Vector3(size.x*.5f,size.y*.5f,size.z*.5f);
            renderer.bounds=world;
        }
    }
    void ResetLiveEnvironmentMirrorBounds()
    {
        // Replacing model parts creates a new native initialization lifetime.
        // Restore still-live old renderers before capturing the replacement.
        if(_liveMirrorBoundsExpanded)
            for(int i=0;i<_liveMirrorExpandableHeads.Count;i++)
            {
                var renderer=_liveMirrorExpandableHeads[i];if(!renderer)continue;
                Bounds world=renderer.bounds;world.extents=_liveMirrorOriginalHeadSizes[i]*.5f;renderer.bounds=world;
            }
        _liveMirrorBoundsInitialized=false;_liveMirrorBoundsExpanded=false;
        _liveMirrorExpandableHeads.Clear();_liveMirrorOriginalHeadSizes.Clear();
    }
}
