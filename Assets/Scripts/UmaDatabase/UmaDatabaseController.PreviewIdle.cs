using System;

public partial class UmaDatabaseController
{
    internal string[] ReadPreviewRandomMotionNames(int partsType)
    {
        if (partsType != 1 && partsType != 2)
            throw new ArgumentOutOfRangeException(nameof(partsType));
        // Native MakeMotionArray(null filter): ordinary ears use EarType.Base;
        // ordinary tails retain every candidate. UseStory belongs to the Story
        // controller's separate filter, not the ordinary preview controller.
        return ReadMaster(masterDb,
            "SELECT motion_name FROM random_ear_tail_motion WHERE parts_type=" + partsType +
            (partsType == 1 ? " AND ear_type=0" : "") + " ORDER BY id")
            .ConvertAll(row => Convert.ToString(row["motion_name"])).ToArray();
    }
}
