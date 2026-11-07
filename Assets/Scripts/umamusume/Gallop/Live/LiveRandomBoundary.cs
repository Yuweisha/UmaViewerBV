using System;
using System.IO;
using UnityEngine;

namespace Gallop.Live
{
    // Engine RNG is an explicit boundary in the recovered native functions.
    // In ordinary playback these methods call the exact Unity API unchanged.
    // An opt-in conformance session records/replays returned inputs and checks
    // call order/ranges, rather than claiming C# and IL2CPP RNG seeds are equal.
    public static class LiveRandomBoundary
    {
        [Serializable] public sealed class Sample
        {
            public int index;
            public string kind;
            public int minInt, maxInt, intValue;
            public float minFloat, maxFloat, floatValue;
        }
        static StreamWriter writer;
        static StreamReader reader;
        static int count;
        static bool active;
        static UnityEngine.Random.State previousState;
        public static int Count => count;
        public static bool IsActive => active;

        public static void Begin(string path, int seed, bool replay)
        {
            if (active) throw new InvalidOperationException("Live RNG session already active");
            previousState = UnityEngine.Random.state;
            try
            {
                if (replay) reader = new StreamReader(path);
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                    writer = new StreamWriter(path,false,new System.Text.UTF8Encoding(false));
                }
                UnityEngine.Random.InitState(seed);
                count = 0;active = true;
            }
            catch
            {
                writer?.Dispose();writer=null;reader?.Dispose();reader=null;
                UnityEngine.Random.state=previousState;
                throw;
            }
        }
        static Sample Exchange(Sample input)
        {
            if (!active) return input;
            input.index = count++;
            if (writer != null)
            {
                writer.WriteLine(JsonUtility.ToJson(input));
                if ((count & 255) == 0) writer.Flush();
                return input;
            }
            string line = reader.ReadLine();
            if (line == null) throw new InvalidOperationException("Live RNG replay exhausted at " + input.index);
            var expected = JsonUtility.FromJson<Sample>(line);
            if (expected.index != input.index || expected.kind != input.kind || expected.minInt != input.minInt ||
                expected.maxInt != input.maxInt || expected.minFloat != input.minFloat || expected.maxFloat != input.maxFloat)
                throw new InvalidOperationException("Live RNG call/range mismatch at " + input.index + ": " + line + " / " + JsonUtility.ToJson(input));
            return expected;
        }
        public static float Value() => Exchange(new Sample { kind="value",floatValue=UnityEngine.Random.value }).floatValue;
        public static int RangeInt(int min,int max) => Exchange(new Sample {
            kind="intRange",minInt=min,maxInt=max,intValue=UnityEngine.Random.Range(min,max) }).intValue;
        public static float RangeFloat(float min,float max) => Exchange(new Sample {
            kind="floatRange",minFloat=min,maxFloat=max,floatValue=UnityEngine.Random.Range(min,max) }).floatValue;
        public static void Flush() { writer?.Flush(); }
        public static void End()
        {
            if (!active) return;
            try
            {
                writer?.Dispose();writer=null;
                if (reader != null && reader.ReadLine() != null)
                    throw new InvalidOperationException("Live RNG replay has unconsumed samples after " + count);
            }
            finally
            {
                reader?.Dispose();reader=null;
                UnityEngine.Random.state=previousState;active=false;
            }
        }
    }
}
