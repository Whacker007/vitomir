// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Diagnostics;
using System.Reflection;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// **The Classic module, headless under the loader**. These tests run in a
/// process that has neither the engine nor the scripting of its own (the program's tests reference the program, which
/// compiles against neither), so the module, its engine, its scripting, IKVM and the converted ecj are all found in
/// <c>Modules\Classic\bin\</c> beside the tests, laid out as beside the program. They prove: the game loads through the
/// loader with its libraries from its bin; a place loads, its scripts compile into the module's own cache and run, and
/// frames step; and **each frame's digest equals the window's** (the platform's program recorded in its window with the
/// same launch, seed and fixed frame time; earlier, the old program's, against the headless loop's copy of the frame, retired),
/// frame for frame, in places of each kind. The proof needs the 1.1 install (VTMR_GAME_DIR) and a GPU (the world steps only on a
/// frame whose picture was had, a window's or the headless host's own target).
/// </summary>
public sealed class ClassicHeadlessTests
{
    static readonly string Here = AppContext.BaseDirectory;

    /// <summary>The module's user folder for these tests (its scripts' cache is kept between runs, as the scripting tests' is).</summary>
    static readonly string UserFolder = Path.Combine(Path.GetTempPath(), "vitomir-classic-host-tests");

    static LoadedGame LoadClassic(GameInstall? install, List<string>? said = null) => ModuleLoader.Load(new ModuleLoadOptions
    {
        ModuleFolders = [ModuleLoader.ProgramModules],
        Game = ModuleLoader.FindGame("classic", [ModuleLoader.ProgramGames]),
        OneOne = install,
        Say = line => said?.Add(line),
    });

    [Fact]
    public void The_classic_game_loads_through_the_loader_with_its_libraries_from_its_bin()
    {
        // This process has no engine of its own: what the module needs must come from its bin.
        var tpa = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "").Split(Path.PathSeparator).Select(Path.GetFileNameWithoutExtension).ToHashSet();
        Assert.DoesNotContain("Vitomir.Engine", tpa);
        Assert.DoesNotContain("Vitomir.Scripting", tpa);

        var bin = Path.Combine(ModuleLoader.ProgramModules, "Classic", "bin");
        foreach (var file in new[] { "Vitomir.Modules.Classic.dll", "Vitomir.Engine.dll", "Vitomir.Scripting.dll", "IKVM.Runtime.dll", "IKVM.Java.dll", "ecj.dll",
                                     "NLayer.dll", "DotRecast.Recast.dll", "BCnEncoder.dll", "ikvm.properties" })
            Assert.True(File.Exists(Path.Combine(bin, file)), $"{file} is not laid out in {bin}");
        Assert.True(Directory.Exists(Path.Combine(bin, "ikvm")), $"IKVM's runtime folder is not laid out in {bin}");
        Assert.Contains(ModuleDependencies.NativeNames("ikvm"), n => File.Exists(Path.Combine(bin, n)));
        Assert.False(File.Exists(Path.Combine(bin, "Vitomir.Platform.dll")), "the platform is the program's; a module never ships its own");

        var game = LoadClassic(GameInstall.TryOpen());
        var (module, loop) = Assert.NotNull(game.Loop);
        Assert.Equal("classic", module.Manifest.Id);
        Assert.Equal(ModuleKind.Rules, module.Manifest.Kind);
        Assert.Equal(Path.Combine(ModuleLoader.ProgramModules, "Classic"), module.Manifest.Folder);
        Assert.StartsWith(bin, module.Code!.GetType().Assembly.Location, StringComparison.Ordinal);
        Assert.NotNull(loop);
        Assert.Contains(bin, ModuleDependencies.Folders);
    }

    /// <summary>
    /// **The windowed game's door** (the pushed frame): the Classic module's loop
    /// plays in a window (<see cref="IGameLoop.Windowed"/>) when the program hands it its host at its start and runs its frames (it
    /// was an <c>IClassicGame</c> with a loop of its own until then); the frame controller
    /// (<c>Vitomir.Classic</c>) is laid out in the module's bin, and the platform's renderer and SDL, managed and native, are not
    /// (the program's one copy serves). **Nor is the old glue** (<c>Vitomir.Render</c>): the module never draws, the
    /// host's renderer draws what it says.
    /// </summary>
    [Fact]
    public void The_classic_loop_is_a_windowed_game_with_its_controller_in_its_bin()
    {
        var bin = Path.Combine(ModuleLoader.ProgramModules, "Classic", "bin");
        Assert.True(File.Exists(Path.Combine(bin, "Vitomir.Classic.dll")), $"Vitomir.Classic.dll is not laid out in {bin}");
        Assert.False(File.Exists(Path.Combine(bin, "Vitomir.Render.dll")), "Vitomir.Render.dll is laid out in the module's bin: the module draws nothing");
        foreach (var file in new[] { "Vitomir.Platform.Render.dll", "SDL3-CS.dll", "libSDL3.so", "SDL3.dll" })
            Assert.False(File.Exists(Path.Combine(bin, file)), $"{file} is the program's; the module never ships its own");
        var (_, loop) = Assert.NotNull(LoadClassic(GameInstall.TryOpen()).Loop);
        Assert.True(loop.Windowed, "the Classic module's loop plays in a window on the program's host");
        loop.Dispose();
    }

    /// <summary>
    /// **The levels generator's cell code stays off under the platform** (switched off, no player's switch).
    /// The cell code is the engine's and ships in the module's bin; a cell level's world is drawn only when the host says so
    /// (<c>Vitomir.Classic.CellWorlds.Make</c>, set by a host when its GPU opens: <c>SdlHost.Opened</c>), and the platform's host does
    /// not (only <c>vitomir-tools</c>', for its tools); the manifest declares none of the cell levels' switches; and in this process,
    /// with the module loaded, nothing has switched it on. The controller's side is <c>ControllerBoundaryTests</c>'.
    /// </summary>
    [Fact]
    public void The_levels_threads_cell_code_is_in_and_switched_off()
    {
        var host = typeof(LoopCommand).Assembly.GetType("Vitomir.Platform.Host.ClassicHost", throwOnError: true)!;
        var opened = host.GetMethod("Opened", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.Equal("SdlHost", opened.DeclaringType!.Name);
        var problems = new List<string>();
        var m = ModuleManifest.Parse(File.ReadAllText(Path.Combine(ModuleLoader.ProgramModules, "Classic", ModuleManifest.FileName)), "module.json", problems)!;
        var names = m.Switches.Select(s => s.Name).ToHashSet();
        foreach (var cells in new[] { "level-l2", "cook-l2", "l2", "convert-l2", "relayout-l2", "stream-fly", "probes-at", "place" })
            Assert.DoesNotContain(cells, names);
        var (_, loop) = Assert.NotNull(LoadClassic(GameInstall.TryOpen()).Loop);
        try
        {
            var bin = Path.Combine(ModuleLoader.ProgramModules, "Classic", "bin");
            Assert.True(File.Exists(Path.Combine(bin, "Vitomir.Engine.dll")), "the engine (and its cell code) is not in the module's bin");
            var context = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(loop.GetType().Assembly)!;
            var worlds = context.LoadFromAssemblyName(new AssemblyName("Vitomir.Classic")).GetType("Vitomir.Classic.CellWorlds", throwOnError: true)!;
            Assert.Null(worlds.GetProperty("Make")!.GetValue(null));
        }
        finally { loop.Dispose(); }
    }

    /// <summary>1.1's command line from the program's: the program's own switches out, the install found named as 1.1's <c>--game</c>.</summary>
    [Fact]
    public void The_windowed_game_is_handed_1_1_s_command_line()
    {
        Assert.Equal(["--game", "/vtmr", "--level", "P1_OLDT", "--size", "1280x720", "--player"],
            LoopCommand.OneOneArgs(["--game", "classic", "--oneone", "/x", "--level", "P1_OLDT", "--modules", "/m", "--size", "1280x720", "--log-file", "l.txt", "--player"], "/vtmr"));
        Assert.Equal(["--menu"], LoopCommand.OneOneArgs(["--game", "classic", "--menu"], null));
    }

    /// <summary>
    /// **A place loaded, its scripts compiled into the module's own cache, frames stepped** (through the one frame, the
    /// program's host with no window, in a process of its own: the module's loop no longer has a frame of its own to run in this one).
    /// A fresh user folder: the scripts are compiled there, by the ecj in the module's bin, into its cache, never the program's folder.
    /// </summary>
    [Fact]
    public void The_classic_loop_loads_a_place_compiles_its_scripts_into_its_own_cache_and_steps_frames()
    {
        var game = GameInstall.Locate();
        Assert.SkipWhen(game is null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"vitomir-classic-fresh-{Guid.NewGuid():N}")).FullName;
        try
        {
            string user = Path.Combine(dir, "user");
            var (code, said) = RunClassic(dir, ["--level", "P1_HAVN", "--frames", "30", "--frame-ms", "33"], game!, user);
            Assert.True(code == 0, said);
            Assert.Contains("game classic: headless, 31 frames of the host run, exit code 0", said);   // the run's start, then its 30
            var cache = Path.Combine(user, "Modules", "classic", "cache", "codex");
            Assert.True(Directory.Exists(cache) && Directory.EnumerateFileSystemEntries(cache).Any(), $"no scripts' cache at {cache}");
            Assert.False(Directory.Exists(Path.Combine(Here, "cache")), "the scripts' cache was made in the program's folder");
            // The shipped classes (Codex.nob's), bridged into the cache; the source path's sources with VITOMIR_SCRIPT_SOURCES=1.
            string what = Environment.GetEnvironmentVariable("VITOMIR_SCRIPT_SOURCES") is { Length: > 0 } v && v != "0" ? " sources, " : " classes (the shipped ones, bridged), ";
            Assert.Contains(said.Split('\n'), l => l.StartsWith("scripts: ", StringComparison.Ordinal) && l.Contains(what, StringComparison.Ordinal) && l.Contains("compiled in", StringComparison.Ordinal));
            Assert.Contains($"installed settings from {Path.Combine(ModuleLoader.ProgramModules, "Classic")}", said);
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    /// <summary>The platform's program beside the tests (its Modules\ and Games\ are the tests'): <c>vitomir-platform</c>.</summary>
    static string Platform => Path.Combine(Here, OperatingSystem.IsWindows() ? "vitomir-platform.exe" : "vitomir-platform");

    /// <summary>Runs a program to its end with these arguments and environment; its exit code and what it printed.</summary>
    static (int Code, string Output) Run(string program, string folder, IEnumerable<string> args, Dictionary<string, string> env)
    {
        var start = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = folder };
        foreach (var a in args) start.ArgumentList.Add(a);
        foreach (var (k, v) in env) start.Environment[k] = v;
        // No window where there is no screen to put one on (a container, a CI box): SDL's own offscreen driver.
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS() && Environment.GetEnvironmentVariable("DISPLAY") is null
            && Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") is null && Environment.GetEnvironmentVariable("SDL_VIDEO_DRIVER") is null)
            start.Environment["SDL_VIDEO_DRIVER"] = "offscreen";
        using var p = new Process { StartInfo = start };
        var err = new System.Text.StringBuilder();
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (err) err.AppendLine(e.Data); };
        p.Start();
        p.BeginErrorReadLine();
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        lock (err) return (p.ExitCode, output + err);
    }

    /// <summary>
    /// The Classic game run by the platform's program, headless, in a process of its own (one Classic per process: the
    /// engine keeps its launch in statics, and a second in one process starts from the first's), its log read back.
    /// </summary>
    static (int Code, string Log) RunClassic(string folder, IEnumerable<string> args, string game, string? user = null)
    {
        string log = Path.Combine(folder, $"platform-{Guid.NewGuid():N}.log");
        var (code, output) = Run(Platform, folder, ["--game", "classic", "--developer", "--headless", "--oneone", game, "--log-file", log, .. args],
            new() { ["VITOMIR_USERDATA"] = user ?? UserFolder });                 // the module's user folder: <user data>\Modules\classic
        // The program's log, then what the game printed (the frame's own lines, the digest's among them, are the controller's).
        return (code, (File.Exists(log) ? File.ReadAllText(log) : "") + "\n" + output);
    }

    /// <summary>The places the proof runs: a quiet one, a street with its people and the player, a new game, a multiplayer chronicle's.</summary>
    public static TheoryData<string, string[], int> Places => new()
    {
        { "haven", ["--level", "P1_HAVN"], 60 },
        { "old-town", ["--level", "P1_OLDT", "--player"], 120 },
        { "new-game", ["--new-game"], 150 },
        { "lobby-multiplayer", ["--level", "N1_LOBY", "--chronicle", "MP_LeavesThree", "--player"], 60 },
    };

    /// <summary>
    /// **Headless is the window's frame** (the headless loop's copy retired): the same launch recorded in
    /// the platform's window (<c>--record</c>: the tape's digest, every frame) and run headless (<c>--headless</c>: the same frame on the
    /// host with no window), each frame's digest checked against the window's. Earlier, the headless run was a copy of the frame of its
    /// own, checked against the old program's window here; the old program is needed no more (the replay tapes are the cross-program check).
    /// </summary>
    [Theory]
    [MemberData(nameof(Places))]
    public void Each_frames_digest_equals_the_windowed_runs(string name, string[] place, int frames)
    {
        var game = GameInstall.Locate();
        Assert.SkipWhen(game is null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        Assert.SkipWhen(Environment.GetEnvironmentVariable("VITOMIR_NO_REPLAYS") == "1", "VITOMIR_NO_REPLAYS=1: the windowed runs left out");

        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"vitomir-classic-proof-{name}-{Guid.NewGuid():N}")).FullName;
        try
        {
            // The platform's program in its window, recorded: the same launch, seed 1, a fixed 33 ms frame, no sound out, no films.
            string tape = Path.Combine(dir, name + ".tape");
            string[] launch = [.. place, "--frames", frames.ToString(System.Globalization.CultureInfo.InvariantCulture), "--frame-ms", "33", "--seed", "1", "--size", "1024x576"];
            var (recorded, recording) = Run(Platform, dir, ["--game", "classic", "--developer", "--oneone", game!, .. launch, "--mute", "--no-video", "--record", tape],
                new() { ["VITOMIR_USERDATA"] = UserFolder, ["SDL_AUDIODRIVER"] = "dummy" });
            Assert.True(recorded == 0 && File.Exists(tape + ".digest"), $"the windowed run (exit {recorded}): {string.Join(" | ", recording.Split('\n').TakeLast(3))}");

            // The same through the program headless, each frame's digest checked against the window's.
            var (code, log) = RunClassic(dir, [.. launch, "--digest-check", tape + ".digest"], game!);
            Assert.True(code == 0, $"{name}: exit {code}\n{string.Join("\n", log.Split('\n').Where(l => l.Contains("digest", StringComparison.Ordinal) || l.Contains("failed", StringComparison.Ordinal)))}");
            Assert.Contains($"classic: digest check: {frames} frames agreed, none differed", log);
            Assert.Contains($"game classic: headless, {frames + 1} frames of the host run, exit code 0", log);   // the run's start, then its frames
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    [Fact]
    public void The_same_launch_run_twice_by_the_program_gives_the_same_digest()
    {
        // Needs only the install: the module's own determinism, under the program, where no GPU is had for the old one.
        var game = GameInstall.Locate();
        Assert.SkipWhen(game is null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"vitomir-classic-twice-{Guid.NewGuid():N}")).FullName;
        try
        {
            string digest = Path.Combine(dir, "first.digest");
            string[] launch = ["--level", "P1_OLDT", "--player", "--frames", "90", "--frame-ms", "33", "--seed", "1", "--size", "1024x576"];
            var (first, firstLog) = RunClassic(dir, [.. launch, "--digest", digest], game!);
            Assert.True(first == 0, firstLog);
            Assert.True(File.Exists(digest));
            var (second, secondLog) = RunClassic(dir, [.. launch, "--digest-check", digest], game!);
            Assert.True(second == 0, secondLog);
            Assert.Contains("classic: digest check: 90 frames agreed, none differed", secondLog);
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    [Fact]
    public void The_program_leaves_the_enhanced_game_alone()
    {
        var settings = new PlatformSettings();
        using var log = new HostLog(Path.Combine(Path.GetTempPath(), $"vitomir-loopcommand-{Guid.NewGuid():N}.log"));
        // The enhanced game (and no --game): not the loop's, the program goes on as before. (the Classic game in a
        // window is played on the program's host, which a test process does not open: its door and its command line are
        // The_classic_loop_is_a_windowed_game_with_its_controller_in_its_bin and The_windowed_game_is_handed_1_1_s_command_line.)
        Assert.Null(LoopCommand.Run(new Cli(["--headless", "x.png"]), settings, log));
        Assert.Null(LoopCommand.Run(new Cli(["--game", "enhanced", "--headless", "x.png"]), settings, log));
    }
}
