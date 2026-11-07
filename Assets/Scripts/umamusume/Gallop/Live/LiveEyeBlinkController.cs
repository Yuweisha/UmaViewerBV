using System;
using System.Linq;
using Gallop.Live.Cutt;
using UnityEngine;

namespace Gallop.Live
{
    // Ordinary Live programmable face owner. Autonomous model blinking is
    // disabled by original LiveModelController.Init; timeline requests remain.
    public sealed class LiveEyeBlinkController:IDisposable
    {
        readonly UmaContainerCharacter actor;
        readonly UmaDatabaseEntry resource;
        bool disposed;
        float delta;
        public readonly LiveEyeBlinkOwner Owner=new LiveEyeBlinkOwner();
        public readonly LiveEyeBlinkTimeline Timeline=new LiveEyeBlinkTimeline();
        public bool IsBlinking=>Owner.Clock.Step!=5;

        LiveEyeBlinkController(UmaContainerCharacter character,UmaDatabaseEntry entry,EyeBlinkSettingData setting)
        {
            actor=character;resource=entry;Owner.Setting=setting;
            Owner.StoredRight=new[]{new FaceParts{_faceParts=0,_weight=1}};
            Owner.StoredLeft=new[]{new FaceParts{_faceParts=0,_weight=1}};
            var clock=Owner.Clock;clock.AutoBlink=false;clock.AutoDouble=true;
            clock.HasSetting=setting!=null;clock.ForceCloseTime=setting!=null?setting.ForceEyeCloseTime:0;
            clock.RandomRange=UnityEngine.Random.Range;
            Owner.UpdateBlink=()=>clock.Update(delta,actor.LiveEyeHighlight?.SuppressEyeBlink??false);
            clock.FaceStep+=(step,duration)=>
            {
                if(step==3||step==4)actor.FaceDrivenKeyTarget.LiveEyeTrace?.AfterBlink();
                actor.FaceDrivenKeyTarget.ApplyLiveBlinkStep(step,duration);
            };
            Timeline.UpdateAuto=true;Timeline.AutoInterval=UnityEngine.Random.Range(3f,5f);
            Timeline.RandomRange=UnityEngine.Random.Range;Timeline.IsBlinking=()=>IsBlinking;
            Timeline.ResetEyeCurrentTime=()=>clock.TimeCount=0;
            Timeline.RequestBlink=()=>Owner.RequestBlink(false);
        }
        public static LiveEyeBlinkController Create(UmaContainerCharacter character)
        {
            if(character.IsMini)return null;
            var entry=UmaViewerMain.Instance.AbList["3d/animator/ast_eyeblink_set"];
            var bundle=UmaAssetManager.LoadAssetBundle(entry,false,true);
            if(bundle==null)throw new InvalidOperationException("Could not load blink setting: "+entry.Name);
            try{return new LiveEyeBlinkController(character,entry,bundle.LoadAllAssets<EyeBlinkSettingData>().Single());}
            catch{UmaAssetManager.UnloadAssetBundle(entry,false);throw;}
        }
        public void SetTimeline(in FacialDataUpdateInfo input)=>Timeline.Update(input.faceCur,input.eyeCur);
        public void BeforeApplyDrivenKey(float elapsed)
        {delta=elapsed;Owner.BeforeApplyProgrammable();}
        public void Dispose()
        {if(disposed)return;disposed=true;if(resource!=null)UmaAssetManager.UnloadAssetBundle(resource,false);}
    }
}
