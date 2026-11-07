using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Gallop.Live
{
    public interface ILiveImageCommonServices<T> where T:class
    {
        void BaseAwake(); Color GetColor(); void SetColor(Color color);
        T Sprite {get;set;} T Material {get;set;}
        bool ObjectEquals(T a,T b);
        T FindSprite(string atlasName,string spriteName);
        T LoadMaterial(string path);
        bool IsNullOrEmpty(string path);
        Color GetButtonColor(bool interactable);
        T GetTransform(); void NotifyIgnoreParentColor(T transform);
        void BaseParentChanged(); void BaseDestroy(); void NotifyParentChanged(T transform);
    }
    // Original ImageCommon control, with game asset/parent services explicit.
    public sealed class LiveImageCommonControl<T> where T:class
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        static float F(float value)=>BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(value));
        public Color DefaultColor=Color.white,MulColor=Color.white,ReceiveColor=Color.white;
        public bool SetupDefault,IgnoreParentColor;
        public string AtlasName="",SpriteName="",MaterialPath="";
        readonly ILiveImageCommonServices<T> source;
        public LiveImageCommonControl(ILiveImageCommonServices<T> source){this.source=source;}
        public void SetupDefaultColor()
        {
            if(SetupDefault)return;
            Color color=source.GetColor();SetupDefault=true;DefaultColor=color;
        }
        public void Awake(){source.BaseAwake();SetupDefaultColor();ReconnectSprite();ReconnectMaterial();}
        public void ReconnectReference(){ReconnectSprite();ReconnectMaterial();}
        public void ReconnectSprite()
        {
            if(!source.ObjectEquals(source.Sprite,null))return;
            T sprite=source.FindSprite(AtlasName,SpriteName);
            if(!source.ObjectEquals(sprite,null))source.Sprite=sprite;
        }
        public void ReconnectMaterial()
        {
            if(!source.ObjectEquals(source.Material,null)||source.IsNullOrEmpty(MaterialPath))return;
            source.Material=source.LoadMaterial(MaterialPath);
        }
        public void SetDefaultColor(Color color){DefaultColor=color;}
        public void SetReceiveColor(Color color){ReceiveColor=color;}
        public void SetMulColor(Color color){MulColor=color;UpdateColor();}
        public void SetAlpha(float alpha){MulColor.a=alpha;UpdateColor();}
        public void SetInteractableMulColor(bool value){MulColor=source.GetButtonColor(value);UpdateColor();}
        public void SetIgnoreParentColor(bool value)
        {
            if(IgnoreParentColor==value)return;
            IgnoreParentColor=value;source.NotifyIgnoreParentColor(source.GetTransform());
        }
        public void OnTransformParentChanged(){source.BaseParentChanged();source.NotifyParentChanged(source.GetTransform());}
        public void OnDestroy(){source.BaseDestroy();source.NotifyParentChanged(source.GetTransform());}
        public void UpdateColor()
        {
            SetupDefaultColor();
            Color m=MulColor,d=DefaultColor,r=ReceiveColor;
            source.SetColor(new Color(F(F(m.r*d.r)*r.r),F(F(m.g*d.g)*r.g),F(F(m.b*d.b)*r.b),F(F(m.a*d.a)*r.a)));
        }
    }
}
