// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The surface the approach found on its target, kept on the target, read
// in a comparison with 1.1 and built.
using System.Numerics;
using System.Runtime.CompilerServices;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// **The approach remembers where it found ground for a thing.** The approach starts from the target's
/// own surface pointer; only when that is null does it run its ladder
/// (<see cref="Actions.Toward"/>) — and whatever surface the ladder finds, **it writes back onto the
/// target**. Every later approach to that thing — the take's retry every 150 ms, the next
/// click, another character's walk to it — therefore takes the first rung: the cached surface, and a
/// point on it at **the target's own x and y** (the height from the surface's plane there).
///
/// So for a purse on a table, served by the ladder's later rungs, the first approach aims at the
/// backed-off point on the floor and every one after it aims **under the purse**, on that floor, and
/// the walker's collision stops the character at the table's edge. For a thing standing on its own
/// ground the two are the same.
///
/// <para>The pointer is the thing's own surface: 1.1 clears it when the thing leaves the world
/// and its physics step keeps it for a thing that moves. A character's is kept by its own
/// walking, which Vitomir's search from its position already answers, so only a thing that is not a
/// character is kept here, and — ours — the entry is dropped when the thing is no longer where it was
/// when it was kept, which is what the engine's own upkeep of the pointer comes to.</para>
/// </summary>
public static class ApproachSurface
{
    sealed record Entry(WalkMap Map, int Node, Vector3 At);

    static readonly ConditionalWeakTable<WorldThing, Entry> kept = new();

    /// <summary>
    /// The cached surface's answer for <paramref name="target"/>: the target's own x and y on the
    /// kept node at that node's height there — or null when nothing is kept for it on this map.
    /// </summary>
    public static (Vector3 To, int Node)? Cached(WorldThing target, WalkMap map)
    {
        if (!kept.TryGetValue(target, out var e)) return null;
        if (!ReferenceEquals(e.Map, map) || e.At != target.Position || !map.IsGround(e.Node))
        {
            kept.Remove(target);                                                  // ours: it has moved
            return null;
        }
        var p = target.Position;
        return (new Vector3(p.X, p.Y, map.Nodes[e.Node].Height(p.X, p.Y)), e.Node);
    }

    /// <summary>Keeps the surface an approach found on a thing that is not a character.</summary>
    public static void Keep(WorldThing target, WalkMap map, int node)
    {
        if (node < 0 || target.Type is ThingType.Actor or ThingType.Player) return;
        kept.AddOrUpdate(target, new Entry(map, node, target.Position));
    }
}
