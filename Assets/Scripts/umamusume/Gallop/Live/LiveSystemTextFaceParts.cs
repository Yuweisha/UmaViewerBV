using System;
using System.Collections.Generic;
using System.Globalization;
using Gallop.Live.Cutt;

namespace Gallop.Live
{
    // MasterFaceTypeData.CreateFaceParts. This table is separate from the
    // compact LiveTimelineDefine facial-id table used by ordinary timeline keys.
    public sealed class LiveSystemTextFaceParts
    {
        readonly Dictionary<string,FaceTypeData> faces=new Dictionary<string,FaceTypeData>();
        readonly Dictionary<string,FacePartsSet> parts=new Dictionary<string,FacePartsSet>();
        public LiveSystemTextFaceParts(IEnumerable<FaceTypeData> source)
        {
            foreach(var face in source)
            {
                faces.Add(face.label,face);
                parts.Add(face.label,new FacePartsSet{_eyeL=Parse(face.eye_l,0),_eyeR=Parse(face.eye_r,0),
                    _eyebrowL=Parse(face.eyebrow_l,1),_eyebrowR=Parse(face.eyebrow_r,1),_mouth=Parse(face.mouth,2)});
            }
        }
        public FaceTypeData GetFace(string label)=>faces.TryGetValue(label,out var value)?value:null;
        public FacePartsSet GetParts(string label)=>parts.TryGetValue(label,out var value)?value:parts["Base"];
        static int Index(string label,int group)
        {
            if(group==0)return Enum.TryParse<LiveTimelineDefine.FacialEyeId>(label,out var eye)?(int)eye:0;
            if(group==1)return Enum.TryParse<LiveTimelineDefine.FacialEyebrowId>(label,out var brow)?(int)brow:0;
            return Enum.TryParse<LiveTimelineDefine.FacialMouthId>(label,out var mouth)?(int)mouth:0;
        }
        public static FaceParts[] Parse(string source,int group)
        {
            var result=new List<FaceParts>();
            foreach(string item in source.Split('|'))
            {
                int split=item.IndexOf("__",StringComparison.Ordinal);string name=item;float weight=1f;
                if(split>=0)
                {
                    name=item.Substring(0,split);int percent=int.Parse(item.Substring(split+2),CultureInfo.InvariantCulture);
                    if(name=="Base")continue;
                    weight=LiveTimelineControl.MonitorFloat32((float)percent/100f);
                }
                result.Add(new FaceParts{_faceParts=Index(name,group),_weight=weight});
            }
            if(result.Count==0)result.Add(new FaceParts{_faceParts=0,_weight=1});
            return result.ToArray();
        }
    }
    public sealed class LiveLipSyncMouthSet
    {
        readonly LiveSystemTextFaceParts faces;
        readonly Dictionary<string,string> overrides=new Dictionary<string,string>();
        public IReadOnlyDictionary<string,string> Overrides=>overrides;
        public LiveLipSyncMouthSet(LiveSystemTextFaceParts faces,IEnumerable<KeyValuePair<string,string>> source)
        {
            this.faces=faces;
            foreach(var pair in source)if(!overrides.ContainsKey(pair.Key))overrides.Add(pair.Key,pair.Value);
        }
        string Override(string label)=>overrides.TryGetValue(label,out string value)?value:label;
        public void Update(LiveLipSyncRuntime lip,FaceTypeData face)
        {
            if(face==null)return;
            string open=face.label,close=face.label;
            if(face.mouth_shape_type==2)close=face.inverce_face_type;
            else if(face.mouth_shape_type==1)open=face.inverce_face_type;
            lip.OpenMouth=faces.GetParts(Override(open))._mouth;lip.CloseMouth=faces.GetParts(Override(close))._mouth;
        }
        public static void CloseMouthIdFromFaceType(LiveLipSyncRuntime lip,string label)
        {
            if(label=="WaraiA"||label=="IkariA"||label=="CloseE"||label=="DoyaA"||label=="FutuA")
                lip.SetMouthId=lip.SetMouthId2=0;
        }
    }
}
