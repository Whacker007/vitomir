// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Modules;
using Vitomir.Platform.Windowing;

namespace Vitomir.Platform.Tests;

/// <summary>
/// **The window's settings kept by the platform, per game**:
/// the file's place, a first start (nothing kept: the start the old program's first start was,
/// borderless full screen at the desktop's own size), the keys written and kept with every other key in its place, a file that
/// does not read, a replay's settings that never write; and the developer switch's rule.
/// </summary>
public sealed class GameWindowSettingsTests : IDisposable
{
    readonly string root = Directory.CreateTempSubdirectory("vitomir-window-settings-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Each_game_keeps_its_window_in_its_own_file_under_the_platform_s_user_data()
    {
        Assert.Equal(Path.Combine(root, "Games", "classic", "window.json"), GameWindowSettings.PathFor(root, "classic"));
        Assert.NotEqual(GameWindowSettings.PathFor(root, "classic"), GameWindowSettings.PathFor(root, "enhanced"));
    }

    [Fact]
    public void A_first_start_keeps_nothing_and_starts_as_vitomir_exe_s_first_start()
    {
        var said = new List<string>();
        var window = new GameWindowSettings(GameWindowSettings.PathFor(root, "classic"), said.Add);
        Assert.False(window.Kept);
        Assert.Equal(0, window.Read().Count);
        // Nothing kept is the rules' first start: full screen, at no mode (borderless at the desktop's own size), and kept.
        var kept = FullScreen.Read(window.Read());
        Assert.Equal(FullScreen.Default, kept);
        var start = FullScreen.Start(kept, replaying: false, size: null, harness: false, windowFlag: false, fullscreenFlag: false);
        Assert.True(start is { Restore: true, Keep: true, Full: true, Mode: null });
        Assert.Empty(said);
        Assert.False(File.Exists(window.Path));
    }

    [Fact]
    public void What_the_window_writes_is_kept_between_starts_with_every_other_key_in_its_place()
    {
        string path = GameWindowSettings.PathFor(root, "classic");
        var first = new GameWindowSettings(path);
        // His middle screen of the Surround desktop, a window with its title bar, then Alt+Enter's full screen off.
        WindowPlacement.Write(first, new WindowPlace(new DisplayRect(2773, 180, 1920, 1080), "1", false));
        FullScreen.WriteOn(first, false);
        Assert.True(first.Kept);

        // The next start reads the same keys afresh, from a settings object of its own.
        var next = new GameWindowSettings(path);
        var keys = next.Read();
        Assert.Equal(new WindowPlace(new DisplayRect(2773, 180, 1920, 1080), "1", false), WindowPlacement.Read(keys));
        Assert.False(FullScreen.Read(keys).On);
        var order = File.ReadAllLines(path).Where(l => l.Contains(':')).Select(l => l.Trim().Split('"')[1]).ToList();

        // A key written again in another case keeps its own place and spelling; a new key goes at the end.
        next.Write([new("X", "2560"), new("worldsize", "1920x1080")]);
        var again = new GameWindowSettings(path).Read();
        Assert.Equal("2560", again["x"]);
        Assert.Equal("1920x1080", again["worldsize"]);
        Assert.Equal([.. order, "worldsize"], File.ReadAllLines(path).Where(l => l.Contains(':')).Select(l => l.Trim().Split('"')[1]).ToList());

        // Nothing changed: the file is not written again.
        var written = File.GetLastWriteTimeUtc(path);
        File.SetLastWriteTimeUtc(path, written.AddMinutes(-5));
        next.Write([new("x", "2560")]);
        Assert.Equal(written.AddMinutes(-5), File.GetLastWriteTimeUtc(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void A_file_that_does_not_read_is_a_first_start_said_once()
    {
        string path = GameWindowSettings.PathFor(root, "classic");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "fullscreen = off");
        var said = new List<string>();
        var window = new GameWindowSettings(path, said.Add);
        Assert.Equal(0, window.Read().Count);
        Assert.Equal(0, window.Read().Count);
        Assert.Single(said);
        Assert.Contains("could not be read", said[0]);
        // A number where the rules write a string is read as its text.
        File.WriteAllText(path, """{ "x": 10, "y": "20", "fullscreen": "off" }""");
        Assert.Equal(("10", "20"), (window.Read()["x"], window.Read()["Y"]));
    }

    [Fact]
    public void A_replay_s_window_settings_read_but_never_write()
    {
        string path = GameWindowSettings.PathFor(root, "classic");
        var kept = new GameWindowSettings(path);
        FullScreen.WriteOn(kept, false);
        var replay = new ReadOnlyWindowSettings(kept);
        Assert.False(FullScreen.Read(replay.Read()).On);
        string before = File.ReadAllText(path);
        FullScreen.WriteOn(replay, true);
        WindowPlacement.Write(replay, new WindowPlace(new DisplayRect(0, 0, 640, 480), "0", false));
        Assert.Equal(before, File.ReadAllText(path));
    }

    [Fact]
    public void A_switch_the_manifest_does_not_declare_needs_the_developer_switch()
    {
        ModuleSwitch[] declared = [new("window", "a window"), new("size", "WxH"), new("fix", "fixes")];
        // Values are not switches; each undeclared switch once, in order.
        Assert.Empty(DeveloperSwitch.Undeclared(declared, ["--window", "--size", "1920x1080", "--fix", "all"]));
        Assert.Equal(["--ui-click", "--level"],
            DeveloperSwitch.Undeclared(declared, ["--size", "640x480", "--ui-click", "10,10", "--level", "P1_OLDT", "--ui-click", "1,1", "--", "-x"]));
        string line = DeveloperSwitch.Refusal("classic", ["--ui-click"]);
        Assert.Contains("--ui-click is not one of the game classic's players' switches", line);
        Assert.EndsWith("needs --developer", line);
        Assert.DoesNotContain('\n', line);
        Assert.Equal("--developer", DeveloperSwitch.Flag);
    }
}
