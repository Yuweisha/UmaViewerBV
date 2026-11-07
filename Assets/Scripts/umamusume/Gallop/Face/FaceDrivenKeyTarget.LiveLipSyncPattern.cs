using Gallop.Live;
using Gallop.Live.Cutt;
using UnityEngine;

namespace Gallop
{
    public partial class FaceDrivenKeyTarget
    {
        readonly LiveLipSyncMouthState livePatternMouth=new LiveLipSyncMouthState();
        bool liveLipSyncEnabled=true;
        public bool IsLiveLipSyncEnabled=>liveLipSyncEnabled;
        public LiveLipSyncMouthState LivePatternMouth=>livePatternMouth;

        // Native LiveFaceController only submits here. DrivenKeyComponent.PostJob
        // advances the shared mouth group once per model frame, including frames
        // where no timeline Pattern event was emitted.
        public void SubmitLiveLipSyncPattern(LiveTimelineKeyLipSyncPatternData key,float duration)
        {
            if(!OrdinaryLiveLipSyncEnabled||Container.LiveSystemTextFace?.Active==true)return;
            if(!liveLipSyncEnabled){livePatternMouth.SetPatternOwner(false);return;}
            SubmitSharedLiveMouth(LiveLipSyncMouthState.BuildParts(key),duration,key.interpolateType,true);
        }

        void SubmitSharedLiveMouth(FaceParts[] parts,float duration,DrivenKeyComponent.InterpolateType mode,bool pattern)
        {
            var visible=new float[LiveLipSyncMouthState.MouthCount];
            foreach(var morph in MouthMorphs)if(morph.index>0 && morph.index<visible.Length)visible[morph.index]=morph.weight;
            livePatternMouth.Submit(parts,duration,mode,visible);
            livePatternMouth.SetPatternOwner(pattern);
            CopySharedLiveMouth();
        }

        void CopySharedLiveMouth()
        {foreach(var morph in MouthMorphs)if(morph.index>0&&morph.index<livePatternMouth.Weights.Count)morph.weight=livePatternMouth.Weights[morph.index];}

        public void SubmitLiveSystemTextMouth(FaceParts[] parts,float weight,float duration)
        {
            var visible=new float[LiveLipSyncMouthState.MouthCount];
            foreach(var morph in MouthMorphs)if(morph.index>0&&morph.index<visible.Length)visible[morph.index]=morph.weight;
            livePatternMouth.SubmitDirect(parts,weight,duration,visible);CopySharedLiveMouth();
        }

        public void AdvanceLiveLipSyncPattern(float elapsedTime)
        {
            if(!liveLipSyncEnabled){livePatternMouth.SetPatternOwner(false);return;}
            if(!livePatternMouth.HasSubmission)return;
            livePatternMouth.Advance(elapsedTime);
            CopySharedLiveMouth();
            // Use the actual loaded face TRS/override/rotation consumer.
            ChangeMorphMouth();
        }

        // Ordinary AutoLip and explicit facial mouths share the native group.
        // A host consumer which directly writes that group must release this
        // adapter only when its own mouth submission is actually accepted.
        public void CancelLiveLipSyncPattern()=>livePatternMouth.SetPatternOwner(false);

        public void AlterUpdateLipSyncPattern(LiveTimelineKeyLipSyncPatternData key,float duration,float elapsedTime)
        {
            SubmitLiveLipSyncPattern(key,duration);
            AdvanceLiveLipSyncPattern(elapsedTime);
        }
    }
}
