using Gallop.Live.Cutt;
using UnityEngine;

namespace Gallop.Live
{
    public partial class Director
    {
        private void OnUpdateCharaFootLight(CharaFootLightUpdateInfo info)
        {
            if(info.CharacterIndex<0||info.CharacterIndex>=CharaContainerScript.Count)return;
            var container=CharaContainerScript[info.CharacterIndex];if(container==null)return;
            container.SetFootLightParameter(info.hightMax,info.lightColor,info.LightBlendMode,info.Easing);
        }
    }
}

public partial class UmaContainerCharacter
{
    private static readonly int HeightLightParamId=Shader.PropertyToID("_HightLightParam");
    private static readonly int HeightLightColorId=Shader.PropertyToID("_HightLightColor");
    private MaterialPropertyBlock _heightLightBlock;
    // Native CharacterObject.SetFootLightParameter 0x4e797d0. Its original
    // LiveModelController.BodyTransform is _bodyObject.transform, NOT Hip or
    // the animated Position node (UpdateCachedTransforms 0x575dcd0).
    public void SetFootLightParameter(float maxHight,Color color,
        Gallop.LiveDefine.HightLightBlendMode blend,Gallop.LiveDefine.Easing easing)
    {
        float baseY=Body!=null?Body.transform.position.y:transform.position.y;
        float height=maxHight>0.000001f?maxHight:0.000001f;
        var parameter=new Vector4(baseY,height,(int)easing,(int)blend);
        if(_heightLightBlock==null)_heightLightBlock=new MaterialPropertyBlock();
        foreach(var renderer in Renderers)
        {
            if(renderer==null)continue;
            renderer.GetPropertyBlock(_heightLightBlock);
            _heightLightBlock.SetVector(HeightLightParamId,parameter);
            _heightLightBlock.SetColor(HeightLightColorId,color);
            renderer.SetPropertyBlock(_heightLightBlock);
        }
    }
}
