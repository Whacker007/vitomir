// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using System.Reflection;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Host.Arena;

namespace Vitomir.Platform.Host.Scenario;

/// <summary>
/// One event of a scenario run as the measures see it: its kind (the record's name), its tick and seconds, the
/// actor who did it (<see cref="By"/>) and the actor it was done to (<see cref="On"/>) by NAME, and its fields by name (matched
/// without regard to case): numbers as doubles, enumerations as their names, true/false, text, actors by name; plus the derived
/// fields the design names (section 4.5): <c>region</c> and <c>side</c> on a body contact, <c>aimedRegion</c> and
/// <c>regionAsAimed</c> on a contact of an aimed blow, <c>power</c> on the powers' events, <c>verdict</c> on the guard's.
/// </summary>
public sealed class ScenarioEvent
{
    public required string Kind { get; init; }
    public required long Tick { get; init; }
    public double Seconds => Tick / (double)World.TicksPerSecond;
    public string? By { get; init; }
    public string? On { get; init; }
    public required Dictionary<string, object?> Fields { get; init; }
    /// <summary>The record as published.</summary>
    public required object Raw { get; init; }
    /// <summary>Its id in the run (its place in the run's stream, 0 up): stable for the same scenario, point and seed.</summary>
    public int Id { get; init; }
    /// <summary>The event it follows from (a contact's blow, a harm's or a verdict's contact, a power's end's use, a step's end's beginning, an arrow's end's loosing), or null.</summary>
    public int? Parent { get; init; }
    /// <summary>Each field that is a person, by its name (<c>Attacker</c> → <c>Christof</c>).</summary>
    public IReadOnlyDictionary<string, string>? Actors { get; init; }

    /// <summary>A field's value (a derived one too), or null when the event has none.</summary>
    public object? Field(string name) => Fields.TryGetValue(name, out var v) ? v : null;

    /// <summary>A field's value as a number (true 1, false 0), or null.</summary>
    public double? Number(string name) => Field(name) switch
    {
        double d => d,
        bool b => b ? 1 : 0,
        string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) => d,
        _ => null,
    };
}

/// <summary>
/// The events of one scenario run, collected tick by tick after each step from the world's queues (every kind of
/// <see cref="EventTrace.Kinds"/>), with the actors' names, the markers' times (for windows) and the derived fields; and the
/// selectors' matching (section 4.5). Reads only the world: a run with the log is the run without it, digest for digest.
/// </summary>
public sealed partial class ScenarioEventLog
{
    readonly World world;
    readonly Dictionary<EntityId, string> names;
    readonly List<Action<World, ScenarioEventLog>> sources;
    readonly List<ScenarioEvent> events = [];
    readonly Dictionary<string, long> markers = new(StringComparer.Ordinal);
    // regionAsAimed: each actor's swings an aimed step released, by (actor, swing) to the region asked ("chest", "upperArm left").
    readonly Dictionary<(string Actor, int Swing), string> aimed = [];
    // Parents: the event a later one follows from (by the fields that join them).
    readonly Dictionary<(string?, double), int> swingBegun = [], arrowLoosed = [], powerUse = [];
    readonly Dictionary<double, int> contactMade = [], stepInstance = [];
    readonly Dictionary<double, int> fixedPlaced = [];   // a fixed contact's placing, by its contact id
    // An arrow's aim (the bow work's ArrowLoosed: the region asked, as BodyRegion's name, and the body aimed at), by (archer, shot).
    readonly Dictionary<(string?, double), (string? Region, string? Target)> arrowAim = [];
    readonly Dictionary<(string?, double, double), int> stepBegan = [];
    readonly Dictionary<EntityId, (float Swing, float Recovery, float Rise)> tempoSeen = [];

    /// <summary>The events so far, in the order the world published them (kinds in <see cref="EventTrace.Kinds"/>' order within a tick).</summary>
    public IReadOnlyList<ScenarioEvent> Events => events;

    /// <summary>How many events the last <see cref="Collect"/> added (they are the last of <see cref="Events"/>).</summary>
    public int Added { get; private set; }

    /// <summary>The first <see cref="Marker"/> of each text, by its tick.</summary>
    public IReadOnlyDictionary<string, long> Markers => markers;

    /// <summary>The reason a program ended the run (<see cref="ScenarioEnded"/>), or null.</summary>
    public string? Ended { get; private set; }

    readonly List<string> order;

    /// <param name="names">The cast's people by name, in the cast's order (their ids are <c>a0</c>, <c>a1</c>, … in it).</param>
    public ScenarioEventLog(World world, IReadOnlyDictionary<EntityId, string> names)
    {
        this.world = world;
        this.names = new Dictionary<EntityId, string>(names);
        order = [.. names.Values];
        sources = Sources;
    }

    /// <summary>An actor's id in the streams and tables (<c>a0</c>: its place in the cast), or its name when it is not in the cast.</summary>
    public string ActorId(string name) { int i = order.IndexOf(name); return i >= 0 ? $"a{i.ToString(CultureInfo.InvariantCulture)}" : name; }

    /// <summary>An actor's name (null for none; <c>e3</c> for a person not in the cast).</summary>
    public string? NameOf(EntityId e) => e.IsNone ? null : names.TryGetValue(e, out var n) ? n : string.Create(CultureInfo.InvariantCulture, $"e{e.Index}");

    /// <summary>The swing an aimed step released, and the region it asked (called by whoever knows: the director's events, a test).</summary>
    public void Aimed(string actor, int swing, string region) => aimed[(actor, swing)] = region;

    /// <summary>Collects the tick just stepped (call after <c>Session.Step</c>).</summary>
    public void Collect()
    {
        int before = events.Count;
        foreach (var s in sources) s(world, this);
        Tempos();
        Added = events.Count - before;
    }

    /// <summary>A fighter's tempo when it is first set and whenever it changes (as trace 1 writes it: kind <c>CombatTempo</c>).</summary>
    void Tempos()
    {
        if (!world.TryStore<CombatTempo>(out var store)) return;
        var ents = store.Entities;
        var data = store.Data;
        for (int i = 0; i < ents.Length; i++)
        {
            var now = (data[i].Swing, data[i].Recovery, data[i].Rise);
            if (tempoSeen.TryGetValue(ents[i], out var was) && was == now) continue;
            tempoSeen[ents[i]] = now;
            var raw = new TempoLine(ents[i], world.Tick - 1, now.Swing, now.Recovery, now.Rise);
            Add(Shape.Of(typeof(TempoLine)), raw, world.Tick - 1);
        }
    }

    /// <summary>A fighter's tempo as the stream writes it (kind <c>CombatTempo</c>; not a world event).</summary>
    public readonly record struct TempoLine(EntityId Entity, long Tick, float Swing, float Recovery, float Rise);

    // ---- the kinds, read by reflection once ----

    static readonly List<Action<World, ScenarioEventLog>> Sources = MakeSources();

    static List<Action<World, ScenarioEventLog>> MakeSources()
    {
        var list = new List<Action<World, ScenarioEventLog>>();
        var make = typeof(ScenarioEventLog).GetMethod(nameof(Source), BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var t in EventTrace.Kinds) list.Add((Action<World, ScenarioEventLog>)make.MakeGenericMethod(t).Invoke(null, null)!);
        return list;
    }

    static Action<World, ScenarioEventLog> Source<T>() where T : struct
    {
        var shape = Shape.Of(typeof(T));
        return (world, log) =>
        {
            if (!world.TryService<EventQueue<T>>(out var q)) return;
            var list = q.ThisTick;
            for (int i = 0; i < list.Count; i++) log.Add(shape, list[i], world.Tick - 1);
        };
    }

    /// <summary>A record's fields, the getters, and which is its by and its on (NOTE-format-002).</summary>
    internal sealed class Shape
    {
        public required string Kind;
        public required (string Name, Func<object, object?> Get, Type Type)[] Members;
        public string? ByField, OnField;

        static readonly Dictionary<Type, Shape> Cache = [];
        static readonly string[] ByNames = ["Attacker", "Entity", "By", "Killer", "User", "Archer"];
        static readonly string[] OnNames = ["Target", "Defender", "Who", "Threat"];

        public static Shape Of(Type t)
        {
            lock (Cache)
            {
                if (Cache.TryGetValue(t, out var s)) return s;
                var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0 && p.Name != "EqualityContract").ToArray();
                var members = props.Select(p => (p.Name, (Func<object, object?>)p.GetValue, p.PropertyType)).ToArray();
                bool IsActor(string n) => props.Any(p => p.Name == n && p.PropertyType == typeof(EntityId));
                string? by = ByNames.FirstOrDefault(IsActor);
                if (by is null && IsActor("Who")) by = "Who";
                string? on = OnNames.FirstOrDefault(n => IsActor(n) && n != by);
                s = new Shape { Kind = t == typeof(TempoLine) ? "CombatTempo" : t.Name, Members = members, ByField = by, OnField = on };
                Cache[t] = s;
                return s;
            }
        }
    }

    /// <summary>A record the runner itself writes into the run's stream (an objective's result), at <paramref name="tick"/>, following from the event <paramref name="parent"/> (an id of this log; −1 none).</summary>
    public void Note<T>(in T e, long tick, int parent) where T : struct => Add(Shape.Of(typeof(T)), e, tick, parent);

    void Add<T>(Shape shape, in T e, long tick, int? forcedParent = null) where T : struct
    {
        object boxed = e;
        var fields = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string>? actorsOf = null;
        string? by = null, on = null;
        foreach (var (name, get, type) in shape.Members)
        {
            object? v = get(boxed);
            if (v is EntityId who && !who.IsNone && NameOf(who) is { } whoName) (actorsOf ??= new(StringComparer.Ordinal))[name] = whoName;
            object? value = v switch
            {
                EntityId person => NameOf(person),
                Enum en => en.ToString(),
                bool b => b,
                string s => s,
                Vector3 or Vector2 => null,
                byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => Convert.ToDouble(v, CultureInfo.InvariantCulture),
                null => null,
                _ => null,                       // a nested record (FlurryStrike.Contact): not a filterable field
            };
            if (value is null && v is not null && v is not EntityId) continue;
            fields[name] = value;
            if (name == shape.ByField) by = value as string;
            if (name == shape.OnField) on = value as string;
        }
        long t = fields.TryGetValue("Tick", out var tk) && tk is double dt ? (long)dt : tick;
        // The aims the stream's derived fields need: the director's AimedBlow (a swing an aimed step pressed: the words asked) and the
        // bow's ArrowLoosed (a shot: the region aimed, the body aimed at), by name so that this reads them whatever branch has them.
        if (shape.Kind == "AimedBlow" && by is not null && fields.TryGetValue("Swing", out var sw) && sw is double swing && fields.TryGetValue("Asked", out var asked) && asked is string a && a.Length > 0)
            aimed[(by, (int)swing)] = a;
        if (shape.Kind == "ArrowLoosed" && fields.TryGetValue("Shot", out var shotV) && shotV is double shotN)
            arrowAim[(by, shotN)] = (fields.TryGetValue("AimedRegion", out var ar) ? ar as string : null, fields.TryGetValue("Target", out var at) ? at as string : null);
        Derive(shape.Kind, boxed, fields, by);
        int id = events.Count;
        Receipt(shape.Kind, boxed, fields, by, id, t);   // the defences, the decisions, Health's books (ScenarioEventLog.Receipts.cs)
        int? parent = Link(shape.Kind, fields, by, id);
        if (forcedParent is { } fp) parent = fp >= 0 && fp < id ? fp : null;   // the runner's own records name their cause
        var ev = new ScenarioEvent { Kind = shape.Kind, Tick = t, By = by, On = on, Fields = fields, Raw = boxed, Id = id, Parent = parent, Actors = actorsOf };
        events.Add(ev);
        if (boxed is Marker m && !markers.ContainsKey(m.Text)) markers[m.Text] = t;
        if (boxed is ScenarioEnded se && Ended is null) Ended = se.Reason;
    }

    /// <summary>
    /// The event this one follows from (<c>parentEventId</c>), and this one remembered for those that follow from it: a blow's
    /// strike, release, switch, cancel and contacts from its <c>AttackBegun</c> (by actor and swing); an arrow's contact and end from
    /// its <c>ArrowLoosed</c> (by archer and shot); a harm, a guard's verdict, a stagger, a severance, a protest, a stop from its
    /// contact (by contact id); a power's end from its use (by actor and action id); a step's end from its beginning.
    /// </summary>
    int? Link(string kind, Dictionary<string, object?> f, string? by, int id)
    {
        double D(string n) => f.TryGetValue(n, out var v) && v is double d ? d : -1;
        int? Find<TK>(Dictionary<TK, int> map, TK key) where TK : notnull => map.TryGetValue(key, out int p) ? p : null;
        int? decided = Decided(kind, f, by, id);   // the brain's decision that caused it (only in a run with decisions)
        switch (kind)
        {
            case "BrainDecided": return DecisionParent(f);   // a defence's decision follows from the foe's blow
            case "AttackBegun": swingBegun[(by, D("Swing"))] = id; return decided;
            case "ArrowLoosed": arrowLoosed[(by, D("Shot"))] = id; return null;
            case "PowerUsed": powerUse[(by, D("ActionId"))] = id; return decided;
            case "StepBegan":
                stepBegan[(by, D("Track"), D("Step"))] = id;
                if (D("Instance") > 0) stepInstance[D("Instance")] = id;
                return D("Parent") > 0 ? Find(stepInstance, D("Parent")) : null;
            case "AimedBlow":
            case "StepRefused":
            case "StepLate":
                return D("Instance") > 0 ? Find(stepInstance, D("Instance")) : Find(stepBegan, (by, D("Track"), D("Step")));
            case "StrikeMoment": case "AttackReleased": case "AttackSwitched": case "SwingCancelled": case "SwingEnded": case "SwingFollowThrough":
                return Find(swingBegun, (by, D("Swing")));
            case "FixedContactPlaced": if (D("ContactId") > 0) fixedPlaced[D("ContactId")] = id; return null;
            case "ContactMade":
                contactMade[D("ContactId")] = id;
                if (f.TryGetValue("Fixed", out var fx) && fx is true) return Find(fixedPlaced, D("ContactId"));   // placed, not swung
                return D("Shot") > 0 ? Find(arrowLoosed, (by, D("Shot"))) : Find(swingBegun, (by, D("Swing")));
            case "ArrowSpent": return Find(arrowLoosed, (by, D("Shot")));
            case "PowerEnded": return Find(powerUse, (by, D("ActionId")));
            case "MoveToRouted": case "MoveToFailed":   // a moveTo's route and failure follow from its StepBegan
                return D("Instance") > 0 ? Find(stepInstance, D("Instance")) : null;
            case "StepEnded": case "StepTimedOut":
                return D("Instance") > 0 ? Find(stepInstance, D("Instance")) : Find(stepBegan, (by, D("Track"), D("Step")));
        }
        if (decided is not null) return decided;   // a block raised, a parry, a dodge, a refusal its decision caused
        return f.ContainsKey("ContactId") && D("ContactId") > 0 ? Find(contactMade, D("ContactId")) : null;
    }

    /// <summary>The derived fields (section 4.5).</summary>
    void Derive(string kind, object e, Dictionary<string, object?> fields, string? by)
    {
        // weaponClass (4.5): "arrow" for a projectile's contact (the bow work's ContactMade.Shot above 0), else "melee" (this build names no
        // finer class for a held weapon); a harm or a verdict takes its contact's.
        if (kind == "ContactMade") fields["weaponClass"] = fields.TryGetValue("Shot", out var shot) && shot is double sh && sh > 0 ? "arrow" : "melee";
        else if ((kind == "HarmResolved" || kind == "GuardResolved") && fields.TryGetValue("ContactId", out var cid) && cid is double cd
                 && contactMade.TryGetValue(cd, out int cIdx) && events[cIdx].Fields.TryGetValue("weaponClass", out var wc))
        {
            fields["weaponClass"] = wc;
            fields["fixed"] = events[cIdx].Fields.TryGetValue("Fixed", out var fx) && fx is true;   // its contact's
        }
        switch (e)
        {
            case ContactMade c when fields.TryGetValue("Shot", out var shotF) && shotF is double shot && shot > 0:
                // An arrow's contact: its aim from its ArrowLoosed (BodyRegion's name, "LeftUpperArm"), met when the body and the region are the ones aimed at.
                if (c.Met == ContactMet.Body && RegionOf(c.Site) is { } ra) { fields["region"] = ra.Region; fields["side"] = ra.Side; }
                if (arrowAim.TryGetValue((by, shot), out var aim) && aim.Region is { } enumName && enumName != "None")
                {
                    string askedWords = EnumRegionWords(enumName);
                    fields["aimedRegion"] = askedWords;
                    fields["regionAsAimed"] = c.Met == ContactMet.Body && RegionOf(c.Site) is { } rr && string.Equals(fields.GetValueOrDefault("Target") as string, aim.Target, StringComparison.Ordinal)
                        && RegionMatches(askedWords, rr.Region, rr.Side);
                }
                break;
            case ContactMade c:
                if (c.Met == ContactMet.Body && RegionOf(c.Site) is { } r)
                {
                    fields["region"] = r.Region;
                    fields["side"] = r.Side;
                    if (by is not null && aimed.TryGetValue((by, c.Swing), out var asked))
                    {
                        fields["aimedRegion"] = asked;
                        fields["regionAsAimed"] = RegionMatches(asked, r.Region, r.Side);
                    }
                }
                else if (by is not null && aimed.TryGetValue((by, c.Swing), out var asked2))
                {
                    fields["aimedRegion"] = asked2;
                    fields["regionAsAimed"] = false;
                }
                break;
            case GuardResolved g:
                fields["verdict"] = g.Verdict.ToString().ToLowerInvariant();
                break;
            case Powers.PowerUsed u:
                fields["power"] = FighterTally.PowerOf(u.Power);
                break;
            case Powers.PowerRefused pr:
                fields["power"] = pr.Power is null ? null : FighterTally.PowerOf(pr.Power);
                break;
            case Powers.PowerEnded pe:
                fields["power"] = FighterTally.PowerOf(pe.Power);
                break;
            case Marker mk:
                fields["text"] = mk.Text;
                break;
        }
    }

    /// <summary>
    /// A body region as <c>aimAt</c> names it from a contact's site (<c>"left upper arm"</c> → <c>upperArm</c>, <c>left</c>;
    /// <c>"chest"</c> → <c>chest</c>, no side); null for a site that is no body region.
    /// </summary>
    public static (string Region, string? Side)? RegionOf(string? site)
    {
        if (string.IsNullOrEmpty(site)) return null;
        string s = site.Trim().ToLowerInvariant();
        string? side = null;
        if (s.StartsWith("left ", StringComparison.Ordinal)) { side = "left"; s = s[5..]; }
        else if (s.StartsWith("right ", StringComparison.Ordinal)) { side = "right"; s = s[6..]; }
        string? region = s switch
        {
            "head" => "head", "neck" => "neck", "chest" => "chest", "belly" => "belly", "hips" => "hips",
            "upper arm" => "upperArm", "forearm" => "forearm", "hand" => "hand", "thigh" => "thigh", "shin" => "shin", "foot" => "foot",
            _ => null,
        };
        return region is null ? null : (region, side);
    }

    /// <summary>A <see cref="BodyRegion"/>'s name as <c>aimAt</c> words (<c>LeftUpperArm</c> → <c>upperArm left</c>; <c>Chest</c> → <c>chest</c>).</summary>
    public static string EnumRegionWords(string enumName)
    {
        if (!Enum.TryParse<BodyRegion>(enumName, out var region) || region == BodyRegion.None) return enumName;
        return RegionOf(BodyRegions.Name(region)) is { } r ? (r.Side is null ? r.Region : $"{r.Region} {r.Side}") : enumName;
    }

    /// <summary>Whether the region met is the region asked (<c>"upperArm"</c> or <c>"upperArm left"</c>; a region asked without a side matches either side).</summary>
    public static bool RegionMatches(string asked, string region, string? side)
    {
        // The words in either order: "upperArm left", "left upperArm", "chest".
        string? r = null, sd = null;
        foreach (var w in asked.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (w.Equals("left", StringComparison.OrdinalIgnoreCase) || w.Equals("right", StringComparison.OrdinalIgnoreCase)) sd = w;
            else r = w;
        }
        if (r is null || !string.Equals(r, region, StringComparison.OrdinalIgnoreCase)) return false;
        return sd is null || string.Equals(sd, side, StringComparison.OrdinalIgnoreCase);
    }

    // ---- selectors (section 4.5) ----

    /// <summary>Whether an event matches a selector's kind, actors and fields (its window apart: <see cref="InWindow"/>).</summary>
    public static bool Matches(EventSelector s, ScenarioEvent e)
    {
        if (!string.Equals(s.Event, e.Kind, StringComparison.OrdinalIgnoreCase)) return false;
        if (s.By is not null && !string.Equals(s.By, e.By, StringComparison.Ordinal)) return false;
        if (s.On is not null && !string.Equals(s.On, e.On, StringComparison.Ordinal)) return false;
        if (s.Where is { } where)
            foreach (var f in where)
            {
                if (!e.Fields.TryGetValue(f.Field, out var v)) return false;
                bool any = false;
                foreach (var want in f.Values) if (ValueEquals(v, want)) { any = true; break; }
                if (!any) return false;
            }
        return true;
    }

    /// <summary>A field's value against a selector's value as text: numbers by value, true/false and names without regard to case.</summary>
    public static bool ValueEquals(object? v, string want) => v switch
    {
        null => string.Equals(want, "null", StringComparison.OrdinalIgnoreCase) || want.Length == 0,
        double d => double.TryParse(want, NumberStyles.Float, CultureInfo.InvariantCulture, out double w) && Math.Abs(d - w) <= 1e-9 * Math.Max(1, Math.Abs(w)),
        bool b => bool.TryParse(want, out bool w) && w == b,
        string s => string.Equals(s, want, StringComparison.OrdinalIgnoreCase),
        _ => string.Equals(Convert.ToString(v, CultureInfo.InvariantCulture), want, StringComparison.OrdinalIgnoreCase),
    };

    /// <summary>A selector's window as ticks: [from, to), to null for the run's end; null when the window has not opened (a <c>fromMarker</c> never reached).</summary>
    public (long From, long? To)? Window(EventSelector s)
    {
        long from = 0;
        long? to = null;
        if (s.FromSeconds is { } fs) from = Math.Max(from, (long)Math.Round(fs * World.TicksPerSecond));
        if (s.ToSeconds is { } ts) to = (long)Math.Round(ts * World.TicksPerSecond);
        if (s.FromMarker is { } fm)
        {
            if (!markers.TryGetValue(fm, out long at)) return null;
            from = Math.Max(from, at);
        }
        if (s.ToMarker is { } tm && markers.TryGetValue(tm, out long end)) to = to is { } t0 ? Math.Min(t0, end) : end;
        return (from, to);
    }

    /// <summary>Whether an event lies in a selector's window.</summary>
    public bool InWindow(EventSelector s, ScenarioEvent e)
    {
        if (s.FromSeconds is null && s.ToSeconds is null && s.FromMarker is null && s.ToMarker is null) return true;
        if (Window(s) is not { } w) return false;
        return e.Tick >= w.From && (w.To is not { } to || e.Tick < to);
    }

    /// <summary>The events matching a selector, in its window, in order.</summary>
    public IEnumerable<ScenarioEvent> Select(EventSelector s)
    {
        foreach (var e in events) if (Matches(s, e) && InWindow(s, e)) yield return e;
    }
}
