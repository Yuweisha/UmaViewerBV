using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AnimateToUnity
{
    // The normal quad and UV layout from AnMeshInfoParameter._CreateMesh/_GetUVList.
    public static class AnMeshGeometry
    {
        public static readonly int[] Triangles={0,1,2,0,3,1};
        public static Vector3[] Quad(Vector2 size)=>new[]{new Vector3(-size.x*.5f,-size.y*.5f,0f),new Vector3(size.x*.5f,size.y*.5f,0f),new Vector3(size.x*.5f,-size.y*.5f,0f),new Vector3(-size.x*.5f,size.y*.5f,0f)};
        public static Vector2[] UV(AnMeshInfoParameter p)
        {
            float x=p._uvOffset.x,y=p._uvOffset.y,w=p._uvSize.x,h=p._uvSize.y;
            return p._rotated?new[]{new Vector2(x,y+h),new Vector2(x+w,y),new Vector2(x,y),new Vector2(x+w,y+h)}:
                new[]{new Vector2(x,y),new Vector2(x+w,y+h),new Vector2(x+w,y),new Vector2(x,y+h)};
        }
        public static Vector3 Shear(Vector3 value,Vector3 offset,Vector2 shear,Vector3 scale)
        {
            float cx=Mathf.Cos(shear.x*Mathf.Deg2Rad),sx=Mathf.Sin(shear.x*Mathf.Deg2Rad);
            float cy=Mathf.Cos(shear.y*Mathf.Deg2Rad),sy=Mathf.Sin(shear.y*Mathf.Deg2Rad);
            float x=(value.x-offset.x)*scale.x,y=(value.y-offset.y)*scale.y;
            return new Vector3(x*cy-y*sx+offset.x,y*cx+x*sy+offset.y,value.z);
        }
        public static void ConfigureRenderer(MeshRenderer renderer,string layer,int order)
        {
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            renderer.lightProbeUsage=LightProbeUsage.Off;renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
            renderer.sortingLayerName=layer;renderer.sortingOrder=order;
        }
    }

    public sealed class AnPlaneMesh : IDisposable
    {
        public readonly Mesh Mesh;
        public readonly MeshRenderer Renderer;
        public readonly AnPlaneParameter Parameter;
        readonly AnRoot root;
        readonly Material material;
        readonly Vector3[] vertices=new Vector3[4];
        readonly Color[] colors=new Color[4];
        readonly Vector2[] offsetRG=new Vector2[4],offsetBA=new Vector2[4];
        Vector3[] baseVertices;
        int textureIndex=-1,textureKeyIndex;
        Texture replacement;
        bool replacementKeepSize=true;
        public AnPlaneMesh(AnRoot root,AnPlaneParameter parameter,Transform target,int order,AnBlendModeTypes blendMode,AnObjectTypes objectType,int stencilRef,int stencilCompare)
        {
            this.root=root;Parameter=parameter;
            Mesh=new Mesh{name=target.name};Mesh.MarkDynamic();
            target.gameObject.AddComponent<MeshFilter>().sharedMesh=Mesh;
            Renderer=target.gameObject.AddComponent<MeshRenderer>();
            AnMeshGeometry.ConfigureRenderer(Renderer,root._parameter._sortLayerName,order);
            bool stencilMask=objectType==AnObjectTypes.StencilMask||objectType==AnObjectTypes.StencilAlphaMask;
            string shaderName="AnimateToUnity/Plane_NoTexAlpha_"+(objectType==AnObjectTypes.StencilAlphaMask?"StencilAlphaMask":objectType==AnObjectTypes.StencilMask?"StencilMask":blendMode==AnBlendModeTypes.Add?"Add":"Normal");
            material=new Material(Shader.Find(shaderName)){name=target.name+" material"};
            material.SetInt("_Stencil",stencilRef);material.SetInt("_StencilComp",stencilMask?8:stencilCompare);
            Renderer.sharedMaterial=material;parameter._textureKeyParam?.Initialize();
            SelectTexture(0);
        }
        void SelectTexture(int index)
        {
            if(textureIndex==index)return;
            var info=root.FindMesh(Parameter._textureNameList[index],out var group);
            if(info==null)throw new InvalidOperationException("A2U mesh missing: "+Parameter._textureNameList[index]);
            if(info._meshType!=AnMeshTypes.Normal)throw new NotSupportedException("A2U mesh type: "+info._meshType);
            baseVertices=AnMeshGeometry.Quad(replacement!=null&&!replacementKeepSize?new Vector2(replacement.width,replacement.height):info._size);Mesh.vertices=baseVertices;Mesh.triangles=AnMeshGeometry.Triangles;
            Mesh.uv=replacement!=null?new[]{Vector2.zero,Vector2.one,Vector2.right,Vector2.up}:AnMeshGeometry.UV(info);
            Mesh.normals=new[]{Vector3.back,Vector3.back,Vector3.back,Vector3.back};
            material.mainTexture=replacement!=null?replacement:group._textureSetColor;
            textureIndex=index;
        }
        public void SetTexture(Texture texture,bool keepSize=true)
        {replacement=texture;replacementKeepSize=keepSize;int previous=textureIndex;textureIndex=-1;SelectTexture(previous);}
        public void Update(AnObjectState state,AnMotionClock clock,bool visible)
        {
            var key=Parameter._textureKeyParam;
            if(key!=null&&key._keyCount>0)
            {
                int index=(int)key.GetValue(0f,clock.CurrentTime,clock.PreviousTime,clock.FixObjectTime,ref textureKeyIndex,false);
                SelectTexture(index);
            }
            for(int i=0;i<4;i++)
            {
                vertices[i]=AnMeshGeometry.Shear(baseVertices[i],-state.PositionOffset,state.Shear,state.Scale);
                colors[i]=state.CurrentColor;offsetRG[i]=new Vector2(state.CurrentColorOffset.r,state.CurrentColorOffset.g);offsetBA[i]=new Vector2(state.CurrentColorOffset.b,state.CurrentColorOffset.a);
            }
            Mesh.vertices=vertices;Mesh.colors=colors;Mesh.uv2=offsetRG;Mesh.uv3=offsetBA;Mesh.RecalculateBounds();
            Renderer.enabled=visible;
        }
        public void Dispose(){UnityEngine.Object.Destroy(Mesh);UnityEngine.Object.Destroy(material);}
    }
}
