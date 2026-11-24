// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Regions: the invisible volumes that tell a script somebody has walked in or out (the region test,
// filled at scene load by the scene load's region pass).
using System.Numerics;

namespace Vitomir.Engine.World;

/// <summary>
/// The regions of a place, and the test that fires <c>entered</c> and <c>exited</c> at their
/// scripts. **92 of the game's scripts implement <c>entered</c>**: it is how a conversation starts
/// when you walk into a room, how a trap springs and how a door knows you have arrived.
///
/// <list type="bullet">
/// <item><b>Each sector keeps a list of up to 32 regions</b> — the ones the scene file says touch
/// it (`NsdThing.Sectors`), in scene order, first empty slot first. A thirty-third is dropped
/// on the floor at load (the thing loader lays each region into its sectors as it reads
/// it, first empty slot first, and its loop stops at 0x20).</item>
/// <item><b>A thing is tested against the list of the sector it is in, whenever it is moved</b>
/// (`simThing::SetPosition` and its twin both end in the region test). Its position is taken into
/// the region's own frame and checked against the brush model's convex hull: inside when it is on
/// the inner side of every plane (<c>dot(n, p) ≤ d</c>).</item>
/// <item><b>Which regions it is in is one word on the thing</b>, a bit per slot in
/// the sector's list. Coming in sets the bit and fires <c>entered</c>; going out clears it and fires
/// <c>exited</c>.</item>
/// <item><b>Crossing into another sector carries the word over by region</b> (inlined in
/// the live sector change and the teleport placement), before the test: a region
/// the new sector also lists keeps its bit, in the new sector's slot, and one it does not list hears
/// <c>exited</c> (<see cref="Crossed"/>). So a character walking inside a large region from one
/// sector to the next stays in it and hears nothing.</item>
/// <item>A region flagged <see cref="NoTracking"/> is skipped, and a thing flagged
/// <c>THING_FLAG_NOREGIONTRIGGER</c> has its bits kept up to date but never fires either
/// callback.</item>
/// </list>
///
/// <b>Nothing is tested on a player's machine</b> (the region test's head): no bit
/// is set there, so the carry-over, which asks nothing about the network, finds none to carry, and no
/// <c>entered</c> or <c>exited</c> is heard there — the host's regions are the game's, their scripts the host's.
/// Vitomir's player's machine (a place in <c>Net.NetMode.Client</c>) is the same (<see cref="Tested"/>).
/// </summary>
public sealed partial class Regions
{
    /// <summary>The region flag that turns it off (tag 91 bit 0, "does not track entry/exit").</summary>
    public const uint NoTracking = 0x1;
    /// <summary>"Region collides with cameras" (tag 91 bit 1).</summary>
    public const uint BlocksCameras = 0x2;
    /// <summary>The most regions one sector holds (32 slots).</summary>
    public const int PerSector = 32;

    readonly Dictionary<long, WorldThing?[]> bySector = [];

    /// <summary>
    /// Somebody came into a region: the region first, then who (<c>entered(regionGuid,
    /// moverGuid, captureId)</c>).
    /// </summary>
    public Action<WorldThing, WorldThing>? Entered { get; set; }
    /// <summary>Somebody left one (<c>exited</c>).</summary>
    public Action<WorldThing, WorldThing>? Exited { get; set; }

    /// <summary>How many regions landed in any sector's list.</summary>
    public int Count { get; private set; }

    /// <summary>
    /// Lays the regions of a place into their sectors, the way the scene loader does. A region
    /// with no hull cannot contain anybody and is left out, as are sector numbers past the level's.
    /// </summary>
    public Regions(IEnumerable<WorldThing> things, int sectorCount)
    {
        foreach (var region in things)
        {
            if (region.Type != ThingType.Region || region.Scene.Model is not { Planes.Length: > 0 }) continue;
            bool placed = false;
            foreach (uint sector in region.Scene.Sectors)
            {
                if (sector >= sectorCount) continue;
                if (!bySector.TryGetValue(sector, out var slots)) bySector[sector] = slots = new WorldThing?[PerSector];
                for (int i = 0; i < PerSector; i++)
                {
                    if (slots[i] is not null) continue;
                    slots[i] = region;
                    placed = true;
                    break;
                }
            }
            if (placed) Count++;
        }
    }

    /// <summary>The regions a sector holds, in slot order (empty slots included, as the engine walks them).</summary>
    public IReadOnlyList<WorldThing?> In(long sector) =>
        bySector.TryGetValue(sector, out var slots) ? slots : [];

    /// <summary>
    /// A thing has been put somewhere, having been in sector <paramref name="from"/> before: the word
    /// is carried into its new sector (<see cref="Crossed"/>, what the sector change does
    /// on the way), and then the test — the order <c>SetPosition</c> has.
    /// </summary>
    public void Moved(WorldThing thing, long from)
    {
        Crossed(thing, from);
        Moved(thing);
    }

    /// <summary>
    /// **The carry-over** (the same loop inlined in the sector change and the teleport placement): a thing that
    /// was in sector <paramref name="from"/> is now in <c>thing.Sector</c>. Each region it was in —
    /// a set bit, read against the <em>old</em> sector's list — is looked for in the new sector's
    /// list by the region itself (a slot holds the region object): found, it keeps its bit
    /// at the new slot; not found, it hears <c>exited</c>. The word becomes the carried
    /// bits. An old sector that lists no regions leaves the word empty and says nothing.
    ///
    /// Nothing here looks at <c>THING_FLAG_NOREGIONTRIGGER</c> — only the test does — so
    /// a thing marked with it that leaves a region by crossing out of the sectors it lists still
    /// sends that <c>exited</c>. Kept. A removed region hears nothing, as in <see cref="Moved(WorldThing)"/>
    /// (ours).
    /// </summary>
    public void Crossed(WorldThing thing, long from)
    {
        long to = thing.Sector;
        if (from == to || thing.InRegions == 0) return;           // the same sector; no bits
        var old = In(from);
        var now = In(to);
        int oldCount = Used(old), newCount = Used(now);
        uint was = thing.InRegions, carried = 0;
        for (int i = 0; i < oldCount; i++)
        {
            if ((was & (1u << i)) == 0 || old[i] is not { } region) continue;
            int j = 0;
            while (j < newCount && !ReferenceEquals(now[j], region)) j++;
            if (j < newCount) carried |= 1u << j;
            else if (!region.Removed) Exited?.Invoke(region, thing);
        }
        thing.InRegions = carried;
    }

    /// <summary>How many slots of a sector's list are in use (the sector's count): they fill from the first.</summary>
    static int Used(IReadOnlyList<WorldThing?> slots)
    {
        int used = 0;
        for (int i = 0; i < slots.Count; i++) if (slots[i] is not null) used = i + 1;
        return used;
    }

    /// <summary>
    /// The test, for a thing that has just been put somewhere. Fires <see cref="Entered"/>
    /// and <see cref="Exited"/> for whatever changed. A thing that has changed sector has its word
    /// carried over first (<see cref="Moved(WorldThing, long)"/>).
    /// </summary>
    public void Moved(WorldThing thing)
    {
        if (!Tested(thing) || !bySector.TryGetValue(thing.Sector, out var slots)) return;
        int used = Used(slots);
        for (int i = 0; i < used; i++)
        {
            if (slots[i] is not { } region || region.Removed) continue;
            uint bit = 1u << (i & 31);
            if ((region.RegionFlags & NoTracking) != 0) continue;
            bool inside = Contains(region, thing.Position);
            if (!inside)
            {
                if ((thing.InRegions & bit) == 0) continue;
                thing.InRegions &= ~bit;
                if (!thing.Has(WorldThing.Flag.NoRegionTrigger)) Exited?.Invoke(region, thing);
            }
            else
            {
                if ((thing.InRegions & bit) != 0) continue;
                thing.InRegions |= bit;
                if (!thing.Has(WorldThing.Flag.NoRegionTrigger)) Entered?.Invoke(region, thing);
            }
        }
    }

    /// <summary>Whether the region test tests anything for <paramref name="thing"/>: not on a player's machine.</summary>
    public static bool Tested(WorldThing thing) => Net.NetOrders.Mode(thing.Place) != Net.NetMode.Client;

    /// <summary>
    /// Whether a point is inside a region's hull, after taking it into the region's own
    /// frame with the inverse of its matrix (a rigid inverse: the transposed rotation and
    /// the negated translation turned back).
    /// </summary>
    public static bool Contains(WorldThing region, Vector3 point)
    {
        if (region.Scene.Model is not { } model) return false;
        var local = Local(region, point);
        foreach (var plane in model.Planes)
            if (plane.X * local.X + plane.Y * local.Y + plane.Z * local.Z > plane.W) return false;
        return true;
    }

    /// <summary>A world point in a thing's own frame, by its rotation and position alone.</summary>
    public static Vector3 Local(WorldThing thing, Vector3 point)
    {
        var r = thing.Rotation;
        var d = point - thing.Position;
        // The rotation's rows are the thing's axes; the inverse of a rotation is its transpose, so
        // the local coordinates are the offset's dot product with each row.
        return new Vector3(d.X * r.M11 + d.Y * r.M12 + d.Z * r.M13,
                           d.X * r.M21 + d.Y * r.M22 + d.Z * r.M23,
                           d.X * r.M31 + d.Y * r.M32 + d.Z * r.M33);
    }
}
