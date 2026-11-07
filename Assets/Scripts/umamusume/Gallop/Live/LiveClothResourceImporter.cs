using System;
using UnityEngine;

namespace Gallop.Live
{
    // Original ClothBuildPathInfo's input surface. Path construction from
    // CharacterBuildInfo is separate and must be supplied by its audited owner.
    [Serializable]
    public sealed class LiveClothBuildPaths
    {
        public int OverrideCategory=-1;
        public string[] Head=new string[5],Bust=new string[5],Body=new string[5],Tail=new string[5];
    }
    public interface ILiveClothImportOwner
    {
        ClothAsset Cloth{get;}
        int LoadHash{get;}
        GameObject Load(string path,int hash);
        void OverrideSkirtController();
    }
    // ModelLoader.ImportClothData controls: original order is Head/Bust/Body/
    // Tail at each of five categories, not all heads then all bodies. Missing
    // resources retain previous slots; loading does not instantiate prefabs.
    public static class LiveClothResourceImporter
    {
        public static void Import(ILiveClothImportOwner owner,LiveClothBuildPaths paths)=>Import(owner,paths,paths.OverrideCategory);
        public static void Import(ILiveClothImportOwner owner,LiveClothBuildPaths paths,int overrideCategory)
        {
            if(overrideCategory>-1&&(int)owner.Cloth.ClothCategory!=overrideCategory)
            {
                owner.Cloth.ClothCategory=(CySpringDataContainer.Category)overrideCategory;
                owner.OverrideSkirtController();
            }
            for(int category=0;category<5;category++)ImportCategory(owner.Cloth,paths,owner.LoadHash,category,owner.Load);
        }
        public static void ImportCategory(ClothAsset cloth,LiveClothBuildPaths paths,int hash,int category,Func<string,int,GameObject> load)
        {
            GameObject prefab;
            if(!string.IsNullOrEmpty(paths.Head[category])){prefab=load(paths.Head[category],hash);if(prefab!=null)cloth.ImportHeadClothData(prefab,category);}
            if(!string.IsNullOrEmpty(paths.Bust[category])){prefab=load(paths.Bust[category],hash);if(prefab!=null)cloth.ImportBustClothData(prefab,category);}
            // Native body branch loads even a null/empty path. The resource
            // manager decides its result; do not add an optional-path gate.
            prefab=load(paths.Body[category],hash);if(prefab!=null)cloth.ImportBodyClothData(prefab,category);
            if(!string.IsNullOrEmpty(paths.Tail[category])){prefab=load(paths.Tail[category],hash);if(prefab!=null)cloth.ImportTailClothData(prefab,category);}
        }
    }
}
