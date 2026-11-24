// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// One frame of a track mover — a door, a lift, a lever, a conveyer — and where it is filed afterwards.

namespace Vitomir.Engine.World;

/// <summary>
/// **The tracker's frame, with the sector the thing is now in**. Each
/// frame a <see cref="WorldThing.MoveTrack"/> thing is moved along (<see cref="WorldThing.Advance"/>)
/// the engine files it again by where it now is:
/// <list type="bullet">
/// <item>a **straight** move re-finds the sector from the old one and, when it
/// has changed, moves the thing between the sectors' lists (the sector change, which carries its region bits
/// over, <see cref="Regions.Crossed"/>) — no surface, and no region test;</item>
/// <item>a **swing** about a pivot sets the whole matrix through `SetTransform`:
/// the same re-find and move, and then the region test, so a swinging thing
/// is heard entering and leaving regions as anything put somewhere is. SetTransform's ground ray
/// for the thing's surface is not kept: Vitomir holds a surface only for a walker, and
/// nothing is known to read a door's.</item>
/// </list>
/// Earlier, Vitomir moved the point and nothing else: a door or lift that crossed into another
/// sector stayed filed in the one it started in, so the walkers and the sweeps of its new sector
/// did not meet it and those of its old one still did.
/// </summary>
public static class TrackMoves
{
    /// <summary>
    /// Moves <paramref name="thing"/> on by <paramref name="seconds"/>, files it again, and tells
    /// <paramref name="arrived"/> when it reaches its keyframe — before the arrival's noise
    /// (<see cref="WorldThing.Advance"/>). Returns the keyframe reached, or −1.
    /// </summary>
    public static int Step(Location place, WorldThing thing, float seconds, Action<int>? arrived = null)
    {
        bool pivot = thing.Motion?.Pivot is not null;
        return thing.Advance(seconds, () => Refile(place, thing, pivot), arrived);
    }

    /// <summary>The filing after a frame's move: the same for a straight move and for a swing.</summary>
    public static void Refile(Location place, WorldThing thing, bool pivot)
    {
        if (!thing.InWorld) return;
        long from = thing.Sector, to = place.At(thing.Position, from);          // the sector search, from the hint
        if (to != from)
        {
            thing.Sector = to;
            place.Props?.Moved(thing, from, to);
            place.Regions?.Crossed(thing, from);                                  // its carry-over
        }
        if (pivot) place.Regions?.Moved(thing);                                   // SetTransform's region test
    }
}
