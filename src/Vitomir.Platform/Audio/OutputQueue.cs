// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// How far ahead of the speakers Vitomir's own sound output keeps its queue (ours): found by the
// slow-frame check, when a frame was allowed a second (World.FrameStep).
namespace Vitomir.Platform.Audio;

/// <summary>
/// **How much mixed sound is queued ahead of the speakers** (ours). 1.1's sound is Miles's, mixed on
/// Miles's own thread whatever the game's frame rate; Vitomir mixes once a frame and queues the result
/// for the device (<see cref="AudioOutput"/>, <c>Render.AudioOut</c> before), a tenth of a second ahead. Below ten frames a second that queue ran
/// dry between frames and the sound broke up into bursts and silences — nothing of the game's, but a
/// Vitomir system that could not take a long frame, which a frame of up to a second (1.1's bound,
/// <c>World.FrameStep</c>) makes easy to meet on a slow machine. So the queue is kept at least one and a
/// half of the last gap between two pumps, up to <see cref="LongestSeconds"/>: at sixty frames a second
/// it is the tenth it was, and at four it is long enough to last until the next frame.
/// </summary>
public static class OutputQueue
{
    /// <summary>How many of the last gap between pumps the queue holds (ours).</summary>
    public const double GapShare = 1.5;

    /// <summary>The most it holds, in seconds: one and a half of 1.1's longest frame (ours).</summary>
    public const double LongestSeconds = 1.5;

    /// <summary>
    /// The frames to keep queued, at <paramref name="rate"/> a second (1.1's mixer's is 22,050), after a gap of
    /// <paramref name="gapSeconds"/> since the last pump: never fewer than <paramref name="least"/>.
    /// </summary>
    public static int Frames(int least, double gapSeconds, int rate)
    {
        double want = Math.Min(Math.Max(gapSeconds, 0) * GapShare, LongestSeconds) * rate;
        return Math.Max(least, (int)Math.Ceiling(want));
    }
}
