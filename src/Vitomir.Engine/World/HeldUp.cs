// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Engine.World;

/// <summary>
/// **A walker held up in its walk scuffs** — the walker's wait, the wait the walker's step
/// calls when something is in the way: 300 ms for a person, 500 for anything else, and
/// 100 after a detour's end. Begun while the walk bit is set, the wait says `stopmove` and the
/// floor's scuff (then the surface's sound offset by 46) exactly as the
/// walker's stop does — but the walk bit stays set, so when the wait runs out the leg
/// starts again without a `startmove` (`StartWalk` plays it only when the bit was clear). Found by
/// A comparison with 1.1; until then Vitomir's held-up walker was silent, because
/// <see cref="Player.Walking"/> stays true through a wait and the footfalls listen for its edges.
/// </summary>
public static class HeldUp
{
    /// <summary>
    /// Whether <paramref name="thing"/>'s walker has been held up since the footfalls last asked:
    /// the steered character's walker, a mind's, or the one a script gave a thing with no mind.
    /// </summary>
    public static bool Heard(Location place, WorldThing thing)
    {
        var walker = place.Player is { } p && ReferenceEquals(p.Thing, thing) ? p : thing.Mind?.Walker ?? thing.Walker;
        if (walker is null || walker.HoldsHeard == walker.Holds) return false;
        walker.HoldsHeard = walker.Holds;
        return true;
    }
}
