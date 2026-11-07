using System.Collections.Generic;
using Gallop.Live;
using UnityEngine;

namespace Cute.UI
{
    // Original ScriptableObject fields and OnEnable/GetSprite; no fallback atlas.
    public class AtlasReference : ScriptableObject,ILiveAtlasReferenceServices<Sprite>
    {
        public Material material;
        public Sprite[] sprites;
        private LiveAtlasReferenceControl<Sprite> control;
        private void OnEnable()
        {
            if(control==null)control=new LiveAtlasReferenceControl<Sprite>(this);
            control.Sprites=sprites;control.OnEnable();
        }
        public Sprite GetSprite(string name)=>control==null?null:control.GetSprite(name);
        public Dictionary<string,Sprite> NewDictionary(int capacity)=>new Dictionary<string,Sprite>(capacity);
        public string GetName(Sprite sprite)=>sprite.name;
        public void Add(Dictionary<string,Sprite> dictionary,string name,Sprite sprite)=>dictionary.Add(name,sprite);
        public bool TryGet(Dictionary<string,Sprite> dictionary,string name,out Sprite sprite)=>dictionary.TryGetValue(name,out sprite);
    }
}
