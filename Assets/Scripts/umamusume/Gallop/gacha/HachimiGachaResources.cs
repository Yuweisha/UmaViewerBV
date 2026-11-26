using System.IO;
using System.IO.Compression;
using Newtonsoft.Json.Linq;
using UnityEngine;

// Hachimi-Hachimi/tl-zh-cn d25ece0757aaa73613c5e413f9fcfb8770c034dd:
// CC BY-NC-SA 4.0. User-supplied comment PNGs retain their separate attribution.
// See THIRD_PARTY_NOTICES.md and the notices inside Localize/gacha-zh-CN.bytes.
internal static class HachimiGachaResources
{
    private static ZipArchive archive;
    private static JObject titles;
    private static JObject captions;

    internal static Font Font => Resources.Load<Font>("Fonts/NotoSansCJKsc-Bold");

    internal static string CardTitle(int card)
    {
        titles ??= ReadJson("titles.json");
        return (string)titles?[card.ToString()];
    }

    internal static string Caption(int character, int voice)
    {
        captions ??= ReadJson("captions.json");
        return (string)captions?[character.ToString()]?[voice.ToString()];
    }

    internal static Texture2D LoadName(string gamePath) =>
        LoadTexture("names/" + gamePath + ".png");

    internal static Texture2D LoadComment(int card) =>
        LoadTexture($"comments/gacha_comment_{card:D6}.png");

    private static ZipArchive Archive
    {
        get
        {
            if (archive != null)
                return archive;
            var resource = Resources.Load<TextAsset>("Localize/gacha-zh-CN");
            if (!resource)
                throw new FileNotFoundException("Bundled CutIn Chinese resources are missing.");
            // Keep one archive for the application lifetime; only the two active
            // textures are decoded, and GachaPreviewFlash disposes those textures.
            archive = new ZipArchive(new MemoryStream(resource.bytes, false), ZipArchiveMode.Read);
            Resources.UnloadAsset(resource);
            return archive;
        }
    }

    private static JObject ReadJson(string path)
    {
        using var stream = Archive.GetEntry(path)?.Open();
        if (stream == null)
            throw new FileNotFoundException("Bundled CutIn translation is missing: " + path);
        using var reader = new StreamReader(stream);
        return JObject.Parse(reader.ReadToEnd());
    }

    private static Texture2D LoadTexture(string path)
    {
        using var stream = Archive.GetEntry(path)?.Open();
        if (stream == null)
            return null;
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            { name = Path.GetFileNameWithoutExtension(path) + " zh-CN", wrapMode = TextureWrapMode.Clamp };
        if (ImageConversion.LoadImage(texture, bytes.ToArray(), true))
            return texture;
        Object.Destroy(texture);
        Debug.LogWarning("Could not decode bundled CutIn texture: " + path);
        return null;
    }
}
