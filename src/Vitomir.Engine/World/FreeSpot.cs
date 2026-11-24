// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The move order's "is somebody standing there" — read.
using System.Numerics;

namespace Vitomir.Engine.World;

/// <summary>
/// **The move order's free-spot test** (`rpgAction::MoveTo`): unless the
/// caller set flag <c>0x100</c>, a body query is made at the order's point with the walker's own
/// shape, and the order is **refused (1) when the nearest thing it meets is not the walker itself**
/// (the closest hit not yet taken). It comes **before** the clamp,
/// before the cancel and before the walker is touched, so a refused walk leaves the
/// character exactly as it was: still walking its last route, still in its action.
///
/// The query (a collision routine nothing obvious reaches; read)
/// takes the walker's shape — a sphere or a cylinder, anything else meets
/// nothing — and radius (and the height), gathers the
/// sectors round the point, and walks every thing in them, testing each by its own
/// shape: a sphere, a cylinder or a model's mesh, each hit
/// recorded with its distance. The move order sets two filters on the query first:
/// **the type mask 2** (things of type 1, plain items, are passed over — a dropped weapon,
/// type 5, is not) and **flag 4** (a dead character or player is passed over);
/// and the query's filter always passes over a thing with flag <c>0x200</c> (DISABLECOLLIDE).
/// The walker itself is not passed over — which is why the order asks whether the nearest hit is it.
/// The geometry is the walker's own body query's (<see cref="Bodies.Hit"/>).
/// </summary>
public static class FreeSpot
{
    /// <summary>
    /// The thing standing at <paramref name="point"/> that refuses a move order there, or null when
    /// the spot is free or the nearest body there is the walker's own.
    /// </summary>
    public static WorldThing? Taken(Player walker, Vector3 point)
    {
        var mover = walker.Thing;
        int sector = walker.Solid?.SectorAt(point with { Z = point.Z + Collision.Body[0] }, walker.Sector) ?? walker.Sector;
        var nearest = walker.Props?.Occupied(mover, point, sector);
        return nearest is null || ReferenceEquals(nearest, mover) ? null : nearest;
    }

    /// <summary>
    /// **The follow's own question**: does the walker fit at
    /// <paramref name="point"/> — nothing but itself touching its body there, the dead, the items and
    /// the <c>NOBLOCK</c> things counted, the flood started from the walker's own sector
    /// (<see cref="Bodies.Touches"/>). Stricter than the move order's test, which passes over plain
    /// items and the dead; asked first, so a slot with a body lying in it is skipped.
    /// </summary>
    public static bool Fits(Player walker, Vector3 point) =>
        walker.Props?.Touches(walker.Thing, point, walker.Sector) != true;

    /// <summary>
    /// The query's two filters from the move order and the filter's own: a plain
    /// item (type 1), a dead character or player, and a DISABLECOLLIDE thing are not met.
    /// </summary>
    public static bool Passed(WorldThing thing) =>
        thing.Type == ThingType.Item
        || (thing.Type is ThingType.Actor or ThingType.Player && thing.Actor?.Has(Actor.Flag.Dead) == true)
        || thing.Has(WorldThing.Flag.DisableCollide);
}
