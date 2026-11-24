// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Diagnostics.Tracing;
using System.Globalization;
using System.Text;

namespace Vitomir.Engine.Diagnostics;

/// <summary>
/// **What the frame allocates, and what the garbage collector made of it**
/// (ours). 1.1 has nothing like it. A frame log of a 234-second
/// walk through the new Old Town showed a full (generation 2) collection in 132 of its seconds with the
/// managed heap small: the mark of large arrays made every frame. This is the instrument that found them,
/// kept so that the next one is found the same way.
///
/// With <c>VITOMIR_ALLOC_LOG</c> set (to <c>1</c> for the console only, or to a file for the summary
/// written there as well) the window's loop counts, phase by phase of <see cref="SlowFrames"/>, the bytes
/// its own thread allocated (<c>GC.GetAllocatedBytesForCurrentThread</c>), and beside them the bytes every
/// other thread allocated (<c>GC.GetTotalAllocatedBytes</c> less the loop's), the collections of each
/// generation and the time they paused; and it listens to the runtime's own events for <em>what</em> was
/// allocated — the allocation tick (one about every 100 KB, carrying the type that crossed the mark and
/// whether it went to the large object heap) — and for <em>why</em> each collection began (the reason the
/// runtime gives: the small heap's budget, the large heap's, somebody asking). The first
/// <see cref="SlowFrames.Settling"/> frames, a place's loading, are left out (<c>VITOMIR_ALLOC_SETTLE</c>
/// gives another count). Without the variable nothing
/// is made and the loop pays nothing.
/// </summary>
public sealed class FrameAllocations : EventListener
{
    /// <summary>The environment variable that turns the ledger on: <c>1</c>, or the file its summary is also written to.</summary>
    public const string Variable = "VITOMIR_ALLOC_LOG";

    /// <summary>The environment variable that says how many first frames are left out, when not <see cref="SlowFrames.Settling"/> (a slow machine's short run).</summary>
    public const string SettleVariable = "VITOMIR_ALLOC_SETTLE";

    /// <summary>The runtime's own event source, its garbage collector keyword and the two events read from it.</summary>
    const string Runtime = "Microsoft-Windows-DotNETRuntime";
    const EventKeywords GcKeyword = (EventKeywords)0x1;
    const int GcStart = 1, AllocationTick = 10;

    /// <summary>Why a collection began, as the runtime numbers its reasons (<c>GCStart</c>'s <c>Reason</c>).</summary>
    static readonly string[] Reasons =
    [
        "small heap budget", "asked for", "low memory", "empty", "large heap budget", "small heap out of space",
        "large heap out of space", "asked for (not forced)", "stress", "low memory (blocking)", "asked for (compacting)",
        "low memory (host)", "pinned heap budget", "low memory (pinned heap)",
    ];

    readonly string[] names;
    readonly long[] byPhase;            // bytes the loop's thread allocated in each phase, and after the last (the rest)
    readonly string? file;
    readonly int settling = int.TryParse(Environment.GetEnvironmentVariable(SettleVariable), out int n) && n >= 0 ? n : SlowFrames.Settling;
    readonly Lock gate = new();
    readonly Dictionary<(string Type, bool Large), (long Ticks, long Bytes, long Largest)> ticks = [];
    readonly Dictionary<(int Generation, string Reason), int> starts = [];
    long mark, frameStart, processStart;
    long frames, own, others;
    int gen0, gen1, gen2;
    TimeSpan paused;
    bool counting, began;

    FrameAllocations(string[] phases, string? file)
    {
        names = phases;
        byPhase = new long[phases.Length + 1];
        this.file = file;
    }

    /// <summary>The ledger for a loop of these phases when <see cref="Variable"/> asks for one, else null.</summary>
    public static FrameAllocations? FromEnvironment(string[] phases)
    {
        var value = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrEmpty(value) || value == "0") return null;
        return new FrameAllocations(phases, value == "1" ? null : value);
    }

    protected override void OnEventSourceCreated(EventSource source)
    {
        if (source.Name == Runtime) EnableEvents(source, EventLevel.Verbose, GcKeyword);
    }

    protected override void OnEventWritten(EventWrittenEventArgs e)
    {
        if (!counting || e.Payload is not { } p || e.PayloadNames is not { } n) return;
        if (e.EventId == AllocationTick)
        {
            string type = Field(p, n, "TypeName") as string ?? "?";
            bool large = Convert.ToInt32(Field(p, n, "AllocationKind") ?? 0, CultureInfo.InvariantCulture) == 1;
            long bytes = Convert.ToInt64(Field(p, n, "AllocationAmount64") ?? 0L, CultureInfo.InvariantCulture);
            long size = Convert.ToInt64(Field(p, n, "ObjectSize") ?? 0L, CultureInfo.InvariantCulture);
            lock (gate)
            {
                ticks.TryGetValue((type, large), out var was);
                ticks[(type, large)] = (was.Ticks + 1, was.Bytes + bytes, Math.Max(was.Largest, size));
            }
        }
        else if (e.EventId == GcStart)
        {
            int generation = Convert.ToInt32(Field(p, n, "Depth") ?? 0, CultureInfo.InvariantCulture);
            int reason = Convert.ToInt32(Field(p, n, "Reason") ?? 0, CultureInfo.InvariantCulture);
            var key = (generation, reason >= 0 && reason < Reasons.Length ? Reasons[reason] : $"reason {reason}");
            lock (gate) starts[key] = starts.GetValueOrDefault(key) + 1;
        }
    }

    static object? Field(IReadOnlyList<object?> payload, IReadOnlyList<string> names, string name)
    {
        for (int i = 0; i < names.Count && i < payload.Count; i++)
            if (names[i] == name) return payload[i];
        return null;
    }

    /// <summary>Where frame <paramref name="frame"/> (counted from the run's start) begins.</summary>
    public void Begin(int frame)
    {
        began = frame > settling;
        if (!began) return;
        if (!counting)
        {
            counting = true;
            gen0 = GC.CollectionCount(0); gen1 = GC.CollectionCount(1); gen2 = GC.CollectionCount(2);
            paused = GC.GetTotalPauseDuration();
        }
        mark = frameStart = GC.GetAllocatedBytesForCurrentThread();
        processStart = GC.GetTotalAllocatedBytes(false);
    }

    /// <summary>The end of phase <paramref name="phase"/>.</summary>
    public void Mark(int phase)
    {
        if (!began || phase >= names.Length) return;
        long now = GC.GetAllocatedBytesForCurrentThread();
        byPhase[phase] += now - mark;
        mark = now;
    }

    /// <summary>The frame's end.</summary>
    public void End()
    {
        if (!began) return;
        long now = GC.GetAllocatedBytesForCurrentThread();
        byPhase[^1] += now - mark;
        own += now - frameStart;
        others += Math.Max(0, GC.GetTotalAllocatedBytes(false) - processStart - (now - frameStart));
        frames++;
    }

    /// <summary>The run's summary: to the console, and to the file <see cref="Variable"/> named.</summary>
    public string Summary(string place)
    {
        var s = new StringBuilder();
        if (frames == 0) { s.AppendLine($"allocations in {place}: no frame after the first {settling} was counted"); return Said(s); }
        counting = false;
        double f = frames;
        s.AppendLine(string.Create(CultureInfo.InvariantCulture, $"allocations in {place} over {frames} frames (after the first {settling}):"));
        s.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  the loop's thread {own / f:N0} bytes a frame; other threads {others / f:N0} bytes a frame"));
        for (int i = 0; i < byPhase.Length; i++)
            s.AppendLine(string.Create(CultureInfo.InvariantCulture, $"    {(i < names.Length ? names[i] : "rest"),-12} {byPhase[i] / f,14:N0}"));
        s.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"  collections: generation 0 {GC.CollectionCount(0) - gen0}, generation 1 {GC.CollectionCount(1) - gen1}, generation 2 {GC.CollectionCount(2) - gen2}; {(GC.GetTotalPauseDuration() - paused).TotalMilliseconds:0} ms paused"));
        lock (gate)
        {
            foreach (var ((generation, reason), count) in starts.OrderBy(x => x.Key.Generation).ThenByDescending(x => x.Value))
                s.AppendLine(string.Create(CultureInfo.InvariantCulture, $"    generation {generation} began {count} times: {reason}"));
            s.AppendLine("  allocation ticks (one about every 100 KB; the type that crossed the mark), the most first:");
            foreach (var ((type, large), (count, bytes, largest)) in ticks.OrderByDescending(x => x.Value.Bytes).Take(24))
                s.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"    {bytes / f,12:N0} bytes a frame  {count,6} ticks  {(large ? "LARGE" : "small")}  largest {largest,10:N0}  {type}"));
        }
        return Said(s);
    }

    string Said(StringBuilder s)
    {
        string text = s.ToString();
        Console.Write(text);
        if (file is not null)
            try { File.WriteAllText(file, text); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Console.WriteLine($"allocations: could not write {file}: {e.Message}"); }
        return text;
    }
}
