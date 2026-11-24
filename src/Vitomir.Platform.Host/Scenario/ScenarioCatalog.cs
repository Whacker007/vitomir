// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Reflection;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Host.Scenario;

/// <summary>One field a step takes beside its kind: its name, what it takes, its unit, its default and one sentence.</summary>
public sealed record StepField(string Name, string Takes, string Unit, string Default, string Meaning);

/// <summary>
/// One kind of step: its kind field's name, what that field takes, the channels it holds while
/// it runs, the fields it takes beside the timing fields, when it ends, whether this build has it (<c>built</c>, or the build that
/// brings it) and one sentence. The reader checks a step by it, and <c>--describe steps</c> prints it.
/// </summary>
public sealed record StepKind(string Name, string Takes, IReadOnlyList<string> Channels, IReadOnlyList<StepField> Fields, string Ends, string Status, string Meaning)
{
    /// <summary>Whether this build runs it (its <see cref="Status"/> is <c>built</c>).</summary>
    public bool Built => Status == "built";
}

/// <summary>An observable (section 4.5): a value at a moment by a dotted path from a closed list.</summary>
public sealed record ObservableKind(string Path, string Unit, string Meaning);

/// <summary>A settable path (section 4.9): what a sweep's <c>set</c> may name, what it takes, and whether a point that changes it loads content again.</summary>
public sealed record SettableKind(string Path, string Takes, string Meaning, string Status = "built");

/// <summary>An event field as <c>--describe events</c> publishes it: its name, its type, and whether it names an actor (and as which: by or on).</summary>
public sealed record EventField(string Name, string Type, string? Actor);

/// <summary>An event kind as <c>--describe events</c> publishes it: from its record by reflection, or a kind still to come.</summary>
public sealed record EventKind(string Name, IReadOnlyList<EventField> Fields, string? By, string? On, IReadOnlyList<string> Derived, string Status, string Meaning);

/// <summary>
/// The words a scenario may use that do not come from the loaded game: channels, gaits, regions, the steps, the
/// observables, the settable paths, the value forms, the comparators, the scopes and the events (from their records). The
/// reader checks by these and <c>--describe</c> prints them, so the two cannot drift.
/// </summary>
public static class ScenarioWords
{
    public const string Format = "vitomir-scenario/1";

    public static readonly string[] Channels = ["move", "face", "attack", "block", "dodge", "power"];
    public static readonly string[] Gaits = ["walk", "run", "sprint", "supernatural"];
    public static readonly string[] AiKinds = ["duel", "stand", "block", "none", "archer"];   // archer
    public static readonly string[] Thens = ["ai", "stand", "end"];
    public static readonly string[] Times = ["night", "day"];
    public static readonly string[] Kinds = ["fight", "contact"];
    public static readonly string[] AttackDirections = ["left", "right", "overhead", "thrust", "auto", "quick", "strong"];
    public static readonly string[] DodgeWays = ["back", "left", "right", "forward"];
    public static readonly string[] Sides = ["left", "right"];
    public static readonly string[] Regions = ["head", "neck", "chest", "belly", "hips", "upperArm", "forearm", "hand", "thigh", "shin", "foot"];

    /// <summary>The regions with a side, as <c>aimAt</c> may name them (<c>leftUpperArm</c>), as well as the side-less ones.</summary>
    public static readonly string[] AimRegions = [.. Regions, .. Regions.Skip(5).SelectMany(r => new[] { "left" + char.ToUpperInvariant(r[0]) + r[1..], "right" + char.ToUpperInvariant(r[0]) + r[1..] })];

    public static readonly string[] Comparators = ["below", "atMost", "above", "atLeast", "equals", "between", "isTrue", "isFalse"];
    public static readonly string[] Scopes = ["eachRun", "anyRun", "aggregate", "trend"];
    public static readonly string[] Statistics = ["mean", "median", "p10", "p90", "rate"];
    public static readonly string[] TrendDirections = ["rising", "falling", "flat"];
    public static readonly string[] SweepModes = ["grid", "zip"];
    public static readonly string[] HarnessStops = ["first", "never"];
    public static readonly string[] EventsKept = ["first", "all", "none"];
    public static readonly string[] TickRuns = ["first", "all"];
    public static readonly string[] FilmViews = ["side", "near", "first", "third"];
    public static readonly string[] ValueForms = ["value", "count", "rate", "timeOf", "delay", "difference", "ratio", "measure", "check"];

    /// <summary>The timing fields every step takes (section 4.4).</summary>
    public static readonly StepField[] Timing =
    [
        new("at", "number", "s", "none", "starts at this second of the run; if the step before has not ended by then it starts when that one ends and the events say StepLate"),
        new("afterSeconds", "number", "s", "0", "starts this long after the step before it ends"),
        new("on", "event selector", "", "none", "starts when an event matching this selector happens (one tick after it), then delaySeconds"),
        new("delaySeconds", "number", "s", "0", "with on: how long after the event the step starts (a reaction time)"),
        new("forSeconds", "number", "s", "none", "runs at most this long; a step that has not met its goal by then ends with StepTimedOut"),
        new("label", "text", "", "none", "a name for the events (StepBegan, StepEnded) and assertions"),
    ];

    static StepField G => new("gait", "walk | run | sprint | supernatural", "", "walk", "how fast it goes (the fighting pace still caps a body whose blow swings or is held: 1.8 m/s)");

    /// <summary>The steps (section 4.4), in the design's order.</summary>
    public static readonly IReadOnlyList<StepKind> Steps =
    [
        new("wait", "number (s)", [], [], "after that many seconds", "built", "does nothing for that many seconds"),
        new("stand", "number (s)", ["move"], [], "after that many seconds", "built", "stands still (no way) for that many seconds"),
        new("face", "an actor's name, [x, y] or degrees", ["face"], [], "when facing it (or after forSeconds)", "built", "turns to face an actor, a point or a bearing (degrees counter-clockwise from +X)"),
        new("moveTo", "[x, y] or { \"actor\", \"distanceMetres\" }", ["move", "face"],
            [G, new("faceWhileMoving", "\"way\" or an actor's name", "", "way", "what it faces while it goes"), new("withinMetres", "number", "m", "0.2", "how near counts as arrived")],
            "on arrival (within withinMetres)", "built", "goes to a point, or to a distance from an actor"),
        new("keepDistance", "{ \"from\", \"metres\", \"toleranceMetres\" }", ["move", "face"], [G], "after forSeconds (needs forSeconds)", "built", "steps toward or away from an actor to hold a distance, facing it"),
        new("retreat", "{ \"from\", \"metres\" }", ["move", "face"], [G], "when that far from the actor", "built", "backpedals away from an actor, facing it (the direction pace applies: 0.70 of the gait)"),
        new("strafe", "\"left\" or \"right\"", ["move", "face"], [G, new("facing", "an actor's name", "", "its foe", "whom it faces while it goes sideways")], "after forSeconds (needs forSeconds)", "built", "goes sideways, facing an actor"),
        new("circle", "{ \"around\", \"radiusMetres\", \"direction\": \"left\" | \"right\" }", ["move", "face"], [G], "after forSeconds (needs forSeconds)", "built", "goes round an actor at a radius"),
        new("path", "[[x, y], ...]", ["move", "face"], [G, new("loop", "true | false", "", "false", "start again from the first waypoint at the last")], "at the last waypoint (never, with loop: give forSeconds)", "built", "goes through waypoints in order"),
        new("attack", "\"left\" | \"right\" | \"overhead\" | \"thrust\" | \"auto\" | \"quick\" | \"strong\"", ["attack"],
            [
                new("holdSeconds", "number", "s", "a tap (a strong blow: the strong threshold)", "how long the blow is held ready before it is released; 0 to 10"),
                new("waitReady", "true | false", "", "true", "true: the press waits (at most readySeconds) until the body may swing again; false: pressed at once, a refusal is an AttackRefused"),
                new("readySeconds", "number", "s", "2", "with waitReady: the most it waits; 0 to 10"),
                new("aimAt", "a region (\"chest\", \"leftUpperArm\"), [x, y, z], or { \"pitchDegrees\", \"yawDegrees\" }", "", "none (the blow's own pitch, level)", "where the blow is aimed: the pitch (and yaw) solved toward that region of the target at the press and while held; a request — the outputs give the region aimed and the region met"),
                new("target", "an actor's name", "", "its foe", "whom the blow and its aim are for"),
            ],
            "when the blow is released (its swing goes on)", "built", "a blow by direction, held and released as the player's attack button does"),
        new("block", "number (s), or { \"until\": event selector }", ["block"], [], "after that many seconds, or when the event happens", "built", "holds block (the guard raised)"),
        new("parry", "{ \"against\": actor, \"leadSeconds\": 0.25 }", ["block"], [], "when that actor's next blow has struck", "built", "waits for that actor's next blow and presses block that long before its strike is due"),
        new("cancel", "true", ["attack"], [], "at once", "built", "lets a held blow down (a feint)"),
        new("dodge", "\"back\" | \"left\" | \"right\" | \"forward\"", ["dodge", "move"], [], "when the dodge ends", "built", "an active dodge (DodgeIntent), begun or refused by the rules as the player's key is"),
        new("usePower", "a power's id (\"celerity\") or { \"slot\": 1 to 8 }", ["power"], [], "at once (the press)", "built", "presses the quick slot that shows that power; none does: StepRefused (NoSlot), nothing pressed; the module's refusals are its PowerRefused"),
        new("equip", "\"ranged\" | \"melee\"", ["attack"], [], "when the swap is done", "bow", "swaps the held weapon (the bow and the sidearm)"),
        new("aim", "{ \"at\": actor, \"region\" } or [x, y, z]", ["attack", "face"], [], "after forSeconds, or when the step after it starts", "bow", "the bow up, an arrow nocked, the aim held on the target"),
        new("draw", "{ \"toShare\": 0 to 1 }", ["attack"], [], "when the draw reaches that share", "bow", "attack held until the draw reaches that share of full draw"),
        new("loose", "true", ["attack"], [], "at once", "bow", "attack released: the arrow leaves at the speed the draw reached"),
        new("cancelDraw", "true", ["attack"], [], "at once", "bow", "lets the string down"),
        new("mark", "text", [], [], "at once", "built", "a Marker event with that text (for windows by marker, stops and assertions)"),
        new("repeat", "a count (1 or more) or \"untilStop\"", [], [new("steps", "a list of steps", "", "(required)", "the steps repeated")], "after its count", "built", "its steps again, in order (its channels are its steps')"),
        new("handOver", "\"ai\"", ["move", "face", "attack", "block", "dodge", "power"], [], "never (the AI drives from here)", "built", "every channel to the actor's AI from here"),
        new("end", "text (a reason)", [], [], "at once: the run ends (stop SCENARIO_END:<reason>)", "built", "ends the run"),
    ];

    /// <summary>The step by its kind field's name (exactly), or null.</summary>
    public static StepKind? Step(string name) => Steps.FirstOrDefault(s => s.Name == name);

    /// <summary>The observables (section 4.5); <c>&lt;actor&gt;</c> is an actor's name, <c>&lt;power&gt;</c> a power's id.</summary>
    public static readonly IReadOnlyList<ObservableKind> Observables =
    [
        new("time", "s", "the run's time"),
        new("<actor>.health", "Health (1 to 1000; 100 a person)", "its Health now"),
        new("<actor>.healthMax", "Health", "its Health's maximum now"),
        new("<actor>.blood", "Blood (1.1's scale, 0 to 1000)", "its Blood now"),
        new("<actor>.bloodMax", "Blood", "its Blood's maximum"),
        new("<actor>.woundsTaken", "count", "the wounds it has taken so far"),
        new("<actor>.woundsDealt", "count", "the wounds it has dealt so far"),
        new("<actor>.bloodSpent", "Blood", "the Blood it has paid so far"),
        new("<actor>.distanceTo.<actor>", "m", "the distance between the two, feet to feet across the ground (x and y)"),
        new("<actor>.speed", "m/s", "its speed over the ground"),
        new("<actor>.stopped", "true/false", "whether a harness stop has stopped it (zero Health, severed)"),
        new("<actor>.powerLive.<power>", "true/false", "whether that power of its is live"),
        new("<actor>.swings", "count", "the blows it has begun so far"),
        new("<actor>.contacts.body", "count", "its contacts that met a body so far"),
        new("<actor>.contacts.shield", "count", "its contacts that met a shield so far"),
        new("<actor>.contacts.weapon", "count", "its contacts that met a blade so far"),
        new("<actor>.contacts.level", "count", "its contacts that met the level so far"),
        new("<actor>.hits", "count", "its contacts that did harm so far"),
    ];

    /// <summary>The settable paths (section 4.9): what a sweep may set.</summary>
    public static readonly IReadOnlyList<SettableKind> Settable =
    [
        new("<actor>.sheet.values.<trait>", "whole number", "a trait's purchased value (an Attribute or an Ability by id) or a resource's (blood_pool, health_pool), set over the sheet"),
        new("<actor>.sheet.powers.<power>.rank", "whole number (0 unlearns)", "a power's rank, with the options its sheet entry names"),
        new("<actor>.ai.<number>", "number (or true/false, or the pace)", "one of the duelling brain's numbers (--describe brain)"),
        new("<actor>.gear.weapon", "a 1.1 weapon's name", "the weapon it holds (a point with another weapon loads content again)"),
        new("<actor>.gear.armor", "a list of armour pieces", "what it wears"),
        new("<actor>.health", "whole number", "its Health at the start"),
        new("<actor>.blood", "whole number", "its Blood at the start"),
        new("<actor>.place.distanceMetres", "number (m)", "a relative place's distance from its actor"),
        new("<actor>.place.bearingDegrees", "number (degrees)", "a relative place's bearing from its actor, counter-clockwise from +X"),
        new("overrides.combatTempo", "number, 0.25 to 4", "the global combat tempo"),
        new("overrides.switches.<name>", "true | false", "a game switch on or off"),
        new("overrides.data.<index>.value", "a JSON value of the kind it replaces", "a data override's value (also written /overrides/data/<index>/value)"),
        new("overrides.actorTempo.<actor>", "number, 0.25 to 4", "an actor's own combat tempo in place of the global"),
        new("overrides.clipRate.<index>.rate", "number, 0.1 to 10", "a clip rate's rate (multiplies the clip's play rate, on top of the tempo)"),
        new("light", "number, 0 to 1", "the light perception sees by, over the whole run, in place of the time's"),
        new("time", "\"night\" | \"day\"", "the light perception sees by"),
        new("contacts.<index>.<field>", "the field's kind", "a fixed contact's field (kind contact): atSeconds, meets, region, part, alongBladeShare, closingSpeedMetresPerSecond, squarenessDegrees, swing, motionRate, peakSpeedMetresPerSecond"),
        new("contacts.all.<field>", "the field's kind", "the same field of every fixed contact at once (kind contact)"),
    ];

    /// <summary>The value forms (section 4.5), with what each takes, for <c>--describe</c>.</summary>
    public static readonly (string Name, string Takes, string Meaning)[] ValueFormHelp =
    [
        ("value", "an observable path; with atEvent: a selector (and nth), or (assertions) atSeconds", "an observable's value at the run's end, at an event, or at a second"),
        ("count", "an event selector (\"sum\": a field adds that field over the matches)", "how many events matched (or the sum of a field)"),
        ("rate", "{ \"of\": selector, \"per\": selector } or { \"of\": selector, \"perSecond\": true }", "matches of one selector over another's (numerator and denominator kept), or a second over the selector's window"),
        ("timeOf", "an event selector; nth (default 1)", "the time the nth match happened (none: censored)"),
        ("delay", "{ \"from\": selector, \"to\": selector, \"pairBy\": field }", "the seconds from each from to its to (joined by the field, else the next to after each from)"),
        ("difference", "[measure, measure] (names or inline forms)", "the first less the second, per run"),
        ("ratio", "[measure, measure]", "the first over the second, per run"),
        ("measure", "a measure's name", "a named measure (the scenario's own, or a built-in one)"),
        ("check", "a built-in check's name", "a built-in check: true when it holds in the run"),
    ];

    /// <summary>The fields the scenario stream adds to events (section 4.5), by kind.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Derived = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["ContactMade"] = ["region", "side", "aimedRegion", "regionAsAimed", "weaponClass", "defence", "defenceEvidence", "blockRaisedSeconds", "blowBegunSeconds"],   // the defence
        ["HarmResolved"] = ["weaponClass", "fixed", "harmRegion", "harmSide", "healthMaxPurchased", "healthMaxEffective", "blood", "strikeEventId"],   // fixed, its contact's (ContactMade has the field itself); Health's books
        ["GuardResolved"] = ["verdict", "fixed", "defence", "defenceEvidence", "blockRaisedSeconds", "blowBegunSeconds"],
        // ----
        ["SwingEnded"] = ["outcome", "defence", "defenceEvidence", "defender", "dodgeEventId", "distanceAtBlowMetres", "distanceAtEndMetres", "attackerReachMetres", "dodgeMovedMetres"],
        ["FlurryStrike"] = ["parentContact", "recheckedAtOwnMoment", "resolution", "defence", "defenceEvidence", "outcome"],
        ["BrainDecided"] = ["caused", "causedKind", "executed"],
        ["ActorDied"] = ["harnessStop", "finalDeath"],
        // ---- end
        ["PowerUsed"] = ["power"],
        ["PowerRefused"] = ["power"],
        ["PowerEnded"] = ["power"],
        ["Marker"] = ["text"],
    };

    /// <summary>One sentence for the kinds a reader most needs (the rest: their records' fields say it).</summary>
    static readonly Dictionary<string, string> Meanings = new(StringComparer.Ordinal)
    {
        ["StepBegan"] = "a step began (Track and Step number it: depth-first in the actor's tracks; Track -1 for a trigger's step)",
        ["StepEnded"] = "a step ended (its Outcome: Done, TimedOut, Refused, Interrupted, Ended, HandedOver)",
        ["StepTimedOut"] = "a step's forSeconds ran out before its goal (why)",
        ["StepRefused"] = "a step could not be asked (NoSlot: no quick slot shows the power; NoTarget)",
        ["StepLate"] = "a step with \"at\" started late: the step before it ran over",
        ["ControlChanged"] = "the channels the script holds, the AI drives and are muted changed",
        ["Marker"] = "a mark step's text",
        ["TriggerFired"] = "a trigger fired (its index and label)",
        ["ScenarioEnded"] = "the script, a trigger or \"then\": \"end\" ended the run (stop SCENARIO_END:<reason>)",
        ["AttackBegun"] = "a blow began (its swing number, strength, direction, aim pitch)",
        ["StrikeMoment"] = "a blow reached its strike moment",
        ["ContactMade"] = "a blow (or an arrow) met something: a body, a shield, a blade or the level",
        ["HarmResolved"] = "the rules resolved a contact's harm: dice, successes, soak, wounds, Health before and after",
        ["GuardResolved"] = "a blow met a guard: held, parried, overpowered, clashed",
        ["PowerUsed"] = "a power was used (paid)",
        ["PowerRefused"] = "a power's press was refused (why)",
        ["ActorDied"] = "a harness stop (ZERO_HEALTH_STOP, SEVERED_STOP): not a death; no torpor or Final Death exists",
        // ----
        ["BrainDecided"] = "a duelling brain's decision (traced runs: outputs.decisions): what it chose (Choice), what it saw (Target, Distance, FoePhase, its own state) and why (Rule; Draw against Threshold, each defence's draw); the parent of what it caused (derived caused, executed)",
        ["SwingEnded"] = "a blow's end: derived outcome (landed, stopped at a guard or shield, dodged, missed, metLevel) and, for one that met nothing, its defence (dodge, with how it is known, or none)",
        ["FlurryStrike"] = "an extra blow of a Celerity swing: a logical strike (its own Strike id) re-meeting its swing's one contact (derived parentContact), checked at its own moment (recheckedAtOwnMoment)",
        ["BlockRaised"] = "a block raised (a brain's Block decision is its parent, in traced runs)",
        // The goals work
        ["MoveToRouted"] = "a moveTo's straight line met the level, so it walks the navigation mesh's route round it: the corners (Path, x,y;x,y), the route's and the straight line's length, where the line met the level (BlockedAt); again each time the route is asked again (Asked)",
        ["MoveToFailed"] = "a moveTo did not get there (Why: NoRoute, the line meets the level and the mesh has no way; Blocked, the body stopped moving twice over with no other way; TimedOut, its forSeconds passed): the goal, where the body is, the nearest it came, how far it walked, the path it actually walked (Walked, x,y;x,y every quarter second) and the route it had (Route), with the reason in words",
        ["ObjectiveMet"] = "an objective met (Objective: its name; Kind: defeat, survive, reach, escape, protect, hold; Who: its actor, or Side), written by the runner (objectives measure: the world is unchanged); its parentEventId the event that decided it (an ActorDied: a harness stop, not a death), with the reason in words",
        ["ObjectiveFailed"] = "an objective failed: as ObjectiveMet (a deadline passed undone, its owner at a harness stop, the protected at one, the area left)",
    };

    // ---- the events

    /// <summary>
    /// The event records the scenario stream carries: the results trace's <see cref="EventTrace.Kinds"/> (the trace's kinds, the five it
    /// left out, the director's and, with the bow, the bow's), so the description is the stream's own list.
    /// </summary>
    public static IReadOnlyList<Type> EventTypes => EventTrace.Kinds;   // The results work's stream (VITOMIR_EVENT_TRACE_2): the one list

    /// <summary>
    /// The kinds that come with the bow, by name: a selector may name them before
    /// their records are in this build (they are then listed as <c>coming</c>); once a record of that name is in the platform it
    /// is described from the record.
    /// </summary>
    public static readonly (string Name, string Lane, string Meaning)[] Coming =
    [
        ("ArrowNocked", "bow", "an arrow nocked"),
        ("DrawBegan", "bow", "a draw began"),
        ("ArrowLoosed", "bow", "an arrow loosed (its speed, the draw share)"),
        ("ArrowSpent", "bow", "an arrow came to rest without meeting anyone"),
        ("DrawCancelled", "bow", "a draw let down"),
    ];

    static readonly string[] ByFields = ["Attacker", "By", "Killer", "Entity", "User", "Archer"];
    static readonly string[] OnFields = ["Target", "Defender", "Whom"];

    /// <summary>Every event kind, described: the records by reflection (fields, types, which name actors, by and on), then the kinds still to come.</summary>
    public static IReadOnlyList<EventKind> Events()
    {
        var list = new List<EventKind>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in EventTypes.Concat(ComingTypes()))
        {
            if (!seen.Add(t.Name)) continue;
            list.Add(Describe(t));
        }
        foreach (var (name, lane, meaning) in Coming)
            if (!seen.Contains(name)) list.Add(new EventKind(name, [], null, null, Derived.TryGetValue(name, out var d) ? d : [], lane == "bow" ? "bow" : "coming", meaning));
        return list;
    }

    /// <summary>The coming kinds whose records are already in the platform (after the director's and the bow's merges).</summary>
    static IEnumerable<Type> ComingTypes()
    {
        var asm = typeof(ContactMade).Assembly;
        foreach (var (name, _, _) in Coming)
        {
            var t = asm.GetTypes().FirstOrDefault(x => x.Name == name && x.IsValueType && x.Namespace?.StartsWith("Vitomir.Platform", StringComparison.Ordinal) == true);
            if (t is not null) yield return t;
        }
    }

    /// <summary>An event record described by reflection: its constructor's parameters in order (the record's fields), each with its type and whether it names an actor.</summary>
    public static EventKind Describe(Type t)
    {
        var ctor = t.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        var names = ctor.GetParameters().Select(p => p.Name!).ToList();
        var types = ctor.GetParameters().Select(p => p.ParameterType).ToList();
        var entities = names.Where((n, i) => types[i] == typeof(EntityId)).ToList();
        string? by = ByFields.FirstOrDefault(entities.Contains);
        string? on = OnFields.FirstOrDefault(entities.Contains);
        if (entities.Contains("Who"))
        {
            if (by is null) by = "Who";
            else on ??= "Who";
        }
        if (on is null && entities.Contains("Threat")) on = "Threat";
        var fields = new List<EventField>();
        for (int i = 0; i < names.Count; i++)
            fields.Add(new EventField(names[i], TypeName(types[i]), types[i] == typeof(EntityId) ? (names[i] == by ? "by" : names[i] == on ? "on" : "actor") : null));
        string[] derived = Derived.TryGetValue(t.Name, out var d) ? d : [];
        string meaning = Meanings.TryGetValue(t.Name, out var m) ? m : Coming.FirstOrDefault(c => c.Name == t.Name).Meaning ?? "";
        return new EventKind(t.Name, fields, by, on, derived, "built", meaning);
    }

    /// <summary>A field's type as the stream writes it: number, whole number, true/false, text, an actor, [x, y, z], or an enum's words.</summary>
    public static string TypeName(Type t)
    {
        var under = Nullable.GetUnderlyingType(t) ?? t;
        if (under == typeof(EntityId)) return "actor";
        if (under == typeof(bool)) return "true/false";
        if (under == typeof(string)) return "text";
        if (under == typeof(float) || under == typeof(double)) return "number";
        if (under == typeof(int) || under == typeof(long) || under == typeof(short) || under == typeof(byte) || under == typeof(uint)) return "whole number";
        if (under == typeof(Vector3)) return "[x, y, z]";
        if (under == typeof(Vector2)) return "[x, y]";
        if (under.IsEnum) return string.Join(" | ", Enum.GetNames(under));
        if (under.IsValueType && under.Namespace?.StartsWith("Vitomir.Platform", StringComparison.Ordinal) == true) return $"{under.Name} (an object)";
        return under.Name;
    }

    /// <summary>The fields a selector's <c>where</c> may filter an event on (case aside): its record's fields and the stream's derived ones.</summary>
    public static IReadOnlyList<string> WhereFields(EventKind e) => [.. e.Fields.Select(f => f.Name), .. e.Derived];

    /// <summary>A record's members by name (for the tests: a record's field that the description must carry).</summary>
    internal static IEnumerable<string> RecordFields(Type t) => t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name);
}
