using System.Collections.Generic;
using Gallop.Live.Cutt;

namespace Gallop.Live
{
    public partial class Director
    {
        readonly Dictionary<(int, int), int> liveSystemTextMotionOverrides = new Dictionary<(int, int), int>();
        LiveSystemTextCharacter[] liveSystemTextCharacters;

        void InitializeLiveSystemTextCharacters(List<LiveCharacterLoadData> characters)
        {
            liveSystemTextMotionOverrides.Clear();
            liveSystemTextCharacters = new LiveSystemTextCharacter[characters.Count];
            for (int i = 0; i < characters.Count; i++)
            {
                var entry = characters[i].CharaEntry;
                liveSystemTextCharacters[i] = entry.Name == "" ? new LiveSystemTextCharacter(-1, -1) : new LiveSystemTextCharacter(entry.IsMob ? 1 : entry.Id, entry.IsMob ? entry.Id : 0);
            }
        }

        internal void SetupLiveSystemTextRandomMotions(LiveTimelineData timeline, int[] motionSequence)
        {
            foreach (var sheet in timeline.worksheetList)
            {
                for (int groupIndex = 0; groupIndex < sheet.charaMotSeqList.Count; groupIndex++)
                {
                    var group = sheet.charaMotSeqList[groupIndex];
                    if (!group.ExistsOverrideSystemTextMotion)
                        continue;
                    var character = LiveSystemTextRandomMotion.ResolveTarget(groupIndex, motionSequence, liveSystemTextCharacters);

                    LiveSystemTextRandomMotion.Setup(group, groupIndex, character, UmaDatabaseController.Instance.LiveSystemTextMasterData, liveSystemTextMotionOverrides);

                }
            }
        }
    }
}
