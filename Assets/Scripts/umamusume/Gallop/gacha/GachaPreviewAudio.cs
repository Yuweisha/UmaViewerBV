using System;
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using CriWareFormats;
using NAudio.Wave;
using Gallop.Live;
using UmaMusumeAudio;
using UnityEngine;
using static Gallop.CutIn.Cutt.CutInTimelineWorkSheet;

internal sealed class GachaPreviewAudio : IDisposable
{
    private const string CommonEffects = "snd_sfx_gacha_000";
    private readonly Transform root;
    private readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Channel> effects = new Dictionary<string, Channel>();
    private readonly Channel music;
    private readonly Channel ambience;
    private readonly Channel voice;
    private readonly Channel cardEffect;
    private readonly Channel cardJingle;
    private readonly GachaJingleDucking jingleDucking = new GachaJingleDucking();
    private readonly Dictionary<string, GachaJingleDucking.Window[]> jingleControls =
        new Dictionary<string, GachaJingleDucking.Window[]>(StringComparer.OrdinalIgnoreCase);
    private Dictionary<uint, GachaJingleDucking.Rule> jingleRules;
    private bool muted;
    private bool paused;
    private readonly bool ownsClips;

    public bool IsVoicePlaying => voice.IsPlaying;

    internal sealed class PlaybackState
    {
        internal Channel[] Channels;
        internal Dictionary<string, Channel> Effects;
        internal GachaJingleDucking Ducking;
    }

    internal PlaybackState CapturePlayback()
    {
        var state = new PlaybackState
        {
            Channels = new[] { music.Capture(), ambience.Capture(), voice.Capture(),
                cardEffect.Capture(), cardJingle.Capture() },
            Effects = new Dictionary<string, Channel>(),
            Ducking = jingleDucking.Capture()
        };
        foreach (var effect in effects)
            state.Effects.Add(effect.Key, effect.Value.Capture());
        return state;
    }

    internal void RestorePlayback(PlaybackState state)
    {
        var channels = new[] { music, ambience, voice, cardEffect, cardJingle };
        for (int index = 0; index < channels.Length; index++)
            channels[index].Restore(state.Channels[index]);
        foreach (var effect in effects)
            if (!state.Effects.ContainsKey(effect.Key))
                effect.Value.Stop();
        foreach (var effect in state.Effects)
            GetEffect(effect.Key).Restore(effect.Value);
        jingleDucking.Restore(state.Ducking);
    }

    internal void SetMuted(bool value)
    {
        muted = value;
        foreach (var source in root.GetComponentsInChildren<AudioSource>(true))
            source.mute = value;
    }

    internal void SetPaused(bool value)
    {
        paused = value;
        music.SetPaused(value);
        ambience.SetPaused(value);
        voice.SetPaused(value);
        cardEffect.SetPaused(value);
        cardJingle.SetPaused(value);
        foreach (var effect in effects.Values)
            effect.SetPaused(value);
    }

    public GachaPreviewAudio(Transform root, Dictionary<string, AudioClip> sharedClips = null)
    {
        this.root = root;
        ownsClips = sharedClips == null;
        if (sharedClips != null)
            clips = sharedClips;
        music = new Channel(CreateSource("Gacha BGM"), CreateSource("Gacha BGM intro"));
        ambience = CreateChannel("Gacha ambience");
        voice = CreateChannel("Gacha voice");
        cardEffect = CreateChannel("Gacha card effect");
        cardJingle = CreateChannel("Gacha card jingle");
    }

    public void StartMusic()
    {
        PlayCue(music, "snd_bgm_gm025", "snd_bgm_GM025_gacha_01", "b", true, 1f);
    }

    public IEnumerator PrepareCharacterBanks(IEnumerable<int> cards, Action<int, int, string> progress)
    {
        if (!Config.Instance.DownloadMissingResources && Config.Instance.WorkMode != WorkMode.Standalone)
            yield break;

        yield return UmaViewerDownload.DownloadAssets(CollectCharacterBanks(cards), progress);
    }

    public List<UmaDatabaseEntry> CollectCharacterBanks(IEnumerable<int> cards)
    {
        var banks = new HashSet<UmaDatabaseEntry>();
        AddBanks(banks, "b", "snd_bgm_gm025");
        foreach (int card in cards)
        {
            var result = UmaDatabaseController.Instance.ReadGachaCard(card);
            if (result == null || Convert.ToInt32(result["default_rarity"]) != 3)
                continue;

            AddBanks(banks, "v", VoiceSheet(card));
            AddBanks(banks, "s", CardEffectSheet(card));
            var jingle = UmaDatabaseController.Instance.ReadGachaCardJingle(card);
            if (jingle != null)
                AddBanks(banks, "b", Convert.ToString(jingle["long_cutin_bgm_cuesheet_name"]));
        }

        return new List<UmaDatabaseEntry>(banks);
    }

    private static void AddBanks(HashSet<UmaDatabaseEntry> banks, string folder, string sheet)
    {
        if (string.IsNullOrEmpty(sheet))
            return;
        var index = UmaViewerMain.Instance.AbList;
        string path = BankPath(folder, sheet);
        if (index.TryGetValue(path + ".acb", out var acb))
            banks.Add(acb);
        if (index.TryGetValue(path + ".awb", out var awb))
            banks.Add(awb);
    }

    private static string VoiceSheet(int card) => $"snd_voi_gacha_{card:D6}";
    private static string CardEffectSheet(int card) => $"snd_sfx_cti_crd{card:D6}_001_lng";
    private static string BankPath(string folder, string sheet) => $"sound/{folder}/{sheet.ToLowerInvariant()}";

    public void PlayCardEffect(int card)
    {
        StopCardEffect();
        var sheet = CardEffectSheet(card);
        cardEffect.Play(Resolve(sheet, sheet, "s"), false, 1f);
        var jingle = UmaDatabaseController.Instance.ReadGachaCardJingle(card);
        if (jingle != null)
        {
            string jingleSheet = Convert.ToString(jingle["long_cutin_bgm_cuesheet_name"]);
            string cue = Convert.ToString(jingle["long_cutin_bgm_cue_name"]);
            var clip = Resolve(jingleSheet, cue, "b");
            var controls = ResolveJingleControls(jingleSheet, cue);
            if (clip)
            {
                cardJingle.Play(clip, false, 1f);
                jingleDucking.Play(controls);
                music.DuckVolume = jingleDucking.Volume;
            }
        }
    }

    private GachaJingleDucking.Window[] ResolveJingleControls(string sheet, string cue)
    {
        string key = $"{sheet}/{cue}";
        if (jingleControls.TryGetValue(key, out var controls))
            return controls;
        try
        {
            if (jingleRules == null)
            {
                using var acf = File.OpenRead(Path.Combine(Application.streamingAssetsPath, "sound_proj.acf"));
                jingleRules = GachaJingleDucking.ReadRules(acf);
            }
            var entry = UmaViewerMain.Instance.AbList[BankPath("b", sheet) + ".acb"];
            using var stream = OpenBank(entry);
            controls = GachaJingleDucking.Read(stream, cue, jingleRules);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Gacha jingle BGM control {key}: {exception.Message}");
            controls = Array.Empty<GachaJingleDucking.Window>();
        }
        jingleControls.Add(key, controls);
        return controls;
    }

    public void StopCardEffect()
    {
        cardEffect.Stop();
        cardJingle.Stop();
        jingleDucking.Stop();
        music.DuckVolume = jingleDucking.Volume;
    }

    public void PlayVoice(int card)
    {
        var sheet = VoiceSheet(card);
        voice.Play(Resolve(sheet, sheet + "_0000", "v"), false, 1f);
    }

    public void StopVoice() => voice.Stop();

    public void PlayEffect(string cue)
    {
        var channel = GetEffect(cue);
        channel.Play(Resolve(CommonEffects, cue, "s"), false, 1f);
    }

    public void PlayCharacterNameEffect(int rarity)
    {
        var cue = $"snd_sfx_gacha_{3 + rarity:D3}";
        var key = $"{CommonEffects}/{cue}";
        if (!clips.TryGetValue(key, out var clip))
        {
            try
            {
                var entry = UmaViewerMain.Instance.AbList[BankPath("s", CommonEffects) + ".acb"];
                using var stream = OpenBank(entry);
                clip = DecodeCharacterNameEffect(stream, cue);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Gacha name effect {key}: {exception.Message}");
                clip = Decode(BankPath("s", CommonEffects), cue);
            }
            clips.Add(key, clip);
        }
        GetEffect(cue).Play(clip, false, 1f);
    }

    private static AudioClip DecodeCharacterNameEffect(Stream stream, string cue)
    {
        const ushort WaitMilliseconds = 2001, NoteOn = 2000;
        var header = new UtfTable(stream, 0);
        UtfTable Table(string name)
        {
            if (!header.Query(0, name, out VLData data) || data.Size == 0)
                throw new InvalidDataException($"Missing {name} in {cue}.");
            return new UtfTable(stream, data.Offset);
        }
        T Field<T>(UtfTable table, int row, string name)
        {
            if (!table.Query(row, name, out T value))
                throw new InvalidDataException($"Missing {name} at row {row} in {cue}.");
            return value;
        }
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
        byte[] ReadData(VLData data)
        {
            stream.Position = data.Offset;
            return reader.ReadBytes(checked((int)data.Size));
        }

        var names = Table("CueNameTable");
        int cueRow = -1;
        for (int row = 0; names.Query(row, "CueName", out string name); row++)
            if (name == cue)
            {
                cueRow = Field<ushort>(names, row, "CueIndex");
                break;
            }
        var cues = Table("CueTable");
        if (cueRow < 0 || Field<byte>(cues, cueRow, "ReferenceType") != 3)
            throw new InvalidDataException($"Expected a sequence for {cue}.");

        var sequences = Table("SequenceTable");
        var tracks = Table("TrackTable");
        var events = Table("TrackEventTable");
        var synths = Table("SynthTable");
        var waves = Table("WaveformTable");
        int sequence = Field<ushort>(cues, cueRow, "ReferenceIndex");
        if (Field<byte>(sequences, sequence, "Type") != 0)
            throw new InvalidDataException($"Expected parallel tracks for {cue}.");
        var indices = ReadData(Field<VLData>(sequences, sequence, "TrackIndex"));
        var notes = new List<(int Delay, int WaveId)>();
        for (int index = 0; index < indices.Length; index += 2)
        {
            int track = BinaryPrimitives.ReadUInt16BigEndian(indices.AsSpan(index));
            int eventIndex = Field<ushort>(tracks, track, "EventIndex");
            var commands = ReadData(Field<VLData>(events, eventIndex, "Command"));
            int delay = 0;
            for (int offset = 0; offset + 3 <= commands.Length;)
            {
                int opcode = BinaryPrimitives.ReadUInt16BigEndian(commands.AsSpan(offset));
                int size = commands[offset + 2];
                var payload = commands.AsSpan(offset + 3, size);
                offset += 3 + size;
                if (opcode == WaitMilliseconds && size == 4)
                    delay = checked(delay + BinaryPrimitives.ReadInt32BigEndian(payload));
                if (opcode != NoteOn || size != 4 || BinaryPrimitives.ReadUInt16BigEndian(payload) != 2)
                    continue;
                int synth = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(2));
                var reference = ReadData(Field<VLData>(synths, synth, "ReferenceItems"));
                if (reference.Length != 4 || BinaryPrimitives.ReadUInt16BigEndian(reference) != 1)
                    throw new InvalidDataException($"Expected a single waveform for {cue} track {track}.");
                int wave = BinaryPrimitives.ReadUInt16BigEndian(reference.AsSpan(2));
                notes.Add((delay, Field<ushort>(waves, wave, "MemoryAwbId")));
            }
        }
        if (notes.Count == 0)
            throw new InvalidDataException($"No waveform notes in {cue}.");

        using var acb = new AcbReader(stream);
        using var bank = acb.GetAwb();
        var decoded = new Dictionary<int, float[]>();
        int rate = 0, channels = 0, total = 0;
        foreach (var note in notes)
        {
            if (!decoded.TryGetValue(note.WaveId, out var samples))
            {
                samples = ReadWave(bank, note.WaveId, out int waveRate, out int waveChannels, out _);
                if (rate != 0 && (waveRate != rate || waveChannels != channels))
                    throw new InvalidDataException($"Incompatible waveform formats in {cue}.");
                rate = waveRate;
                channels = waveChannels;
                decoded.Add(note.WaveId, samples);
            }
            total = Math.Max(total, checked(MillisecondsToSamples(note.Delay) + samples.Length));
        }
        int MillisecondsToSamples(int delay) => checked((int)((long)delay * rate / 1000) * channels);
        var mixed = new float[total];
        foreach (var note in notes)
        {
            var samples = decoded[note.WaveId];
            int start = MillisecondsToSamples(note.Delay);
            for (int index = 0; index < samples.Length; index++)
                mixed[start + index] += samples[index];
        }
        return CreateClip(cue, mixed, rate, channels);
    }

    public void Apply(SoundKey key, bool seeking)
    {
        Apply(key._bgmData, music, "b", "", seeking);
        Apply(key._envData, ambience, "s", CommonEffects, seeking);

        var se = key._seData;
        if (se?._enable == true && !string.IsNullOrEmpty(se._id) && (!seeking || !se._play || se._loop))
            Apply(se, GetEffect(se._id), "s", CommonEffects, seeking);
    }

    public void Tick(float deltaTime)
    {
        jingleDucking.Tick(deltaTime);
        music.DuckVolume = jingleDucking.Volume;
        music.Tick(deltaTime);
        ambience.Tick(deltaTime);
        voice.Tick(deltaTime);
        cardEffect.Tick(deltaTime);
        cardJingle.Tick(deltaTime);
        foreach (var effect in effects.Values)
            effect.Tick(deltaTime);
    }

    internal void Synchronize()
    {
        music.Synchronize();
        ambience.Synchronize();
        voice.Synchronize();
        cardEffect.Synchronize();
        cardJingle.Synchronize();
        foreach (var effect in effects.Values)
            effect.Synchronize();
    }

    private Channel GetEffect(string cue)
    {
        if (!effects.TryGetValue(cue, out var channel))
        {
            channel = CreateChannel(cue);
            effects.Add(cue, channel);
        }
        return channel;
    }

    private void Apply(Sound key, Channel channel, string folder, string fallbackSheet, bool seeking)
    {
        if (key?._enable != true)
            return;

        if (key._play && (!seeking || channel == ambience || channel == music || key._loop))
        {
            var sheet = key._sheetName;
            if (string.IsNullOrEmpty(sheet) || sheet == "gacha_000")
                sheet = fallbackSheet;
            float volume = key._isVolume ? key._volume : 1f;
            PlayCue(channel, sheet, key._id, folder, key._loop,
                key._fadeDurationMilliseconds > 0 && key._isVolume ? 0f : volume);
            channel.Fade(volume, key._isVolume ? key._fadeDurationMilliseconds : 0, false);
        }
        else if (key._isVolume)
            channel.Fade(key._volume, key._fadeDurationMilliseconds, false);
        else if (!key._play && !key._keepBgm)
            channel.Fade(0f, key._stopFadeDurationMilliseconds, true);
    }

    private void PlayCue(Channel channel, string sheet, string cue, string folder, bool loop, float volume)
    {
        if (channel != music || !loop || string.IsNullOrEmpty(sheet) || string.IsNullOrEmpty(cue))
        {
            channel.Play(Resolve(sheet, cue, folder), loop, volume);
            return;
        }
        string key = $"{sheet}/{cue}/loop";
        if (!clips.TryGetValue(key, out var body))
        {
            try
            {
                body = Decode(BankPath(folder, sheet), cue, intro => clips[key + "/intro"] = intro);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Gacha BGM {cue}: {exception.Message}");
            }
            clips[key] = body;
        }
        clips.TryGetValue(key + "/intro", out var introClip);
        music.Play(body, true, volume, introClip);
    }

    private AudioClip Resolve(string sheet, string cue, string folder)
    {
        if (string.IsNullOrEmpty(sheet) || string.IsNullOrEmpty(cue))
            return null;

        string key = $"{sheet}/{cue}";
        if (clips.TryGetValue(key, out var clip))
            return clip;

        try
        {
            clip = Decode(BankPath(folder, sheet), cue);
            if (!clip)
                Debug.LogWarning($"Gacha audio cue unavailable: {key}");
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Gacha audio cue {key}: {exception.Message}");
        }
        clips.Add(key, clip);
        return clip;
    }

    private Stream OpenBank(UmaDatabaseEntry entry) => File.OpenRead(entry.FilePath);

    private AudioClip Decode(string path, string cue, Action<AudioClip> saveIntro = null)
    {
        var index = UmaViewerMain.Instance.AbList;
        if (!index.TryGetValue(path + ".acb", out var acbEntry))
        {
            if (!index.TryGetValue(path + ".awb", out var awbEntry))
                return null;
            using (var bank = new AwbReader(OpenBank(awbEntry)))
                return bank.Waves.Count == 1 ? DecodeWave(bank, bank.Waves[0].WaveId, cue, saveIntro) : null;
        }

        using (var stream = OpenBank(acbEntry))
        {
            if (LiveVoiceCueResolver.TryResolveCueId(stream, cue, out int cueId, out _))
            {
                stream.Position = 0;
                if (LiveVoiceCueResolver.TryResolveWaveId(stream, cueId, out int waveId, out bool streaming, out _))
                {
                    if (streaming)
                    {
                        if (!index.TryGetValue(path + ".awb", out var external))
                            return null;
                        using (var bank = new AwbReader(OpenBank(external)))
                            return DecodeWave(bank, waveId, cue, saveIntro);
                    }

                    stream.Position = 0;
                    using (var acb = new AcbReader(stream))
                    using (var bank = acb.GetAwb())
                        return DecodeWave(bank, waveId, cue, saveIntro);
                }
            }

            stream.Position = 0;
            using (var acb = new AcbReader(stream))
            {
                if (index.TryGetValue(path + ".awb", out var external))
                {
                    using (var bank = new AwbReader(OpenBank(external)))
                    {
                        var clip = FindWave(acb, bank, cue, false, saveIntro);
                        if (clip)
                            return clip;
                    }
                }

                if (!acb.HasEmbeddedAwb)
                    return null;
                using (var bank = acb.GetAwb())
                    return FindWave(acb, bank, cue, true, saveIntro);
            }
        }
    }

    private static AudioClip FindWave(AcbReader acb, AwbReader bank, string cue, bool embedded, Action<AudioClip> saveIntro)
    {
        foreach (var wave in bank.Waves)
        {
            string names = acb.GetWaveName(wave.WaveId, -1, embedded);
            foreach (var name in names.Split(';'))
                if (string.Equals(name.Trim(), cue, StringComparison.OrdinalIgnoreCase))
                    return DecodeWave(bank, wave.WaveId, cue, saveIntro);
        }
        return null;
    }

    private static AudioClip DecodeWave(AwbReader bank, int waveId, string cue, Action<AudioClip> saveIntro)
    {
        var samples = ReadWave(bank, waveId, out int rate, out int channels, out var info);
        if (saveIntro != null && info.LoopEnabled)
        {
            int start = info.LoopStartSample, end = info.LoopEndSample;
            if (start < 0 || end <= start || end > samples.Length / channels)
                throw new InvalidDataException($"Invalid HCA loop {start}..{end} for {cue}.");
            if (start > 0)
                saveIntro(CreateClip(cue + " intro", new ArraySegment<float>(samples, 0, start * channels).ToArray(), rate, channels));
            samples = new ArraySegment<float>(samples, start * channels, (end - start) * channels).ToArray();
        }
        return CreateClip(cue, samples, rate, channels);
    }

    private static float[] ReadWave(AwbReader bank, int waveId, out int rate, out int channels, out ClHcaSharp.HcaInfo info)
    {
        using (var wave = new UmaWaveStream(bank, waveId))
        {
            info = wave.Info;
            wave.Loop = false;
            var format = wave.WaveFormat;
            rate = format.SampleRate;
            channels = format.Channels;
            int frames = checked((int)(wave.Length / format.BlockAlign));
            var samples = new float[frames * format.Channels];
            var provider = wave.ToSampleProvider();
            for (int read = 0; read < samples.Length;)
            {
                int count = provider.Read(samples, read, samples.Length - read);
                if (count == 0)
                    break;
                read += count;
            }
            return samples;
        }
    }

    private static AudioClip CreateClip(string cue, float[] samples, int rate, int channels)
    {
        var clip = AudioClip.Create(cue, samples.Length / channels, channels, rate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private AudioSource CreateSource(string name)
    {
        var node = new GameObject(name);
        node.transform.SetParent(root, false);
        var source = node.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.mute = muted;
        return source;
    }

    private Channel CreateChannel(string name)
    {
        var channel = new Channel(CreateSource(name));
        channel.SetPaused(paused);
        return channel;
    }

    public void Dispose()
    {
        foreach (var source in root.GetComponentsInChildren<AudioSource>(true))
        {
            source.Stop();
            source.clip = null;
        }
        if (ownsClips)
        {
            foreach (var clip in clips.Values)
                if (clip) UnityEngine.Object.Destroy(clip);
            clips.Clear();
        }
    }

    internal sealed class Channel
    {
        public readonly AudioSource Source;
        private float startVolume;
        private float targetVolume;
        private float fadeTime;
        private float elapsed;
        private bool stopAfterFade;
        private float volume = 1f;
        private float duckVolume = 1f;
        private double playbackTime;
        private bool paused;
        public bool IsPlaying { get; private set; }
        private AudioClip savedClip;
        private bool savedLoop;
        private readonly AudioSource introSource;
        private AudioClip introClip;
        private double IntroDuration => introClip ? (double)introClip.samples / introClip.frequency : 0;


        internal Channel Capture()
        {
            var state = (Channel)MemberwiseClone();
            state.savedClip = Source.clip;
            state.savedLoop = Source.loop;
            return state;
        }

        internal void Restore(Channel state)
        {
            Source.Stop();
            if (introSource) introSource.Stop();
            introClip = state.introClip;
            Source.clip = state.savedClip;
            Source.loop = state.savedLoop;
            startVolume = state.startVolume;
            targetVolume = state.targetVolume;
            fadeTime = state.fadeTime;
            elapsed = state.elapsed;
            stopAfterFade = state.stopAfterFade;
            volume = state.volume;
            duckVolume = state.duckVolume;
            playbackTime = state.playbackTime;
            IsPlaying = state.IsPlaying;
            ApplyVolume();
            Synchronize();
        }

        public float DuckVolume
        {
            set
            {
                duckVolume = Mathf.Clamp01(value);
                ApplyVolume();
            }
        }

        private void ApplyVolume()
        {
            Source.volume = volume * duckVolume;
            if (introSource) introSource.volume = Source.volume;
        }

        public Channel(AudioSource source, AudioSource introSource = null)
        {
            Source = source;
            this.introSource = introSource;
        }

        // Schedule both sides of the intro seam on Unity's audio clock. From then on
        // the mixer loops only the exact PCM frames between the two HCA markers.
        private void PlayIntroAt(int sample)
        {
            introSource.Stop();
            Source.Stop();
            introSource.clip = introClip;
            introSource.timeSamples = sample;
            Source.timeSamples = 0;
            double start = AudioSettings.dspTime + 0.02;
            introSource.PlayScheduled(start);
            Source.PlayScheduled(start + (double)(introClip.samples - sample) / introClip.frequency);
        }

        public void Play(AudioClip clip, bool loop, float volume, AudioClip intro = null)
        {
            if (!clip)
                return;
            if (introSource) introSource.Stop();
            introClip = intro;
            Fade(volume, 0, false);
            Source.clip = clip;
            Source.loop = loop;
            playbackTime = 0;
            IsPlaying = true;
            if (introClip)
            {
                Source.Stop();
                if (!paused) PlayIntroAt(0);
                return;
            }
            Source.Play();
            if (paused)
                Source.Pause();
        }

        public void Stop()
        {
            if (introSource) introSource.Stop();
            IsPlaying = false;
            playbackTime = 0;
            Source.Stop();
        }

        public void Synchronize()
        {
            var clip = Source.clip;
            if (!IsPlaying || !clip)
                return;
            double length = (double)clip.samples / clip.frequency;
            double bodyTime = playbackTime - IntroDuration;
            if (introClip && bodyTime < 0)
            {
                int introSample = Math.Min(introClip.samples - 1, (int)(playbackTime * introClip.frequency));
                if (!paused && (!introSource.isPlaying || Math.Abs(introSource.timeSamples - introSample) > clip.frequency / 10))
                    PlayIntroAt(introSample);
                return;
            }
            double position = Source.loop ? bodyTime % length : bodyTime;
            int sample = Math.Min(clip.samples - 1, (int)(position * clip.frequency));
            if (paused)
            {
                Source.timeSamples = sample;
                return;
            }
            int difference = Math.Abs(Source.timeSamples - sample);
            if (Source.loop)
                difference = Math.Min(difference, clip.samples - difference);
            if (!Source.isPlaying)
            {
                Source.timeSamples = sample;
                Source.Play();
            }
            else if (difference > clip.frequency / 10)
                Source.timeSamples = sample;
        }

        public void SetPaused(bool value)
        {
            if (paused == value)
                return;
            paused = value;
            if (introClip)
            {
                introSource.Stop();
                Source.Stop();
                if (!value) Synchronize();
                return;
            }
            if (value)
            {
                Source.Pause();
                Synchronize();
            }
            else
            {
                Source.UnPause();
                Synchronize();
            }
        }

        public void Fade(float volume, int milliseconds, bool stop)
        {
            startVolume = this.volume;
            targetVolume = Mathf.Clamp01(volume);
            fadeTime = milliseconds / 1000f;
            elapsed = 0f;
            stopAfterFade = stop;
            if (fadeTime <= 0f)
                CompleteFade();
        }

        public void Tick(float deltaTime)
        {
            if (IsPlaying && Source.clip)
            {
                playbackTime += deltaTime;
                if (!Source.loop && playbackTime >= (double)Source.clip.samples / Source.clip.frequency)
                    Stop();
            }
            if (fadeTime <= 0f)
                return;
            elapsed += deltaTime;
            volume = Mathf.Lerp(startVolume, targetVolume, elapsed / fadeTime);
            ApplyVolume();
            if (elapsed >= fadeTime)
                CompleteFade();
        }

        private void CompleteFade()
        {
            volume = targetVolume;
            ApplyVolume();
            fadeTime = 0f;
            if (stopAfterFade)
                Stop();
        }
    }
}
