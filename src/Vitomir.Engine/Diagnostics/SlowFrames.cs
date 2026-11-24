// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Vitomir.Engine.Diagnostics;

/// <summary>
/// **A slow frame, and where its time went** (ours).
/// The first recorded play-test showed a frame missing its screen refresh about every two seconds —
/// a 32 ms frame, then two short ones catching up — that the tape could time but not explain. Each
/// frame of the window's loop is cut into its phases (<see cref="Mark"/>); a frame over
/// <see cref="Threshold"/> is logged with each phase's share and what the garbage collector did in it,
/// and the run ends with a summary. It costs a few timestamps a frame and writes nothing on a good one.
/// </summary>
public sealed class SlowFrames
{
    /// <summary>A frame this long has missed at least one refresh at 60 Hz (16.7 ms) by a clear margin.</summary>
    public const double Threshold = 28.0;

    /// <summary>The first frames of a run (a place's loading, its first uploads) are not counted.</summary>
    public const int Settling = 120;

    readonly ILogger log = Diag.For("Frames");
    readonly string[] names;
    readonly long[] at;
    long start;
    int marked;
    int gen0, gen1, gen2;
    TimeSpan paused;
    long allocated;

    int frames, slow;
    double longest, total;
    int longestFrame;
    /// <summary>What each phase allocated, when <c>VITOMIR_ALLOC_LOG</c> asks (ours; <see cref="FrameAllocations"/>); null otherwise.</summary>
    readonly FrameAllocations? allocations;
    int begun;

    /// <summary>The phases' names, in order; the time after the last mark is <c>rest</c>.</summary>
    public IReadOnlyList<string> Phases => names;

    /// <summary>
    /// **The frame just ended, measured** (ours, read by <see cref="FrameTimes"/>): its whole time in
    /// milliseconds, each phase's (<see cref="LastPhase"/>), the time after the last mark, what its thread
    /// allocated, and what the garbage collector did in it. Worked out for every frame, the first ones of a run
    /// included, whether or not it was slow.
    /// </summary>
    public double LastMs { get; private set; }
    public double LastRestMs { get; private set; }
    public long LastAllocated { get; private set; }
    public (int Gen0, int Gen1, int Gen2) LastCollections { get; private set; }
    public double LastPauseMs { get; private set; }
    readonly double[] lastPhase;

    /// <summary>The frame just ended: phase <paramref name="phase"/>'s milliseconds; 0 for one the frame did not mark.</summary>
    public double LastPhase(int phase) => phase < lastPhase.Length ? lastPhase[phase] : 0.0;

    public SlowFrames(params string[] phases)
    {
        names = phases;
        at = new long[phases.Length];
        lastPhase = new double[phases.Length];
        allocations = FrameAllocations.FromEnvironment(phases);
    }

    /// <summary>Where the frame starts.</summary>
    public void Begin()
    {
        start = Stopwatch.GetTimestamp();
        marked = 0;
        gen0 = GC.CollectionCount(0);
        gen1 = GC.CollectionCount(1);
        gen2 = GC.CollectionCount(2);
        paused = GC.GetTotalPauseDuration();
        allocated = GC.GetAllocatedBytesForCurrentThread();
        allocations?.Begin(++begun);
    }

    /// <summary>The end of phase <paramref name="phase"/> (in the order given to the constructor).</summary>
    public void Mark(int phase)
    {
        if (phase < at.Length) { at[phase] = Stopwatch.GetTimestamp(); marked = Math.Max(marked, phase + 1); }
        allocations?.Mark(phase);
    }

    /// <summary>
    /// The frame's end: logged when slow. <paramref name="frame"/> counts from the run's start;
    /// <paramref name="detail"/>, when given, is asked for only then and added to the line (the step's
    /// own share of step+draw, and the textures and buffer rebuilds the frame made).
    /// </summary>
    public void End(int frame, string place, Func<string>? detail = null)
    {
        long end = Stopwatch.GetTimestamp();
        allocations?.End();
        double ms = Stopwatch.GetElapsedTime(start, end).TotalMilliseconds;
        // Every frame's own numbers, kept for whoever writes them down (FrameTimes).
        LastMs = ms;
        long before = start;
        for (int i = 0; i < lastPhase.Length; i++)
        {
            lastPhase[i] = i < marked ? Stopwatch.GetElapsedTime(before, at[i]).TotalMilliseconds : 0.0;
            if (i < marked) before = at[i];
        }
        LastRestMs = Stopwatch.GetElapsedTime(before, end).TotalMilliseconds;
        LastAllocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        LastCollections = (GC.CollectionCount(0) - gen0, GC.CollectionCount(1) - gen1, GC.CollectionCount(2) - gen2);
        LastPauseMs = (GC.GetTotalPauseDuration() - paused).TotalMilliseconds;
        if (frame <= Settling) return;
        frames++;
        total += ms;
        if (ms > longest) { longest = ms; longestFrame = frame; }
        if (ms <= Threshold) return;
        slow++;
        var parts = new List<string>(names.Length + 1);
        long from = start;
        for (int i = 0; i < marked; i++)
        {
            parts.Add($"{names[i]} {Stopwatch.GetElapsedTime(from, at[i]).TotalMilliseconds:0.0}");
            from = at[i];
        }
        parts.Add($"rest {Stopwatch.GetElapsedTime(from, end).TotalMilliseconds:0.0}");
        string said = string.Join(", ", parts) + (detail is null ? "" : "; " + detail());
        double gcMs = (GC.GetTotalPauseDuration() - paused).TotalMilliseconds;
        log.LogInformation("slow frame {Frame} in {Place}: {Ms:0.0} ms — {Parts}; gc {G0}/{G1}/{G2} ({Gc:0.0} ms paused), {Kb:0} KB allocated",
            frame, place, ms, said,
            GC.CollectionCount(0) - gen0, GC.CollectionCount(1) - gen1, GC.CollectionCount(2) - gen2, gcMs,
            (GC.GetAllocatedBytesForCurrentThread() - allocated) / 1024.0);
    }

    /// <summary>The run's summary line.</summary>
    public void Summary(string place)
    {
        allocations?.Summary(place);
        if (frames == 0) return;
        log.LogInformation("frames in {Place}: {Frames}, {Slow} over {Threshold} ms, mean {Mean:0.0} ms, longest {Longest:0.0} ms (frame {At}); gc {G0}/{G1}/{G2} so far, {Pause:0} ms paused",
            place, frames, slow, Threshold, total / frames, longest, longestFrame,
            GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), GC.GetTotalPauseDuration().TotalMilliseconds);
    }
}
