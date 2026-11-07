using Gallop;
using Gallop.Live;
using UnityEngine;

public partial class UmaContainerCharacter
{
    public LiveLimbIKRuntime LiveSystemTextLimbIK { get; private set; }
    public LiveSystemTextIKRuntime LiveSystemTextIK { get; private set; }

    void PrepareLiveSystemTextIK()
    {
        if (LiveSystemTextIK != null)
            return;
        Transform Find(string name) => LiveModelIK.NodeCache.TryGetValue(name, out var node) ? node : null;
        var limb = LiveSystemTextLimbIK = new LiveLimbIKRuntime(Find)
        {
            Enabled = false,
            Paused = LiveModelComponentsPaused,
            Collision = LiveModelIK.Limb.Collision
        };
        for (int index = 0; index < 2; index++)
        {
            limb.Limbs[index].Solver.RotationType = LiveTwoBoneIKRuntime.EffectorRotationType.None;
            limb.Limbs[index].SetTargetSpace(true, Find("Waist"));
        }

        void ClearBlend(float value)
        {
            foreach (var solver in limb.Limbs)
            {
                solver.BlendValue = value;
                solver.IsUseAnimationBlendAlpha = false;
            }
        }

        ClearBlend(0f);
        var bundle = AcquireSystemTextResource(LiveEffectController.FindResourceEntry("3d/Animator/ast_event_fk_group"));
        var group = EventCharaFKAnimationGroup.Runtime.Create(bundle.LoadAllAssets<EventCharaFKAnimationGroup>()[0], CharaEntry.IsMob ? 1 : CharaEntry.Id, LiveMasterDressId);
        void SetComponentBlend(float value)
        {
            ClearBlend(value);

        }

        LiveAnimationMode.SystemTextIK = new LiveSystemTextIKModeState
        {
            Enabled = false,
            SetComponentEnabled = value => limb.Enabled = value,
            SetComponentBlend = SetComponentBlend
        };
        LiveSystemTextIK = new LiveSystemTextIKRuntime(group, LiveAnimationMode.SystemTextIK, (index, value) => limb.Limbs[index].BlendValue = value);
    }

    void BeginLiveSystemTextIK()
    {
        if (LiveSystemTextIK == null)
            return;
        LiveAnimationMode.SystemTextIK.Enabled = true;
        LiveSystemTextLimbIK.Enabled = true;
        LiveAnimationMode.PreviousSystemTextBodyMotionName = string.Empty;
        LiveSystemTextIK.PreviousMotionElapsed = 0f;
    }

    void SecondLiveSystemTextIK()
    {
        if (LiveSystemTextIK == null)
            return;
        LiveAnimationMode.PreviousSystemTextBodyMotionName = LiveSystemTextBody.PreviousMotion?.BodyMotion;
        LiveSystemTextIK.PreviousMotionElapsed = 0f;
    }
}
