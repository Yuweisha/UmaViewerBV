using System;
using Gallop.Live;
using UnityEngine;

namespace Gallop
{
    public static class AtlasUtil
    {
        // Installed by the resource owner. Absence is not replaced by a guessed atlas.
        public static ILiveAtlasLookupServices<UnityEngine.Object> OriginalServices {get;set;}
        public static Sprite GetSpriteByName(string atlasName,string spriteName)
        {
            if(string.IsNullOrEmpty(spriteName))return null;
            var services=OriginalServices??throw new InvalidOperationException("Original AtlasUtil resource owner is not installed.");
            return (Sprite)new LiveAtlasLookupControl<UnityEngine.Object>(services).GetSpriteByName(atlasName,spriteName);
        }
    }
}
