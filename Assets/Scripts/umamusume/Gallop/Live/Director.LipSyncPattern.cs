using System.Collections.Generic;
using Gallop.Live.Cutt;
using UnityEngine;

namespace Gallop.Live
{
    public partial class Director
    {
        // Native enabledVariationIds is distinct from the worksheet selector.
        // Context owners may populate these before InitializeTimeline.
        public HashSet<int> EnabledLipSyncVariationIds { get; } = new HashSet<int>();
        public List<int> EnabledLiveVariationOrder { get; } = new List<int>();

        int _selectedLiveVariationSong = -1;
        public Dictionary<int, int> LipSyncContextCharacterIds { get; } = new Dictionary<int, int>();
        // Original LiveLoadSettings ctor initializes this independently to -1.
        public int LiveVariationIdOverride { get; set; } = -1;
        // Original Live3DSettings.SongPartVariationId is distinct from both
        // worksheet variation and enabled variation IDs. Copied from the caller's
        // LiveLoadSettings by Setup; it is not a serialized resource asset field.
        public int SongPartVariationId { get; set; }
        public Dictionary<int, int[]> LiveVariationDressIds { get; } = new Dictionary<int, int[]>();

        int _lipSyncSettingsMusicId = -1;
        int _lipSyncCsvMusicId = -1;
        UmaDatabaseEntry _lipSyncCsvAsset;
        Dictionary<int, int[]> _lipSyncCsvPatterns;
        internal void InitializeLiveVariationSelection(int musicId, IList<LiveCharacterLoadData> characters)
        {
            if (_liveSettingsResources == null || _lipSyncSettingsMusicId != musicId)
            {
                _liveSettingsResources = LiveSettingsResourceMap.Load(musicId);
                _lipSyncSettingsMusicId = musicId;
            }

            if (_selectedLiveVariationSong == musicId)
                return;
            var cast = new List<LiveVariationSelection.Character>();
            int count = characters != null ? characters.Count : CharaContainerScript.Count;
            for (int slot = 0; slot < count; slot++)
            {
                var model = slot < CharaContainerScript.Count ? CharaContainerScript[slot] : null;
                int id = LipSyncContextCharacterIds.TryGetValue(slot, out int originalId) ? originalId : characters != null ? characters[slot].CharaEntry.Id : model != null && model.CharaEntry != null ? model.CharaEntry.Id : 0;
                if (!LiveVariationDressIds.TryGetValue(slot, out int[] dresses))
                    dresses = new[]
                    {
                        characters != null ? characters[slot].MasterDressId : model != null ? model.LiveMasterDressId : 0
                    };
                cast.Add(new LiveVariationSelection.Character { CharaId = id, DressIds = dresses });
            }

            EnabledLipSyncVariationIds.Clear();
            EnabledLiveVariationOrder.Clear();
            foreach (int id in LiveVariationSelection.Select(LiveVariationIdOverride, _liveSettingsResources.VariationDefaultPattern, _liveSettingsResources.VariationMin, _liveSettingsResources.VariationMax, _liveSettingsResources.UseVariationCharacterData, cast, _liveSettingsResources.VariationCharacterRules, LiveRandomBoundary.RangeInt))
            {
                EnabledLipSyncVariationIds.Add(id);
                EnabledLiveVariationOrder.Add(id);
            }

            _selectedLiveVariationSong = musicId;
        }

        internal void InitializeLipSyncPatterns()
        {
            InitializeLiveVariationSelection(live.MusicId, null);
            if (_lipSyncCsvMusicId != live.MusicId)
            {
                ReleaseLipSyncPatternAsset();
                _lipSyncCsvMusicId = live.MusicId;
            }

            if (_liveSettingsResources.UseLipSyncPattern && _lipSyncCsvPatterns == null)
            {
                string path = $"live/musicscores/m{live.MusicId}/m{live.MusicId}_lipsync_pattern";
                if (UmaViewerMain.Instance.AbList.TryGetValue(path, out var entry))
                {
                    var bundle = UmaAssetManager.LoadAssetBundle(entry);
                    if (bundle != null)
                        _lipSyncCsvAsset = entry;
                    var source = bundle == null ? null : bundle.LoadAsset<TextAsset>($"m{live.MusicId}_lipsync_pattern");
                    if (source != null)
                        _lipSyncCsvPatterns = LiveLipSyncPatternCsv.Parse(source.text);
                }
            }

            _liveTimelineControl.ConfigureLipSyncPatterns(_lipSyncCsvPatterns, slot => slot >= 0 && slot < CharaContainerScript.Count && CharaContainerScript[slot] != null && CharaContainerScript[slot].CharaEntry != null ? (int? )CharaContainerScript[slot].CharaEntry.Id : null, slot => LipSyncContextCharacterIds.TryGetValue(slot, out int id) ? (int? )id : null, id => EnabledLipSyncVariationIds.Contains(id), _liveSettingsResources.UseLipSyncPattern, slot => slot >= 0 && slot < charaObjs.Count && charaObjs[slot] != null);
            _liveTimelineControl.OnUpdateLipSyncPattern -= OnUpdateLipSyncPattern;
            _liveTimelineControl.OnUpdateLipSyncPattern += OnUpdateLipSyncPattern;
        }

        void ReleaseLipSyncPatternAsset()
        {
            if (_lipSyncCsvAsset != null)
                UmaAssetManager.UnloadAssetBundle(_lipSyncCsvAsset, false);
            _lipSyncCsvAsset = null;
            _lipSyncCsvPatterns = null;
            _lipSyncCsvMusicId = -1;
        }

        void OnUpdateLipSyncPattern(int slot, LiveTimelineKeyLipSyncPatternData key, float duration)
        {
            if (slot < 0 || slot >= CharaContainerScript.Count)
                return;
            var model = CharaContainerScript[slot];
            if (model != null && model.FaceDrivenKeyTarget != null)
                model.FaceDrivenKeyTarget.SubmitLiveLipSyncPattern(key, duration);
        }
    }
}
