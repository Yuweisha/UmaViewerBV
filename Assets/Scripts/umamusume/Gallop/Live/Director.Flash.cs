using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live
{
    public partial class Director
    {
        public LiveFlashController LiveFlashController { get; private set; }
        public LiveChampionsTextController ChampionsTextController { get; private set; }

        public LiveFlashRaceInfo FlashRaceInfo = new LiveFlashRaceInfo();
        public static bool IsLiveCall = true;
        public bool IsStarted => _isLiveSetup && !isExit;

        readonly LiveVoicePlayer flashSePlayer = new LiveVoicePlayer();
        public AudioSource FlashSeSource => flashSePlayer.Source;

        public bool TryPlayFlashSe(string cueSheet, string cueName)
        {
            if (!LiveSubtitleSettings.Enabled)
                return false;
            var sounds = UmaViewerMain.Instance.AbSounds;
            var acb = sounds.FirstOrDefault(x => x.Name.Equals("sound/s/" + cueSheet + ".acb", StringComparison.OrdinalIgnoreCase));
            var awb = sounds.FirstOrDefault(x => x.Name.Equals("sound/s/" + cueSheet + ".awb", StringComparison.OrdinalIgnoreCase));
            if (acb == null || awb == null)
            {
                Debug.LogWarning("[LiveFlashSE] Missing cue sheet " + cueSheet);
                return false;
            }

            bool played = flashSePlayer.TryPlay(acb.FilePath, awb.FilePath, cueName, 0f, 1f, out string error);
            if (!played)
                Debug.LogWarning("[LiveFlashSE] " + error);
            return played;
        }

        public bool IsLiveFlashControllerValid => LiveFlashController != null && LiveFlashController.IsInitialized;

        void InitializeLiveFlash(List<LiveCharacterLoadData> characters)
        {
            ReleaseLiveFlash();
            if (_liveSettingsResources == null)
                _liveSettingsResources = LiveSettingsResourceMap.Load(live.MusicId);
            var setting = _liveSettingsResources.RawRows.FirstOrDefault(x => x.Type == 14);
            if (setting.Fields == null || setting.Fields.Count < 3)
                return;
            int kind = int.Parse(setting.Fields[2], System.Globalization.CultureInfo.InvariantCulture);
            string path = GetLiveFlashResourcePath(kind);
            if (path == null)
                return;
            if (kind == 1)
            {
                if (ChampionsTextController != null)
                    return;
                if (FlashRaceInfo.ChampionsMeetingResourceId <= 0)
                {
                    FlashRaceInfo = UmaDatabaseController.Instance.ReadLatestChampionsLiveRaceInfo();
                    // Preview ranks follow the selected cast. Explicit caller-supplied
                    // race results keep their own rank arrays and event context.
                    var names = new List<string>();
                    if (characters != null)
                        for (int i = 0; i < 9 && i < characters.Count; i++)
                        {
                            var entry = characters[i] != null ? characters[i].CharaEntry : null;
                            names.Add(entry == null ? string.Empty : UmaDatabaseController.Instance.ReadLiveFlashCharacterName(entry.Id, entry.IsMob));
                        }

                    FlashRaceInfo.CharacterNameArray = names.ToArray();
                    FlashRaceInfo.TrainerNameArray = Enumerable.Repeat("Trainer", names.Count).ToArray();
                }

                if (FlashRaceInfo.ChampionsMeetingResourceId <= 0)
                    return;
                var championsHost = new GameObject("LiveChampionsTextController");
                championsHost.transform.SetParent(transform, false);
                ChampionsTextController = championsHost.AddComponent<LiveChampionsTextController>();
                ChampionsTextController.RaceInfo = FlashRaceInfo;
                ChampionsTextController.Initialize(path);
                _liveTimelineControl.ChampionsTextController = ChampionsTextController;

                return;
            }

            var host = new GameObject("LiveFlashController");
            host.transform.SetParent(transform, false);
            if (kind == 4)
            {
                var onsen = host.AddComponent<LiveFlashOnsenController>();
                onsen.CharacterNames = characters.Take(2).Select(x => x.CharaEntry.Name == "" ? string.Empty : UmaDatabaseController.Instance.ReadLiveFlashCharacterName(x.CharaEntry.Id, x.CharaEntry.IsMob)).ToArray();
                LiveFlashController = onsen;
            }
            else if (kind == 5)
                LiveFlashController = host.AddComponent<LiveFlashRamenController>();
            else
                LiveFlashController = kind == 2 ? (LiveFlashController)host.AddComponent<LiveFlashLegendController>() : host.AddComponent<LiveFlashController>();
            LiveFlashController.Initialize(path);
            if (LiveFlashController is LiveFlashRamenController ramen)
                ramen.Setup(LiveFlashRamenController.GetLyricsImagePaths());
            if (LiveFlashController is LiveFlashLegendController legend)
            {
                int id = characters.Count > 0 ? (characters[0].CharaEntry.IsMob ? 1 : characters[0].CharaEntry.Id) : 0;
                var first = ResolvePhysicalFacialCharacter(0);
                string name = first == null ? string.Empty : UmaDatabaseController.Instance.ReadLiveFlashCharacterName(first.CharaEntry.Id, first.CharaEntry.IsMob);
                legend.Setup(name, LiveFlashLegendController.GetCatchCopyImagePath(id, _liveSettingsResources.PermissionCharacterVoices.Contains(id)));
            }

            _liveTimelineControl.FlashController = LiveFlashController;

        }

        public static string GetLiveFlashResourcePath(int kind)
        {
            switch (kind)
            {
                case 0:
                    return null;
                case 1:
                    return "UIAnimation/FlashCombine/Action/Champions/fa_champions_live_text00";
                case 2:
                    return "UIAnimation/FlashCombine/Action/SingleMode/Legend/fa_singlemode_legend_live_catchcopy00";
                case 3:
                    return "UIAnimation/FlashCombine/Action/SingleMode/Pioneer/fa_singlemode_pioneer_notice_window00";
                case 4:
                    return "UIAnimation/Flash/SingleMode/Onsen/pf_fl_singlemode_onsen_live_yukoma00";
                case 5:
                    return "UIAnimation/FlashCombine/Action/SingleMode/Ramen/fa_singlemode_ramen_live_retsuden00";
                default:
                    throw new InvalidOperationException("Live Flash controller kind: " + kind);
            }
        }

        void ReleaseLiveFlash()
        {
            flashSePlayer.Dispose();
            if (_liveTimelineControl != null)
            {
                _liveTimelineControl.FlashController = null;
                _liveTimelineControl.ChampionsTextController = null;
            }

            if (ChampionsTextController != null)
            {
                ChampionsTextController.Destroy();
                Destroy(ChampionsTextController.gameObject);
                ChampionsTextController = null;
            }

            if (LiveFlashController == null)
                return;
            LiveFlashController.Destroy();
            Destroy(LiveFlashController.gameObject);
            LiveFlashController = null;
        }
    }
}
