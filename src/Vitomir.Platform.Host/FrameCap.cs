// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: a cap on how many pictures a second the window draws.
using System.Diagnostics;

namespace Vitomir.Platform.Host;

/// <summary>
/// The frame cap (the setting <c>frameCap</c>, <c>--frame-cap</c>): the window draws at most so many pictures a second, an even fraction
/// of the display's refresh so each picture stays on the screen for the same number of its refreshes. Presentation only: the main loop
/// waits before it draws; the simulation's fixed tick and its accumulator go on as before (a tape is the same with any cap).
/// </summary>
public sealed class FrameCap
{
    /// <summary>
    /// The cap a run draws at for <paramref name="asked"/> pictures a second on a display refreshed <paramref name="refreshHz"/> times a second:
    /// 0 (none) for none asked; the refresh divided by the whole number that brings it nearest to the ask without going under it (60 asked is 72
    /// on 144 Hz, 60 on 60 or 120 Hz, 50 on 100 Hz; more than the refresh, the refresh); the ask as given when the refresh is not known.
    /// </summary>
    public static double Effective(double asked, double refreshHz)
    {
        if (!(asked > 0)) return 0;
        if (!(refreshHz > 0)) return asked;
        int n = Math.Max(1, (int)Math.Floor(refreshHz / asked + 1e-6));
        return refreshHz / n;
    }

    readonly long interval;   // Stopwatch ticks a picture
    long next;

    /// <summary>A cap of <paramref name="fps"/> pictures a second (above 0).</summary>
    public FrameCap(double fps)
    {
        Fps = fps;
        interval = (long)Math.Round(Stopwatch.Frequency / fps);
        next = Stopwatch.GetTimestamp();
    }

    /// <summary>The pictures a second it holds to.</summary>
    public double Fps { get; }

    /// <summary>
    /// Waits until the next picture is due (slept to within about a millisecond and a half of it, then spun): call before each draw. A picture
    /// that came late starts the count again from now, so a stall is not followed by a burst.
    /// </summary>
    public void Wait()
    {
        long now = Stopwatch.GetTimestamp();
        if (now - next > interval) next = now;          // late by more than a picture: from now
        long spinFrom = next - Stopwatch.Frequency * 3 / 2000;
        while (true)
        {
            now = Stopwatch.GetTimestamp();
            if (now >= next) break;
            if (now < spinFrom) Thread.Sleep(Math.Max(1, (int)((spinFrom - now) * 1000 / Stopwatch.Frequency)));
            else Thread.SpinWait(64);
        }
        next += interval;
    }
}
