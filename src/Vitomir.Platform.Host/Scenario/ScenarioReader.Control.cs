// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json;
using System.Text.Json.Nodes;
using Vitomir.Platform.Control;
using Vitomir.Platform.Host.Arena;

namespace Vitomir.Platform.Host.Scenario;

internal sealed partial class ScenarioReader
{
    // ---------------------------------------------------------------- control

    JsonObject ControlOf(JsonElement a, string at, int index)
    {
        string cp = at + "/control";
        var o = new JsonObject();
        bool hasControl = a.TryGetProperty("control", out var c) && c.ValueKind != JsonValueKind.Null;
        if (hasControl && !IsObject(c, cp, "{ \"ai\", \"mute\", \"script\" | \"tracks\", \"then\" }")) hasControl = false;
        if (hasControl) Known(c, cp, ControlFields, "a control block");
        bool hasScript = hasControl && (Has(c, "script") || Has(c, "tracks"));
        bool hasAi = hasControl && Has(c, "ai");

        // The AI: its kind and, for the duelling brain, every number made explicit.
        var ai = new JsonObject { ["kind"] = hasScript ? "none" : "stand" };
        if (hasAi) ai = Ai(c.GetProperty("ai"), cp + "/ai");
        o["ai"] = ai;

        var mute = new JsonArray();
        if (hasControl && c.TryGetProperty("mute", out var mv) && mv.ValueKind != JsonValueKind.Null && IsList(mv, cp + "/mute", "channels: " + string.Join(", ", ScenarioWords.Channels)))
        {
            int i = 0;
            foreach (var x in mv.EnumerateArray())
            {
                if (Word(x, ScenarioPointer.Of(cp + "/mute", i++), ScenarioWords.Channels, "a channel") is { } ch && !mute.Any(m => m!.GetValue<string>() == ch)) mute.Add(ch);
            }
            if (mute.Count > 0 && ai["kind"]!.GetValue<string>() == "none")
                Error(ScenarioErrorCode.BadBrain, cp + "/mute", "mutes channels of an AI the actor does not have; give \"ai\" (\"duel\", \"stand\", \"block\") or leave \"mute\" out");
        }
        o["mute"] = mute;

        var tracks = new JsonArray();
        if (hasControl && Has(c, "script") && Has(c, "tracks"))
            Error(ScenarioErrorCode.BadStep, cp, "give \"script\" (one track) or \"tracks\" (several at once), not both");
        else if (hasControl && c.TryGetProperty("script", out var sc) && sc.ValueKind != JsonValueKind.Null)
        {
            if (IsList(sc, cp + "/script", "a list of steps")) tracks.Add(Track(sc, cp + "/script", index));
        }
        else if (hasControl && c.TryGetProperty("tracks", out var ts) && ts.ValueKind != JsonValueKind.Null && IsList(ts, cp + "/tracks", "a list of tracks, each a list of steps"))
        {
            int t = 0;
            foreach (var track in ts.EnumerateArray())
            {
                string tp = ScenarioPointer.Of(cp + "/tracks", t++);
                if (IsList(track, tp, "a list of steps")) tracks.Add(Track(track, tp, index));
            }
            ChannelClashes(tracks, cp + "/tracks");
        }
        o["tracks"] = tracks;

        // The director work's PLAYER_INPUT_REPLAY: the actor driven through the player's own controls, and nothing else.
        if (hasControl && c.TryGetProperty("playerInput", out var pi) && pi.ValueKind != JsonValueKind.Null)
        {
            if (inputActor is { } other) Error(ScenarioErrorCode.BadInput, cp + "/playerInput", $"{other} is already driven by the player's input; one actor at most is the player");
            inputActor = actorNames[index];
            foreach (var f in new[] { "ai", "mute", "script", "tracks" })
                if (Has(c, f)) Error(ScenarioErrorCode.BadInput, ScenarioPointer.Of(cp, f), "an actor driven by the player's input takes no ai, mute, script or tracks: the input holds every channel");
            o["ai"] = new JsonObject { ["kind"] = "none" };
            o["playerInput"] = PlayerInput(pi, cp + "/playerInput");
            o["then"] = "stand";
            return o;
        }
        string kind = ai["kind"]!.GetValue<string>();
        string then = kind == "none" ? "stand" : "ai";
        if (hasControl && Word(c, "then", cp, ScenarioWords.Thens, "what follows the script") is { } given)
        {
            if (given == "ai" && kind == "none") Error(ScenarioErrorCode.BadBrain, cp + "/then", "hands over to an AI the actor does not have; give \"ai\" or make it \"stand\" or \"end\"");
            then = given;
        }
        o["then"] = then;
        return o;
    }

    /// <summary>The actor driven by the player's input, once one is (one at most).</summary>
    internal string? inputActor;

    static readonly string[] SegmentFields = ["atSeconds", "forSeconds", "moveRight", "moveForward", "turnLeftDegrees", "turnUpDegrees", "hold", "press", "attackMode", "label"];

    /// <summary>
    /// The buttons a segment may hold or press (the platform's <see cref="Buttons"/>, by name, case aside). not
    /// the retired <see cref="Buttons.SwapShoulder"/> (no button the player can press; switchView cycles both shoulders).
    /// </summary>
    internal static readonly string[] ButtonNames = [.. Enum.GetNames<Buttons>().Where(n => n != nameof(Buttons.None) && n != nameof(Buttons.SwapShoulder)).Select(n => char.ToLowerInvariant(n[0]) + n[1..])];

    /// <summary>The ways of choosing a blow's direction (the platform's <see cref="AttackDirectionMode"/>).</summary>
    internal static readonly string[] AttackModeNames = [.. Enum.GetNames<AttackDirectionMode>().Select(n => char.ToLowerInvariant(n[0]) + n[1..])];

    /// <summary>A player's input: segments of move keys, look turned, buttons held and pressed (the director work's <c>InputSegment</c>).</summary>
    JsonArray PlayerInput(JsonElement v, string pp)
    {
        var list = new JsonArray();
        if (v.ValueKind != JsonValueKind.Array) { Error(ScenarioErrorCode.BadInput, pp, $"must be a list of segments ({{ \"atSeconds\", \"forSeconds\", \"moveForward\", \"hold\": [\"attack\"], ... }}), got {Kind(v)}"); return list; }
        int i = 0;
        foreach (var seg in v.EnumerateArray())
        {
            string sp = ScenarioPointer.Of(pp, i++);
            if (seg.ValueKind != JsonValueKind.Object) { Error(ScenarioErrorCode.BadInput, sp, $"a segment is an object, got {Kind(seg)}"); continue; }
            Known(seg, sp, SegmentFields, "a player-input segment");
            var o = new JsonObject { ["atSeconds"] = Number(seg, "atSeconds", sp, 0, 86_400, "s") ?? 0 };
            o["forSeconds"] = Number(seg, "forSeconds", sp, 0, 86_400, "s") is { } fs ? fs : "one tick";
            if (Number(seg, "moveRight", sp, -1, 1, "-1 left to 1 right") is { } mr) o["moveRight"] = mr;
            if (Number(seg, "moveForward", sp, -1, 1, "-1 back to 1 forward") is { } mf) o["moveForward"] = mf;
            o["turnLeftDegrees"] = Number(seg, "turnLeftDegrees", sp, -36_000, 36_000, "degrees, spread over the segment") ?? 0;
            o["turnUpDegrees"] = Number(seg, "turnUpDegrees", sp, -180, 180, "degrees, spread over the segment") ?? 0;
            foreach (var f in new[] { "hold", "press" })
            {
                var names = new JsonArray();
                if (seg.TryGetProperty(f, out var bs) && bs.ValueKind != JsonValueKind.Null && IsList(bs, ScenarioPointer.Of(sp, f), "the player's buttons by name (\"attack\", \"block\", \"dodge\", \"sprint\")"))
                {
                    int k = 0;
                    foreach (var b in bs.EnumerateArray())
                    {
                        string bp = ScenarioPointer.Of(ScenarioPointer.Of(sp, f), k++);
                        if (b.ValueKind != JsonValueKind.String) { Error(ScenarioErrorCode.BadInput, bp, $"must be a button's name, got {Kind(b)}"); continue; }
                        if (string.Equals(b.GetString(), "swapShoulder", StringComparison.OrdinalIgnoreCase))
                        { Error(ScenarioErrorCode.UnknownName, bp, "there is no button \"swapShoulder\" any more (retired): \"switchView\" cycles first person, the right shoulder and the left", ["switchView"]); continue; }
                        if (Name(b.GetString()!, bp, ButtonNames, "button") is { } name) names.Add(name);
                    }
                }
                o[f] = names;
            }
            if (seg.TryGetProperty("attackMode", out var am) && am.ValueKind != JsonValueKind.Null) o["attackMode"] = Word(am, sp + "/attackMode", AttackModeNames, "a way of choosing a blow's direction");
            if (Text(seg, "label", sp) is { } label) o["label"] = label;
            list.Add(o);
        }
        return list;
    }

    JsonObject Ai(JsonElement v, string ap)
    {
        if (v.ValueKind == JsonValueKind.String)
        {
            string? kind = Word(v, ap, ScenarioWords.AiKinds, "an AI's kind");
            return kind == "duel" ? DuelNumbers(default, ap) : new JsonObject { ["kind"] = kind ?? "stand" };   // "archer" with the module's numbers
        }
        if (!IsObject(v, ap, "an AI's kind, or { \"kind\": \"duel\", <brain numbers> }")) return new JsonObject { ["kind"] = "stand" };
        if (!v.TryGetProperty("kind", out var k)) { Error(ScenarioErrorCode.BadBrain, ap + "/kind", $"is required: {ScenarioPointer.Words(ScenarioWords.AiKinds)}"); return new JsonObject { ["kind"] = "stand" }; }
        string? kind2 = Word(k, ap + "/kind", ScenarioWords.AiKinds, "an AI's kind");
        if (kind2 == "duel") return DuelNumbers(v, ap);
        if (kind2 == "archer") return ArcherNumbers(v, ap);
        foreach (var p in v.EnumerateObject())
            if (p.Name != "kind")
                Error(p.Name is var n && BrainNumbers.Find(n) is not null ? ScenarioErrorCode.BadBrain : ScenarioErrorCode.UnknownField, ScenarioPointer.Of(ap, p.Name),
                    BrainNumbers.Find(p.Name) is not null ? $"the AI \"{kind2}\" takes no numbers (only \"duel\" does)" : $"an AI \"{kind2}\" takes only \"kind\"",
                    BrainNumbers.Find(p.Name) is null ? ScenarioPointer.Nearest(p.Name, ["kind"]) : null);
        return new JsonObject { ["kind"] = kind2 ?? "stand" };
    }

    /// <summary>The duelling brain's numbers, every one explicit (the given ones checked by the arena's table: its names and ranges).</summary>
    JsonObject DuelNumbers(JsonElement v, string ap)
    {
        var o = new JsonObject { ["kind"] = "duel" };
        bool given = v.ValueKind == JsonValueKind.Object;
        if (given) Known(v, ap, ["kind", .. BrainNumbers.Names, ArenaSpec.ApproachField], "the duelling brain");   // its approach
        var d = new Combat.DuelBrainSettings();
        foreach (var n in BrainNumbers.All)
        {
            JsonElement x = default;
            bool has = given && v.TryGetProperty(n.Name, out x) && x.ValueKind != JsonValueKind.Null;
            string np = ScenarioPointer.Of(ap, n.Name);
            switch (n.Kind)
            {
                case BrainNumberKind.Number:
                    o[n.Name] = has ? Number(x, np, n.Least, n.Most, n.Unit) ?? n.Get(d) : Math.Round(n.Get(d), 6);
                    break;
                case BrainNumberKind.Count:
                    o[n.Name] = has ? Whole(x, np, (long)n.Least, (long)n.Most, n.Unit) ?? (long)n.Get(d) : (long)n.Get(d);
                    break;
                case BrainNumberKind.Flag:
                    if (has && x.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) Error(ScenarioErrorCode.WrongType, np, $"must be true or false, got {Kind(x)}");
                    o[n.Name] = has && x.ValueKind is JsonValueKind.True or JsonValueKind.False ? x.GetBoolean() : n.Get(d) != 0f;
                    break;
                case BrainNumberKind.Pace:
                    if (!has) { o[n.Name] = "its kind's"; break; }
                    bool none = x.ValueKind == JsonValueKind.String && x.GetString() == "none";
                    bool pair = x.ValueKind == JsonValueKind.Array && x.GetArrayLength() == 2 && x[0].ValueKind == JsonValueKind.Number && x[1].ValueKind == JsonValueKind.Number;
                    double lo = pair ? x[0].GetDouble() : 0, hi = pair ? x[1].GetDouble() : 0;
                    if (!(none || pair) || !(lo >= 0 && hi >= lo && hi <= 60)) { Error(ScenarioErrorCode.BadBrain, np, "must be \"none\" or [least, most] seconds (0 to 60, rising)"); o[n.Name] = "its kind's"; }
                    else o[n.Name] = none ? "none" : new JsonArray(lo, hi);
                    break;
            }
        }
        if (given && ApproachGiven(v, ap) is { } approach) o[ArenaSpec.ApproachField] = approach;   // else its preset's (ApproachDefault)
        return o;
    }

    // ---------------------------------------------------------------- steps

    JsonArray Track(JsonElement list, string tp, int index)
    {
        var track = new JsonArray();
        int i = 0;
        foreach (var s in list.EnumerateArray())
        {
            string sp = ScenarioPointer.Of(tp, i++);
            if (Step(s, sp, index) is { } step) track.Add(step);
        }
        return track;
    }

    /// <summary>One step: exactly one kind field, its value and fields checked by its kind, the timing fields; the resolved step with its defaults.</summary>
    internal JsonObject? Step(JsonElement s, string sp, int index)
    {
        if (s.ValueKind != JsonValueKind.Object) { Error(ScenarioErrorCode.BadStep, sp, $"a step is an object with one kind field ({{ \"wait\": 1 }}), got {Kind(s)}"); return null; }
        var kinds = s.EnumerateObject().Where(p => ScenarioWords.Step(p.Name) is not null).Select(p => p.Name).ToList();
        if (kinds.Count != 1)
        {
            var names = ScenarioWords.Steps.Select(k => k.Name).ToList();
            var guess = s.EnumerateObject().Select(p => p.Name).Where(n => !ScenarioWords.Timing.Any(t => t.Name == n)).SelectMany(n => ScenarioPointer.Nearest(n, names)).Distinct().ToList();
            Error(ScenarioErrorCode.BadStep, sp, kinds.Count == 0
                ? $"names no step; a step is an object with one of {string.Join(", ", names)}"
                : $"names {kinds.Count} steps ({string.Join(", ", kinds)}); a step is one kind (put them one after another)", kinds.Count == 0 ? guess : null);
            return null;
        }
        var kind = ScenarioWords.Step(kinds[0])!;
        Known(s, sp, [kind.Name, .. ScenarioWords.Timing.Select(t => t.Name), .. kind.Fields.Select(f => f.Name)], $"a \"{kind.Name}\" step");
        if (kind.Status == "bow" && !ScenarioGame.BowBuilt) { NotBuilt(ScenarioPointer.Of(sp, kind.Name), $"the step \"{kind.Name}\" (the bow)", "a later build"); return null; }
        string kp = ScenarioPointer.Of(sp, kind.Name);
        var v = s.GetProperty(kind.Name);
        var o = new JsonObject();
        string me = actorNames[index];
        JsonNode? value = null;
        switch (kind.Name)
        {
            case "wait":
            case "stand":
                value = Number(v, kp, 0, 3600, "s");
                break;
            case "face":
                value = v.ValueKind switch
                {
                    JsonValueKind.String => OtherActor(v, kp, me),
                    JsonValueKind.Number => Number(v, kp, -3600, 3600, "degrees counter-clockwise from +X"),
                    _ => Point2(v, kp, "an actor's name, [x, y] or degrees"),
                };
                break;
            case "moveTo":
                if (v.ValueKind == JsonValueKind.Array) value = Point2(v, kp, "[x, y] or { \"actor\", \"distanceMetres\" }");
                else if (IsObject(v, kp, "[x, y] or { \"actor\", \"distanceMetres\" }"))
                {
                    Known(v, kp, ["actor", "distanceMetres"], "a moveTo's goal");
                    value = new JsonObject
                    {
                        ["actor"] = v.TryGetProperty("actor", out var ma) ? OtherActor(ma, kp + "/actor", me) : Required(kp + "/actor", "the actor to go to"),
                        ["distanceMetres"] = Number(v, "distanceMetres", kp, 0, 100, "m") ?? 0,
                    };
                }
                o["gait"] = Gait(s, sp);
                o["faceWhileMoving"] = s.TryGetProperty("faceWhileMoving", out var fw) && fw.ValueKind != JsonValueKind.Null
                    ? fw.ValueKind == JsonValueKind.String && fw.GetString() == "way" ? "way" : OtherActor(fw, sp + "/faceWhileMoving", me)
                    : "way";
                o["withinMetres"] = Number(s, "withinMetres", sp, 0.01, 10, "m") ?? 0.2;
                break;
            case "keepDistance":
                if (IsObject(v, kp, "{ \"from\", \"metres\", \"toleranceMetres\" }"))
                {
                    Known(v, kp, ["from", "metres", "toleranceMetres"], "keepDistance");
                    value = new JsonObject
                    {
                        ["from"] = v.TryGetProperty("from", out var kf) ? OtherActor(kf, kp + "/from", me) : Required(kp + "/from", "the actor to keep the distance from"),
                        ["metres"] = Has(v, "metres") ? Number(v, "metres", kp, 0, 100, "m") : Required(kp + "/metres", "the distance to keep (m)"),
                        ["toleranceMetres"] = Number(v, "toleranceMetres", kp, 0, 10, "m") ?? 0.2,
                    };
                }
                o["gait"] = Gait(s, sp);
                NeedsFor(s, sp, kind.Name);
                break;
            case "retreat":
                if (IsObject(v, kp, "{ \"from\", \"metres\" }"))
                {
                    Known(v, kp, ["from", "metres"], "retreat");
                    value = new JsonObject
                    {
                        ["from"] = v.TryGetProperty("from", out var rf) ? OtherActor(rf, kp + "/from", me) : Required(kp + "/from", "the actor to retreat from"),
                        ["metres"] = Has(v, "metres") ? Number(v, "metres", kp, 0, 100, "m") : Required(kp + "/metres", "how far from that actor to go (m)"),
                    };
                }
                o["gait"] = Gait(s, sp);
                break;
            case "strafe":
                value = Word(v, kp, ScenarioWords.Sides, "a side");
                o["gait"] = Gait(s, sp);
                o["facing"] = s.TryGetProperty("facing", out var sf) && sf.ValueKind != JsonValueKind.Null ? OtherActor(sf, sp + "/facing", me) : "its foe";
                NeedsFor(s, sp, kind.Name);
                break;
            case "circle":
                if (IsObject(v, kp, "{ \"around\", \"radiusMetres\", \"direction\" }"))
                {
                    Known(v, kp, ["around", "radiusMetres", "direction"], "circle");
                    value = new JsonObject
                    {
                        ["around"] = v.TryGetProperty("around", out var ca) ? OtherActor(ca, kp + "/around", me) : Required(kp + "/around", "the actor to go round"),
                        ["radiusMetres"] = Has(v, "radiusMetres") ? Number(v, "radiusMetres", kp, 0.1, 100, "m") : Required(kp + "/radiusMetres", "the radius (m)"),
                        ["direction"] = Word(v, "direction", kp, ScenarioWords.Sides, "a direction") ?? "left",
                    };
                }
                o["gait"] = Gait(s, sp);
                NeedsFor(s, sp, kind.Name);
                break;
            case "path":
                if (IsList(v, kp, "a list of waypoints [[x, y], ...]"))
                {
                    var pts = new JsonArray();
                    int i = 0;
                    foreach (var p in v.EnumerateArray()) if (Point2(p, ScenarioPointer.Of(kp, i++), "[x, y]") is { } pt) pts.Add(pt);
                    if (v.GetArrayLength() == 0) Error(ScenarioErrorCode.BadStep, kp, "needs one waypoint at least");
                    value = pts;
                }
                o["gait"] = Gait(s, sp);
                o["loop"] = Flag(s, "loop", sp) ?? false;
                if (o["loop"]!.GetValue<bool>()) NeedsFor(s, sp, "path with loop");
                break;
            case "attack":
                value = Word(v, kp, ScenarioWords.AttackDirections, "a blow's direction");
                // Not given: a tap — or, for a strong blow, held past the strong threshold (the director's), so not a number here.
                o["holdSeconds"] = Number(s, "holdSeconds", sp, 0, 10, "s") is { } hold ? hold : value?.GetValue<string>() == "strong" ? "the strong threshold" : "tap";
                o["waitReady"] = Flag(s, "waitReady", sp) ?? true;
                o["readySeconds"] = Number(s, "readySeconds", sp, 0, 10, "s") ?? 2;
                o["aimAt"] = s.TryGetProperty("aimAt", out var aim) && aim.ValueKind != JsonValueKind.Null ? AimAt(aim, sp + "/aimAt") : "level";
                o["target"] = s.TryGetProperty("target", out var tg) && tg.ValueKind != JsonValueKind.Null ? OtherActor(tg, sp + "/target", me) : "its foe";
                break;
            case "block":
                if (v.ValueKind == JsonValueKind.Number) value = Number(v, kp, 0, 3600, "s");
                else if (IsObject(v, kp, "seconds, or { \"until\": <event selector> }"))
                {
                    Known(v, kp, ["until"], "block");
                    value = v.TryGetProperty("until", out var u) ? new JsonObject { ["until"] = Selector(u, kp + "/until") } : Required(kp + "/until", "an event selector");
                }
                break;
            case "parry":
                if (IsObject(v, kp, "{ \"against\": \"<actor>\", \"leadSeconds\": 0.25 }"))
                {
                    Known(v, kp, ["against", "leadSeconds"], "parry");
                    value = new JsonObject
                    {
                        ["against"] = v.TryGetProperty("against", out var pa) ? OtherActor(pa, kp + "/against", me) : "its foe",
                        ["leadSeconds"] = Number(v, "leadSeconds", kp, 0, 2, "s") ?? 0.25,
                    };
                }
                break;
            case "cancel":
            case "loose":
            case "cancelDraw":
                if (v.ValueKind != JsonValueKind.True) Error(ScenarioErrorCode.BadStep, kp, $"is true (\"{kind.Name}\": true), got {Kind(v)}");
                value = true;
                break;
            case "dodge":
                value = Word(v, kp, ScenarioWords.DodgeWays, "a dodge's way");
                break;
            case "usePower":
                if (v.ValueKind == JsonValueKind.String) value = game is null ? v.GetString() : Name(v.GetString()!, kp, game.Powers.Keys, "power");
                else if (IsObject(v, kp, "a power's id, or { \"slot\": 1 to 8 }"))
                {
                    Known(v, kp, ["slot"], "usePower");
                    value = new JsonObject { ["slot"] = Has(v, "slot") ? Whole(v, "slot", kp, 1, Powers.QuickSlots.Count) : Required(kp + "/slot", "a quick slot, 1 to 8") };
                }
                break;
            case "equip":
                value = Word(v, kp, ["ranged", "melee"], "what to hold");
                break;
            case "aim":
                if (v.ValueKind == JsonValueKind.Array) value = Point3(v, kp);
                else if (IsObject(v, kp, "{ \"at\": \"<actor>\", \"region\" } or [x, y, z]"))
                {
                    Known(v, kp, ["at", "region"], "aim");
                    value = new JsonObject
                    {
                        ["at"] = v.TryGetProperty("at", out var aa) ? OtherActor(aa, kp + "/at", me) : "its foe",
                        ["region"] = Word(v, "region", kp, ScenarioWords.AimRegions, "a body region") ?? "chest",
                    };
                }
                break;
            case "draw":
                if (IsObject(v, kp, "{ \"toShare\": 0 to 1 }"))
                {
                    Known(v, kp, ["toShare"], "draw");
                    value = new JsonObject { ["toShare"] = Number(v, "toShare", kp, 0, 1, "share of full draw") ?? 1.0 };
                }
                break;
            case "mark":
            case "end":
                if (v.ValueKind != JsonValueKind.String || v.GetString()!.Length == 0) Error(ScenarioErrorCode.BadStep, kp, $"is text ({(kind.Name == "mark" ? "the marker's text" : "the reason the run ends")}), got {Kind(v)}");
                else value = v.GetString();
                break;
            case "handOver":
                if (v.ValueKind != JsonValueKind.String || v.GetString() != "ai") Error(ScenarioErrorCode.BadStep, kp, $"is \"ai\" (every channel to the AI from here), got {Kind(v)}");
                value = "ai";
                break;
            case "repeat":
                if (v.ValueKind == JsonValueKind.String)
                {
                    if (v.GetString() != "untilStop") Error(ScenarioErrorCode.BadStep, kp, $"is a count (1 or more) or \"untilStop\", got {Kind(v)}");
                    value = "untilStop";
                }
                else value = Whole(v, kp, 1, 100_000, "times");
                if (!s.TryGetProperty("steps", out var rs) || rs.ValueKind != JsonValueKind.Array || rs.GetArrayLength() == 0)
                    Error(ScenarioErrorCode.BadStep, sp + "/steps", "is required: the steps to repeat, a list of one step or more");
                else o["steps"] = Track(rs, sp + "/steps", index);
                break;
        }
        var step = new JsonObject { [kind.Name] = value };
        foreach (var (k, x) in o.ToList()) { o.Remove(k); step[k] = x; }
        Timing(s, sp, step);
        return step;
    }

    void Timing(JsonElement s, string sp, JsonObject step)
    {
        if (Number(s, "at", sp, 0, 86_400, "s from the run's start") is { } at) step["at"] = at;
        if (Number(s, "afterSeconds", sp, 0, 3600, "s") is { } after) step["afterSeconds"] = after;
        if (s.TryGetProperty("on", out var on) && on.ValueKind != JsonValueKind.Null) step["on"] = Selector(on, sp + "/on");
        if (Number(s, "delaySeconds", sp, 0, 60, "s") is { } delay)
        {
            if (!Has(s, "on")) Error(ScenarioErrorCode.BadStep, sp + "/delaySeconds", "goes with \"on\" (the delay after the event); give \"afterSeconds\" for a delay after the step before");
            step["delaySeconds"] = delay;
        }
        if (Number(s, "forSeconds", sp, 0, 86_400, "s") is { } fs) step["forSeconds"] = fs;
        if (Text(s, "label", sp) is { } label) step["label"] = label;
        if (Has(s, "at") && Has(s, "on")) Error(ScenarioErrorCode.BadStep, sp + "/on", "a step starts at a time (\"at\") or on an event (\"on\"), not both");
    }

    JsonNode? Required(string at, string what)
    {
        Error(ScenarioErrorCode.BadStep, at, $"is required: {what}");
        return null;
    }

    void NeedsFor(JsonElement s, string sp, string kind)
    {
        if (!Has(s, "forSeconds")) Error(ScenarioErrorCode.BadStep, sp + "/forSeconds", $"is required: a \"{kind}\" step runs until its forSeconds");
    }

    string Gait(JsonElement s, string sp) => Word(s, "gait", sp, ScenarioWords.Gaits, "a gait") ?? "walk";

    JsonNode? OtherActor(JsonElement v, string at, string me)
    {
        var name = Actor(v, at);
        if (name == me) { Error(ScenarioErrorCode.BadStep, at, $"names the actor itself ({me})"); return null; }
        return name;
    }

    JsonNode? Point2(JsonElement v, string at, string what)
    {
        if (v.ValueKind == JsonValueKind.Array && v.GetArrayLength() == 2 && v.EnumerateArray().All(x => x.ValueKind == JsonValueKind.Number))
            return new JsonArray(v[0].GetDouble(), v[1].GetDouble());
        Error(ScenarioErrorCode.BadStep, at, $"must be {what} (metres, the level's frame), got {Kind(v)}");
        return null;
    }

    JsonNode? Point3(JsonElement v, string at)
    {
        if (v.ValueKind == JsonValueKind.Array && v.GetArrayLength() == 3 && v.EnumerateArray().All(x => x.ValueKind == JsonValueKind.Number))
            return new JsonArray(v[0].GetDouble(), v[1].GetDouble(), v[2].GetDouble());
        Error(ScenarioErrorCode.BadStep, at, $"must be [x, y, z] (metres, the level's frame), got {Kind(v)}");
        return null;
    }

    JsonNode? AimAt(JsonElement v, string at)
    {
        if (v.ValueKind == JsonValueKind.String) return Word(v, at, ScenarioWords.AimRegions, "a body region");
        if (v.ValueKind == JsonValueKind.Array) return Point3(v, at);
        if (IsObject(v, at, "a region, [x, y, z], or { \"pitchDegrees\", \"yawDegrees\" }"))
        {
            Known(v, at, ["pitchDegrees", "yawDegrees"], "an aim");
            return new JsonObject { ["pitchDegrees"] = Number(v, "pitchDegrees", at, -90, 90, "degrees, up positive") ?? 0, ["yawDegrees"] = Number(v, "yawDegrees", at, -180, 180, "degrees from the body's facing, counter-clockwise") ?? 0 };
        }
        return null;
    }

    /// <summary>
    /// Two tracks of one actor holding one channel from the same scheduled time clash (<see cref="ScenarioErrorCode.ChannelClash"/>):
    /// what is known before the run is each track's first step (at 0, or its <c>at</c>) and every step with an <c>at</c>.
    /// </summary>
    void ChannelClashes(JsonArray tracks, string tp)
    {
        var starts = new List<(int Track, int Step, double At, string[] Channels)>();
        for (int t = 0; t < tracks.Count; t++)
        {
            var track = tracks[t]!.AsArray();
            for (int i = 0; i < track.Count; i++)
            {
                var step = track[i]!.AsObject();
                double? at = step["at"]?.GetValue<double>() ?? (i == 0 && step["on"] is null ? step["afterSeconds"]?.GetValue<double>() ?? 0 : null);
                if (at is null) continue;
                starts.Add((t, i, at.Value, Channels(step)));
            }
        }
        for (int a = 0; a < starts.Count; a++)
            for (int b = a + 1; b < starts.Count; b++)
            {
                if (starts[a].Track == starts[b].Track || Math.Abs(starts[a].At - starts[b].At) > 0.5 / Core.World.TicksPerSecond) continue;
                var shared = starts[a].Channels.Intersect(starts[b].Channels).ToList();
                if (shared.Count > 0)
                    Error(ScenarioErrorCode.ChannelClash, ScenarioPointer.Of(ScenarioPointer.Of(tp, starts[b].Track), starts[b].Step),
                        ReaderInv($"holds {string.Join(", ", shared)} from {starts[b].At} s, as tracks[{starts[a].Track}][{starts[a].Step}] does; one channel is driven by one step at a time (put them in one track, or start one later)"));
            }
    }

    static string ReaderInv(FormattableString s) => Inv(s);

    /// <summary>The channels a resolved step holds (a repeat: its steps').</summary>
    internal static string[] Channels(JsonObject step)
    {
        var kind = ScenarioWords.Steps.FirstOrDefault(k => step.ContainsKey(k.Name));
        if (kind is null) return [];
        if (kind.Name == "repeat" && step["steps"] is JsonArray inner) return [.. inner.SelectMany(s => Channels(s!.AsObject())).Distinct()];
        return [.. kind.Channels];
    }
}
