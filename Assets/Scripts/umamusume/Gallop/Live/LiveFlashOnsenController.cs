using System;
using UnityEngine;

namespace Gallop.Live
{
    public sealed class LiveFlashOnsenController : LiveFlashController
    {
        const string FontName="FOT-TsukuMin Pro E";
        public string[] CharacterNames=Array.Empty<string>();
        public int ReplacedTextCount{get;private set;}
        public override void Initialize(string resourcePath)
        {
            var font=LoadOnView<Font>("Font/Mincho/FOT-TsukuMinPro-E");
            if(font!=null)ReplaceFlashFont(FontName,font);
            base.Initialize(resourcePath);
            string first=CharacterNames.Length>0?CharacterNames[0]:string.Empty;
            string second=CharacterNames.Length>1?CharacterNames[1]:string.Empty;
            // Localize.JP.Get(2508/2509), called by Onsen.SetCharacterName.
            string title=string.Format("♪ {0} / {1}",first,second);
            string outro=string.Format("♪ {0}\n♪ {1}",first,second);
            ReplacedTextCount=0;
            SetName(title,"MOT_character_text00/TXT_object0");
            SetName(title,"OBJ_clr_character_text00/offset/MOT_clr_character_text00/TXT_object0");
            SetName(outro,"OBJ_outro/MOT_outro/TXT_txt_outro00_2");
            SetName(outro,"OBJ_clr_outro/MOT_clr_outro/TXT_txt_outro00_2");
        }
        void SetName(string text,string path)
        {if(SetFlashText(text,path,false,null))ReplacedTextCount++;}
        protected override void DestroySub()
        {ReplaceFlashFont(FontName,null);base.DestroySub();}
    }
}
