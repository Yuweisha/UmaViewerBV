using System;
using System.Collections.Generic;
using Gallop;
using Gallop.Live;
using Gallop.Live.Cutt;
using IK;
using UnityEngine;

public partial class UmaContainerCharacter
{
    public LiveModelIKRuntime LiveModelIK { get; private set; }
    public LiveMicStandRuntime LiveMicStand { get; private set; }
    public LiveIKTimelineActor LiveIKActor { get; private set; }

    [NonSerialized]
    public AnimationState LiveMSQCurrentAnimState;
    [NonSerialized]
    public float LiveMSQCurrentAnimStartTime;
    public LiveModelPhaseOwner LiveModelPhaseOwner { get; internal set; }

    LiveModelPhaseRegistry liveModelPhaseRegistry;
    void OnEnable()
    {
        if (LiveModelPhaseOwner != null)
            liveModelPhaseRegistry?.AddItem(LiveModelPhaseOwner);
    }

    void UnregisterLiveModelIK()
    {
        if (LiveModelPhaseOwner != null)
            liveModelPhaseRegistry?.RemoveItem(LiveModelPhaseOwner);
    }

    public void InstallLiveModelIK()
    {
        if (!IsLive || IsMini || LiveModelIK != null)
            return;
        var cache = new Dictionary<string, Transform>();
        foreach (var node in GetComponentsInChildren<Transform>(true))
            if (!cache.ContainsKey(node.name))
                cache.Add(node.name, node);
        // MergeModel moves the source body children to this active model root;
        // Body remains the inactive asset-holder shell. Native BodyTransform's
        // loaded-model counterpart is therefore this merged root.
        LiveMicStand = new LiveMicStandRuntime(cache, transform, Position, () => BodyScale);
        LiveMicStand.ResetOffsets(); // InitializeModelComponent disables both processes.

        var body = Body != null ? Body.GetComponent<AssetHolder>()?.Get<IKCollisionData>("ikcol") : null;
        var head = Head != null ? Head.GetComponent<AssetHolder>()?.Get<IKCollisionData>("ikcol") : null;
        LiveModelIK = new LiveModelIKRuntime(Position, cache, body, head, LiveTotalScale, BodyScale, () => LiveTotalScale);
        // LiveModelController.InitializeModelComponent disables the newly
        // registered LiveIK after OnInitialize; a timeline mode enables it.
        LiveModelIK.Enabled = false;
        LiveIKActor = new LiveIKTimelineActor(LiveModelIK, LiveMicStand, () => LiveLocator);
        if (Director.instance != null)
            liveModelPhaseRegistry = Director.instance.RegisterLiveModelIK(this);

    }
}
