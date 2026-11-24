// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Text;
using Vitomir.Engine.Audio;
using Vitomir.Engine.Levels.Cells;
using Vitomir.Engine.Levels.L2;
using Vitomir.Engine.World;
using Vitomir.Formats;
using Vitomir.Formats.L2;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **Exits and areas of a cell level while it is played** (ours):
/// which area the party is in, with its hysteresis at the doorway and its events; the exit table a level's
/// manifest gives, taken through the chronicle as a sign's click is; a gate's volume; the place mapping,
/// which changes nothing unless asked and only a place's level when it is; and the crossfade an area's
/// sound uses. Nothing here needs the game: the chronicle is a made-up one of four places.
/// </summary>
public class CellExitsAndAreasTests
{
    const float M = L2Manifest.EngineUnitsPerMetre;

    /// <summary>A room 6 m square and 3 m high from (8, −10) to (14, −4), its door in the middle of its west wall, behind exit 9.</summary>
    internal static L2Area Room() => new()
    {
        Name = "room", Box = new L2AreaBox { Origin = [8, -10, 0], Size = [6, 6, 3] }, Place = "Room", Exit = 9,
        Doors = [new L2AreaDoor { Position = [8, -7, 0], Width = 2, Height = 2.2f, Inside = [9.5f, -7, 0], Outside = [6.5f, -7, 0] }],
    };

    /// <summary>A town of four places: exit 9 to a room, 10 to an inn, 3 to a road by a gate, 7 to the road again and shut.</summary>
    internal const string Story = """
        location Town   TOWN.nil   null   map.nui  mapTown  0x0
        location Room   ROOM.nil   null   map.nui  mapRoom  0x0
        location Inn    INN.nil    null   map.nui  mapInn   0x0
        location Road   ROAD.nil   null   map.nui  mapRoad  0x0
        exit Town,9   Room,0
        exit Town,10  Inn,0
        exit Town,3   Road,0
        exit Town,7   Road,1  0x1
        """;

    internal static Chronicle Chronicle()
    {
        var c = new Chronicle(NscChronicle.Read(Encoding.Latin1.GetBytes(Story), "Test.nsc"));
        c.Current = c.Find("Town");
        return c;
    }

    static L2Manifest Manifest() => new()
    {
        Version = L2Manifest.Version2, Name = "town", Areas = [Room()],
        Exits =
        [
            new L2Exit { Number = 3, To = "Road", ToLevel = "ROAD", At = [-12, 0, 1.5f],
                         Volume = new L2AreaBox { Origin = [-13.5f, -1.5f, -1], Size = [3, 3, 4.5f] } },
            new L2Exit { Number = 7, To = "Road", ToLevel = "ROAD", Closed = true },
            new L2Exit { Number = 9, To = "Room", ToLevel = "ROOM", At = [8, -7, 1.5f] },
            new L2Exit { Number = 10, To = "Inn", ToLevel = "INN", At = [5, 8, 1.5f] },
        ],
    };

    static Vector3 Feet(float x, float y) => new Vector3(x, y, 0) * M;

    [Fact]
    public void The_party_is_in_an_area_once_its_leader_is_well_inside_and_out_only_once_well_outside()
    {
        var areas = new CellAreas([Room()]);
        var heard = new List<string>();
        areas.Entered += a => heard.Add("in " + a.Name);
        areas.Left += a => heard.Add("out of " + a.Name);
        Assert.Null(areas.Update(Feet(5, -7)));                                  // in the street, 3 m from the door
        Assert.Null(areas.Update(Feet(8.1f, -7)));                               // across the threshold, not yet EnterDepth in
        Assert.Null(areas.Update(Feet(8.29f, -7)));
        Assert.Equal("room", areas.Update(Feet(8.31f, -7))?.Name);               // 0.31 m inside
        Assert.Equal(["in room"], heard);
        // Back in the doorway, and a little outside it: still in the room.
        Assert.Equal("room", areas.Update(Feet(8.05f, -7))?.Name);
        Assert.Equal("room", areas.Update(Feet(7.75f, -7))?.Name);
        Assert.Equal("room", areas.Update(Feet(8.1f, -7))?.Name);
        Assert.Equal(["in room"], heard);                                        // standing in the doorway raises nothing
        Assert.Null(areas.Update(Feet(7.69f, -7)));                              // 0.31 m outside
        Assert.Equal(["in room", "out of room"], heard);
        Assert.Null(areas.Update(Feet(8.1f, -7)));                               // and the doorway again is the street's now
        Assert.Equal(0.30f, CellAreas.EnterDepth);
        Assert.Equal(0.30f, CellAreas.LeaveDepth);
    }

    [Fact]
    public void The_first_look_takes_any_box_the_leader_stands_in_and_a_floor_above_is_not_the_room()
    {
        var areas = new CellAreas([Room()]);
        Assert.Equal("room", areas.Update(Feet(8.1f, -7))?.Name);                // a save loaded in the doorway: inside is inside
        areas.Reset();
        Assert.Null(areas.Update(Feet(7.9f, -7)));
        // The test is made a metre above the feet, so the room's floor is not its edge, and its roof is.
        Assert.Equal("room", CellAreas.At([Room()], null, new Vector3(11, -7, 1))?.Name);
        Assert.Null(CellAreas.At([Room()], null, new Vector3(11, -7, 4)));
        // A box inside a box: the smaller one.
        var cupboard = new L2Area { Name = "cupboard", Box = new L2AreaBox { Origin = [10, -8, 0], Size = [2, 2, 3] } };
        Assert.Equal("cupboard", CellAreas.At([Room(), cupboard], null, new Vector3(11, -7, 1))?.Name);
        Assert.Equal("room", CellAreas.At([Room(), cupboard], Room(), new Vector3(9, -7, 1))?.Name);
        Assert.Null(new CellAreas(null).Update(Feet(11, -7)));                   // a level with no areas
    }

    [Fact]
    public void A_cell_level_s_exit_table_keeps_the_place_s_numbers_with_the_room_behind_its_exit()
    {
        var exits = new CellExits(Manifest());
        Assert.Equal([3, 7, 9, 10], exits.Table.Select(e => e.Number));
        Assert.Equal("Inn", exits[10]!.To);
        Assert.Equal(new Vector3(5, 8, 1.5f) * M, exits[10]!.At);
        Assert.NotNull(exits[3]!.Volume);
        Assert.Null(exits[10]!.Volume);
        Assert.Equal("room", exits[9]!.Area?.Name);
        Assert.Null(exits[10]!.Area);
        Assert.Null(exits[6]);
        // A room whose exit the manifest does not list is in the table all the same.
        var bare = new CellExits(new L2Manifest { Name = "town", Areas = [Room()] });
        Assert.Equal("room", Assert.Single(bare.Table).Area?.Name);
        Assert.Equal(new Vector3(9.5f, -7, 0), CellExits.Inside(Room()));
        var noPoints = Room();
        noPoints.Doors![0].Inside = null;
        Assert.Equal(new Vector3(9.2f, -7, 0), CellExits.Inside(noPoints)!.Value);   // 1.2 m in from the threshold, towards the middle
    }

    [Fact]
    public void Taking_an_exit_asks_the_chronicle_as_a_sign_s_click_does()
    {
        var story = Chronicle();
        var exits = new CellExits(Manifest());
        var heard = new List<(int, CellExits.Taken)>();
        exits.Took += (n, taken, _) => heard.Add((n, taken));
        Assert.Equal(CellExits.Taken.Refused, exits.Take(10));                   // no chronicle bound: a level played on its own
        exits.Chronicle = story;
        Assert.Equal(CellExits.Taken.Refused, exits.Take(7));                    // shut
        Assert.Equal(CellExits.Taken.Refused, exits.Take(6));                    // leads nowhere
        Assert.Equal(CellExits.Taken.Refused, exits.Take(40));                   // past the end
        Assert.Null(story.Pending);
        Assert.Equal(CellExits.Taken.Leaving, exits.Take(10));
        Assert.Equal(new Chronicle.Travel(10, story.Find("Inn"), 0), story.Leaving);
        Assert.Equal([(10, CellExits.Taken.Refused), (7, CellExits.Taken.Refused), (6, CellExits.Taken.Refused),
                      (40, CellExits.Taken.Refused), (10, CellExits.Taken.Leaving)], heard);
        // A script opening the gate is followed, the manifest's own "closed" being only what the layout recorded.
        story.Take();
        story.SetExitClosed("Town", 7, false);
        Assert.Equal(CellExits.Taken.Leaving, exits.Take(7));
        Assert.Equal(1, story.Leaving!.Value.Entrance);
    }

    [Fact]
    public void A_room_in_the_map_answers_its_exit_through_the_chronicle_s_threshold_and_no_journey_is_asked_for()
    {
        var story = Chronicle();
        var exits = new CellExits(Manifest()) { Chronicle = story };
        int asked = 0;
        story.Threshold = n => { asked++; return n == 9; };                      // what CellLevel.Bind sets, the walk left out
        Assert.Equal(CellExits.Taken.WalkingIn, exits.Take(9));
        Assert.Null(story.Pending);
        Assert.Equal(CellExits.Taken.Leaving, exits.Take(10));                   // any other exit is the journey
        Assert.Equal(2, asked);
        // A shut door is refused before the room is asked: the chronicle's gates come first.
        story.Take();
        story.SetExitClosed("Town", 9, true);
        Assert.Equal(CellExits.Taken.Refused, exits.Take(9));
        Assert.Equal(2, asked);
        // And an exit of another place (a host's remote player) is never the room's.
        story.SetExitClosed("Town", 9, false);
        Assert.True(story.TakeExit(9, story.Find("Town")));
        Assert.NotNull(story.Leaving);
    }

    [Fact]
    public void A_gate_s_volume_takes_its_exit_only_when_walked_into_from_outside()
    {
        var exits = new CellExits(Manifest());
        Assert.Null(exits.Walked(Feet(-12, 0)));                                 // the party arrives inside it: nothing
        Assert.Null(exits.Walked(Feet(-12, 0.5f)));
        Assert.Null(exits.Walked(Feet(-9, 0)));                                  // steps out
        Assert.Equal(3, exits.Walked(Feet(-12, 0)));                             // and walks in
        Assert.Null(exits.Walked(Feet(-12.2f, 0)));                              // standing in it does not ask again
        Assert.Null(exits.Walked(Feet(-9, 0)));
        Assert.Equal(3, exits.Walked(Feet(-12.5f, 1)));
        exits.Reset();                                                           // an arrival
        Assert.Null(exits.Walked(Feet(-12, 0)));
        // An exit that is clicked has no volume: standing on its sign does nothing.
        Assert.Null(exits.Walked(Feet(5, 8)));
    }

    [Fact]
    public void Without_a_place_mapped_the_chronicle_is_as_its_file_wrote_it()
    {
        Assert.Empty(CellPlaces.Map);                                            // nothing maps a place unless a run asks
        var story = Chronicle();
        var set = ChronicleSet.Of(story);
        Assert.Equal(0, CellPlaces.Apply(set));
        Assert.Equal(0, CellPlaces.Apply(set, new Dictionary<string, string>()));
        Assert.Equal(["TOWN", "ROOM", "INN", "ROAD"], story.Data.Locations.Select(l => l.LevelName));
        Assert.Equal("Town", set.ForLevel("TOWN")?.Entry?.Name);
        Assert.Null(L2Levels.PackageOf("TOWN"));
        Assert.Null(CellPlaces.Replaces("TOWN"));
        // And with no threshold asked, exit 9 is a journey like any other.
        Assert.Null(story.Threshold);
        Assert.True(story.TakeExit(9));
        Assert.Equal(new Chronicle.Travel(9, story.Find("Room"), 0), story.Leaving);
    }

    [Fact]
    public void A_mapped_place_keeps_everything_the_chronicle_says_of_it_but_its_level()
    {
        var root = Path.Combine(Path.GetTempPath(), "vitomir-cellplace-" + Guid.NewGuid().ToString("N"));
        string name = "newtown" + Guid.NewGuid().ToString("N")[..6];
        var package = L2Package.PathFor(root, name);
        try
        {
            L2Package.Write(package, new L2Manifest { Version = L2Manifest.Version2, Name = name }, new Dictionary<string, L2Cell>());
            var story = Chronicle();
            var set = ChronicleSet.Of(story);
            var said = new List<string>();
            Assert.Equal(1, CellPlaces.Apply(set, new Dictionary<string, string> { ["Town"] = package, ["Nowhere"] = package }, said.Add));
            var town = story.At("Town")!;
            Assert.Equal(name, town.LevelName);
            Assert.Equal(("Town", "", "mapTown", 11), (town.Name, town.SceneName, town.MapMarker, town.ExitCount));
            Assert.Equal(story.Find("Inn"), town.Exits[10].To);
            Assert.True(town.Exits[7].Closed);
            Assert.Equal(["ROOM", "INN", "ROAD"], story.Data.Locations.Skip(1).Select(l => l.LevelName));   // the other places are theirs
            Assert.Equal("Town", set.ForLevel(name)?.Entry?.Name);                // the level leads back to the place
            Assert.Null(set.ForLevel("TOWN"));
            Assert.Equal(package, L2Levels.PackageOf(name));
            Assert.Equal("TOWN", CellPlaces.Replaces(name));
            Assert.Contains(said, s => s.Contains("Town is played as the cell level " + name) && s.Contains("in place of TOWN"));
            Assert.Contains(said, s => s.Contains("no chronicle has a place called Nowhere"));
            Assert.Equal(0, CellPlaces.Apply(set, new Dictionary<string, string> { ["Town"] = package }));   // twice is once
            Assert.Empty(CellPlaces.Map);                                         // the run's own map was not touched
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void A_place_is_named_to_a_run_as_Place_equals_package_and_a_folder_that_is_no_cooked_level_is_refused()
    {
        var root = Directory.CreateTempSubdirectory("vitomir-cellplace").FullName;
        try
        {
            Assert.False(CellPlaces.Parse("OldTownDay", out string wrong));
            Assert.Contains("<Place>=<package.l2>", wrong);
            Assert.False(CellPlaces.Parse("OldTownDay=" + root, out wrong));
            Assert.Contains("not a cooked cell level", wrong);
            Assert.True(CellPlaces.Parse("", out _));
            // The ini's lines are the same pairs, under [levels]; other keys are not places.
            var said = new List<string>();
            var ini = Diagnostics.IniFile.Parse($"[levels]\nplace.OldTownDay = {root}\nplace. = x\nother = 1\n[modern]\nquality = high\n");
            Assert.Equal(0, CellPlaces.FromIni(ini, said.Add));
            Assert.Contains("the ini's levels.place.OldTownDay", Assert.Single(said));
            Assert.Empty(CellPlaces.Map);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void An_area_s_sound_changes_over_a_crossfade_and_the_outside_s_comes_back()
    {
        using var install = new AudioTests.FakeInstall();
        var audio = new GameAudio(install.Files);
        audio.Configure(0.75f, 0.8f);
        var (street, forge) = (audio.Bank.Load("a", stream: true)!, audio.Bank.Load("b", stream: true)!);
        audio.SetMusicLevel(100);
        audio.StartMusic(street);
        var old = audio.MusicChannel!;
        Assert.Equal(0.8f, old.Volume, 5);

        audio.Crossfade(forge, GameAudio.LevelOf(50), null, 0.25f, seconds: 1f);
        Assert.True(audio.Crossfading);
        var now = audio.MusicChannel!;
        Assert.NotSame(old, now);
        Assert.Same(forge, audio.Music);
        Assert.True(old.Active);                                                 // the old track plays on under the new one
        Assert.Equal(0f, now.Volume, 5);
        audio.StepCrossfade(0.5f);
        Assert.Equal(0.4f, old.Volume, 4);                                       // half way down from 0.8
        Assert.Equal(0.2f, now.Volume, 4);                                       // half way up to 0.5 × 0.8
        audio.StepCrossfade(0.6f);
        Assert.False(audio.Crossfading);
        Assert.False(old.Active);
        Assert.Equal(0.4f, audio.MusicChannel!.Volume, 5);
        Assert.Equal(1f, audio.MusicChannel.Target);                             // its ceiling lifted, as 1.1 leaves it

        // The same track asked for again keeps playing and only takes the level; none asked for leaves it alone.
        audio.Crossfade(forge, 1f, null, 0.25f, seconds: 1f);
        Assert.False(audio.Crossfading);
        Assert.Same(now, audio.MusicChannel);
        Assert.Equal(0.8f, now.Volume, 5);
        audio.Crossfade(null, 1f, null, 0.25f, seconds: 1f);
        Assert.Same(forge, audio.Music);
        // The way back to an outside that had no music: the room's fades out and nothing replaces it.
        audio.Crossfade(null, 1f, null, 0.25f, seconds: 1f, stopMissing: true);
        Assert.Null(audio.Music);
        Assert.True(now.Active);
        audio.StepCrossfade(1.1f);
        Assert.False(now.Active);
        Assert.Null(audio.MusicChannel);
        Assert.Equal(1.5f, GameAudio.AreaCrossfade);
    }

    [Fact]
    public void A_level_change_in_the_middle_of_a_crossfade_leaves_the_new_place_s_channels_alone()
    {
        using var install = new AudioTests.FakeInstall();
        var audio = new GameAudio(install.Files);
        var (street, forge, next) = (audio.Bank.Load("a", stream: true)!, audio.Bank.Load("b", stream: true)!, audio.Bank.Load("c", stream: true)!);
        audio.StartMusic(street);
        audio.Crossfade(forge, 1f, null, 0.25f, seconds: 1f);
        audio.Mixer.StopAll();                                                   // a level change
        audio.AbandonCrossfade();                                                // CellLevel.Arrived
        audio.StartMusic(next);
        float full = audio.MusicChannel!.Volume;
        audio.StepCrossfade(0.5f);
        Assert.Equal(full, audio.MusicChannel!.Volume);
        Assert.True(audio.MusicChannel.Active);
    }
}
