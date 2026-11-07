using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Gallop;
using Gallop.Live;
using Gallop.Live.Cutt;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>Original Live mouth/lip keys, not audio-derived replacement phonemes.</summary>
public static class NativeLipExporter
{
    public static JObject Key(LiveTimelineKey key)
    {
        var mouth=key as LiveTimelineKeyFacialMouthData;var lip=key as LiveTimelineKeyLipSyncData;
        if(mouth==null&&lip==null)throw new ArgumentException("Not a mouth/lip key");
        var parts=mouth!=null?mouth.facialPartsDataArray:lip.facialPartsDataArray;
        var j=new JObject{["frame"]=key.frame,["attribute"]=(int)key.attribute,
            ["facialId"]=mouth!=null?mouth.facialId:lip.facialId,["weight"]=mouth!=null?mouth.weight:lip.weight,
            ["speed"]=mouth!=null?mouth.speed:lip.speed,["time"]=mouth!=null?mouth.time:lip.time,
            ["interpolateType"]=(int)(mouth!=null?mouth.interpolateType:lip.interpolateType),
            ["parts"]=new JArray((parts??new FacialPartsData[0]).Select(p=>new JObject{["id"]=p.FacialPartsId,["weightPercent"]=p.WeightPer}))};
        if(lip!=null)j["characterMask"]=(int)lip.character;else j["type"]=mouth.type;
        return j;
    }
    public static JObject Track(ILiveTimelineKeyDataList list,bool present=true) => new JObject {
        ["present"]=present,["attribute"]=list!=null?(int)list.attribute:0,
        ["playMode"]=list!=null?(int)list.playMode:0,
        ["keys"]=new JArray(list==null?Enumerable.Empty<JObject>():list.ToArray().Select(Key))};
    public static JObject Capture(LiveTimelineWorkSheet sheet,FaceDrivenKeyTarget face,JObject source)
    {
        if(sheet==null||face==null)throw new ArgumentNullException();
        var facial=new List<LiveTimelineFacialData>{sheet.facial1Set};
        facial.AddRange(sheet.other4FacialArray??new LiveTimelineFacialData[0]);
        return new JObject{["schema"]="umaviewer.native-lip/1",["sourceFps"]=60,["source"]=source,
            ["semanticsReference"]="Current UmaViewer LiveTimelineControl/Director/FaceDrivenKeyTarget, not DLL-equivalence proof",
            ["mouthOrder"]=new JArray(face.MouthMorphs.Select(m=>m.name)),
            ["mouthTracks"]=new JArray(facial.Select(f=>Track(f?.mouthKeys,f!=null))),
            ["lipTracks"]=new JArray(Track(sheet.ripSyncKeys),Track(sheet.ripSync2Keys)),
            ["order"]=new JArray("facial mouth + lock","ripSyncKeys","ripSync2Keys"),
            ["historyClock"]="60 Hz from cleared mouth and lock=false; fractional frame evaluated after previous source tick",
            ["unsupported"]=new JArray("Audio, body/camera/stage tracks and non-mouth facial tracks are not included",
                "Story voice scheduling is not provided; this package is a Live mouth source")};
    }
    public static void Write(string path,JObject data)
    {
        if(File.Exists(path))throw new IOException("Choose a new lip timeline name");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        File.WriteAllText(path,data.ToString(Formatting.Indented));
    }
    public static void Export(UmaContainerCharacter c,int position,string path)
    {
        var d=Director.instance;
        if(d==null||d._liveTimelineControl?.data?.worksheetList==null||d._liveTimelineControl.data.worksheetList.Count==0)
            throw new InvalidOperationException("Load a Live timeline first");
        // The export panel omits null characters; its compact selection index
        // must not replace the original bit-mask position in the Live cast.
        position=Array.IndexOf(d.CharaContainerScript.ToArray(),c);
        if(position<0)throw new InvalidOperationException("Character is not in the loaded Live cast");
        var source=new JObject{["kind"]="loaded-live",["musicId"]=d.live.MusicId,["character"]=c.CharaEntry.Id,["position"]=position};
        string name=$"cutt/cutt_son{d.live.MusicId}/data";
        if(UmaViewerMain.Instance.AbList.TryGetValue(name,out var entry)){
            source["resource"]=entry.Name;source["checksum"]=entry.Checksum;
            if(File.Exists(entry.Path))source["sha256"]=NativeFaceExporter.Hash(entry.Path);
        }
        Write(path,Capture(d._liveTimelineControl.data.worksheetList[0],c.FaceDrivenKeyTarget,source));
    }
}
