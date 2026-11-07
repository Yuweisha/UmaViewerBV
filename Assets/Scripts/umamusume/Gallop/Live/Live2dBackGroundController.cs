using System.Collections.Generic;
using System.Linq;
using Gallop.Live.Cutt;
using UnityEngine;

namespace Gallop.Live
{
    public sealed class Live2dBackGroundController
    {
        readonly Dictionary<(int,int),Texture2D> images=new Dictionary<(int,int),Texture2D>();
        readonly List<UmaDatabaseEntry> entries=new List<UmaDatabaseEntry>();
        // ResourcePath.GetBackgroundPath0x3550900, no atlas/guessed filename.
        public static string Path(int id,int sub)=>$"bg/bg_{id:D4}_{sub:D5}";
        public static IEnumerable<string> ResourcePaths(LiveTimelineControl timeline)=>
            timeline.data.worksheetList.Where(s=>s?.objectList!=null).SelectMany(s=>s.objectList)
                .Where(g=>g?.keys?.thisList!=null).SelectMany(g=>g.keys.thisList)
                .Where(k=>k.Use2dBackground).Select(k=>Path(k.BgId,k.BgSubId)).Distinct();
        public void LoadAllBackgroundTexture(LiveTimelineControl timeline)
        {
            foreach(var sheet in timeline.data.worksheetList)
                if(sheet?.objectList!=null)foreach(var group in sheet.objectList)
                    if(group?.keys?.thisList!=null)foreach(var key in group.keys.thisList)
                        if(key.Use2dBackground&&!images.ContainsKey((key.BgId,key.BgSubId)))
                        {
                            var path=Path(key.BgId,key.BgSubId);
                            if(!UmaViewerMain.Instance.AbList.TryGetValue(path,out var entry))throw new System.InvalidOperationException("Missing Live background: "+path);
                            var bundle=UmaAssetManager.LoadAssetBundle(entry);
                            entries.Add(entry);
                            images.Add((key.BgId,key.BgSubId),bundle.LoadAllAssets<Texture2D>().First());
                        }
        }
        public Texture2D Get(int id,int sub)=>images.TryGetValue((id,sub),out var image)?image:null;
        public bool Owns(Texture texture)=>images.Values.Any(value=>value==texture);
        public void Release(){foreach(var entry in entries)UmaAssetManager.UnloadAssetBundle(entry,false);entries.Clear();images.Clear();}
    }
}
