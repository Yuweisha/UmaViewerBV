using System;

namespace Gallop.Live
{
    // StageController name pipeline, reconstructed from metadata string literals:
    // DeleteCloneToken replaces "(Clone)" with the empty string (literals 986 and 0),
    // then TextUtil.Format runs string.Format(cleanName, nameSuffix, null), and the
    // registry hash is FNVHash.Generate of that result.
    //
    // IMPORTANT: the Replace/Format bodies were not executed natively; this is
    // metadata-evidence-based reconstruction, not a native differential result.
    public static class LiveStageNameComposition
    {
        public const string CloneToken = "(Clone)";

        public static string DeleteCloneToken(string name)
        {
            if (name == null) throw new NullReferenceException("stage object name");
            return name.Replace(CloneToken, string.Empty);
        }

        public static string Compose(string name, string nameSuffix)
        {
            string clean = DeleteCloneToken(name);
            return string.Format(clean, nameSuffix, null);
        }

        public static int Hash(string name, string nameSuffix)
        {
            return Gallop.Live.Cutt.FNVHash.Generate(Compose(name, nameSuffix));
        }
    }
}
