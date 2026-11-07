using System;
using System.Linq;
using UnityEngine;

namespace Gallop.Live
{
    public sealed class LiveFlashRamenController : LiveFlashController
    {
        Texture2D[] lyricsTextures;
        public int ContextTextureCount{get;private set;}
        public static string[] GetLyricsImagePaths()=>Enumerable.Range(0,7)
            .Select(i=>$"Single/ScenarioRamen/utx_txt_live_fontmask_{i:D2}").ToArray();
        public void Setup(string[] paths)
        {
            if(!IsInitialized||paths==null||paths.Length==0)return;
            lyricsTextures=new Texture2D[paths.Length];ContextTextureCount=0;
            for(int i=0;i<paths.Length;i++)
            {
                var texture=LoadOnView<Texture2D>(paths[i]);lyricsTextures[i]=texture;
                if(texture!=null&&SetFlashTexture($"PLN_dum_slide{i:D2}",texture,0,true,false,null))ContextTextureCount++;
            }
        }
        protected override void DestroySub(){lyricsTextures=null;base.DestroySub();}
    }
}
