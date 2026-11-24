// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Modules;

namespace Vitomir.Modules.Classic.Tests;

/// <summary>
/// Which of a player's switches only set a run up, and which start something (RunSettings): a run given
/// nothing but settings is the front end, the title screen and the main menu. An earlier build found `--streamer` alone going straight
/// into a new game at the haven on a Windows PC, as `--skip-intro`, `--no-video` and `--devmode` alone had; so every switch the
/// Classic module's manifest declares is sorted here, and a switch added to the manifest without being sorted fails.
/// </summary>
public sealed class FrontEndSwitchesTests
{
    static string Folder => Path.Combine(ModuleLoader.ProgramModules, "Classic");

    // Settings: alone (with the switch's value where it takes one), the front end.
    static readonly Dictionary<string, string?> settings = new()
    {
        ["window"] = null, ["fullscreen"] = null, ["borderless"] = "1280x720", ["size"] = "1280x720", ["gpu"] = "vulkan",
        ["ui-layout"] = "fit", ["menu-lettering"] = "ours", ["camera-up"] = "-10", ["camera-steps"] = "off", ["camera-out"] = "2", ["gamma"] = "1.2", ["skip-intro"] = null, ["no-video"] = null,
        ["ffmpeg"] = "ffmpeg", ["mute"] = null, ["fix"] = "all", ["qol"] = "freecamera", ["no-qol"] = "freecamera",
        ["menu"] = null, ["credits"] = null, ["devmode"] = null, ["log"] = "Debug", ["log-folder"] = "logs", ["log-keep"] = "5",
        ["no-log-file"] = null, ["no-log-console"] = null, ["config"] = "vitomir.ini", ["no-upnp"] = null, ["streamer"] = null,
    };

    // Starts: a game, a host, a report or a bundle; or a dedicated host's own settings, which go with --headless-server.
    static readonly string[] starts = ["new-game", "load", "diagnostics", "rules", "headless-server", "port", "mp-chronicle", "moderated",
        "no-auto-st", "game-name", "password", "max-players", "log-bundle"];

    [Fact]
    public void Every_switch_the_manifest_declares_is_sorted_as_a_setting_or_a_start()
    {
        var problems = new List<string>();
        var manifest = ModuleManifest.Read(Folder, problems);
        Assert.Empty(problems);
        var unsorted = manifest!.Switches.Select(s => s.Name).Where(n => !settings.ContainsKey(n) && !starts.Contains(n)).ToList();
        Assert.Empty(unsorted);
    }

    [Fact]
    public void A_setting_alone_is_the_front_end()
    {
        var wrong = new List<string>();
        foreach (var (name, value) in settings)
        {
            string[] given = value is null ? ["--game", "C:/Games/VTMR", "--" + name] : ["--game", "C:/Games/VTMR", "--" + name, value];
            if (!RunSettings.FrontEndWanted(given)) wrong.Add(name);
        }
        Assert.Empty(wrong);
    }

    [Fact]
    public void The_developers_settings_alone_are_the_front_end_too()
    {
        Assert.True(RunSettings.FrontEndWanted(["--log-window"]));
        Assert.True(RunSettings.FrontEndWanted(["--no-predict"]));
        Assert.True(RunSettings.FrontEndWanted(["--net-sim", "latency=200,jitter=20,loss=2"]));
        Assert.True(RunSettings.FrontEndWanted(["--streamer", "--window", "--skip-intro"]));
        // A frame dump on a replay's line: the recording began at the title screen, and so does its film.
        Assert.True(RunSettings.FrontEndWanted(["--window", "--frame-dump", "frames", "--frame-dump-fps", "60", "--frame-dump-log", "log.jsonl"]));
    }

    [Fact]
    public void A_setting_given_without_its_value_never_takes_the_next_switch()
    {
        // `--borderless` takes a size; given bare before `--player-module`, it took that switch as its size, and the next
        // run read `--player-module` as a module's folder.
        string[] given = ["--game", "G", "--borderless", "--player-module", "A", "--player-module", "B"];
        Assert.Equal(given, RunSettings.Settings(given));
        Assert.True(RunSettings.FrontEndWanted(given));
        Assert.Equal(["--size", "1920x1080", "--borderless"], RunSettings.Settings(["--size", "1920x1080", "--borderless"]));
    }

    [Fact]
    public void A_start_is_still_a_start()
    {
        Assert.False(RunSettings.FrontEndWanted(["--streamer", "--new-game"]));
        Assert.False(RunSettings.FrontEndWanted(["--skip-intro", "--level", "P1_HAVN"]));
        Assert.False(RunSettings.FrontEndWanted(["--streamer", "--join", "127.0.0.1", "--net-sim", "latency=200"]));
        Assert.False(RunSettings.FrontEndWanted(["--headless-server", "--streamer", "--no-upnp"]));
    }
}
