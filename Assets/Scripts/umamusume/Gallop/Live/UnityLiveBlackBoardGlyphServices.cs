using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using TargetEntry = Gallop.Live.LiveBlackBoardTargetControl<UnityEngine.MeshRenderer, UnityEngine.Material>.TargetMesh;

namespace Gallop.Live
{
    // Actual engine bridge. Converted AtlasReference/ImageCommon and original
    // layer/property tables must be supplied; no substitute font or UI fallback.
    public sealed class UnityLiveBlackBoardGlyphServices : ILiveBlackBoardGlyphServices<object>
    {
        readonly Func<object,string,Sprite> sprite;
        readonly Func<GameObject,Image> addImageCommon;
        readonly Func<int,int> layer, property;
        public UnityLiveBlackBoardGlyphServices(Func<GameObject,Image> addImageCommon, Func<int,int> layer, Func<int,int> property)
            : this((atlas,name)=>((Cute.UI.AtlasReference)atlas).GetSprite(name),addImageCommon,layer,property) { }
        public UnityLiveBlackBoardGlyphServices(Func<object,string,Sprite> sprite,
            Func<GameObject,Image> addImageCommon, Func<int,int> layer, Func<int,int> property)
        {
            this.sprite=sprite??throw new ArgumentNullException(nameof(sprite));
            this.addImageCommon=addImageCommon??throw new ArgumentNullException(nameof(addImageCommon));
            this.layer=layer??throw new ArgumentNullException(nameof(layer));
            this.property=property??throw new ArgumentNullException(nameof(property));
        }
        public bool ObjectEquals(object a,object b)
        {
            if((a==null||a is UnityEngine.Object)&&(b==null||b is UnityEngine.Object))return (UnityEngine.Object)a==(UnityEngine.Object)b;
            return ReferenceEquals(a,b);
        }
        public object GetCurrentCulture()=>CultureInfo.CurrentCulture;
        public void TouchCompareInfo(object c){_ = ((CultureInfo)c).CompareInfo;}
        public object NewGameObject()=>new GameObject();
        public object GetTransform(object o,bool component)=>component?((Component)o).transform:((GameObject)o).transform;
        public void SetParent(object t,object p)=>((Transform)t).SetParent((Transform)p);
        public int GetLayer(int i)=>layer(i);
        public void SetLayer(object o,int l)=>((GameObject)o).layer=l;
        public string Substring(string v,int s,int n)=>v.Substring(s,n);
        public int IndexOf(string v,string p)=>v.IndexOf(p);
        public string FormatSpriteName(int i)=>string.Format("txt_{0:D2}",i);
        public object GetSprite(object a,string n)=>sprite(a,n);
        public object AddImageCommon(object o)=>addImageCommon((GameObject)o);
        public void EnsureImageType(object i){if(!(i is Image))throw new ArrayTypeMismatchException();}
        public void SetSprite(object i,object s)=>((Image)i).sprite=(Sprite)s;
        public void SetNativeSize(object i)=>((Image)i).SetNativeSize();
        public object GetRectTransform(object i)=>((Image)i).rectTransform;
        public void SetLocalScale(object t,Vector3 v)=>((Transform)t).localScale=v;
        public void SetLocalPosition(object t,Vector3 v)=>((Transform)t).localPosition=v;
        public Vector3 GetLocalPosition(object t)=>((Transform)t).localPosition;
        public void Render(object c)=>((Camera)c).Render();
        public object NewTexture(int w,int h,int f,bool mip)=>new Texture2D(w,h,(TextureFormat)f,mip);
        public object GetActiveRT()=>RenderTexture.active;
        public void SetActiveRT(object r)=>RenderTexture.active=(RenderTexture)r;
        public void ReadPixels(object t,Rect r,int x,int y)=>((Texture2D)t).ReadPixels(r,x,y);
        public void Apply(object t)=>((Texture2D)t).Apply();
        public void Discard(object r)=>((RenderTexture)r).DiscardContents();
        public int GetTargetCount(object l)=>((List<TargetEntry>)l).Count;
        sealed class Target : ILiveBlackBoardGlyphTarget<object>
        {
            readonly TargetEntry entry;internal Target(TargetEntry entry){this.entry=entry;}
            public object Renderer=>entry.Renderer;public object Material=>entry.Material;
        }
        public ILiveBlackBoardGlyphTarget<object> GetTarget(object l,int i)
        {var entry=((List<TargetEntry>)l)[i];return entry==null?null:new Target(entry);}
        public int GetPropertyId(int i)=>property(i);
        public void SetTexture(object m,int p,object t)=>((Material)m).SetTexture(p,(Texture)t);
        public void SetSharedMaterial(object r,object m)=>((Renderer)r).sharedMaterial=(Material)m;
        public void SetActive(object o,bool a)=>((GameObject)o).SetActive(a);
    }
}
