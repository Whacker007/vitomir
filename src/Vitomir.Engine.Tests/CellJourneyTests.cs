// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Levels.Cells;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;
using Vitomir.Formats.L2;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **A journey through a cell level** (ours), with the game's own
/// chronicle and loader: a small cooked level — a 30 m floor with a room's box on it — is played as Prague's
/// <c>OldTownDay</c>. Christof leaves it by exit 10 into 1.1's inn (<c>P1_IN4D</c>) and comes back through
/// the inn's door onto the cell level's starts for entrance 10, facing as they face; arriving from the inn
/// in a game that has never built the cell level stands him on the same spot; exit 9, whose place is a room
/// of the map, walks him in through the door with no journey, and the threshold is crossed in and out with
/// 1.1's numbers for the same level change; the gate's volume asks for the East Gate when walked into; and
/// the cell level let go as the seventh place loaded stops its stream and is built again from its record.
/// The level is generated here; the inn and the chronicle are read from the install.
/// </summary>
[Collection(LevelCooksCollection.Name)]
public class CellJourneyTests
{
    const float M = L2Manifest.EngineUnitsPerMetre;

    /// <summary>The four starts of the cell level's entrance 10 (metres), and the way they face.</summary>
    static readonly Vector2[] InnDoor = [new(5, 5), new(6, 5), new(5, 6), new(6, 6)];
    const float InnDoorYaw = -135f;

    static L2Anchor Start(string level, int index, uint group, Vector2 at, float yaw) => new()
    {
        Id = $"{level}/starts/{index}", Kind = "start", Scene = level, Index = index, Name = $"playerstart{group:00}",
        Position = [at.X, at.Y, 1], Rotation = [0, 0, yaw], Group = group,
    };

    /// <summary>Cooks the level: a floor from −15 to 15 m of 1 m squares, starts for entrances 0, 9 and 10, the room behind exit 9, a gate's volume for exit 3.</summary>
    static (string Root, string Package) Cook(string name)
    {
        var v = new List<Vector3>();
        var idx = new List<int>();
        for (int y = -15; y < 15; y++)
            for (int x = -15; x < 15; x++)
            {
                int b = v.Count;
                v.AddRange([new Vector3(x, y, 0) * M, new Vector3(x + 1, y, 0) * M, new Vector3(x + 1, y + 1, 0) * M, new Vector3(x, y + 1, 0) * M]);
                idx.AddRange([b, b + 1, b + 2, b, b + 2, b + 3]);
            }
        var anchors = new List<L2Anchor> { Start(name, 0, 0, new(-5, 0), -90) };
        foreach (var p in new Vector2[] { new(4, -7), new(4, -6), new(3, -7), new(3, -6) }) anchors.Add(Start(name, anchors.Count, 9, p, -90));
        foreach (var p in InnDoor) anchors.Add(Start(name, anchors.Count, 10, p, InnDoorYaw));
        var root = Path.Combine(Path.GetTempPath(), "vitomir-celljourney-" + Guid.NewGuid().ToString("N"));
        var result = CellCook.Cook(new CellCookInput
        {
            Name = name, Vertices = [.. v], Indices = [.. idx], TriangleMaterials = [.. idx.Where((_, i) => i % 3 == 0).Select(_ => 0)],
            Materials = ["ground"], Ground = m => m == "ground", Passable = _ => false, Anchors = anchors,
            Areas = [CellExitsAndAreasTests.Room()],
            Exits =
            [
                new L2Exit { Number = 3, To = "EastGate", ToLevel = "P1_EGAT", At = [-12, 0, 1.5f],
                             Volume = new L2AreaBox { Origin = [-13.5f, -1.5f, -1], Size = [3, 3, 4.5f] } },
                new L2Exit { Number = 9, To = "SmithyDay", ToLevel = "P1_SMYD", At = [8, -7, 1.5f] },
                new L2Exit { Number = 10, To = "Inn4StagsDay", ToLevel = "P1_IN4D", At = [5, 8, 1.5f] },
            ],
        }, L2Package.PathFor(root, name));
        return (root, result.Package);
    }

    /// <summary>Prague with its <c>OldTownDay</c> played as the cooked level, and a campaign standing in <paramref name="startIn"/>.</summary>
    static (Campaign Campaign, Chronicle Prague) Game(Vfs.GameFiles files, string package, string startIn, int entrance)
    {
        var set = ChronicleSet.Load(files);
        Assert.Equal("P1_OTDY", set["Prague"]!.At("OldTownDay")!.LevelName);       // the file's, until a place is mapped
        Assert.Equal(1, CellPlaces.Apply(set, new Dictionary<string, string> { ["OldTownDay"] = package }));
        var prague = set["Prague"]!;
        Location Build(string level, string? scene, int at) =>
            Location.Load(files, level, sky: false, scene: null, player: "Christof", entrance: at);
        prague.Current = prague.Find(startIn);
        var here = Build(prague.At(startIn)!.LevelName, null, entrance);
        CellLevel.Arrived(here, prague);
        return (new Campaign { Chronicle = prague, Build = Build, Here = here }, prague);
    }

    static void AssertAtInnDoor(Location town, bool entered = true)
    {
        var leader = town.Player!.Thing;
        Assert.Equal(InnDoor[0].X * M, leader.Position.X, 0);
        Assert.Equal(InnDoor[0].Y * M, leader.Position.Y, 0);
        Assert.Equal(0f, leader.Position.Z, 0);
        Assert.Equal(InnDoorYaw, leader.Orientation.Z, 1);
        if (!entered) return;                                                     // a place read back keeps its record's entrance
        Assert.Equal(10, town.Entrance);
        Assert.Equal(4, town.Arrivals.Length);                                    // a start for each of the party's four
    }

    [Fact]
    public void Christof_leaves_a_cell_level_for_a_1_1_level_and_comes_back_at_the_exit_s_door()
    {
        using var files = Install.Open();
        string name = "celltown" + Guid.NewGuid().ToString("N")[..6];
        var (root, package) = Cook(name);
        try
        {
            var (campaign, prague) = Game(files, package, "OldTownDay", -1);
            var town = campaign.Here;
            var cells = CellLevel.Of(town.Level)!;
            Assert.Equal(name, prague.At("OldTownDay")!.LevelName);
            Assert.NotNull(prague.Threshold);
            Assert.Equal(-5 * M, town.Player!.Position.X, 0);                      // the level's own start, entrance 0's

            // Out by exit 10: the chronicle's own answer, and 1.1's inn built by the campaign as ever.
            Assert.Equal(CellExits.Taken.Leaving, cells.Exits.Take(10));
            var there = campaign.Travel()!.Value;
            Assert.Equal("P1_IN4D", there.To.Name);
            Assert.Null(CellLevel.Of(there.To.Level));
            Assert.Equal((0, 1), (there.Entrance, there.Party));
            Assert.Equal(prague.Find("Inn4StagsDay"), prague.Current);
            CellLevel.Arrived(there.To, prague);
            Assert.Null(prague.Threshold);                                         // nothing of ours is asked in a 1.1 level

            // And back through the inn's door, exit 0: the cell level as it was left, the party on entrance 10's starts.
            Assert.True(prague.TakeExit(0));
            var back = campaign.Travel()!.Value;
            Assert.Same(town, back.To);
            Assert.True(back.Returned);
            Assert.Equal(10, back.Entrance);
            CellLevel.Arrived(back.To, prague);
            AssertAtInnDoor(town);
            Assert.Same(cells, CellLevel.Of(town.Level));
            Assert.NotNull(prague.Threshold);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Arriving_from_a_1_1_level_builds_the_cell_level_in_the_place_s_stead_at_the_matching_door()
    {
        using var files = Install.Open();
        string name = "celltown" + Guid.NewGuid().ToString("N")[..6];
        var (root, package) = Cook(name);
        try
        {
            var (campaign, prague) = Game(files, package, "Inn4StagsDay", 0);
            Assert.Equal("P1_IN4D", campaign.Here.Name);
            Assert.True(campaign.Chronicle.TakeExit(0));
            Assert.True(campaign.Builds);                                          // a place not loaded: 1.1's loading plaque
            var arrival = campaign.Travel()!.Value;
            Assert.Equal(name, arrival.To.Name);
            Assert.False(arrival.Returned);
            Assert.NotNull(CellLevel.Of(arrival.To.Level));
            Assert.Equal(prague.Find("OldTownDay"), prague.Current);
            AssertAtInnDoor(arrival.To);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void The_exit_of_a_room_in_the_map_walks_the_party_in_and_the_threshold_is_crossed_with_1_1_s_numbers()
    {
        using var files = Install.Open();
        string name = "celltown" + Guid.NewGuid().ToString("N")[..6];
        var (root, package) = Cook(name);
        try
        {
            var (campaign, prague) = Game(files, package, "OldTownDay", 9);
            var town = campaign.Here;
            var cells = CellLevel.Of(town.Level)!;
            var said = new List<string>();
            cells.Say = said.Add;
            var crossings = new List<CellCrossing>();
            cells.Crossed += crossings.Add;
            var sim = new Simulation(town);
            sim.Step(1f / 60f);
            Assert.Null(cells.Areas.Current);

            Assert.Equal(CellExits.Taken.WalkingIn, cells.Exits.Take(9));
            Assert.Null(prague.Pending);                                           // no level change is asked for
            Assert.Null(campaign.Travel());
            Assert.True(town.Player!.Walking);
            for (int i = 0; i < 60 * 20 && (cells.Areas.Current is null || town.Player.Walking); i++) sim.Step(1f / 60f);
            Assert.Equal("room", cells.Areas.Current?.Name);
            Assert.Equal(9.5f * M, town.Player.Position.X, 0);                     // the point just inside the door
            Assert.Equal(-7f * M, town.Player.Position.Y, 0);
            var inside = Assert.Single(crossings);
            Assert.Equal(new CellCrossing(cells.Areas.Current!, true, "OldTownDay", "SmithyDay", 9, 0), inside);
            Assert.Contains(said, s => s.StartsWith("exit: 9 taken: room is a room of this map"));
            Assert.Contains(said, s => s.StartsWith("area: the party is in room (OldTownDay -> SmithyDay, 1.1's exit 9 to entrance 0)")
                                       && s.Contains("SmithyDay's sound (SMYD_4_1): music DA_WeaponStore.mp3 at"));
            Assert.Same(town, campaign.Here);
            Assert.Equal(prague.Find("OldTownDay"), prague.Current);               // the chronicle's place has not moved

            // Out again through the doorway: 1.1's way back is the smithy's exit 0 to the Old Town's entrance 9.
            var outside = new Vector3(4, -7, 0) * M;
            Assert.True(town.Player.WalkTo(outside, town.Walk!.Find(outside, 256f)));
            for (int i = 0; i < 60 * 20 && cells.Areas.Current is not null; i++) sim.Step(1f / 60f);
            Assert.Null(cells.Areas.Current);
            Assert.Equal(2, crossings.Count);
            Assert.Equal((false, "SmithyDay", "OldTownDay", 0, 9), (crossings[1].Entering, crossings[1].From, crossings[1].To, crossings[1].Exit, crossings[1].Entrance));

            // And on to the gate: walking into its volume asks the chronicle for exit 3.
            var gate = new Vector3(-12, 0, 0) * M;
            Assert.True(town.Player.WalkTo(gate, town.Walk.Find(gate, 256f)));
            for (int i = 0; i < 60 * 30 && prague.Leaving is null; i++) sim.Step(1f / 60f);
            Assert.Equal(new Chronicle.Travel(3, prague.Find("EastGate"), 0), prague.Leaving);
            Assert.Contains(said, s => s.StartsWith("exit: 3 taken: leaving OldTownDay for EastGate (P1_EGAT), entrance 0"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void A_cell_level_let_go_as_the_seventh_place_stops_its_stream_and_comes_back_from_its_record()
    {
        using var files = Install.Open();
        string name = "celltown" + Guid.NewGuid().ToString("N")[..6];
        var (root, package) = Cook(name);
        try
        {
            var effects = new EffectDefinitions(files);
            var powers = new DisciplineDefinitions(files);
            var set = ChronicleSet.Load(files);
            CellPlaces.Apply(set, new Dictionary<string, string> { ["OldTownDay"] = package });
            var prague = set["Prague"]!;
            var session = new Session { Chronicle = prague };
            session.Messages.Text = new Strings(files);
            Location Build(string level, string? scene, int entrance)
            {
                var place = Location.Load(files, level, things: false, sky: false, scene: scene, player: "Christof", entrance: entrance);
                place.Session = session;
                return place;
            }
            Location BuildSaved(Save.LocationSave record, int entrance)
            {
                var place = Build(record.Level, record.Scene.Length > 0 ? record.Scene : null, entrance);
                Save.SaveApply.Place(place, record, effects, powers);
                return place;
            }
            int town = prague.Find("OldTownDay");
            prague.Current = town;
            var here = Build(name, prague.At(town)!.SceneName, -1);
            session.Here = here;
            var campaign = new Campaign { Chronicle = prague, Build = Build, BuildSaved = BuildSaved, Here = here };
            campaign.Discarded += CellLevel.Gone;
            var cells = CellLevel.Of(here.Level)!;

            // Six small places of Prague by a script's Jump; the sixth makes seven loaded, and the one loaded first goes: the town.
            foreach (var where in new[] { "Smithy", "Unorna", "StThomas", "University", "SmithyDay" })
            {
                Assert.True(prague.Jump(where, 0));
                Assert.NotNull(campaign.Travel());
            }
            Assert.Empty(campaign.LetGo);
            Assert.True(prague.Jump("UnornaDay", 0));
            Assert.NotNull(campaign.Travel());
            Assert.Equal([town], campaign.LetGo);
            Assert.False(campaign.Resident.ContainsKey(town));
            Assert.True(campaign.Parked.TryGetValue(town, out var record));
            Assert.Equal(name, record!.Level);                                     // the record names the package's level

            // Back by the inn's entrance: built again from the record as a cell level of its own, the party at the door.
            Assert.True(prague.Jump("OldTownDay", 10));
            var back = campaign.Travel()!.Value;
            Assert.True(back.Restored);
            Assert.NotSame(here, back.To);
            var again = CellLevel.Of(back.To.Level);
            Assert.NotNull(again);
            Assert.NotSame(cells, again);
            AssertAtInnDoor(back.To, entered: false);
            CellLevel.Gone(back.To);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
