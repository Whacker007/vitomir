// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The pieces a scripted scene is made of, against the monastery's own data: its regions, its
/// conversation starts, the experience its first conversation hands out, and the render flags that
/// keep the buried Cappadocians out of sight until they rise.
/// </summary>
public class StoryTests
{
    static (Location Where, GameFiles Files) Monastery()
    {
        var dir = GameFiles.Locate();
        Assert.SkipWhen(dir is null, "no game install");
        var files = new GameFiles(dir!);
        var set = ChronicleSet.Load(files);
        var where = set.ForLevel("P1_MON1")!.Value;
        return (Location.Load(files, "P1_MON1", sky: false, scene: where.Entry!.SceneName, player: "Christof"), files);
    }

    /// <summary>
    /// Walking into a region fires <c>entered</c> once, standing in it fires nothing more, and
    /// walking out fires <c>exited</c> once — which is how every "walk into the room and the
    /// conversation starts" in the game works.
    /// </summary>
    [Fact]
    public void A_region_fires_once_on_the_way_in_and_once_on_the_way_out()
    {
        var (place, files) = Monastery();
        using var _ = files;
        Assert.NotNull(place.Regions);
        Assert.True(place.Regions!.Count > 0);
        var christof = place.Player!.Thing;
        var home = christof.Position;

        // A region that holds its own origin, so there is somewhere known to stand in it.
        var region = place.Things.First(t => t.Type == ThingType.Region && (t.RegionFlags & Regions.NoTracking) == 0
                                             && t.Scene.Model is { Planes.Length: > 0 }
                                             && Regions.Contains(t, t.Position));
        var seen = new List<string>();
        place.Regions.Entered = (r, who) => seen.Add($"in {r.Guid} {who.Guid}");
        place.Regions.Exited = (r, who) => seen.Add($"out {r.Guid} {who.Guid}");

        place.Move(christof, region.Position);
        place.Move(christof, region.Position + new Vector3(0.5f, 0, 0));
        Assert.Equal([$"in {region.Guid} {christof.Guid}"], seen);

        // Out again, without leaving the sector (the next two tests cross one).
        long sector = christof.Sector;
        var outside = Enumerable.Range(1, 40).SelectMany(r => new[]
            {
                new Vector3(25f * r, 0, 0), new Vector3(-25f * r, 0, 0),
                new Vector3(0, 25f * r, 0), new Vector3(0, -25f * r, 0),
            })
            .Select(d => region.Position + d)
            .First(p => place.At(p, sector) == sector && !Regions.Contains(region, p));
        place.Move(christof, outside);
        Assert.Equal([$"in {region.Guid} {christof.Guid}", $"out {region.Guid} {christof.Guid}"], seen);
        place.Move(christof, home);
    }

    /// <summary>
    /// **The word is carried over by region** (in the sector change).
    /// Which regions a thing is in is a bit per slot of its sector's list; crossing into another
    /// sector looks each region it was in up in the new sector's list by the region itself. Leave the
    /// sector from inside a region and walk back to the start: the region left hears its one
    /// <c>exited</c>, and the start sector's region that sits in the same slot — one Christof was never
    /// in — hears nothing. (Earlier, Vitomir read the old bits against the new list, and it was the
    /// other way round.)
    /// </summary>
    [Fact]
    public void Leaving_a_sector_from_inside_a_region_exits_that_region_and_no_other()
    {
        var (place, files) = Monastery();
        using var _ = files;
        var christof = place.Player!.Thing;
        var home = christof.Position;
        long homeSector = christof.Sector;
        // A region, and the slot it sits in, in a sector other than the one Christof starts in —
        // where the start sector's list has a different region in that same slot, which it is not in.
        foreach (var region in place.Things.Where(t => t.Type == ThingType.Region
                     && (t.RegionFlags & Regions.NoTracking) == 0 && t.Scene.Model is { Planes.Length: > 0 }
                     && Regions.Contains(t, t.Position)))
        {
            long there = place.At(region.Position, homeSector);
            if (there == homeSector) continue;
            int slot = place.Regions!.In(there).ToList().IndexOf(region);
            var homeList = place.Regions.In(homeSector);
            if (slot < 0 || slot >= homeList.Count || homeList[slot] is not { } other || ReferenceEquals(other, region)
                || Regions.Contains(other, home) || (other.RegionFlags & Regions.NoTracking) != 0) continue;

            var seen = new List<string>();
            place.Regions.Entered = (r, _) => seen.Add($"in {r.Guid}");
            place.Regions.Exited = (r, _) => seen.Add($"out {r.Guid}");
            place.Move(christof, region.Position);
            place.Move(christof, home);
            Assert.Equal($"in {region.Guid}", seen[0]);
            Assert.Single(seen, $"out {region.Guid}");
            Assert.DoesNotContain($"out {other.Guid}", seen);
            return;
        }
        Assert.Skip("no region in the monastery sits where the old quirk could be shown from the start");
    }

    /// <summary>
    /// A character moving about inside one region from a sector that lists it into another that
    /// lists it too stays in it: no <c>exited</c>, no second <c>entered</c>, and its bit now at the
    /// region's slot in the new sector's list. This is the case a large room spanning
    /// several sectors meets every time somebody crosses it.
    /// </summary>
    [Fact]
    public void Crossing_sectors_inside_a_region_both_list_says_nothing()
    {
        var (place, files) = Monastery();
        using var _ = files;
        var christof = place.Player!.Thing;
        var home = christof.Position;
        var regions = place.Regions!;
        foreach (var region in place.Things.Where(t => t.Type == ThingType.Region
                     && (t.RegionFlags & Regions.NoTracking) == 0 && t.Scene.Model is { Planes.Length: > 0 }
                     && t.Scene.Sectors.Length > 1))
        {
            var inside = PointsIn(region).ToList();
            if (inside.Count < 2) continue;
            regions.Entered = null;
            regions.Exited = null;
            place.Move(christof, inside[0]);
            long first = christof.Sector;
            if (!regions.In(first).Contains(region)) continue;
            foreach (var p in inside.Skip(1))
            {
                long next = place.At(p, first);
                if (next == first || !regions.In(next).Contains(region)) continue;
                var seen = new List<string>();
                regions.Entered = (r, _) => seen.Add($"in {r.Guid}");
                regions.Exited = (r, _) => seen.Add($"out {r.Guid}");
                place.Move(christof, p);
                Assert.Equal(next, christof.Sector);
                Assert.DoesNotContain($"in {region.Guid}", seen);
                Assert.DoesNotContain($"out {region.Guid}", seen);
                int slot = regions.In(next).ToList().IndexOf(region);
                Assert.NotEqual(0u, christof.InRegions & (1u << slot));
                regions.Entered = null;
                regions.Exited = null;
                place.Move(christof, home);
                return;
            }
        }
        Assert.Skip("no region in the monastery spans two sectors where Christof can be put");
    }

    /// <summary>Points inside a region's hull, on a grid over its model's bounds, in the world.</summary>
    static IEnumerable<Vector3> PointsIn(WorldThing region)
    {
        var model = region.Scene.Model!;
        var r = region.Rotation;
        var min = model.BoundsMin;
        var size = model.BoundsMax - min;
        const int n = 8;
        for (int k = 1; k < 4; k++)
            for (int i = 1; i < n; i++)
                for (int j = 1; j < n; j++)
                {
                    var l = min + size * new Vector3(i / (float)n, j / (float)n, k / 4f);
                    var w = region.Position + l.X * new Vector3(r.M11, r.M12, r.M13)
                                            + l.Y * new Vector3(r.M21, r.M22, r.M23)
                                            + l.Z * new Vector3(r.M31, r.M32, r.M33);
                    if (Regions.Contains(region, w)) yield return w;
                }
    }

    /// <summary>
    /// THING_FLAG_NOREGIONTRIGGER keeps a thing's bits up to date and fires nothing — so it does
    /// not "enter" a region it is already in the moment the flag comes off.
    /// </summary>
    [Fact]
    public void A_thing_marked_no_region_trigger_is_tracked_but_never_heard()
    {
        var (place, files) = Monastery();
        using var _ = files;
        var christof = place.Player!.Thing;
        var region = place.Things.First(t => t.Type == ThingType.Region && (t.RegionFlags & Regions.NoTracking) == 0
                                             && t.Scene.Model is { Planes.Length: > 0 }
                                             && Regions.Contains(t, t.Position));
        int heard = 0;
        place.Regions!.Entered = (_, _) => heard++;
        christof.Flags |= WorldThing.Flag.NoRegionTrigger;
        place.Move(christof, region.Position);
        Assert.Equal(0, heard);
        Assert.NotEqual(0u, christof.InRegions);
        christof.Flags &= ~WorldThing.Flag.NoRegionTrigger;
        place.Move(christof, region.Position + new Vector3(0.5f, 0, 0));
        Assert.Equal(0, heard);
    }

    /// <summary>
    /// <c>TeleportPartyToConversation</c>: the leader on the group's first unnumbered start, the
    /// next member on the second, each turned the way its start faces. The monastery's group 0 is
    /// the opening room — Christof at (91, 298), facing +Y.
    /// </summary>
    [Fact]
    public void The_party_is_stood_on_the_conversations_own_starts()
    {
        var (place, files) = Monastery();
        using var _ = files;
        var christof = place.Player!.Thing;
        var wilhem = place.Place("wilhem", christof.Position + new Vector3(96, 0, 0), christof.Orientation, christof.Sector)!;
        wilhem.Actor!.Flags |= Actor.Flag.Party;

        Assert.True(place.TeleportPartyToConversation(0));
        var starts = place.ConversationStarts.Where(s => s.Group == 0).ToArray();
        Assert.Equal(starts[0].Position, christof.Position);
        Assert.Equal(starts[1].Position, wilhem.Position);
        Assert.Equal(0f, christof.Orientation.Z, 3);
        // A group with no start for somebody refuses — and the scene has no group 9.
        Assert.False(place.TeleportPartyToConversation(9));
    }

    /// <summary>
    /// The party's move cancels each member's order **unforced** (the cancel with (0, 0), read in
    /// A comparison with 1.1): a member kept busy — a swing running out — keeps it; a free one has it
    /// cancelled. Vitomir cleared every member's action outright.
    /// </summary>
    [Fact]
    public void The_partys_move_leaves_a_busy_member_its_order()
    {
        var (place, files) = Monastery();
        using var _ = files;
        var christof = place.Player!.Thing;
        var wilhem = place.Place("wilhem", christof.Position + new Vector3(96, 0, 0), christof.Orientation, christof.Sector)!;
        wilhem.Actor!.Flags |= Actor.Flag.Party;
        foreach (var who in new[] { christof, wilhem }) { who.Actor!.Action = Rpg.ActionMode.Swing; who.Actor.Target = null; }
        wilhem.Actor.BusyUntil = place.Milliseconds + 5_000;

        Assert.True(place.TeleportPartyToConversation(0));
        Assert.Equal(Rpg.ActionMode.None, christof.Actor!.Action);
        Assert.Equal(Rpg.ActionMode.Swing, wilhem.Actor.Action);
    }

    /// <summary>
    /// Experience goes onto both the pool and the lifetime total; crossing a rank's threshold runs
    /// the rank-up once, for the rank reached — two ranks at once give only the second's bonus.
    /// A kindred's blood pool and health go up by the table's numbers, and the blood is filled.
    /// </summary>
    [Fact]
    public void Experience_that_crosses_a_rank_gives_that_ranks_bonus_once()
    {
        var actor = new Actor { Type = ActorType.Vampire };
        actor.Stat[(int)ActorStat.BloodPool] = actor.SetBaseStat((int)ActorStat.BloodPool, 100);
        actor.Stat[(int)ActorStat.Blood] = actor.SetBaseStat((int)ActorStat.Blood, 40);
        actor.Health = actor.MaxHealth = 100;
        actor.Health = 50;
        var thing = new WorldThing
        {
            Index = 0, Guid = 1, Scene = new NsdThing(), Template = "christof", Type = ThingType.Player, Actor = actor,
        };
        var ranks = new List<int>();

        Experience.Award(thing, 50, (_, r) => ranks.Add(r));
        Assert.Equal(50, actor.Experience);
        Assert.Equal(50, actor.SpendableExperience);
        Assert.Empty(ranks);

        // 50 + 9,960 = 10,010: past rank 1 (5,000) and rank 2 (10,000) in one award.
        Experience.Award(thing, 9_960, (_, r) => ranks.Add(r));
        Assert.Equal([2], ranks);
        var (pool, health) = Experience.RankBonus[2, (int)ActorType.Vampire];
        Assert.Equal(100 + pool, actor.Stat[(int)ActorStat.BloodPool], 3);
        Assert.Equal(100 + pool, actor.Stat[(int)ActorStat.Blood], 3);
        Assert.Equal(100 + health, actor.MaxHealth, 3);
        Assert.Equal(50 + health, actor.Health, 3);
    }

    /// <summary>
    /// Tag 8 is the render flags the scripts reach and tag 10 is the model's
    /// lighting flags. The buried Cappadocians of the monastery's second scene carry DON'T RENDER
    /// from their template, and their script clears it when they rise — so until then the draw
    /// leaves them out.
    /// </summary>
    [Fact]
    public void The_buried_cappadocians_start_out_of_sight()
    {
        var dir = GameFiles.Locate();
        Assert.SkipWhen(dir is null, "no game install");
        using var files = new GameFiles(dir!);
        var set = ChronicleSet.Load(files);
        var place = Location.Load(files, "P1_MON2", sky: false, scene: set.ForLevel("P1_MON2")?.Entry?.SceneName);
        var buried = place.Things.Where(t => t.Template == "cappadocian_buried").ToArray();
        Assert.NotEmpty(buried);
        Assert.All(buried, t => Assert.True(t.ModelHidden));
        Assert.All(buried, t => Assert.Equal(0xeu, t.RenderFlags));
        Assert.False(place.Player?.Thing.ModelHidden ?? false);
    }
}
