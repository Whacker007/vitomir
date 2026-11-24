// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Host.Arena;

namespace Vitomir.Platform.Host.Scenario;

/// <summary>
/// The scenario's compiler: from the resolved form to what the runner runs — each point of the sweep (grid or
/// zip) with its values set, its actors as the arena's cast (places resolved, checked against the ground), the director's
/// program (the director work's model), each actor's execution mode and setup conditions; and the measures, assertions, stop and
/// outputs as the results work's model. Errors it finds (a place inside a wall at one point) go to the reader's list.
/// </summary>
internal sealed partial class ScenarioCompiler(ScenarioReader reader, JsonObject resolved, ScenarioGame? game)   // partial (ScenarioCompiler.Overrides.cs)
{
    static string Inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
    static string S(JsonNode? n) => n!.GetValue<string>();
    static double D(JsonNode? n) => n!.GetValue<double>();
    static bool IsText(JsonNode? n, string text) => n is JsonValue v && v.TryGetValue(out string? s) && s == text;
    static bool IsText(JsonNode? n) => n is JsonValue v && v.TryGetValue(out string? _);

    // ---------------------------------------------------------------- points

    /// <summary>Every point of the sweep, in order: a grid (the first dimension outermost, as the arena's) or a zip.</summary>
    public IReadOnlyList<SweepPoint> Points()
    {
        var dims = resolved["sweep"]!.AsArray();
        var combos = new List<int[]>();
        if (dims.Count == 0) combos.Add([]);
        else if (S(resolved["sweepMode"]) == "zip")
        {
            int n = dims.Min(d => d!["values"]!.AsArray().Count);
            for (int i = 0; i < n; i++) combos.Add([.. Enumerable.Repeat(i, dims.Count)]);
        }
        else
        {
            combos.Add([]);
            foreach (var d in dims)
            {
                int count = d!["values"]!.AsArray().Count;
                combos = [.. combos.SelectMany(c => Enumerable.Range(0, count).Select(i => (int[])[.. c, i]))];
            }
        }
        var points = new List<SweepPoint>(combos.Count);
        for (int p = 0; p < combos.Count; p++) points.Add(Point(p, combos[p]));
        return points;
    }

    SweepPoint Point(int index, int[] combo)
    {
        var doc = resolved.DeepClone().AsObject();
        doc.Remove("sweep");
        var dims = resolved["sweep"]!.AsArray();
        var values = new List<(string, string)>();
        var swept = new List<(int Actor, ScenarioReader.Settable Path, JsonNode? Value)>();
        for (int d = 0; d < combo.Length; d++)
        {
            var path = reader.SweepPaths[d];
            var value = dims[d]!["values"]![combo[d]]!.DeepClone();
            values.Add((path.Path, ScenarioSpec.ValueText(value)));
            Set(doc, path, value);
            swept.Add((path.Actor, path, value));
        }
        string name = string.Join(", ", values.Select(v => $"{v.Item1} = {v.Item2.Trim('"')}"));
        var actors = doc["actors"]!.AsArray();
        var places = Places(actors, index, name);
        var fighters = new List<CastFighter>(actors.Count);
        var setup = new JsonArray();
        for (int i = 0; i < actors.Count; i++) fighters.Add(Fighter(actors[i]!.AsObject(), i, places, swept, setup));
        var program = Program(doc);
        var modes = actors.Select((a, i) => Mode(a!.AsObject(), i, program)).ToList();
        // An attacker of fixed contacts is labelled by them (its blows are placed, not swung)
        if (program?.Contacts is { } fixedContacts)
            for (int i = 0; i < actors.Count; i++)
                if (fixedContacts.Exists(c => c.Attacker == S(actors[i]!["name"]))) modes[i] = ExecutionModes.FixedContact;
        for (int i = 0; i < actors.Count; i++) actors[i]!["executionMode"] = modes[i];
        string mode = modes.Distinct().Count() == 1 ? modes[0] : ExecutionModes.Hybrid;
        if (S(doc["kind"]) == "contact") mode = ExecutionModes.FixedContact;   // its contacts are fixed, whoever stands
        doc["executionMode"] = mode;
        var tempo = doc["overrides"]!["combatTempo"];
        var (dataNote, light) = Overridden(doc, fighters, setup);   // the data overrides, the actors' tempos and clip rates, the light
        return new SweepPoint
        {
            DataOverrides = dataNote,
            Light = light,
            Index = index,
            Name = name,
            Values = values,
            Cast = new ArenaCast { Fighters = fighters },
            Program = program,
            Switches = [.. doc["overrides"]!["switches"]!.AsArray().Select(s => S(s))],
            CombatTempo = tempo is JsonValue tv && tv.TryGetValue(out double t) ? (float)t : null,
            Night = S(doc["time"]) == "night",
            ActorModes = modes,
            ExecutionMode = mode,
            Setup = setup,
            Resolved = doc,
        };
    }

    /// <summary>A swept value set in a point's copy of the resolved scenario.</summary>
    static void Set(JsonObject doc, ScenarioReader.Settable path, JsonNode? value)
    {
        var actor = path.Actor >= 0 ? doc["actors"]![path.Actor]!.AsObject() : null;
        switch (path.Kind)
        {
            case "time": doc["time"] = value; break;
            case "light" or "data" or "actorTempo" or "clipRate": SetOverride(doc, path, value); break;
            case "combatTempo": doc["overrides"]!["combatTempo"] = value; break;
            case "switch":
                var list = doc["overrides"]!["switches"]!.AsArray();
                var had = list.FirstOrDefault(s => S(s) == path.Key);
                if (value!.GetValue<bool>()) { if (had is null) list.Add(path.Key); }
                else if (had is not null) list.Remove(had);
                break;
            case "contact": SetContact(doc, path.Key!, value); break;
            case "health": actor!["health"] = value; break;
            case "blood": actor!["blood"] = value; break;
            case "gear.weapon": actor!["gear"]!["weapon"] = value; break;
            case "gear.armor": actor!["gear"]!["armor"] = value; break;
            case "place.distanceMetres": actor!["place"]!["distanceMetres"] = value; break;
            case "place.bearingDegrees": actor!["place"]!["bearingDegrees"] = value; break;
            case "ai": actor!["control"]!["ai"]![path.Key!] = value; break;
            case "sheet.values": actor!["sheet"]!["values"]![path.Key!] = value; break;
            case "sheet.powers":
                var powers = actor!["sheet"]!["powers"]!.AsObject();
                if (powers[path.Key!] is JsonObject p) p["rank"] = value;
                else powers[path.Key!] = new JsonObject { ["rank"] = value, ["options"] = "the sheet's" };
                break;
        }
    }

    // ---------------------------------------------------------------- places

    /// <summary>
    /// Each actor's feet and facing at a point: an absolute place as given (z 0.02, the arena's, when not given); a relative one
    /// from its actor's (resolved first; the distance along the bearing, to a ten-thousandth of a metre); none, round the free
    /// yard's middle as the arena's fighters (or round the level's start); each checked against the ground.
    /// </summary>
    List<(Vector3 Feet, float Facing)> Places(JsonArray actors, int point, string pointName)
    {
        int n = actors.Count;
        var feet = new Vector3?[n];
        bool range = IsText(resolved["ground"], ScenarioGround.Range);   // the range's own bounds
        bool freeYard = resolved["ground"] is not JsonObject && !range;
        Vector3 middle = new(ArenaSpec.Yard.X, ArenaSpec.Yard.Y, ArenaSpec.Yard.Z);
        if (!freeYard && !range && game?.Start(resolved["ground"]!["start"]?.GetValue<string>()) is { } start) middle = start;
        Vector3 Resolve(int i, int depth)
        {
            if (feet[i] is { } done) return done;
            var place = actors[i]!["place"];
            Vector3 at;
            if (place is JsonArray xs)
                at = new Vector3((float)D(xs[0]), (float)D(xs[1]), xs.Count > 2 ? (float)D(xs[2]) : freeYard || range ? ArenaSpec.Yard.Z : game?.FloorAt((float)D(xs[0]), (float)D(xs[1]), middle.Z) ?? ArenaSpec.Yard.Z);
            else if (place is JsonObject rel && depth < n && rel["from"] is { } fromName && reader.Actors.ToList().IndexOf(S(fromName)) is var from and >= 0)
            {
                var o = Resolve(from, depth + 1);
                double d = D(rel["distanceMetres"]), b = D(rel["bearingDegrees"]) * Math.PI / 180.0;
                at = new Vector3((float)Math.Round(o.X + d * Math.Cos(b), 4), (float)Math.Round(o.Y + d * Math.Sin(b), 4), o.Z);
            }
            else
            {
                // The arena's ring (ArenaSpec.Fighter): 2 m out round the middle, in the actors' order.
                float angle = MathF.PI * (1f + 2f * i / n);
                at = new Vector3(middle.X + ArenaSpec.Yard.Radius * Round3((float)Math.Cos(angle)), middle.Y + ArenaSpec.Yard.Radius * Round3((float)Math.Sin(angle)), middle.Z);
            }
            feet[i] = at;
            return at;
        }
        var result = new List<(Vector3, float)>(n);
        for (int i = 0; i < n; i++) Resolve(i, 0);
        for (int i = 0; i < n; i++)
        {
            var me = feet[i]!.Value;
            var facing = actors[i]!["facing"];
            float deg;
            if (facing is JsonValue fv && fv.TryGetValue(out double given)) deg = (float)given;
            else
            {
                Vector2 toward;
                var t = facing?["toward"];
                if (t is JsonArray pt) toward = new Vector2((float)D(pt[0]), (float)D(pt[1]));
                else
                {
                    string other = t is not null && !IsText(t, "its foe") ? S(t) : S(actors[i]!["foe"]);
                    int o = reader.Actors.ToList().IndexOf(other);
                    toward = o >= 0 ? new Vector2(feet[o]!.Value.X, feet[o]!.Value.Y) : new Vector2(middle.X, middle.Y);
                }
                deg = Round3((float)(Math.Atan2(toward.Y - me.Y, toward.X - me.X) * 180.0 / Math.PI));
            }
            result.Add((me, deg));
            actors[i]!["placeResolved"] = new JsonArray(Math.Round(me.X, 4), Math.Round(me.Y, 4), Math.Round(me.Z, 4));
            actors[i]!["facingResolvedDegrees"] = Math.Round(deg, 3);
            if ((range ? ScenarioGame.RangeProblem(me) : game?.PlaceProblem(me, freeYard)) is { } problem)
                reader.Error(ScenarioErrorCode.PlaceBlocked, $"/actors/{i}/place", pointName.Length == 0 ? problem : $"{problem}, at point {point} ({pointName})");
        }
        // Two actors on one spot cannot both stand there.
        for (int a = 0; a < n; a++)
            for (int b = a + 1; b < n; b++)
                if (Vector2.Distance(new(feet[a]!.Value.X, feet[a]!.Value.Y), new(feet[b]!.Value.X, feet[b]!.Value.Y)) < 0.6f)
                    reader.Error(ScenarioErrorCode.PlaceBlocked, $"/actors/{b}/place", Inv($"{reader.Actors[b]} stands within 0.6 m of {reader.Actors[a]} (two bodies 0.3 m round cannot){(pointName.Length == 0 ? "" : $", at point {point} ({pointName})")}"));
        return result;
    }

    static float Round3(float v) => MathF.Round(v * 1000f) / 1000f;

    // ---------------------------------------------------------------- the cast

    CastFighter Fighter(JsonObject a, int i, List<(Vector3 Feet, float Facing)> places, List<(int Actor, ScenarioReader.Settable Path, JsonNode? Value)> swept, JsonArray setup)
    {
        string name = S(a["name"]);
        var gear = a["gear"]!.AsObject();
        var sheet = a["sheet"]!.AsObject();
        var ai = a["control"]!["ai"]!.AsObject();
        void Note(string kind, string id, JsonNode? value, string source) =>
            setup.Add(new JsonObject { ["actor"] = name, ["kind"] = kind, ["id"] = id, ["value"] = value?.DeepClone(), ["source"] = source });
        string presetSource = a["preset"] is null ? "scenario" : "preset";

        // The sheet's values in the arena's order: the traits, the powers, the swept ones last.
        var mine = swept.Where(s => s.Actor == i).ToList();
        var values = new List<CastValue>();
        if (sheet["template"] is { } t) Note("sheetTemplate", S(t), t, presetSource);
        foreach (var (id, v) in sheet["values"]!.AsObject())
            if (!mine.Any(s => s.Path.Kind == "sheet.values" && s.Path.Key == id)) { values.Add(new CastValue(id, (int)v!.GetValue<long>())); Note("trait", id, v, "scenario"); }
        foreach (var (id, p) in sheet["powers"]!.AsObject())
            if (!mine.Any(s => s.Path.Kind == "sheet.powers" && s.Path.Key == id)) { values.Add(Power(id, p!.AsObject())); Note("power", id, p, "scenario"); }
        foreach (var s in mine)
        {
            if (s.Path.Kind == "sheet.values") { values.Add(new CastValue(s.Path.Key!, (int)s.Value!.GetValue<long>())); Note("trait", s.Path.Key!, s.Value, "sweep"); }
            else if (s.Path.Kind == "sheet.powers") { values.Add(Power(s.Path.Key!, sheet["powers"]![s.Path.Key!]!.AsObject())); Note("power", s.Path.Key!, sheet["powers"]![s.Path.Key!], "sweep"); }
        }
        string Source(string kind) => mine.Any(s => s.Path.Kind == kind) ? "sweep" : "scenario";
        int? health = a["health"] is JsonValue hv && hv.TryGetValue(out long h) ? (int)h : null;
        int? blood = a["blood"] is JsonValue bv && bv.TryGetValue(out long bl) ? (int)bl : null;
        if (health is not null) Note("health", "health", a["health"], Source("health"));
        if (blood is not null) Note("blood", "blood", a["blood"], Source("blood"));
        string? weapon = IsText(gear["weapon"], "its own") ? null : S(gear["weapon"]);
        bool? shield = gear["shield"] is JsonValue sv && sv.TryGetValue(out bool sb) ? sb : null;
        var armor = gear["armor"]!.AsArray().Select(x => S(x)).ToList();
        Note("weapon", weapon ?? "its own", gear["weapon"], Source("gear.weapon"));
        if (armor.Count > 0) Note("armor", string.Join(",", armor), gear["armor"], Source("gear.armor"));

        var brain = S(ai["kind"]) switch { "duel" => CastBrain.Duel, "block" => CastBrain.Block, "archer" => CastBrain.Archer, _ => CastBrain.Stand };   // archer
        DuelBrainSettings? numbers = null;
        if (brain == CastBrain.Duel)
        {
            var d = new DuelBrainSettings();
            var n = d;
            foreach (var bn in BrainNumbers.All)
            {
                var x = ai[bn.Name];
                switch (bn.Kind)
                {
                    case BrainNumberKind.Number: n = bn.Set(n, (float)x!.GetValue<double>()); break;
                    case BrainNumberKind.Count: n = bn.Set(n, x!.GetValue<long>()); break;
                    case BrainNumberKind.Flag: n = bn.Set(n, x!.GetValue<bool>() ? 1f : 0f); break;
                    case BrainNumberKind.Pace:
                        if (IsText(x, "none")) n = n with { PaceLeastSeconds = 0, PaceMostSeconds = 0 };
                        else if (x is JsonArray pace) n = n with { PaceLeastSeconds = (float)D(pace[0]), PaceMostSeconds = (float)D(pace[1]) };
                        break;
                }
            }
            // 1.1's approach (oneone); duel is the default and leaves the numbers as they were.
            if (ai[ArenaSpec.ApproachField] is { } approach && S(approach) == "oneone")
            {
                n = n with { Approach = DuelApproach.OneOne };
                Note("approach", "oneone", approach, reader.FromPreset.Contains(name + ".approach") ? "preset" : "scenario");
            }
            numbers = n == d ? null : n;
        }
        // Its movement (human, the default, leaves the cast as it was).
        string? movement = a["movement"] is { } mv && S(mv) != Session.HumanMovement ? S(mv) : null;
        if (movement is not null) Note("movement", movement, a["movement"], reader.FromPreset.Contains(name + ".movement") ? "preset" : "scenario");
        var foe = reader.Actors.ToList().IndexOf(S(a["foe"]));
        var place = places[i];
        return new CastFighter
        {
            Name = name,
            Template = S(a["template"]),
            Weapon = weapon,
            X = place.Feet.X, Y = place.Feet.Y, Z = place.Feet.Z, Facing = place.Facing,
            Brain = brain,
            Numbers = numbers,
            Script = null,
            Foe = Math.Max(0, foe),
            Sheet = new CastSheet
            {
                Template = sheet["template"]?.GetValue<string>(),
                Text = sheet["file"] is { } f ? System.IO.File.ReadAllText(S(f)) : null,
                Values = values,
            },
            Blood = blood,
            Slots = a["slots"] is JsonArray sl ? [.. sl.Select(x => (int)x!.GetValue<long>())] : null,
            Shield = shield,
            Armor = armor.Count == 0 ? null : armor,
            Side = a["side"]?.GetValue<string>(),
            Health = health,   // Health at the start
            Ranged = gear["ranged"] is JsonObject rg ? S(rg["weapon"]) : null,                          // the bow
            Arrows = gear["ranged"] is JsonObject ra ? (int)Math.Round(D(ra["arrows"])) : null,
            Archer = brain == CastBrain.Archer ? ArcherOf(ai) : null,   // the archer brain's numbers given
            Movement = movement,
        };
    }

    /// <summary>The archer brain's numbers a scenario gave (the others the module's: null).</summary>
    static CastArcher ArcherOf(JsonObject ai)
    {
        float? F(string field) => ai[field] is JsonValue v && v.TryGetValue(out double x) ? (float)x : null;
        return new CastArcher(F("keepMetres"), F("swordMetres"), F("holdSeconds"), F("errorScale"));
    }

    static CastValue Power(string id, JsonObject p) =>
        new(id, (int)p["rank"]!.GetValue<long>(), true, p["options"] is JsonArray os ? [.. os.Select(o => S(o))] : null);

    // ---------------------------------------------------------------- execution modes

    /// <summary>
    /// An actor's execution mode: its AI alone (nothing scripted, muted or triggered for it) is <see cref="ExecutionModes.NativeAi"/>;
    /// a script (or a trigger's step) and no AI is <see cref="ExecutionModes.ControlledScript"/>; both, or an AI muted, is
    /// <see cref="ExecutionModes.Hybrid"/>.
    /// </summary>
    static string Mode(JsonObject a, int index, ScenarioProgram? program)
    {
        var c = a["control"]!.AsObject();
        if (c["playerInput"] is JsonArray) return ExecutionModes.PlayerInputReplay;
        string kind = S(c["ai"]!["kind"]);
        string name = S(a["name"]);
        bool scripted = c["tracks"]!.AsArray().Count > 0 || program?.Triggers.Any(t => t.Do.Any(d => d.Actor == name && d.Step is not null)) == true;
        bool muted = c["mute"]!.AsArray().Count > 0;
        // A standing dummy contests no channel: with steps it is the script's (as the director work's ScenarioProgram.AuthorityOf says), alone the engine's own dummy.
        bool ai = kind is "duel" or "block" or "archer";   // archer
        if (scripted) return ai ? ExecutionModes.Hybrid : ExecutionModes.ControlledScript;
        if (kind == "none") return ExecutionModes.ControlledScript;
        return ai && muted ? ExecutionModes.Hybrid : ExecutionModes.NativeAi;
    }

    // ---------------------------------------------------------------- the program

    /// <summary>The director's program (the director work's model), or null when nothing is scripted, muted or triggered.</summary>
    ScenarioProgram? Program(JsonObject doc)
    {
        var actors = doc["actors"]!.AsArray();
        var triggers = doc["triggers"]!.AsArray();
        bool any = triggers.Count > 0 || actors.Any(a => a!["control"]!["tracks"]?.AsArray().Count > 0 || a["control"]!["mute"]?.AsArray().Count > 0 || S(a["control"]!["ai"]!["kind"]) == "none");
        any |= doc["contacts"] is JsonArray { Count: > 0 };   // fixed contacts need the program
        if (!any) return null;
        var input = actors.FirstOrDefault(a => a!["control"]!["playerInput"] is JsonArray);
        var program = new ScenarioProgram
        {
            Input = input is null ? null : new PlayerInput { Actor = S(input["name"]), Segments = [.. input["control"]!["playerInput"]!.AsArray().Select(x => Segment(x!.AsObject()))] },
            Contacts = doc["contacts"] is JsonArray { Count: > 0 } fixedList ? [.. fixedList.Select((c, i) => FixedContact(c!.AsObject(), i))] : null,
        };
        foreach (var a in actors)
        {
            var c = a!["control"]!.AsObject();
            if (c["playerInput"] is JsonArray) continue;   // the player's body: no control block (the director refuses one)
            var mute = ControlChannels.None;
            foreach (var m in c["mute"]!.AsArray()) mute |= Channel(S(m));
            program.Actors.Add(new ActorControl
            {
                Actor = S(a["name"]),
                Ai = S(c["ai"]!["kind"]) switch { "duel" => ScenarioAi.Duel, "stand" => ScenarioAi.Stand, "block" => ScenarioAi.Block, "archer" => ScenarioAi.Archer, _ => ScenarioAi.None },   // archer
                Mute = mute,
                Tracks = [.. c["tracks"]!.AsArray().Select(t => t!.AsArray().Select(s => Step(s!.AsObject())).ToList())],
                Then = S(c["then"]) switch { "ai" => ScenarioThen.Ai, "end" => ScenarioThen.End, _ => ScenarioThen.Stand },
                Foe = a["foe"]?.GetValue<string>(),
            });
        }
        foreach (var t in triggers)
        {
            var w = t!["when"]!.AsObject();
            var when = new TriggerWhen
            {
                Event = w["event"] is JsonObject ev ? Selector(ev) : null,
                Value = w["value"] is { } v ? new ValueCondition(S(v), Compare(w)!) : null,
                Count = w["count"] is JsonObject cnt ? new CountCondition(Selector(cnt), Compare(w)!) : null,
            };
            program.Triggers.Add(new ScenarioTrigger
            {
                Label = t["label"]?.GetValue<string>(),
                When = when,
                Do = [.. t["do"]!.AsArray().Select(d => d!["end"] is { } end
                    ? new TriggerAction { End = S(end) }
                    : new TriggerAction { Actor = d["actor"]?.GetValue<string>(), Step = d["step"] is JsonObject st ? Step(st) : null })],
                Repeat = t["repeat"]!.GetValue<bool>(),
                CooldownSeconds = (float)D(t["cooldownSeconds"]),
                AfterSeconds = t["afterSeconds"] is { } af ? (float)D(af) : null,
                BeforeSeconds = t["beforeSeconds"] is { } bf ? (float)D(bf) : null,
            });
        }
        return program;
    }

    // ---- the fixed contacts

    /// <summary>A sweep's value set on a contact (<c>"0:squarenessDegrees"</c> or <c>"all:…"</c>); a part sets its share too, a share its part to "by share".</summary>
    static void SetContact(JsonObject doc, string key, JsonNode? value)
    {
        int colon = key.IndexOf(':');
        string which = key[..colon], field = key[(colon + 1)..];
        var list = doc["contacts"]!.AsArray();
        for (int i = 0; i < list.Count; i++)
        {
            if (which != "all" && which != i.ToString(CultureInfo.InvariantCulture)) continue;
            var c = list[i]!.AsObject();
            c[field] = value?.DeepClone();
            if (field == "part") c["alongBladeShare"] = ScenarioReader.PartShare(S(value));
            if (field == "alongBladeShare") c["part"] = "by share";
        }
    }

    /// <summary>A resolved contact as the program's <see cref="FixedContactSpec"/>.</summary>
    static FixedContactSpec FixedContact(JsonObject c, int index)
    {
        string meets = c["meets"] is JsonValue mv && mv.TryGetValue(out string? m) ? m! : "body";
        string region = meets == "body" ? S(c["region"]) : "upperArm";   // a guard meeting has no region (unused)
        StepSide? side = null;
        if (region.StartsWith("left", StringComparison.Ordinal) && region.Length > 4 && char.IsUpper(region[4])) { side = StepSide.Left; region = region[4..]; }
        else if (region.StartsWith("right", StringComparison.Ordinal) && region.Length > 5 && char.IsUpper(region[5])) { side = StepSide.Right; region = region[5..]; }
        string swing = S(c["swing"]);
        return new FixedContactSpec
        {
            Index = index,
            AtSeconds = (float)D(c["atSeconds"]),
            Attacker = S(c["attacker"]),
            Defender = S(c["defender"]),
            Region = Enum.Parse<AimRegion>(region, ignoreCase: true),
            Side = side,
            AlongBlade = (float)D(c["alongBladeShare"]),
            ClosingSpeed = (float)D(c["closingSpeedMetresPerSecond"]),
            SquarenessDegrees = (float)D(c["squarenessDegrees"]),
            Strength = swing == "strong" ? AttackStrength.Strong : AttackStrength.Quick,
            Direction = swing switch { "left" => AttackDirection.Left, "overhead" => AttackDirection.Overhead, "thrust" => AttackDirection.Thrust, _ => AttackDirection.Right },
            MotionRate = c["motionRate"] is JsonValue mr && mr.TryGetValue(out double rate) ? (float)rate : null,
            PeakSpeed = c["peakSpeedMetresPerSecond"] is JsonValue pk && pk.TryGetValue(out double peak) ? (float)peak : null,
            Meets = meets switch { "shield" => FixedMeet.Shield, "heldBlade" => FixedMeet.HeldBlade, "parryingBlade" => FixedMeet.ParryingBlade, _ => FixedMeet.Body },
        };
    }
    // ---- end

    static InputSegment Segment(JsonObject s)
    {
        Buttons Of(JsonNode? list)
        {
            var b = Buttons.None;
            foreach (var n in list!.AsArray()) b |= Enum.Parse<Buttons>(S(n), ignoreCase: true);
            return b;
        }
        return new InputSegment
        {
            AtSeconds = (float)D(s["atSeconds"]),
            ForSeconds = s["forSeconds"] is JsonValue f && f.TryGetValue(out double fs) ? (float)fs : null,
            MoveRight = s["moveRight"] is { } mr ? (float)D(mr) : null,
            MoveForward = s["moveForward"] is { } mf ? (float)D(mf) : null,
            TurnLeftDegrees = (float)D(s["turnLeftDegrees"]),
            TurnUpDegrees = (float)D(s["turnUpDegrees"]),
            Hold = Of(s["hold"]),
            Press = Of(s["press"]),
            AttackMode = s["attackMode"] is { } am ? Enum.Parse<AttackDirectionMode>(S(am), ignoreCase: true) : null,
            Label = s["label"]?.GetValue<string>(),
        };
    }

    static ControlChannels Channel(string c) => c switch
    {
        "move" => ControlChannels.Move, "face" => ControlChannels.Face, "attack" => ControlChannels.Attack,
        "block" => ControlChannels.Block, "dodge" => ControlChannels.Dodge, "power" => ControlChannels.Power, _ => ControlChannels.None,
    };

    static Gait GaitOf(JsonNode? g) => S(g) switch { "run" => Gait.Run, "sprint" => Gait.Sprint, "supernatural" => Gait.Supernatural, _ => Gait.Walk };
    static StepSide SideOf(JsonNode? s) => S(s) == "right" ? StepSide.Right : StepSide.Left;
    static string? ActorOr(JsonNode? n, string otherwise) => n is null || IsText(n, otherwise) ? null : S(n);
    static ScenarioPlace Place(JsonNode? p) => new((float)D(p![0]), (float)D(p[1]), p.AsArray().Count > 2 ? (float)D(p[2]) : null);

    /// <summary>A resolved step as the director work's step record.</summary>
    internal static ScenarioStep Step(JsonObject s)
    {
        var kind = ScenarioWords.Steps.First(k => s.ContainsKey(k.Name)).Name;
        var v = s[kind];
        ScenarioStep step = kind switch
        {
            "wait" => new WaitStep { Seconds = (float)D(v) },
            "stand" => new StandStep { Seconds = (float)D(v) },
            "face" => new FaceStep { Toward = Target(v) },
            "moveTo" => new MoveToStep { To = Target(v), Gait = GaitOf(s["gait"]), FaceWhileMoving = ActorOr(s["faceWhileMoving"], "way"), WithinMetres = (float)D(s["withinMetres"]) },
            "keepDistance" => new KeepDistanceStep { From = S(v!["from"]), Metres = (float)D(v["metres"]), ToleranceMetres = (float)D(v["toleranceMetres"]), Gait = GaitOf(s["gait"]) },
            "retreat" => new RetreatStep { From = S(v!["from"]), Metres = (float)D(v["metres"]), Gait = GaitOf(s["gait"]) },
            "strafe" => new StrafeStep { Side = SideOf(v), Gait = GaitOf(s["gait"]), Facing = ActorOr(s["facing"], "its foe") },
            "circle" => new CircleStep { Around = S(v!["around"]), RadiusMetres = (float)D(v["radiusMetres"]), Direction = SideOf(v["direction"]), Gait = GaitOf(s["gait"]) },
            "path" => new PathStep { Points = [.. v!.AsArray().Select(Place)], Gait = GaitOf(s["gait"]), Loop = s["loop"]!.GetValue<bool>() },
            "attack" => Attack(s, S(v)),
            "block" => new BlockStep { Until = v is JsonObject u ? Selector(u["until"]!.AsObject()) : null },
            "parry" => new ParryStep { Against = v!["against"]?.GetValue<string>(), LeadSeconds = (float)D(v["leadSeconds"]) },
            "cancel" => new CancelStep(),
            "dodge" => new DodgeStep { Way = S(v) switch { "left" => DodgeWay.Left, "right" => DodgeWay.Right, "forward" => DodgeWay.Forward, _ => DodgeWay.Back } },
            "usePower" => v is JsonObject slot ? new UsePowerStep { Slot = (int)slot["slot"]!.GetValue<long>() } : new UsePowerStep { Power = S(v) },
            "equip" => new EquipStep { What = S(v) == "melee" ? EquipWhat.Melee : EquipWhat.Ranged },
            "aim" => new AimStep { At = v is JsonArray pt ? new ScenarioAim { Point = Place(pt) } : Aim(v!["region"], S(v["at"]) == "its foe" ? null : S(v["at"])) },
            "draw" => new DrawStep { ToShare = (float)D(v!["toShare"]) },
            "loose" => new LooseStep(),
            "cancelDraw" => new CancelDrawStep(),
            "mark" => new MarkStep { Text = S(v) },
            "repeat" => new RepeatStep { UntilStop = IsText(v, "untilStop"), Count = IsText(v) ? 1 : (int)v!.GetValue<long>(), Steps = [.. s["steps"]!.AsArray().Select(x => Step(x!.AsObject()))] },
            "handOver" => new HandOverStep(),
            "end" => new EndStep { Reason = S(v) },
            _ => throw new InvalidOperationException($"no step {kind}"),
        };
        // A block given as seconds holds that long (its forSeconds).
        float? forSeconds = s["forSeconds"] is { } fs ? (float)D(fs) : kind == "block" && v is JsonValue bs && bs.TryGetValue(out double secs) ? (float)secs : null;
        return step with
        {
            Label = s["label"]?.GetValue<string>(),
            AtSeconds = s["at"] is { } at ? (float)D(at) : null,
            AfterSeconds = s["afterSeconds"] is { } af ? (float)D(af) : null,
            On = s["on"] is JsonObject on ? Selector(on) : null,
            DelaySeconds = s["delaySeconds"] is { } dl ? (float)D(dl) : null,
            ForSeconds = forSeconds,
        };
    }

    static ScenarioTarget Target(JsonNode? v) => v switch
    {
        JsonArray p => new ScenarioTarget { Point = Place(p) },
        JsonObject o => new ScenarioTarget { Actor = S(o["actor"]), DistanceMetres = (float)D(o["distanceMetres"]) },
        JsonValue x when x.TryGetValue(out double deg) => new ScenarioTarget { Degrees = (float)deg },
        _ => new ScenarioTarget { Actor = S(v) },
    };

    static AttackStep Attack(JsonObject s, string direction)
    {
        var (dir, strength) = direction switch
        {
            "left" => (AttackDirection.Left, AttackStrength.Quick),
            "right" => (AttackDirection.Right, AttackStrength.Quick),
            "overhead" => (AttackDirection.Overhead, AttackStrength.Quick),
            "thrust" => (AttackDirection.Thrust, AttackStrength.Quick),
            "strong" => (AttackDirection.None, AttackStrength.Strong),
            _ => (AttackDirection.None, AttackStrength.Quick),
        };
        var aim = s["aimAt"];
        return new AttackStep
        {
            Direction = dir,
            Strength = strength,
            HoldSeconds = s["holdSeconds"] is JsonValue h && h.TryGetValue(out double hold) ? (float)hold : null,
            WaitReady = s["waitReady"]!.GetValue<bool>(),
            ReadySeconds = (float)D(s["readySeconds"]),
            AimAt = aim is null || IsText(aim, "level") ? null
                : aim is JsonArray pt ? new ScenarioAim { Point = Place(pt) }
                : aim is JsonObject ang ? new ScenarioAim { PitchDegrees = (float)D(ang["pitchDegrees"]), YawDegrees = (float)D(ang["yawDegrees"]) }
                : Aim(aim, null),
            Target = ActorOr(s["target"], "its foe"),
        };
    }

    /// <summary>A region as <c>aimAt</c> names it (<c>chest</c>, <c>leftUpperArm</c>) as the model's region and side.</summary>
    static ScenarioAim Aim(JsonNode? region, string? actor)
    {
        string r = S(region);
        StepSide? side = null;
        if (r.StartsWith("left", StringComparison.Ordinal)) { side = StepSide.Left; r = r[4..]; }
        else if (r.StartsWith("right", StringComparison.Ordinal)) { side = StepSide.Right; r = r[5..]; }
        return new ScenarioAim { Actor = actor, Region = Enum.Parse<AimRegion>(r, ignoreCase: true), Side = side };
    }

    internal static EventSelector Selector(JsonObject s) => new(
        S(s["event"]),
        s["by"]?.GetValue<string>(),
        s["on"]?.GetValue<string>(),
        s["where"] is JsonObject w ? [.. w.Select(f => new WhereFilter(f.Key, f.Value is JsonArray xs ? [.. xs.Select(Word)] : [Word(f.Value)]))] : null,
        s["fromSeconds"] is { } fs ? D(fs) : null,
        s["toSeconds"] is { } ts ? D(ts) : null,
        s["fromMarker"]?.GetValue<string>(),
        s["toMarker"]?.GetValue<string>());

    /// <summary>A filter's value as invariant text: <c>body</c>, <c>true</c>, <c>3</c>.</summary>
    static string Word(JsonNode? v) => v is JsonValue x && x.TryGetValue(out string? s) ? s! : v?.ToJsonString() ?? "";

    static Comparison? Compare(JsonObject o)
    {
        foreach (var c in ScenarioWords.Comparators)
        {
            if (o[c] is not { } v) continue;
            return c switch
            {
                "below" => new Comparison(CompareOp.Below, D(v)),
                "atMost" => new Comparison(CompareOp.AtMost, D(v)),
                "above" => new Comparison(CompareOp.Above, D(v)),
                "atLeast" => new Comparison(CompareOp.AtLeast, D(v)),
                "equals" => new Comparison(CompareOp.Equals, v is JsonValue b && b.TryGetValue(out bool flag) ? (flag ? 1 : 0) : D(v)),
                "between" => new Comparison(CompareOp.Between, D(v[0]), D(v[1])),
                "isTrue" => new Comparison(CompareOp.IsTrue),
                _ => new Comparison(CompareOp.IsFalse),
            };
        }
        return null;
    }

    // ---------------------------------------------------------------- measures, assertions, stop, outputs

    ValueForm Form(JsonObject o)
    {
        if (o["value"] is { } v) return o["atEvent"] is JsonObject ae ? new ObservableAtEventForm(S(v), Selector(ae), (int)(o["nth"]?.GetValue<long>() ?? 1)) : new ObservableForm(S(v), o["atSeconds"] is { } at ? D(at) : null);
        if (o["count"] is JsonObject c) return new CountForm(Selector(c), c["sum"]?.GetValue<string>());
        if (o["rate"] is JsonObject r)
            return new RateForm(Selector(r["of"]!.AsObject()), r["per"] is JsonObject per ? Selector(per) : null, r["perSecond"]?.GetValue<bool>() == true,
                r["of"]!["sum"]?.GetValue<string>(), r["per"]?["sum"]?.GetValue<string>());
        if (o["timeOf"] is JsonObject t) return new TimeOfForm(Selector(t), (int)(o["nth"]?.GetValue<long>() ?? 1));
        if (o["delay"] is JsonObject d) return new DelayForm(Selector(d["from"]!.AsObject()), Selector(d["to"]!.AsObject()), IsText(d["pairBy"], "next") ? null : d["pairBy"]?.GetValue<string>());
        if (o["difference"] is JsonArray df) return new DifferenceForm(Part(df[0]), Part(df[1]));
        if (o["ratio"] is JsonArray ra) return new RatioForm(Part(ra[0]), Part(ra[1]));
        if (o["measure"] is { } m) return new MeasureRefForm(S(m));
        return new CheckForm(S(o["check"]));
    }

    ValueForm Part(JsonNode? p) => p is JsonObject inner ? Form(inner) : new MeasureRefForm(S(p));

    public IReadOnlyList<MeasureSpec> MeasureSpecs() =>
        [.. resolved["measures"]!.AsArray().Select((m, i) => new MeasureSpec(S(m!["name"]), Form(m.AsObject()), Pointer: $"/measures/{i}"))];

    public IReadOnlyList<AssertionSpec> AssertionSpecs() =>
        [.. resolved["assertions"]!.AsArray().Select((a, i) =>
        {
            var o = a!.AsObject();
            string scope = S(o["scope"]);
            return new AssertionSpec(S(o["name"]), Form(o), Compare(o),
                scope switch { "anyRun" => AssertionScope.AnyRun, "aggregate" => AssertionScope.Aggregate, "trend" => AssertionScope.Trend, _ => AssertionScope.EachRun },
                PassShare: o["passShare"] is { } ps ? D(ps) : 1.0,
                Statistic: o["statistic"] is { } st && !IsText(st, "the measure's own") ? Enum.Parse<Statistic>(S(st), ignoreCase: true) : null,
                Over: o["over"]?.GetValue<string>(),
                Direction: o["direction"] is { } dir ? Enum.Parse<TrendDirection>(S(dir), ignoreCase: true) : null,
                Points: o["points"] is JsonObject pts ? [.. pts.Select(p => new PointFilter(p.Key, [.. p.Value!.AsArray().Select(x => x!.ToJsonString().Trim('"'))]))] : null,
                Pointer: $"/assertions/{i}");
        })];

    public ScenarioStop Stop()
    {
        var s = resolved["stop"]!.AsObject();
        return new ScenarioStop(D(s["afterSeconds"]), S(s["onHarnessStops"]) == "never" ? HarnessStops.Never : HarnessStops.First,
            [.. s["when"]!.AsArray().Select((c, i) => new StopCondition(Form(c!.AsObject()), Compare(c.AsObject())!, $"/stop/when/{i}"))],
            s["onObjectives"] is JsonValue oo ? S(oo) : "none");
    }

    public (ScenarioOutputs, string, double) Outputs()
    {
        var o = resolved["outputs"]!.AsObject();
        var events = S(o["events"]) switch { "all" => TraceRuns.All, "none" => TraceRuns.None, _ => TraceRuns.First };
        var ticks = o["ticks"] is JsonObject t ? new TicksOutput((int)t["everyTicks"]!.GetValue<long>(), S(t["runs"]) == "all" ? TraceRuns.All : TraceRuns.First) : null;
        int? tapes = o["tapes"] is JsonValue tv && tv.TryGetValue(out long n) ? (int)n : null;
        var policy = S(o["tracePolicy"]) switch { "NONE" => TracePolicy.None, "FAILURES" => TracePolicy.Failures, "ALL" => TracePolicy.All, _ => TracePolicy.Sampled };
        return (new ScenarioOutputs(events, (int)o["traceRuns"]!.GetValue<long>(), ticks, o["csv"]!.GetValue<bool>(), o["summaryPage"]!.GetValue<bool>(), tapes,
                policy, (int)o["traceBudgetRuns"]!.GetValue<long>(), o["wideBouts"]!.GetValue<bool>(),
                (o["decisions"] is { } dn ? S(dn) : null) switch { "all" => DecisionsOutput.All, "none" => DecisionsOutput.None, _ => DecisionsOutput.Traced }),
            S(o["tracePolicy"]), D(o["traceBudgetMegabytes"]));
    }
}
