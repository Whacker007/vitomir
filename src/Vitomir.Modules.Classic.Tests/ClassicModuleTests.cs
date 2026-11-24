// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.World;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Modules;

namespace Vitomir.Modules.Classic.Tests;

/// <summary>
/// The Classic module's own pieces: its manifest and code agree and the loader takes it;
/// it keeps its own time and registers nothing in the platform's world; and it has no frame of its own to run in a test's
/// process: every frame, headless too, is the controller's on the program's host (its runs, its folders and its digests are the
/// program's tests', ClassicHeadlessTests and ClassicFoldersTests).
/// </summary>
[Collection("classic")]
public sealed class ClassicModuleTests
{
    static string Folder => Path.Combine(ModuleLoader.ProgramModules, "Classic");

    [Fact]
    public void Its_manifest_and_its_code_agree_and_the_loader_takes_it()
    {
        var problems = new List<string>();
        var manifest = ModuleManifest.Read(Folder, problems);
        Assert.Empty(problems);
        Assert.NotNull(manifest);
        var module = new ClassicModule();
        Assert.Equal("classic", manifest!.Id);
        Assert.Equal(ModuleKind.Rules, manifest.Kind);
        Assert.Equal(manifest.Version.ToString(), module.Info.Version);
        Assert.Null(PlatformApi.Refuses(manifest.Platform));
        Assert.Equal("bin/Vitomir.Modules.Classic.dll", manifest.Assembly);
        Assert.Equal(typeof(ClassicModule).FullName, manifest.Type);

        var game = ModuleLoader.Load(new ModuleLoadOptions { ModuleFolders = [ModuleLoader.ProgramModules], Game = new GameList("classic", "test", ["classic"]) });
        var (loaded, loop) = Assert.NotNull(game.Loop);
        Assert.Equal("classic", loaded.Manifest.Id);
        Assert.IsType<ClassicLoop>(loop);
        Assert.Same(loop, loaded.Code!.Loop);                                     // one loop for the module's life
    }

    [Fact]
    public void It_keeps_its_own_time_and_puts_nothing_in_the_platforms_world()
    {
        var module = new ClassicModule();
        module.Configure(new ModuleConfigContext { Assets = AssetRegistry.ForModule(Folder).For("classic") });
        var world = new WorldBuilder(1);
        int before = WorldBuilder.RunOrder(world.Peek).Count;
        module.Register(world);
        Assert.Equal(before, WorldBuilder.RunOrder(world.Build()).Count);
        Assert.NotNull(module.Loop);
        Assert.Equal("classic", module.Stamp.ModuleId);
    }

    /// <summary>
    /// **No frame of its own** (the headless loop's copy retired): the loop plays only on the program's host
    /// (in a window, or headless on the host with no window), so started without one it refuses in one line, naming where it is played.
    /// </summary>
    [Fact]
    public void Without_the_programs_host_it_refuses_saying_where_it_is_played()
    {
        using var loop = new ClassicLoop();
        var refused = Assert.ThrowsAny<Exception>(() => loop.Start(new LoopContext
        {
            ModuleFolder = Folder, UserFolder = Path.Combine(Path.GetTempPath(), "vitomir-classic-module-tests"), Headless = true,
            Args = ["--level", "P1_OLDT", "--player"],
        }));
        Assert.Equal("ClassicStartException", refused.GetType().Name);
        Assert.Contains("played on the program's host (vitomir-platform --game classic;", refused.Message);
        Assert.True(loop.Windowed);
    }

    /// <summary>
    /// **A headless run's line**: the harness's seed unless one is given, no sound and no films, as the copy's was; a network game
    /// and a tape refused in one line each (a network game's windowless runs and a tape are a window's run's).
    /// </summary>
    [Fact]
    public void A_headless_runs_line_is_the_harnesss_and_refuses_a_network_game_and_a_tape()
    {
        Assert.Equal(["--level", "P1_OLDT", "--seed", "1", "--mute", "--no-video"], ClassicLoop.Headless(["--level", "P1_OLDT"]));
        Assert.Equal(["--seed", "7", "--mute", "--no-video"], ClassicLoop.Headless(["--seed", "7", "--mute"]));
        foreach (var network in ClassicLoop.NetworkSwitches)
            Assert.Contains("a network game is not played headless", Assert.ThrowsAny<Exception>(() => ClassicLoop.Headless([network, "x"])).Message);
        foreach (var tape in ClassicLoop.TapeSwitches)
            Assert.Contains("a tape works on a run that shows its picture", Assert.ThrowsAny<Exception>(() => ClassicLoop.Headless([tape, "x.tape"])).Message);
    }
}

[CollectionDefinition("classic", DisableParallelization = true)]
public sealed class ClassicCollection;
