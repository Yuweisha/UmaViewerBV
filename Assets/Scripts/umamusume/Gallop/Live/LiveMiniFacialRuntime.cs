using System;
using UnityEngine;

namespace Gallop.Live
{
    // Original GridTexture551d780 and GridTextureOffset551d610 atlas writes.
    // Eye material packs left offset in zw and right offset in xy.
    public sealed class LiveMiniGridTexture
    {
        readonly Material material;
        readonly float sizeX,sizeY;
        readonly int columns;
        readonly bool eye,left,valid;
        public event Action<Material,string,Vector4> MaterialWrite;
        public LiveMiniGridTexture(Material material,float sizeX,float sizeY,int columns,bool eye=false,bool left=false)
        {this.material=material;this.sizeX=sizeX;this.sizeY=sizeY;this.columns=columns;this.eye=eye;this.left=left;valid=columns>0&&sizeX>0&&sizeY>0&&material!=null;}
        static float F(float v)=>Cutt.LiveTimelineControl.MonitorFloat32(v);
        public void SetId(int id)
        {
            if(!valid||id<0)return;int x=id%columns,y=id/columns;string property=eye?"_UVOffset":"_MainTex_ST";var value=material.GetVector(property);float offsetX=F(x*sizeX),offsetY=F(y*F(-sizeY));
            if(!eye){value.z=offsetX;value.w=offsetY;}else if(left){value.z=offsetX;value.w=offsetY;}else{value.x=offsetX;value.y=offsetY;}
            material.SetVector(property,value);MaterialWrite?.Invoke(material,property,value);
        }
    }
    // Original MiniFacialController locator branch. Master face-type transitions,
    // lip-sync and Cheek owners are separate from the Live clip locator channel.
    public sealed class LiveMiniFacialRuntime : ILiveMiniFacialAnimation
    {
        readonly Animator animator;
        readonly AnimatorOverrideController animatorOverride;
        readonly Transform eyeL,eyeR,browL,browR,mouth;
        readonly LiveMiniGridTexture eyeLeft,eyeRight,browLeft,browRight,mouthGrid;
        public bool AnimationEnabled {get;set;}
        public bool IsLocatorControll {get;set;}=true;
        public bool IsPaused {get;set;}
        public event Action<Material,string,Vector4> MaterialWrite;
        public LiveMiniFacialRuntime(Material eyebrowL,Material eyebrowR,Material eye,Material mouthMaterial,Transform locator,Animator animator,AnimatorOverrideController animatorOverride)
        {
            this.animator=animator;this.animatorOverride=animatorOverride;
            browLeft=new LiveMiniGridTexture(eyebrowL,0.25f,0.25f,4);browRight=new LiveMiniGridTexture(eyebrowR,0.25f,0.25f,4);eyeLeft=new LiveMiniGridTexture(eye,0.25f,0.125f,4,true,true);eyeRight=new LiveMiniGridTexture(eye,0.25f,0.125f,4,true,false);mouthGrid=new LiveMiniGridTexture(mouthMaterial,0.25f,0.125f,4);
            eyeL=locator.Find("Eye_L_Ctrl");eyeR=locator.Find("Eye_R_Ctrl");browL=locator.Find("Mayu_L_Ctrl");browR=locator.Find("Mayu_R_Ctrl");mouth=locator.Find("Mouth_Ctrl");foreach(var grid in new[]{browLeft,browRight,eyeLeft,eyeRight,mouthGrid})grid.MaterialWrite+=(m,p,v)=>MaterialWrite?.Invoke(m,p,v);
        }
        public void OverrideAnimation(string name,AnimationClip clip){if(animatorOverride!=null)animatorOverride[name]=clip;}
        public void Play(string name,int layer,float normalizedTime)=>animator.Play(Animator.StringToHash(name),layer,normalizedTime);
        static int LocatorId(Transform node)=>unchecked((int)System.Math.Round((double)Cutt.LiveTimelineControl.MonitorFloat32(node.localPosition.x*-100f),MidpointRounding.AwayFromZero));
        public void AfterApplyDrivenKey()
        {
            if(IsPaused||AnimationEnabled||!IsLocatorControll)return;
            eyeLeft.SetId(LocatorId(eyeL));eyeRight.SetId(LocatorId(eyeR));browLeft.SetId(LocatorId(browL));browRight.SetId(LocatorId(browR));mouthGrid.SetId(LocatorId(mouth));
        }
    }
}
