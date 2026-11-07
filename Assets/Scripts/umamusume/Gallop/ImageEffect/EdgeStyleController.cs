using System;
using System.Collections.Generic;
using UnityEngine;
using Gallop.RenderPipeline;

namespace Gallop
{
    public enum EdgeStyleMode { RainSplash=0 }

    // Explicit CharaPartsHolder.GetPartsData boundary. No hierarchy-name or
    // renderer-type inference: the original model loader must supply these parts.
    public sealed class EdgeStylePartsData
    {
        public bool IsBody,IsBodyProp,IsFace,IsHair,IsHairProp,IsTail,IsEye;
        public Renderer BodyRenderer,FaceRenderer,HairRenderer,TailRenderer,EyeRenderer;
        public Renderer[] BodyPropRendererArray,HairPropRendererArray;
    }
    public sealed class EdgeStyleController
    {
        LiveImageEffect _imageEffect;
        public EdgeStyleMode CurrentMode {get;private set;}
        public void Initialize(LiveImageEffect effect,EdgeStyleMode mode)
        {
            if(effect==null)return;
            _imageEffect=effect;CurrentMode=mode;
            if(mode==EdgeStyleMode.RainSplash&&effect.RainSplashParam.RendererInfoDictionary==null)
                effect.RainSplashParam.RendererInfoDictionary=new Dictionary<int,RainSplashPass.EdgeStyleRendererInfo>();
        }
        public void SetupRendererList(int index,UnityEngine.Object model,Func<UnityEngine.Object,EdgeStylePartsData> getParts)
        {
            if(_imageEffect==null||model==null)return;
            var parts=getParts?.Invoke(model);if(parts==null)return;
            var info=new RainSplashPass.EdgeStyleRendererInfo();var selected=new List<Renderer>();
            if(parts.IsBody)selected.Add(parts.BodyRenderer);
            if(parts.IsBodyProp)selected.AddRange(parts.BodyPropRendererArray);
            if(parts.IsFace)selected.Add(parts.FaceRenderer);
            if(parts.IsHair)selected.Add(parts.HairRenderer);
            if(parts.IsHairProp)selected.AddRange(parts.HairPropRendererArray);
            if(parts.IsTail)selected.Add(parts.TailRenderer);
            if(parts.IsEye)selected.Add(parts.EyeRenderer);
            info.IsVisible=false;info.RendererList=selected;
            if(CurrentMode==EdgeStyleMode.RainSplash)
            {
                var dictionary=_imageEffect.RainSplashParam.RendererInfoDictionary;
                if(!dictionary.ContainsKey(index))dictionary.Add(index,info);
            }
        }
        public void CleanUp()
        {
            if(CurrentMode==EdgeStyleMode.RainSplash&&_imageEffect!=null)
                _imageEffect.RainSplashParam.RendererInfoDictionary?.Clear();
            _imageEffect=null;
        }
    }
}
