// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Formats.L2;

namespace Vitomir.Engine.Levels.Cells;

/// <summary>
/// **Ours: which area of a cell level the party is in**. The
/// party's area is the one its leader stands in — the manifest's <see cref="L2Area"/> boxes tested against
/// the leader's position each step — and a change raises <see cref="Left"/> and then <see cref="Entered"/>,
/// which the area's sound, the log and (next) the scripts hear. 1.1 has nothing like it: there a room is a
/// level, and "which place is this" changes only at a level change.
///
/// The edge has **hysteresis**, so that somebody standing in a doorway does not flap between two places:
/// the party is in once the leader is <see cref="EnterDepth"/> inside the box, and out again only once the
/// leader is <see cref="LeaveDepth"/> outside it.
/// </summary>
public sealed class CellAreas
{
    /// <summary>How far inside an area's box the leader must be before the party is in it, metres (ours).</summary>
    public const float EnterDepth = 0.30f;
    /// <summary>How far outside an area's box the leader must be before the party has left it, metres (ours).</summary>
    public const float LeaveDepth = 0.30f;
    /// <summary>The height above the leader's feet the test is made at, metres (ours: the body's middle, so a floor at the box's bottom face does not count as its edge).</summary>
    public const float BodyHeight = 1.0f;

    readonly IReadOnlyList<L2Area> areas;
    readonly float unitsPerMetre;
    bool looked;

    public CellAreas(IReadOnlyList<L2Area>? areas, float engineUnitsPerMetre = L2Manifest.EngineUnitsPerMetre)
    {
        this.areas = areas ?? [];
        unitsPerMetre = engineUnitsPerMetre;
    }

    /// <summary>Every area of the level.</summary>
    public IReadOnlyList<L2Area> All => areas;

    /// <summary>The area the party is in, or null in the open (the level's own place).</summary>
    public L2Area? Current { get; private set; }

    /// <summary>The party has come into an area (after <see cref="Left"/> for the one it was in, if any).</summary>
    public event Action<L2Area>? Entered;
    /// <summary>The party has gone out of an area.</summary>
    public event Action<L2Area>? Left;

    /// <summary>
    /// Forgets where the party was, raising nothing: the next <see cref="Update"/> looks afresh — for an
    /// arrival through an exit, which may stand the party anywhere.
    /// </summary>
    public void Reset() { Current = null; looked = false; }

    /// <summary>
    /// The leader is at <paramref name="feet"/> (engine units): the party's area is worked out, the events
    /// raised when it changed, and the area answered.
    /// </summary>
    public L2Area? Update(Vector3 feet)
    {
        if (areas.Count == 0) return null;
        var point = feet / unitsPerMetre + new Vector3(0, 0, BodyHeight);
        var now = At(areas, Current, point, first: !looked);
        looked = true;
        if (ReferenceEquals(now, Current)) return now;
        var was = Current;
        Current = now;
        if (was is not null) Left?.Invoke(was);
        if (now is not null) Entered?.Invoke(now);
        return now;
    }

    /// <summary>
    /// The area a body at <paramref name="point"/> (metres, the level's frame) is in, having been in
    /// <paramref name="current"/>: it stays in that one until it is <see cref="LeaveDepth"/> outside it, and
    /// is in another only <see cref="EnterDepth"/> inside it — the smallest, when one box is inside another. The
    /// <paramref name="first"/> look, with nowhere to have come from, takes any box it is inside at all.
    /// </summary>
    public static L2Area? At(IReadOnlyList<L2Area> areas, L2Area? current, Vector3 point, bool first = false)
    {
        if (current is not null && current.Depth(point) > -LeaveDepth) return current;
        L2Area? best = null;
        float need = first ? 0f : EnterDepth, smallest = float.MaxValue;
        foreach (var a in areas)
        {
            float volume = a.Box.Size[0] * a.Box.Size[1] * a.Box.Size[2];
            if (a.Depth(point) < need || volume >= smallest) continue;
            best = a;
            smallest = volume;
        }
        return best;
    }
}
