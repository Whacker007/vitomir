// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Text.Json;
using Vitomir.Modules.Enhanced.Sheets;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Space;

namespace Vitomir.Modules.Enhanced.Tests;

/// <summary>
/// The enhanced module as a module folder, loaded as the program loads it:
/// the platform's loader finds <c>Modules/Enhanced</c> beside the tests (laid out by the build), checks its
/// manifest, makes its code and configures it reading its data by id. It gives the same configuration hash as
/// before it moved (the banner's <c>config 43051f7e</c>), the same refusal without the sandbox switch (28
/// reasons), the program's game names it, and a 30 s drive through it replays agreeing (with the test level).
/// </summary>
public sealed class ModuleFolderTests
{
    /// <summary>The configuration hash of the sandbox before the module moved into its folder (config 43051f7e3f9b then; 8c90cf9aee87 since the sandbox profile's id names no release of the rules package).</summary>
    const string SandboxHashBefore = "8c90cf9aee87";

    static string Modules => Path.Combine(AppContext.BaseDirectory, ModuleLoader.ModulesFolderName);

    static GameList Enhanced => new("enhanced", "the enhanced game", ["enhanced"]);

    static LoadedGame Load(params string[] switches) => ModuleLoader.Load(new ModuleLoadOptions
    {
        ModuleFolders = [Modules],
        Game = Enhanced,
        Switches = new HashSet<string>(switches, StringComparer.Ordinal),
    });

    [Fact]
    public void Loaded_from_its_folder_it_gives_the_same_configuration_hash_as_before()
    {
        var game = Load("sandbox", "headless");
        var loaded = Assert.Single(game.Modules);
        Assert.Equal("enhanced", loaded.Manifest.Id);
        Assert.Equal(["sandbox"], loaded.Switches);
        var module = Assert.IsType<EnhancedModule>(loaded.Code);
        Assert.StartsWith(SandboxHashBefore, module.Stamp.ConfigHash);
        Assert.Equal(TestData.Sandbox().Stamp, module.Stamp);
        Assert.Equal(module.Config.Banner, game.Banner);
        Assert.Contains($"config {SandboxHashBefore[..8]}", game.Banner);
        // Its data read by id, each file the package's own.
        Assert.Contains(module.Config.Files, f => f.File == "enhanced:profiles/prototype_sandbox" && f.MatchesPackage);
        Assert.Equal(5, module.Config.Files.Count);
    }

    [Fact]
    public void Loaded_from_its_folder_without_the_sandbox_switch_it_refuses_with_the_same_28_reasons()
    {
        var refused = Assert.Throws<ModuleRefusedException>(() => Load());
        Assert.Equal(EnhancedModule.ModuleId, refused.ModuleId);
        Assert.Equal(28, refused.Reasons.Count);
        var direct = Assert.Throws<ModuleRefusedException>(() => new EnhancedModule().Configure(TestData.Context()));
        Assert.Equal(direct.Reasons, refused.Reasons);
        Assert.Equal("Start with --sandbox to run the research sandbox profile (the shipping profile refuses: fail closed).", refused.Hint);
    }

    [Fact]
    public void Its_manifest_agrees_with_its_code_the_checker_finds_nothing_and_the_programs_game_names_it()
    {
        var check = ModuleChecker.Check([Modules], Enhanced);
        Assert.True(check.AllWell, string.Join("\n", check.Problems.Concat(check.Elsewhere)));
        var m = Assert.Single(check.Order);
        Assert.Equal(new EnhancedModule().Info.Version, m.Version.ToString());
        Assert.Equal(typeof(EnhancedModule).FullName, m.Type);
        Assert.Equal($"bin/{typeof(EnhancedModule).Assembly.GetName().Name}.dll", m.Assembly);
        Assert.Equal(ModuleKind.Rules, m.Kind);
        Assert.Null(PlatformApi.Refuses(m.Platform));
        Assert.Equal(["streamer", "sandbox", "skill-tempo-off", "skill-contest", "celerity-curve-oneone", "celerity-curve-tempo", "celerity-attack-off", "severance-off", "contact-stop-off", "rebound-fixed", "rebound-small", "rebound-stun-after", "rebound-large", "dodge-dexterity-off", "severance-angle-off", "severance-melee-off", "armor-off", "guard-off", "guard-sight-off", "guard-sight-body", "flurry-compressed", "flurry-off", "flurry-swing-spacing", "guard-technique-off", "guard-melee-off", "contest-dexterity-performed", "flurry-motion", "flurry-spacing-80ms", "flurry-spacing-120ms", "flurry-spacing-160ms", "blow-quality-off", "blow-quality-hilt-off", "brain-range-off", "guard-solver-off", "attack-spin", "guard-skill-off", "brain-parry-fixed", "stagger-oneone", "rebound-two-thirds", "rebound-stun-at-meeting", "swings-oneone", "stagger-hurt-clip", "stagger-push-only", "stagger-hurt-short", "pace-off", "blade-guard-everyone", "combat-tempo-off", "fighting-pace-off", "rush-unpaced", "dodge-paced", "shield-read-off", "window-from-windup", "blows-on-moving-hips", "guards-one-blow", "aim-at-press", "blow-hips-upright", "foes-uniform", "shield-at-bow-off", "bow-look-off", "bow-aim-off", "bow-solve-on", "brain-straight", "accel-ramp", "arrows-heavy", "landing-slide"], m.Switches.Select(s => s.Name));   // streamer mode (the platform's, every game's); the tempo turned off, the contest turned on; Celerity's curve and attack; the pure tempo; severance off; its angle and Melee's edge off; armour off; the guard off; the guard's sight off or by the body; the flurry compressed or off, its re-strike motion and spacings; the guard's technique or Melee off, the contest's Dexterity only with a performed defence; blow quality off, its hilt off, the brain's range keeping off; the fixed rebound, the film's sizes, the stun after the swing; the guard's arm solver off; 1.1's tap-and-hold back; the guard by skill off, the brain's fixed parry, 1.1's flinch, the rebound at two thirds, the stun from the meeting; the uniform foes; the ramp and the heavy arrows; the landings' slide
        // Thirty-nine ids (Blood's numbers, the powers and the player's sheet): contact's matter and protected people, the Abilities' channels, the bodies' three movement
        // profiles, the registry, the gates and their mapping, perception's world numbers and ten kinds of observer,
        // the powers, two profiles, the staged harm's numbers and three fighters' sheets with the combat rules' eight fixtures, the sources; armour's and the guard's; directional attacks' and the stitched blows'; the pace and the wind-ups.
        Assert.Equal(["enhanced:combat/armor", "enhanced:combat/directions", "enhanced:combat/directions-stitched", "enhanced:combat/foes", "enhanced:combat/guard", "enhanced:combat/pace", "enhanced:combat/protected", "enhanced:combat/ranged", "enhanced:combat/skill", "enhanced:combat/weapons", "enhanced:combat/windups", "enhanced:movement/elder", "enhanced:movement/human", "enhanced:movement/neonate",
                      "enhanced:numeric_gate_register",
                      "enhanced:perception/beggar", "enhanced:perception/drunk", "enhanced:perception/guard", "enhanced:perception/merchant",
                      "enhanced:perception/monk", "enhanced:perception/nightcitizen", "enhanced:perception/oldman", "enhanced:perception/sight",
                      "enhanced:perception/townsman", "enhanced:perception/watchman", "enhanced:perception/younglady",
                      "enhanced:power_verb_contracts", "enhanced:profile_value_gates",
                      "enhanced:profiles/enhanced_unapproved_template", "enhanced:profiles/prototype_sandbox",
                      "enhanced:rules/blood", "enhanced:rules/flurry", "enhanced:rules/harm", "enhanced:rules/powers", "enhanced:rules/quality", "enhanced:rules/severance", "enhanced:sheets/archer", "enhanced:sheets/christof", "enhanced:sheets/developed_ninth", "enhanced:sheets/elite_stress",
                      "enhanced:sheets/guard", "enhanced:sheets/guard_trained", "enhanced:sheets/human_reference", "enhanced:sheets/knight",
                      "enhanced:sheets/newly_embraced", "enhanced:sheets/player", "enhanced:sheets/raw1000_stress", "enhanced:sheets/szlachta", "enhanced:sheets/townsperson_reference",
                      "enhanced:sheets/watchman", "enhanced:sources",
                      "enhanced:vampire_trait_registry"], check.Assets!.List("enhanced", "").Select(i => i.ToString()));

        // The program's game (src/Vitomir.Platform.Host/Games/enhanced.json) is this module.
        var src = new DirectoryInfo(AppContext.BaseDirectory);
        while (src is not null && !File.Exists(Path.Combine(src.FullName, "Vitomir.slnx"))) src = src.Parent;
        Assert.NotNull(src);
        var game = ModuleLoader.FindGame("enhanced", [Path.Combine(src.FullName, "Vitomir.Platform.Host", ModuleLoader.GamesFolderName)]);
        Assert.Equal(["enhanced"], game.Modules);
        Assert.Equal(ModuleLoader.DefaultGame, game.Id);
        // The source manifest is the one laid out.
        Assert.Equal(File.ReadAllText(Path.Combine(src.FullName, "Vitomir.Modules.Enhanced", ModuleManifest.FileName)),
                     File.ReadAllText(Path.Combine(Modules, "Enhanced", ModuleManifest.FileName)));
    }

    [Fact]
    public void It_dresses_the_people_the_program_makes_through_the_platforms_hook()
    {
        var game = Load("sandbox");
        var b = new WorldBuilder(3);
        game.Register(b);
        var w = b.Build();
        var sheets = w.Service<ActorSheets>();
        var (player, townsperson, guard, watchman) = (w.Create(), w.Create(), w.Create(), w.Create());
        w.Service<PeopleHooks>().Made(player, PersonRole.Player);
        w.Service<PeopleHooks>().Made(townsperson, PersonRole.Townsperson);
        w.Service<PeopleHooks>().Made(guard, PersonRole.Townsperson, "rudolfGuard");             // by the character drawn
        w.Service<PeopleHooks>().Made(watchman, PersonRole.Townsperson, "KnightOfStJohn_torch");
        Assert.Equal("vampire", sheets.Species(player));
        Assert.Equal("dark-ages", sheets.Era(player));
        // The player a newly embraced vampire with Celerity 1 and Potence 1 and 1.1's pool of Blood; the
        // guards and the watch the trained guard; the townsfolk the combat rules' townsperson. The stress fixtures dress nobody.
        Assert.Equal("sandbox.player", sheets.Template(player));
        Assert.Equal(15, sheets.Purchased(player, "strength"));
        Assert.Equal((1, 1), (sheets.Rank(player, "celerity"), sheets.Rank(player, "potence")));
        Assert.Equal(["celerity.active"], sheets.Powers(player).First(p => p.PowerId == "celerity").Options);
        Assert.Equal(100, sheets.Bought(player, "blood_pool"));
        Assert.Equal(new ResourceState("blood", 100, 100, 0, 0), sheets.Resource(player, "blood"));
        Assert.Equal("human", sheets.Species(townsperson));
        Assert.Equal("sandbox.townsperson_reference", sheets.Template(townsperson));
        Assert.Equal(14, sheets.Purchased(townsperson, "strength"));
        Assert.Equal("sandbox.guard_trained", sheets.Template(guard));
        Assert.Equal("sandbox.guard_trained", sheets.Template(watchman));
        foreach (var e in new[] { townsperson, guard, watchman }) Assert.Null(sheets.Resource(e, "blood"));   // mortals: no Blood
        foreach (var e in new[] { player, townsperson, guard, watchman })
            Assert.Equal(new ResourceState("health", 100, 100, 0, 0), sheets.Resource(e, "health"));    // the engine-sandbox Health
        Assert.Equal(4, w.Store<ActorIdentity>().Count);
    }

    /// <summary>
    /// The scripted drive of <c>vitomir-platform --record-drive</c> (Program.ScriptedDrive): running,
    /// turning, jumping, sprinting, walking, strafing, switching view, a pattern of twelve seconds.
    /// </summary>
    static ActionFrame Drive(int t)
    {
        float s = (t % (12 * World.TicksPerSecond)) / (float)World.TicksPerSecond;
        var move = new Vector2(s is >= 9f and < 10f ? 1f : 0f, s < 9f ? 1f : 0f);
        float turn = s < 1f ? 0.012f : s < 2f ? -0.012f : s is >= 4f and < 4.5f ? 0.02f : 0f;
        float pitch = s is >= 10f and < 10.5f ? 0.004f : s is >= 10.5f and < 11f ? -0.004f : 0f;
        var held = Buttons.None;
        if (t % (2 * World.TicksPerSecond) < 3) held |= Buttons.Jump;
        if (s is >= 3f and < 5f) held |= Buttons.Sprint;
        if (s is >= 6f and < 7f) held |= Buttons.Walk;
        if (s is >= 8f and < 8.02f) held |= Buttons.SwitchView;
        return new ActionFrame(move, new Vector2(turn, pitch), held, new Vector2(0.5f, 0.5f));
    }

    /// <summary>A session as the program builds one (without 1.1's characters): the level, the loaded module, the player and six townsfolk, driven; the digest every <see cref="Tape.DigestEvery"/> ticks.</summary>
    static (Tape Tape, int People) Record(LevelSpace level, int ticks, Func<int, ActionFrame> frames)
    {
        var game = Load("sandbox");
        var b = new WorldBuilder(1);
        MovementServices.Register(b, level);
        game.Register(b);
        var w = b.Build();
        var people = w.Service<PeopleHooks>();
        var start = level.Starts.First();
        people.Made(MovementServices.SpawnControlled(w, start.Position, start.Yaw, Locomotion.Sandbox), PersonRole.Player);
        var rng = w.Random.For("host.spawn");
        for (int i = 0, made = 0; made < 6 && i < 24; i++)
        {
            if (level.Navigation is null || !level.Navigation.RandomReachable(start.Position, 30f, rng, out var at)) continue;
            people.Made(MovementServices.SpawnWanderer(w, at, rng.NextFloat(-DetMath.Pi, DetMath.Pi), Locomotion.Sandbox), PersonRole.Townsperson);
            made++;
        }
        var tape = new Tape { Header = new TapeHeader { Game = game.Game.Id, Modules = game.Recorded, Seed = 1 } };
        for (int t = 0; t < ticks; t++)
        {
            var frame = frames(t);
            MovementServices.Step(w, frame);
            tape.Frames.Add(frame);
            if (w.Tick % Tape.DigestEvery == 0) tape.Digests[w.Tick] = w.Digest();
        }
        return (tape, w.Store<ActorIdentity>().Count);
    }

    [Fact]
    public void A_thirty_second_drive_through_the_loaded_module_replays_agreeing()
    {
        var path = Environment.GetEnvironmentVariable("VITOMIR_TEST_LEVEL");
        Assert.SkipUnless(path is not null && File.Exists(Path.Combine(path, "level.json")), "no test level (VITOMIR_TEST_LEVEL)");
        var level = LevelSpace.Open(path!);
        var (recorded, people) = Record(level, 30 * World.TicksPerSecond, Drive);
        Assert.True(people >= 2, $"{people} people were given sheets");
        Assert.Equal(120, recorded.Digests.Count);

        // Written and read back as a tape, then replayed through a fresh load of the module: the same digests.
        var file = Path.Combine(Directory.CreateTempSubdirectory("vitomir-tape-").FullName, "drive.tape");
        recorded.Write(file);
        var read = Tape.Read(file);
        var again = Load("sandbox");
        Assert.Empty(again.Differences(read.Header.Modules, out bool same));
        Assert.True(same);
        var (replayed, _) = Record(level, read.Frames.Count, t => read.Frames[t]);
        Assert.Equal(read.Digests, replayed.Digests);
        Directory.Delete(Path.GetDirectoryName(file)!, recursive: true);
    }
}
