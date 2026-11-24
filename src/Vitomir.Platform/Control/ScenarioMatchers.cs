// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: what the director reads for its triggers and a step's "on" — events by selector, observables.
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Core;
using Vitomir.Platform.Powers;

namespace Vitomir.Platform.Control;

/// <summary>
/// An event selector made ready for a world: the event's type found by its kind's name among the Vitomir assemblies' event
/// records, its "by" and "on" fields by the published rule (by: the first of Attacker, Entity, By, Killer, User, Archer, else Who;
/// on: the first of Target, Defender, Who when another field is "by", Threat), its <c>where</c> filters compiled to typed
/// predicates (names and enumerated values without regard to case; a body region also by its side-less name: <c>upperArm</c>
/// meets either arm; the derived <c>region</c> and <c>side</c> of a contact from its <c>Site</c>). Counts last tick's matches.
/// Allocates nothing per tick.
/// </summary>
internal abstract class EventMatcher
{
    /// <summary>How many of last tick's events match.</summary>
    public abstract int Matches(World world, long tick);

    static readonly string[] ByFields = ["Attacker", "Entity", "By", "Killer", "User", "Archer"];
    static readonly string[] OnFields = ["Target", "Defender"];

    public static EventMatcher For(World world, ScenarioDirector director, EventSelector selector, string where)
    {
        var types = EventTypes(selector.Event);
        if (types.Count == 0)
            throw new ScenarioProgramException($"scenario: {where}: no event is called {selector.Event} in this build");
        EventMatcher? first = null;
        foreach (var t in types)
        {
            var m = (EventMatcher)Activator.CreateInstance(typeof(EventMatcher<>).MakeGenericType(t), nonPublic: true)!;
            m.Init(world, director, selector, where);
            first ??= m;
            if (m.Published) return m;
        }
        return first!;
    }

    protected abstract void Init(World world, ScenarioDirector director, EventSelector selector, string where);

    /// <summary>Whether this world publishes the event at all.</summary>
    protected abstract bool Published { get; }

    static readonly Dictionary<string, List<Type>> Known = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The event records of that name in the loaded Vitomir assemblies (value types).</summary>
    internal static List<Type> EventTypes(string name)
    {
        lock (Known)
        {
            if (Known.TryGetValue(name, out var found)) return found;
            found = [];
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic || asm.GetName().Name is not { } an || !an.StartsWith("Vitomir", StringComparison.Ordinal)) continue;
                Type[] types;
                try { types = asm.GetExportedTypes(); }
                catch (Exception e) when (e is ReflectionTypeLoadException or NotSupportedException) { continue; }
                foreach (var t in types)
                    if (t.IsValueType && !t.IsEnum && !t.IsGenericType && string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) found.Add(t);
            }
            if (found.Count > 0) Known[name] = found;
            return found;
        }
    }

    /// <summary>A record's member by name, without regard to case (a property or a field).</summary>
    internal static MemberInfo? Member(Type t, string name) =>
        (MemberInfo?)t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
        ?? t.GetField(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

    internal static Type TypeOf(MemberInfo m) => m is PropertyInfo p ? p.PropertyType : ((FieldInfo)m).FieldType;

    /// <summary>The "by" and "on" members of an event record (the rule <c>--describe events</c> publishes).</summary>
    internal static (MemberInfo? By, MemberInfo? On) Actors(Type t)
    {
        MemberInfo? by = null, on = null;
        foreach (var n in ByFields)
            if (Member(t, n) is { } m && TypeOf(m) == typeof(EntityId) && m.Name == n) { by = m; break; }
        var who = Member(t, "Who") is { } w && TypeOf(w) == typeof(EntityId) ? w : null;
        foreach (var n in OnFields)
            if (Member(t, n) is { } m && TypeOf(m) == typeof(EntityId) && m.Name == n) { on = m; break; }
        if (by is null && who is not null) by = who;
        else if (on is null && who is not null) on = who;
        if (on is null && Member(t, "Threat") is { } th && TypeOf(th) == typeof(EntityId)) on = th;
        return (by, on);
    }
}

internal sealed class EventMatcher<T> : EventMatcher where T : struct
{
    EventQueue<T>? queue;
    Func<T, EntityId>? by, on;
    EntityId byE, onE;
    Func<T, bool>[] filters = [];
    double? fromSeconds, toSeconds;
    string? fromMarker, toMarker;
    ScenarioDirector director = null!;

    protected override bool Published => queue is not null;

    protected override void Init(World world, ScenarioDirector d, EventSelector s, string where)
    {
        director = d;
        queue = world.TryService<EventQueue<T>>(out var q) ? q : null;
        var (byM, onM) = Actors(typeof(T));
        if (s.By is { } byName)
        {
            if (byM is null) throw new ScenarioProgramException($"scenario: {where}: {typeof(T).Name} has no actor who does it, so \"by\" cannot be used");
            by = Getter<EntityId>(byM);
            byE = Actor(d, byName, where);
        }
        if (s.On is { } onName)
        {
            if (onM is null) throw new ScenarioProgramException($"scenario: {where}: {typeof(T).Name} has no actor it is done to, so \"on\" cannot be used");
            on = Getter<EntityId>(onM);
            onE = Actor(d, onName, where);
        }
        var list = new List<Func<T, bool>>();
        foreach (var f in s.Where ?? []) list.Add(Filter(d, f, where));
        filters = [.. list];
        fromSeconds = s.FromSeconds;
        toSeconds = s.ToSeconds;
        fromMarker = s.FromMarker;
        toMarker = s.ToMarker;
    }

    static EntityId Actor(ScenarioDirector d, string name, string where)
    {
        var e = d.Entity(name);
        if (e.IsNone) throw new ScenarioProgramException($"scenario: {where}: no actor is named {name} (the actors: {string.Join(", ", d.Names)})");
        return e;
    }

    public override int Matches(World world, long tick)
    {
        if (queue is null) return 0;
        long at = tick - 1;   // last tick's events
        double seconds = at / (double)World.TicksPerSecond;
        if (fromSeconds is { } f && seconds < f) return 0;
        if (toSeconds is { } t && seconds > t) return 0;
        if (fromMarker is { } fm) { long m = director.MarkerTick(fm); if (m < 0 || at < m) return 0; }
        if (toMarker is { } tm) { long m = director.MarkerTick(tm); if (m >= 0 && at >= m) return 0; }
        var list = queue.LastTick;
        int n = 0;
        for (int i = 0; i < list.Count; i++)
        {
            var ev = list[i];
            if (by is not null && by(ev) != byE) continue;
            if (on is not null && on(ev) != onE) continue;
            bool ok = true;
            for (int k = 0; k < filters.Length && ok; k++) ok = filters[k](ev);
            if (ok) n++;
        }
        return n;
    }

    static Func<T, R> Getter<R>(MemberInfo m)
    {
        var p = Expression.Parameter(typeof(T), "e");
        Expression body = Expression.MakeMemberAccess(p, m);
        if (body.Type != typeof(R)) body = Expression.Convert(body, typeof(R));
        return Expression.Lambda<Func<T, R>>(body, p).Compile();
    }

    Func<T, bool> Filter(ScenarioDirector d, WhereFilter f, string where)
    {
        var values = f.Values;
        var m = Member(typeof(T), f.Field);
        if (m is null)
        {
            // Derived from a contact's site ("left upper arm"): its region as aimAt names it, and its side.
            if (Member(typeof(T), "Site") is { } site && TypeOf(site) == typeof(string))
            {
                var get = Getter<string?>(site);
                if (string.Equals(f.Field, "region", StringComparison.OrdinalIgnoreCase))
                {
                    var wants = values.Select(Norm).ToArray();
                    return e => SiteIs(get(e), wants);
                }
                if (string.Equals(f.Field, "side", StringComparison.OrdinalIgnoreCase))
                {
                    var wants = values.Select(v => v.ToLowerInvariant()).ToArray();
                    return e => Array.IndexOf(wants, SideOf(get(e))) >= 0;
                }
            }
            var known = string.Join(", ", typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name));
            throw new ScenarioProgramException($"scenario: {where}: the director cannot read \"{f.Field}\" of {typeof(T).Name} in a trigger or a step's \"on\"/\"until\" (its fields: {known})");
        }
        var type = TypeOf(m);
        var under = Nullable.GetUnderlyingType(type);
        if (under is not null) throw new ScenarioProgramException($"scenario: {where}: {typeof(T).Name}.{m.Name} may be empty, which a trigger cannot filter on in this build");
        if (type == typeof(EntityId))
        {
            var get = Getter<EntityId>(m);
            var es = values.Select(v => Actor(d, v, where)).ToArray();
            return e => Array.IndexOf(es, get(e)) >= 0;
        }
        if (type.IsEnum)
        {
            var get = Getter<long>(m);
            var allowed = new List<long>();
            foreach (var v in values)
            {
                bool any = false;
                foreach (var name in Enum.GetNames(type))
                    if (EnumNameMatches(type, name, v)) { allowed.Add(Convert.ToInt64(Enum.Parse(type, name))); any = true; }
                if (!any) throw new ScenarioProgramException($"scenario: {where}: \"{v}\" is not a {type.Name} of {typeof(T).Name}.{m.Name} (one of: {string.Join(", ", Enum.GetNames(type))})");
            }
            var arr = allowed.ToArray();
            return e => Array.IndexOf(arr, get(e)) >= 0;
        }
        if (type == typeof(string))
        {
            var get = Getter<string?>(m);
            var arr = values.ToArray();
            return e => { var s = get(e); foreach (var v in arr) if (string.Equals(s, v, StringComparison.OrdinalIgnoreCase)) return true; return false; };
        }
        if (type == typeof(bool))
        {
            var get = Getter<bool>(m);
            bool wantTrue = values.Any(v => string.Equals(v, "true", StringComparison.OrdinalIgnoreCase));
            bool wantFalse = values.Any(v => string.Equals(v, "false", StringComparison.OrdinalIgnoreCase));
            if (!wantTrue && !wantFalse) throw new ScenarioProgramException($"scenario: {where}: {typeof(T).Name}.{m.Name} is true or false");
            return e => get(e) ? wantTrue : wantFalse;
        }
        if (type == typeof(int) || type == typeof(long) || type == typeof(float) || type == typeof(double) || type == typeof(byte) || type == typeof(short))
        {
            var get = Getter<double>(m);
            var arr = new double[values.Count];
            for (int i = 0; i < arr.Length; i++)
                if (!double.TryParse(values[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out arr[i]))
                    throw new ScenarioProgramException($"scenario: {where}: {typeof(T).Name}.{m.Name} is a number, not \"{values[i]}\"");
            return e => Array.IndexOf(arr, get(e)) >= 0;
        }
        throw new ScenarioProgramException($"scenario: {where}: {typeof(T).Name}.{m.Name} ({type.Name}) cannot be filtered on in a trigger in this build");
    }

    /// <summary>An enumerated value's name against a selector's word, without regard to case or spaces; a body region also by its name without the side.</summary>
    static bool EnumNameMatches(Type type, string name, string word)
    {
        string n = Norm(name), v = Norm(word);
        if (n == v) return true;
        if (type == typeof(BodyRegion)) return n == "left" + v || n == "right" + v;
        return false;
    }

    internal static string Norm(string s)
    {
        Span<char> buf = stackalloc char[s.Length];
        int k = 0;
        foreach (char c in s) if (c != ' ' && c != '_' && c != '-') buf[k++] = char.ToLowerInvariant(c);
        return new string(buf[..k]);
    }

    static bool SiteIs(string? site, string[] wants)
    {
        if (site is null) return false;
        // Allocation-free: compare the site without spaces to each word, with or without its side.
        foreach (var w in wants)
        {
            if (SameLetters(site, w, 0)) return true;
            if (StartsSide(site, "left", out int skip) && SameLetters(site, w, skip)) return true;
            if (StartsSide(site, "right", out skip) && SameLetters(site, w, skip)) return true;
        }
        return false;
    }

    static string SideOf(string? site) =>
        site is null ? "" : site.StartsWith("left", StringComparison.OrdinalIgnoreCase) ? "left" : site.StartsWith("right", StringComparison.OrdinalIgnoreCase) ? "right" : "";

    static bool StartsSide(string site, string side, out int skip)
    {
        skip = 0;
        if (!site.StartsWith(side, StringComparison.OrdinalIgnoreCase)) return false;
        skip = side.Length;
        return true;
    }

    /// <summary>Whether <paramref name="site"/> from <paramref name="from"/>, spaces left out and case ignored, is <paramref name="norm"/>.</summary>
    static bool SameLetters(string site, string norm, int from)
    {
        int k = 0;
        for (int i = from; i < site.Length; i++)
        {
            char c = site[i];
            if (c == ' ' || c == '_' || c == '-') continue;
            if (k >= norm.Length || char.ToLowerInvariant(c) != norm[k]) return false;
            k++;
        }
        return k == norm.Length;
    }
}

/// <summary>
/// An observable the director reads for a trigger's value condition (section 4.5's list, the part the world shows at the moment):
/// <c>time</c>; <c>&lt;actor&gt;.health</c>, <c>.healthMax</c>, <c>.blood</c>, <c>.bloodMax</c> (the module's <see cref="Vitals"/>);
/// <c>.distanceTo.&lt;actor&gt;</c> (feet to feet, level: X and Y only, m); <c>.speed</c> (level, m/s); <c>.stopped</c> (1 or 0);
/// <c>.powerLive.&lt;power&gt;</c> (1 while a slot shows it live); <c>.swings</c>. The tallies (<c>.woundsTaken</c>, <c>.contacts.*</c>,
/// <c>.hits</c>, <c>.bloodSpent</c>) are the runner's: refused by name here (a count condition on the event does the same).
/// </summary>
internal sealed class Observable
{
    enum Kind { Time, Health, HealthMax, Blood, BloodMax, Distance, Speed, Stopped, PowerLive, Swings }

    Kind kind;
    EntityId actor, other;
    string power = "";

    public static readonly string[] Supported = ["time", "<actor>.health", "<actor>.healthMax", "<actor>.blood", "<actor>.bloodMax",
        "<actor>.distanceTo.<actor>", "<actor>.speed", "<actor>.stopped", "<actor>.powerLive.<power>", "<actor>.swings"];

    public static Observable Parse(World world, ScenarioDirector d, string path, string where)
    {
        if (string.Equals(path, "time", StringComparison.OrdinalIgnoreCase)) return new Observable { kind = Kind.Time };
        int dot = path.IndexOf('.');
        string Bad() => $"scenario: {where}: \"{path}\" is not an observable a trigger can read (it can read: {string.Join(", ", Supported)})";
        if (dot <= 0) throw new ScenarioProgramException(Bad());
        var e = d.Entity(path[..dot]);
        if (e.IsNone) throw new ScenarioProgramException($"scenario: {where}: \"{path}\": no actor is named {path[..dot]} (the actors: {string.Join(", ", d.Names)})");
        var rest = path[(dot + 1)..];
        var o = new Observable { actor = e };
        int dot2 = rest.IndexOf('.');
        string head = dot2 < 0 ? rest : rest[..dot2], arg = dot2 < 0 ? "" : rest[(dot2 + 1)..];
        switch (head.ToLowerInvariant())
        {
            case "health": o.kind = Kind.Health; break;
            case "healthmax": o.kind = Kind.HealthMax; break;
            case "blood": o.kind = Kind.Blood; break;
            case "bloodmax": o.kind = Kind.BloodMax; break;
            case "speed": o.kind = Kind.Speed; break;
            case "stopped": o.kind = Kind.Stopped; break;
            case "swings": o.kind = Kind.Swings; break;
            case "distanceto":
                o.kind = Kind.Distance;
                o.other = d.Entity(arg);
                if (o.other.IsNone) throw new ScenarioProgramException($"scenario: {where}: \"{path}\": no actor is named {arg} (the actors: {string.Join(", ", d.Names)})");
                break;
            case "powerlive":
                o.kind = Kind.PowerLive;
                o.power = arg;
                if (arg.Length == 0) throw new ScenarioProgramException(Bad());
                break;
            default: throw new ScenarioProgramException(Bad());
        }
        if (o.kind is Kind.Health or Kind.HealthMax or Kind.Blood or Kind.BloodMax && !world.TryStore<Vitals>(out _))
            throw new ScenarioProgramException($"scenario: {where}: \"{path}\": this world's modules show no Health or Blood");
        return o;
    }

    public double Read(World w, ScenarioDirector d)
    {
        switch (kind)
        {
            case Kind.Time: return w.Tick / (double)World.TicksPerSecond;
            case Kind.Health or Kind.HealthMax or Kind.Blood or Kind.BloodMax:
            {
                if (!w.TryStore<Vitals>(out var vitals)) return 0;
                ref var v = ref vitals.Find(actor);
                if (Unsafe.IsNullRef(ref v)) return 0;
                return kind switch { Kind.Health => v.Health, Kind.HealthMax => v.HealthMax, Kind.Blood => v.Blood, _ => v.BloodMax };
            }
            case Kind.Distance:
            {
                var bodies = w.Store<Body>();
                ref var a = ref bodies.Find(actor);
                ref var b = ref bodies.Find(other);
                if (Unsafe.IsNullRef(ref a) || Unsafe.IsNullRef(ref b)) return double.PositiveInfinity;
                return Vector2.Distance(new(a.Feet.X, a.Feet.Y), new(b.Feet.X, b.Feet.Y));
            }
            case Kind.Speed:
            {
                ref var a = ref w.Store<Body>().Find(actor);
                return Unsafe.IsNullRef(ref a) ? 0 : new Vector2(a.Velocity.X, a.Velocity.Y).Length();
            }
            case Kind.Stopped: return w.TryStore<Dead>(out var dead) && dead.Has(actor) ? 1 : 0;
            case Kind.Swings:
            {
                ref var c = ref w.Store<Combatant>().Find(actor);
                return Unsafe.IsNullRef(ref c) ? 0 : c.Swings;
            }
            case Kind.PowerLive:
            {
                if (!w.TryStore<PowerSlots>(out var views) || !w.TryService<IPowerCatalogue>(out var catalogue)) return 0;
                ref var v = ref views.Find(actor);
                if (Unsafe.IsNullRef(ref v)) return 0;
                for (int s = 0; s < QuickSlots.Count; s++)
                {
                    int index = v.Slots[s].Power;
                    if (index >= 0 && index < catalogue.Count && v.Slots[s].State == SlotState.Live && ScenarioDirector.PowerMatches(catalogue.Id(index), power)) return 1;
                }
                return 0;
            }
        }
        return 0;
    }
}
