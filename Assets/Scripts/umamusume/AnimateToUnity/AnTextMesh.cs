using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnimateToUnity
{
    public sealed class AnTextMesh : IDisposable
    {
        public readonly AnTextParameter Parameter;
        public readonly TextMesh Main;
        readonly List<TextMesh> meshes=new List<TextMesh>();
        readonly List<Material> materials=new List<Material>();
        readonly List<MeshRenderer> renderers=new List<MeshRenderer>();
        readonly AnRoot root;
        readonly Transform target;
        readonly AnFontLayout layout;
        Material gradationMaterial;
        AnObjectInstance gradationStart,gradationEnd;
        bool overrideGradationAlpha;
        float gradationAlpha;
        public AnTextMesh(AnRoot root,AnTextParameter parameter,Transform target,int order,AnBlendModeTypes blendMode,int stencilRef,int stencilCompare)
        {
            this.root=root;this.target=target;Parameter=parameter;
            layout=AnFontLayout.Get(parameter._fontName,parameter._fontSize);
            Font font=root.FontResolver?.Invoke(parameter._fontName);
            if(font==null)throw new InvalidOperationException("A2U font missing: "+parameter._fontName);
            string shader="AnimateToUnity/Text_Main_"+(blendMode==AnBlendModeTypes.Add?"Add":"Normal");
            var material=new Material(Shader.Find(shader)){name=target.name+" text",mainTexture=font.material.mainTexture};materials.Add(material);
            material.SetInt("_Stencil",stencilRef);material.SetInt("_StencilComp",stencilCompare);
            if(parameter._textMeshType==AnTextMeshTypes.Shadow||parameter._textMeshType==AnTextMeshTypes.ShadowAndOutline)
            {
                float angle=-parameter._shadowAngle*Mathf.Deg2Rad;
                Create("shadow",font,material,parameter._shadowColor,new Vector3(Mathf.Cos(angle)*parameter._shadowOffset,Mathf.Sin(angle)*parameter._shadowOffset,0f),order);
            }
            int outlineQuality=AnFontLayout.OutlineQuality(parameter._fontName,parameter._fontSize,parameter._outlineQuality,parameter._outlineOffset);
            if((parameter._textMeshType==AnTextMeshTypes.Outline||parameter._textMeshType==AnTextMeshTypes.ShadowAndOutline)&&parameter._outlineOffset>0f&&outlineQuality>2)
                for(int i=0;i<outlineQuality;i++)
                {
                    float angle=(i+.5f)*2f*Mathf.PI/outlineQuality;
                    Create("outline"+i,font,material,parameter._outlineColor,new Vector3(Mathf.Cos(angle)*parameter._outlineOffset,Mathf.Sin(angle)*parameter._outlineOffset,0f),order+1);
                }
            Main=Create("main",font,material,parameter._textColor,Vector3.zero,order+2);
            Main.transform.localPosition=layout.TextOffset(parameter._anchor,parameter._size);
            foreach(var mesh in meshes)if(mesh!=Main)mesh.transform.SetParent(Main.transform,false);
            Font.textureRebuilt+=OnFontTextureRebuilt;
        }
        TextMesh Create(string name,Font font,Material material,Color color,Vector3 position,int order)
        {
            var go=new GameObject(name,typeof(TextMesh));go.layer=target.gameObject.layer;go.transform.SetParent(target,false);go.transform.localPosition=position;
            var text=go.GetComponent<TextMesh>();text.font=font;text.characterSize=10f;text.fontSize=Parameter._fontSize+layout.sizeOffset;text.lineSpacing=Parameter._lineSpace+layout.lineSpacing;
            text.anchor=Parameter._anchor;text.alignment=Parameter._alignment;text.fontStyle=Parameter._fontStyle;text.richText=true;text.tabSize=40f;text.color=color;text.text=Parameter._text;
            var renderer=go.GetComponent<MeshRenderer>();renderer.sharedMaterial=material;AnMeshGeometry.ConfigureRenderer(renderer,root._parameter._sortLayerName,order);
            meshes.Add(text);renderers.Add(renderer);return text;
        }
        void OnFontTextureRebuilt(Font font)
        {if(Main!=null&&Main.font==font)foreach(var material in materials)material.mainTexture=font.material.mainTexture;}
        public void SetText(string text){foreach(var mesh in meshes)mesh.text=text;}
        public void BindGradation(Transform motion)
        {
            if(string.IsNullOrEmpty(Parameter._gradationStartObjectName)||string.IsNullOrEmpty(Parameter._gradationEndObjectName))return;
            gradationStart=root.FindObject(Parameter._gradationStartObjectName,motion);gradationEnd=root.FindObject(Parameter._gradationEndObjectName,motion);
            if(gradationStart==null||gradationEnd==null)return;
            var original=Main.GetComponent<MeshRenderer>().sharedMaterial;
            gradationMaterial=new Material(original){shader=Shader.Find(original.shader.name+"_Gradation")};
            Main.GetComponent<MeshRenderer>().sharedMaterial=gradationMaterial;materials.Add(gradationMaterial);
        }
        public void SetOverrideGradationAlpha(bool enable,float alpha){overrideGradationAlpha=enable;gradationAlpha=alpha;}
        public void SetGradationColors(Color start,Color end)
        {if(gradationStart!=null)gradationStart.State.MultiplyColor=start;if(gradationEnd!=null)gradationEnd.State.MultiplyColor=end;SetOverrideGradationAlpha(true,1f);}
        public void UpdateGradation()
        {
            if(gradationMaterial==null)return;
            // Keep this text's own animated color from Update. Native AnText
            // multiplies its vertex color by CurrentColor even when gradient
            // endpoint alpha is overridden (Champions names use alpha = 1).
            var start=gradationStart.State.CurrentColor;var end=gradationEnd.State.CurrentColor;
            if(overrideGradationAlpha){start.a=gradationAlpha;end.a=gradationAlpha;}
            gradationMaterial.SetColor("_gradStartColor",start);gradationMaterial.SetColor("_gradEndColor",end);
            gradationMaterial.SetVector("_gradStartPosition",gradationStart.Transform.position);gradationMaterial.SetVector("_gradEndPosition",gradationEnd.Transform.position);
        }
        public void SetSortOrder(int order)
        {foreach(var renderer in renderers)renderer.sortingOrder=order+(renderer.gameObject==Main.gameObject?0:renderer.name=="shadow"?-2:-1);}
        public void SetFont(Font font){foreach(var mesh in meshes)mesh.font=font;foreach(var material in materials)material.mainTexture=font.material.mainTexture;}
        public void Update(AnObjectState state,bool visible)
        {
            foreach(var renderer in renderers)renderer.enabled=visible;
            foreach(var material in materials){material.SetColor("_multiplyColor",state.CurrentColor);material.SetColor("_colorOffset",state.CurrentColorOffset);}
        }
        public void Dispose(){Font.textureRebuilt-=OnFontTextureRebuilt;foreach(var material in materials)UnityEngine.Object.Destroy(material);}
    }
}
