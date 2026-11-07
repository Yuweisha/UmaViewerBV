using System;
using UnityEngine;
using UnityEngine.UI;

namespace Gallop.Live
{
    // Adapter to an existing converted Image. Asset utilities, raw m_Material,
    // button colours and ColorSender/base hooks remain explicit caller bindings.
    public sealed class UnityLiveImageCommonServices : ILiveImageCommonServices<UnityEngine.Object>
    {
        readonly Image image;
        readonly Func<Material> rawMaterial;
        readonly Func<string,string,Sprite> findSprite;
        readonly Func<string,Material> loadMaterial;
        readonly Func<bool,Color> buttonColor;
        readonly Action<Transform> dirtyParent,dirtyHierarchy;
        readonly Action awake,parent,destroy;
        public UnityLiveImageCommonServices(Image image,Func<Material> rawMaterial,
            Func<string,string,Sprite> findSprite,Func<string,Material> loadMaterial,Func<bool,Color> buttonColor,
            Action<Transform> dirtyParent,Action<Transform> dirtyHierarchy,Action awake,Action parent,Action destroy)
        {
            this.image=image?image:throw new ArgumentNullException(nameof(image));
            this.rawMaterial=rawMaterial??throw new ArgumentNullException(nameof(rawMaterial));
            this.findSprite=findSprite??throw new ArgumentNullException(nameof(findSprite));
            this.loadMaterial=loadMaterial??throw new ArgumentNullException(nameof(loadMaterial));
            this.buttonColor=buttonColor??throw new ArgumentNullException(nameof(buttonColor));
            this.dirtyParent=dirtyParent??throw new ArgumentNullException(nameof(dirtyParent));
            this.dirtyHierarchy=dirtyHierarchy??throw new ArgumentNullException(nameof(dirtyHierarchy));
            this.awake=awake??throw new ArgumentNullException(nameof(awake));
            this.parent=parent??throw new ArgumentNullException(nameof(parent));
            this.destroy=destroy??throw new ArgumentNullException(nameof(destroy));
        }
        public void BaseAwake()=>awake();public Color GetColor()=>image.color;public void SetColor(Color c)=>image.color=c;
        public UnityEngine.Object Sprite{get=>image.sprite;set=>image.sprite=(Sprite)value;}
        public UnityEngine.Object Material{get=>rawMaterial();set=>image.material=(Material)value;}
        public bool ObjectEquals(UnityEngine.Object a,UnityEngine.Object b)=>a==b;
        public UnityEngine.Object FindSprite(string a,string s)=>findSprite(a,s);
        public UnityEngine.Object LoadMaterial(string p)=>loadMaterial(p);
        public bool IsNullOrEmpty(string p)=>string.IsNullOrEmpty(p);
        public Color GetButtonColor(bool i)=>buttonColor(i);
        public UnityEngine.Object GetTransform()=>image.transform;
        public void NotifyIgnoreParentColor(UnityEngine.Object t)=>dirtyParent((Transform)t);
        public void BaseParentChanged()=>parent();public void BaseDestroy()=>destroy();
        public void NotifyParentChanged(UnityEngine.Object t)=>dirtyHierarchy((Transform)t);
    }
}
