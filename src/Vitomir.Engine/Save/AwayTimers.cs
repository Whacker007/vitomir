// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Engine.Save;

/// <summary>
/// **A place's record, taken while the party is away from it**. 1.1 writes a place's
/// `location_&lt;n&gt;.nsw` when the party leaves it, with its timers as times on
/// the one game clock, and reads every one back whose script exists — so a
/// timer that came due after the party left and before the place was read back is on the list in
/// the past, and fires at the first tick. Vitomir takes the record later — when the place is let go
/// (<c>Campaign</c>), or when a save is written with the place still loaded — from scripts whose clock
/// stopped when the party left; this puts that clock forward by the time away, and a timer already
/// due is left due now, to fire as soon as the place is back, as 1.1's would.
/// </summary>
public static class AwayTimers
{
    /// <summary>
    /// The record of <paramref name="place"/>, taken while the party is in <paramref name="here"/>: put
    /// forward by the time since the party left it (none, for the place the party is in), and marked
    /// with the game's clock.
    /// </summary>
    public static LocationSave Taken(LocationSave record, World.Location place, World.Location here)
    {
        if (!ReferenceEquals(place, here)) CatchUp(record, (long)here.Milliseconds - place.Milliseconds);
        record.AsOfMs = here.Milliseconds;
        return record;
    }

    /// <summary>
    /// A record about to be read back at <paramref name="nowMs"/> on the game's clock: put forward by
    /// the time since it was taken, as 1.1's file of the place is on the one clock (the load re-arms
    /// each timer at its own time, so one that has passed fires at the first tick).
    /// </summary>
    public static LocationSave Since(LocationSave record, long nowMs)
    {
        if (record.AsOfMs > 0) CatchUp(record, nowMs - record.AsOfMs);
        record.AsOfMs = nowMs;
        return record;
    }

    /// <summary>Puts <paramref name="record"/>'s script clock forward by <paramref name="awayMs"/>.</summary>
    public static LocationSave CatchUp(LocationSave record, long awayMs)
    {
        if (awayMs <= 0) return record;
        record.ScriptClockMs += awayMs;
        foreach (var t in record.Timers) t.InMs = (int)Math.Max(0L, t.InMs - awayMs);
        return record;
    }
}
