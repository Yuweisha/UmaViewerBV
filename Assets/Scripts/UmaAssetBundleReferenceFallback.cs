using System;
using System.IO;
using System.Text;
using K4os.Compression.LZ4;
using UnityEngine;

internal static class UmaAssetBundleReferenceFallback
{
    private const string PrefabBundle = "3d/effect/charaskill/pfb_eff_chr1124_02/pfb_eff_chr1124_02_05";
    private const string OriginalTextureBundle = "sourceresources/3d/effect/common/textures/tex_eff_com_glow04_128_mirror";
    private const string TextureBundle = "sourceresources/3d/effect/common/materials/mtl_eff_com_shadow03";
    private const string OriginalCab = "CAB-afcb0e7d80f7526503bee2afb74676ad";
    private const string TextureCab = "CAB-ae0c1ff8822a8556acd404e28811f3a0";

    internal static UmaDatabaseEntry GetDependency(UmaViewerMain main, UmaDatabaseEntry entry)
    {
        if (entry.Name != PrefabBundle || main.AbList.ContainsKey(OriginalTextureBundle))
            return null;
        // 官方资源路径错误：glow04_mirror 的独立包引用仍指向旧 CAB，实际纹理已收录于 common/materials/mtl_eff_com_shadow03。
        // 仅在原路径未收录时兜底修正外部引用，保留原材质与纹理 pathID，由 Unity 原生解析。
        if (!main.AbList.TryGetValue(TextureBundle, out var dependency))
            throw new InvalidOperationException($"Texture reference fallback unavailable: {TextureBundle}");
        return dependency;
    }

    internal static bool TryLoad(UmaDatabaseEntry entry, string filePath, out AssetBundle bundle)
    {
        bundle = null;
        if (GetDependency(UmaViewerMain.Instance, entry) == null)
            return false;
        using var input = new UmaAssetBundleStream(filePath, entry.IsEncrypted ? entry.FKey : null);
        byte[] corrected = RedirectReference(input);
        if (corrected == null)
            return false;
        bundle = AssetBundle.LoadFromMemory(corrected);
        return true;
    }

    internal static byte[] RedirectReference(Stream input)
    {
        using var reader = new BinaryReader(input, Encoding.UTF8, true);
        if (ReadString(reader) != "UnityFS")
            throw new InvalidDataException("Texture reference fallback requires a UnityFS bundle");
        uint version = ReadUInt32(reader);
        ReadString(reader);
        string engineVersion = ReadString(reader);
        long sizeOffset = input.Position;
        long bundleSize = checked((long)ReadUInt64(reader));
        int compressedInfoSize = checked((int)ReadUInt32(reader));
        int infoSize = checked((int)ReadUInt32(reader));
        uint flags = ReadUInt32(reader);
        int headerSize = checked((int)input.Position);
        bool alignHeader = version >= 7 || engineVersion.StartsWith("2019.4.", StringComparison.Ordinal);
        long infoOffset = alignHeader ? Align(input.Position) : input.Position;
        input.Position = (flags & 0x80) != 0 ? bundleSize - compressedInfoSize : infoOffset;
        byte[] directory = Decode(ReadBytes(reader, compressedInfoSize), infoSize, flags);
        input.Position = (flags & 0x80) != 0 ? infoOffset : infoOffset + compressedInfoSize;
        if ((flags & 0x200) != 0)
            input.Position = Align(input.Position);
        using var directoryStream = new MemoryStream(directory, true);
        using var directoryReader = new BinaryReader(directoryStream, Encoding.UTF8, true);
        using var directoryWriter = new BinaryWriter(directoryStream, Encoding.UTF8, true);
        using var payload = new MemoryStream();
        directoryStream.Position = 16;
        uint blockCount = ReadUInt32(directoryReader);
        for (uint blockIndex = 0; blockIndex < blockCount; blockIndex++)
        {
            int uncompressedSize = checked((int)ReadUInt32(directoryReader));
            int compressedSize = checked((int)ReadUInt32(directoryReader));
            uint blockFlags = (uint)(directoryReader.ReadByte() << 8 | directoryReader.ReadByte());
            byte[] block = Decode(ReadBytes(reader, compressedSize), uncompressedSize, blockFlags);
            payload.Write(block, 0, block.Length);
            directoryStream.Position -= 6;
            WriteUInt32(directoryWriter, (uint)uncompressedSize);
            directoryWriter.Write((byte)(blockFlags >> 8));
            directoryWriter.Write((byte)(blockFlags & ~0x3f));
        }
        byte[] data = payload.ToArray();
        byte[] originalPath = Encoding.UTF8.GetBytes($"archive:/{OriginalCab}/{OriginalCab}\0");
        byte[] correctedPath = Encoding.UTF8.GetBytes($"archive:/{TextureCab}/{TextureCab}\0");
        int matches = 0;
        for (int offset = 0; offset <= data.Length - originalPath.Length; offset++)
        {
            if (data[offset] != originalPath[0])
                continue;
            int matched = 1;
            while (matched < originalPath.Length && data[offset + matched] == originalPath[matched])
                matched++;
            if (matched != originalPath.Length)
                continue;
            Buffer.BlockCopy(correctedPath, 0, data, offset, correctedPath.Length);
            matches++;
            offset += originalPath.Length - 1;
        }
        if (matches == 0)
            return null;
        if (matches != 1)
            throw new InvalidDataException($"Unexpected texture reference count: {matches}");
        Array.Clear(directory, 0, 16);
        input.Position = 0;
        byte[] header = ReadBytes(reader, headerSize);
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.UTF8, true);
        writer.Write(header);
        if (alignHeader)
            output.SetLength(output.Position = Align(output.Position));
        writer.Write(directory);
        if ((flags & 0x200) != 0)
            output.SetLength(output.Position = Align(output.Position));
        writer.Write(data);
        long outputSize = output.Length;
        output.Position = sizeOffset;
        WriteUInt32(writer, (uint)(outputSize >> 32));
        WriteUInt32(writer, (uint)outputSize);
        WriteUInt32(writer, (uint)directory.Length);
        WriteUInt32(writer, (uint)directory.Length);
        WriteUInt32(writer, flags & ~0xbfu);
        return output.ToArray();
    }

    private static byte[] Decode(byte[] compressed, int length, uint flags)
    {
        uint compression = flags & 0x3f;
        if (compression == 0 && compressed.Length == length)
            return compressed;
        if (compression != 2 && compression != 3)
            throw new InvalidDataException($"Unsupported texture reference bundle compression: {compression}");
        var decoded = new byte[length];
        if (LZ4Codec.Decode(compressed, 0, compressed.Length, decoded, 0, length) != length)
            throw new InvalidDataException("Truncated texture reference bundle block");
        return decoded;
    }

    private static string ReadString(BinaryReader reader)
    {
        using var bytes = new MemoryStream();
        byte value;
        while ((value = reader.ReadByte()) != 0)
            bytes.WriteByte(value);
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    private static byte[] ReadBytes(BinaryReader reader, int count)
    {
        byte[] bytes = reader.ReadBytes(count);
        if (bytes.Length != count)
            throw new EndOfStreamException();
        return bytes;
    }

    private static uint ReadUInt32(BinaryReader reader) =>
        (uint)reader.ReadByte() << 24 | (uint)reader.ReadByte() << 16 | (uint)reader.ReadByte() << 8 | reader.ReadByte();

    private static ulong ReadUInt64(BinaryReader reader) => (ulong)ReadUInt32(reader) << 32 | ReadUInt32(reader);

    private static void WriteUInt32(BinaryWriter writer, uint value)
    {
        writer.Write((byte)(value >> 24));
        writer.Write((byte)(value >> 16));
        writer.Write((byte)(value >> 8));
        writer.Write((byte)value);
    }

    private static long Align(long position) => (position + 15) & ~15L;
}
