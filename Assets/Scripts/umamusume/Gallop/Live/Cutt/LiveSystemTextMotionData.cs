using System.Collections.Generic;

namespace Gallop.Live.Cutt
{
    public sealed class LiveSystemTextData
    {
        public int CharacterId,VoiceId,MotionSet,MotionSecondSet,MotionSecondStart;
        public string LipSyncData;
        public int LipSyncFlag,PropSystemTextId;
    }
    public sealed class LiveSystemTextMotionSet
    {
        public int Id,BodyMotionType,BodyMotionPathSegment,BodyMotionPlayType;
        public string BodyMotion,FaceType,EarMotion,TailMotion;
        public int Cheek,EyeDefault,TailMotionType;
    }
    public readonly struct LiveSystemTextCharacter
    {
        public readonly int CharacterId,MobId;
        public bool IsMob=>CharacterId==1&&MobId!=0;
        public LiveSystemTextCharacter(int characterId,int mobId){CharacterId=characterId;MobId=mobId;}
    }
    public interface ILiveSystemTextMaster
    {
        LiveSystemTextData GetText(int characterId,int voiceId);
        LiveSystemTextMotionSet GetMotionSet(int id);
        List<int> GetPlayableCharacterIds();
        int GetPersonality(LiveSystemTextCharacter character);
    }
}
