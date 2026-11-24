// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The sector a thing is in**, found as `simThing::SetPosition` finds it:
/// The sector search on the thing's own position — its feet — from the sector it was in. It decides which
/// lights a thing is lit from (the picker starts with its own sector's), and until then
/// Vitomir's walker took the sector of a point 16 units up. In the monastery's corridors the sectors
/// part 4 units over the floor, so Christof and Wilhem stood in the sectors above — 172 for Wilhem, a
/// sector with no surfaces and no lights — and Wilhem was drawn black where 1.1 lights him (as
/// screenshots of an SP4 save show). 1.1's saves record every party member's sector, and they
/// are the sectors of the feet.
/// </summary>
public class SectorSearchTests
{
    /// <summary>A sector bounded only across x, from <paramref name="lo"/> to <paramref name="hi"/> (normals inward).</summary>
    static NilSector Slab(float lo, float hi) => new()
    {
        SectorPlanes = [new NilPlane(Vector3.UnitX, lo), new NilPlane(-Vector3.UnitX, -hi)],
    };

    [Fact]
    public void The_search_keeps_the_hint_then_takes_the_first_in_order_and_never_answers_nowhere()
    {
        var level = new NilLevel { Sectors = [Slab(0, 10), Slab(5, 15)] };
        var both = new Vector3(7, 0, 0);
        Assert.Equal(1, SectorSearch.Find(level, both, 1));            // the hint holds it: kept
        Assert.Equal(0, SectorSearch.Find(level, both, -1));           // no hint: the first in order
        Assert.Equal(1, SectorSearch.Find(level, new Vector3(12, 0, 0), 0));
        Assert.Equal(0, SectorSearch.Find(level, new Vector3(20, 0, 0), 0));   // in none: the hint again
        Assert.Equal(-1, SectorSearch.Find(level, new Vector3(20, 0, 0), -1));

        // The plane test is strict and has no slack: on a plane is in, past it by any amount is out.
        Assert.True(SectorSearch.Holds(level.Sectors[0], new Vector3(10, 0, 0)));
        Assert.False(SectorSearch.Holds(level.Sectors[0], new Vector3(10.001f, 0, 0)));
        Assert.Equal(1, SectorSearch.Find(level, new Vector3(10.001f, 0, 0), 0));
        // A sector with no boundary planes holds every point.
        Assert.True(SectorSearch.Holds(new NilSector(), new Vector3(1e6f)));
    }

    /// <summary>Wilhem's place in 1.1's save SP4 (Monastery 1, 01:28:05), and Christof's.</summary>
    static readonly Vector3 Wilhem = new(-915.8151f, -610.0678f, -252f), Christof = new(-825.8553f, -589.8866f, -252f);

    [Fact]
    public void In_the_monastery_corridor_the_feet_are_in_the_lit_sector_below_and_16_up_in_a_dark_one()
    {
        using var files = Install.Open();
        var level = NilLevel.Read(files.Read("levels/P1_MON1.nil"), "P1_MON1");
        var solid = new Collision(level);
        Assert.Equal(44, SectorSearch.Find(level, Wilhem, -1));
        Assert.Equal(43, SectorSearch.Find(level, Christof, -1));
        // Where an earlier walker looked: the sectors above, and Wilhem's has nothing to light him with.
        Assert.Equal(172, solid.SectorAt(Wilhem with { Z = Wilhem.Z + 16f }));
        Assert.Equal(169, solid.SectorAt(Christof with { Z = Christof.Z + 16f }));
        Assert.Empty(level.Sectors[172].Surfaces);
        Assert.Empty(level.Sectors[172].Lights);
        Assert.Equal(14, level.Sectors[44].Lights.Length);
    }

    [Fact]
    public void A_walker_stands_in_the_sector_of_its_feet()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var walker = place.Player!;
        walker.Thing.Position = Wilhem;
        Assert.True(walker.Ground());
        Assert.Equal(44, walker.Sector);
        Assert.Equal(44L, walker.Thing.Sector);

        // And every step keeps it so (the position's set on each): a walk along the corridor ends in the
        // sector of wherever the feet are.
        Assert.True(walker.WalkTo(Christof, place.Walk!.Find(Christof, WalkMap.Reach), run: false));
        for (int i = 0; i < 100 && walker.Walking; i++) walker.Follow(0.05f);
        Assert.True(Vector3.Distance(walker.Thing.Position, Christof) < 16f, $"the walk ended at {walker.Thing.Position}");
        Assert.Equal(43L, walker.Thing.Sector);
        Assert.Equal(SectorSearch.Find(place.Level, walker.Thing.Position, -1), (int)walker.Thing.Sector);
    }

    [Fact]
    public void Every_party_member_1_1_saved_is_in_the_sector_of_his_feet()
    {
        // 1.1 writes each thing's sector into the save (as the sector's index in the world's array).
        // The saves are the player's, so this runs against whatever the install has.
        using var files = Install.Open();
        string dir = Path.Combine(files.Root, "SaveGames");
        Assert.SkipUnless(Directory.Exists(dir), $"no SaveGames folder in {files.Root}");
        var slots = Directory.EnumerateDirectories(dir).Where(d => File.Exists(Path.Combine(d, "!main.nsg"))).ToArray();
        int checkedMembers = 0;
        foreach (var slot in slots)
        {
            var nsg = NsgSave.ReadFile(Path.Combine(slot, "!main.nsg"));
            foreach (var p in nsg.Party.Players)
            {
                // Which level a member's place is: the save's own chronicle says (a place's number is the
                // chronicle's — London's 3 is not Prague's 3, which the first version of this test, taking
                // it from any slot's `.nsw`, found out when the slots held three cities).
                if (p.Sector < 0 || p.World >= nsg.Chronicle.Locations.Length) continue;
                string levelName = Path.GetFileNameWithoutExtension(nsg.Chronicle.Locations[p.World].Level);
                if (!files.TryRead($"levels/{levelName}.nil", out var nil)) continue;
                var level = NilLevel.Read(nil, levelName);
                var feet = new Vector3(BitConverter.ToSingle(p.Transform, 36), BitConverter.ToSingle(p.Transform, 40),
                                       BitConverter.ToSingle(p.Transform, 44));
                Assert.True(SectorSearch.Find(level, feet, -1) == p.Sector,
                            $"{Path.GetFileName(slot)} {p.Name} at {feet} in {levelName}: 1.1 says sector {p.Sector}, the feet are in {SectorSearch.Find(level, feet, -1)}");
                checkedMembers++;
            }
        }
        TestContext.Current.SendDiagnosticMessage($"{checkedMembers} party members checked");
        Assert.SkipWhen(checkedMembers == 0, "no 1.1 save with a party member in a level of the install");
    }
}
