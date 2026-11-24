// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>The campaign's own chronicle files, read out of the player's install.</summary>
public class ChronicleTests
{
    [Fact]
    public void Every_chronicle_in_the_install_parses()
    {
        using var files = Install.Open();
        var set = ChronicleSet.Load(files);
        // Five single-player chronicles and nine multiplayer ones ship with 1.1.
        Assert.Equal(14, set.All.Count);
        Assert.All(set.All, c => Assert.True(c.Count > 0, $"{c.Name} has no locations"));
        // Only one line in the whole folder is not a directive: a stray header line in
        // MP_LeavesThree.nsc that does not start with '#'. Anything more means a parser gap.
        Assert.Equal(1, set.All.Sum(c => c.Data.Ignored.Count));
    }

    [Fact]
    public void Prague_is_the_campaigns_start_and_the_rest_chain_from_it()
    {
        using var files = Install.Open();
        var set = ChronicleSet.Load(files);
        var single = set.All.Where(c => !c.Kind.HasFlag(NscChronicleFlags.Multiplayer)).ToList();
        Assert.Equal(5, single.Count);
        var unchained = single.Where(c => !c.Kind.HasFlag(NscChronicleFlags.Chained)).ToList();
        Assert.Single(unchained);
        Assert.Equal("Prague", unchained[0].Name);
        Assert.Equal("Convent", unchained[0].Data.Start!.Name);
        // The modern-day half of the story is London and New York.
        Assert.Equal(["London", "NEW_YORK"],
            single.Where(c => c.Kind.HasFlag(NscChronicleFlags.ModernDay)).Select(c => c.Name).Order());
    }

    [Fact]
    public void Every_chronicle_names_its_script_class_and_its_metafile()
    {
        using var files = Install.Open();
        var set = ChronicleSet.Load(files);
        foreach (var c in set.All.Where(c => !c.Kind.HasFlag(NscChronicleFlags.Storyteller)))
            Assert.True(c.Data.Scripts.Count == 1, $"{c.Name} names {c.Data.Scripts.Count} scripts");
        Assert.All(set.All, c => Assert.NotEqual("", c.Data.Metafile));
        Assert.All(set.All, c => Assert.NotNull(c.Meta));
        Assert.Equal("PragueChronicle", set["Prague"]!.Data.Scripts[0]);
        Assert.Equal("Christof", set["Prague"]!.Meta!.Player);
    }

    [Fact]
    public void The_tower_of_london_is_a_chain_of_four_with_the_way_in_locked()
    {
        using var files = Install.Open();
        var london = ChronicleSet.Load(files)["London"]!;
        // East London's exit 1 into the Tower starts closed; a script opens it when the story does.
        Assert.True(london.IsExitValid("EastLondon", 1));
        Assert.True(london.IsExitClosed("EastLondon", 1));
        Assert.Equal("TowerOfLondon1", london.NameOf(london.At("EastLondon")!.Exits[1].To));
        // And the loader's backward link is closed too.
        Assert.True(london.IsExitClosed("TowerOfLondon1", 0));

        for (int i = 1; i <= 3; i++)
        {
            Assert.True(london.IsExitValid($"TowerOfLondon{i}", 1));
            Assert.Equal($"TowerOfLondon{i + 1}", london.NameOf(london.At($"TowerOfLondon{i}")!.Exits[1].To));
            Assert.False(london.IsExitClosed($"TowerOfLondon{i}", 1));
        }
        Assert.False(london.IsExitValid("TowerOfLondon4", 1));   // the top of the tower is the end
    }

    [Fact]
    public void Opening_an_exit_changes_only_the_side_it_names()
    {
        using var files = Install.Open();
        var london = ChronicleSet.Load(files)["London"]!;
        london.SetExitClosed("EastLondon", 1, false);
        Assert.False(london.IsExitClosed("EastLondon", 1));
        // The natives never follow the backward link — the loader made it once, and a script that
        // wants both ways open says so twice, as the shipped ones do.
        Assert.True(london.IsExitClosed("TowerOfLondon1", 0));
    }

    [Fact]
    public void A_location_that_is_not_there_answers_the_way_the_original_does()
    {
        using var files = Install.Open();
        var london = ChronicleSet.Load(files)["London"]!;
        Assert.False(london.IsExitValid("Atlantis", 0));
        Assert.False(london.IsExitClosed("Atlantis", 0));
        Assert.Equal(0u, london.GetLocationFlags("Atlantis"));
        Assert.Equal(0u, london.SetLocationFlags("Atlantis", 0xff));
        Assert.Equal(Chronicle.InvalidLocation, london.NameOf(999));
        Assert.Equal(Chronicle.InvalidLocation, london.NameOf(-1));
    }

    [Fact]
    public void Location_flags_are_set_and_cleared_as_masks()
    {
        using var files = Install.Open();
        var london = ChronicleSet.Load(files)["London"]!;
        uint before = london.GetLocationFlags("WestLondon");
        Assert.Equal(before | 0x8u, london.SetLocationFlags("WestLondon", 0x8));
        Assert.Equal(before & ~0x20u | 0x8u, london.ClearLocationFlags("WestLondon", 0x20));
    }

    [Fact]
    public void Chronicle_flags_hold_a_thousand_and_one_bits_and_return_the_previous_state()
    {
        using var files = Install.Open();
        var prague = ChronicleSet.Load(files)["Prague"]!;

        Assert.False(prague.GetFlag(7));
        Assert.False(prague.SetFlag(7));            // it was not set before
        Assert.True(prague.GetFlag(7));
        Assert.True(prague.SetFlag(7));             // and now it was
        Assert.True(prague.ClearFlag(7));
        Assert.False(prague.GetFlag(7));

        Assert.False(prague.ToggleFlag(7));
        Assert.True(prague.GetFlag(7));
        Assert.True(prague.ToggleFlag(7));
        Assert.False(prague.GetFlag(7));

        // The SDK says 1024 flags; the original's bound check lets index 1024 through as well, which in
        // the original writes one dword past its array. Here that index simply has a bit.
        Assert.False(prague.SetFlag(Chronicle.MaxFlag));
        Assert.True(prague.GetFlag(Chronicle.MaxFlag));
        Assert.False(prague.SetFlag(Chronicle.MaxFlag + 1));
        Assert.False(prague.GetFlag(Chronicle.MaxFlag + 1));
        Assert.False(prague.GetFlag(-1));

        prague.SetFlag(3);
        prague.ClearAllFlags();
        Assert.Empty(prague.SetFlags());
    }

    [Fact]
    public void A_level_resolves_back_to_the_chronicle_and_location_that_own_it()
    {
        using var files = Install.Open();
        var set = ChronicleSet.Load(files);

        var tower = set.ForLevel("l1_tol2");
        Assert.NotNull(tower);
        Assert.Equal("London", tower!.Value.Chronicle.Name);
        Assert.Equal("TowerOfLondon2", tower.Value.Name);
        Assert.Equal("TOL2_30_1", tower.Value.Entry!.SceneName);

        // A scene belongs to exactly one location, which is the reliable way round.
        Assert.Equal("Haven", set.ForScene("HAVN_25_1")!.Value.Name);
        Assert.Equal("Prague2", set.ForScene("HAVN_25_1")!.Value.Chronicle.Name);
        Assert.Equal("Prague", set.ForScene("HAVN_7_7")!.Value.Chronicle.Name);

        // A level in two chronicles goes to the earlier one in story order.
        Assert.Equal("Prague", set.ForLevel("P1_HAVN")!.Value.Chronicle.Name);
        Assert.Null(set.ForLevel("nosuchlevel"));
    }

    [Fact]
    public void Every_location_names_a_level_the_install_has()
    {
        using var files = Install.Open();
        foreach (var c in ChronicleSet.Load(files).All)
            foreach (var loc in c.Data.Locations)
            {
                if (loc.LevelName.Equals("dummy", StringComparison.OrdinalIgnoreCase)) continue;
                Assert.True(files.TryRead($"levels/{loc.LevelName}.nil", out _),
                            $"{c.Name}/{loc.Name} names a level the install has not got: {loc.Level}");
            }
    }

    [Fact]
    public void One_shipped_location_names_a_scene_file_that_does_not_exist()
    {
        // Prague.nsc gives EastGateNight the scene EGAT_7_1.nsd. The level is P1_EGNT and the
        // scene the game ships is EGNT_7_1.nsd, so the name is a typo in the shipped chronicle —
        // one character, and the only one in the whole folder. It is listed here rather than
        // fixed: no single-player script names EGNT_7_1 or sets that location's scene (only the
        // multiplayer chronicles call ChangeScene), so 1.1 most likely loads it with no scene too.
        using var files = Install.Open();
        var missing = new List<string>();
        foreach (var c in ChronicleSet.Load(files).All)
            foreach (var loc in c.Data.Locations)
                if (loc.SceneName.Length > 0 && !files.TryRead($"scenes/{loc.SceneName}.nsd", out _))
                    missing.Add($"{c.Name}/{loc.Name} -> {loc.Scene}");

        Assert.Equal(["Prague/EastGateNight -> EGAT_7_1.nsd"], missing);
        Assert.True(files.TryRead("scenes/EGNT_7_1.nsd", out _), "the scene it meant is there");
    }

    [Fact]
    public void Portal_and_revive_move_only_to_a_location_that_exists()
    {
        using var files = Install.Open();
        var prague = ChronicleSet.Load(files)["Prague"]!;
        Assert.Equal("Haven", prague.NameOf(prague.Portal.Location));
        Assert.Equal(15, prague.Portal.Entrance);

        Assert.True(prague.SetPortal(0, 2));
        Assert.Equal((0, 2), prague.Portal);
        Assert.False(prague.SetPortal(prague.Count, 0));
        Assert.False(prague.SetPortal(-1, 0));
        Assert.Equal((0, 2), prague.Portal);
    }
}
