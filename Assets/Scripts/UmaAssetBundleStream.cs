using System.IO;

public sealed class UmaAssetBundleStream : FileStream
{
    private const int HeaderSize = 256;
    private const int FileBufferSize = 128 * 1024;

    private readonly byte[] keys;
    private readonly object readLock = new object();

    public UmaAssetBundleStream(string filename, byte[] keys)
        : base(filename,FileMode.Open,FileAccess.Read,FileShare.Read,FileBufferSize,FileOptions.SequentialScan)
    {
        this.keys = keys;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        lock (readLock)
        {
            long startPosition = Position;
            int read = base.Read(buffer, offset, count);

            if (read <= 0 || keys == null || keys.Length == 0)
                return read;

            int begin = 0;

            if (startPosition < HeaderSize)
            {
                long remainingHeader = HeaderSize - startPosition;
                if (remainingHeader >= read)
                    return read;

                begin = (int)remainingHeader;
            }

            int keyLength = keys.Length;
            int keyIndex = (int)((startPosition + begin) % keyLength);
            int end = offset + read;
            int p = offset + begin;

            // 密钥流长度是 8 的倍数（ABKey 11 字节 × 8 = 88）时按 8 字节批量异或，
            // ARM 上逐字节取模/边界检查的开销很明显，这里一次处理 8 字节。
            if ((keyLength & 7) == 0 && keyLength >= 8)
            {
                // 先推进到 8 字节对齐，之后 keyIndex 始终保持 8 的倍数
                while (p < end && (keyIndex & 7) != 0)
                {
                    buffer[p++] ^= keys[keyIndex];

                    if (++keyIndex == keyLength)
                        keyIndex = 0;
                }

                while (p + 8 <= end)
                {
                    buffer[p] ^= keys[keyIndex];
                    buffer[p + 1] ^= keys[keyIndex + 1];
                    buffer[p + 2] ^= keys[keyIndex + 2];
                    buffer[p + 3] ^= keys[keyIndex + 3];
                    buffer[p + 4] ^= keys[keyIndex + 4];
                    buffer[p + 5] ^= keys[keyIndex + 5];
                    buffer[p + 6] ^= keys[keyIndex + 6];
                    buffer[p + 7] ^= keys[keyIndex + 7];

                    p += 8;
                    keyIndex += 8;

                    if (keyIndex >= keyLength)
                        keyIndex = 0;
                }
            }

            while (p < end)
            {
                buffer[p++] ^= keys[keyIndex];

                if (++keyIndex == keyLength)
                    keyIndex = 0;
            }

            return read;
        }
    }
}
