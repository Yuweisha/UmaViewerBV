using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using CriWareFormats;

// The decoded jingle waveform does not contain its silent category-control cues.
// Preserve their ACB timing and the ACF REACT rules targeting the background BGM.
internal sealed class GachaJingleDucking
{
    internal sealed class Rule
    {
        public float Level;
        public double Attack, Release;
        public int AttackCurve, ReleaseCurve;
        public float AttackStrength, ReleaseStrength;
    }

    internal sealed class Window
    {
        public double Start, End;
        public Rule Rule;
        public float ReleaseFrom;

        public float ValueAt(double time)
        {
            if (time < Start)
                return 1f;
            if (time < End)
                return AttackAt(time);
            return Lerp(ReleaseFrom, 1f, Curve(time - End, Rule.Release,
                Rule.ReleaseCurve, Rule.ReleaseStrength));
        }

        public float AttackAt(double time) => Lerp(1f, Rule.Level,
            Curve(time - Start, Rule.Attack, Rule.AttackCurve, Rule.AttackStrength));
    }

    private readonly List<Window> active = new List<Window>();
    private double time;
    public float Volume { get; private set; } = 1f;

    internal GachaJingleDucking Capture()
    {
        var state = new GachaJingleDucking { time = time, Volume = Volume };
        foreach (var window in active)
            state.active.Add(new Window { Start = window.Start, End = window.End,
                Rule = window.Rule, ReleaseFrom = window.ReleaseFrom });
        return state;
    }

    internal void Restore(GachaJingleDucking state)
    {
        time = state.time;
        Volume = state.Volume;
        active.Clear();
        foreach (var window in state.active)
            active.Add(new Window { Start = window.Start, End = window.End,
                Rule = window.Rule, ReleaseFrom = window.ReleaseFrom });
    }

    public void Play(Window[] windows)
    {
        Stop();
        foreach (var window in windows)
        {
            var playing = new Window { Start = time + window.Start, End = time + window.End, Rule = window.Rule };
            playing.ReleaseFrom = playing.AttackAt(playing.End);
            active.Add(playing);
        }
        Evaluate();
    }

    public void Stop()
    {
        for (int index = active.Count - 1; index >= 0; --index)
        {
            var window = active[index];
            if (time < window.Start)
                active.RemoveAt(index);
            else if (time < window.End)
            {
                window.ReleaseFrom = window.AttackAt(time);
                window.End = time;
            }
        }
        Evaluate();
    }

    public void Tick(float deltaTime)
    {
        time += Math.Max(0, deltaTime);
        Evaluate();
    }

    private void Evaluate()
    {
        Volume = 1f;
        for (int index = active.Count - 1; index >= 0; --index)
        {
            var window = active[index];
            if (time >= window.End + window.Rule.Release)
                active.RemoveAt(index);
            else
                Volume = Math.Min(Volume, window.ValueAt(time));
        }
    }

    private static float Lerp(float from, float to, float value) => from + (to - from) * value;

    private static float Curve(double elapsed, double duration, int type, float strength)
    {
        float t = duration <= 0 ? 1f : (float)Math.Max(0, Math.Min(1, elapsed / duration));
        // The BGM-targeting REACTs use Linear and Square. Strength is 0..100.
        return type == 1 ? Lerp(t, t * t, strength / 100f) : t;
    }

    public static Dictionary<uint, Rule> ReadRules(Stream acf)
    {
        var reader = new Tables(acf);
        var names = reader.Table("CategoryNameTable");
        var categories = reader.Table("CategoryTable");
        int bgm = -1;
        for (int row = 0; row < names.Rows; ++row)
            if (names.Field<string>(row, "Name") == "BGM")
                bgm = names.Field<ushort>(row, "Index");
        if (bgm < 0)
            throw new InvalidDataException("ACF BGM category missing.");
        var rules = new Dictionary<uint, Rule>();
        var reacts = reader.Table("ReactTable");
        for (int row = 0; row < reacts.Rows; ++row)
        {
            if (reacts.Field<int>(row, "Dest") != bgm || reacts.Field<int>(row, "TargetType") != 0)
                continue;
            int attackCurve = reacts.Field<int>(row, "StartCurveType");
            int releaseCurve = reacts.Field<int>(row, "EndCurveType");
            if (reacts.Field<int>(row, "HoldType") != 0 || attackCurve > 1 || releaseCurve > 1)
                throw new InvalidDataException("Unsupported BGM REACT fade or hold type.");
            uint source = categories.Field<uint>(reacts.Field<int>(row, "Src"), "Id");
            rules.Add(source, new Rule
            {
                Level = reacts.Field<float>(row, "Level"),
                Attack = reacts.Field<int>(row, "DecrementTime") / 1000.0,
                Release = reacts.Field<int>(row, "IncrementTime") / 1000.0,
                AttackCurve = attackCurve, ReleaseCurve = releaseCurve,
                AttackStrength = reacts.Field<float>(row, "StartCurveStrength"),
                ReleaseStrength = reacts.Field<float>(row, "EndCurveStrength")
            });
        }
        return rules;
    }

    public static Window[] Read(Stream acb, string cue, Dictionary<uint, Rule> rules)
    {
        var reader = new CueReader(acb, rules);
        var names = reader.Table("CueNameTable");
        for (int row = 0; row < names.Rows; ++row)
            if (names.Field<string>(row, "CueName") == cue)
            {
                reader.Walk(7, names.Field<ushort>(row, "CueIndex"), 0, 0);
                return reader.Windows.ToArray();
            }
        throw new InvalidDataException($"Jingle cue missing: {cue}.");
    }

    private class Tables
    {
        protected readonly Stream Stream;
        private readonly UtfTable header;
        private readonly Dictionary<string, TableData> tables = new Dictionary<string, TableData>();

        public Tables(Stream stream)
        {
            Stream = stream;
            header = new UtfTable(stream, 0);
        }

        public TableData Table(string name)
        {
            if (!tables.TryGetValue(name, out var table))
            {
                if (!header.Query(0, name, out VLData data) || data.Size == 0)
                    throw new InvalidDataException($"Missing CRI table: {name}.");
                table = new TableData(Stream, data.Offset);
                tables.Add(name, table);
            }
            return table;
        }

        public byte[] Bytes(VLData data)
        {
            Stream.Position = data.Offset;
            using var reader = new BinaryReader(Stream, System.Text.Encoding.UTF8, true);
            var bytes = reader.ReadBytes(checked((int)data.Size));
            if (bytes.Length != data.Size)
                throw new EndOfStreamException();
            return bytes;
        }
    }

    private sealed class TableData
    {
        private readonly UtfTable table;
        public readonly int Rows;

        public TableData(Stream stream, uint offset) => table = new UtfTable(stream, offset, out Rows, out _);

        public T Field<T>(int row, string name)
        {
            // The typed query also resolves VLData offsets against its parent table.
            if (table.Query(row, name, out T value))
                return value;
            if (!table.Query(row, table.GetColumn(name), out Result result))
                throw new InvalidDataException($"Missing CRI field: {name}, row {row}.");
            return result.Value is T typed ? typed : (T)Convert.ChangeType(result.Value, typeof(T));
        }
    }

    private sealed class CueReader : Tables
    {
        private readonly Dictionary<uint, Rule> rules;
        private readonly HashSet<(int, int)> path = new HashSet<(int, int)>();
        public readonly List<Window> Windows = new List<Window>();

        public CueReader(Stream stream, Dictionary<uint, Rule> rules) : base(stream) => this.rules = rules;

        public void Walk(int type, int index, double start, double duration)
        {
            if (type == 1)
                return; // Waveforms are played by the existing decoder.
            if (!path.Add((type, index)))
                throw new InvalidDataException("Cyclic jingle cue reference.");
            try
            {
                if (type == 7)
                {
                    var cues = Table("CueTable");
                    uint milliseconds = cues.Field<uint>(index, "Length");
                    Walk(cues.Field<byte>(index, "ReferenceType"), cues.Field<ushort>(index, "ReferenceIndex"),
                        start, milliseconds == uint.MaxValue ? double.PositiveInfinity : milliseconds / 1000.0);
                }
                else if (type == 2)
                {
                    var items = Bytes(Table("SynthTable").Field<VLData>(index, "ReferenceItems"));
                    for (int offset = 0; offset + 4 <= items.Length; offset += 4)
                        Walk(BinaryPrimitives.ReadUInt16BigEndian(items.AsSpan(offset)),
                            BinaryPrimitives.ReadUInt16BigEndian(items.AsSpan(offset + 2)), start, duration);
                }
                else if (type == 3)
                {
                    var sequences = Table("SequenceTable");
                    ushort command = sequences.Field<ushort>(index, "CommandIndex");
                    if (command != ushort.MaxValue)
                        foreach (var item in Commands("SeqCommandTable", command))
                            if (item.Code == 0x41)
                                for (int offset = 0; offset + 4 <= item.Data.Length; offset += 4)
                                    if (rules.TryGetValue(BinaryPrimitives.ReadUInt32BigEndian(item.Data.AsSpan(offset)), out var rule))
                                        Windows.Add(new Window { Start = start, End = start + duration, Rule = rule });
                    var tracks = Bytes(sequences.Field<VLData>(index, "TrackIndex"));
                    if (tracks.Length > 2 && sequences.Field<byte>(index, "Type") != 0)
                        throw new InvalidDataException("Expected parallel jingle tracks.");
                    for (int offset = 0; offset + 2 <= tracks.Length; offset += 2)
                    {
                        int track = BinaryPrimitives.ReadUInt16BigEndian(tracks.AsSpan(offset));
                        ushort events = Table("TrackTable").Field<ushort>(track, "EventIndex");
                        if (events == ushort.MaxValue)
                            continue;
                        double delay = 0;
                        foreach (var item in Commands("TrackEventTable", events))
                        {
                            if (item.Code == 2001 && item.Data.Length == 4)
                                delay += BinaryPrimitives.ReadUInt32BigEndian(item.Data) / 1000.0;
                            else if ((item.Code == 2000 || item.Code == 2003) && item.Data.Length >= 4)
                                Walk(BinaryPrimitives.ReadUInt16BigEndian(item.Data),
                                    BinaryPrimitives.ReadUInt16BigEndian(item.Data.AsSpan(2)), start + delay, duration);
                        }
                    }
                }
                else
                    throw new InvalidDataException($"Unsupported jingle reference type: {type}.");
            }
            finally { path.Remove((type, index)); }
        }

        private IEnumerable<(int Code, byte[] Data)> Commands(string table, int index)
        {
            var commands = Bytes(Table(table).Field<VLData>(index, "Command"));
            for (int offset = 0; offset + 3 <= commands.Length;)
            {
                int code = BinaryPrimitives.ReadUInt16BigEndian(commands.AsSpan(offset));
                int size = commands[offset + 2];
                offset += 3;
                if (offset + size > commands.Length)
                    throw new InvalidDataException($"Truncated jingle command in {table}[{index}] at {offset}: {size}/{commands.Length}.");
                var data = new byte[size];
                Array.Copy(commands, offset, data, 0, size);
                offset += size;
                yield return (code, data);
            }
        }
    }
}
