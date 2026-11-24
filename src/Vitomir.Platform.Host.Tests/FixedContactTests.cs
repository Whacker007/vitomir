// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Host.Scenario;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// The fixed-contact kind (<c>"kind": "contact"</c>, FIXED_CONTACT) — a fixed contact reaches the rules' harm with
/// the same receipt chain as a swept one (placed → contact → harm, as a blow's begun → contact → harm), the same contact on the same seed
/// gives the same harm, the reader refuses a misspelt region with the nearest names (and the kind's other refusals); one run again by
/// its id (<c>--only-run</c>) reproduces its digest, and an event's chain is read from its stream; the range ground (built in code) has
/// navigation, and a 100 m shot on it meets its target; examples 05, 06 and 07 check and run. Skips without the 1.1 install or the
/// proving ground.
/// </summary>
public sealed class FixedContactTests
{
    static (string Course, string Game) Grounds()
    {
        var course = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        var game = GameInstall.Locate();
        Assert.SkipUnless(course is not null && File.Exists(Path.Combine(course, "level.json")), "no proving ground (VITOMIR_COURSE_LEVEL: a cooked proving-ground.l2)");
        Assert.SkipUnless(game is not null, "the 1.1 install is not here (VTMR_GAME_DIR, or C:\\Games\\VTMR)");
        Environment.SetEnvironmentVariable("VITOMIR_USERDATA", Path.Combine(Path.GetTempPath(), "vitomir-scenario-tests-userdata"));
        return (course!, game!);
    }

    static SessionOptions Baseline()
    {
        var (course, game) = Grounds();
        return new SessionOptions { Level = course, Game = game, Switches = new HashSet<string>(StringComparer.Ordinal) { "sandbox" } };
    }

    static string Folder(string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"vitomir-fixed-{name}-{Environment.ProcessId}");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        return dir;
    }

    static CompiledScenario Compile(string folder, string name, string text)
    {
        Grounds();   // a scenario on the proving ground reads its level when it is compiled: skip first, as Baseline() does, never fail
        string path = Path.Combine(folder, name + ".scenario.json");
        File.WriteAllText(path, text);
        var load = ScenarioSpec.Load(path);
        Assert.True(load.Ok, string.Join("\n", load.Errors.Select(e => e.Line())));
        return load.Scenario!;
    }

    /// <summary>A run's stream: each event's kind, id, parent and its record's fields.</summary>
    static List<JsonObject> Stream(string path) =>
        [.. File.ReadLines(path).Skip(1).Where(l => l.Length > 0).Select(l => JsonNode.Parse(l)!.AsObject())];

    static long Id(JsonObject e) => e["eventId"]!.GetValue<long>();
    static long? Parent(JsonObject e) => e["parentEventId"] is JsonValue v && v.TryGetValue(out long p) ? p : null;
    static string Kind(JsonObject e) => e["kind"]!.GetValue<string>();

    const string Contact = """
        { "format": "vitomir-scenario/1", "title": "one fixed contact", "kind": "contact", "ground": "free-yard",
          "actors": [ { "name": "A", "template": "Christof", "sheet": "sandbox.newly_embraced", "gear": { "weapon": "Rapier", "shield": false }, "place": [38, 3], "foe": "B" },
                      { "name": "B", "template": "rudolfGuard", "sheet": "sandbox.guard_trained", "gear": { "weapon": "Rapier", "shield": false },
                        "place": { "from": "A", "distanceMetres": 1.2 }, "foe": "A" } ],
          "contacts": [ { "atSeconds": 0.5, "attacker": "A", "defender": "B", "region": "leftUpperArm", "part": "edge",
                          "closingSpeedMetresPerSecond": 25.8, "squarenessDegrees": 56, "swing": "right" } ],
          "stop": { "afterSeconds": 1 }, "runs": RUNS, "seed": 77,
          "outputs": { "tracePolicy": "ALL" } }
        """;

    // ---- a fixed contact reaches harm with a swept contact's receipts ----

    [Fact]
    public void A_fixed_contact_reaches_harm_with_the_same_receipt_chain_as_a_swept_one()
    {
        string folder = Folder("chain");
        var plan = ScenarioPlan.From(Compile(folder, "fixed", Contact.Replace("RUNS", "1")));
        var (results, exit) = ScenarioRunner.Execute(plan, Baseline(), folder, _ => { });
        Assert.Equal(0, exit);
        Assert.Equal("FIXED_CONTACT", results.Points[0].ExecutionMode);
        var events = Stream(Path.Combine(folder, "events", "p000-r000.events.jsonl"));
        var placed = Assert.Single(events, e => Kind(e) == "FixedContactPlaced");
        var contact = Assert.Single(events, e => Kind(e) == "ContactMade");
        var harm = Assert.Single(events, e => Kind(e) == "HarmResolved");
        Assert.True(placed["event"]!["Placed"]!.GetValue<bool>());
        Assert.True(contact["event"]!["Fixed"]!.GetValue<bool>());
        Assert.Equal("Body", contact["event"]!["Met"]!.GetValue<string>());
        Assert.Equal("left upper arm", contact["event"]!["Site"]!.GetValue<string>());
        // The chain: placed → contact → harm (a swept blow's is AttackBegun → ContactMade → HarmResolved).
        Assert.Equal(Id(placed), Parent(contact));
        Assert.Equal(Id(contact), Parent(harm));
        Assert.True(harm["derived"]!["fixed"]!.GetValue<bool>());
        Assert.Equal(contact["event"]!["ContactId"]!.GetValue<long>(), harm["event"]!["ContactId"]!.GetValue<long>());
        // The speeds as given: closing 25.8 at 56°, the speed into the surface closing × sin(56°).
        Assert.Equal(25.8, contact["event"]!["PointSpeed"]!.GetValue<double>(), 3);
        Assert.Equal(25.8 * Math.Sin(56 * Math.PI / 180), contact["event"]!["ImpactSpeed"]!.GetValue<double>(), 3);
        // The rules' own trace staged it as a swept contact (and says it was fixed).
        string trace = File.ReadAllText(Path.Combine(folder, "events", "p000-r000.harm.jsonl"));
        Assert.Contains("\"fixed\":true", trace);
        Assert.Contains("\"stage\":\"Contact\"", trace);

        // A swept blow in the same world: the same kinds follow its contact, by the same links.
        string swept = Folder("chain-swept");
        var duel = ScenarioPlan.From(Compile(swept, "swept", """
            { "format": "vitomir-scenario/1", "title": "one swept blow", "ground": "free-yard",
              "actors": [ { "name": "A", "template": "Christof", "sheet": "sandbox.newly_embraced", "gear": { "weapon": "Rapier", "shield": false }, "place": [38, 3], "foe": "B",
                            "control": { "script": [ { "attack": "right", "aimAt": "upperArm" } ] } },
                          { "name": "B", "template": "rudolfGuard", "sheet": "sandbox.guard_trained", "gear": { "weapon": "Rapier", "shield": false },
                            "place": { "from": "A", "distanceMetres": 1.3 }, "foe": "A", "control": { "ai": "stand" } } ],
              "stop": { "afterSeconds": 2 }, "runs": 1, "seed": 77, "outputs": { "tracePolicy": "ALL" } }
            """));
        ScenarioRunner.Execute(duel, Baseline(), swept, _ => { });
        var sweptEvents = Stream(Path.Combine(swept, "events", "p000-r000.events.jsonl"));
        var sweptContact = sweptEvents.First(e => Kind(e) == "ContactMade" && e["event"]!["Met"]!.GetValue<string>() == "Body");
        var sweptHarm = sweptEvents.First(e => Kind(e) == "HarmResolved");
        Assert.False(sweptContact["event"]!["Fixed"]!.GetValue<bool>());
        Assert.Equal("AttackBegun", Kind(sweptEvents.First(e => Id(e) == Parent(sweptContact))));
        Assert.Equal(Id(sweptContact), Parent(sweptHarm));
        Assert.Null(sweptHarm["derived"]?["fixed"]);   // written only for a fixed contact's harm
        var after = new[] { "HarmResolved", "Staggered", "CatchLetGo" };
        foreach (var k in after.Where(k => sweptEvents.Any(e => Kind(e) == k && Parent(e) == Id(sweptContact))))
            Assert.Contains(events, e => Kind(e) == k && Parent(e) == Id(contact));
    }

    [Fact]
    public void A_fixed_contact_on_the_guard_is_judged_by_the_guard_contest_and_harms_nobody()
    {
        string folder = Folder("guard");
        var plan = ScenarioPlan.From(Compile(folder, "guard", """
            { "format": "vitomir-scenario/1", "title": "fixed contacts on the guard", "kind": "contact",
              "actors": [ { "name": "A", "template": "Christof", "sheet": "sandbox.newly_embraced", "gear": { "weapon": "Rapier", "shield": false }, "place": [38, 3], "foe": "B" },
                          { "name": "B", "template": "rudolfGuard", "sheet": "sandbox.guard_trained", "gear": { "weapon": "weapRapier_shield" },
                            "place": { "from": "A", "distanceMetres": 1.2 }, "foe": "A" } ],
              "contacts": [ { "atSeconds": 0.5, "meets": "shield" }, { "atSeconds": 1.0, "meets": "heldBlade" }, { "atSeconds": 1.5, "meets": "parryingBlade" } ],
              "stop": { "afterSeconds": 2 }, "runs": 1, "seed": 5, "outputs": { "tracePolicy": "ALL" } }
            """));
        ScenarioRunner.Execute(plan, Baseline(), folder, _ => { });
        var events = Stream(Path.Combine(folder, "events", "p000-r000.events.jsonl"));
        var contacts = events.Where(e => Kind(e) == "ContactMade").ToList();
        Assert.Equal(["Shield", "Weapon", "Weapon"], contacts.Select(c => c["event"]!["Met"]!.GetValue<string>()));
        Assert.Equal(["Shield", "Blade", "Parry"], contacts.Select(c => c["event"]!["Guard"]!.GetValue<string>()));
        var verdicts = events.Where(e => Kind(e) == "GuardResolved").ToList();
        Assert.Equal(3, verdicts.Count);
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(Id(contacts[i]), Parent(verdicts[i]));
            Assert.True(verdicts[i]["derived"]!["fixed"]!.GetValue<bool>());
        }
        Assert.DoesNotContain(events, e => Kind(e) == "HarmResolved" && e["event"]!["Wounds"]!.GetValue<int>() > 0);
        // The reader: a meeting it has not; a sweep to the body of a contact with no region.
        var bad = Has(Read(WithContact("""{ "atSeconds": 0.5, "meets": "sheild" }""")), ScenarioErrorCode.OutOfRange, "/contacts/0/meets");
        Assert.Contains("shield", bad.Suggestions);
        Has(Read(WithContact("""{ "atSeconds": 0.5, "meets": "shield" }""", extra: """, "sweep": [ { "set": "contacts.0.meets", "values": ["shield", "body"] } ]""")), ScenarioErrorCode.BadSweep, "/sweep/0/values/1");
        Assert.True(Read(WithContact("""{ "atSeconds": 0.5, "meets": "shield", "region": "chest" }""", extra: """, "sweep": [ { "set": "contacts.0.meets", "values": ["shield", "body"] } ]""")).Ok);
    }

    // ---- the same contact, the same seed, the same harm ----

    [Fact]
    public void The_same_fixed_contact_on_the_same_seed_gives_the_same_harm_and_digest()
    {
        string a = Folder("same-a"), b = Folder("same-b");
        var plan = ScenarioPlan.From(Compile(a, "fixed", Contact.Replace("RUNS", "3")));
        ScenarioRunner.Execute(plan, Baseline(), a, _ => { });
        ScenarioRunner.Execute(plan, Baseline(), b, _ => { }, new ScenarioRunOptions { Jobs = 2 });
        for (int r = 0; r < 3; r++)
        {
            string id = $"p000-r{r:000}";
            var ha = Stream(Path.Combine(a, "events", id + ".events.jsonl")).Single(e => Kind(e) == "HarmResolved")["event"]!.ToJsonString();
            var hb = Stream(Path.Combine(b, "events", id + ".events.jsonl")).Single(e => Kind(e) == "HarmResolved")["event"]!.ToJsonString();
            Assert.Equal(ha, hb);
            Assert.Equal(ScenarioTraceEvent.DigestIn(a, id), ScenarioTraceEvent.DigestIn(b, id));
        }
        // A fixed contact's tape replays: the program (with its contacts) is in the tape's head.
        Assert.True(File.Exists(Path.Combine(a, "tapes", "p000-r001.tape")));
    }

    // ---- the reader's refusals ----

    static ScenarioLoad Read(string json) => ScenarioSpec.Read(json, Path.GetTempPath(), null, ArenaSpecTests.FakeLevel.Value, levelFromEnvironment: false);

    static string WithContact(string contact, string extra = "", string control = "") => $$"""
        { "format": "vitomir-scenario/1", "kind": "contact",
          "actors": [ { "name": "A", "place": [38, 3] {{control}} }, { "name": "B", "template": "rudolfGuard", "place": [39.2, 3] } ],
          "contacts": [ {{contact}} ] {{extra}} }
        """;

    static ScenarioError Has(ScenarioLoad load, ScenarioErrorCode code, string pointer)
    {
        var hit = load.Errors.FirstOrDefault(e => e.Code == code && e.Pointer == pointer);
        Assert.True(hit is not null, $"expected {code} at {pointer}; got: {string.Join(" | ", load.Errors.Select(e => e.Line()))}");
        return hit!;
    }

    [Fact]
    public void A_region_the_body_has_not_is_refused_with_the_nearest_names_and_the_kinds_other_refusals_point_at_their_field()
    {
        var region = Has(Read(WithContact("""{ "atSeconds": 0.5, "region": "uperArm" }""")), ScenarioErrorCode.OutOfRange, "/contacts/0/region");
        Assert.Contains("upperArm", region.Suggestions);
        Assert.Contains("is not a body region", region.Message);
        Has(Read(WithContact("""{ "atSeconds": 0.5, "region": "chest", "closingSpeedMetresPerSecond": -1 }""")), ScenarioErrorCode.OutOfRange, "/contacts/0/closingSpeedMetresPerSecond");
        Has(Read(WithContact("""{ "atSeconds": 0.5, "region": "chest", "squarenessDegrees": 120 }""")), ScenarioErrorCode.OutOfRange, "/contacts/0/squarenessDegrees");
        Has(Read(WithContact("""{ "region": "chest" }""")), ScenarioErrorCode.BadContact, "/contacts/0/atSeconds");
        Has(Read(WithContact("""{ "atSeconds": 0.5 }""")), ScenarioErrorCode.BadContact, "/contacts/0/region");
        Has(Read(WithContact("""{ "atSeconds": 0.5, "region": "chest", "part": "edge", "alongBladeShare": 0.3 }""")), ScenarioErrorCode.BadContact, "/contacts/0/part");
        var flat = Has(Read(WithContact("""{ "atSeconds": 0.5, "region": "chest", "part": "flat" }""")), ScenarioErrorCode.OutOfRange, "/contacts/0/part");
        Assert.Contains("no flat-of-the-blade", flat.Message);
        var near = Has(Read(WithContact("""{ "atSeconds": 0.5, "region": "chest", "closingSpeed": 3 }""")), ScenarioErrorCode.UnknownField, "/contacts/0/closingSpeed");
        Assert.Contains("closingSpeedMetresPerSecond", near.Suggestions);
        Has(Read(WithContact("""{ "atSeconds": 0.5, "region": "chest", "attacker": "C" }""")), ScenarioErrorCode.UnknownName, "/contacts/0/attacker");
        Has(Read(WithContact("""{ "atSeconds": 0.5, "region": "chest", "defender": "A" }""")), ScenarioErrorCode.BadContact, "/contacts/0/defender");
        // No native blow beside fixed ones; no contacts in a fight.
        Has(Read(WithContact("""{ "atSeconds": 0.5, "region": "chest" }""", control: """, "control": { "script": [ { "attack": "right" } ] }""")), ScenarioErrorCode.BadContact, "/actors/0/control/script/0");
        Has(Read(WithContact("""{ "atSeconds": 0.5, "region": "chest" }""").Replace("\"kind\": \"contact\",", "")), ScenarioErrorCode.BadContact, "/contacts");
        // A duelling brain in a contact scenario is muted on attack, said.
        var muted = Read(WithContact("""{ "atSeconds": 0.5, "region": "chest" }""", control: """, "control": { "ai": "duel" }"""));
        Assert.True(muted.Ok, string.Join("\n", muted.Errors.Select(e => e.Line())));
        Assert.Contains("attack", muted.Resolved!["actors"]![0]!["control"]!["mute"]!.AsArray().Select(m => m!.GetValue<string>()));
        Assert.Contains(muted.Warnings, w => w.Contains("muted on attack"));
        // A valid one: every default written; a sweep over a contact's field.
        var ok = Read(WithContact("""{ "atSeconds": 0.5, "region": "leftUpperArm" }""", extra: """, "sweep": [ { "set": "contacts.0.squarenessDegrees", "values": [10, 90] }, { "set": "contacts.all.part", "values": ["edge", "haft"] } ]"""));
        Assert.True(ok.Ok, string.Join("\n", ok.Errors.Select(e => e.Line())));
        var c = ok.Resolved!["contacts"]![0]!;
        Assert.Equal("B", c["defender"]!.GetValue<string>());
        Assert.Equal(25.8, c["closingSpeedMetresPerSecond"]!.GetValue<double>());
        Assert.Equal("its own", c["motionRate"]!.GetValue<string>());
        Assert.Equal(4, ok.Scenario!.Points.Count);
        Assert.Equal(0.05f, ok.Scenario.Points[1].Program!.Contacts![0].AlongBlade, 3);   // haft
        Assert.Equal(90f, ok.Scenario.Points[2].Program!.Contacts![0].SquarenessDegrees);
        Assert.Equal("FIXED_CONTACT", ok.Scenario.ExecutionMode);
        Has(Read(WithContact("""{ "atSeconds": 0.5, "region": "chest" }""", extra: """, "sweep": [ { "set": "contacts.3.squarenessDegrees", "values": [10] } ]""")), ScenarioErrorCode.BadPath, "/sweep/0/set");
        Has(Read(WithContact("""{ "atSeconds": 0.5, "region": "chest" }""", extra: """, "sweep": [ { "set": "contacts.0.squareness", "values": [10] } ]""")), ScenarioErrorCode.BadPath, "/sweep/0/set");
    }

    // ---- one run again, by its id ----

    [Fact]
    public void Only_run_reproduces_a_runs_digest_from_its_results_and_an_events_chain_is_read_from_its_stream()
    {
        string first = Folder("only-a"), again = Folder("only-b");
        var plan = ScenarioPlan.From(Compile(first, "duel", """
            { "format": "vitomir-scenario/1", "title": "two duellists", "ground": "free-yard",
              "actors": [ { "name": "A", "template": "Christof", "sheet": "sandbox.newly_embraced", "gear": { "weapon": "Rapier", "shield": false }, "place": [38, 3], "foe": "B", "control": { "ai": "duel" } },
                          { "name": "B", "template": "rudolfGuard", "sheet": "sandbox.guard_trained", "gear": { "weapon": "Rapier", "shield": false }, "place": [41, 3], "foe": "A", "control": { "ai": "duel" } } ],
              "stop": { "afterSeconds": 4 }, "runs": 3, "seed": 300, "sweep": [ { "set": "A.sheet.values.melee", "values": [1, 3] } ],
              "outputs": { "tracePolicy": "NONE" } }
            """));
        ScenarioRunner.Execute(plan, Baseline(), first, _ => { });
        var recorded = ScenarioTraceEvent.Recorded(Path.Combine(first, "results.json"), "p001-r002", out string why);
        Assert.True(recorded is not null, why);
        Assert.True(ScenarioTraceEvent.TryRunId("p001-r002", out int point, out int run));
        // The seed form (an assertion's first failing seed): point 1 at seed 302 is run 2 there.
        Assert.True(ScenarioTraceEvent.TryPointSeed("p001@302", out int seedPoint, out ulong seed) && seedPoint == 1 && seed == 302);
        Assert.False(ScenarioTraceEvent.TryRunId("p001@302", out _, out _));
        var (results, _) = ScenarioRunner.Execute(plan, Baseline(), again, _ => { },
            new ScenarioRunOptions { Only = point, OnlyRun = run, OnlyRunSeed = recorded!.Value.Seed });
        Assert.Equal(1, results.Summary.Runs);
        Assert.Equal(recorded.Value.Digest, ScenarioTraceEvent.DigestIn(again, "p001-r002"));
        // Its whole trace, though the policy kept none: the stream, the harm trace, the tape, every tick.
        foreach (var f in new[] { "events/p001-r002.events.jsonl", "events/p001-r002.harm.jsonl", "tapes/p001-r002.tape", "ticks/p001-r002.ticks.jsonl" })
            Assert.True(File.Exists(Path.Combine(again, f)), f);
        // An event's chain: the first harm's parents up to its blow, and what follows from it.
        var events = Stream(Path.Combine(again, "events", "p001-r002.events.jsonl"));
        var harm = events.FirstOrDefault(e => Kind(e) == "HarmResolved");
        Assert.SkipUnless(harm is not null, "the run made no harm in 4 s");
        var lines = new List<string>();
        int code = ScenarioTraceEvent.Run($"p001-r002#{Id(harm!)}", again, null, false, lines.Add, TextWriter.Null);
        Assert.Equal(0, code);
        Assert.Contains(lines, l => l.Contains("AttackBegun"));
        Assert.Contains(lines, l => l.Contains("ContactMade"));
        Assert.Contains(lines, l => l.Contains("> #" + Id(harm!).ToString(CultureInfo.InvariantCulture) + " "));
        Assert.Equal(1, ScenarioTraceEvent.Run("p001-r002#99999", again, null, false, _ => { }, TextWriter.Null));
    }

    // ---- the range ----

    [Fact]
    public void The_range_is_built_in_code_flat_and_open_with_a_route_from_end_to_end()
    {
        var range = BuiltInGrounds.Open(BuiltInGrounds.Range);
        Assert.NotNull(range.Navigation);
        Assert.True(range.Navigation!.Nearest(new Vector3(100f, 9f, 0f), out var on) != 0);
        Assert.Equal(0f, on.Z, 2);
        var corners = new Vector3[16];
        Assert.True(range.Navigation.Route(new Vector3(2f, -4f, 0f), new Vector3(168f, 22f, 0f), corners) > 0);
        Assert.Single(range.Starts);
        Assert.Equal(PlaceKind.Free, range.Places.At(new Vector3(100f, 9f, 0f)));
        // Level everywhere: a ray down meets the floor at 0 from end to end; nothing stands on it.
        for (float x = 1f; x < 170f; x += 13f)
            for (float y = -5f; y < 24f; y += 7f)
                Assert.Equal(0f, range.Collision.Ray(new Vector3(x, y, 3f), -Vector3.UnitZ, 5f)!.Value.Point.Z, 3);
        Assert.Null(range.Collision.Ray(new Vector3(1f, 9f, 1f), Vector3.UnitX, 168f));
        Assert.Throws<IOException>(() => BuiltInGrounds.Open("builtin:moon"));
    }

    [Fact]
    public void A_100_m_shot_on_the_range_meets_its_target_and_nothing_stops_the_arrows()
    {
        string folder = Folder("range-bow");
        var plan = ScenarioPlan.From(Compile(folder, "bow100", """
            { "format": "vitomir-scenario/1", "title": "example 01's archer at 100 m on the range", "ground": "range",
              "actors": [ { "name": "Christof", "sheet": { "template": "sandbox.human_reference", "values": { "melee": 2, "firearms": 2 } },
                            "gear": { "weapon": "BroadSword", "shield": false, "ranged": { "weapon": "war_bow", "arrows": 3 } },
                            "place": [21, 9], "facing": { "toward": "Guard" }, "foe": "Guard",
                            "control": { "script": [ { "equip": "ranged" }, { "repeat": 3, "steps": [ { "aim": { "at": "Guard", "region": "chest" } },
                              { "draw": { "toShare": 1.0 } }, { "wait": 0.4 }, { "loose": true } ] } ] } },
                          { "name": "Guard", "template": "rudolfGuard", "sheet": "sandbox.guard_trained", "gear": { "weapon": "Rapier", "shield": false },
                            "place": { "from": "Christof", "distanceMetres": 100, "bearingDegrees": 0 },
                            "facing": { "toward": "Christof" }, "foe": "Christof", "control": { "ai": "stand" } } ],
              "stop": { "afterSeconds": 12 }, "runs": 2, "seed": 9, "outputs": { "tracePolicy": "ALL" } }
            """));
        Assert.Equal(BuiltInGrounds.Range, plan.Level);
        ScenarioRunner.Execute(plan, Baseline(), folder, _ => { });
        int body = 0, spent = 0;
        foreach (var r in new[] { "p000-r000", "p000-r001" })
            foreach (var e in Stream(Path.Combine(folder, "events", r + ".events.jsonl")))
            {
                if (Kind(e) == "ContactMade" && e["event"]!["Met"]!.GetValue<string>() == "Body") body++;
                if (Kind(e) != "ArrowSpent") continue;
                spent++;
                Assert.True(e["event"]!["DistanceMetres"]!.GetValue<double>() > 95, "an arrow stopped short of 100 m (no wall on the range)");
            }
        Assert.Equal(6, spent);
        Assert.True(body >= 1, "no arrow of six met the guard at 100 m");
    }

    // ---- the examples ----

    /// <summary>By the name after the example's number (the examples' numbers may change).</summary>
    [Theory]
    [InlineData("fixed-contacts-upper-arm-speed-and-squareness.scenario.json")]
    [InlineData("contact-ab-native-scripted-blows.scenario.json")]
    [InlineData("contact-ab-fixed-half-p000-r000.scenario.json")]
    public void The_fixed_contact_examples_check_and_run(string stem)
    {
        Grounds();
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tools", "platform", "scenarios", "examples"))) dir = dir.Parent;
        Assert.SkipUnless(dir is not null, "the repository's tools/platform/scenarios/examples is not above the tests");
        var file = Assert.Single(Directory.GetFiles(Path.Combine(dir!.FullName, "tools", "platform", "scenarios", "examples"), "*-" + stem));
        var load = ScenarioSpec.Load(file);
        Assert.True(load.Ok, string.Join("\n", load.Errors.Select(e => e.Line())));
        string folder = Folder("example-" + stem[..12]);
        var (results, exit) = ScenarioRunner.Execute(ScenarioPlan.From(load.Scenario!), Baseline(), folder, _ => { }, new ScenarioRunOptions { Runs = 1, Only = 0 });
        Assert.True(exit is 0 or 4, $"exit {exit}");
        Assert.Equal(0, results.Summary.Failed);
        Assert.Equal(stem.Contains("native", StringComparison.Ordinal) ? "HYBRID" : "FIXED_CONTACT", results.Points[0].ExecutionMode);
    }
}
