// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json.Nodes;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Host.Arena;

namespace Vitomir.Platform.Host.Scenario;

/// <summary>
/// <c>--describe [all|&lt;list&gt;,…] [--json] [--sandbox]</c> (the output contract's
/// catalogue): everything a scenario's author may name, made from the loaded game and the code's own tables so it cannot drift —
/// characters, presets, sheets, traits, weapons, armour, powers and their options, switches (as named policy variants), the
/// brain's numbers with their defaults and ranges, the steps and their fields, the events and their fields (from their records),
/// the observables (read-only), the settable paths, the data assets, the grounds; and the catalogue of every property with a stable
/// id, its type, unit, range, default and its status, whether it is set at setup, during the run, needs a rebuild, or is not
/// controllable yet, the execution modes, the units and the error codes. With <c>--json</c>, one JSON document.
/// </summary>
public static partial class ScenarioDescribe
{
    /// <summary>The lists, in the order <c>all</c> gives them.</summary>
    public static readonly string[] Lists =
        ["characters", "presets", "sheets", "traits", "weapons", "armor", "powers", "switches", "brain", "steps", "events", "observables",
         "settable", "measures", "checks", "stops", "data", "grounds", "modes", "units", "properties", "coverage", "errors", "schemas",
         "places"];   // the level's named places (an objective's "place")

    internal static int Run(Cli cli, PlatformSettings settings, HostLog log, TextWriter output)
    {
        string what = cli.Arg("--describe") ?? "all";
        var asked = what == "all" ? Lists : what.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var a in asked)
            if (!Lists.Contains(a))
            {
                var near = ScenarioPointer.Nearest(a, Lists);
                log.Say($"error: --describe {a}: there is no such list (the lists: all, {string.Join(", ", Lists)}){(near.Count == 0 ? "" : $"; did you mean {string.Join(", ", near)}?")} [UnknownName]");
                return 1;
            }
        var options = new SessionOptions
        {
            Level = "",
            Game = Program.GameFolder(cli, settings),
            GameList = ModulesCommand.GameChoice(cli),
            ModulesFolder = cli.Arg("--modules"),
            Switches = ModulesCommand.Switches(cli),
            Say = log.Say,
        };
        using var game = ScenarioGame.Load(options, null);
        var doc = Describe(game, asked);
        if (cli.Flag("--json")) output.WriteLine(doc.ToJsonString(ScenarioSpec.Writing));
        else foreach (var line in Words(doc)) log.Say(line);
        return 0;
    }

    /// <summary>The asked lists as one JSON document: <c>{ "schema", "format", "build", "&lt;list&gt;": [...] }</c>.</summary>
    public static JsonObject Describe(ScenarioGame game, IReadOnlyList<string> asked)
    {
        var doc = new JsonObject
        {
            ["schema"] = "VITOMIR_SCENARIO_DESCRIBE_1",
            ["format"] = ScenarioWords.Format,
            ["build"] = PlatformVersion.Text,
            ["bow"] = ScenarioGame.BowBuilt ? "built" : "NOT_BUILT (a later build)",
            ["git"] = Git(),
            ["modules"] = new JsonArray([.. game.Stamps.Select(s => (JsonNode)new JsonObject { ["module"] = s.Module, ["configHash"] = s.ConfigHash, ["profile"] = s.Profile })]),
        };
        foreach (var list in asked) doc[list] = List(game, list);
        return doc;
    }

    /// <summary>The source's commit, branch and whether the tree is dirty (the results work's <see cref="GitInfo"/>), when known.</summary>
    internal static JsonObject Git()
    {
        var g = GitInfo.Read();
        return new JsonObject { ["commit"] = g.Commit, ["branch"] = g.Branch, ["dirty"] = g.Dirty, ["from"] = g.From, ["cloneCommit"] = g.CloneCommit };
    }

    static JsonArray Arr(IEnumerable<JsonNode?> items) => new([.. items]);
    static JsonArray Words(IEnumerable<string> words) => new([.. words.Select(w => (JsonNode?)w)]);

    static JsonNode List(ScenarioGame game, string list) => list switch
    {
        "characters" => Arr(game.Characters.Select(c => (JsonNode?)new JsonObject
        {
            ["template"] = c,
            ["in"] = OneOneIn(c, game),
            ["note"] = "a 1.1 character, read from the player's own 1.1 install; any 1.1 template with a model is accepted beside these",
        })),
        "presets" => Arr(game.Presets.Values.OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => (JsonNode?)new JsonObject
        {
            ["preset"] = p.Name, ["template"] = p.Template, ["weapon"] = p.Weapon, ["shield"] = p.Shield, ["sheet"] = p.Sheet, ["shown"] = p.Shown,
            ["movement"] = p.Movement ?? Session.HumanMovement, ["approach"] = p.Approach is { } pa ? ArenaSpec.NameOf(pa) : "duel",
            ["status"] = "SANDBOX", ["source"] = "the module's combat/foes (Data/combat/foes.json)",
        })),
        "sheets" => Arr(game.SheetTemplates.Select(id => game.Sheets.FirstOrDefault(s => s.Id == id) is { } s
            ? new JsonObject
            {
                ["id"] = s.Id, ["title"] = s.Title, ["species"] = s.Species, ["status"] = s.Status,
                ["purchased"] = new JsonObject(s.Purchased.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value))),
                ["powers"] = Words(s.Powers), ["note"] = "traits not listed are the module's least (Abilities 0); Health from rules/harm",
            }
            : (JsonNode?)new JsonObject { ["id"] = id, ["note"] = "made by the module's code (the sandbox's townsperson)" })),
        "traits" => Arr(game.Traits.Select(t => (JsonNode?)new JsonObject
        {
            ["id"] = t.Id, ["domain"] = t.Domain, ["category"] = t.Category, ["least"] = t.Least, ["most"] = t.Most, ["unit"] = t.Unit,
            ["status"] = t.Status, ["source"] = t.Source, ["setAs"] = $"<actor>.sheet.values.{t.Id}",
        })),
        "weapons" => Arr(game.Weapons.Select(w => (JsonNode?)new JsonObject
        {
            ["weapon"] = w, ["note"] = w.Contains("shield", StringComparison.OrdinalIgnoreCase) ? "carries a shield (in 1.1 a shield is part of the weapon)" : "1.1's weapon template",
            ["source"] = "the module's combat/weapons (Data/combat/weapons.json); any 1.1 weapon template is accepted beside these",
        })),
        "armor" => Arr(game.ArmorPieces.Select(a => (JsonNode?)new JsonObject { ["piece"] = a.Id, ["name"] = a.Name, ["slot"] = a.Slot, ["covers"] = Words(a.Covers), ["source"] = "Data/combat/armor.json" })),
        "powers" => Arr(game.Powers.Values.Select(p => (JsonNode?)new JsonObject
        {
            ["power"] = p.Id, ["label"] = p.Label, ["kind"] = p.Kind, ["options"] = Words(p.Options),
            ["ranks"] = Arr(p.Ranks.Select(r => (JsonNode?)new JsonObject { ["rank"] = r.Rank, ["costBloodPoints"] = r.Cost, ["seconds"] = r.Seconds })),
            ["status"] = p.Status, ["note"] = "a rank learns no option by itself: name its options (\"powers\": { \"" + p.Id + "\": { \"rank\": 2, \"options\": [...] } })",
        })),
        "switches" => Arr(game.Switches.OrderBy(s => s.Key, StringComparer.Ordinal).Select(s => (JsonNode?)new JsonObject
        {
            ["id"] = $"switch.{s.Key}", ["switch"] = s.Key, ["module"] = s.Value.Module, ["selects"] = s.Value.Help,
            ["kind"] = "policy variant", ["setAs"] = $"overrides.switches: [\"{s.Key}\"] (sweep: overrides.switches.{s.Key} true/false)",
        })),
        "brain" => Arr(BrainNumbers.All.Select(n => (JsonNode?)new JsonObject
        {
            ["number"] = n.Name, ["kind"] = n.Kind.ToString().ToLowerInvariant(), ["least"] = n.Least, ["most"] = n.Most, ["unit"] = n.Unit,
            ["default"] = n.Kind switch
            {
                BrainNumberKind.Flag => n.Get(new DuelBrainSettings()) != 0f,
                BrainNumberKind.Pace => "its kind of person's (data/combat/guard.json's brain_pace)",
                _ => Math.Round(n.Get(new DuelBrainSettings()), 6),
            },
            ["status"] = "ENGINE_SANDBOX", ["source"] = "src/Vitomir.Platform/Combat/DuelBrain.cs (DuelBrainSettings)", ["meaning"] = n.Meaning,
            ["setAs"] = $"<actor>.control.ai.{n.Name} (\"ai\": {{ \"kind\": \"duel\", \"{n.Name}\": ... }})",
        })),
        "steps" => new JsonObject
        {
            ["timing"] = Arr(ScenarioWords.Timing.Select(Field)),
            ["channels"] = Words(ScenarioWords.Channels),
            ["playerInput"] = new JsonObject
            {
                ["segmentFields"] = Words(["atSeconds", "forSeconds", "moveRight", "moveForward", "turnLeftDegrees", "turnUpDegrees", "hold", "press", "attackMode", "label"]),
                ["buttons"] = Words(ScenarioReader.ButtonNames),
                ["attackModes"] = Words(ScenarioReader.AttackModeNames),
                ["meaning"] = "control.playerInput: one actor driven through the player's own controls; segments from atSeconds for forSeconds (none: one tick): the move keys (-1 to 1), the look turned over the segment (degrees, left and up positive), buttons held through it and pressed on its first tick, the attack-direction mode set on its first tick",
            },
            ["steps"] = Arr(ScenarioWords.Steps.Select(s => (JsonNode?)new JsonObject
            {
                ["step"] = s.Name, ["takes"] = s.Takes, ["channels"] = Words(s.Channels), ["fields"] = Arr(s.Fields.Select(Field)),
                ["ends"] = s.Ends, ["status"] = s.Status == "bow" ? (ScenarioGame.BowBuilt ? "built" : "NOT_BUILT (a later build)") : s.Status, ["meaning"] = s.Meaning,
            })),
        },
        "events" => Arr(ScenarioWords.Events().Select(e => (JsonNode?)new JsonObject
        {
            ["event"] = e.Name,
            ["status"] = e.Status == "built" ? "built" : e.Status == "bow" ? (ScenarioGame.BowBuilt ? "built" : "NOT_BUILT (the bow)") : "coming (a later build)",
            ["by"] = e.By, ["on"] = e.On,
            ["fields"] = Arr(e.Fields.Select(f => (JsonNode?)new JsonObject { ["name"] = f.Name, ["type"] = f.Type, ["actor"] = f.Actor })),
            ["derived"] = Words(e.Derived),
            ["meaning"] = e.Meaning,
        })),
        "observables" => Arr(ScenarioWords.Observables.Select(o => (JsonNode?)new JsonObject { ["path"] = o.Path, ["unit"] = o.Unit, ["meaning"] = o.Meaning, ["control"] = "readOnly" })),
        "settable" => Arr(ScenarioWords.Settable.Select(s => (JsonNode?)new JsonObject { ["path"] = s.Path, ["takes"] = s.Takes, ["meaning"] = s.Meaning, ["status"] = s.Status == "built" ? "built" : $"NOT_BUILT ({s.Status})" })),
        "measures" => Arr(BuiltInMeasures.PerActor.Select(m => (JsonNode?)new JsonObject { ["name"] = $"<actor>.{m.Name}", ["unit"] = m.Unit, ["column"] = m.Column, ["meaning"] = m.Sentence })
            .Concat(BuiltInMeasures.PerRun.Select(m => (JsonNode?)new JsonObject { ["name"] = $"{BuiltInMeasures.RunScope}.{m.Name}", ["unit"] = m.Unit, ["column"] = m.Column, ["meaning"] = m.Sentence }))),
        "checks" => Arr(BuiltInChecks.All.Select(c => (JsonNode?)new JsonObject { ["check"] = c.Name, ["meaning"] = c.Sentence, ["status"] = c.NeedsBow && !ScenarioGame.BowBuilt ? "NOT_BUILT (the bow)" : "built" })),
        "stops" => Arr(typeof(StopNames).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string)).Select(f => (JsonNode?)new JsonObject { ["stop"] = (string)f.GetRawConstantValue()!, ["name"] = f.Name })),
        "schemas" => Schemas(),
        "data" => DataList(game),   // every asset with every leaf pointer (ScenarioDescribe.Overrides.cs)
        "grounds" => Arr(ScenarioGround.All.Select(g => (JsonNode?)new JsonObject { ["ground"] = g.Name, ["status"] = g.Status == "built" ? "built" : $"NOT_BUILT ({g.Status})", ["meaning"] = g.Meaning })),
        "modes" => Arr(ExecutionModes.All.Select(m => (JsonNode?)new JsonObject { ["executionMode"] = m.Mode, ["status"] = m.Status, ["meaning"] = m.Meaning })),
        "units" => Arr(Units.Select(u => (JsonNode?)new JsonObject { ["unit"] = u.Id, ["meaning"] = u.Meaning })),
        "properties" => Properties(game),
        "coverage" => Arr(Coverage.Select(c => (JsonNode?)new JsonObject { ["what"] = c.What, ["status"] = c.Status, ["meaning"] = c.Meaning })),
        "errors" => Arr(Enum.GetValues<ScenarioErrorCode>().Select(e => (JsonNode?)new JsonObject { ["code"] = e.ToString(), ["meaning"] = ErrorMeaning(e) })),
        "places" => Places(game),
        _ => new JsonArray(),
    };

    /// <summary>
    /// The published JSON schemas of what a run writes (the results work's <c>ScenarioSchemas</c>: results.json, an event line, the stream's
    /// head), generated from its records; read by reflection so this list follows the runner's file, or says it is not there.
    /// </summary>
    static JsonNode Schemas()
    {
        var o = new JsonObject();
        var type = typeof(ScenarioDescribe).Assembly.GetTypes().FirstOrDefault(t => t.Name == "ScenarioSchemas");
        foreach (var name in new[] { "Results", "EventLine", "EventHead" })
        {
            var m = type?.GetMethod(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, []);
            o[char.ToLowerInvariant(name[0]) + name[1..]] = m?.Invoke(null, null) is JsonNode schema ? schema.DeepClone() : "NOT_BUILT (ScenarioSchemas, a later build)";
        }
        return o;
    }

    static string OneOneIn(string template, ScenarioGame game) =>
        Vitomir.Platform.Assets.OneOne.OneOneCatalogue.Find(template) is not null ? "the catalogue (the player's characters)"
        : Townsfolk.Templates.Contains(template) ? "the townsfolk and the keepers of the peace"
        : game.Presets.Values.Any(p => p.Template == template) ? "a foe's preset" : "";

    static JsonNode? Field(StepField f) => new JsonObject { ["field"] = f.Name, ["takes"] = f.Takes, ["unit"] = f.Unit, ["default"] = f.Default, ["meaning"] = f.Meaning };

    /// <summary>The units a scenario's numbers are in (the output contract's units dictionary).</summary>
    public static readonly (string Id, string Meaning)[] Units =
    [
        ("attributePoints", "an Attribute's purchased value, the module's integer scale (0 to 1000; a human about 15 to 25)"),
        ("abilityDots", "an Ability's rating, 0 to 10"),
        ("powerRank", "a power's (Discipline's) rank, 0 to 10 (0 unlearns it); its options are learned separately"),
        ("dice", "a pool of dice the rules roll"),
        ("successes", "a count of dice that succeeded"),
        ("wounds", "a count of wounds (the module's SANDBOX conversion: 15 Health points a wound)"),
        ("healthPoints", "Health on the module's numerical scale, 1 to 1000 (a person 100: ENGINE_SANDBOX); 0 is the harness's ZERO_HEALTH_STOP, not death"),
        ("bloodPoints", "Blood on 1.1's scale, 0 to 1000"),
        ("metres", "metres, the level's frame, +Z up"),
        ("seconds", "seconds of the run (a tick is 1/120 s; a time is rounded to the nearest tick)"),
        ("degrees", "degrees; a facing or bearing counter-clockwise from +X, a pitch up positive"),
        ("metresPerSecond", "metres a second"),
        ("ticks", "simulation ticks, 120 a second"),
        ("share", "a share, 0 to 1"),
        ("chance", "a probability, 0 to 1, drawn from the actor's own random stream"),
        ("weight", "a relative weight, taken in proportion with its siblings"),
        ("count", "a count of events"),
        ("kilograms", "kilograms"),
        ("megabytes", "megabytes of files written"),
    ];

    /// <summary>What a scenario cannot control in this build, said rather than left out (the output contract's coverage list).</summary>
    public static readonly (string What, string Status, string Meaning)[] Coverage =
    [
        ("the bow (gear.ranged, the steps equip, aim, draw, loose, cancelDraw, the arrow events, arrowsAccounted)", ScenarioGame.BowBuilt ? "built (headless v0)" : "NOT_BUILT (a later build)", "a headless war bow driven by a scenario; the player's bow comes later"),
        ("overrides.data (any module data value by asset and JSON pointer)", "built", "replace (the default), remove or add a value of any JSON asset the modules read, before they read it (their own checks run: a refusal is a --check error at the override); recorded in results.json's setup with the value and the asset's hash before and after; sweepable (/overrides/data/<i>/value). Not combat/foes.json (its presets are expanded when the scenario is read: give the template, weapon and sheet instead)"),
        ("overrides.actorTempo, overrides.clipRate (per-actor combat tempo, one clip's play rate)", "built", "an actor's own tempo in place of the global (under Melee's and Celerity's rates); a clip's play rate for one actor (its contact window and busy time follow); both in actors.csv, both sweepable"),
        ("actor.movement (human, neonate, elder movement profiles per actor; template: a 1.1 creature's own walk and run)", "built", "human by default; the szlachta preset moves at its 1.1 template's speeds; the switch foes-uniform makes everyone human"),
        ("actor.control.ai.approach (how a duelling brain comes on from beyond its striking range: duel, or 1.1's manoeuvre oneone)", "built", "duel by default; the szlachta preset comes on as 1.1's szlachta (oneone); within its striking range every approach fights as the duelling brain; the switch foes-uniform makes every approach duel"),
        ("light (a light level over the time)", "built", "a level 0 to 1 over the run, or steps [{ atSeconds, level }], in place of the time's night 0.3 / day 1.0 where no area of the level says its own; sweepable (light)"),
        // ---- the player's bow, the bow's look, the shield raised against a drawn bow
        ("the player's bow (control.playerInput: press swapWeapon, hold attack to draw and let go to loose, press block or cancel to let down, hold aim)", "built", "the arrow leaves toward the point under the crosshair (the view's ray, the camera's offset solved); the bow's swap_seconds 0.6 (ENGINE_SANDBOX); example 05"),
        ("an archer drawn and posed with 1.1's bow (Christof's stand_bow, walk_bow, run_bow, the shot chrisAttackBow played by the draw, hurt_bow, the longBow's model)", "built", "the pose moves the archer's hit volumes; the switch bow-look-off keeps his melee stance and sword"),
        ("a duelling brain's shield raised against a bow drawn and aimed at it", "built", "within 40 m, drawn to 0.25, aimed within 12 degrees of its chest, in 80 degrees of its facing, after its block's reaction (ENGINE_SANDBOX); the switch shield-at-bow-off turns it off; the event ShieldAgainstBow; example 06"),
        // ---- end
        // ---- aim mode as designed, the arrow where the player aims, the aids, the archer brain
        ("the player's bow in aim mode (control.playerInput: hold aim or press aimToggle for the ready pose, hold attack while aiming to draw, let go to loose; attack without aim does nothing with the bow)", "built", "the arrow leaves along the line to the crosshair's point and drops as a real arrow (no spread for the player); the draw's time by Range for every archer (1.3 s at 0 to 0.8 s at 5); switches bow-solve-on (the launch angle solved to meet the point) and bow-aim-off (attack alone draws, the spread); example 10"),
        ("actor.control.ai.kind archer (the archer brain: keeps its range, aims at the chest, draws, holds, looses; its aim's error by its Range drawn as each arrow is nocked and seen in where the bow points (no sway); its draw time by its Range; its sword up close)", "built", "numbers keepMetres, swordMetres, holdSeconds, errorScale over the module's ranged.json archer_brain (ENGINE_SANDBOX); needs gear.ranged; the events ArcherAimed and ArcherToSword; the preset archer gives its bow; example 12"),
        ("the bow's developer aids (the point of impact, the path and a shot's real path)", "built, in play and pictures only", "the actions showImpact and showPath (unbound), --show-impact and --show-path for --headless pictures; presentation only: no scenario field"),
        // ---- end
        // ---- objectives and endings, routing round walls, the paired swap, the player's receipts
        ("objectives (defeat, survive, reach, escape, protect, hold) and stop.onObjectives", "built", "each objective resolved MET, FAILED or UNRESOLVED with its time and reason and the event that decided it (ObjectiveMet / ObjectiveFailed); runs.csv's ending says which stops and objectives ended the run; a harness stop is never a death; objectives measure (an AI does not pursue them: a goTo or flee behaviour is not built)"),
        ("moveTo and path round walls through the navigation mesh, and an honest failure", "built", "the straight line clear: walked straight as before; meeting the level: the mesh's route (MoveToRouted); no route, a body that stops moving, or a time-out: MoveToFailed with the path actually walked (a path: each leg to its next waypoint so). keepDistance, retreat, strafe and circle (relative to an actor, no goal point) still walk straight"),
        ("the duelling brain closing round a wall", "built", "only while its straight line to its foe meets the level; the switch brain-straight closes straight; 1.1's manoeuvre (approach oneone) does not route; backing off and circling do not route"),
        ("the paired swap (two actors' places, or the sides' ground)", "built", "swap: { actors: [a, b] } or { sides: true }; results.json swaps[]; the cast's order (which actor acts first in a tick) is not swapped"),
        ("the player-input actor's receipts in the tick trace (camera, input, aim ray, requested and actual move, what blocked it)", "built", "ticks.jsonl's player on that actor (outputs.ticks); another body in the way is not named (only the level), the mouse pointer is the centre"),
        // ---- end
        ("witnesses and the masquerade in a run", "NOT_BUILT", "the arena's worlds have no townsfolk"),
        // ----
        ("the AI's decisions (BrainDecided: what it chose, saw and why, linked to what it caused; outputs.decisions)", "built", "traced runs by default (\"traced\"; \"all\", \"none\"); chosen against executed (derived executed); never in an untraced run's outputs or a digest; example 13"),
        ("which defence (guardHeld, reactiveBlock, timedParry, dodge, shieldInterception, bladeInterception, clash; outcome landed, stopped, dodged, missed)", "built", "derived on ContactMade, GuardResolved, SwingEnded, FlurryStrike with its evidence; per actor in actors.csv; a dodge is inferred from the motion (no counterfactual sweep); example 13"),
        ("Celerity's rights ledger, denied activations, resting against activated effects", "built", "activations.csv and results.json's receipts: each landed swing's extra blows granted, used, unused and why, their parent contact; denials with the reason; example 14"),
        ("Health's books (purchased and effective maximum, Health, wounds by region, severances, band, Blood, the harness stop)", "built", "actors.csv's last columns and results.json's receipts at the start and the end; on each HarmResolved; ZERO_HEALTH_STOP is never a death; example 15"),
        // ---- end
        ("kind contact (FIXED_CONTACT: a contact placed and resolved without animation)", "built", "\"kind\": \"contact\" and \"contacts\": each a ContactMade (flagged fixed) at a time on a region, with the closing speed, squareness, place on the blade and the blow it stands for; the same harm, soak, wounds, severance and stop as a swept contact; the body, or (meets) the defender's shield or blade for the guard contest (no rebound, stun, overpowering follow-through or broken guard after it); an arrow's fixed contact, a clash and a chain's later contact are not built"),
        ("the ground \"range\" (a long flat lane)", "built", "\"ground\": \"range\": a flat, open lane built in code, x 0 to 170 m, y -6 to 24 m (no walls; not drawn: no picture or film is made on it); the free yard's clear line is 36 m (y = 9)"),
        ("PLAYER_INPUT_REPLAY beyond one actor; mouse pointer and free-look input", "UNSUPPORTED", "one actor may be driven by control.playerInput (move keys, the look turned, buttons); the pointer's position is fixed at the middle"),
        ("outputs.pictures, outputs.film", "UNSUPPORTED by this build's runner", "read and checked; the arena's --picture-at and --film-at draw and film"),
        ("the AI's choices beyond its numbers (tactics, perception, a ranged brain)", "UNSUPPORTED (a code change)", "the brain's 36 numbers, mute and the switches are the controls; a new behaviour is a change in your clone"),
        ("targeted powers (Dominate, Presence), items, firearms", "NOT_BUILT", "not in the platform yet"),
        ("level geometry, hit volumes, the animation clips' motion", "UNSUPPORTED (rebuild)", "a cooked level or the platform's code; read-only to a scenario (a clip's play rate is overrides.clipRate; the anatomy's and the windows' numbers in the module's data are overrides.data)"),
        ("dynamic changes of traits, gear or AI numbers during a run", "UNSUPPORTED", "set at setup (or swept); during a run only steps and triggers act"),
    ];

    /// <summary>The level's named places (its starts), for an objective's <c>place</c>: the loaded level's, else VITOMIR_COURSE_LEVEL's.</summary>
    static JsonArray Places(ScenarioGame game)
    {
        var level = game.Level;
        string? path = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        Space.LevelSpace? opened = null;
        if (level is null && !string.IsNullOrWhiteSpace(path) && File.Exists(Path.Combine(path, "level.json")))
        {
            try { level = opened = Space.LevelSpace.Open(path); } catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException) { }
        }
        if (level is null) return [new JsonObject { ["place"] = null, ["meaning"] = "no level loaded: set VITOMIR_COURSE_LEVEL (the proving ground) or give the ground's level; a level's named places are its starts" }];
        return Arr(level.Starts.Select(s => (JsonNode?)new JsonObject
        {
            ["place"] = s.Name, ["point"] = new JsonArray(Math.Round(s.Position.X, 3), Math.Round(s.Position.Y, 3), Math.Round(s.Position.Z, 3)),
            ["meaning"] = "a start of the level (" + level.Name + "): objectives' reach, hold and escape take it as \"place\"",
        }));
    }

    static string ErrorMeaning(ScenarioErrorCode code)
    {
        var field = typeof(ScenarioErrorCode).GetField(code.ToString());
        // The enum's own doc comments are not at run time: the guide has each with an example.
        return code switch
        {
            ScenarioErrorCode.FileMissing => "the file is missing or unreadable",
            ScenarioErrorCode.NotJson => "not JSON (comments and trailing commas are allowed), or not an object",
            ScenarioErrorCode.UnknownField => "a field the format does not have there; the nearest names are suggested",
            ScenarioErrorCode.WrongType => "a field of the wrong kind (text where a number is wanted, …), or a required field missing",
            ScenarioErrorCode.OutOfRange => "a number outside its range, or a word not among those the field takes",
            ScenarioErrorCode.NoLevel => "no level for the ground (VITOMIR_COURSE_LEVEL or --level)",
            ScenarioErrorCode.LevelNotFound => "the level named is not a level package",
            ScenarioErrorCode.BadName => "an actor's name empty, repeated or holding . , : ; @ ^ + / ~ \"",
            ScenarioErrorCode.BadPlace => "a place that is not [x, y], [x, y, z] or a relative place, or relative places in a circle",
            ScenarioErrorCode.BadBrain => "an ai that is not duel, stand, block or none, numbers a kind does not take, a mute or then without an AI",
            ScenarioErrorCode.BadSheet => "a sheet that is not a template's id or { template | file, values, powers }",
            ScenarioErrorCode.SheetFileMissing => "a sheet file that is not there",
            ScenarioErrorCode.UnknownFoe => "a foe that is no other actor",
            ScenarioErrorCode.BadSweep => "a sweep dimension without values, a value of the wrong kind, a zip of unequal lengths",
            ScenarioErrorCode.BadPower => "a power that is not a rank or { rank, options }, or a nonzero rank with no options",
            ScenarioErrorCode.BadSlots => "slots that are not quick slots 1 to 8, each once",
            ScenarioErrorCode.BadSide => "sides given to some actors only, all on one side, or a foe on its own side",
            ScenarioErrorCode.UnknownFormat => "no \"format\", or not \"vitomir-scenario/1\"",
            ScenarioErrorCode.TooFewActors => "fewer than two actors",
            ScenarioErrorCode.UnknownName => "a name the game does not have (character, preset, sheet, trait, weapon, armour, power, option, switch, event, actor, measure); the nearest are suggested",
            ScenarioErrorCode.BadStep => "a step without exactly one kind, or a value or field its kind does not take",
            ScenarioErrorCode.BadSelector => "an event selector without an event, or filtering on a field the event does not have",
            ScenarioErrorCode.BadObservable => "an observable path that is not one",
            ScenarioErrorCode.BadPath => "a settable path (a sweep's set, a trend's over, an assertion's points) that is not one",
            ScenarioErrorCode.ChannelClash => "two tracks of one actor holding one channel from the same scheduled time",
            ScenarioErrorCode.PlaceBlocked => "a place inside a wall, off the floor, outside the ground, or on another actor",
            ScenarioErrorCode.NotBuilt => "a field this build does not have yet; the message names the build that brings it",
            ScenarioErrorCode.BadTrigger => "a trigger without when or do, or a do that is not { actor, step } or { end }",
            ScenarioErrorCode.BadMeasure => "a measure without a name or a value form, or a repeated name",
            ScenarioErrorCode.BadAssertion => "an assertion without a value form or comparator, or without what its scope needs",
            ScenarioErrorCode.Refused => "the game refused what the scenario asks when the world was built (--check builds each point's first world)",
            ScenarioErrorCode.BadContact => "a fixed contact without atSeconds or region, part and alongBladeShare both, a weapon its attacker does not hold, contacts in a fight, or a native blow (attack or bow step, player's input) in a contact scenario",
            ScenarioErrorCode.BadObjective => "an objective without one owner (actor or side) or one kind (defeat, survive, reach, escape, protect, hold), a target that is its owner or on its side, a place neither a point nor the level's named place, a radius below 0, a time its kind does not take, a reach, escape or hold given to a side, a repeated name; or a swap that cannot be made (an actor not named, sides not two)",
            _ => field?.Name ?? code.ToString(),
        };
    }

    // ---- the catalogue of properties

    static JsonObject Prop(string id, string type, string unit, string range, string control, string status, JsonNode? dflt, string defaultStatus, string source, string meaning) => new()
    {
        ["id"] = id, ["type"] = type, ["unit"] = unit, ["range"] = range, ["control"] = control, ["status"] = status,
        ["default"] = dflt, ["defaultStatus"] = defaultStatus, ["source"] = source, ["meaning"] = meaning,
    };

    /// <summary>
    /// Every property a scenario may set, by a stable namespaced id (<c>actor.sheet.values.melee</c>), with its type, unit, range,
    /// default and the default's status and source, and its control: <c>setup</c> (set when the world is built; sweepable),
    /// <c>dynamic</c> (acts during the run: steps and triggers), <c>rebuild</c> (a change of data or code in your clone) or
    /// <c>notControllable</c>; then the read-only computed values apart.
    /// </summary>
    static JsonObject Properties(ScenarioGame game)
    {
        var setup = new JsonArray
        {
            Prop("scenario.time", "enum", "", "night | day", "setup", "built", "night", "ENGINE_SANDBOX", "src/Vitomir.Platform.Host/Cli.cs", "the light perception sees by (night 0.3, day 1.0)"),
            Prop("scenario.ground", "enum or object", "", "free-yard | range | { level, start }", "setup", "built", "free-yard", "ours", "the format", "where the run happens (range: a flat lane built in code, x 0 to 170 m, y -6 to 24 m)"),
            Prop("scenario.runs", "whole number", "count", "1 to 1000000", "setup", "built", 10, "ours", "the format", "runs per point"),
            Prop("scenario.seed", "whole number", "", "0 or more", "setup", "built", 1, "ours", "the format", "run r of every point has seed seed + r"),
            Prop("scenario.stop.afterSeconds", "number", "seconds", "0.1 to 3600", "setup", "built", new ScenarioStop().AfterSeconds, "ours", "ScenarioStop", "the most a run lasts"),
            // ---- objectives, what they do to the run, the paired swap
            Prop("scenario.stop.onObjectives", "enum", "", "none | first | all", "dynamic", "built", "none", "ours", "the format", "none: objectives only measure; first: the first objective decided (MET or FAILED) ends the run; all: the run ends once every one is decided (a survive, protect or hold for the whole run is decided only by failing, and is met when the others are done)"),
            Prop("scenario.objectives", "list of objectives", "", "{ name, actor | side, one of defeat | survive | reach | escape | protect | hold, bySeconds | untilSeconds }", "dynamic", "built", new JsonArray(), "ours", "src/Vitomir.Platform.Host/Scenario/ScenarioObjectives.cs", "what an actor or a side is out to do; each resolved MET, FAILED or UNRESOLVED with its time and reason (results.json points[].objectives, runs.csv objective_<name>, events ObjectiveMet / ObjectiveFailed); measures only"),
            Prop("scenario.objectives.<i>.defeat", "actor name, { actor } or { side }", "", "a foe (not the owner, not its side)", "dynamic", "built", null, "ours", "ScenarioObjectives.cs", "MET when the actor (every actor of the side) reaches a harness stop (ZERO_HEALTH_STOP, SEVERED_STOP, DISARMED: never a death); FAILED when bySeconds passes or the owner (every actor of the owning side) is stopped"),
            Prop("scenario.objectives.<i>.survive", "true", "", "true", "dynamic", "built", null, "ours", "ScenarioObjectives.cs", "FAILED when the owner (every actor of the side) reaches a harness stop; MET at untilSeconds, or when the run ends"),
            Prop("scenario.objectives.<i>.reach", "{ point | place, radiusMetres }", "metres", $"radiusMetres 0 to 10000 (default {ScenarioReader.DefaultAreaRadiusMetres}); place: --describe places", "dynamic", "built", null, "ENGINE_SANDBOX", "ScenarioObjectives.cs", "MET when the actor's feet are within radiusMetres of the point (level distance; within 1.5 m in height when the point has a z); FAILED at bySeconds or when it is stopped; an actor's only"),
            Prop("scenario.objectives.<i>.escape", "{ fromFoesMetres } or { outOf: { point | place, radiusMetres } }", "metres", "0 to 10000", "dynamic", "built", null, "ours", "ScenarioObjectives.cs", "MET when farther than fromFoesMetres from every foe still standing (with sides: every actor of another side; without: its foe and whoever's foe it is), or out of the area; FAILED at bySeconds or when it is stopped; an actor's only"),
            Prop("scenario.objectives.<i>.protect", "actor name", "", "another actor", "dynamic", "built", null, "ours", "ScenarioObjectives.cs", "FAILED when that actor reaches a harness stop; MET at untilSeconds, or when the run ends"),
            Prop("scenario.objectives.<i>.hold", "{ point | place, radiusMetres }", "metres", $"radiusMetres 0 to 10000 (default {ScenarioReader.DefaultAreaRadiusMetres})", "dynamic", "built", null, "ENGINE_SANDBOX", "ScenarioObjectives.cs", "FAILED when the actor's feet leave the area (or it is stopped), with when; MET at untilSeconds, or when the run ends; an actor's only"),
            Prop("scenario.objectives.<i>.bySeconds", "number", "seconds", "0 to 3600", "dynamic", "built", "none: UNRESOLVED if the run ends first", "ours", "ScenarioObjectives.cs", "defeat, reach, escape: FAILED when this second passes undone"),
            Prop("scenario.objectives.<i>.untilSeconds", "number", "seconds", "0 to 3600", "dynamic", "built", "the run's end", "ours", "ScenarioObjectives.cs", "survive, protect, hold: MET at this second if kept; UNRESOLVED if the run ends before it"),
            Prop("scenario.swap", "{ actors: [a, b] } or { sides: true }", "", "two actors; or exactly two sides", "setup", "built", null, "ours", "src/Vitomir.Platform.Host/Scenario/ScenarioSwap.cs", "each point followed by its twin with two actors' places and facings exchanged, or the two sides' ground turned round (180 degrees about the middle between the sides' centres), on the same seeds; results.json swaps[] sets each seed against its twin"),
            Prop("scenario.overrides.switches.brain-straight", "true/false", "", "true | false", "setup", "built", false, "ours", "src/Vitomir.Platform/Combat/DuelRoutes.cs", "the duelling brain closing straight at its foe whatever stands between them (by default it runs the navigation mesh's route round a wall its straight line meets)"),
            // ---- end
            Prop("scenario.overrides.combatTempo", "number", "share", $"{CombatTempoOverride.Least} to {CombatTempoOverride.Most}", "setup", "built", "the module's (data/combat/pace.json tempo.global)", "SANDBOX", "Data/combat/pace.json", "the global combat tempo (1 is 1.1's own timing)"),
            Prop("scenario.overrides.data", "list", "", "{ asset, pointer, value, op: replace | remove | add }", "setup", "built", new JsonArray(), "", "--describe data", "a module data value by asset (a path under Data/) and RFC 6901 pointer, the same JSON kind as the value it replaces; applied before the modules read it; sweepable as overrides.data.<i>.value"),
            Prop("scenario.overrides.actorTempo", "{ <actor>: number }", "share", $"{CombatTempoOverride.Least} to {CombatTempoOverride.Most}", "setup", "built", new JsonObject(), "ENGINE_SANDBOX", "src/Vitomir.Platform/Combat/CombatTempo.cs (ActorTempo)", "an actor's own combat tempo in place of the global: its swing and recovery under Melee's and Celerity's rates, never the block's rise; sweepable as overrides.actorTempo.<actor>"),
            Prop("scenario.overrides.clipRate", "list of { actor, clip, rate }", "share", $"rate {ScenarioReader.ClipRateLeast} to {ScenarioReader.ClipRateMost}; clip: a clip's name (--check lists an actor's) or quick | strong | block | hurt", "setup", "built", new JsonArray(), "ENGINE_SANDBOX", "src/Vitomir.Platform.Host/Session.Overrides.cs", "one clip's play rate for one actor, multiplied into the clip's own rate (on top of the tempo): its contact window and busy time scale with it; sweepable as overrides.clipRate.<i>.rate"),
            Prop("scenario.light", "number or list of { atSeconds, level }", "share", "0 to 1", "dynamic", "built", "the time's (night 0.3, day 1.0)", "ENGINE_SANDBOX", "Data/perception/sight.json (the time's levels)", "the light perception sees by where no area says its own, over the whole run or in steps; sweepable as light"),
            Prop("actor.preset", "enum", "", string.Join(" | ", game.Presets.Keys.Order(StringComparer.Ordinal)), "setup", "built", null, "SANDBOX", "Data/combat/foes.json", "one of the module's kinds of foe: its template, weapon, shield, sheet, movement and approach"),
            Prop("actor.template", "name", "", "a 1.1 character (--describe characters)", "setup", "built", "Christof", "1.1", "the player's 1.1 install", "the 1.1 character drawn and animated"),
            Prop("actor.sheet.template", "name", "", string.Join(" | ", game.SheetTemplates), "setup", "built", "the module's townsperson", "SANDBOX", "Data/sheets/", "the sheet the actor starts from"),
            Prop("actor.health", "whole number", "healthPoints", "1 to 1000 (and its maximum)", "setup", "built", "full", "ENGINE_SANDBOX", "Data/rules/harm.json", "Health at the start"),
            Prop("actor.blood", "whole number", "bloodPoints", "0 to its pool", "setup", "built", "full", "SANDBOX", "Data/rules/blood.json", "Blood at the start"),
            Prop("actor.movement", "enum", "", "human | neonate | elder | template", "setup", "built", "its preset's, else human", "SANDBOX (template: 1.1's, read from the install)", "Data/movement/; template: the 1.1 install's ACTOR_WALKSPEED and ACTOR_RUNSPEED", "how it moves: a movement profile (walk, run, sprint m/s and the rest), or template: its 1.1 template's own walk and run (its sprint its run; the szlachta 1.11 and 1.67 m/s), everything else the human's"),
            Prop("actor.gear.weapon", "name", "", "a weapon (--describe weapons)", "setup", "built", "Christof: BroadSword; else its own", "1.1", "the 1.1 install", "the weapon held"),
            Prop("actor.gear.shield", "true/false", "", "true | false", "setup", "built", "as its weapon holds", "1.1", "the 1.1 install", "checked: in 1.1 a shield is part of the weapon"),
            Prop("actor.gear.armor", "list", "", string.Join(" | ", game.Armor), "setup", "built", new JsonArray(), "SANDBOX", "Data/combat/armor.json", "the armour worn (exactly this; none when not given)"),
            Prop("actor.gear.ranged", "object", "", "{ weapon, arrows }", "setup", ScenarioGame.BowBuilt ? "built" : "NOT_BUILT (the bow)", null, "SANDBOX", "Data/combat/ranged.json", "a bow and arrows"),
            Prop("actor.place", "[x, y] | [x, y, z] | { from, distanceMetres, bearingDegrees }", "metres / degrees", "on the ground's floor, not in a wall", "setup", "built", "round the free yard's middle (40, 3), 2 m out", "ours", "the arena", "where it stands"),
            Prop("actor.facing", "degrees | { toward }", "degrees", "", "setup", "built", "toward its foe", "ours", "the arena", "where it faces"),
            Prop("actor.foe", "name", "", "another actor", "setup", "built", "the next actor", "ours", "the arena", "whom its AI fights; its steps' default target"),
            Prop("actor.side", "text", "", "", "setup", "built", null, "ours", "the arena", "one against several"),
            Prop("actor.slots", "list", "", "quick slots 1 to 8", "setup", "built", "all", "ours", "the arena", "the quick slots its AI may press"),
            Prop("actor.control.ai.kind", "enum", "", "duel | stand | block | none | archer", "setup", "built", "stand (none with a script)", "ours", "the arena", "its AI"),
            // ---- the archer brain's numbers (ENGINE_SANDBOX; the module's ranged.json archer_brain by default)
            Prop("actor.control.ai.keepMetres", "number", "m", "2 to 150", "setup", "built", 20, "ENGINE_SANDBOX", "Data/combat/ranged.json archer_brain.keep_metres", "archer: it comes on until its foe is this near, then stands and shoots"),
            Prop("actor.control.ai.swordMetres", "number", "m", "0 to 20", "setup", "built", 4, "ENGINE_SANDBOX", "Data/combat/ranged.json archer_brain.sword_metres", "archer: its foe this near (or its quiver empty), it draws its melee weapon (the duelling brain)"),
            Prop("actor.control.ai.holdSeconds", "number", "s", "0 to 10", "setup", "built", 0.5, "ENGINE_SANDBOX", "Data/combat/ranged.json archer_brain.hold_seconds", "archer: a full draw held this long on its aim before the loose"),
            Prop("actor.control.ai.errorScale", "number", "", "0 to 10", "setup", "built", 1, "ENGINE_SANDBOX", "Data/combat/ranged.json archer_brain.error_scale", "archer: its aim's error is the bow's spread by its Range (firearms: 2.0 degrees at 0 to 0.35 at 5, one standard deviation each way) times this, drawn once a shot as the arrow is nocked"),
            // ---- end
            Prop("actor.control.ai.approach", "enum", "", "duel | oneone", "setup", "built", "its preset's, else duel", "ours (oneone: 1.1's, its AI file read from the install)", "src/Vitomir.Platform/Combat/DuelApproach.cs", "how a duelling brain comes on from beyond its striking range: duel, straight at its foe without a pause; oneone, 1.1's manoeuvre (a step within 30 degrees of the line to its foe, up to its AI file's chaseDist, at its run, every moveRate seconds; it stands when a step ends early); within its striking range both fight as the duelling brain"),
            Prop("actor.control.mute", "list", "", string.Join(" | ", ScenarioWords.Channels), "setup", "built", new JsonArray(), "ours", "the format", "AI channels cleared each tick"),
            Prop("actor.control.then", "enum", "", "ai | stand | end", "setup", "built", "ai with an AI, else stand", "ours", "the format", "what follows its script"),
        };
        // ---- a fixed contact's fields (kind contact; sweepable as contacts.<index>.<field> or contacts.all.<field>)
        setup.Add(Prop("scenario.kind", "enum", "", "fight | contact", "setup", "built", "fight", "ours", "the format", "fight: the actors' own blows (native); contact: fixed contacts placed and resolved without a swing (FIXED_CONTACT)"));
        setup.Add(Prop("scenario.contacts.<index>.atSeconds", "number", "seconds", "0 to 3600 (rounded to the tick)", "setup", "built", null, "", "the format", "when the contact is placed (required)"));
        setup.Add(Prop("scenario.contacts.<index>.attacker", "name", "", "an actor", "setup", "built", "the first actor", "ours", "the format", "whose blow it is (its weapon, Strength, Potence, Melee, body)"));
        setup.Add(Prop("scenario.contacts.<index>.defender", "name", "", "another actor", "setup", "built", "the attacker's foe", "ours", "the format", "whose body is met"));
        setup.Add(Prop("scenario.contacts.<index>.weapon", "name", "", "the attacker's held weapon", "setup", "built", "its attacker's", "1.1", "the 1.1 install", "checked only: a fixed contact's weapon is the one its attacker holds (set actor.gear.weapon)"));
        setup.Add(Prop("scenario.contacts.<index>.meets", "enum", "", string.Join(" | ", ScenarioReader.ContactMeets), "setup", "built", "body", "ours", "the format", "what the blow meets: the defender's body (a region), its held shield, its blade held in guard, or its blade in its parry window (the guard contest judges those: GuardResolved; no rebound, stun, overpowering follow-through or broken guard follows a fixed one)"));
        setup.Add(Prop("scenario.contacts.<index>.region", "enum", "", string.Join(" | ", ScenarioWords.AimRegions), "setup", "built", null, "", "the format", "the region met (required when it meets the body); without a side, the side nearer the attacker"));
        setup.Add(Prop("scenario.contacts.<index>.part", "enum", "", "edge | point | haft", "setup", "built", "edge", "ENGINE_SANDBOX", "src/Vitomir.Platform.Host/Scenario/ScenarioReader.Contact.cs", "a named place on the blade: edge 0.4 of the way from the guard, point 1, haft 0.05 (inside the rules' hilt: bashing)"));
        setup.Add(Prop("scenario.contacts.<index>.alongBladeShare", "number", "share", "0 to 1", "setup", "built", ScenarioReader.EdgeShare, "ENGINE_SANDBOX", "the median of swept contacts (the fixed-contact harness)", "where on the blade it meets: 0 the guard, 1 the tip (ContactMade.AlongBlade); instead of part"));
        setup.Add(Prop("scenario.contacts.<index>.closingSpeedMetresPerSecond", "number", "metresPerSecond", "0 to 200", "setup", "built", ScenarioReader.TypicalClosing, "ENGINE_SANDBOX", "the median of swept contacts (the fixed-contact harness)", "the striking point's speed against the defender (ContactMade.PointSpeed)"));
        setup.Add(Prop("scenario.contacts.<index>.squarenessDegrees", "number", "degrees", "0 to 90", "setup", "built", ScenarioReader.TypicalSquareness, "ENGINE_SANDBOX", "the median of swept contacts (the fixed-contact harness)", "the angle between the point's motion and the struck surface: 90 square on, 0 sliding along it; the speed into it (ContactMade.ImpactSpeed) is closing x sin(squareness)"));
        setup.Add(Prop("scenario.contacts.<index>.swing", "enum", "", string.Join(" | ", ScenarioReader.ContactSwings), "setup", "built", "right", "ours", "Data/combat/directions.json", "the attacker's own blow it stands for (quick by direction, or strong): its clip gives the blow quality's reference and the peak speed"));
        setup.Add(Prop("scenario.contacts.<index>.motionRate", "number", "share", "0.05 to 10", "setup", "built", "its own", "ours", "the attacker's CombatTempo", "the rate the blow stands at (the peak tip speed scales with it); its own: the attacker's swing rate at the contact (combat tempo, skill, Celerity)"));
        setup.Add(Prop("scenario.contacts.<index>.peakSpeedMetresPerSecond", "number", "metresPerSecond", "0.01 to 500", "setup", "built", "its blow's", "ours", "the clip's active window (ContactWindows)", "the blow's fastest tip speed (ContactMade.PeakSpeed: blow quality's q is the speed into the surface over it); its blow's: the clip's measured peak x the rate"));
        // ---- end
        foreach (var t in game.Traits)
            setup.Add(Prop($"actor.sheet.values.{t.Id}", "whole number", t.Unit, $"{t.Least} to {t.Most}", "setup", "built", "the sheet template's", t.Status, t.Source, $"{t.Domain} {t.Id} ({t.Category}): purchased value, set over the template; an explicit setup condition recorded in provenance"));
        foreach (var p in game.Powers.Values)
            setup.Add(Prop($"actor.sheet.powers.{p.Id}", "{ rank, options }", "powerRank", $"0 to 10; options: {string.Join(", ", p.Options)}", "setup", "built", 0, p.Status, "Data/rules/powers.json", $"{p.Label} ({p.Kind}): learned at a rank with named options; an explicit setup condition recorded in provenance"));
        foreach (var n in BrainNumbers.All)
            setup.Add(Prop($"actor.control.ai.{n.Name}", n.Kind.ToString().ToLowerInvariant(), n.Unit, n.Kind == BrainNumberKind.Pace ? "\"none\" | [least, most] s" : $"{n.Least} to {n.Most}", "setup", "built",
                n.Kind == BrainNumberKind.Pace ? "its kind's" : n.Kind == BrainNumberKind.Flag ? n.Get(new DuelBrainSettings()) != 0f : Math.Round(n.Get(new DuelBrainSettings()), 6), "ENGINE_SANDBOX", "src/Vitomir.Platform/Combat/DuelBrain.cs", n.Meaning));
        foreach (var (name, (help, module)) in game.Switches.OrderBy(s => s.Key, StringComparer.Ordinal))
            setup.Add(Prop($"scenario.overrides.switches.{name}", "true/false", "", "true | false (a policy variant)", name == "sandbox" ? "notControllable" : "setup", "built", false, "", $"{module}'s module.json", name == "sandbox" ? "the command line's --sandbox, not the scenario's" : help));
        var dynamic = new JsonArray();
        foreach (var s in ScenarioWords.Steps)
            dynamic.Add(Prop($"step.{s.Name}", s.Takes, "", "", "dynamic", s.Status == "bow" ? (ScenarioGame.BowBuilt ? "built" : "NOT_BUILT (the bow)") : "built", null, "", "ScenarioDirector", $"{s.Meaning}; holds {(s.Channels.Count == 0 ? "no channel" : string.Join(", ", s.Channels))}; ends {s.Ends}"));
        dynamic.Add(Prop("actor.control.playerInput", "a list of segments", "seconds / degrees", "move -1 to 1; buttons: --describe steps", "dynamic", "built", null, "", "PlayerInput", "the player's own controls replayed for one actor (PLAYER_INPUT_REPLAY): the camera, the crosshair and the automatic directions as in play"));
        dynamic.Add(Prop("trigger", "{ when, do, repeat, cooldownSeconds }", "", "", "dynamic", "built", null, "", "ScenarioDirector", "when an event or a value holds, start a step on an actor or end the run (one tick after the event)"));
        return new JsonObject
        {
            ["note"] = "control: setup = set when the world is built (and sweepable); dynamic = acts during the run; rebuild = a change of data or code in your clone; notControllable",
            ["setup"] = setup,
            ["dynamic"] = dynamic,
            ["readOnly"] = Arr(ScenarioWords.Observables.Select(o => (JsonNode?)new JsonObject { ["id"] = $"observable.{o.Path}", ["unit"] = o.Unit, ["meaning"] = o.Meaning })),
        };
    }

    // ---- words

    /// <summary>The document in words, list by list (without --json).</summary>
    static IEnumerable<string> Words(JsonObject doc)
    {
        yield return $"describe: {doc["build"]} (commit {doc["git"]?["commit"]?.ToString() ?? "unknown"}{(doc["git"]?["dirty"]?.GetValue<bool>() == true ? ", dirty" : "")}{(doc["git"]?["from"] is { } from ? $", from the {from}" : "")}{(doc["git"]?["cloneCommit"] is { } clone ? $"; the clone is now at {clone}" : "")}); format {doc["format"]}; the bow {doc["bow"]}; modules {string.Join(", ", doc["modules"]!.AsArray().Select(m => $"{m!["module"]} {m["configHash"]} ({m["profile"]})"))}";
        foreach (var (key, value) in doc)
        {
            if (key is "schema" or "format" or "build" or "bow" or "modules" or "git") continue;
            yield return "";
            yield return $"== {key}";
            if (key == "data" && value is JsonArray assets) { foreach (var line in DataWords(assets)) yield return line; continue; }   // a value a line
            if (value is JsonArray items) foreach (var item in items) yield return "  " + Line(item);
            else if (value is JsonObject o)
                foreach (var (k, v) in o)
                {
                    yield return $"  -- {k}";
                    if (v is JsonArray inner) foreach (var item in inner) yield return "    " + Line(item);
                    else yield return "    " + v?.ToJsonString();
                }
        }
    }

    static string Line(JsonNode? item) => item is JsonObject o
        ? string.Join("; ", o.Where(kv => kv.Value is not null).Select(kv => $"{kv.Key} {(kv.Value is JsonValue v && v.TryGetValue(out string? s) ? s : kv.Value!.ToJsonString(ScenarioSpec.Writing).Replace("\n", " ").Replace("  ", ""))}"))
        : item?.ToJsonString() ?? "";
}
