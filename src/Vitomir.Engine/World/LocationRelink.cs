// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The world's thing list as a load leaves it: each thing read linked back in at the head.
namespace Vitomir.Engine.World;

public sealed partial class Location
{
    /// <summary>
    /// **Links <paramref name="read"/> into the list as 1.1's load does**: one after another, each
    /// at the head, so the first read ends up the oldest and the
    /// last read the newest (<see cref="ListOrder"/> is <paramref name="read"/> reversed). 1.1's
    /// list is empty when its load begins; Vitomir's already holds the place as the scene built it,
    /// so anything here that <paramref name="read"/> does not name — the place's own player, which
    /// the party's load takes over, and anything the load entered of its own accord — keeps its
    /// order and stays newer than everything read, as the party is in 1.1, linked in after the
    /// place. A thing named that is not in the world is passed over.
    /// </summary>
    public void Relink(IEnumerable<WorldThing> read)
    {
        var order = new List<WorldThing>(things.Count);
        var seen = new HashSet<WorldThing>(ReferenceEqualityComparer.Instance);
        var here = new HashSet<WorldThing>(things, ReferenceEqualityComparer.Instance);
        foreach (var t in read)
            if (here.Contains(t) && seen.Add(t)) order.Add(t);
        foreach (var t in things)
            if (!seen.Contains(t)) order.Add(t);
        things.Clear();
        things.AddRange(order);
    }
}
