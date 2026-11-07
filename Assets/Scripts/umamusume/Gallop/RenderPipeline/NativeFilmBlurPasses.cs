using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gallop.RenderPipeline
{
    public interface IOriginalFilmScrollOwner
    {
        Vector4 ScreenUVScrollParameter {get;set;}
        void ResetScreenUVScrollParameter();
    }
    // The original Gallop.Screen getters, with UIManager and device leaves
    // supplied by their actual owners. Device pixels are not virtual pixels.
    public static class NativeVirtualScreenSize
    {
        // This host has no Gallop.UIManager landscape/vertical camera owner.
        // Use the original no-landscape-owner branch, yielding virtual pixels.
        public static Vector2Int ForCurrentDevice() => Evaluate(false, false, UnityEngine.Screen.width, UnityEngine.Screen.height);
        public static Vector2Int Evaluate(bool hasUIManager,bool portrait,int deviceWidth,int deviceHeight)
        {
            if(hasUIManager)return portrait?new Vector2Int(1440,1920):new Vector2Int(1920,1080);
            return deviceWidth>deviceHeight?new Vector2Int(1920,1080):new Vector2Int(1080,1920);
        }
    }
    public sealed class FilmRollPass : NativeExtendedPostPass
    {
        public struct Parameter
        {
            public enum EnqueueTimingType {Normal=0,BeforeImageEffect=100}
            public bool IsEnable;
            public Vector2 RollPosition,RollMaskSize,RollMaskFadeSize;
            public Color RollMaskColor;
            public float NoisePower;
            public bool IsNoiseRandom;
            public Color NoiseColor;
            public Vector2 NoiseRange;
            public float ScanLineSpeed,ScanLineSize;
            public Vector2 ScanlineBlurSize;
            public float ScanLinePower;
            public EnqueueTimingType EnqueueTiming;
            public float Time;
            public bool IsValidity=>IsEnable;
            public static Parameter Default()=>new Parameter{RollMaskColor=Color.black,NoiseColor=Color.black,NoiseRange=new Vector2(-1,0),ScanlineBlurSize=Vector2.one,ScanLinePower=1.5f};
        }
        public static Func<int> OriginalScreenWidth {get;set;}
        public static Func<int> OriginalScreenHeight {get;set;}
        public static Func<IOriginalFilmScrollOwner> OriginalGraphicSettings {get;set;}
        public static Action<Camera,RenderTargetIdentifier,RenderTargetIdentifier> HostTargetBridgeObserver {get;set;}
        protected override int Kind=>0xb1;
        protected override string PoolName=>"FilmRollPass";
        protected override string TargetName=>"FilmRollPass_RT";
        protected override bool Enabled(PostImageEffectFeature.Parameter p)=>p.FilmRoll.IsEnable;
        protected override RenderPassEvent Event(PostImageEffectFeature.Parameter p)=>(RenderPassEvent)(p.FilmRoll.EnqueueTiming==Parameter.EnqueueTimingType.BeforeImageEffect?550:601);
        protected override RenderTargetIdentifier CameraTarget(ref RenderingData data)=>new RenderTargetIdentifier(data.cameraData.camera.targetTexture);
        protected override void CompleteHostTarget(CommandBuffer cmd,ref RenderingData data)
        {
            if((int)renderPassEvent<=550)return;
            var target=data.cameraData.camera.targetTexture;
            if(target==null)throw new InvalidOperationException("Original late FilmRoll camera targetTexture is not installed.");
            var originalTarget=new RenderTargetIdentifier(target);var hostTarget=data.cameraData.renderer.cameraColorTargetHandle.nameID;
            if(originalTarget!=hostTarget)
            {
                // Native writes targetTexture. URP14 owns another intermediate
                // and would overwrite it in FinalBlit; publish those exact pixels
                // back to the host color attachment before that final copy.
                cmd.Blit(originalTarget,hostTarget);
                HostTargetBridgeObserver?.Invoke(data.cameraData.camera,originalTarget,hostTarget);
            }
        }
        protected override void OriginalService(NativePostCommand command)=>ApplyScroll(command);
        public static void ApplyScroll(NativePostCommand command)
        {
            if(OriginalGraphicSettings==null)throw new InvalidOperationException("Original GraphicSettings instance owner is not installed.");
            var owner=OriginalGraphicSettings();if(owner==null)throw new NullReferenceException();
            if(command.Call=="scrollWrite")owner.ScreenUVScrollParameter=command.Vector;
            else owner.ResetScreenUVScrollParameter();
        }
        public override List<NativePostCommand> Build(PostImageEffectFeature.Parameter p,int width,int height)
        {
            if(OriginalScreenWidth==null||OriginalScreenHeight==null)throw new InvalidOperationException("Original Gallop.Screen owner is not installed.");
            float invW=F(1f/OriginalScreenWidth()),invH=F(1f/OriginalScreenHeight());var a=p.FilmRoll;
            var c=new List<NativePostCommand>();NativePostCommand.Allocate(c,"target",feature==null?width:feature.SourceRT.Width,feature==null?height:feature.SourceRT.Height);
            float x=F(invW*a.RollMaskSize.x),y=F(invH*a.RollMaskSize.y);
            var scroll=new Vector4(a.RollPosition.x,a.RollPosition.y,x,y);
            NativePostCommand.Vec(c,0x1c7,scroll,"material");
            c.Add(new NativePostCommand{Call=a.EnqueueTiming==Parameter.EnqueueTimingType.BeforeImageEffect?"scrollWrite":"scrollReset",Vector=scroll});
            NativePostCommand.Vec(c,0x1c8,a.RollMaskColor,"materialColor");
            NativePostCommand.Vec(c,0x1c9,new Vector4(F(x-F(invW*a.RollMaskFadeSize.x)),F(y-F(invH*a.RollMaskFadeSize.y)),0,0),"material");
            NativePostCommand.Float(c,0x1ca,a.NoisePower,"material");NativePostCommand.Float(c,0x1cb,a.IsNoiseRandom?1:-1,"material");
            NativePostCommand.Vec(c,0x1cc,a.NoiseColor,"material");NativePostCommand.Vec(c,0x1cd,new Vector4(a.NoiseRange.x,a.NoiseRange.y,0,0),"material");
            NativePostCommand.Float(c,0x1ce,F(invH*a.ScanLineSpeed),"material");NativePostCommand.Float(c,0x1d0,a.ScanLinePower,"material");NativePostCommand.Float(c,0x44,a.Time,"material");
            NativePostCommand.Float(c,0x1cf,a.ScanLineSize>0?F((float)OriginalScreenHeight()/a.ScanLineSize):0,"material");NativePostCommand.Vec(c,0x1d1,new Vector4(a.ScanlineBlurSize.x,a.ScanlineBlurSize.y,0,0),"material");
            NativePostCommand.Blit(c,"source","target",true);NativePostCommand.Resolve(c,NativeEvent((RenderPassEvent)(a.EnqueueTiming==Parameter.EnqueueTimingType.BeforeImageEffect?550:601)));return c;
        }
    }
    public sealed class BlurOptimizePass : NativeExtendedPostPass
    {
        public struct Parameter
        {
            public bool IsEnable;
            public int Downsample;
            public float BlurSize;
            public int BlurIterations,BlurType;
            public RenderPassEvent RenderPassEvent;
            public static Parameter Default()=>new Parameter{Downsample=1,BlurSize=3,BlurIterations=2,RenderPassEvent=(RenderPassEvent)550};
        }
        Vector4 parameterProp;
        public Vector4 ParameterProperty {get=>parameterProp;set=>parameterProp=value;}
        protected override int Kind=>0xc2;
        protected override string PoolName=>"BlurOptimizePass";
        protected override string TargetName=>"BlurOptimizePass_RT0";
        protected override bool RestoreShader=>false;
        protected override bool Enabled(PostImageEffectFeature.Parameter p)=>p.BlurOptimize.IsEnable;
        protected override RenderPassEvent Event(PostImageEffectFeature.Parameter p)=>p.BlurOptimize.RenderPassEvent;
        protected override Dictionary<string,int> Names()=>new Dictionary<string,int>{["rt0"]=Shader.PropertyToID("BlurOptimizePass_RT0"),["rt1"]=Shader.PropertyToID("BlurOptimizePass_RT1")};
        public override List<NativePostCommand> Build(PostImageEffectFeature.Parameter p,int width,int height)
        {
            var a=p.BlurOptimize;var c=new List<NativePostCommand>();float scale=F(1f/(float)unchecked(1<<a.Downsample));
            parameterProp.x=F(scale*a.BlurSize);parameterProp.y=F((-a.BlurSize)*scale);
            NativePostCommand.Vec(c,0xb8,parameterProp,"material");int w=width>>a.Downsample,h=height>>a.Downsample;
            c.Add(new NativePostCommand{Call="makeRT",Target="rt0",Width=w,Height=h});c.Add(new NativePostCommand{Call="makeRT",Target="rt1",Width=w,Height=h});
            c.Add(new NativePostCommand{Call="allocateRT",Target="rt0",Value=1});NativePostCommand.Blit(c,"source","rt0",true,0);
            c.Add(NativePostCommand.Op("executeCommand"));c.Add(NativePostCommand.Op("clearCommand"));c.Add(new NativePostCommand{Call="allocateRT",Target="rt1",Value=1});
            for(int i=0;i<a.BlurIterations;i++)
            {
                var v=parameterProp;v.x=F(v.x+(float)i);v.y=F(v.y-(float)i);NativePostCommand.Vec(c,0xb8,v);
                NativePostCommand.Blit(c,"rt0","rt1",true,a.BlurType==0?1:3);NativePostCommand.Blit(c,"rt1","rt0",true,a.BlurType==0?2:4);
                c.Add(NativePostCommand.Op("executeCommand"));c.Add(NativePostCommand.Op("clearCommand"));
            }
            c.Add(NativePostCommand.Op("releaseRT","rt1"));NativePostCommand.Blit(c,"rt0","source");c.Add(NativePostCommand.Op("releaseRT","rt0"));return c;
        }
    }
}
