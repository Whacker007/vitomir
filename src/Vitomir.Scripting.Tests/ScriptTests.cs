// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Scripting;

namespace Vitomir.Scripting.Tests;

/// <summary>The game's own Java scripts, compiled by the engine and run against it.</summary>
[Collection("scripts")]
public class ScriptTests(Scripts s)
{
    [Fact]
    public void The_players_scripts_compile()
    {
        s.Ready();
        var build = s.Runtime!.Build;
        Assert.True(build.Sources > 400, $"expected the game's script tree, found {build.Sources} sources");
        Assert.Equal(build.Sources, build.Classes);
        // 447 methods are `native` in the SDK's 18 Codex classes; the bridge replaces every one.
        Assert.Equal(447, build.Natives);
    }

    [Fact]
    public void The_one_file_a_modern_compiler_refuses_is_fixed_in_a_shadow_copy()
    {
        s.Ready();
        // JDK 1.1 javac let DisciplineEyesOfChaos read a local that is unassigned on one switch
        // path. On a cache hit there is nothing to report, which is why this only checks the shape.
        Assert.All(s.Runtime!.Build.Notes, n => Assert.Contains("initialised local", n));
    }

    [Fact]
    public void Every_scene_script_runs_its_beginscene()
    {
        s.Ready();
        // A clean set of natives first. The collection's other tests each leave the shared engine
        // pointing at the place they entered — and at an install they have since closed — so which
        // of them ran last decided whether a scene's quest load read a disposed archive.
        _ = new CodexScenes(s.Runtime!);
        string[] chronicles = ["PragueChronicle", "ViennaChronicle", "LondonChronicle", "NewYorkChronicle",
                               "Prague2Chronicle", "MP_LOTChronicle", "MP_CTDChronicle"];
        var objs = chronicles.Select(c => s.Runtime!.Instantiate(c)).ToArray();
        Assert.All(objs, Assert.NotNull);

        int scenes = 0, ran = 0;
        var failed = new List<string>();
        foreach (var name in s.Runtime!.ClassNames())
        {
            if (!s.Runtime.IsScene(name)) continue;
            scenes++;
            string? last = null;
            foreach (var chronicle in objs)
            {
                s.Engine.On("Codex.GetChronicleScript", (_, _) => chronicle!);
                // A scene script belongs to one chronicle and casts it in its constructor, so the
                // wrong one throws ClassCastException — that means "try the next", not "broken".
                if (!s.Runtime.TryInstantiate(name, out var script, out var why))
                {
                    last = why;
                    if (why == "java.lang.ClassCastException") continue;
                    break;
                }
                try { s.Runtime.Call(script!, "beginscene", 1, 0); last = null; break; }
                catch (Exception e)
                {
                    last = ScriptException.From(name, e).JavaType;
                    if (last != "java.lang.ClassCastException") break;
                }
            }
            if (last is null) ran++; else failed.Add($"{name}: {last}");
        }
        Assert.True(scenes >= 44, $"expected the game's scene scripts, found {scenes}");
        Assert.Empty(failed);
        Assert.Equal(scenes, ran);
    }

    [Fact]
    public void A_script_call_reaches_a_handler_the_engine_registers()
    {
        s.Ready();
        // The Tower of London's storm: the script that made us write a C# stand-in. Now it runs for
        // real, and its first act is to switch three light styles off.
        var disabled = new List<int>();
        var frames = new List<(string Material, int Frame)>();
        // SetTimer(float seconds, int id, float param) is a Java wrapper; what reaches the bridge is
        // the native it calls, SetTimer(guid, milliseconds, id, p1, p2, p3, p4).
        var timers = new List<(int Ms, int Id)>();
        s.Engine.On("Codex.DisableLightStyle", (_, a) => disabled.Add(CodexHost.Int(a, 0)));
        s.Engine.On("Codex.MaterialSetFrame", (_, a) => frames.Add((CodexHost.Str(a, 0)!, CodexHost.Int(a, 1))));
        s.Engine.On("Codex.SetTimer", (_, a) => { timers.Add((CodexHost.Int(a, 1), CodexHost.Int(a, 2))); return CodexHost.Box(true); });

        var lightning = s.Runtime!.Instantiate("Lightning");
        Assert.NotNull(lightning);
        Assert.True(s.Runtime.Call(lightning!, "beginscene", 1, 0));

        Assert.Equal([7, 8, 9], disabled);
        Assert.Equal(6, frames.Count);
        Assert.All(frames, f => Assert.Equal(0, f.Frame));                     // all six start black
        Assert.Equal(["lightning1", "lightning2", "lightning3",
                      "lightningflare1", "lightningflare2", "lightningflare3"], frames.Select(f => f.Material));
        Assert.Equal(3, timers.Count);
        Assert.Equal([7, 8, 9], timers.Select(t => t.Id));
        Assert.All(timers, t => Assert.InRange(t.Ms, 1000, 4000));             // 1 + random×3 seconds
    }

    [Fact]
    public void An_unimplemented_native_logs_once_and_returns_a_default()
    {
        s.Ready();
        var host = new CodexHost();
        // A native nothing implements yet is not a problem — there are hundreds of them and they
        // are expected — so it goes to LogMissing (the engine log's Debug) rather than to Log,
        // which is where a script that actually failed is reported.
        var logged = new List<string>();
        var problems = new List<string>();
        host.LogMissing = logged.Add;
        host.Log = problems.Add;
        object Call(string key) => host.get(new object[] { key, null!, new object[] { java.lang.Integer.valueOf(1) } });

        Assert.Null(Call("CodexActor.NothingImplementsThis"));
        Assert.Null(Call("CodexActor.NothingImplementsThis"));
        Assert.Null(Call("CodexActor.NothingImplementsThis"));
        Assert.Single(logged);                                                 // once, not three times
        Assert.Contains("CodexActor.NothingImplementsThis", logged[0]);
        Assert.Equal(3, host.Missing["CodexActor.NothingImplementsThis"]);     // but all three counted
        Assert.Equal(3, host.Calls);
        Assert.Empty(problems);

        host.Strict = true;
        Assert.Throws<NotImplementedException>(() => Call("CodexActor.NothingImplementsThis"));
    }

    /// <summary>
    /// Recompiling one scene is what a level load does when that scene's source has changed since
    /// it was compiled, so it has to be cheap — tens of milliseconds of real work, not seconds.
    ///
    /// Timed as a benchmark rather than as a stopwatch race, and deliberately so. A single sample
    /// measures the machine as much as the compiler: this assertion failed at **3,182 ms against a
    /// 2,000 ms cap** while three other test assemblies were compiling and loading
    /// all 112 levels beside it, on a run where nothing in the compiler had changed. The best of
    /// several runs is the one number contention can only push the wrong way, so that is what is
    /// asserted — a compiler that really had got slow is slow in every run and the minimum still
    /// fails.
    /// </summary>
    [Fact]
    public void Recompiling_one_scene_costs_a_fraction_of_a_level_load()
    {
        s.Ready();
        // The scene's source, written as a mod's would be (the SDK's own is not compiled over the shipped class), and each
        // compile written into a folder of its own, so the measure leaves nothing in the build the other tests load from.
        var sdk = Path.Combine(GameFiles.Locate()!, "Codex", "Lightning.java");
        Assert.SkipWhen(!File.Exists(sdk), "the install has no Codex sources to measure a recompile with");
        string dir = Path.Combine(Path.GetTempPath(), "vitomir-recompile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var source = Path.Combine(dir, "Lightning.java");
            File.WriteAllText(source, File.ReadAllText(sdk, System.Text.Encoding.Latin1) + "\n// a mod's change\n", System.Text.Encoding.Latin1);
            s.Compiler!.Recompile(source, dir);                                    // warm the compiler up
            var runs = new long[5];
            for (int i = 0; i < runs.Length; i++) runs[i] = s.Compiler.Recompile(source, dir);
            Assert.True(runs.Min() <= 2000,
                        $"best of {runs.Length} recompiles was {runs.Min()} ms ({string.Join(", ", runs)})");
        }
        finally { Directory.Delete(dir, true); }
    }
}

/// <summary>Every scripted thing in the game, created the way the original creates one.</summary>
[Collection("scripts")]
public class ThingScriptTests(Scripts s)
{
    [Fact]
    public void A_things_script_gets_its_parameters_and_its_guid_before_its_constructor_runs()
    {
        s.Ready();
        // Door(float speed) reads GetClassThing() in its constructor, which needs the script's own
        // GUID: the original allocates, writes the field, then calls <init>, and so must we.
        var types = s.Runtime!.ConstructorTypes("Door");
        Assert.NotNull(types);
        Assert.Equal(["float"], types!);

        int askedFor = 0;
        s.Engine.On("Codex.GetClassThing", (_, a) => { askedFor = CodexHost.Int(a, 0); return CodexHost.Box(42); });
        var door = s.Runtime.NewScript("Door", 4321, [java.lang.Float.valueOf(150f)]);
        Assert.NotNull(door);
        Assert.Equal(4321, askedFor);                  // the constructor saw its own GUID, not 0
    }

    [Fact]
    public void Every_scripted_thing_in_every_level_is_created()
    {
        s.Ready();
        using var files = new GameFiles(GameFiles.Locate()!);
        var levels = files.List("levels/")
                          .Where(n => n.EndsWith(".nil", StringComparison.OrdinalIgnoreCase))
                          .Select(Path.GetFileNameWithoutExtension).OfType<string>().Order().ToArray();
        Assert.True(levels.Length > 100, $"expected the game's levels, found {levels.Length}");

        var scenes = new CodexScenes(s.Runtime!);
        int created = 0, named = 0;
        var missed = new List<string>();
        var absent = new List<string>();
        foreach (var name in levels)
        {
            var location = Location.Load(files, name);
            // A thing's script is its template's unless the placement names one (the two are
            // merged and the result attached), so the count is over the built things, not the scene.
            // One template names a class the game does not ship — `barrel1_scr`'s `Test.class`, in
            // two levels — and the engine's lookup fails and leaves the barrel without a script
            // (its script is null), which is what happens here too.
            var scripted = location.Things.Where(t => t.Script.Length > 0).ToList();
            foreach (var t in scripted.Where(t => s.Runtime!.ConstructorTypes(t.Script) is null))
                absent.Add($"{name}: {t.Template} names {t.Script}");
            int want = scripted.Count(t => s.Runtime!.ConstructorTypes(t.Script) is not null);
            scenes.Enter(location);
            int got = scenes.Running.Count();
            if (got != want) missed.Add($"{name}: {got} of {want}");
            created += got; named += want;
            scenes.Leave();
        }
        Assert.Empty(missed);
        Assert.Equal(["L1_SOC3: barrel1_scr names Test", "V1_EAST: barrel1_scr names Test"], absent);
        Assert.Equal(named, created);
        Assert.True(created > 400, $"expected the game's scripted things, created {created}");
    }
}

/// <summary>The CodexThing natives: a script reading and writing the state of its own thing.</summary>
[Collection("scripts")]
public class ThingStateTests(Scripts s)
{
    [Fact]
    public void A_script_changes_the_flags_of_the_thing_it_is_attached_to()
    {
        s.Ready();
        using var files = new GameFiles(GameFiles.Locate()!);
        var location = Location.Load(files, "l1_tol4");

        // ClickExit, the game's most-used script (216 of them), asks whether its exit is valid and
        // makes the thing unhighlightable when it isn't. The sequence system that would answer
        // isn't built yet, so the answer is the default — and the script acts on it, which is
        // exactly what we want to see reach the world.
        var exit = location.Things.First(t => t.Script == "ClickExit");
        Assert.False(exit.Has(WorldThing.Flag.NoHighlight));

        var scenes = new CodexScenes(s.Runtime!);
        scenes.Enter(location);
        Assert.True(exit.Has(WorldThing.Flag.NoHighlight));
        Assert.NotEqual("", exit.DescriptionId);          // its template's, put back unchanged
        scenes.Leave();
    }

    [Fact]
    public void Using_a_thing_reaches_its_script_s_clicked()
    {
        s.Ready();
        using var files = new GameFiles(GameFiles.Locate()!);
        var location = Location.Load(files, "l1_tol4", player: "Christof");
        var scenes = new CodexScenes(s.Runtime!);
        scenes.Enter(location);

        // **117 of the 465 shipped classes implement `clicked`** — a quarter of the game's
        // scripts, and once not one of them could hear it. ClickExit is the most used
        // of all of them, and clicking one is how a level is left.
        var exit = location.Things.First(t => t.Script == "ClickExit");
        Assert.NotNull(location.Clicked);            // Enter wired it, which is the plumbing test

        var me = location.Player!.Thing;
        me.Position = exit.Position;                 // standing on it, so nothing has to walk
        Assert.Equal(Vitomir.Engine.Rpg.OrderResult.Started,
                     Vitomir.Engine.Rpg.Actions.Use(me, exit, nowMs: 0));
        // The script has a `clicked` and it ran: true is "the method was there and was called",
        // which is what 1.1's sender gets back too. A script error would have been logged instead.
        Assert.True(location.Clicked!(exit, me));
        Assert.DoesNotContain(s.Warnings, w => w.Contains("clicked failed"));

        // And a thing with no script at all is simply not heard by anybody.
        var plain = location.Things.First(t => t.Script.Length == 0);
        Assert.False(location.Clicked(plain, me));
        scenes.Leave();
    }

    [Fact]
    public void Capturing_a_thing_is_remembered_and_only_its_captor_can_release_it()
    {
        s.Ready();
        using var files = new GameFiles(GameFiles.Locate()!);
        var location = Location.Load(files, "l1_tol4");
        var scenes = new CodexScenes(s.Runtime!);
        scenes.Enter(location);

        var thing = location.Things[0];
        object Call(string key, params int[] args) =>
            s.Engine.get(new object[] { key, null!, args.Select(a => (object)java.lang.Integer.valueOf(a)).ToArray() });

        Assert.Empty(thing.Captures);
        Assert.True(((java.lang.Boolean)Call("Codex.CaptureThing", 777, thing.Guid, 5)).booleanValue());
        Assert.Equal(new WorldThing.Capture(777, 5), Assert.Single(thing.Captures));
        Assert.False(((java.lang.Boolean)Call("Codex.ReleaseThing", 778, thing.Guid)).booleanValue());
        Assert.Single(thing.Captures);

        // **More than one script may hold the same thing**, because the engine keeps a list at
        // the thing's capture list and walks all of it. A second capture does not evict the first.
        Assert.True(((java.lang.Boolean)Call("Codex.CaptureThing", 778, thing.Guid, 9)).booleanValue());
        Assert.Equal(2, thing.Captures.Count);
        // The newest hold goes on the front, so it hears the thing's events first...
        Assert.Equal([new WorldThing.Capture(778, 9), new WorldThing.Capture(777, 5)], thing.Captures);
        // ...and a script capturing what it already holds only changes the id.
        Assert.True(((java.lang.Boolean)Call("Codex.CaptureThing", 777, thing.Guid, 6)).booleanValue());
        Assert.Equal([new WorldThing.Capture(778, 9), new WorldThing.Capture(777, 6)], thing.Captures);
        Assert.True(((java.lang.Boolean)Call("Codex.CaptureThing", 777, thing.Guid, 5)).booleanValue());
        Assert.True(((java.lang.Boolean)Call("Codex.ReleaseThing", 778, thing.Guid)).booleanValue());
        Assert.Equal(new WorldThing.Capture(777, 5), Assert.Single(thing.Captures));

        Assert.True(((java.lang.Boolean)Call("Codex.ReleaseThing", 777, thing.Guid)).booleanValue());
        Assert.Empty(thing.Captures);
        scenes.Leave();
    }
}

/// <summary>Things that move: the script, the thing model and the callback that keeps them going.</summary>
[Collection("scripts")]
public class MovingThingTests(Scripts s)
{
    [Fact]
    public void A_rotating_prop_turns_by_its_keyframe_and_keeps_going()
    {
        s.Ready();
        using var files = new GameFiles(GameFiles.Locate()!);
        var location = Location.Load(files, "V1_STE1");
        var scenes = new CodexScenes(s.Runtime!);
        scenes.Enter(location);

        // RotateAlways calls RotatePivot in its beginscene, so things are already turning.
        var stone = location.Things.First(t => t.Script == "RotateAlways");
        Assert.Equal(2, stone.Keyframes.Length);
        Assert.NotNull(stone.Motion);
        Assert.True(scenes.Moving >= 17);
        var start = stone.Orientation;
        var turn = stone.Keyframes[1].Orientation;                  // a delta, not a destination
        Assert.NotEqual(Vector3.Zero, turn);

        // Half way through the three-second turn it is half way round.
        for (int i = 0; i < 90; i++) scenes.Step(1f / 60f);
        Assert.True(Vector3.Distance(stone.Orientation, start) > 1f, "it should have turned by now");
        Assert.True(Vector3.Distance(stone.Orientation, start + turn) > 1f, "and not be there yet");
        Assert.True(stone.Moved);

        // And by the end of it, exactly where the keyframe said.
        for (int i = 0; i < 120; i++) scenes.Step(1f / 60f);
        Assert.True(Vector3.Distance(stone.Orientation, start + turn) < 0.5f,
                    $"expected {start + turn}, got {stone.Orientation}");

        // arrived fires, the script sets a timer, and the next turn begins: it never stops.
        for (int i = 0; i < 120; i++) scenes.Step(1f / 60f);
        Assert.True(Vector3.Distance(stone.Orientation, start + turn * 2f) < 40f,
                    "the second turn should have started");
        scenes.Leave();
    }

    [Fact]
    public void A_thing_that_has_not_moved_is_drawn_exactly_where_the_scene_built_it()
    {
        s.Ready();
        using var files = new GameFiles(GameFiles.Locate()!);
        var location = Location.Load(files, "V1_STE1");
        var still = location.Things.First(t => t.Script.Length == 0);
        Assert.False(still.Moved);
        Assert.Equal(still.Base, still.World);
    }
}

/// <summary>
/// The Tower of London's storm, end to end: the game's own `Lightning` script driving the engine's
/// light styles and material frames, with nothing of ours in between.
/// </summary>
[Collection("scripts")]
public class StormTests(Scripts s)
{
    [Fact]
    public void The_shipped_script_strikes_about_once_a_second_and_is_dark_the_rest_of_the_time()
    {
        s.Ready();
        using var files = GameFiles.Locate() is { } dir ? new GameFiles(dir) : null;
        Assert.SkipWhen(files is null, "no install");
        var location = Location.Load(files!, "l1_tol4");
        Assert.Contains("Lightning", location.ScriptNames);

        var scenes = new CodexScenes(s.Runtime!);
        scenes.Enter(location);
        Assert.Contains("Lightning", scenes.Running);
        // Lightning extends Codex directly and never casts a chronicle, so any of them fits it;
        // which one gets chosen is only interesting for scripts that do cast.
        Assert.NotNull(scenes.ChronicleName);

        var bolts = new[] { "lightning1", "lightning2", "lightning3" }
            .Select(n => location.Materials.Find(n)).ToArray();
        Assert.All(bolts, b => Assert.NotNull(b));
        Assert.All(bolts, b => Assert.Equal(0, b!.CurrentFrame));      // beginscene blacks them out
        Assert.All([7, 8, 9], n => Assert.False(location.Styles.IsEnabled(n)));

        // A minute at sixty frames, the same clock the renderer runs on. Deliberately under 100 s:
        // the lightning materials also carry `pageflip 100000`, and the original never resets that
        // clock when a script sets a frame, so every 100 s the page flip nudges a bolt out of step
        // with its light style for a second or two. Faithful, and not what this test is about.
        int steps = 60 * 60, anyLit = 0, strikes = 0;
        var wasLit = new bool[bolts.Length];
        for (int i = 0; i < steps; i++)
        {
            scenes.Step(1f / 60f);
            location.Styles.Step(1000f / 60f);
            location.Materials.Animate(1000f / 60f, location.Styles);
            bool any = false;
            for (int b = 0; b < bolts.Length; b++)
            {
                bool on = bolts[b]!.CurrentFrame == 1;
                if (on) { any = true; if (!wasLit[b]) strikes++; }
                // A bolt is lit exactly while its own light style is switched on.
                Assert.Equal(on, location.Styles.IsEnabled(7 + b));
                wasLit[b] = on;
            }
            if (any) anyLit++;
        }
        // Three channels, each dark 1–4 s then striking for 0.2 s: about one strike a second, with
        // something lit roughly a quarter of the time. The script's own rhythm, not ours.
        Assert.InRange(strikes, 45, 90);
        Assert.InRange(anyLit / (double)steps, 0.12, 0.35);

        scenes.Leave();
        Assert.Empty(scenes.Running);
    }
}
