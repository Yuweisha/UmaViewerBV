using System;
using System.Collections.Generic;
using System.Data;
using Gallop.Live.Cutt;

public partial class UmaDatabaseController
{
    Gallop.Live.LiveSystemTextFaceParts liveSystemTextFaceParts;
    public Gallop.Live.LiveSystemTextFaceParts LiveSystemTextFaceParts=>liveSystemTextFaceParts??(liveSystemTextFaceParts=new Gallop.Live.LiveSystemTextFaceParts(FaceTypeData));
    public Gallop.Live.LiveLipSyncMouthSet ReadLiveLipSyncMouthSet(int characterId)
    {
        var rules=new List<KeyValuePair<string,string>>();
        foreach(var row in ReadMaster(masterDb,"SELECT before_facialname,after_facialname FROM facial_mouth_change WHERE chara_id="+characterId+" ORDER BY id"))
            rules.Add(new KeyValuePair<string,string>(Convert.ToString(row["before_facialname"]),Convert.ToString(row["after_facialname"])));
        return new Gallop.Live.LiveLipSyncMouthSet(LiveSystemTextFaceParts,rules);
    }
    Dictionary<string,int> liveEventMotionTypes;
    public LiveSystemTextMotionSet ReadSystemTextIdleMotion(int characterId,bool isMob)
    {
        var rows=ReadMaster(masterDb,"SELECT standby_motion_id_1 FROM system_voice_standby_motion WHERE chara_id="+characterId);
        int id=rows.Count>0?Convert.ToInt32(rows[0]["standby_motion_id_1"]):isMob?8000:9999999;
        return LiveSystemTextMasterData.GetMotionSet(id);
    }
    public int? ReadSystemTextEventMotionType(string name)
    {
        if(liveEventMotionTypes==null)
        {
            liveEventMotionTypes=new Dictionary<string,int>();
            foreach(var row in ReadMaster(masterDb,"SELECT command_name,type FROM event_motion_data WHERE type BETWEEN -2 AND 0 ORDER BY type,id"))
            {
                string command=Convert.ToString(row["command_name"]);int type=Convert.ToInt32(row["type"]);
                liveEventMotionTypes.Add(command,type);liveEventMotionTypes.Add(command+"_mirror",type);
            }
        }
        return liveEventMotionTypes.TryGetValue(name,out int value)?value:(int?)null;
    }
    LiveSystemTextMaster liveSystemTextMaster;
    public ILiveSystemTextMaster LiveSystemTextMasterData=>liveSystemTextMaster??(liveSystemTextMaster=new LiveSystemTextMaster(this));
    sealed class LiveSystemTextMaster:ILiveSystemTextMaster
    {
        readonly UmaDatabaseController database;
        readonly Dictionary<int,Dictionary<int,LiveSystemTextData>> texts=new Dictionary<int,Dictionary<int,LiveSystemTextData>>();
        readonly Dictionary<int,LiveSystemTextMotionSet> motions=new Dictionary<int,LiveSystemTextMotionSet>();
        List<DataRow> cards;
        static int I(DataRow row,string name)=>Convert.ToInt32(row[name]);
        static string S(DataRow row,string name)=>Convert.ToString(row[name]);
        public LiveSystemTextMaster(UmaDatabaseController value){database=value;}
        public LiveSystemTextData GetText(int characterId,int voiceId)
        {
            if(!texts.TryGetValue(characterId,out var values))
            {
                values=new Dictionary<int,LiveSystemTextData>();
                foreach(var row in ReadMaster(database.masterDb,"SELECT * FROM character_system_text WHERE character_id="+characterId))
                {
                    var text=new LiveSystemTextData{CharacterId=I(row,"character_id"),VoiceId=I(row,"voice_id"),MotionSet=I(row,"motion_set"),
                        MotionSecondSet=I(row,"motion_second_set"),MotionSecondStart=I(row,"motion_second_start"),LipSyncData=S(row,"lip_sync_data"),
                        LipSyncFlag=I(row,"lip_sync_flag"),PropSystemTextId=I(row,"prop_system_text_id")};
                    values.Add(text.VoiceId,text);
                }
                texts.Add(characterId,values);
            }
            return values.TryGetValue(voiceId,out var result)?result:null;
        }
        public LiveSystemTextMotionSet GetMotionSet(int id)
        {
            if(motions.TryGetValue(id,out var cached))return cached;
            var rows=ReadMaster(database.masterDb,"SELECT * FROM chara_motion_set WHERE id="+id);
            LiveSystemTextMotionSet result=null;
            if(rows.Count>0)
            {
                var row=rows[0];result=new LiveSystemTextMotionSet{Id=id,BodyMotion=S(row,"body_motion"),BodyMotionType=I(row,"body_motion_type"),
                    BodyMotionPathSegment=I(row,"body_motion_path_segment"),BodyMotionPlayType=I(row,"body_motion_play_type"),FaceType=S(row,"face_type"),
                    Cheek=I(row,"cheek"),EyeDefault=I(row,"eye_default"),EarMotion=S(row,"ear_motion"),TailMotion=S(row,"tail_motion"),TailMotionType=I(row,"tail_motion_type")};
            }
            motions.Add(id,result);return result;
        }
        public List<int> GetPlayableCharacterIds()
        {
            if(cards==null)cards=ReadMaster(database.masterDb,"SELECT id,chara_id,default_rarity FROM card_data");
            long now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();var result=new List<int>();var seen=new HashSet<int>();
            foreach(var card in cards)
            {
                if(I(card,"default_rarity")==0&&I(card,"id")>9000000)continue;
                int id=I(card,"chara_id");var character=database.CharaData.Find(row=>I(row,"id")==id);
                if(character==null||now<Convert.ToInt64(character["start_date"]))continue;
                if(!seen.Add(id))continue;
                if(I(character,"chara_category")==0)result.Add(id);
            }
            return result;
        }
        public int GetPersonality(LiveSystemTextCharacter character)=>database.ReadLiveRunMotionIdentity(new CharaEntry
            {Id=character.IsMob?character.MobId:character.CharacterId,IsMob=character.IsMob}).Personality;
    }
}
