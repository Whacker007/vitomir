// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.World;
using Xunit;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The two registers and the rule that separates them: a fix is off by default because the original
/// got something wrong, a QoL is on by default because the original never had it.
/// </summary>
public class QolTests
{
    /// <summary>
    /// Every QoL is on until something says otherwise — but `winkey`, which holds the whole keyboard
    /// and so waits to be asked for (*"Stay off."*), and `freecamera`, Ctrl+P's way
    /// past what the game camera shows (an option, and off it does not work); and the two film skips, `skipopening` and
    /// `skipstoryfilms`, since 1.1 plays its films and skipping them is the player's to ask for.
    /// </summary>
    [Fact]
    public void Everything_is_on_until_something_says_otherwise_but_the_windows_key_the_free_camera_and_the_film_skips()
    {
        var qol = new Qol();
        Assert.All(qol.All.Where(q => q.Name is not ("winkey" or "freecamera" or "skipopening" or "skipstoryfilms")), q => Assert.True(q.On, $"{q.Name} should default on"));
        Assert.False(qol.WinKey);
        Assert.False(qol.FreeCameraKey);
        Assert.True(qol.FilmVolume);                                   // the films follow the Sound slider
        Assert.False(qol.SkipOpening);                                 // the films play, as 1.1 plays them
        Assert.False(qol.SkipStoryFilms);
        Assert.Equal("off: freecamera, winkey, skipopening, skipstoryfilms", qol.ToString());
    }

    [Fact]
    public void A_fix_is_the_other_way_round()
    {
        var fixes = new Fixes();
        Assert.All(fixes.All, f => Assert.False(f.On, $"{f.Name} should default off"));
        Assert.Equal("faithful (no fixes)", fixes.ToString());
    }

    /// <summary>
    /// `--no-qol` turns one off and `--qol` turns it back on, both repeatable, and `all` takes the
    /// lot — which is how a run with none of them is asked for.
    /// </summary>
    [Fact]
    public void The_command_line_turns_them_off_and_on_again()
    {
        var off = Qol.Parse(["--no-qol", "quickequip"], Nowhere, userPath: NoUserSettings);
        Assert.False(off.QuickEquip);
        Assert.True(off.Driving);
        Assert.Equal("off: quickequip, freecamera, winkey, skipopening, skipstoryfilms", off.ToString());

        var none = Qol.Parse(["--no-qol", "all"], Nowhere, userPath: NoUserSettings);
        Assert.All(none.All, q => Assert.False(q.On));

        var back = Qol.Parse(["--no-qol", "all", "--qol", "camera"], Nowhere, userPath: NoUserSettings);
        Assert.True(back.Camera);
        Assert.False(back.Driving);
    }

    [Fact]
    public void A_name_that_is_not_a_switch_is_reported_rather_than_ignored()
    {
        var said = new List<string>();
        Qol.Parse(["--no-qol", "nosuchthing"], Nowhere, said.Add, userPath: NoUserSettings);
        Assert.Single(said);
        Assert.Contains("nosuchthing", said[0]);

        said.Clear();
        Qol.Parse(["--no-qol"], Nowhere, said.Add, userPath: NoUserSettings);
        Assert.Single(said);
    }

    [Fact]
    public void Every_switch_the_register_lists_can_actually_be_named()
    {
        // The bug this pins: `Fixes.Known` was a hand-written list of two while `All` had five, so
        // three fixes had been unreachable from the ini and the command line since the day each was
        // added. Both registers now derive it.
        foreach (var name in new Qol().All.Select(q => q.Name))
        {
            Assert.True(Qol.Known(name), $"{name} is listed but not reachable");
            var one = Qol.Parse(["--no-qol", name], Nowhere, userPath: NoUserSettings);
            Assert.False(one[name], $"--no-qol {name} did not turn it off");
        }
        foreach (var name in new Fixes().All.Select(f => f.Name))
        {
            Assert.True(Fixes.Known(name), $"{name} is listed but not reachable");
            // A Fix that refines another needs that one named too (`matchedgait` and `movespeed`).
            string[] needs = Vitomir.Engine.Features.GameFeatures.Registry.Find("fixes", name)!.Refines is { } refined ? ["--fix", refined.Split('.')[1]] : [];
            var one = Fixes.Parse(["--fix", name, .. needs], Nowhere, userPath: NoUserSettings);
            Assert.True(one[name], $"--fix {name} did not turn it on");
        }
    }

    [Fact]
    public void Names_are_case_insensitive_the_way_the_ini_keys_are()
    {
        Assert.True(Qol.Known("QuickEquip"));
        Assert.True(Fixes.Known("MatchedGait"));
        var qol = Qol.Parse(["--no-qol", "QUICKEQUIP"], Nowhere, userPath: NoUserSettings);
        Assert.False(qol.QuickEquip);
    }

    /// <summary>
    /// **The guard on the bug this file caused.** `Configure` installs a process-wide static and
    /// xUnit runs test classes in parallel, so a test that calls it changes what every other test
    /// running at that moment sees — which is how six new tests here broke two physics tests and an
    /// inventory one in other classes. `Parse` answers the same question and installs nothing, and
    /// this pins that.
    /// </summary>
    [Fact]
    public void Parsing_settings_does_not_touch_what_the_rest_of_the_process_sees()
    {
        var qolBefore = Qol.Current;
        var fixesBefore = Fixes.Current;

        Qol.Parse(["--no-qol", "all"], Nowhere, userPath: NoUserSettings);
        Fixes.Parse(["--fix", "all"], Nowhere, userPath: NoUserSettings);

        Assert.Same(qolBefore, Qol.Current);
        Assert.Same(fixesBefore, Fixes.Current);
        Assert.True(Qol.Current.QuickEquip);
        Assert.False(Fixes.Current.BodyCollision);
    }

    /// <summary>
    /// **The rule, enforced rather than remembered.** Both registers are process-wide statics that
    /// engine code reads, and xUnit runs test classes in parallel — so one test writing one is a
    /// race with every other test in the assembly, and a `finally` that puts it back only narrows
    /// the window. Every place that reads one takes an optional argument instead
    /// (`Physics.Contact`, `Player.RateFor`, `InventoryHands.Rules`), so no test needs to write one
    /// and none does. This fails the moment that stops being true — whether a test puts a new register
    /// in (<c>Current =</c>, <c>Configure(</c>) or writes one switch of the one that is there
    /// (<c>Current.WakeOnHit =</c>, <c>Current["wakeonhit"] =</c>), which is the same race and slipped
    /// past this guard once.
    /// </summary>
    [Fact]
    public void No_test_in_this_assembly_installs_a_register()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Vitomir.slnx")))
            dir = dir.Parent;
        if (dir is null) { Assert.Skip("not running from inside the tree"); return; }

        var tests = Path.Combine(dir.FullName, "src");
        var offenders = new List<string>();
        foreach (var path in Directory.EnumerateFiles(tests, "*Tests.cs", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            var lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.TrimStart().StartsWith("//")) continue;
                if (Installs(line, "Fixes") || Installs(line, "Qol"))
                    offenders.Add($"{Path.GetFileName(path)}:{i + 1} {line.Trim()}");
            }
        }
        Assert.True(offenders.Count == 0,
                    "a test installs a process-wide register, which races every other test:\n  "
                    + string.Join("\n  ", offenders));
    }

    static bool Installs(string line, string register) =>
        line.Contains($"{register}.Current =") || line.Contains($"{register}.Configure(")
        || System.Text.RegularExpressions.Regex.IsMatch(line, $@"\b{register}\.Current\s*(\.\s*\w+|\[[^\]]+\])\s*([-+*/|&^]?=)(?!=)");

    /// <summary>The guard's own eye, tried on lines of both kinds (the register is written <c>@</c> here, so these lines are not themselves caught).</summary>
    [Theory]
    [InlineData("@.Current = new @();", true)]
    [InlineData("var f = @.Configure(args);", true)]
    [InlineData("@.Current.WakeOnHit = fix;", true)]
    [InlineData("World.@.Current.WakeOnHit=true;", true)]
    [InlineData("@.Current[\"gunreach\"] = true;", true)]
    [InlineData("@.Current[name] |= on;", true)]
    [InlineData("Assert.True(@.Current.WakeOnHit == was);", false)]
    [InlineData("bool was = @.Current.WakeOnHit;", false)]
    [InlineData("var page = new @Page(menus) { Host = () => host };", false)]
    [InlineData("Assert.Same(before, @.Current);", false)]
    public void The_guard_knows_a_write_to_a_register_from_a_read_of_one(string line, bool writes) =>
        Assert.Equal(writes, Installs(line.Replace("@", "Fixes"), "Fixes"));

    /// <summary>A directory with no `vitomir.ini` in it, so a test reads no machine's settings.</summary>
    static string Nowhere => Path.Combine(Path.GetTempPath(), "vitomir-no-such-settings");

    /// <summary>A player's settings file that is not there, so the machine's own (where a Fix may be on) cannot leak into a test.</summary>
    static string NoUserSettings => Path.Combine(Path.GetTempPath(), "vitomir-no-such-settings", "user.ini");
}
