using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CriWareFormats;
using NAudio.Wave;
using UmaMusumeAudio;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Gallop.Live
{
    // Native 2.32 ResourcePath.GetLiveVoice[Chara]CueSheetPath and
    // Director.RegisterSongCueSheet select live voice separately from vocals.
    public static class LiveVoiceCueSheet
    {
        public static string NameForMusic(int musicId)
            => $"sound/l/{musicId:D4}/snd_bgm_live_{musicId:D4}_vo";

        public static string NameForCharacter(int musicId,int characterId,int voiceVersion=1)
            => NameForMusic(musicId)+$"_{characterId:D4}_{voiceVersion:D2}";

        public static string Select(int musicId,IEnumerable<int> characters,
            LiveSettingsResourceMap settings,int sheetVariationId=0)
        {
            int mode=0,resourceId=musicId;
            foreach(var rule in settings.MusicResourceOverrides)
            {
                if(rule.SheetVariationId>=0&&rule.SheetVariationId!=sheetVariationId)continue;
                if(rule.MusicResourceId>0)resourceId=rule.MusicResourceId;
                mode=rule.VoiceResourceId;
                break;
            }
            if(mode==1)return NameForMusic(resourceId);
            if(mode!=2)return null;
            // Native prefers the primary performer, then neutral commentary,
            // then a permitted singer. Viewer uses commentary voice A by default.
            var cast=characters.ToArray();
            int character=cast.Length>0&&settings.PermissionCharacterVoices.Contains(cast[0])?cast[0]:0;
            if(character==0&&!settings.PermissionCharacterVoices.Contains(0))
                character=cast.FirstOrDefault(id=>settings.PermissionCharacterVoices.Contains(id));
            return NameForCharacter(resourceId,character);
        }

        public static bool TryFindEntries(IEnumerable<UmaDatabaseEntry> sounds,int musicId,
            out UmaDatabaseEntry acb,out UmaDatabaseEntry awb)
            => TryFindEntries(sounds,NameForMusic(musicId),out acb,out awb);

        public static bool TryFindEntries(IEnumerable<UmaDatabaseEntry> sounds,string sheet,
            out UmaDatabaseEntry acb,out UmaDatabaseEntry awb)
        {
            acb=null;awb=null;
            if(sounds==null||string.IsNullOrEmpty(sheet))return false;
            foreach(var entry in sounds)
            {
                if(entry==null)continue;
                if(string.Equals(entry.Name,sheet+".acb",StringComparison.OrdinalIgnoreCase))acb=entry;
                else if(string.Equals(entry.Name,sheet+".awb",StringComparison.OrdinalIgnoreCase))awb=entry;
            }
            // Memory AWB is embedded in many live ACBs, including 1181.
            return acb!=null;
        }
    }

    // Reads the ACB Cue table (CueId/ReferenceType/ReferenceIndex) and Waveform
    // table to resolve a numeric cue id to the AWB wave id used by UmaWaveStream.
    public static class LiveVoiceCueResolver
    {
        // Native CRI GetCueInfoById reads sequence/synth command 0x57 as
        // authored cue volume (percent). A zero is intentional silence even
        // when the cue still references a valid waveform, as in Live1095.
        public static bool TryResolveCueVolume(Stream stream, int cueId, out float volume, out string error)
        {
            volume = 1f; error = null;
            try
            {
                var header = new UtfTable(stream, (uint)stream.Position);
                if (!header.Query(0, "CueTable", out VLData cueData)) { error = "CueTable not found"; return false; }
                var cues = new UtfTable(stream, cueData.Offset, out int count, out _);
                int row = -1;
                for (int i = 0; i < count; i++)
                    if (TryReadInt(cues, i, cues.GetColumn("CueId"), out long id) && id == cueId) { row = i; break; }
                if (row < 0) { error = "cue id " + cueId + " not found"; return false; }
                if (!TryReadInt(cues, row, cues.GetColumn("ReferenceType"), out long type) ||
                    !TryReadInt(cues, row, cues.GetColumn("ReferenceIndex"), out long index))
                { error = "cue reference unreadable"; return false; }
                string reference = type == 3 ? "SequenceTable" : type == 2 ? "SynthTable" : null;
                if (reference == null || !header.Query(0, reference, out VLData referenceData)) return true;
                var table = new UtfTable(stream, referenceData.Offset);
                if (!TryReadInt(table, (int)index, table.GetColumn("CommandIndex"), out long commandIndex) || commandIndex == 65535) return true;
                string commandName = type == 3 ? "SeqCommandTable" : "SynthCommandTable";
                if (!header.Query(0, commandName, out VLData commands) && !header.Query(0, "CommandTable", out commands)) return true;
                var commandTable = new UtfTable(stream, commands.Offset);
                if (!commandTable.Query((int)commandIndex, "Command", out VLData command)) return true;
                uint pos = 0;
                while (pos + 3 <= command.Size)
                {
                    int code = ReadUInt16BE(stream, command.Offset + pos);
                    int size = ReadByte(stream, command.Offset + pos + 2);
                    pos += 3;
                    if (pos + size > command.Size) { error = "truncated cue command"; return false; }
                    if (code == 0x57 && size == 2) volume = ReadUInt16BE(stream, command.Offset + pos) / 100f;
                    pos += (uint)size;
                }
                return true;
            }
            catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; return false; }
        }

        public static bool TryResolveCueId(Stream acbStream,string cueName,out int cueId,out string error)
        {
            cueId=-1;error=null;
            try
            {
                var header=new UtfTable(acbStream,(uint)acbStream.Position);
                if(!header.Query(0,"CueNameTable",out VLData namesData)||!header.Query(0,"CueTable",out VLData cuesData))
                {error="CueNameTable/CueTable not found";return false;}
                var names=new UtfTable(acbStream,namesData.Offset,out int rows,out _);
                var cues=new UtfTable(acbStream,cuesData.Offset);
                for(int row=0;row<rows;row++)
                    if(names.Query(row,"CueName",out string name)&&name==cueName&&names.Query(row,"CueIndex",out ushort index)&&
                        TryReadInt(cues,index,cues.GetColumn("CueId"),out long id))
                    {cueId=checked((int)id);return true;}
                error="cue name "+cueName+" not found";return false;
            }
            catch(Exception ex){error=ex.GetType().Name+": "+ex.Message;return false;}
        }
        public static bool TryResolveWaveId(Stream acbStream, int cueId, out int waveId, out string error)
            => TryResolveWaveId(acbStream,cueId,out waveId,out _,out error);

        public static bool TryResolveWaveId(Stream acbStream,int cueId,out int waveId,out bool isStreaming,out string error)
        {
            isStreaming=false;
            waveId = -1;
            error = null;
            try
            {
                long start = acbStream.Position;
                var header = new UtfTable(acbStream, (uint)start);
                if (!header.Query(0, "CueTable", out VLData cueData))
                {
                    error = "CueTable not found";
                    return false;
                }
                var cueTable = new UtfTable(acbStream, cueData.Offset, out int cueRows, out _);
                int cueIdColumn = cueTable.GetColumn("CueId");
                int refTypeColumn = cueTable.GetColumn("ReferenceType");
                int refIndexColumn = cueTable.GetColumn("ReferenceIndex");
                if (cueIdColumn < 0 || refTypeColumn < 0 || refIndexColumn < 0)
                {
                    error = "CueTable columns missing";
                    return false;
                }
                int row = -1;
                for (int i = 0; i < cueRows; i++)
                {
                    if (!TryReadInt(cueTable, i, cueIdColumn, out long id) || id != cueId) continue;
                    row = i;
                    break;
                }
                if (row < 0)
                {
                    error = "cue id " + cueId + " not found in " + cueRows + " cues";
                    return false;
                }
                if (!TryReadInt(cueTable, row, refTypeColumn, out long referenceType) ||
                    !TryReadInt(cueTable, row, refIndexColumn, out long referenceIndex))
                {
                    error = "cue reference unreadable";
                    return false;
                }
                var waveRowsList = new List<int>();
                WalkReference(acbStream, header, (int)referenceType, (int)referenceIndex, waveRowsList, 0);
                if (waveRowsList.Count == 0)
                {
                    error = "no waveform reachable from cue reference type " + referenceType;
                    return false;
                }
                if (!header.Query(0, "WaveformTable", out VLData waveData))
                {
                    error = "WaveformTable not found";
                    return false;
                }
                var waveTable = new UtfTable(acbStream, waveData.Offset, out int waveRows, out _);
                int rowIndex = waveRowsList[0];
                if (rowIndex < 0 || rowIndex >= waveRows)
                {
                    error = "waveform index out of range";
                    return false;
                }
                int streamingColumn = waveTable.GetColumn("Streaming");
                int memoryIdColumn = waveTable.GetColumn("MemoryAwbId");
                int streamIdColumn = waveTable.GetColumn("StreamAwbId");
                int idColumn = waveTable.GetColumn("Id");
                long waveIdValue = -1;
                if (streamingColumn >= 0 && TryReadInt(waveTable, rowIndex, streamingColumn, out long streaming) && streaming != 0)
                {
                    isStreaming=true;
                    if (streamIdColumn < 0 || !TryReadInt(waveTable, rowIndex, streamIdColumn, out waveIdValue))
                    {
                        error = "StreamAwbId unreadable";
                        return false;
                    }
                }
                else if (idColumn >= 0 && TryReadInt(waveTable, rowIndex, idColumn, out waveIdValue))
                {
                    // memory-first waveform: explicit Id
                }
                else if (memoryIdColumn >= 0 && TryReadInt(waveTable, rowIndex, memoryIdColumn, out waveIdValue))
                {
                }
                else if (streamIdColumn >= 0 && TryReadInt(waveTable, rowIndex, streamIdColumn, out waveIdValue))
                {
                }
                else
                {
                    error = "wave id unreadable";
                    return false;
                }
                waveId = (int)waveIdValue;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private static void WalkReference(Stream stream, UtfTable header, int referenceType, int referenceIndex,
            List<int> waveRows, int depth)
        {
            if (depth > 3 || referenceIndex < 0) return;
            switch (referenceType)
            {
                case 1:
                    waveRows.Add(referenceIndex);
                    break;
                case 2:
                {
                    if (!header.Query(0, "SynthTable", out VLData data)) return;
                    var table = new UtfTable(stream, data.Offset, out int rows, out _);
                    if (referenceIndex >= rows) return;
                    if (!table.Query(referenceIndex, "ReferenceItems", out VLData items)) return;
                    WalkReferenceItems(stream, header, items, waveRows, depth);
                    break;
                }
                case 3:
                {
                    if (!header.Query(0, "SequenceTable", out VLData data)) return;
                    var table = new UtfTable(stream, data.Offset, out int rows, out _);
                    if (referenceIndex >= rows) return;
                    if (!table.Query(referenceIndex, "TrackIndex", out VLData trackIndex)) return;
                    int trackCount = (int)(trackIndex.Size / 2);
                    for (int i = 0; i < trackCount; i++)
                    {
                        int trackRow = ReadInt16BE(stream, trackIndex.Offset + (uint)(i * 2));
                        if (trackRow < 0) continue;
                        if (!header.Query(0, "TrackTable", out VLData trackData)) continue;
                        var trackTable = new UtfTable(stream, trackData.Offset, out int trackRows, out _);
                        if (trackRow >= trackRows || !trackTable.Query(trackRow, "EventIndex", out ushort eventIndex)) continue;
                        if (eventIndex == 65535) continue;
                        if (!header.Query(0, "TrackEventTable", out VLData commandData) &&
                            !header.Query(0, "CommandTable", out commandData)) continue;
                        var commandTable = new UtfTable(stream, commandData.Offset, out int commandRows, out _);
                        if (eventIndex >= commandRows || !commandTable.Query(eventIndex, "Command", out VLData command)) continue;
                        WalkCommandTlvs(stream, header, command, waveRows, depth);
                    }
                    break;
                }
                default:
                    break;
            }
        }

        private static void WalkReferenceItems(Stream stream, UtfTable header, VLData items, List<int> waveRows, int depth)
        {
            int count = (int)(items.Size / 4);
            for (int i = 0; i < count; i++)
            {
                int type = ReadUInt16BE(stream, items.Offset + (uint)(i * 4));
                int index = ReadUInt16BE(stream, items.Offset + (uint)(i * 4 + 2));
                if (type == 1) waveRows.Add(index);
                else if (type == 2 || type == 3) WalkReference(stream, header, type, index, waveRows, depth + 1);
            }
        }

        private static void WalkCommandTlvs(Stream stream, UtfTable header, VLData command, List<int> waveRows, int depth)
        {
            uint pos = 0;
            while (pos + 3 <= command.Size)
            {
                ushort code = (ushort)ReadUInt16BE(stream, command.Offset + pos);
                int size = ReadByte(stream, command.Offset + pos + 2);
                pos += 3;
                if ((code == 2000 || code == 2003) && size >= 4 && pos + 4 <= command.Size)
                {
                    int type = ReadUInt16BE(stream, command.Offset + pos);
                    int index = ReadUInt16BE(stream, command.Offset + pos + 2);
                    if (type == 2 || type == 3) WalkReference(stream, header, type, index, waveRows, depth + 1);
                }
                pos += (uint)size;
            }
        }

        private static int ReadUInt16BE(Stream stream, uint offset)
        {
            stream.Position = offset;
            int hi = stream.ReadByte();
            int lo = stream.ReadByte();
            if (hi < 0 || lo < 0) return -1;
            return (hi << 8) | lo;
        }

        private static int ReadInt16BE(Stream stream, uint offset) => (short)ReadUInt16BE(stream, offset);

        private static int ReadByte(Stream stream, uint offset)
        {
            stream.Position = offset;
            return stream.ReadByte();
        }

        private static bool TryReadInt(UtfTable table, int row, int column, out long value)
        {
            value = 0;
            if (table.Query(row, column, out uint u)) { value = u; return true; }
            if (table.Query(row, column, out int i)) { value = i; return true; }
            if (table.Query(row, column, out ushort us)) { value = us; return true; }
            if (table.Query(row, column, out short s)) { value = s; return true; }
            if (table.Query(row, column, out byte b)) { value = b; return true; }
            if (table.Query(row, column, out sbyte sb)) { value = sb; return true; }
            return false;
        }
    }

    // Plays one voice cue at a time (original AudioManager voice is single-stream
    // for live), decoding the AWB wave through UmaWaveStream like the jukebox path.
    public sealed class LiveVoicePlayer : IDisposable
    {
        private GameObject _root;
        private AudioSource _source;
        private AudioClip _clip;
        private AwbReader _awbReader;
        private Stream _awbStream;
        public bool IsPlaying => _source != null && _source.isPlaying;
        public AudioClip Clip => _clip;
        public AudioSource Source => _source;

        public bool TryPlay(string acbPath, string awbPath, int cueId, float startTime, float volume, out string error)
        {
            error = null;
            Stop();
            if (string.IsNullOrEmpty(acbPath) || !File.Exists(acbPath)) { error = "ACB missing: " + acbPath; return false; }
            int waveId;
            bool isStreaming;
            float cueVolume;
            using (var acbStream = File.OpenRead(acbPath))
            {
                if (!LiveVoiceCueResolver.TryResolveWaveId(acbStream, cueId, out waveId, out isStreaming, out error)) return false;
                acbStream.Position = 0;
                if (!LiveVoiceCueResolver.TryResolveCueVolume(acbStream, cueId, out cueVolume, out error)) return false;
            }
            try
            {
                if(isStreaming)
                {
                    if(string.IsNullOrEmpty(awbPath)||!File.Exists(awbPath))
                    {error="AWB missing: "+awbPath;return false;}
                    _awbStream=File.OpenRead(awbPath);
                    _awbReader=new AwbReader(_awbStream);
                }
                else
                {
                    _awbStream=File.OpenRead(acbPath);
                    _awbReader=new AcbReader(_awbStream).GetAwb();
                }
                Wave wave = _awbReader.Waves.FirstOrDefault(item => item.WaveId == waveId);
                if (wave.Length <= 0) { error = "wave " + waveId + " not found in " + Path.GetFileName(awbPath); Stop(); return false; }
                var stream = new UmaWaveStream(_awbReader, waveId);
                var sampleProvider = stream.ToSampleProvider();
                int channels = stream.WaveFormat.Channels;
                int bytesPerSample = stream.WaveFormat.BitsPerSample / 8;
                int sampleRate = stream.WaveFormat.SampleRate;
                int sampleLength = checked((int)(stream.Length / channels / bytesPerSample));
                _clip = AudioClip.Create("LiveVoice_" + cueId, sampleLength, channels, sampleRate, true,
                    data => sampleProvider.Read(data, 0, data.Length),
                    position => stream.Position = (long)position * channels * bytesPerSample);
                _root = new GameObject("LiveVoiceAudio");
                GameObject audioController = GameObject.Find("AudioManager/AudioControllerBgm");
                if (audioController != null) _root.transform.SetParent(audioController.transform, false);
                _source = _root.AddComponent<AudioSource>();
                _source.clip = _clip;
                _source.loop = false;
                _source.playOnAwake = false;
                _source.volume = volume * cueVolume;
                if (startTime > 0f) _source.time = Mathf.Min(startTime, _clip.length - 0.01f);
                _source.Play();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                Stop();
                return false;
            }
        }

        public bool TryPlay(string acbPath,string awbPath,string cueName,float startTime,float volume,out string error)
        {
            error=null;
            if(!File.Exists(acbPath)){error="ACB missing: "+acbPath;return false;}
            int cue;
            using(var stream=File.OpenRead(acbPath))
                if(!LiveVoiceCueResolver.TryResolveCueId(stream,cueName,out cue,out error))return false;
            return TryPlay(acbPath,awbPath,cue,startTime,volume,out error);
        }

        public void Stop()
        {
            if (_source != null) { _source.Stop(); }
            if (_root != null)
            {
                if (Application.isPlaying) Object.Destroy(_root); else Object.DestroyImmediate(_root);
                _root = null;
            }
            if (_clip != null)
            {
                if (Application.isPlaying) Object.Destroy(_clip); else Object.DestroyImmediate(_clip);
                _clip = null;
            }
            _source = null;
            _awbReader = null;
            if (_awbStream != null) { _awbStream.Dispose(); _awbStream = null; }
        }

        public void Dispose() => Stop();
    }
}
