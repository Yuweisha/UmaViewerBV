using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Gallop.Live
{
    // ResourcePath.GetLiveSettingsPath 0x349d4f0 -> livesettings TextAsset;
    // Master3dLive.Live3dData constructor 0x4e8cad0, DataType=11.
    // Timeline TextureId is a dictionary key, not a filename suffix.
    public sealed class LiveSettingsResourceMap
    {
        public readonly Dictionary<int,string> Projector = new Dictionary<int,string>();
        public readonly Dictionary<int,string> MirrorBall = new Dictionary<int,string>();
        public readonly List<int> MirrorScanTextureIds = new List<int>();
        public readonly List<string> AudienceAnimationNames = new List<string>();
        public readonly List<LiveMiniSelection.Rule> MiniCharacters = new List<LiveMiniSelection.Rule>();
        public bool UseLipSyncPattern { get; private set; }
        public int PrePlayMusicId { get; private set; }
        public string SourceCsv { get; private set; }
        public int VariationDefaultPattern { get; private set; }
        public int VariationMin { get; private set; }
        public int VariationMax { get; private set; }
        public bool UseVariationCharacterData { get; private set; }
        public int OkeLiveVariationPattern { get; private set; }
        // Raw rows preserved for settings types whose native consumer is not yet
        // identified (for example the authored SongPartVariationId lives in the
        // Live3DSettings asset, not in this CSV). Keeping the rows avoids
        // silently dropping authored values while their mapping is verified.
        public readonly List<(int Type, List<string> Fields)> RawRows = new List<(int Type, List<string> Fields)>();
        public readonly List<LiveVariationSelection.Rule> VariationCharacterRules = new List<LiveVariationSelection.Rule>();
        public readonly List<int> PermissionCharacterVoices = new List<int>();
        public readonly List<(int MusicResourceId,int VoiceResourceId,int SheetVariationId)> MusicResourceOverrides =
            new List<(int,int,int)>();

        public static LiveSettingsResourceMap Load(int musicId)
        {
            var result = new LiveSettingsResourceMap();
            var main = UmaViewerMain.Instance;
            if (main == null || !main.AbList.TryGetValue("livesettings", out var entry)) return result;
            var bundle = UmaAssetManager.LoadAssetBundle(entry, neverUnload:true, isRecursive:true);
            if (bundle == null) return result;
            string suffix = "/live/settings/" + musicId.ToString("D4",CultureInfo.InvariantCulture) + ".csv";
            foreach (string path in bundle.GetAllAssetNames())
                if (path.EndsWith(suffix,StringComparison.OrdinalIgnoreCase))
                {
                    var source = bundle.LoadAsset<TextAsset>(path);
                    if (source != null) result.Read(source.text);
                    break;
                }
            foreach(string path in bundle.GetAllAssetNames())
                if(path.EndsWith("/live/settings/live_permission_chara_voice_data.csv",StringComparison.OrdinalIgnoreCase))
                {
                    var source=bundle.LoadAsset<TextAsset>(path);
                    if(source!=null)foreach(var fields in Rows(source.text))
                        if(fields.Count>=2&&int.TryParse(fields[0],out int song)&&song==musicId&&int.TryParse(fields[1],out int character))
                            result.PermissionCharacterVoices.Add(character);
                    break;
                }
            if (result.UseVariationCharacterData)
            {
                string variationSuffix = "/live/settings/live_variation_chara_data_" +
                    musicId.ToString("D4",CultureInfo.InvariantCulture) + ".csv";
                foreach (string path in bundle.GetAllAssetNames())
                    if (path.EndsWith(variationSuffix,StringComparison.OrdinalIgnoreCase))
                    {
                        var source = bundle.LoadAsset<TextAsset>(path);
                        if (source != null) result.VariationCharacterRules.AddRange(LiveVariationSelection.ParseRules(source.text));
                        break;
                    }
            }
            return result;
        }

        internal void Read(string csv)
        {
            SourceCsv = csv;
            foreach (var fields in Rows(csv))
            {
                if (fields.Count < 3 || !int.TryParse(fields[1],out int type)) continue;
                RawRows.Add((type, new List<string>(fields)));
                // Native Live3dData case24 stores PrePlayMusicId (+110).
                if (type == 24) { PrePlayMusicId = ReadInteger(fields,2); continue; }
                // Native Live3dData constructor case15 creates an ordered
                // MusicResourceOverrideData array (0x4fe33b7).
                if(type==15)
                {
                    MusicResourceOverrides.Add((ReadInteger(fields,2),ReadInteger(fields,3),ReadInteger(fields,4)));
                    continue;
                }
                // Original Live3dData type5 is an ordered array, independent of type11 IDs.
                if (type == 5) { MirrorScanTextureIds.Add(int.Parse(fields[2],CultureInfo.InvariantCulture)); continue; }
                // Master3dLive.Live3dData.ctor 0x4e8cad0, case 0x12.
                // Pattern keys and the CSV can exist while this setting is off.
                if (type == 18)
                {
                    UseLipSyncPattern = int.TryParse(fields[2],NumberStyles.Integer,
                        CultureInfo.InvariantCulture,out int enabled) && enabled > 0;
                    continue;
                }
                if (type == 20)
                {
                    VariationDefaultPattern = ReadInteger(fields,2);
                    VariationMin = ReadInteger(fields,3);
                    VariationMax = ReadInteger(fields,4);
                    UseVariationCharacterData = ReadInteger(fields,5) > 0;
                    OkeLiveVariationPattern = ReadInteger(fields,6);
                    continue;
                }
                if (type == 21)
                {
                    MiniCharacters.Add(new LiveMiniSelection.Rule{Mode=ReadInteger(fields,2),PositionId=ReadInteger(fields,3),CharaId=ReadInteger(fields,4),DressId=ReadInteger(fields,5),DressColorId=ReadInteger(fields,6)});
                    continue;
                }
                if (type == 10) { AudienceAnimationNames.Add(fields[2].Trim()); continue; }
                if (fields.Count < 3 || type != 11) continue;
                // Live3dData type11: param1=texture name, param2=texture id.
                // An empty param2 is authored as id 0 (1156 has a single
                // unnamed-id projector texture tex_env_live_cmn_projector001).
                int id = 0;
                if (fields.Count > 3 && !string.IsNullOrWhiteSpace(fields[3]))
                    int.TryParse(fields[3],NumberStyles.Integer,CultureInfo.InvariantCulture,out id);
                bool mirror = fields.Count > 4 && int.TryParse(fields[4],out int mode) && mode == 1;
                var table = mirror ? MirrorBall : Projector;
                if (!table.ContainsKey(id)) table.Add(id,fields[2].Trim());
            }
        }

        static int ReadInteger(List<string> fields,int index) => index < fields.Count &&
            int.TryParse(fields[index],NumberStyles.Integer,CultureInfo.InvariantCulture,out int value) ? value : 0;

        internal static IEnumerable<List<string>> Rows(string csv)
        {
            var row = new List<string>(); var field = new StringBuilder(); bool quoted = false;
            for (int index=0;index<=csv.Length;index++)
            {
                char c = index<csv.Length ? csv[index] : '\n';
                if (c=='"')
                {
                    if (quoted && index+1<csv.Length && csv[index+1]=='"') {field.Append('"');index++;}
                    else quoted=!quoted;
                }
                else if (!quoted && (c==',' || c=='\n' || c=='\r'))
                {
                    row.Add(field.ToString());field.Clear();
                    if (c!=',')
                    {
                        if (c=='\r' && index+1<csv.Length && csv[index+1]=='\n')index++;
                        if (row.Count>1 || row[0].Length>0)yield return row;
                        row=new List<string>();
                    }
                }
                else field.Append(c);
            }
        }

        public string ProjectorPath(int id, bool mirror)
        {
            var source = mirror ? MirrorBall : Projector;
            return source.TryGetValue(id,out string name)
                ? (mirror ? "3d/env/live/common/projectormirrorball/" : "3d/env/live/common/projector/")+name
                : null;
        }
    }
}
