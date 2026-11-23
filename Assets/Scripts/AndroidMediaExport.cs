using System;
using System.IO;
using UnityEngine;

/// <summary>
/// 把渲染好的视频导出到公共 Downloads 目录。
///
/// Android 10+ 的 scoped storage 不允许直接往 /sdcard/Download 写文件：
/// targetSdk 29 时 requestLegacyExternalStorage 还能用，但 Android 11+ 会忽略它，
/// 手机上是 Android 16 就更不行了。正确做法是插入 MediaStore.Downloads，
/// 由系统分配 Uri，再通过 ContentResolver 流式写入 —— 全过程不需要任何权限。
/// </summary>
internal static class AndroidMediaExport
{
    private const int ChunkSize = 1 << 20;  // 1 MB，避免整段视频进内存

    /// <summary>
    /// 把 sourcePath 复制到 Downloads/&lt;album&gt;/&lt;displayName&gt;。
    /// 成功返回 true；失败给出 reason（例如厂商 ROM 禁用了 MediaStore 写入）。
    /// </summary>
    public static bool TryExport(string sourcePath, string displayName, string album,
        out string exportedAs, out string reason)
    {
        exportedAs = null;
        reason = null;

        if (!File.Exists(sourcePath))
        {
            reason = "source file missing: " + sourcePath;
            return false;
        }

        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (AndroidJavaObject resolver = activity.Call<AndroidJavaObject>("getContentResolver"))
            using (var mediaStore = new AndroidJavaClass("android.provider.MediaStore$Downloads"))
            using (AndroidJavaObject collection = mediaStore.GetStatic<AndroidJavaObject>("EXTERNAL_CONTENT_URI"))
            using (var values = new AndroidJavaObject("android.content.ContentValues"))
            {
                values.Call("put", "display_name", displayName);
                values.Call("put", "mime_type", "video/mp4");

                // Android 10+ 才认 relative_path；低版本会直接拒绝 insert，所以先只在 Q+ 上设
                if (GetSdkInt() >= 29)
                    values.Call("put", "relative_path", "Download/" + album);

                AndroidJavaObject uri = resolver.Call<AndroidJavaObject>("insert", collection, values);

                if (uri == null)
                {
                    reason = "MediaStore insert returned null (Downloads write may be blocked by this ROM)";
                    return false;
                }

                try
                {
                    using (AndroidJavaObject stream = resolver.Call<AndroidJavaObject>("openOutputStream", uri))
                    {
                        if (stream == null)
                        {
                            reason = "openOutputStream returned null";
                            return false;
                        }

                        using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read,
                                   FileShare.Read, 1 << 16))
                        {
                            var buffer = new byte[ChunkSize];
                            int read;

                            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                // Unity 的 AndroidJavaObject 会把 byte[] 转成 jbyteArray
                                var chunk = read == buffer.Length ? buffer : Trim(buffer, read);
                                stream.Call("write", chunk);
                            }
                        }

                        stream.Call("flush");
                    }
                }
                finally
                {
                    uri.Dispose();
                }

                exportedAs = "Download/" + album + "/" + displayName;
                return true;
            }
        }
        catch (Exception failure)
        {
            reason = failure.GetType().Name + ": " + failure.Message;
            return false;
        }
    }

    private static int GetSdkInt()
    {
        try
        {
            using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                return version.GetStatic<int>("SDK_INT");
        }
        catch
        {
            return 0;
        }
    }

    private static byte[] Trim(byte[] buffer, int length)
    {
        var result = new byte[length];
        Array.Copy(buffer, result, length);
        return result;
    }
}