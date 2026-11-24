// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Host.Arena;
using Vitomir.Platform.Powers;

namespace Vitomir.Platform.Host.Scenario;

/// <summary>
/// One scenario run's measures: the observables at a moment, the value forms
/// over the run's events (<see cref="ScenarioEventLog"/>), the built-in measures (from the arena's <see cref="FighterTally"/>, which
/// <see cref="BoutTally"/> keeps beside it), the built-in checks, and the stop's conditions after each tick. Reads the world and the
/// events only.
/// </summary>
public sealed class ScenarioTally
{
    readonly World world;
    readonly IReadOnlyList<EntityId> actors;
    readonly IReadOnlyList<string> names;
    readonly BoutResult result;
    readonly ScenarioEventLog log;
    readonly ScenarioMeasuresModel model;
    readonly Dictionary<string, MeasureSpec> named;
    // Probes: an observable at a set tick, and at the n-th match of a selector (sampled at the end of that tick).
    readonly Dictionary<ObservableForm, (long Tick, double? Value, bool Taken)> atTick = [];
    readonly Dictionary<ObservableAtEventForm, (int Seen, double? Value, bool Taken)> atEvent = [];
    readonly bool[] liveCondition;
    readonly RunValue?[] lastCondition;
    long now;

    public ScenarioTally(World world, IReadOnlyList<EntityId> actors, IReadOnlyList<string> names, BoutResult result, ScenarioEventLog log, ScenarioMeasuresModel model)
    {
        this.world = world;
        this.actors = actors;
        this.names = names;
        this.result = result;
        this.log = log;
        this.model = model;
        named = model.Measures.ToDictionary(m => m.Name, StringComparer.Ordinal);
        foreach (var f in AllForms()) Register(f);
        var when = model.Stop.When ?? [];
        liveCondition = [.. when.Select(c => ReadsLive(c.Value))];
        lastCondition = new RunValue?[when.Count];
        now = world.Tick;
        Probe();   // a probe at 0 s
    }

    /// <summary>The run's ticks so far.</summary>
    public long Now => now;

    /// <summary>Every value form the scenario names (measures, assertions, stop conditions), nested ones included.</summary>
    IEnumerable<ValueForm> AllForms()
    {
        foreach (var m in model.Measures) foreach (var f in Walk(m.Form)) yield return f;
        foreach (var a in model.Assertions) foreach (var f in Walk(a.Value)) yield return f;
        foreach (var c in model.Stop.When ?? []) foreach (var f in Walk(c.Value)) yield return f;
    }

    static IEnumerable<ValueForm> Walk(ValueForm f)
    {
        yield return f;
        if (f is DifferenceForm d) { foreach (var x in Walk(d.Left)) yield return x; foreach (var x in Walk(d.Right)) yield return x; }
        if (f is RatioForm r) { foreach (var x in Walk(r.Numerator)) yield return x; foreach (var x in Walk(r.Denominator)) yield return x; }
    }

    void Register(ValueForm f)
    {
        if (f is ObservableForm { AtSeconds: { } s } o) atTick.TryAdd(o, ((long)Math.Round(s * World.TicksPerSecond), null, false));
        if (f is ObservableAtEventForm e) atEvent.TryAdd(e, (0, null, false));
    }

    /// <summary>Whether a form reads the world as it is now (an observable without a moment, a built-in measure), so a stop condition is checked every tick, not only on ticks with events.</summary>
    bool ReadsLive(ValueForm f) => f switch
    {
        ObservableForm { AtSeconds: null } => true,
        DifferenceForm d => ReadsLive(d.Left) || ReadsLive(d.Right),
        RatioForm r => ReadsLive(r.Numerator) || ReadsLive(r.Denominator),
        RateForm { PerSecond: true } => true,
        MeasureRefForm m => !named.TryGetValue(m.Measure, out var spec) || ReadsLive(spec.Form),
        _ => false,
    };

    /// <summary>Collects the tick just stepped (after the event log's and the arena's tally's): the probes due.</summary>
    public void Collect()
    {
        now = world.Tick;
        Probe();
        if (log.Added == 0 || atEvent.Count == 0) return;
        int first = log.Events.Count - log.Added;
        foreach (var form in atEvent.Keys.ToList())
        {
            var (seen, value, taken) = atEvent[form];
            if (taken) continue;
            for (int i = first; i < log.Events.Count; i++)
            {
                var e = log.Events[i];
                if (!ScenarioEventLog.Matches(form.AtEvent, e) || !log.InWindow(form.AtEvent, e)) continue;
                if (++seen == form.Nth) { value = Observable(form.Observable); taken = true; break; }
            }
            atEvent[form] = (seen, value, taken);
        }
    }

    void Probe()
    {
        if (atTick.Count == 0) return;
        foreach (var form in atTick.Keys.ToList())
        {
            var p = atTick[form];
            if (!p.Taken && p.Tick == now) atTick[form] = (p.Tick, Observable(form.Observable), true);
        }
    }

    /// <summary>
    /// The stop's conditions after this tick: <c>CONDITION:&lt;index&gt;</c> for the first that holds, or null. A condition that reads
    /// only events is checked on ticks that brought events (its value cannot change otherwise).
    /// </summary>
    public string? StopNow()
    {
        var when = model.Stop.When;
        if (when is null) return null;
        for (int i = 0; i < when.Count; i++)
        {
            if (!liveCondition[i] && log.Added == 0 && lastCondition[i] is not null) continue;
            var v = Evaluate(when[i].Value, live: true);
            lastCondition[i] = v;
            if (Holds(when[i].Compare, v.Value) == true) return StopNames.ConditionPrefix + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        return null;
    }

    /// <summary>Whether a value meets a comparison (null: no value).</summary>
    public static bool? Holds(Comparison c, double? value)
    {
        if (value is not { } v || double.IsNaN(v)) return null;
        const double eps = 1e-9;
        bool Eq(double a, double b) => Math.Abs(a - b) <= eps * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b)));
        return c.Op switch
        {
            CompareOp.Below => v < c.Value && !Eq(v, c.Value),
            CompareOp.AtMost => v <= c.Value || Eq(v, c.Value),
            CompareOp.Above => v > c.Value && !Eq(v, c.Value),
            CompareOp.AtLeast => v >= c.Value || Eq(v, c.Value),
            CompareOp.Equals => Eq(v, c.Value),
            CompareOp.Between => (v >= c.Value || Eq(v, c.Value)) && (v <= c.High || Eq(v, c.High)),
            CompareOp.IsTrue => !Eq(v, 0),
            CompareOp.IsFalse => Eq(v, 0),
            _ => null,
        };
    }

    // ---- observables (section 4.5) ----

    int IndexOf(string actor)
    {
        for (int i = 0; i < names.Count; i++) if (string.Equals(names[i], actor, StringComparison.Ordinal)) return i;
        return -1;
    }

    /// <summary>An observable's value now (<c>time</c>, <c>Guard.health</c>, <c>Christof.distanceTo.Guard</c>, …), or null when it has none.</summary>
    public double? Observable(string path)
    {
        if (path == "time") return now / (double)World.TicksPerSecond;
        int dot = path.IndexOf('.');
        if (dot <= 0) return null;
        int a = IndexOf(path[..dot]);
        if (a < 0) return null;
        string rest = path[(dot + 1)..];
        var e = actors[a];
        var f = result.Fighters[a];
        switch (rest)
        {
            case "health": case "healthMax": case "blood": case "bloodMax":
                if (!world.TryStore<Vitals>(out var vitals)) return null;
                ref var v = ref vitals.Find(e);
                if (System.Runtime.CompilerServices.Unsafe.IsNullRef(ref v)) return null;
                return rest switch
                {
                    "health" => v.HealthMax > 0 ? Math.Round(v.Health) : null,
                    "healthMax" => v.HealthMax > 0 ? Math.Round(v.HealthMax) : null,
                    "blood" => v.HasBlood ? Math.Round(v.Blood) : null,
                    _ => v.HasBlood ? Math.Round(v.BloodMax) : null,
                };
            case "woundsTaken": return f.WoundsTaken;
            case "woundsDealt": return f.WoundsDealt;
            case "bloodSpent": return f.BloodSpent;
            case "swings": return f.Swings;
            case "hits": return f.Hits;
            case "contacts.body": return f.ContactsBody;
            case "contacts.shield": return f.ContactsShield;
            case "contacts.weapon": return f.ContactsWeapon;
            case "contacts.level": return f.ContactsLevel;
            case "stopped": return f.Stopped ? 1 : 0;
            case "speed":
                if (!world.TryStore<Body>(out var bodies) || !bodies.Has(e)) return null;
                var vel = bodies.Get(e).Velocity;
                return Math.Sqrt(vel.X * vel.X + vel.Y * vel.Y);
        }
        if (rest.StartsWith("distanceTo.", StringComparison.Ordinal))
        {
            int b = IndexOf(rest["distanceTo.".Length..]);
            if (b < 0 || !world.TryStore<Body>(out var bodies) || !bodies.Has(e) || !bodies.Has(actors[b])) return null;
            var p = bodies.Get(e).Feet;
            var q = bodies.Get(actors[b]).Feet;
            return Math.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y));
        }
        if (rest.StartsWith("powerLive.", StringComparison.Ordinal))
            return f.Live.ContainsKey(rest["powerLive.".Length..]) ? 1 : 0;
        return null;
    }

    // ---- the value forms (section 4.5) ----

    readonly Dictionary<string, RunValue> memo = new(StringComparer.Ordinal);

    /// <summary>
    /// A value form over the run so far (<paramref name="live"/>: mid-run, a stop's condition; named measures are not memoised) or at
    /// its end. A named measure is computed once at the end and kept.
    /// </summary>
    public RunValue Evaluate(ValueForm form, bool live = false, int depth = 0)
    {
        if (depth > 32) return new RunValue(null);   // a measure naming itself (the reader refuses it; a guard here)
        switch (form)
        {
            case CountForm c:
            {
                double n = 0;
                if (log.Window(c.Of) is null && HasWindow(c.Of)) return new RunValue(0, 0, null);
                foreach (var e in log.Select(c.Of)) n += c.SumField is { } sf ? e.Number(sf) ?? 0 : 1;
                return new RunValue(n, n);
            }
            case RateForm r:
            {
                double num = 0;
                foreach (var e in log.Select(r.Of)) num += r.OfSumField is { } sf ? e.Number(sf) ?? 0 : 1;
                double den;
                if (r.PerSecond)
                {
                    if (log.Window(r.Of) is not { } w) return new RunValue(null, num, 0);
                    long to = w.To is { } t ? Math.Min(t, now) : now;
                    den = Math.Max(0, to - w.From) / (double)World.TicksPerSecond;
                }
                else if (r.Per is { } per)
                {
                    den = 0;
                    foreach (var e in log.Select(per)) den += r.PerSumField is { } sf ? e.Number(sf) ?? 0 : 1;
                }
                else return new RunValue(null, num, null);
                return new RunValue(den > 0 ? num / den : null, num, den);
            }
            case ObservableForm o:
                if (o.AtSeconds is null) return new RunValue(Observable(o.Observable));
                return new RunValue(atTick.TryGetValue(o, out var p) && p.Taken ? p.Value : null);
            case ObservableAtEventForm ae:
                return new RunValue(atEvent.TryGetValue(ae, out var q) && q.Taken ? q.Value : null);
            case TimeOfForm t:
            {
                int seen = 0;
                double followed = now / (double)World.TicksPerSecond;
                foreach (var e in log.Select(t.Of)) if (++seen == t.Nth) return new RunValue(e.Seconds, FollowedSeconds: followed);
                return new RunValue(null, FollowedSeconds: followed);
            }
            case DelayForm d: return Delay(d);
            case DifferenceForm df:
            {
                var a = Evaluate(df.Left, live, depth + 1).Value;
                var b = Evaluate(df.Right, live, depth + 1).Value;
                return new RunValue(a is { } x && b is { } y ? x - y : null);
            }
            case RatioForm rf:
            {
                var a = Evaluate(rf.Numerator, live, depth + 1).Value;
                var b = Evaluate(rf.Denominator, live, depth + 1).Value;
                return new RunValue(a is { } x && b is { } y && y != 0 ? x / y : null, a, b);
            }
            case MeasureRefForm m:
                if (named.TryGetValue(m.Measure, out var spec))
                {
                    if (!live && memo.TryGetValue(m.Measure, out var had)) return had;
                    var v = Evaluate(spec.Form, live, depth + 1);
                    if (!live) memo[m.Measure] = v;
                    return v;
                }
                return new RunValue(BuiltIn(m.Measure));
            case CheckForm ch: return Check(ch.Check);
        }
        return new RunValue(null);
    }

    static bool HasWindow(EventSelector s) => s.FromMarker is not null;

    RunValue Delay(DelayForm d)
    {
        var froms = log.Select(d.From).ToList();
        var tos = log.Select(d.To).ToList();
        var used = new bool[tos.Count];
        var samples = new List<double>();
        int unanswered = 0;
        foreach (var f in froms)
        {
            object? key = d.PairBy is { } pb ? f.Field(pb) : null;
            int found = -1;
            for (int i = 0; i < tos.Count; i++)
            {
                if (used[i] || tos[i].Tick < f.Tick) continue;
                if (d.PairBy is { } pb2)
                {
                    var other = tos[i].Field(pb2);
                    if (key is null || other is null || !ScenarioEventLog.ValueEquals(other, Convert.ToString(key, System.Globalization.CultureInfo.InvariantCulture) ?? "")) continue;
                }
                found = i;
                break;
            }
            if (found < 0) { unanswered++; continue; }
            used[found] = true;
            samples.Add(tos[found].Seconds - f.Seconds);
        }
        return new RunValue(samples.Count > 0 ? samples.Average() : null, Samples: samples, Unanswered: unanswered);
    }

    /// <summary>A built-in measure's value now (<c>Christof.swings</c>, <c>run.seconds</c>), or null for none.</summary>
    public double? BuiltIn(string name)
    {
        int dot = name.IndexOf('.');
        if (dot <= 0) return null;
        string who = name[..dot], what = name[(dot + 1)..];
        if (who == BuiltInMeasures.RunScope)
            return what switch { "seconds" => now / (double)World.TicksPerSecond, "ticks" => now, _ => null };
        int a = IndexOf(who);
        if (a < 0) return null;
        var f = result.Fighters[a];
        return what switch
        {
            "swings" => f.Swings,
            "strikes" => f.Strikes,
            "contactsBody" => f.ContactsBody,
            "contactsShield" => f.ContactsShield,
            "contactsWeapon" => f.ContactsWeapon,
            "contactsLevel" => f.ContactsLevel,
            "hits" => f.Hits,
            "woundsDealt" => f.WoundsDealt,
            "woundsTaken" => f.WoundsTaken,
            "healthStart" => f.HealthStart,
            "healthEnd" => f.HealthEnd,
            "bloodSpent" => f.BloodSpent,
            "powerUses" => f.Uses,
            "renewals" => f.Renewals,
            "powersRefused" => f.PowersRefused,
            "liveSeconds" => f.LiveAnyTicks / (double)World.TicksPerSecond,
            "dodges" => f.DodgesBegun,
            "guardHeld" => f.GuardHeld,
            "guardParried" => f.GuardParried,
            "guardOverpowered" => f.GuardOverpowered,
            "staggers" => f.Staggered,
            "stopped" => f.Stopped ? 1 : 0,
            _ => null,
        };
    }

    // ---- the built-in checks (section 4.5; the rules package's acceptance negatives) ----

    /// <summary>The longest an arrow flies before the bow gives it up (the bow work's flight limit, 8 s; ENGINE_SANDBOX): arrowsAccounted does not count one loosed this close to the end.</summary>
    public const double ArrowFlightMostSeconds = 8.0;

    /// <summary>A built-in check over the run: 1 when it held, 0 when violated; the violations and the cases looked at kept.</summary>
    public RunValue Check(string name)
    {
        int cases = 0, violations = 0;
        var events = log.Events;
        switch (name)
        {
            case BuiltInChecks.NoHarmWithoutContact:
            {
                var contacts = new HashSet<long>();
                foreach (var e in events)
                {
                    if (e.Raw is ContactMade c) contacts.Add(c.ContactId);
                    else if (e.Raw is HarmResolved h) { cases++; if (!contacts.Contains(h.ContactId)) violations++; }
                }
                break;
            }
            case BuiltInChecks.BlockedBodyNoWound:
            {
                // A contact met at a shield, or at a guard the rules held or parried (or a clash): its own blow and any later contact of
                // the same physical action must deal no wound. The action is keyed by its own
                // identity (ActionKey: a blow's swing, an arrow's shot, a fixed contact's id), never by the swing number alone — every
                // arrow's Swing is 0, so a later, independent arrow that wounded was counted against an earlier blocked one. A Celerity
                // flurry strike belongs to its swing: its wound after its swing was blocked is a violation too.
                var blocked = new Dictionary<long, (ActionKey Action, long Tick)>();
                var contactOf = new Dictionary<long, ContactMade>();
                foreach (var e in events)
                {
                    if (e.Raw is ContactMade c)
                    {
                        contactOf[c.ContactId] = c;
                        if (c.Met == ContactMet.Shield && c.Guard == GuardMeet.None) { blocked.TryAdd(c.ContactId, (ActionKey.Of(in c), c.Tick)); }
                    }
                    else if (e.Raw is GuardResolved g && g.Verdict != GuardVerdict.Overpowered && contactOf.TryGetValue(g.ContactId, out var gc))
                        blocked.TryAdd(g.ContactId, (ActionKey.Of(in gc), gc.Tick));
                }
                cases = blocked.Count;
                foreach (var e in events)
                {
                    if (e.Raw is not HarmResolved h || h.Wounds <= 0) continue;
                    if (h.Flurry == 0 && blocked.ContainsKey(h.ContactId)) { violations++; continue; }
                    if (contactOf.TryGetValue(h.ContactId, out var hc))
                    {
                        var key = ActionKey.Of(in hc);
                        foreach (var b in blocked.Values)
                            if (b.Action == key && h.Tick >= b.Tick && (h.Flurry == 0 ? hc.Tick >= b.Tick : true)) { violations++; break; }
                    }
                }
                break;
            }
            case BuiltInChecks.NoDuplicateHarm:
            {
                var seen = new HashSet<long>();
                foreach (var e in events)
                    if (e.Raw is HarmResolved h && h.Flurry == 0) { cases++; if (!seen.Add(h.ContactId)) violations++; }
                break;
            }
            case BuiltInChecks.ProtectedUnharmed:
            {
                var protectedContacts = new HashSet<long>();
                foreach (var e in events) if (e.Raw is ProtectedProtest p) { cases++; protectedContacts.Add(p.ContactId); }
                foreach (var e in events) if (e.Raw is HarmResolved h && h.Wounds > 0 && protectedContacts.Contains(h.ContactId)) violations++;
                break;
            }
            case BuiltInChecks.ArrowsAccounted:
            {
                // The bow's events by name (the bow work's records): every arrow loosed came to rest (ArrowSpent, by archer and shot), and every
                // one that met something has its contact; an arrow loosed in the last ArrowFlightMostSeconds before the run's end may be in
                // the air still (the bow work gives a flight up after 8 s), and is not counted against the check.
                var spent = new HashSet<(string?, double)>();
                var contacts = new HashSet<long>();
                foreach (var e in events)
                {
                    if (e.Kind == "ArrowSpent")
                    {
                        spent.Add((e.By, e.Number("Shot") ?? -1));
                        if (string.Equals(e.Field("End") as string, "Met", StringComparison.OrdinalIgnoreCase) && e.Number("ContactId") is { } cid && cid > 0)
                            contacts.Add((long)cid);
                    }
                }
                var made = new HashSet<long>();
                foreach (var e in events) if (e.Raw is ContactMade cm) made.Add(cm.ContactId);
                long inFlightFrom = now - (long)(ArrowFlightMostSeconds * World.TicksPerSecond);
                foreach (var e in events)
                {
                    if (e.Kind != "ArrowLoosed") continue;
                    if (spent.Contains((e.By, e.Number("Shot") ?? -1))) { cases++; continue; }
                    if (e.Tick >= inFlightFrom) continue;   // may be in the air at the end
                    cases++;
                    violations++;
                }
                foreach (var c in contacts) if (!made.Contains(c)) violations++;
                break;
            }
            default: return new RunValue(null);
        }
        return new RunValue(violations == 0 ? 1 : 0, violations, cases);
    }

    // ---- the run's end ----

    /// <summary>Every measure at the run's end: the built-in ones (every actor's, the run's) and the named ones, by name.</summary>
    public Dictionary<string, RunValue> Finish()
    {
        now = world.Tick;
        var all = new Dictionary<string, RunValue>(StringComparer.Ordinal);
        foreach (var b in BuiltInMeasures.PerRun) all[$"{BuiltInMeasures.RunScope}.{b.Name}"] = new RunValue(BuiltIn($"{BuiltInMeasures.RunScope}.{b.Name}"));
        foreach (var n in names)
            foreach (var b in BuiltInMeasures.PerActor) all[$"{n}.{b.Name}"] = new RunValue(BuiltIn($"{n}.{b.Name}"));
        foreach (var m in model.Measures) all[m.Name] = Evaluate(new MeasureRefForm(m.Name));
        return all;
    }

    /// <summary>The steps that fell short (the director's events), in order.</summary>
    public List<StepShortfall> Shortfalls()
    {
        var list = new List<StepShortfall>();
        foreach (var e in log.Events)
        {
            switch (e.Raw)
            {
                case StepTimedOut t: list.Add(new(e.By ?? "", e.Kind, Step(t.Kind), t.Label, e.Seconds, t.Why)); break;
                case StepRefused r: list.Add(new(e.By ?? "", e.Kind, Step(r.Kind), r.Label, e.Seconds, r.Why.ToString())); break;
                case StepLate l: list.Add(new(e.By ?? "", e.Kind, Step(l.Kind), l.Label, e.Seconds, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"late {l.LateSeconds:0.###} s"))); break;
            }
        }
        return list;
    }

    /// <summary>A step kind as the file names it (<c>moveTo</c>).</summary>
    public static string Step(ScenarioStepKind k) { string s = k.ToString(); return char.ToLowerInvariant(s[0]) + s[1..]; }
}

/// <summary>
/// A physical action's identity: a blow by its attacker and swing, an arrow by its archer and
/// shot, a fixed contact by its contact id. Two contacts of one action share it; an independent arrow (every arrow's swing is 0) or
/// an independent fixed contact never does.
/// </summary>
public readonly record struct ActionKey(EntityId Attacker, ActionKind Kind, long Id)
{
    /// <summary>The action a contact belongs to.</summary>
    public static ActionKey Of(in ContactMade c) =>
        c.Fixed ? new(c.Attacker, ActionKind.FixedContact, c.ContactId)
        : c.Shot > 0 ? new(c.Attacker, ActionKind.Shot, c.Shot)
        : new(c.Attacker, ActionKind.Swing, c.Swing);
}

/// <summary>What kind of physical action a contact came from.</summary>
public enum ActionKind : byte { Swing, Shot, FixedContact }

