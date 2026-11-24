// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Diagnostics;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Windowing;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// **The Classic module's own folder, its window's settings and its switches under the platform** (the end of a comparison with 1.1
/// and its window's): the module's things in
/// <c>&lt;user data&gt;\Modules\classic\</c> (<c>VITOMIR_USERDATA</c> naming the user data, so each test keeps a folder of its own),
/// a save made there and loaded from there, the window's keys in the platform's <c>Games\classic\window.json</c> and not the
/// module's <c>vitomir.ini</c>, a first start said, the manifest's players' switches and an undeclared switch refused without
/// <c>--developer</c> and taken with it. Each run is the platform's program in a process of its own (one Classic per process).
/// </summary>
public sealed class ClassicFoldersTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("vitomir-classic-folders-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    static string Here => AppContext.BaseDirectory;

    static string Platform => Path.Combine(Here, OperatingSystem.IsWindows() ? "vitomir-platform.exe" : "vitomir-platform");

    string User => Path.Combine(dir, "user");

    string Module => Path.Combine(User, "Modules", "classic");

    /// <summary>The platform's program run to its end, the user data in this test's folder; its exit code, its log and what it printed.</summary>
    (int Code, string Log, string Output) Run(params string[] args)
    {
        string log = Path.Combine(dir, $"platform-{Guid.NewGuid():N}.log");
        var start = new ProcessStartInfo(Platform) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = dir };
        foreach (var a in (string[])["--game", "classic", "--log-file", log, .. args]) start.ArgumentList.Add(a);
        start.Environment["VITOMIR_USERDATA"] = User;
        start.Environment["SDL_AUDIODRIVER"] = "dummy";
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS() && Environment.GetEnvironmentVariable("DISPLAY") is null
            && Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") is null && Environment.GetEnvironmentVariable("SDL_VIDEO_DRIVER") is null)
            start.Environment["SDL_VIDEO_DRIVER"] = "offscreen";
        using var p = new Process { StartInfo = start };
        var err = new System.Text.StringBuilder();
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (err) err.AppendLine(e.Data); };
        p.Start();
        p.BeginErrorReadLine();
        var output = p.StandardOutput.ReadToEnd();
        if (!p.WaitForExit(600_000)) { p.Kill(entireProcessTree: true); Assert.Fail($"the platform did not end: {string.Join(' ', args)}"); }
        p.WaitForExit();
        lock (err) return (p.ExitCode, File.Exists(log) ? File.ReadAllText(log) : "", output + err);
    }

    static string Game()
    {
        var game = GameInstall.Locate();
        Assert.SkipWhen(game is null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        return game!;
    }

    [Fact]
    public void The_manifest_declares_the_players_switches_and_asks_for_platform_1_5_0()
    {
        var problems = new List<string>();
        var m = ModuleManifest.Parse(File.ReadAllText(Path.Combine(ModuleLoader.ProgramModules, "Classic", ModuleManifest.FileName)), "module.json", problems)!;
        Assert.Empty(problems);
        Assert.Equal(PlatformApi.Version, m.Platform);
        Assert.Equal(new ModuleVersion(1, 5, 0), m.Platform);   // 1.4.0; the pushed frame (LoopContext.Host, HostFrame.TimeNs, the host's wait)
        var names = m.Switches.Select(s => s.Name).ToHashSet();
        foreach (var players in new[] { "window", "fullscreen", "borderless", "size", "gpu", "ui-layout", "skip-intro", "no-video", "mute", "fix", "qol", "no-qol", "load", "menu", "new-game", "streamer" })
            Assert.Contains(players, names);
        // A dedicated host's switches are a player's.
        foreach (var dedicated in new[] { "headless-server", "port", "no-upnp", "mp-chronicle", "moderated", "no-auto-st", "game-name", "password", "max-players" })
            Assert.Contains(dedicated, names);
        // Not the harness's, the tools' nor the developer's (behind --developer); nor the network's other shortcuts.
        foreach (var harness in new[] { "level", "player", "headless", "frames", "frame-ms", "settle", "seed", "ui-click", "ui-type", "menu-steps", "record", "replay", "replay-check", "debug", "watch", "journal", "sweep", "developer",
                                        "host", "join", "name", "player-template", "net-bot", "crypt", "net-say", "st", "mp-game" })
            Assert.DoesNotContain(harness, names);
    }

    /// <summary>
    /// **The network's shortcuts stay a developer's**: <c>--host</c> (with a dedicated host's switches beside
    /// it, which are a player's) is refused without <c>--developer</c>, before anything runs, in one line naming <c>--host</c> alone and
    /// <c>--developer</c>; and the refusal never echoes a password.
    /// </summary>
    [Fact]
    public void A_playing_host_s_shortcut_is_refused_without_the_developer_switch()
    {
        var (code, log, output) = Run("--host", "5990", "--moderated", "--mp-chronicle", "MP_CurseDarkness", "--password", "Qx7ZebraLantern", "--no-upnp");
        Assert.True(code == 2, $"exit {code}\n{log}\n{output}");
        var refusal = Assert.Single(log.Split('\n'), l => l.Contains("refused: ", StringComparison.Ordinal));
        Assert.Contains("refused: --host is not one of the game classic's players' switches", refusal);
        Assert.Contains("needs --developer", refusal);
        foreach (var players in new[] { "--moderated", "--mp-chronicle", "--password", "--no-upnp" }) Assert.DoesNotContain(players, refusal);
        Assert.DoesNotContain("Qx7ZebraLantern", log + output);
        Assert.False(Directory.Exists(Module));
    }

    /// <summary>
    /// **<c>--streamer</c> is a player's**: declared in the manifest, taken with no <c>--developer</c>, and streamer mode is
    /// on for that run alone (the program's log says so; nothing kept, the platform's setting file is not written).
    /// </summary>
    [Fact]
    public void The_streamer_switch_is_a_players_and_turns_streamer_mode_on_for_the_run()
    {
        var game = Game();
        var (code, log, output) = Run("--streamer", "--oneone", game, "--diagnostics");
        Assert.True(code == 0, $"exit {code}\n{log}\n{output}");
        Assert.DoesNotContain("refused: ", log);
        Assert.Contains("streamer: on (--streamer, this run)", log);
        Assert.False(File.Exists(Path.Combine(User, Vitomir.Platform.Presentation.StreamerMode.FileName)));
        (code, log, output) = Run("--oneone", game, "--diagnostics");
        Assert.True(code == 0, $"exit {code}\n{log}\n{output}");
        Assert.Contains("streamer: off", log);
    }

    /// <summary>The value after <c>--password</c> (or written with <c>=</c>) is **** wherever a command line is echoed.</summary>
    [Fact]
    public void A_password_s_value_is_masked_wherever_the_command_line_is_echoed()
    {
        Assert.Equal(["--host", "--password", "****", "--name", "A"], SecretSwitches.Masked(["--host", "--password", "s3cret", "--name", "A"]));
        Assert.Equal(["--password=****", "--port", "5"], SecretSwitches.Masked(["--password=s3cret", "--port", "5"]));
        Assert.Equal(["--password", "--port", "5"], SecretSwitches.Masked(["--password", "--port", "5"]));
        Assert.Equal(["--password"], SecretSwitches.Masked(["--password"]));
        Assert.Equal("--game x --password ****", SecretSwitches.Line(["--game", "x", "--password", "pw"]));
        Assert.Equal(["--port", "s3cret"], SecretSwitches.Masked(["--port", "s3cret"]));
    }

    [Fact]
    public void An_undeclared_switch_is_refused_without_the_developer_switch_and_taken_with_it()
    {
        // Refused before anything runs: no install needed, nothing made in the module's folder, one line naming --developer.
        var (code, log, output) = Run("--headless", "--frames", "3", "--level", "P1_HAVN", "--size", "320x240");
        Assert.True(code == 2, $"exit {code}\n{log}\n{output}");
        var refusal = Assert.Single(log.Split('\n'), l => l.Contains("refused: ", StringComparison.Ordinal));
        Assert.Contains("--headless, --frames, --level are not among the game classic's players' switches", refusal);
        Assert.Contains("needs --developer", refusal);
        Assert.DoesNotContain("--size", refusal);
        Assert.False(Directory.Exists(Module));

        // With it, the same start runs (headless: the frame on the host with no window), and the developer switch is not the module's.
        var game = Game();
        (code, log, output) = Run("--developer", "--oneone", game, "--headless", "--frames", "3", "--level", "P1_HAVN", "--size", "320x240");
        Assert.True(code == 0, $"exit {code}\n{log}\n{output}");
        Assert.Contains("game classic: --developer: the module's every switch is taken", log);
        Assert.Contains("game classic: headless, 4 frames of the host run, exit code 0", log);   // the run's start, then its 3
        Assert.Equal(["--game", "/g", "--level", "P1_HAVN"], LoopCommand.OneOneArgs(["--game", "classic", "--developer", "--level", "P1_HAVN"], "/g"));
    }

    /// <summary>
    /// **A tool's switch is refused, naming vitomir-tools** (a finding: with <c>--developer</c> the tools' switches were
    /// taken as a developer's and the run went on into the game): each of the 32 (<see cref="ToolSwitches.Names"/>, held equal to the
    /// tools' own list by <c>ToolsProgramTests</c>) ends the platform's program at once, with and without <c>--developer</c>, in one
    /// line naming <c>vitomir-tools</c>, exit code 2, nothing made in the module's folder. Under the platform's own game the two it
    /// reads itself (<c>--place</c>, <c>--gait</c>) are its own; the rest are refused there too.
    /// </summary>
    [Fact]
    public void A_tools_switch_is_refused_in_one_line_naming_vitomir_tools()
    {
        Assert.Equal(33, ToolSwitches.Names.Length);                 // --recook, --template and --format the last three
        foreach (var tool in ToolSwitches.Names)
            foreach (var developer in new[] { false, true })
            {
                string[] line = developer ? ["--developer", tool, "x", "--level", "P1_HAVN"] : [tool, "x", "--level", "P1_HAVN"];
                var (code, log, output) = Run(line);
                Assert.True(code == 2, $"{string.Join(' ', line)}: exit {code}\n{log}\n{output}");
                var refusal = Assert.Single(log.Split('\n'), l => l.Contains("refused: ", StringComparison.Ordinal));
                Assert.Contains($"refused: {tool} is a tool's switch, and tools are vitomir-tools' (vitomir-tools {tool} ...", refusal);
                Assert.DoesNotContain("game classic: module", log);                // nothing loaded, nothing played
                Assert.False(Directory.Exists(Module));
            }
        Assert.Equal("--things", ToolSwitches.First(["--game", "enhanced", "--things"], moduleLoop: false));
        Assert.Null(ToolSwitches.First(["--place", "Prague", "--gait", "walk"], moduleLoop: false));
        Assert.Equal("--place", ToolSwitches.First(["--place", "Prague"], moduleLoop: true));
    }

    [Fact]
    public void A_save_made_under_the_platform_lands_in_the_module_s_folder_and_loads_from_there()
    {
        var game = Game();
        var started = DateTime.UtcNow;
        var (code, log, output) = Run("--developer", "--oneone", game, "--level", "P1_OLDT", "--player", "--headless", Path.Combine(dir, "saved.png"),
                                      "--size", "320x240", "--save", "SPT");
        Assert.True(code == 0, $"exit {code}\n{log}\n{output}");
        // The module's folder (VITOMIR_USERDATA's override kept: under it), with the game's settings, saves, logs and cache.
        Assert.Contains($"classic: the game's folder {Module} ", log);
        string save = Path.Combine(Module, "Saves", "SPT.vsave");
        Assert.True(File.Exists(save), $"no save at {save}\n{output}");
        Assert.Contains($"-> {save} (", output);
        Assert.True(File.Exists(Path.Combine(Module, "Logs", "vitomir.log")));
        Assert.NotEmpty(Directory.EnumerateFiles(Path.Combine(Module, "cache", "codex"), "*", SearchOption.AllDirectories));
        // Not the old program's layout under the same override, and nothing written beside the program.
        Assert.False(Directory.Exists(Path.Combine(User, "Saves")));
        Assert.False(File.Exists(Path.Combine(User, "vitomir.ini")));
        string beside = Path.Combine(Here, "cache");
        Assert.True(!Directory.Exists(beside) || Directory.EnumerateFiles(beside, "*", SearchOption.AllDirectories).All(f => File.GetLastWriteTimeUtc(f) < started),
                    "the scripts' cache was written beside the program");

        (code, log, output) = Run("--developer", "--oneone", game, "--load", "SPT", "--headless", Path.Combine(dir, "loaded.png"), "--size", "320x240");
        Assert.True(code == 0, $"exit {code}\n{log}\n{output}");
        Assert.Contains("load: P1_OLDT/", output);
        Assert.DoesNotContain("could not be read", output);
    }

    [Fact]
    public void The_window_s_settings_are_the_platform_s_for_the_game_a_first_start_says_so_and_they_are_kept()
    {
        var game = Game();
        string window = GameWindowSettings.PathFor(User, "classic");
        // The module's own vitomir.ini keeps a [window] (a hand's, or the old layout's): under the platform it is neither read nor written.
        const string ini = "[window]\nfullscreen = off\nx = 33\ny = 44\nwidth = 800\nheight = 600\n";
        Directory.CreateDirectory(Module);
        File.WriteAllText(Path.Combine(Module, "vitomir.ini"), ini);

        // A first start (nothing kept for the game): said. A harness run keeps nothing, so nothing is written.
        var (code, log, output) = Run("--developer", "--oneone", game, "--menu", "--skip-intro", "--frames", "20", "--mute");
        Assert.True(code == 0, $"exit {code}\n{log}\n{output}");
        Assert.Contains($"window: no window settings kept for the game classic yet ({window}): a first start (borderless full screen", log);
        Assert.DoesNotContain("800x600", output);
        Assert.False(File.Exists(window));

        // A player's run (no harness switch: kept) of a window (the player chose one: fullscreen off), closed from the main
        // menu as its close button does (`quit`): the window's place is written to the platform's file for the game. The
        // window comes up at the safeguard's fallback, whose size is the desktop's to decide (1600x900 on a 7680x1440 desktop,
        // smaller in the cloud), so nothing here clicks a button by its place or names a size (a click meant for Quit
        // at one size missed at another and the run never ended; a scripted resize is put back on Windows, the edges not
        // dragging).
        new GameWindowSettings(window).Write([new("fullscreen", "off")]);
        (code, log, output) = Run("--developer", "--oneone", game, "--menu", "--skip-intro", "--mute", "--menu-steps", "wait:30;quit");
        Assert.True(code == 0, $"exit {code}\n{log}\n{output}");
        Assert.Contains($"window: the game classic's window settings from {window}", log);
        var kept = new GameWindowSettings(window).Read();
        Assert.Equal("off", kept["fullscreen"]);
        var place = WindowPlacement.Read(kept);
        Assert.NotNull(place);
        Assert.False(string.IsNullOrEmpty(place!.Display), "the window's display was not kept");

        // The next start reads it: the window where it was left (moved down onto the display if its bar would be off it).
        (code, log, output) = Run("--developer", "--oneone", game, "--menu", "--skip-intro", "--frames", "20", "--mute");
        Assert.True(code == 0, $"exit {code}\n{log}\n{output}");
        Assert.DoesNotContain("no window settings kept", log);
        Assert.Contains($"{place.Rect.Width}x{place.Rect.Height} at {place.Rect.X},", output);
        Assert.DoesNotContain("800x600", output);
        Assert.Equal(ini, File.ReadAllText(Path.Combine(Module, "vitomir.ini")));
    }
}
