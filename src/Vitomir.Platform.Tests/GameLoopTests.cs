// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Tests;

/// <summary>
/// A module's own frame loop: the headless host gives each frame its wall time, the
/// picture and the focus, stops at the loop's first answer that is not to go on and disposes it; the loader names the module
/// that keeps the game's time and refuses a game with two; a module that offers none keeps the platform's world.
/// </summary>
public sealed class GameLoopTests
{
    sealed class Counting(int quitAt = 0) : IGameLoop
    {
        public LoopContext? Context;
        public readonly List<HostFrame> Frames = [];
        public bool Disposed;

        public void Start(LoopContext context) => Context = context;

        public LoopResult Frame(in HostFrame frame)
        {
            Frames.Add(frame);
            return quitAt > 0 && frame.Index == quitAt ? LoopResult.Quit(4, "enough") : LoopResult.Continue;
        }

        public void Dispose() => Disposed = true;
    }

    static LoopContext Context(string user) => new() { ModuleFolder = AppContext.BaseDirectory, UserFolder = user, Headless = true };

    [Fact]
    public void The_headless_host_gives_each_frame_its_time_and_the_picture_and_disposes_the_loop()
    {
        var user = Path.Combine(Path.GetTempPath(), $"vitomir-loop-{Guid.NewGuid():N}");
        try
        {
            var loop = new Counting();
            var ran = HeadlessLoop.Run(loop, Context(user), [33_000_000, 16_000_000, 1_000_000_000], 1024, 576);
            Assert.Equal(3, ran.Frames);
            Assert.Equal(LoopVerdict.Continue, ran.Last.Verdict);
            Assert.True(loop.Disposed);
            Assert.True(Directory.Exists(user));                                  // the host makes the module's user folder
            Assert.Equal(Path.Combine(user, "cache"), loop.Context!.CacheFolder);
            Assert.Equal([1L, 2L, 3L], loop.Frames.Select(f => f.Index));
            Assert.Equal([33_000_000L, 16_000_000L, 1_000_000_000L], loop.Frames.Select(f => f.ElapsedNs));
            Assert.All(loop.Frames, f =>
            {
                Assert.Equal((1024, 576), (f.PictureWidth, f.PictureHeight));
                Assert.True(f.Focused);
                Assert.False(f.CanDraw);
                Assert.Empty(f.Input);
            });
            Assert.Equal(0.033, loop.Frames[0].ElapsedSeconds, 9);
        }
        finally { if (Directory.Exists(user)) Directory.Delete(user, true); }
    }

    [Fact]
    public void The_headless_host_stops_at_the_loops_first_answer_that_is_not_to_go_on()
    {
        var user = Path.Combine(Path.GetTempPath(), $"vitomir-loop-{Guid.NewGuid():N}");
        try
        {
            var loop = new Counting(quitAt: 2);
            var ran = HeadlessLoop.Run(loop, Context(user), HeadlessLoop.Fixed(10, 33));
            Assert.Equal(2, ran.Frames);
            Assert.Equal(LoopResult.Quit(4, "enough"), ran.Last);
            Assert.True(loop.Disposed);
            Assert.Equal([33_000_000L, 33_000_000L], loop.Frames.Select(f => f.ElapsedNs));
        }
        finally { if (Directory.Exists(user)) Directory.Delete(user, true); }
    }

    [Fact]
    public void A_loops_answers_say_what_they_ask()
    {
        Assert.Equal(LoopVerdict.Continue, LoopResult.Continue.Verdict);
        Assert.Equal((LoopVerdict.Quit, 3), (LoopResult.Quit(3).Verdict, LoopResult.Quit(3).ExitCode));
        var again = LoopResult.Restart(["--level", "P1_OLDT"], "the host moved the game");
        Assert.Equal(LoopVerdict.Restart, again.Verdict);
        Assert.Equal(["--level", "P1_OLDT"], again.Args!);
        Assert.Equal(1.5, PlatformApi.Version.Major + PlatformApi.Version.Minor / 10.0, 9);   // the loop an addition (1.1.0); the audio output (1.2.0); the neutral input (1.3.0); the window's rules and settings, the developer switch (1.4.0); the pushed frame (1.5.0)
        Assert.Equal(0, LoopResult.Continue.NextFrameNs);                                   // at once, unless the loop asks the host to wait
        var later = LoopResult.ContinueAt(5_000_000_000);
        Assert.Equal((LoopVerdict.Continue, 5_000_000_000L), (later.Verdict, later.NextFrameNs));
        Assert.Null(PlatformApi.Refuses(new ModuleVersion(1, 0, 0)));                       // a module built for 1.0.0 still runs
    }

    // ---- the loader: which module keeps the game's time ----

    /// <summary>A module of the tests' own whose game keeps its own time.</summary>
    public sealed class LoopingModule : IModule
    {
        public ModuleInfo Info { get; } = new("looping", "1.0.0", "1");
        public ModuleStamp Stamp { get; } = new("looping", "-", "1", false, "");
        public void Configure(ModuleConfigContext context) { }
        public void Register(WorldBuilder world) { }
        public IGameLoop? Loop { get; } = new Counting();
    }

    /// <summary>Another, for the game that has two.</summary>
    public sealed class OtherLoopingModule : IModule
    {
        public ModuleInfo Info { get; } = new("other", "1.0.0", "1");
        public ModuleStamp Stamp { get; } = new("other", "-", "1", false, "");
        public void Configure(ModuleConfigContext context) { }
        public void Register(WorldBuilder world) { }
        public IGameLoop? Loop { get; } = new Counting();
    }

    /// <summary>A module of the platform's world (as the enhanced one): no loop.</summary>
    public sealed class WorldModule : IModule
    {
        public ModuleInfo Info { get; } = new("worldly", "1.0.0", "1");
        public ModuleStamp Stamp { get; } = new("worldly", "-", "1", false, "");
        public void Configure(ModuleConfigContext context) { }
        public void Register(WorldBuilder world) { }
    }

    static string AddCode(string root, string folder, string id, Type type)
    {
        var dir = Directory.CreateDirectory(Path.Combine(root, folder)).FullName;
        var dll = Path.GetFileName(typeof(GameLoopTests).Assembly.Location);
        File.WriteAllText(Path.Combine(dir, ModuleManifest.FileName),
            $$"""{ "id": "{{id}}", "name": "module {{id}}", "version": "1.0.0", "platform": "1.1.0", "kind": "rules", "dependencies": {}, "assembly": "bin/{{dll}}", "type": "{{type.FullName}}" }""");
        Directory.CreateDirectory(Path.Combine(dir, "bin"));
        File.Copy(typeof(GameLoopTests).Assembly.Location, Path.Combine(dir, "bin", dll));
        return dir;
    }

    static LoadedGame Load(string root, params string[] modules) => ModuleLoader.Load(new ModuleLoadOptions
    {
        ModuleFolders = [root],
        Game = new GameList("test", "a test game", modules),
    });

    [Fact]
    public void The_loader_names_the_module_that_keeps_the_games_time_and_refuses_a_game_with_two()
    {
        var root = Directory.CreateTempSubdirectory("vitomir-loops-").FullName;
        try
        {
            AddCode(root, "Looping", "looping", typeof(LoopingModule));
            AddCode(root, "Other", "other", typeof(OtherLoopingModule));
            AddCode(root, "Worldly", "worldly", typeof(WorldModule));

            var one = Load(root, "looping");
            Assert.NotNull(one.Loop);
            Assert.Equal("looping", one.Loop!.Value.Module.Manifest.Id);
            Assert.IsType<Counting>(one.Loop.Value.Loop);

            Assert.Null(Load(root, "worldly").Loop);                              // the platform's world, as the enhanced game
            Assert.Null(((IModule)new WorldModule()).Loop);                       // the default: no loop

            var two = Assert.Throws<ModuleCheckException>(() => Load(root, "looping", "other"));
            Assert.Contains("modules looping and other each offer their own frame loop", Assert.Single(two.Problems));
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    [Fact]
    public void A_modules_native_library_is_looked_for_by_the_systems_names()
    {
        var names = ModuleDependencies.NativeNames("ikvm").ToList();
        Assert.Equal("ikvm", names[0]);
        if (OperatingSystem.IsWindows()) Assert.Contains("ikvm.dll", names);
        else if (OperatingSystem.IsMacOS()) Assert.Contains("libikvm.dylib", names);
        else Assert.Equal(["ikvm", "ikvm.so", "libikvm.so"], names);
        Assert.DoesNotContain(ModuleDependencies.NativeNames("libikvm.so"), n => n.StartsWith("liblib", StringComparison.Ordinal));
    }
}
