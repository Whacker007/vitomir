// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **Always run** (ours). Driving is Vitomir's own, so its run rule is argued rather
/// than copied from 1.1: the run key (either Shift) runs and nothing walks — and with the Gameplay screen's
/// <i>always run</i> on, nothing held runs and the run key walks, which is, in effect, inverting
/// it. It is an Options setting (<see cref="GameOptions.AlwaysRun"/>), off by default, kept in
/// <c>[options]</c> and on a tape's <c>options</c> line like the others; and it is the keyboard's alone —
/// a click's walk keeps 1.1's rule (a run from 300 units out), so only driving reads it.
/// </summary>
public class AlwaysRunTests
{
    [Fact]
    public void Off_the_run_key_runs_and_on_it_walks()
    {
        var options = new GameOptions();
        Assert.False(options.AlwaysRun);                         // off by default: driving as it was
        Assert.True(AlwaysRunOption.Runs(runHeld: true, options));
        Assert.False(AlwaysRunOption.Runs(runHeld: false, options));
        options.AlwaysRun = true;
        Assert.False(AlwaysRunOption.Runs(runHeld: true, options));
        Assert.True(AlwaysRunOption.Runs(runHeld: false, options));
    }

    [Fact]
    public void It_is_kept_in_the_options_section_and_read_back()
    {
        var options = new GameOptions { AlwaysRun = true };
        Assert.Contains(options.Values(), p => p.Key == "alwaysRun" && p.Value == "1");
        var read = new GameOptions();
        read.Apply(IniFile.Parse("[options]\nalwaysRun = 1\n"));
        Assert.True(read.AlwaysRun);
        read.Apply(IniFile.Parse("[options]\nalwaysRun = 0\n"));
        Assert.False(read.AlwaysRun);
        Assert.True(options.Clone().AlwaysRun);
    }

    [Fact]
    public void It_is_one_of_the_settings_a_tape_records()
    {
        // The tape's options line is every public primitive property of GameOptions (InputTape.Settings).
        var p = typeof(GameOptions).GetProperty(nameof(GameOptions.AlwaysRun))!;
        Assert.True(p.CanRead && p.CanWrite && p.PropertyType.IsPrimitive);
    }

    [Fact]
    public void Only_driving_reads_it_so_a_click_s_walk_keeps_1_1_s_rule()
    {
        if (Root() is not { } root) { Assert.Skip("not running from inside the tree"); return; }
        var readers = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains("Tests", StringComparison.Ordinal) && !f.EndsWith("AlwaysRunOption.cs", StringComparison.Ordinal))
            .SelectMany(f => File.ReadLines(f).Where(l => l.Contains("AlwaysRunOption.Runs(", StringComparison.Ordinal)).Select(l => (f, l)))
            .ToList();
        Assert.Equal(3, readers.Count);                           // the steered character, a player's machine, the carried body
        Assert.All(readers, r => Assert.True(r.f.EndsWith(Path.Combine("Vitomir.Classic", "Controller", "ClassicGame.Frame.cs"), StringComparison.Ordinal)
                                             && (r.l.Contains("Drive(", StringComparison.Ordinal) || r.l.Contains("player.Running", StringComparison.Ordinal)), r.l));
    }

    [Fact]
    public void The_gameplay_screen_has_the_box_under_1_1_s_two_and_ok_puts_it_in_force()
    {
        using var files = Install.Open();
        var assets = new UiAssets(files);
        var text = new Strings(files);
        var menus = new FrontEnd(n => assets.Pane(n), n => assets.Font(n), text.Find);
        var ini = Path.Combine(Path.GetTempPath(), $"vitomir-alwaysrun-{Guid.NewGuid():N}.ini");
        var options = new GameOptions();
        var screen = new OptionsMenu(menus, text.Find, options) { SavePath = ini };
        menus.Options = screen;
        screen.Open();
        screen.OpenGameplay();
        var pane = menus.Pane(OptionsMenu.GameplayFile);
        var box = pane.Element(AlwaysRunOption.Box)!;
        Assert.Equal(pane.Element("opgpSubtitling")!.Pos[0], box.Pos[0]);
        Assert.Equal(AlwaysRunOption.Caption, pane.Texts[AlwaysRunOption.Label]);
        Assert.False(pane.Checks[AlwaysRunOption.Box]);
        pane.Check(AlwaysRunOption.Box, true);
        Assert.False(options.AlwaysRun);                          // nothing kept until OK
        var ok = pane.Element("opgpOKButton")!;
        var (x, y) = pane.At(ok);
        menus.PointerAt(x + ok.Size[0] / 2f, y + ok.Size[1] / 2f);
        menus.Frame(menus.NowMs);
        menus.LeftHeld = true;
        menus.Press();
        menus.LeftHeld = false;
        menus.Release();
        Assert.True(options.AlwaysRun);                           // the setting the frame loop reads, at once
        Assert.Equal("1", IniFile.Read(ini)["options.alwaysRun"]);
        File.Delete(ini);
        screen.OpenGameplay();
        Assert.True(pane.Checks[AlwaysRunOption.Box]);
    }

    static string? Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "src", "Vitomir.slnx"))) return dir.FullName;
        return null;
    }
}
