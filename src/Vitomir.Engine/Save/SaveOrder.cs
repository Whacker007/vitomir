// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The world's thing list order through a save and a load.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Save;

/// <summary>
/// **The thing list after a load is the list at the save, reversed** — 1.1's, kept.
///
/// 1.1's place file walks the world's list from its head (each thing's next) and writes
/// every thing its filter keeps, **newest first**; its load reads the records in
/// the file's order and makes each, which links it into the new world **at the
/// head**. So the first record read — the newest thing at the save — ends up at
/// the tail, and after a load the list is the one saved turned round: the **oldest** thing ticks
/// first (<see cref="Location.ListOrder"/>), until new things arrive at the head. The party is not in
/// the file (the filter leaves out a `THING_AF_PARTY` player): it comes back from `!main.nsg` without a
/// world (the party's reader makes each with none) and is linked in once the place stands, each member at
/// the head in the party's order, so after a load the party is the newest,
/// its last member first. Things made at run time are in the file like any other and come back in
/// their place in the list, and what the filter leaves out — projectiles, explosions, and what is worn,
/// which comes back with its wearer — is not in the order either.
///
/// Vitomir keeps the order in the `.vsave` (<see cref="LocationSave.ListOrder"/>, head first) and the
/// load links the things back in by it (<see cref="Location.Relink"/>). A save written before the
/// field was kept has none, and loads as it always did: the scene's things in the scene's order, and
/// what was made after them.
/// </summary>
public static class SaveOrder
{
    /// <summary>
    /// The order a place's things are written in (ours): the placed things by the scene's order, then
    /// the things made here by the count that names them, then anything else as it came. A file is a
    /// record by id and the list's order is <see cref="LocationSave.ListOrder"/>, so the records keep one
    /// order however the list has turned — and a game saved again after a load reads as it did.
    /// </summary>
    public static List<WorldThing> Filed(Location place, IEnumerable<WorldThing> kept)
    {
        (int Kind, int N) Key(WorldThing t) =>
            ThingIds.Placed(place, t) is not null ? (0, t.Index)
            : ThingIds.Of(place, t) is { } id && ThingIds.ParseMade(id) is { } made ? (1, made.N)
            : (2, 0);
        return [.. kept.Select((t, i) => (t, i)).OrderBy(x => Key(x.t)).ThenBy(x => x.i).Select(x => x.t)];
    }

    /// <summary>The ids of the things <paramref name="save"/> holds, in the world's list order (head first).</summary>
    public static List<string> Of(Location place, IReadOnlyDictionary<WorldThing, string> ids, LocationSave save)
    {
        var order = new List<string>();
        foreach (var t in place.ListOrder)
            if (ids.TryGetValue(t, out var id) && save.Things.ContainsKey(id)) order.Add(id);
        return order;
    }

    /// <summary>
    /// Links the things the load made back into the list in the saved order, each at the head as
    /// 1.1 reads it (<see cref="Location.Relink"/>); nothing, for a save with no order.
    /// </summary>
    public static void Apply(Location place, LocationSave save, IReadOnlyDictionary<string, WorldThing> byId)
    {
        if (save.ListOrder is not { } order) return;
        var read = new List<WorldThing>(order.Count);
        foreach (var id in order)
            if (byId.TryGetValue(id, out var t) && t.InWorld) read.Add(t);
        place.Relink(read);
    }
}
