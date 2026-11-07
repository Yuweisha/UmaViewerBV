using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Gallop.Live
{
    // Game voice selection: GallopUtil.GetPlaySongRandomCharaId 0x4c36520.
    // The shuffled list is an explicit input; Unity/CRI random seed is not recovered.
    public sealed class LiveSongAudioPlan
    {
        public sealed class Binding
        {
            public int Slot, CharacterId, VoiceCharacterId;
            public string Reason;
            public UmaDatabaseEntry Entry;
        }
        public int SongId, ShuffleSeed;
        public int[] ShuffledEligibleCharacters;
        public readonly List<Binding> Voices = new List<Binding>();
        public UmaDatabaseEntry Accompaniment;
        public int AccompanimentVariationId, AccompanimentLiveVariationId;
#if UNITY_EDITOR
        // Explicit offline visual fixture input, supplied by the isolated batch
        // capture. Normal runtime cue selection remains below.
        public static Func<int, UmaDatabaseEntry> CaptureAccompanimentOverride;
#endif

        public static int SelectVoice(int character, int preferred, IList<int> shuffled,
            ICollection<int> excluded, Func<int, bool> available)
        {
            if (available(character)) return character;
            if (available(preferred)) return preferred;
            foreach (int candidate in shuffled)
                if ((excluded == null || !excluded.Contains(candidate)) && available(candidate)) return candidate;
            return 0;
        }

        // Native GetPlaySongRandomCharaId 0x4d79060 tests MasterLiveData.LiveData.IsExistSongData
        // (live_permission_data for SongCharaType 2). The installed player additionally has to
        // check the local AWB because the packaged permission row can exist without a playable
        // voice file; the original client then substitutes another permitted voice. Treat a
        // voice whose AWB is absent on disk as not selectable instead of aborting Live setup.
        private static Dictionary<int, UmaDatabaseEntry> BuildVoiceEntries(UmaViewerMain main, int song)
        {
            var entries = new Dictionary<int, UmaDatabaseEntry>();
            string prefix = $"sound/l/{song}/snd_bgm_live_{song}_chara_";
            const string suffix = "_01.awb";
            foreach (var entry in main.AbSounds)
            {
                if (entry == null || string.IsNullOrEmpty(entry.Name)) continue;
                if (!entry.Name.StartsWith(prefix, StringComparison.Ordinal)) continue;
                if (!entry.Name.EndsWith(suffix, StringComparison.Ordinal)) continue;
                int length = entry.Name.Length - prefix.Length - suffix.Length;
                if (length <= 0) continue;
                if (int.TryParse(entry.Name.Substring(prefix.Length, length), out int chara) &&
                    !entries.ContainsKey(chara))
                    entries.Add(chara, entry);
            }
            return entries;
        }

        // RegisterSongCueSheet4e35690 -> GetSongOke[Variation]CueSheetPath.
        // The last suffix selects a whole-song variation bank, not a time segment.
        public static string AccompanimentPath(int song,int requestedOke,bool isLiveCall,int pattern,IList<int> variations)
        {
            int oke=requestedOke>=1?requestedOke:1;
            bool useVariation=pattern==3 || pattern==1&&isLiveCall || pattern==2&&!isLiveCall;
            int variation=useVariation&&variations!=null&&variations.Count>0?variations[0]:0;
            return $"sound/l/{song:D4}/snd_bgm_live_{song:D4}_oke_{oke:D2}"+(variation>0?$"_{variation:D2}":string.Empty);
        }

        public static LiveSongAudioPlan Create(int song, int singerCount, IList<LiveCharacterLoadData> characters,
            int requestedOke=0,bool isLiveCall=true,int okePattern=0,IList<int> variations=null)
        {
            var main = UmaViewerMain.Instance;
            var eligible = new HashSet<int>(UmaDatabaseController.Instance.ReadSongEligibleCharacters(song));
            var order = eligible.OrderBy(x => x).ToArray();
            int seed = int.TryParse(Environment.GetEnvironmentVariable("UMA_LIVE_AUDIO_SEED"), out int supplied)
                ? supplied : Environment.TickCount;
            var random = new System.Random(seed);
            for (int i = order.Length - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                int temporary = order[i]; order[i] = order[j]; order[j] = temporary;
            }
            var plan = new LiveSongAudioPlan { SongId = song, ShuffleSeed = seed, ShuffledEligibleCharacters = order };
            var voiceEntries = BuildVoiceEntries(main, song);
            // Prefer the installed client's download path before substituting: only a voice
            // that cannot be found in the index or fetched may be replaced. Keep a bounded
            // budget so an offline client cannot stall Live setup by probing every candidate.
            bool allowDownload = Config.Instance.DownloadMissingResources || Config.Instance.WorkMode == WorkMode.Standalone;
            int downloadBudget = allowDownload ? 7 : 0;
            var playableCache = new Dictionary<int, bool>();
            int downloaded = 0;
            Func<int, bool> isPlayable = chara =>
            {
                if (!eligible.Contains(chara)) return false;
                if (playableCache.TryGetValue(chara, out bool known)) return known;
                bool ok = false;
                if (voiceEntries.TryGetValue(chara, out var entry))
                {
                    ok = System.IO.File.Exists(entry.Path);
                    if (!ok && downloadBudget > 0)
                    {
                        downloadBudget--;
                        Debug.LogWarning($"[LiveAudio] song {song}: voice {chara} AWB absent; downloading {entry.Name}");
                        try { UmaViewerDownload.DownloadAssetSync(entry); }
                        catch (Exception error) { Debug.LogWarning($"[LiveAudio] song {song}: voice {chara} download failed: {error.Message}"); }
                        ok = System.IO.File.Exists(entry.Path);
                        if (ok) downloaded++;
                    }
                }
                playableCache[chara] = ok;
                return ok;
            };
            int count = System.Math.Min(System.Math.Min(singerCount, 7), characters.Count);
            var excluded = new HashSet<int>(characters.Take(count).Select(x => x.CharaEntry.Id));
            for (int i = 0; i < count; i++)
            {
                int character = characters[i].CharaEntry.Id;
                bool characterPermitted = eligible.Contains(character);
                int voice = SelectVoice(character, 0, order, excluded, isPlayable);
                if (voice > 0) excluded.Add(voice);
                var entry = voice > 0 && voiceEntries.TryGetValue(voice, out var voiceEntry) ? voiceEntry : null;
                if (voice > 0 && (entry == null || !System.IO.File.Exists(entry.Path)))
                {
                    // Defensive: the availability predicate already filters this; keep the
                    // native no-voice outcome instead of aborting the whole Live setup.
                    voice = 0;
                    entry = null;
                }
                string reason = voice == character ? "character-permitted-by-master"
                    : voice > 0 ? (characterPermitted ? "file-missing-substitute"
                        : "game-priority-first-eligible-shuffled-nonexcluded")
                    : "no-permitted-voice";
                plan.Voices.Add(new Binding { Slot = i, CharacterId = character, VoiceCharacterId = voice,
                    Reason = reason, Entry = entry });
            }
            string accompanimentPath=AccompanimentPath(song,requestedOke,isLiveCall,okePattern,variations);
            plan.AccompanimentVariationId=requestedOke>=1?requestedOke:1;
            plan.AccompanimentLiveVariationId=(okePattern==3||okePattern==1&&isLiveCall||okePattern==2&&!isLiveCall)&&variations!=null&&variations.Count>0?variations[0]:0;
            plan.Accompaniment =
#if UNITY_EDITOR
                CaptureAccompanimentOverride?.Invoke(song) ??
#endif
                main.AbSounds.SingleOrDefault(x => x.Name == accompanimentPath+".awb")
                ?? main.AbSounds.SingleOrDefault(x => x.Name == accompanimentPath+".acb")
                ?? throw new InvalidOperationException("Missing accompaniment for song " + song);
            int substitutions = plan.Voices.Count(v => v.Reason == "file-missing-substitute");
            if (substitutions > 0)
                Debug.LogWarning($"[LiveAudio] song {song}: {substitutions} permitted voice(s) have no local AWB; substituted with playable eligible voices (seed {seed}).");
            if (downloaded > 0)
                Debug.Log($"[LiveAudio] song {song}: downloaded {downloaded} missing voice AWB(s).");
            return plan;
        }

        public IEnumerable<UmaDatabaseEntry> Entries
        {
            get
            {
                foreach (var voice in Voices) if (voice.Entry != null) yield return voice.Entry;
                yield return Accompaniment;
                // The paired ACB carries the cue gain, including explicit mute.
                // Route missing metadata through the normal resource downloader.
                string acbName = System.IO.Path.ChangeExtension(Accompaniment.Name, ".acb").Replace('\\', '/');
                var acb = UmaViewerMain.Instance.AbSounds.FirstOrDefault(x =>
                    string.Equals(x.Name, acbName, StringComparison.OrdinalIgnoreCase));
                if (acb != null && acb != Accompaniment) yield return acb;
            }
        }
    }

    public static class LiveSongAudioControl
    {
        public static IEnumerable<UmaViewerAudio.CuteAudioSource> Banks(Director director)
            => new[] { director.liveMusic }.Concat(director.liveVocal).Where(x => x != null);

        public static void Start(Director director, float time, double scheduledLeadSeconds = 0.1)
        {
            // All independent streams share one DSP start, including currently
            // silent selector streams, so later part changes never restart audio.
            UmaViewerAudio.LastAudioPartIndex = -1;
            UmaViewerAudio.AlterUpdate(time, director.partInfo, director.liveVocal, true);
            double dspTime=AudioSettings.dspTime,realTime=Time.realtimeSinceStartupAsDouble;
            double start = dspTime + scheduledLeadSeconds;
            foreach (var bank in Banks(director))
                foreach (var source in bank.sourceList)
                {
                    source.Stop(); source.loop = false;
                    source.timeSamples = System.Math.Min(System.Math.Max(0, (int)System.Math.Round(time * source.clip.frequency)), source.clip.samples - 1);
                    source.PlayScheduled(start);
                }
            var music=director.liveMusic.sourceList[0];
            AudioSettings.GetDSPBufferSize(out int bufferLength,out int bufferCount);
            director.SongPlaybackClock.Start(start,(double)music.timeSamples/music.clip.frequency,
                (double)music.clip.samples/music.clip.frequency,dspTime,realTime,
                (double)bufferLength*bufferCount/AudioSettings.outputSampleRate);
        }

        public static bool Pause(Director director, bool paused)
        {
            bool found = false;
            foreach (var bank in Banks(director))
                foreach (var source in bank.sourceList)
                {
                    if (paused) source.Pause(); else source.UnPause();
                    found = true;
                }
            if(found)
            {
                if(paused)director.SongPlaybackClock.Pause(AudioSettings.dspTime,Time.realtimeSinceStartupAsDouble);
                else director.SongPlaybackClock.Resume(AudioSettings.dspTime,Time.realtimeSinceStartupAsDouble);
            }
            return found;
        }
    }
}
