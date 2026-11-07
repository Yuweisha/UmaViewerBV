using System.Collections.Generic;
using System.Data;

public partial class UmaDatabaseController
{
    public List<DataRow> ReadRecommendedStoryLivePositions(int musicId)
    {
        return ReadMaster(masterDb,
            "SELECT * FROM story_live_position WHERE music_id=" + musicId +
            " AND set_id=(SELECT MIN(set_id) FROM story_live_position WHERE music_id=" + musicId +
            " GROUP BY music_id HAVING COUNT(DISTINCT set_id)=1) ORDER BY position_id");
    }

    public List<DataRow> ReadLiveRecommendFormation(int musicId)
    {
        return ReadMaster(masterDb,
            "SELECT position_id,chara_type,chara_id,dress_id,dress_color,second_dress_id,second_dress_color " +
            "FROM live_recommend_formation WHERE music_id=" + musicId + " ORDER BY position_id");
    }

    public List<DataRow> ReadLiveFixMembers(int musicId)
    {
        return ReadMaster(masterDb,
            "SELECT \"order\",chara_id,dress_id1,dress_id2,dress_color1,dress_color2 " +
            "FROM live_fix_member_data WHERE music_id=" + musicId + " ORDER BY \"order\"");
    }
}
