// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Assets;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Host.Arena;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// One against several in the arena: the sides are read and checked (every fighter or none, two sides at least, a foe
/// named on another side); a cast without sides is the cast before, text for text; one against two ends when the subject is down
/// (the foes' side wins) or when both foes are down (the subject's), the subject's brain turning to the second foe when the first
/// falls; the group's columns are filled. Skips without the proving ground (VITOMIR_COURSE_LEVEL) and the 1.1 install.
/// </summary>
public sealed class ArenaSidesTests
{
    static string Course()
    {
        var course = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        Assert.SkipUnless(course is not null && File.Exists(Path.Combine(course, "level.json")), "no proving ground (VITOMIR_COURSE_LEVEL)");
        return course!;
    }

    static ArenaSpec Read(string fighters) => ArenaSpec.Read($$"""{ "fighters": [ {{fighters}} ], "seconds": 3, "bouts": 1 }""", Path.GetTempPath(), Course());

    static string Tools()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "tools", "platform", "arena"))) dir = Path.GetDirectoryName(dir);
        Assert.SkipUnless(dir is not null, "the repository's tools/platform/arena is not above the tests");
        return Path.Combine(dir!, "tools", "platform", "arena");
    }

    [Fact]
    public void Sides_are_given_to_every_fighter_or_none_on_two_sides_and_a_foe_is_of_another_side()
    {
        var spec = Read("""{ "name": "C", "side": "me" }, { "name": "A", "side": "them" }, { "name": "B", "side": "them" }""");
        Assert.Equal(["me", "them"], spec.SideNames);
        Assert.Equal([1, 0, 0], spec.Fighters.Select(f => f.Foe));            // the first of another side (B's default would be C anyway)
        Assert.True(spec.Cast(spec.Points[0]).HasSides);
        var none = Read("""{ "name": "C" }, { "name": "A" }, { "name": "B" }""");
        Assert.Empty(none.SideNames);
        Assert.Equal([1, 2, 0], none.Fighters.Select(f => f.Foe));            // the next fighter round, as before
        Assert.DoesNotContain("Side", none.Cast(none.Points[0]).Write(), StringComparison.Ordinal);   // the cast earlier, text for text
        Assert.False(ArenaCast.SidesIn(none.Cast(none.Points[0]).Write()));
        foreach (var bad in new[]
        {
            """{ "name": "C", "side": "me" }, { "name": "A" }""",
            """{ "name": "C", "side": "us" }, { "name": "A", "side": "us" }""",
            """{ "name": "C", "side": "me" }, { "name": "A", "side": "them" }, { "name": "B", "side": "them", "foe": "A" }""",
            """{ "name": "C", "side": "" }, { "name": "A", "side": "them" }""",
        })
            Assert.Equal(ArenaSpecError.BadSide, Assert.Throws<ArenaSpecException>(() => Read(bad)).Error);
    }

    [Fact]
    public void One_against_two_ends_when_the_subject_or_both_foes_are_down_and_fills_the_group_s_columns()
    {
        Course();
        Assert.SkipUnless(GameInstall.Locate() is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        Environment.SetEnvironmentVariable("VITOMIR_USERDATA", Path.Combine(Path.GetTempPath(), "vitomir-sides-tests-userdata"));
        var baseline = new SessionOptions { Level = Course(), Game = GameInstall.Locate(), Switches = new HashSet<string>(StringComparer.Ordinal) { "sandbox" } };

        // The player's sheet against two townsmen with clubs (seed 3000): he wins, each foe down in turn, his brain turned to the second.
        var spec = ArenaSpec.ReadFile(Path.Combine(Tools(), "group-player-townsman-club-2.json"), Course());
        var run = new ArenaRun(spec, baseline);
        var won = run.Bout(0, 0);
        Assert.Equal(0, won.Winner);
        Assert.Equal(0, won.SideWon);
        Assert.False(won.Fighters[0].Stopped);
        Assert.True(won.Fighters[1].Stopped && won.Fighters[2].Stopped, "both foes down");
        Assert.NotNull(won.KillSeconds);
        Assert.Equal(won.KillSeconds!.Value, new[] { won.Fighters[1].DownSeconds!.Value, won.Fighters[2].DownSeconds!.Value }.Max(), 0.01);
        Assert.True(won.Fighters[0].FoeSwitches >= 1, "the subject turned to the foe left");
        var row = ArenaTables.BoutRow(spec, won);
        var head = ArenaTables.BoutHeader(spec);
        Assert.Equal(head.Count, row.Count);
        string Cell(string column) => row[head.IndexOf(column)];
        Assert.Equal("christof", Cell("side_won"));
        Assert.Matches(@"^Townsman\d@[\d.]+;Townsman\d@[\d.]+$", Cell("down_order"));
        Assert.Equal("foes", Cell("Townsman2.side"));
        Assert.Equal("", Cell("Christof.down_s"));
        run.Content?.Dispose();

        // The newly Embraced against two trained guards (seed 3000, Melee 0): he is down, and the bout ends there with a guard's win.
        spec = ArenaSpec.ReadFile(Path.Combine(Tools(), "group-guard-2.json"), Course());
        run = new ArenaRun(spec, baseline);
        var lost = run.Bout(0, 0);
        Assert.True(lost.Fighters[0].Stopped);
        Assert.Equal(1, lost.SideWon);
        Assert.InRange(lost.Winner, 1, 2);
        Assert.Equal(lost.KillSeconds!.Value, lost.Fighters[0].DownSeconds!.Value, 0.01);
        Assert.False(lost.Fighters[1].Stopped && lost.Fighters[2].Stopped);
        run.Content?.Dispose();
    }
}
