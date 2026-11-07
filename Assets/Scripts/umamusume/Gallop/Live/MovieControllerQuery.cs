using System;

namespace Gallop.Live
{
    // StageController.GetMovieController 0x4eacf10:
    // movie alive check (0x5e5c7f0 returns true when destroyed), index bounds,
    // entry+0x50 flag; require flag checks entry+0x30.
    public static class MovieControllerQuery
    {
        public static object Get(object movie, bool destroyed, int index, int count,
            object[] entries, bool requireFlag, byte entryFlag50, byte entryFlag30)
        {
            if (movie == null || destroyed) return null;
            if (index < 0 || index >= count) return null;
            var entry = entries != null && index < entries.Length ? entries[index] : null;
            if (entry == null) return null;
            if (entryFlag50 == 0) return null;
            if (requireFlag && entryFlag30 == 0) return null;
            return entry;
        }
    }
}
