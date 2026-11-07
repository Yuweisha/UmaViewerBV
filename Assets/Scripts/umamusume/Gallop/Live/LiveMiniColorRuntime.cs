using System;
using UnityEngine;
using Gallop.Live.Cutt;

namespace Gallop.Live
{
    // MiniModelController material-block consumers and RendererHolder broadcast.
    // The model owner's other material defaults remain its initialization input.
    public sealed class LiveMiniColorRuntime : ILiveMiniColorPlayer
    {
        readonly Renderer[] renderers;
        readonly UnityEngine.MaterialPropertyBlock block=new UnityEngine.MaterialPropertyBlock();
        static readonly int Chara=Shader.PropertyToID("_CharaColor"),Outline=Shader.PropertyToID("_OutlineColor"),Width=Shader.PropertyToID("_OutlineWidth");
        public event Action<string,int,Vector4> PropertyWrite;
        public event Action<Renderer> RendererWrite;
        public LiveMiniColorRuntime(Renderer[] renderers){this.renderers=renderers;}
        static float F(float value)=>LiveTimelineControl.MonitorFloat32(value);
        public void SetCharaColor(Color color){block.SetColor(Chara,color);PropertyWrite?.Invoke("SetColor",17,color);}
        public void SetOutlineColor(Color color,LiveTimelineDefine.OutlineColorBlend blend)
        {
            float alpha=blend==LiveTimelineDefine.OutlineColorBlend.Blend?F(F(1f-color.a)*0.5f):blend==LiveTimelineDefine.OutlineColorBlend.Multiply?F(F(color.a*0.5f)+0.5f):0.5f;
            var encoded=new Color(color.r,color.g,color.b,alpha);block.SetColor(Outline,encoded);PropertyWrite?.Invoke("SetColor",54,encoded);
        }
        public void SetOutlineWidthForPower(float power){float value=F(power*0.325f);block.SetFloat(Width,value);PropertyWrite?.Invoke("SetFloat",53,new Vector4(value,0,0,0));}
        public void UpdateMaterialPropertyBlock(){if(renderers==null)return;foreach(var renderer in renderers){renderer.SetPropertyBlock(block);RendererWrite?.Invoke(renderer);}}
    }
}
