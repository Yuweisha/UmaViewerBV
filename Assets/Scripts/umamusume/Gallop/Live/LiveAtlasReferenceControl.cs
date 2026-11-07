using System;
using System.Collections.Generic;

namespace Gallop.Live
{
    public interface ILiveAtlasReferenceServices<T> where T:class
    {
        Dictionary<string,T> NewDictionary(int capacity);
        string GetName(T sprite);
        void Add(Dictionary<string,T> dictionary,string name,T sprite);
        bool TryGet(Dictionary<string,T> dictionary,string name,out T sprite);
    }
    public sealed class LiveAtlasReferenceControl<T> where T:class
    {
        public T[] Sprites;
        public Dictionary<string,T> Dictionary;
        readonly ILiveAtlasReferenceServices<T> source;
        public LiveAtlasReferenceControl(ILiveAtlasReferenceServices<T> source){this.source=source;}
        public void OnEnable()
        {
            if(Sprites==null)return; // Keep an existing dictionary on null array.
            Dictionary=source.NewDictionary(Sprites.Length);
            for(int i=0;i<Sprites.Length;i++)
            {
                T sprite=Sprites[i];var dictionary=Dictionary;
                if(ReferenceEquals(sprite,null))throw new NullReferenceException();
                string name=source.GetName(sprite);
                if(dictionary==null)throw new NullReferenceException();
                source.Add(dictionary,name,sprite);
            }
        }
        public T GetSprite(string name)
        {
            if(Dictionary==null)return null;
            T sprite;return source.TryGet(Dictionary,name,out sprite)?sprite:null;
        }
    }
}
