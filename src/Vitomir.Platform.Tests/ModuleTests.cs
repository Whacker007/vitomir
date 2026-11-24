// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Assets;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Tests;

/// <summary>
/// Modules as folders:
/// the manifest read strictly, namespaced asset ids, versions and the platform API, the game's load order and
/// its cycles, ids claimed twice, overrides declared or refused, the registry resolving module files and 1.1's,
/// what each module may read, the loader making a module's code and configuring it by id, and a recording's
/// module set compared plainly. Modules are written to scratch folders; one small content module of the
/// tests' own is checked in (TestModules/testpeople).
/// </summary>
public sealed class ModuleTests
{
    // ---- module folders in a scratch folder ----

    sealed class Scratch : IDisposable
    {
        public Scratch() { Root = Directory.CreateTempSubdirectory("vitomir-modules-").FullName; }

        /// <summary>The folder of module folders.</summary>
        public string Root { get; }

        public string Add(string folder, string manifest, params (string Path, string Text)[] files)
        {
            string dir = Path.Combine(Root, folder);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, ModuleManifest.FileName), manifest);
            foreach (var (path, text) in files)
            {
                string full = Path.Combine(dir, path);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, text);
            }
            return dir;
        }

        /// <summary>A module with code: this test assembly in its bin/ and <paramref name="type"/> as its module.</summary>
        public string AddCode(string folder, string id, string version, Type type, string extra = "", params (string Path, string Text)[] files)
        {
            var dir = Add(folder, Manifest(id, version, "rules", extra: $"\"assembly\": \"bin/{Path.GetFileName(typeof(ModuleTests).Assembly.Location)}\", \"type\": \"{type.FullName}\"" + (extra.Length > 0 ? ", " + extra : "")), files);
            Directory.CreateDirectory(Path.Combine(dir, "bin"));
            File.Copy(typeof(ModuleTests).Assembly.Location, Path.Combine(dir, "bin", Path.GetFileName(typeof(ModuleTests).Assembly.Location)));
            return dir;
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch (IOException) { }
        }
    }

    static string Manifest(string id, string version = "1.0.0", string kind = "content", string deps = "", string platform = "1.0.0", string extra = "") =>
        $$"""{ "id": "{{id}}", "name": "module {{id}}", "version": "{{version}}", "platform": "{{platform}}", "kind": "{{kind}}", "dependencies": { {{deps}} }{{(extra.Length > 0 ? ", " + extra : "")}} }""";

    static GameList Game(params string[] modules) => new("test", "the test game", modules);

    static List<string> Problems(string manifest)
    {
        var problems = new List<string>();
        ModuleManifest.Parse(manifest, "module.json", problems);
        return problems;
    }

    // ---- manifests ----

    [Fact]
    public void A_good_manifest_is_read_with_every_field()
    {
        var problems = new List<string>();
        var m = ModuleManifest.Parse("""
            { "id": "tactical", "name": "Tactics", "description": "the NWN-like rules", "version": "2.1.3", "platform": "1.0.0", "kind": "rules",
              "dependencies": { "people": "1.2.0", "arms": "3.0.0" }, "assembly": "bin/Tactical.dll", "type": "Tactical.TacticalModule",
              "overrides": [ "people:motions/attack_sword" ], "switches": { "sandbox": "run its research numbers" } }
            """, "module.json", problems)!;
        Assert.Empty(problems);
        Assert.Equal("tactical", m.Id);
        Assert.Equal(new ModuleVersion(2, 1, 3), m.Version);
        Assert.Equal(ModuleKind.Rules, m.Kind);
        Assert.Equal([new ModuleDependency("people", new(1, 2, 0)), new ModuleDependency("arms", new(3, 0, 0))], m.Dependencies);
        Assert.Equal("bin/Tactical.dll", m.Assembly);
        Assert.Equal("Tactical.TacticalModule", m.Type);
        Assert.Equal([AssetId.Parse("people:motions/attack_sword")], m.Overrides);
        Assert.Equal([new ModuleSwitch("sandbox", "run its research numbers")], m.Switches);
        // A module of data only has neither assembly nor type.
        Assert.False(ModuleManifest.Parse(Manifest("people"), "module.json", problems)!.HasCode);
        Assert.Empty(problems);
    }

    [Fact]
    public void A_manifest_missing_a_field_is_refused_naming_each()
    {
        var problems = Problems("""{ "dependencies": {} }""");
        foreach (var field in new[] { "id", "name", "version", "platform", "kind" })
            Assert.Contains($"module.json: '{field}' is missing", problems);
        Assert.Contains(Problems("""{ "id": "a", "name": "A", "version": "1.0", "platform": "1.0.0", "kind": "rules" }"""),
            p => p.Contains("'version' is '1.0', not a version"));
        Assert.Contains(Problems(Manifest("a", kind: "story")), p => p.Contains("'kind' is 'story'"));
        Assert.Contains(Problems(Manifest("a", extra: "\"assembly\": \"bin/A.dll\"")), p => p.Contains("'assembly' and 'type' come together"));
        Assert.Contains(Problems(Manifest("a", extra: "\"assembly\": \"../A.dll\", \"type\": \"A\"")), p => p.Contains("must be a path inside the module's folder"));
        Assert.Contains(Problems(Manifest("a", deps: "\"b\": \"latest\"")), p => p.Contains("'b' must give the least version"));
        Assert.Contains(Problems("not json"), p => p.StartsWith("module.json: not valid JSON", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unknown_field_is_named_once_and_a_field_given_twice_is_named()
    {
        var problems = Problems("""{ "id": "a", "name": "A", "version": "1.0.0", "platform": "1.0.0", "kind": "content", "dependancies": {}, "colour": 1, "name": "B" }""");
        Assert.Single(problems, p => p.Contains("unknown field 'dependancies'"));
        Assert.Single(problems, p => p.Contains("unknown field 'colour'"));
        Assert.Single(problems, p => p.Contains("'name' is given twice"));
        Assert.Equal(3, problems.Count);
    }

    [Fact]
    public void A_bad_module_id_is_refused_by_name()
    {
        Assert.Contains(Problems(Manifest("Enhanced")), p => p.Contains("must start with a lower-case letter"));
        Assert.Contains(Problems(Manifest("en hanced")), p => p.Contains("may hold only lower-case letters"));
        Assert.Contains(Problems(Manifest("9lives")), p => p.Contains("must start with a lower-case letter"));
        Assert.Contains(Problems(Manifest(AssetId.OneOne)), p => p.Contains("the 1.1 install's source"));
        Assert.Contains(Problems(Manifest("a", extra: "\"overrides\": [\"people/motions/run\"]")), p => p.Contains("a bare path is not one"));
        Assert.Contains(Problems(Manifest("a", extra: "\"overrides\": [\"People:motions/run\"]")), p => p.Contains("must start with a lower-case letter"));
    }

    // ---- ids and versions ----

    [Fact]
    public void An_asset_id_is_a_module_and_a_path_parsed_and_printed_never_a_bare_path()
    {
        var id = AssetId.Parse("enhanced:profiles/prototype_sandbox");
        Assert.Equal("enhanced", id.Module);
        Assert.Equal("profiles/prototype_sandbox", id.Path);
        Assert.Equal("enhanced:profiles/prototype_sandbox", id.ToString());
        Assert.Equal(id, AssetId.Of("enhanced", "profiles/prototype_sandbox"));
        Assert.True(id.IsIn("profiles"));
        Assert.False(id.IsIn("profile"));
        Assert.True(AssetId.Parse("oneone:3d/models/christof.nod").IsOneOne);
        // A file's id: its path under the asset folder, lower-cased, without its extension.
        Assert.Equal(AssetId.Parse("enhanced:numeric_gate_register"), AssetId.FromFile("enhanced", "NUMERIC_GATE_REGISTER.json"));
        Assert.Equal(AssetId.Parse("people:motions/run.v2"), AssetId.FromFile("people", "motions\\run.v2.glb"));
        foreach (var bad in new[] { "profiles/prototype_sandbox", "enhanced:", ":x", "enhanced:a//b", "enhanced:../x", "enhanced:a b", "enhanced:A", "enhanced:x.", "enhanced:/x" })
            Assert.False(AssetId.TryParse(bad, out _, out var problem), bad + " " + problem);
        Assert.Throws<FormatException>(() => AssetId.FromFile("people", "motions/Run Fast.glb"));
    }

    [Fact]
    public void A_dependency_accepts_its_least_version_and_later_ones_of_the_same_major_only()
    {
        var least = ModuleVersion.Parse("1.2.0");
        Assert.True(ModuleVersion.Parse("1.2.0").Satisfies(least));
        Assert.True(ModuleVersion.Parse("1.10.3").Satisfies(least));
        Assert.False(ModuleVersion.Parse("1.1.9").Satisfies(least));
        Assert.False(ModuleVersion.Parse("2.0.0").Satisfies(least));
        Assert.Equal("1.10.3", ModuleVersion.Parse("1.10.3").ToString());
        foreach (var bad in new[] { "1.2", "v1.2.3", "01.2.3", "1.2.3-beta", "1.-2.3", "" })
            Assert.False(ModuleVersion.TryParse(bad, out _), bad);
        // The platform API: the same major, not a newer minor.
        Assert.Null(PlatformApi.Refuses(PlatformApi.Version));
        Assert.Null(PlatformApi.Refuses(PlatformApi.Version with { Patch = PlatformApi.Version.Patch + 7 }));
        Assert.Contains("another major", PlatformApi.Refuses(PlatformApi.Version with { Major = PlatformApi.Version.Major + 1 }));
        Assert.Contains("a newer minor", PlatformApi.Refuses(PlatformApi.Version with { Minor = PlatformApi.Version.Minor + 1 }));
    }

    // ---- the checker ----

    [Fact]
    public void The_load_order_is_the_games_where_the_dependencies_allow_it()
    {
        using var s = new Scratch();
        s.Add("Rules", Manifest("rules", kind: "rules", deps: "\"city\": \"1.0.0\", \"people\": \"1.0.0\""));
        s.Add("People", Manifest("people"));
        s.Add("City", Manifest("city", deps: "\"people\": \"1.0.0\""));
        s.Add("Arms", Manifest("arms"));
        var check = ModuleChecker.Check([s.Root], Game("rules", "arms", "city", "people"));
        Assert.True(check.Ok, string.Join("\n", check.Problems));
        Assert.Equal(["arms", "people", "city", "rules"], check.Order.Select(m => m.Id));
        // With no dependency between them, the game's order stands.
        Assert.Equal(["people", "arms"], ModuleChecker.Check([s.Root], Game("people", "arms")).Order.Select(m => m.Id));
    }

    [Fact]
    public void A_dependency_cycle_is_refused_naming_it()
    {
        using var s = new Scratch();
        s.Add("A", Manifest("a", deps: "\"b\": \"1.0.0\""));
        s.Add("B", Manifest("b", deps: "\"c\": \"1.0.0\""));
        s.Add("C", Manifest("c", deps: "\"a\": \"1.0.0\""));
        var check = ModuleChecker.Check([s.Root], Game("a", "b", "c"));
        Assert.False(check.Ok);
        Assert.Single(check.Problems, p => p == "a dependency cycle: a → b → c → a");
        Assert.Null(check.Assets);
    }

    [Fact]
    public void Dependencies_absent_unlisted_or_of_a_refused_version_are_named()
    {
        using var s = new Scratch();
        s.Add("Rules", Manifest("rules", kind: "rules", deps: "\"people\": \"1.3.0\", \"arms\": \"2.0.0\", \"city\": \"1.0.0\""));
        s.Add("People", Manifest("people", version: "1.2.9"));
        s.Add("Arms", Manifest("arms", version: "3.0.0"));
        s.Add("City", Manifest("city", version: "1.0.0", platform: "2.0.0"));
        s.Add("Story", Manifest("story", deps: "\"rules\": \"1.0.0\""));
        var check = ModuleChecker.Check([s.Root], Game("people", "rules", "nowhere"));
        Assert.Contains("module rules needs module people at least 1.3.0 within major 1, and people is 1.2.9", check.Problems);
        Assert.Contains("module rules needs module arms at least 2.0.0 within major 2, and arms is 3.0.0", check.Problems);
        Assert.Contains(check.Problems, p => p.StartsWith("module city is refused: it was built for platform API 2.0.0", StringComparison.Ordinal));
        Assert.Contains("content module story depends on rules module rules; content never depends on rules", check.Problems);
        Assert.Contains(check.Problems, p => p.StartsWith("game test loads module rules, which needs module arms, and the game does not list it (it is in ", StringComparison.Ordinal));
        Assert.Contains("game test lists module nowhere, which is not there", check.Problems);
        Assert.Contains(check.Problems, p => p.StartsWith("(the modules were looked for in ", StringComparison.Ordinal));
    }

    [Fact]
    public void A_module_asking_for_platform_1_6_0_is_refused_and_one_asking_for_1_5_0_is_not()
    {
        // The platform API was 1.4.0 (the window's rules and settings, the developer switch); 1.5.0 (the pushed frame). A
        // module built for a newer minor may use what this platform does not have, so it is refused, naming both; one built for 1.5.0
        // or before loads.
        Assert.Equal(new ModuleVersion(1, 5, 0), PlatformApi.Version);
        using var s = new Scratch();
        s.Add("Now", Manifest("now", platform: "1.5.0"));
        s.Add("Older", Manifest("older", platform: "1.0.0"));
        s.Add("Next", Manifest("next", platform: "1.6.0"));
        var check = ModuleChecker.Check([s.Root], Game("now", "older", "next"));
        Assert.Contains("module next is refused: it was built for platform API 1.6.0, a newer minor than this platform's 1.5.0", check.Problems);
        Assert.DoesNotContain(check.Problems, p => p.Contains("module now ", StringComparison.Ordinal) || p.Contains("module older ", StringComparison.Ordinal));
        Assert.False(check.Ok);
        Assert.True(ModuleChecker.Check([s.Root], Game("now", "older")).Ok);
    }

    [Fact]
    public void An_id_claimed_twice_is_refused_naming_both()
    {
        using var s = new Scratch();
        var first = s.Add("People", Manifest("people"), ("Content/motions/run.glb", "a"), ("Data/motions/run.json", "{}"), ("Content/motions/Walk.glb", "w"));
        var second = s.Add("People2", Manifest("people"));
        var check = ModuleChecker.Check([s.Root], Game("people"));
        Assert.Contains($"the module id people is claimed twice, by {first} and {second}", check.Problems);
        Assert.Contains("module people: the id people:motions/run is claimed twice, by Data/motions/run.json and Content/motions/run.glb", check.Problems);
        // A name that makes an id (upper case is lowered) is not a problem; a folder with no manifest is, though not the game's.
        Assert.DoesNotContain(check.Problems, p => p.Contains("Walk"));
        Directory.CreateDirectory(Path.Combine(s.Root, "Empty"));
        Assert.Contains(ModuleChecker.Check([s.Root], Game("people")).Elsewhere, p => p.EndsWith("Empty: a folder among the modules with no module.json", StringComparison.Ordinal));
    }

    [Fact]
    public void An_undeclared_override_is_refused_and_a_declared_one_applied_and_logged()
    {
        using var s = new Scratch();
        s.Add("People", Manifest("people"), ("Content/motions/attack_sword.glb", "people's"), ("Content/motions/run.glb", "run"));
        s.Add("Tactical", Manifest("tactical", kind: "rules", deps: "\"people\": \"1.0.0\""), ("Overrides/people/motions/attack_sword.glb", "tactical's"));
        var refused = ModuleChecker.Check([s.Root], Game("people", "tactical"));
        Assert.Contains("module tactical: Overrides/people/motions/attack_sword.glb overrides people:motions/attack_sword without declaring it (add it to 'overrides' in its module.json)", refused.Problems);

        File.WriteAllText(Path.Combine(s.Root, "Tactical", ModuleManifest.FileName),
            Manifest("tactical", kind: "rules", deps: "\"people\": \"1.0.0\"", extra: "\"overrides\": [\"people:motions/attack_sword\"]"));
        var said = new List<string>();
        var game = ModuleLoader.Load(new ModuleLoadOptions { ModuleFolders = [s.Root], Game = Game("people", "tactical"), Say = said.Add });
        var id = AssetId.Parse("people:motions/attack_sword");
        Assert.Equal("tactical's", System.Text.Encoding.UTF8.GetString(game.Assets.Read(id)));
        Assert.Equal("tactical", game.Assets.Resolve(id).Module);
        Assert.Equal("run", game.Assets.For("tactical").ReadText(AssetId.Parse("people:motions/run")));
        var applied = Assert.Single(game.Assets.Overrides);
        Assert.Contains(applied.ToString(), said);
        Assert.Equal("override: people:motions/attack_sword from module tactical (Overrides/people/motions/attack_sword.glb) in place of module people's own", applied.ToString());
        // Only in a game that loads the overrider.
        Assert.Equal("people's", System.Text.Encoding.UTF8.GetString(ModuleChecker.Check([s.Root], Game("people")).Assets!.Read(id)));
    }

    [Fact]
    public void Overrides_the_load_order_does_not_decide_or_of_ids_not_depended_on_or_not_there_are_refused()
    {
        using var s = new Scratch();
        string over = "\"overrides\": [\"people:motions/attack_sword\"]";
        s.Add("People", Manifest("people"), ("Content/motions/attack_sword.glb", "people's"));
        s.Add("Tactical", Manifest("tactical", kind: "rules", deps: "\"people\": \"1.0.0\"", extra: over), ("Overrides/people/motions/attack_sword.glb", "t"));
        s.Add("Action", Manifest("action", kind: "rules", deps: "\"people\": \"1.0.0\"", extra: over), ("Overrides/people/motions/attack_sword.glb", "a"));
        s.Add("Stray", Manifest("stray", extra: over), ("Overrides/people/motions/attack_sword.glb", "s"));
        s.Add("Ghost", Manifest("ghost", deps: "\"people\": \"1.0.0\"", extra: "\"overrides\": [\"people:motions/fly\", \"people:motions/swim\"]"), ("Overrides/people/motions/fly.glb", "f"));
        var check = ModuleChecker.Check([s.Root], Game("people", "tactical", "action", "stray", "ghost"));
        Assert.Contains("modules tactical and action both override people:motions/attack_sword and neither depends on the other, so the load order does not say which wins", check.Problems);
        Assert.Contains("module stray overrides people:motions/attack_sword, but does not depend on module people", check.Problems);
        Assert.Contains("module ghost overrides people:motions/fly, which module people does not have", check.Problems);
        Assert.Contains("module ghost: it declares an override of people:motions/swim but has no file for it under Overrides/people/", check.Problems);
        // When one depends on the other, the dependant (later) wins.
        File.WriteAllText(Path.Combine(s.Root, "Action", ModuleManifest.FileName),
            Manifest("action", kind: "rules", deps: "\"people\": \"1.0.0\", \"tactical\": \"1.0.0\"", extra: over));
        var ordered = ModuleChecker.Check([s.Root], Game("action", "tactical", "people"));
        Assert.True(ordered.Ok, string.Join("\n", ordered.Problems));
        Assert.Equal("a", System.Text.Encoding.UTF8.GetString(ordered.Assets!.Read(AssetId.Parse("people:motions/attack_sword"))));
        Assert.Equal(["tactical", "action"], ordered.Assets.Overrides.Select(o => o.By));
    }

    // ---- the registry ----

    [Fact]
    public void The_registry_resolves_module_files_and_each_module_reads_only_what_it_depends_on()
    {
        var tests = Path.Combine(AppContext.BaseDirectory, "TestModules");
        using var s = new Scratch();
        s.Add("Rules", Manifest("rules", kind: "rules", deps: "\"testpeople\": \"1.0.0\""), ("Data/profiles/a.json", "{\"a\": 1}"), ("Data/top.json", "{}"));
        s.Add("Other", Manifest("other"), ("Data/secret.json", "{}"));
        var check = ModuleChecker.Check([tests, s.Root], Game("testpeople", "rules", "other"));
        Assert.True(check.Ok, string.Join("\n", check.Problems));
        var registry = check.Assets!;
        // The checked-in content module: two motions, no code, no rules.
        var people = Assert.Single(check.Order, m => m.Id == "testpeople");
        Assert.Equal(ModuleKind.Content, people.Kind);
        Assert.False(people.HasCode);
        Assert.Equal(["testpeople:motions/attack_sword", "testpeople:motions/run"], registry.List("testpeople", "motions").Select(i => i.ToString()));
        Assert.Equal(Path.Combine(tests, "testpeople", "Content", "motions", "run.txt"), registry.Resolve(AssetId.Parse("testpeople:motions/run")).File);

        var rules = registry.For("rules");
        Assert.Equal(AssetId.Parse("rules:profiles/a"), rules.Own("profiles/a"));
        Assert.Equal(["rules:profiles/a"], rules.List("profiles").Select(i => i.ToString()));
        Assert.Equal("{\"a\": 1}", rules.ReadText(rules.Own("profiles/a")));
        Assert.StartsWith("a placeholder for the run motion", rules.ReadText(AssetId.Parse("testpeople:motions/run")));
        Assert.False(rules.Exists(AssetId.Parse("testpeople:motions/fly")));
        Assert.Contains("module testpeople has no such asset", Assert.Throws<AssetException>(() => rules.Read(AssetId.Parse("testpeople:motions/fly"))).Message);
        // Another module's ids, not depended on, are refused by name; and a content module reads none of the rules'.
        Assert.False(rules.Exists(AssetId.Parse("other:secret")));
        Assert.Equal("module rules may not read other:secret: it does not depend on module other",
            Assert.Throws<AssetException>(() => rules.Read(AssetId.Parse("other:secret"))).Message);
        Assert.Throws<AssetException>(() => registry.For("testpeople").Read(AssetId.Parse("rules:top")));
        Assert.True(registry.Exists(AssetId.Parse("other:secret")));
    }

    [Fact]
    public void The_registry_reads_oneone_ids_from_the_players_install()
    {
        using var s = new Scratch();
        s.Add("Rules", Manifest("rules", kind: "rules"));
        var none = ModuleChecker.Check([s.Root], Game("rules")).Assets!;
        Assert.Equal("oneone:3d/models/christof.nod: no 1.1 install is open", Assert.Throws<AssetException>(() => none.Read(AssetId.Parse("oneone:3d/models/christof.nod"))).Message);

        using var install = GameInstall.TryOpen(Environment.GetEnvironmentVariable("VTMR_GAME_DIR"));
        Assert.SkipUnless(install is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        var found = install.FindByFileName("christof.nod");
        Assert.SkipUnless(found is not null, "no christof.nod in the install");
        var registry = ModuleChecker.Check([s.Root], Game("rules"), install).Assets!;
        var id = AssetId.Parse($"oneone:{found.ToLowerInvariant()}");
        var view = registry.For("rules");
        Assert.True(view.Exists(id));
        Assert.Equal(install.Read(found), view.Read(id));
        Assert.Equal(AssetId.OneOne, registry.Resolve(id).Module);
        Assert.False(view.Exists(AssetId.Parse("oneone:3d/models/no_such_model.nod")));
    }

    // ---- the loader ----

    /// <summary>A module of the tests: reads its own data by id and says what it was given.</summary>
    public sealed class ProbeModule : IModule
    {
        public ModuleInfo Info { get; } = new("probe-rules", "1.0.0", "none");
        public ModuleStamp Stamp { get; private set; } = new("probe-rules", "", "none", false, "");
        public ModuleConfigContext? Given { get; private set; }
        public string Settings { get; private set; } = "";
        public string? Banner => Given is { } g && g.Switches.Contains("loud") ? "PROBE IS LOUD" : null;

        public void Configure(ModuleConfigContext context)
        {
            Given = context;
            if (!context.Assets.Exists(context.Assets.Own("settings"))) throw new ModuleRefusedException(Info.Id, ["probe:settings is missing", "and a second reason"]);
            Settings = context.Assets.ReadText(context.Assets.Own("settings"));
            Stamp = Stamp with { ProfileId = context.Profile ?? "default" };
        }

        public void Register(WorldBuilder world) =>
            world.People.OnMade((e, role) => world.Peek.Set(e, new Made { Role = (int)role + 1 }));
    }

    public struct Made { public int Role; }

    public sealed class OldProbeModule : IModule
    {
        public ModuleInfo Info { get; } = new("old", "0.9.0", "none");
        public ModuleStamp Stamp { get; } = new("old", "", "none", false, "");
        public void Configure(ModuleConfigContext context) { }
        public void Register(WorldBuilder world) { }
    }

    public sealed class NotAModule { }

    [Fact]
    public void The_loader_makes_a_modules_code_and_configures_it_by_id_with_its_declared_switches()
    {
        using var s = new Scratch();
        s.AddCode("Probe", "probe", "1.0.0", typeof(ProbeModule), "\"switches\": { \"loud\": \"be loud\", \"quiet\": \"be quiet\" }", ("Data/settings.json", "{\"x\": 1}"));
        var game = ModuleLoader.Load(new ModuleLoadOptions
        {
            ModuleFolders = [s.Root], Game = Game("probe"), Profile = "p1",
            Switches = new HashSet<string>(["loud", "headless", "sandbox"], StringComparer.Ordinal),
        });
        var probe = Assert.IsType<ProbeModule>(Assert.Single(game.Code));
        Assert.Equal("{\"x\": 1}", probe.Settings);
        Assert.Equal("probe", probe.Given!.Assets.Module);
        Assert.Equal(["loud"], probe.Given.Switches);
        Assert.Equal("p1", probe.Stamp.ProfileId);
        Assert.Equal("PROBE IS LOUD", game.Banner);
        Assert.Equal(["loud"], game.Modules[0].Switches);
        Assert.Matches("^[0-9a-f]{64}$", game.Modules[0].FilesHash);

        // Its registration: the people the program makes are said to it.
        var b = new WorldBuilder(1);
        b.Component<Made>();
        game.Register(b);
        var w = b.Build();
        var e = w.Create();
        w.Service<PeopleHooks>().Made(e, PersonRole.Townsperson);
        Assert.Equal(2, w.Get<Made>(e).Role);
    }

    [Fact]
    public void A_module_refusing_is_given_a_hint_naming_its_switches_and_bad_code_is_refused_by_name()
    {
        using var s = new Scratch();
        s.AddCode("Probe", "probe", "1.0.0", typeof(ProbeModule), "\"switches\": { \"loud\": \"be loud\" }");
        var refused = Assert.Throws<ModuleRefusedException>(() => ModuleLoader.Load(new ModuleLoadOptions { ModuleFolders = [s.Root], Game = Game("probe") }));
        Assert.Equal(["probe:settings is missing", "and a second reason"], refused.Reasons);
        Assert.Equal("Start with --loud to be loud.", refused.Hint);

        s.AddCode("Old", "old", "1.0.0", typeof(OldProbeModule));
        s.AddCode("Nota", "nota", "1.0.0", typeof(NotAModule));
        var check = Assert.Throws<ModuleCheckException>(() => ModuleLoader.Load(new ModuleLoadOptions { ModuleFolders = [s.Root], Game = Game("old", "nota") }));
        Assert.Contains("module old: its code says version 0.9.0 and its module.json 1.0.0; they must agree", check.Problems);
        Assert.Contains(check.Problems, p => p.StartsWith("module nota: Vitomir.Platform.Tests.ModuleTests+NotAModule is not a module", StringComparison.Ordinal));
        // A manifest naming an assembly its folder does not have is refused at the check.
        s.Add("Missing", Manifest("missing", kind: "rules", extra: "\"assembly\": \"bin/Missing.dll\", \"type\": \"Missing.Module\""));
        Assert.Contains(Assert.Throws<ModuleCheckException>(() => ModuleLoader.Load(new ModuleLoadOptions { ModuleFolders = [s.Root], Game = Game("missing") })).Problems,
            p => p.Contains("its assembly bin/Missing.dll is not in"));
    }

    [Fact]
    public void A_game_is_found_by_id_or_file_and_its_file_read_strictly()
    {
        using var s = new Scratch();
        File.WriteAllText(Path.Combine(s.Root, "action.json"), """{ "id": "action", "name": "The action game", "modules": ["prague1141", "people", "enhanced"] }""");
        File.WriteAllText(Path.Combine(s.Root, "wrong.json"), """{ "id": "other", "name": "x", "modules": ["a"] }""");
        File.WriteAllText(Path.Combine(s.Root, "bad.json"), """{ "id": "bad", "name": "x", "modules": ["a", "a", "B"], "mods": [] }""");
        var game = ModuleLoader.FindGame("action", [s.Root]);
        Assert.Equal(["prague1141", "people", "enhanced"], game.Modules);
        Assert.Equal(game.Modules, ModuleLoader.FindGame(Path.Combine(s.Root, "action.json"), []).Modules);
        Assert.Contains("its id is 'other', not 'wrong'", Assert.Single(Assert.Throws<ModuleCheckException>(() => ModuleLoader.FindGame("wrong", [s.Root])).Problems));
        Assert.StartsWith("no game 'missing'", Assert.Single(Assert.Throws<ModuleCheckException>(() => ModuleLoader.FindGame("missing", [s.Root])).Problems));
        var bad = Assert.Throws<ModuleCheckException>(() => ModuleLoader.FindGame("bad", [s.Root])).Problems;
        Assert.Contains(bad, p => p.Contains("'modules': a is listed twice"));
        Assert.Contains(bad, p => p.Contains("must start with a lower-case letter"));
        Assert.Contains(bad, p => p.Contains("unknown field 'mods'"));
    }

    /// <summary>
    /// **A command line that names no game plays the base game where it is the only one**: a games folder with the default's file
    /// (or with neither) leaves the default; one with <c>classic.json</c> and no <c>enhanced.json</c>, as release 1's package's
    /// <c>bin\Games\</c>, plays the base game.
    /// </summary>
    [Fact]
    public void With_no_game_named_the_base_game_is_played_where_it_is_the_only_one()
    {
        using var s = new Scratch();
        Assert.Null(ModuleLoader.WhenNoneNamed(s.Root));
        File.WriteAllText(Path.Combine(s.Root, "classic.json"), "{}");
        Assert.Equal(ModuleLoader.ClassicGame, ModuleLoader.WhenNoneNamed(s.Root));
        File.WriteAllText(Path.Combine(s.Root, ModuleLoader.DefaultGame + ".json"), "{}");
        Assert.Null(ModuleLoader.WhenNoneNamed(s.Root));
        Assert.Null(ModuleLoader.WhenNoneNamed(Path.Combine(s.Root, "none")));
    }

    // ---- recordings ----

    [Fact]
    public void A_recording_made_with_other_modules_is_said_plainly()
    {
        using var s = new Scratch();
        s.Add("People", Manifest("people", version: "1.2.0"));
        s.Add("Arms", Manifest("arms"));
        var game = ModuleLoader.Load(new ModuleLoadOptions { ModuleFolders = [s.Root], Game = Game("people", "arms") });
        var here = game.Recorded;
        Assert.Equal(["people", "arms"], here.Select(m => m.Id));

        Assert.Empty(game.Differences(here, out bool same));
        Assert.True(same);
        Assert.Contains("names no modules", Assert.Single(game.Differences([], out same)));
        Assert.True(same);
        // The same versions with other files: said, and the digests decide.
        Assert.Contains("module people 1.2.0's files differ from the recording's", Assert.Single(game.Differences([here[0] with { Hash = "0123456789abcdef" }, here[1]], out same)));
        Assert.True(same);
        // Another version, another set, another order: not the same modules.
        Assert.Equal("module people is version 1.2.0 here and was 1.1.0 when recorded", Assert.Single(game.Differences([here[0] with { Version = "1.1.0" }, here[1]], out same)));
        Assert.False(same);
        var other = game.Differences([here[0], new TapeModule("tactical", "1.0.0", "x")], out same);
        Assert.False(same);
        Assert.Contains("it was recorded with module tactical 1.0.0, which game test does not load here", other);
        Assert.Contains("module arms 1.0.0 is loaded here and was not when recorded", other);
        Assert.Contains("load in another order", Assert.Single(game.Differences([here[1], here[0]], out same)));
        Assert.False(same);
    }

    [Fact]
    public void A_modules_files_hash_changes_with_any_file_but_its_debug_symbols()
    {
        using var s = new Scratch();
        var dir = s.Add("People", Manifest("people"), ("Content/motions/run.glb", "run"));
        string a = ModuleLoader.FilesHash(dir);
        Assert.Equal(a, ModuleLoader.FilesHash(dir));
        File.WriteAllText(Path.Combine(dir, "bin.pdb"), "symbols");
        Assert.Equal(a, ModuleLoader.FilesHash(dir));
        File.WriteAllText(Path.Combine(dir, "Content", "motions", "run.glb"), "run!");
        Assert.NotEqual(a, ModuleLoader.FilesHash(dir));
    }

    [Fact]
    public void The_people_hooks_run_every_handler_in_the_order_registered()
    {
        var b = new WorldBuilder(1);
        var said = new List<string>();
        b.People.OnMade((e, r) => said.Add($"first {r}"));
        b.People.OnMade((e, r) => said.Add($"second {r}"));
        var w = b.Build();
        w.Service<PeopleHooks>().Made(w.Create(), PersonRole.Player);
        Assert.Equal(["first Player", "second Player"], said);
        Assert.Same(b.People, w.Service<PeopleHooks>());
    }
}
