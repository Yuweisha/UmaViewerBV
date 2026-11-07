using System.Collections.Generic;
using Gallop.Live.Cutt;
using Gallop.RenderPipeline;
using UnityEngine;

namespace Gallop.Live
{
    public partial class StageController
    {
        // Original Initialize4eadb70 / RenderUtils.GetMaterialArray51da430.
        // Hash the source slot name, retained on the renderer's material instance.
        readonly Dictionary<int,List<Material>> _instanceMaterialDictionary = new Dictionary<int,List<Material>>();
        static readonly Dictionary<int,int> TextureWashLabels = TextureLabels("BgWash");
        static readonly Dictionary<int,int> TextureFootLabels = TextureLabels("BgFoot");
        static readonly Dictionary<int,int> TextureMovieLabels = TextureLabels("Monitor");
        [SerializeField] MovieMaterialController.MovieMaterialInfo[] _movieMaterialInfos;
        static Dictionary<int,int> TextureLabels(string header)
        {
            var labels = new Dictionary<int,int>();
            for (int i=0;i<26;i++) labels.Add(FNVHash.Generate(header+(char)('A'+i)),i);
            return labels;
        }
        void InitializeTextureAnimationMaterials()
        {
            _instanceMaterialDictionary.Clear();
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                var materials = RenderUtils.GetMaterialArray(renderer);
                if (materials == null) continue;
                foreach (var material in materials)
                {
                    if (material == null) continue;
                    int hash = FNVHash.Generate(material.name);
                    if (!_instanceMaterialDictionary.TryGetValue(hash,out var list))
                        _instanceMaterialDictionary.Add(hash,list=new List<Material>());
                    list.Add(material);
                }
            }
        }
        void UpdateTextureAnimation(TextureAnimationUpdateInfo info)
        {
            LiveTextureAnimationConsumer.Dispatch(info.LabelId,info,_instanceMaterialDictionary,
                _washLightMaterials,_footLightMaterials,
                hash=>TextureWashLabels.TryGetValue(hash,out int index)?index:(int?)null,
                hash=>TextureFootLabels.TryGetValue(hash,out int index)?index:(int?)null,UpdateMovieMaterialTextureAnimation);
        }
        void UpdateMovieMaterialTextureAnimation(TextureAnimationUpdateInfo info)
        {
            if(_movieMaterialInfos==null||!TextureMovieLabels.TryGetValue(info.LabelId,out int index)||index>=_movieMaterialInfos.Length)return;
            var source=_movieMaterialInfos[index]?.Material;
            if(source==null||!_instanceMaterialDictionary.TryGetValue(FNVHash.Generate(source.name),out var materials))return;
            // Original movie fallback always writes main texture ST.
            info.UseMainTexture=true;
            foreach(var material in materials)LiveTextureAnimationUpdate.Apply(material,info);
        }
    }
}
