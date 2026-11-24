// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A place's scripts while the party is away.
using System.Runtime.CompilerServices;
using Vitomir.Engine.World;

namespace Vitomir.Scripting;

public sealed partial class CodexScenes
{
    /// <summary>When each place kept loaded was left, on the game's clock (the place's own, stopped there).</summary>
    readonly ConditionalWeakTable<Location, StrongBox<int>> leftAt = new();

    /// <summary>A place is left and kept: when, so a return knows how long the party was away.</summary>
    void LeftBehind(Location place) => leftAt.AddOrUpdate(place, new StrongBox<int>(place.Milliseconds));

    /// <summary>
    /// **A place's timers while the party is away**. 1.1 has one timer list and one clock,
    /// and every tick it takes each timer that has come due off
    /// the list and fires it **only if its script's thing is in the place the party is in** (or it
    /// has no thing, or the game is a network host) — otherwise it is thrown away unfired. So a timer
    /// set in a place the party then leaves is lost if it comes due while they are gone, and fires at
    /// its own time if they are back first. Vitomir keeps each place's scripts on a clock of their
    /// own that stops while the party is away; on the way back in it is put forward by the time away
    /// — which the place's clock tells, stopped where the party left and moved to the game's before
    /// the scripts are entered (<c>Simulation.Resume</c>) — and every timer due in that time goes,
    /// unfired. Answers the kept clock caught up.
    /// </summary>
    float CatchUp(Location place, float keptNow, List<Pending> kept)
    {
        if (!leftAt.TryGetValue(place, out var left)) return keptNow;
        leftAt.Remove(place);
        float away = Math.Max(0, place.Milliseconds - left.Value) / 1000f;
        if (away <= 0f) return keptNow;
        float back = keptNow + away;
        kept.RemoveAll(t => t.At < back);
        return back;
    }
}
