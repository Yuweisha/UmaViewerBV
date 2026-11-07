using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class UmaViewerAudio
{

    static UmaViewerMain Main => UmaViewerMain.Instance;
    public static int LastAudioPartIndex = -1;

    public struct UmaSoundInfo
    {
        public UmaDatabaseEntry awb;
    }

    public class CuteAudioSource
    {
        public string tag;
        public UmaDatabaseEntry entry;
        public int characterId, voiceCharacterId;
        public int[] waveIds;
        public bool enable;
        public float volume;
        public float pan;
        public int cur_active_source = - 1;
        // Requested (possibly de-duplicated) label is distinct from the CRI
        // selector retained by a playback when UpdateSong does not write it.
        public int requestedPartLabel;
        public AudioSource activeSource;
        public List<AudioSource> sourceList;

        public void SwitchActiveSource(int index, bool forceUpdate = false)
        {
            if (cur_active_source == index && !forceUpdate) return;
            cur_active_source = index;
            activeSource = null;
            for(int i = 0; i < sourceList.Count; i++)
            {
                if (i + 1 == index)
                {
                    activeSource = sourceList[i];
                    sourceList[i].volume = volume;
                    sourceList[i].panStereo = pan;
                }
                else
                {
                    sourceList[i].volume = 0;
                }
            }
        }

        public void SetVolume(float volume) 
        {
            this.volume = volume;
            if (activeSource) activeSource.volume = volume;
        }

        public void SetPanStereo(float pan)
        {
            this.pan = pan;
            if (activeSource) activeSource.panStereo = pan;
        }
    }

    static public UmaSoundInfo getSoundPath(string name)
    {
        UmaSoundInfo info;
        info.awb = Main.AbSounds.FirstOrDefault(a => a.Name.Contains(name) && a.Name.EndsWith("awb"));
        return info;
    }

    static public CuteAudioSource ApplySound(string cueName, int part)
    {
        UmaSoundInfo soundInfo = getSoundPath(cueName);
        return ApplySound(soundInfo.awb, part);
    }

    static public CuteAudioSource ApplySound(UmaDatabaseEntry entry, int part)
    {
        if (entry == null) throw new ArgumentNullException(nameof(entry));

        GameObject sourceRoot = new GameObject("CuteAudioSource");
        sourceRoot.transform.SetParent(GameObject.Find("AudioManager/AudioControllerBgm").transform);

        List<AudioSource> sourceList = new List<AudioSource>();

        List<AudioClip> sounds = UmaViewerBuilder.LoadAudio(entry);
        if (sounds.Count == 0) throw new InvalidOperationException("No decoded audio streams: " + entry.Name);
        // Accompaniment is a single cue, but its authored gain is not always
        // one. Resolve the paired ACB rather than treating AWB PCM as the mix.
        float cueVolume = 1f;
        if (part < 0)
        {
            string acbName = System.IO.Path.ChangeExtension(entry.Name, ".acb").Replace('\\', '/');
            var acb = Main.AbSounds.FirstOrDefault(x => string.Equals(x.Name, acbName, StringComparison.OrdinalIgnoreCase));
            if (acb != null && System.IO.File.Exists(acb.FilePath))
                using (var stream = System.IO.File.OpenRead(acb.FilePath))
                    if (!Gallop.Live.LiveVoiceCueResolver.TryResolveCueVolume(stream, 0, out cueVolume, out string error))
                        throw new InvalidOperationException("Accompaniment cue volume: " + error);
        }
        foreach(var clip in sounds)
        {
            AudioSource source = sourceRoot.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.clip = clip;
            source.volume = part >= 0 ? 0f : cueVolume;
            sourceList.Add(source);
        }

        Debug.Log(sourceList);

        CuteAudioSource cute = new CuteAudioSource
        {
            tag = ((PartForm)part).ToString(),
            entry = entry,
            waveIds = sounds.Select(x => int.Parse(x.name.Substring(x.name.LastIndexOf('_') + 1))).ToArray(),
            enable = false,
            volume = cueVolume,
            pan = 0,
            sourceList = sourceList
        };

        return cute;
    }

    public static void Play(CuteAudioSource sourceList)
    {
        foreach(var source in sourceList.sourceList)
        {
            source.Play();
        }
    }

    public static void Stop(CuteAudioSource sourceList)
    {
        foreach (var source in sourceList.sourceList)
        {
            source.Stop();
        }
    }

    public static void SetTime(CuteAudioSource sourceList, float time)
    {
        foreach (var source in sourceList.sourceList)
        {
            source.time = time;
        }
    }

    public enum PartForm
    {
        center = 0,
        left = 1,
        right = 2,
        left2 = 3,
        right2 = 4,
        left3 = 5,
        right3 = 6,
    }

    // Original .cctor 0x1b8ea00, GetVolume 0x1b8d670 and GetPartData 0x1b8d290.
    static readonly float[] DefaultVolumes = { 0f, .79f, .89f, 1f, 1.12f, 1.26f };
    static readonly float[] DefaultVolumeRates = { 0f, .79f, .79f, .56f, .53f, .47f, .42f, .37f };
    static readonly int[][] DefaultVolumeIndices = {
        new[] { 0 }, new[] { 5 }, new[] { 2, 2 }, new[] { 3, 5, 3 },
        new[] { 3, 3, 3, 3 }, new[] { 1, 2, 4, 2, 1 },
        new[] { 3, 3, 3, 3, 3, 3 }, new[] { 1, 1, 2, 4, 2, 1, 1 }
    };
    static readonly float[][] DefaultPans = {
        new[] { 0f }, new[] { 0f }, new[] { -.15f, .15f }, new[] { -.3f, 0f, .3f },
        new[] { -.3f, -.1f, .1f, .3f }, new[] { -.3f, -.15f, 0f, .15f, .3f },
        new[] { -.3f, -.2f, -.1f, .1f, .2f, .3f }, new[] { -.3f, -.2f, -.1f, 0f, .1f, .2f, .3f }
    };
    static readonly string[] OrderedParts = { "left3", "left2", "left", "center", "right", "right2", "right3" };

    static public void AlterUpdate(float _liveCurrentTime, PartEntry partInfo, List<CuteAudioSource> liveVocal, bool forceUpdate = false)
    {
        if (partInfo == null || liveVocal == null ||
            !partInfo.PartSettings.TryGetValue("time", out var times)) return;
        int targetIndex = times.Count - 1;
        while (targetIndex >= 0 && _liveCurrentTime < times[targetIndex] / 1000.0) targetIndex--;
        if (targetIndex < 0 || (!forceUpdate && LastAudioPartIndex == targetIndex)) return;
        LastAudioPartIndex = targetIndex;

        int activeCount = 0;
        // SongPartData.UpdateSongPartCharacter (0x390ee50): missing banks
        // and repeated (cue sheet, part label) pairs do not double the choir.
        var labels = NativePartLabels(partInfo, liveVocal, targetIndex);
        foreach (string part in OrderedParts)
        {
            var vocal = FindVocal(liveVocal, part);
            if (vocal != null && labels[part] > 0) activeCount++;
        }
        // Original AlterUpdate 0x1b8c31d and 0x1b8c562: >=900 means default,
        // not zero. An explicit zero remains authored silence.
        float rate = PartValue(partInfo, "volume_rate", targetIndex, 901f);
        if (rate >= 900f) rate = activeCount < DefaultVolumeRates.Length ? DefaultVolumeRates[activeCount] : 1f;
        int rank = 0;
        foreach (string part in OrderedParts)
        {
            var vocal = FindVocal(liveVocal, part);
            if (vocal == null) continue;
            int index = labels[part];
            vocal.requestedPartLabel = index;
            // Native UpdateSong 0x4a1f386 only writes selector labels >=1.
            // A zero label leaves the playback's previous selector intact.
            // UpdatePartParamer then applies explicit volume/pan even when
            // OrderArray is zero. Do not translate this into stopping a stem.
            // Resolve history also for direct seeks/export, independently of
            // whichever future selector happened to be sampled previously.
            int selector = index;
            for (int rowIndex = targetIndex - 1; selector <= 0 && rowIndex >= 0; rowIndex--)
                selector = NativePartLabels(partInfo, liveVocal, rowIndex)[part];
            // Viewer playback starts on stem 1. The retained-selector case
            // below is native-verified; the global ACF default is not yet decoded.
            if (selector <= 0) selector = 1;
            vocal.SwitchActiveSource(selector, forceUpdate);
            bool active = index > 0;
            int row = activeCount < DefaultVolumeIndices.Length ? activeCount : 0;
            int column = active && row > 0 ? rank++ : 0;
            if (!active) row = 0;
            float volume = PartValue(partInfo, part + "_vol", targetIndex, 901f);
            float pan = PartValue(partInfo, part + "_pan", targetIndex, 901f);
            if (volume >= 900f) volume = DefaultVolumes[DefaultVolumeIndices[row][column]];
            if (pan >= 900f) pan = DefaultPans[row][column];
            vocal.SetVolume(volume * rate);
            vocal.SetPanStereo(pan);
        }
    }

    static Dictionary<string, int> NativePartLabels(PartEntry data, List<CuteAudioSource> vocals, int row)
    {
        var result = new Dictionary<string, int>();
        var chosen = new HashSet<string>(StringComparer.Ordinal);
        // SongPart uses physical slot order, distinct from left-to-right
        // ranking. Every bank, including repeated characters, follows this.
        foreach (var vocal in vocals.OrderBy(x => (int)Enum.Parse(typeof(PartForm), x.tag)))
        {
            int label = (int)PartValue(data, vocal.tag, row, 0f);
            string identity = (vocal.entry != null ? vocal.entry.Name : vocal.tag) + ":" + label;
            result[vocal.tag] = label > 0 && chosen.Add(identity) ? label : 0;
        }
        return result;
    }

    static CuteAudioSource FindVocal(List<CuteAudioSource> vocals, string part)
    {
        for (int i = 0; i < vocals.Count; i++)
            if (vocals[i].tag == part) return vocals[i];
        return null;
    }

    static float PartValue(PartEntry data, string column, int row, float fallback)
    {
        return data.PartSettings.TryGetValue(column, out var values) && row < values.Count
            ? values[row] : fallback;
    }
}
