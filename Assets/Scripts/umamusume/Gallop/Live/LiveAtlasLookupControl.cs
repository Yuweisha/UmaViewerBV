using System;

namespace Gallop.Live
{
    public interface ILiveAtlasLookupServices<T> where T:class
    {
        bool IsManualDownload(string atlasName);
        string FormatPath(string format,string atlasName);
        bool HasResourceManager(); bool HasUIManager();
        int GetCurrentViewHash();
        T LoadOnScene(string path); T LoadOnView(string path,int viewHash);
        T GetUIAtlas(int atlasType,bool load);
        bool IsUnityNull(T atlas); T GetSprite(T atlas,string spriteName);
    }
    // AtlasUtil 4c13110/4c13120/4c131e0. No atlas substitution on lookup failure.
    public sealed class LiveAtlasLookupControl<T> where T:class
    {
        readonly ILiveAtlasLookupServices<T> services;
        public LiveAtlasLookupControl(ILiveAtlasLookupServices<T> services){this.services=services;}
        public T GetSpriteByName(string atlasName,string spriteName)
        {
            if(string.IsNullOrEmpty(spriteName))return null;
            string format=services.IsManualDownload(atlasName)?"ManualDownloadAtlas/{0}/{0}":"Atlas/{0}/{0}";
            string path=services.FormatPath(format,atlasName);
            if(!services.HasResourceManager())return null;
            T atlas;
            if(!services.HasUIManager())atlas=services.LoadOnScene(path);
            else
            {
                int hash=services.GetCurrentViewHash();
                atlas=hash==-1?services.LoadOnScene(path):services.LoadOnView(path,hash);
            }
            if(services.IsUnityNull(atlas))return null;
            if(atlas==null)throw new NullReferenceException();
            return services.GetSprite(atlas,spriteName);
        }
        public T GetSpriteByName(int atlasType,string spriteName)
        {
            T atlas=services.GetUIAtlas(atlasType,true);
            if(services.IsUnityNull(atlas))return null;
            if(atlas==null)throw new NullReferenceException();
            return services.GetSprite(atlas,spriteName);
        }
    }
}
