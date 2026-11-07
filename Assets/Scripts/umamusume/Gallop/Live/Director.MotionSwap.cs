using System;
using System.Collections.Generic;
using Gallop.Live.Cutt;
using UnityEngine;

namespace Gallop.Live
{
    public partial class Director
    {
        public LiveMotionSwapContext MotionSwapContext{get;private set;}
        void InitializeMotionSwapContext(List<LiveCharacterLoadData> characters)
        {
            var context=new LiveMotionSwapContext{CharacterCount=characters.Count,Characters=new LiveMotionSwapCharacter[characters.Count]};
            for(int i=0;i<characters.Count;i++)context.Characters[i]=new LiveMotionSwapCharacter{CharaId=characters[i].CharaEntry.Id,DressIds=new[]{characters[i].MasterDressId}};
            // Original Live3DSettings.Setup reads this independent global CSV
            // from the livesettings bundle; master dress/name heuristics do not
            // determine HandType. The loaded main-model adapter owns one dress.
            var entries=UmaViewerMain.Instance?.AbList;
            if(entries!=null&&entries.TryGetValue("livesettings",out var entry))
            {
                var bundle=UmaAssetManager.LoadAssetBundle(entry,neverUnload:true,isRecursive:true);
                if(bundle!=null)foreach(string path in bundle.GetAllAssetNames())
                    if(path.EndsWith("/live/settings/dress_data_extension.csv",StringComparison.OrdinalIgnoreCase))
                    {context.ReadDressExtensions(bundle.LoadAsset<TextAsset>(path)?.text);break;}
            }
            MotionSwapContext=context;
        }
    }
}
