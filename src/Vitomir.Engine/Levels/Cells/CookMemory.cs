// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Engine.Levels.Cells;

/// <summary>
/// **Ours: the cook's memory.** What the cook holds for good is small next to what passes through
/// it: the Old Town's scene, its meshes and the cook's lists are about 375 MB, while each cell's meshes,
/// levels of detail, collision tree and navigation tile, each far version and each map being compressed
/// come and go, gigabytes of them over a cook. The collector left to itself let that garbage pile up to
/// 2.7 GB on a machine that seemed to have memory to spare (the cook ran inside a smaller allowance than
/// the machine's, as on a laptop beside a game or a browser). So after each cell, far version and map the
/// cook calls <see cref="Settle"/>: when the heap has grown <see cref="Slack"/> past what was alive at the
/// last collection, it collects now. A full collection over the cook's live set takes a fraction of a
/// second and comes a few times a minute.
/// </summary>
public static class CookMemory
{
    /// <summary>How far (bytes) the heap may grow past the last collection's survivors before <see cref="Settle"/> collects.</summary>
    public static long Slack { get; set; } = 768L << 20;

    static long live = 0;
    static readonly object gate = new();

    /// <summary>Collects if the heap has grown <see cref="Slack"/> past the last collection's survivors; any thread may call it.</summary>
    public static void Settle()
    {
        if (GC.GetTotalMemory(false) <= Interlocked.Read(ref live) + Slack) return;
        lock (gate)
        {
            if (GC.GetTotalMemory(false) <= live + Slack) return;
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);
            Interlocked.Exchange(ref live, GC.GetTotalMemory(false));
        }
    }

    /// <summary>The most memory (MB) the process has held at once (its peak working set).</summary>
    public static long PeakMB()
    {
        using var self = System.Diagnostics.Process.GetCurrentProcess();
        return self.PeakWorkingSet64 >> 20;
    }
}
