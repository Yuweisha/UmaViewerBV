using System;
using System.Collections.Generic;
using System.Data;
using UnityEngine;

/// <summary>Native ordinary preview random motions, resolved without loading bundles.</summary>
internal sealed class PreviewIdleMotionData
{
    public Vector2 EarInterval, TailInterval;
    public UmaDatabaseEntry[] EarMotions, TailMotions;

    public static PreviewIdleMotionData Load(UmaContainerCharacter actor)
    {
        if (actor == null) throw new ArgumentNullException(nameof(actor));
        var database = UmaDatabaseController.Instance;
        var character = actor.CharaData ?? UmaDatabaseController.ReadCharaData(actor.CharaEntry);
        return new PreviewIdleMotionData
        {
            EarInterval = new Vector2(Seconds(character, "ear_random_time_min", 0.5f),
                Seconds(character, "ear_random_time_max", 1.5f)),
            TailInterval = new Vector2(Seconds(character, "tail_random_time_min", 0.8f),
                Seconds(character, "tail_random_time_max", 1.5f)),
            EarMotions = Resolve(database.ReadPreviewRandomMotionNames(1), true),
            TailMotions = Resolve(database.ReadPreviewRandomMotionNames(2), false)
        };
    }

    static float Seconds(DataRow character, string column, float fallback)
    {
        // Native data overrides defaults even when the authored interval is zero.
        return character == null || !character.Table.Columns.Contains(column) || character.IsNull(column)
            ? fallback : Convert.ToSingle(character[column]) / 100f;
    }

    static UmaDatabaseEntry[] Resolve(string[] motions, bool ear)
    {
        var entries = new List<UmaDatabaseEntry>(motions.Length);
        var resources = UmaViewerMain.Instance.AbList;
        foreach (var motion in motions)
        {
            // Ordinary controllers use type00 with swapId=-1. Preview has no
            // native swap-ear/tail identity, so do not guess a costume variant.
            string path = ear
                ? "3d/motion/event/facial/type00/anm_eve_type00_" + motion + "_driven"
                : "3d/motion/event/body/type00/tail/anm_eve_type00_" + motion;
            if (resources.TryGetValue(path.ToLowerInvariant(), out var entry))
                entries.Add(entry);
        }
        return entries.ToArray();
    }
}
