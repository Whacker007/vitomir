// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Core;
using Vitomir.Platform.Host.Arena;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Host.Scenario;

/// <summary>The kinds of objective (the rules package's).</summary>
public enum ObjectiveKind { Defeat, Survive, Reach, Escape, Protect, Hold }

/// <summary>
/// One objective as compiled: whose it is (<paramref name="Actor"/> or <paramref name="Side"/>), its kind and what it is about —
/// a target actor or side (defeat, protect), an area (reach, hold, escape out of: a point, the level's named place, a radius), a distance
/// from every foe (escape) — and its time: <paramref name="BySeconds"/> a deadline for what is to be done (defeat, reach, escape),
/// <paramref name="UntilSeconds"/> the end of what is to be kept (survive, protect, hold; none: the run's end).
/// </summary>
public sealed record ObjectiveSpec(string Name, ObjectiveKind Kind, string? Actor, string? Side, string? Target, string? TargetSide,
    double[]? Point, string? Place, double RadiusMetres, double? FromFoesMetres, double? BySeconds, double? UntilSeconds, string Pointer)
{
    /// <summary>Kept for the whole run (no time): decided only by failing, or met when the run ends.</summary>
    public bool WholeRun => Kind is ObjectiveKind.Survive or ObjectiveKind.Protect or ObjectiveKind.Hold && UntilSeconds is null;

    /// <summary>The kind's word (<c>defeat</c>, …).</summary>
    public string KindWord => Kind.ToString().ToLowerInvariant();

    /// <summary>The objectives of a resolved scenario (none when it gives none).</summary>
    public static List<ObjectiveSpec> From(JsonObject resolved)
    {
        var list = new List<ObjectiveSpec>();
        if (resolved["objectives"] is not JsonArray arr) return list;
        int i = 0;
        foreach (var n in arr)
        {
            var o = n!.AsObject();
            string? S(string k) => o[k] is JsonValue v && v.TryGetValue(out string? s) ? s : null;
            double? D(JsonNode? x) => x is JsonValue v && v.TryGetValue(out double d) ? d : null;
            var area = o["area"] as JsonObject ?? o["outOf"] as JsonObject;
            list.Add(new ObjectiveSpec(S("name")!, Enum.Parse<ObjectiveKind>(S("kind")!, ignoreCase: true), S("actor"), S("side"), S("target"), S("targetSide"),
                area?["point"] is JsonArray p ? [.. p.Select(x => x!.GetValue<double>())] : null, area?["place"]?.GetValue<string>(),
                D(area?["radiusMetres"]) ?? 0, D(o["fromFoesMetres"]), D(o["bySeconds"]), D(o["untilSeconds"]), $"/objectives/{i++}"));
        }
        return list;
    }
}

/// <summary>
/// What became of one objective in one run: MET, FAILED or UNRESOLVED, when (s; null for UNRESOLVED), why in words, the stream's
/// event of the result (<c>ObjectiveMet</c> / <c>ObjectiveFailed</c>, when the run's stream was kept) and the event that decided it.
/// </summary>
public sealed record ObjectiveOutcome(string Name, string Kind, string Owner, string Result, double? Seconds, string Reason, int? EventId = null, int? CauseEventId = null);

/// <summary>One objective over a point's runs: how many runs met, failed and left it unresolved, the share met with Wilson's 95 % interval (unresolved runs counted as not met, and said), the median second it was met and failed, and a run of each result to look at.</summary>
public sealed record ObjectiveAggregate(string Name, string Kind, string Owner, int Runs, int Met, int Failed, int Unresolved,
    double? MetShare, double[]? MetShareInterval95, double? MedianMetSeconds, double? MedianFailedSeconds,
    IReadOnlyDictionary<string, string>? ExampleRuns = null, IReadOnlyDictionary<string, int>? Reasons = null);

/// <summary>
/// The objectives of one run, followed after each tick from the world and the run's events, never acting on the world:
/// a run with objectives is the run without them, digest for digest. Harness stops (<c>ActorDied</c>: <c>ZERO_HEALTH_STOP</c>,
/// <c>SEVERED_STOP</c>; the tally's <c>DISARMED</c>) are what "defeated" and "stopped" mean — never a death. Each result is written into the
/// run's event log as <see cref="ObjectiveMet"/> / <see cref="ObjectiveFailed"/>, its parent the event that decided it. A side's result is
/// the side's: whoever dealt the last harm is named in the reason as a receipt, never awarded the result.
/// </summary>
public sealed class ObjectiveTracker
{
    static readonly CultureInfo Ic = CultureInfo.InvariantCulture;
    readonly World world;
    readonly IReadOnlyList<EntityId> fighters;
    readonly IReadOnlyList<string> names;
    readonly ArenaCast cast;
    readonly IReadOnlyList<ObjectiveSpec> specs;
    readonly ScenarioEventLog log;
    readonly BoutResult tally;
    readonly long[] stoppedTick;
    readonly string?[] stopName;
    readonly int[] stopEvent;
    readonly string?[] lastHarmBy;
    readonly int[] lastHarmEvent;
    readonly ObjectiveResult[] result;
    readonly long[] decidedTick;
    readonly string[] reason;
    readonly int[] cause;
    readonly int[] eventId;
    readonly float[] closest;
    int seen;

    public ObjectiveTracker(World world, IReadOnlyList<EntityId> fighters, IReadOnlyList<string> names, ArenaCast cast,
        IReadOnlyList<ObjectiveSpec> specs, ScenarioEventLog log, BoutResult tally)
    {
        this.world = world; this.fighters = fighters; this.names = names; this.cast = cast; this.specs = specs; this.log = log; this.tally = tally;
        int n = fighters.Count;
        stoppedTick = Enumerable.Repeat(-1L, n).ToArray();
        stopName = new string?[n];
        stopEvent = Enumerable.Repeat(-1, n).ToArray();
        lastHarmBy = new string?[n];
        lastHarmEvent = Enumerable.Repeat(-1, n).ToArray();
        result = new ObjectiveResult[specs.Count];
        decidedTick = Enumerable.Repeat(-1L, specs.Count).ToArray();
        reason = Enumerable.Repeat("", specs.Count).ToArray();
        cause = Enumerable.Repeat(-1, specs.Count).ToArray();
        eventId = Enumerable.Repeat(-1, specs.Count).ToArray();
        closest = Enumerable.Repeat(float.MaxValue, specs.Count).ToArray();
    }

    /// <summary>The objectives decided on the last <see cref="Update"/> (for "first").</summary>
    public int DecidedNow { get; private set; }

    int Index(string? name) => name is null ? -1 : names.ToList().IndexOf(name);
    string? SideOf(int i) => i >= 0 && i < cast.Fighters.Count ? cast.Fighters[i].Side : null;
    bool Stopped(int i) => stoppedTick[i] >= 0;
    static string Sec(long tick) => (tick / (double)World.TicksPerSecond).ToString("0.##", Ic);

    Vector3? Feet(int i)
    {
        if (i < 0 || i >= fighters.Count) return null;
        ref var b = ref world.Store<Body>().Find(fighters[i]);
        return Unsafe.IsNullRef(ref b) ? null : b.Feet;
    }

    /// <summary>Reads the tick just stepped (call after the log's <c>Collect</c> and the tally's).</summary>
    public void Update()
    {
        long tick = world.Tick - 1;
        DecidedNow = 0;
        var events = log.Events;
        for (; seen < events.Count; seen++)
        {
            var e = events[seen];
            int on = Index(e.On), by = Index(e.By);
            switch (e.Kind)
            {
                case "ActorDied":
                {
                    int who = Index(e.Field("Who") as string);
                    if (who >= 0 && !Stopped(who)) { stoppedTick[who] = e.Tick; stopName[who] = e.Field("Stop") is "SeveredStop" ? "SEVERED_STOP" : "ZERO_HEALTH_STOP"; stopEvent[who] = e.Id; }
                    break;
                }
                case "HarmResolved":
                    if (on >= 0 && e.By is not null) { lastHarmBy[on] = e.By; lastHarmEvent[on] = e.Id; }
                    break;
            }
            _ = by;
        }
        for (int i = 0; i < fighters.Count; i++)
            if (!Stopped(i) && tally.Fighters.Length > i && tally.Fighters[i].Stopped)
            {
                stoppedTick[i] = tick;
                stopName[i] = tally.Fighters[i].Stop is { Length: > 0 } sn ? sn : "DISARMED";
            }
        for (int k = 0; k < specs.Count; k++)
            if (result[k] == ObjectiveResult.Unresolved) Evaluate(k, tick);
    }

    void Decide(int k, long tick, ObjectiveResult r, string why, int causeId)
    {
        result[k] = r; decidedTick[k] = tick; reason[k] = why; cause[k] = causeId;
        DecidedNow++;
        var s = specs[k];
        int owner = Index(s.Actor), target = Index(s.Target);
        var who = owner >= 0 ? fighters[owner] : EntityId.None;
        var on = target >= 0 ? fighters[target] : EntityId.None;
        int before = log.Events.Count;
        if (r == ObjectiveResult.Met) log.Note(new ObjectiveMet(who, tick, s.Name, s.KindWord, s.Side, on, why), tick, causeId);
        else log.Note(new ObjectiveFailed(who, tick, s.Name, s.KindWord, s.Side, on, why), tick, causeId);
        eventId[k] = log.Events.Count > before ? log.Events[^1].Id : -1;
        seen = log.Events.Count;   // the tracker's own records are not read back
    }

    string StopWords(int i) =>
        $"{names[i]} reached {stopName[i]} at {Sec(stoppedTick[i])} s (a harness stop, not a death){(lastHarmBy[i] is { } h ? $"; the last harm to it was {h}'s" : "")}";

    int CauseOf(int i) => stopEvent[i] >= 0 ? stopEvent[i] : lastHarmEvent[i];

    IEnumerable<int> Members(string side) => Enumerable.Range(0, fighters.Count).Where(i => SideOf(i) == side);

    /// <summary>The owner's state: −1 for a side; and whether the owner (an actor, or every actor of the side) is stopped.</summary>
    bool OwnerDown(ObjectiveSpec s, out string words, out int causeId)
    {
        words = ""; causeId = -1;
        if (Index(s.Actor) is var o and >= 0)
        {
            if (!Stopped(o)) return false;
            words = StopWords(o); causeId = CauseOf(o);
            return true;
        }
        if (s.Side is { } side)
        {
            var m = Members(side).ToList();
            if (m.Count == 0 || m.Any(i => !Stopped(i))) return false;
            int last = m.OrderBy(i => stoppedTick[i]).Last();
            words = $"every actor of side {side} reached a harness stop (the last: {StopWords(last)})";
            causeId = CauseOf(last);
            return true;
        }
        return false;
    }

    bool Inside(ObjectiveSpec s, Vector3 feet, out float across)
    {
        across = float.MaxValue;
        if (s.Point is not { } p) return false;
        across = new Vector2(feet.X - (float)p[0], feet.Y - (float)p[1]).Length();
        bool level = p.Length < 3 || MathF.Abs(feet.Z - (float)p[2]) <= 1.5f;
        return level && across <= s.RadiusMetres;
    }

    string AreaWords(ObjectiveSpec s) =>
        s.Point is { } p ? string.Create(Ic, $"{(s.Place is null ? "" : $"{s.Place}, ")}{s.RadiusMetres:0.##} m round ({p[0]:0.##}, {p[1]:0.##})") : s.Place ?? "its area";

    void Evaluate(int k, long tick)
    {
        var s = specs[k];
        double now = (tick + 1) / (double)World.TicksPerSecond;   // the state's time after the tick (the run's own seconds); events carry the tick they happened in
        int owner = Index(s.Actor);
        switch (s.Kind)
        {
            case ObjectiveKind.Defeat:
            {
                if (s.Target is { } tn && Index(tn) is var t and >= 0 && Stopped(t)) { Decide(k, stoppedTick[t], ObjectiveResult.Met, StopWords(t), CauseOf(t)); return; }
                if (s.TargetSide is { } ts)
                {
                    var m = Members(ts).ToList();
                    if (m.Count > 0 && m.All(Stopped))
                    {
                        int last = m.OrderBy(i => stoppedTick[i]).Last();
                        Decide(k, stoppedTick[last], ObjectiveResult.Met, $"every actor of side {ts} reached a harness stop (a harness stop, not a death; the last: {StopWords(last)})", CauseOf(last));
                        return;
                    }
                }
                if (OwnerDown(s, out var w, out int c)) { Decide(k, tick, ObjectiveResult.Failed, $"its owner cannot go on: {w}", c); return; }
                break;
            }
            case ObjectiveKind.Survive:
                if (OwnerDown(s, out var ws, out int cs))
                {
                    Decide(k, tick, ObjectiveResult.Failed, s.Side is null ? ws : ws, cs);
                    return;
                }
                break;
            case ObjectiveKind.Protect:
            {
                int t = Index(s.Target);
                if (t >= 0 && Stopped(t)) { Decide(k, stoppedTick[t], ObjectiveResult.Failed, StopWords(t), CauseOf(t)); return; }
                break;
            }
            case ObjectiveKind.Reach:
            {
                if (Feet(owner) is { } f)
                {
                    bool inside = Inside(s, f, out float across);
                    closest[k] = MathF.Min(closest[k], across);
                    if (inside && !Stopped(owner)) { Decide(k, tick, ObjectiveResult.Met, string.Create(Ic, $"{s.Actor} stood {across:0.##} m from the middle of {AreaWords(s)} at {now:0.##} s"), -1); return; }
                }
                if (OwnerDown(s, out var w, out int c)) { Decide(k, tick, ObjectiveResult.Failed, $"{w}, {closest[k].ToString("0.##", Ic)} m from the area's middle at the nearest", c); return; }
                break;
            }
            case ObjectiveKind.Escape:
            {
                if (Feet(owner) is { } f)
                {
                    if (s.FromFoesMetres is { } d)
                    {
                        float nearest = float.MaxValue; string? who = null;
                        foreach (int fo in Foes(owner))
                        {
                            if (Stopped(fo) || Feet(fo) is not { } ff) continue;
                            float x = new Vector2(ff.X - f.X, ff.Y - f.Y).Length();
                            if (x < nearest) { nearest = x; who = names[fo]; }
                        }
                        closest[k] = MathF.Min(closest[k], nearest);
                        if (nearest > d && !Stopped(owner))
                        {
                            Decide(k, tick, ObjectiveResult.Met, who is null ? string.Create(Ic, $"no foe of {s.Actor} still stands at {now:0.##} s") :
                                string.Create(Ic, $"{s.Actor} was {nearest:0.##} m from its nearest foe still standing ({who}) at {now:0.##} s, more than {d:0.##} m"), -1);
                            return;
                        }
                    }
                    else if (!Inside(s, f, out float across) && !Stopped(owner))
                    {
                        Decide(k, tick, ObjectiveResult.Met, string.Create(Ic, $"{s.Actor} was out of {AreaWords(s)} at {now:0.##} s ({across:0.##} m from its middle)"), -1);
                        return;
                    }
                }
                if (OwnerDown(s, out var w, out int c)) { Decide(k, tick, ObjectiveResult.Failed, w, c); return; }
                break;
            }
            case ObjectiveKind.Hold:
            {
                if (OwnerDown(s, out var w, out int c)) { Decide(k, tick, ObjectiveResult.Failed, w, c); return; }
                if (Feet(owner) is { } f && !Inside(s, f, out float across))
                {
                    Decide(k, tick, ObjectiveResult.Failed, string.Create(Ic, $"{s.Actor} left {AreaWords(s)} at {now:0.##} s ({across:0.##} m from its middle)"), -1);
                    return;
                }
                break;
            }
        }
        // Times: a deadline passed undone fails; an end reached kept meets.
        if (s.BySeconds is { } by && now >= by - 1e-9)
        {
            Decide(k, tick, ObjectiveResult.Failed, string.Create(Ic, $"not done by {by:0.##} s ({Pending(k, s)})"), -1);
            return;
        }
        if (s.UntilSeconds is { } until && now >= until - 1e-9)
            Decide(k, tick, ObjectiveResult.Met, string.Create(Ic, $"kept to {until:0.##} s ({Kept(s)})"), -1);
    }

    IEnumerable<int> Foes(int owner)
    {
        string? side = SideOf(owner);
        for (int i = 0; i < fighters.Count; i++)
        {
            if (i == owner) continue;
            if (side is not null) { if (SideOf(i) is { } si && si != side) yield return i; }
            else if (cast.Fighters[owner].Foe == i || cast.Fighters[i].Foe == owner) yield return i;
        }
    }

    string Pending(int k, ObjectiveSpec s) => s.Kind switch
    {
        ObjectiveKind.Defeat => s.Target is { } t ? $"{t} still stands" : $"not every actor of side {s.TargetSide} reached a harness stop",
        ObjectiveKind.Reach => string.Create(Ic, $"{s.Actor} came within {closest[k]:0.##} m of the middle of {AreaWords(s)} at the nearest"),
        ObjectiveKind.Escape => s.FromFoesMetres is { } d ? string.Create(Ic, $"its nearest foe still standing never more than {d:0.##} m off together; nearest at its best {closest[k]:0.##} m") : $"still in {AreaWords(s)}",
        _ => "",
    };

    string Kept(ObjectiveSpec s) => s.Kind switch
    {
        ObjectiveKind.Survive => s.Actor is { } a ? $"{a} never reached a harness stop" : $"an actor of side {s.Side} still stood",
        ObjectiveKind.Protect => $"{s.Target} never reached a harness stop",
        ObjectiveKind.Hold => $"{s.Actor} stayed in {AreaWords(s)}",
        _ => "",
    };

    /// <summary>
    /// Whether the objectives end the run now (<c>stop.onObjectives</c>): <c>first</c>, one decided on this tick; <c>all</c>, every one decided
    /// — a survive, protect or hold kept for the whole run is decided only by failing, so once only those are left (and there was another)
    /// they are met and the run ends. The stop's name, or null.
    /// </summary>
    public string? StopNow(string mode)
    {
        if (mode == "first") return DecidedNow > 0 ? StopNames.ObjectivesPrefix + "first" : null;
        if (mode != "all") return null;
        bool anyOther = specs.Any(s => !s.WholeRun);
        for (int k = 0; k < specs.Count; k++)
            if (result[k] == ObjectiveResult.Unresolved && (!specs[k].WholeRun || !anyOther)) return null;
        return result.Any(r => r == ObjectiveResult.Unresolved) || DecidedNow > 0 ? StopNames.ObjectivesPrefix + "all" : null;
    }

    /// <summary>
    /// The run's end (<paramref name="stop"/> at <paramref name="endTick"/>): what is kept for the whole run and was not lost is met; what is
    /// still open is UNRESOLVED. Returns each objective's outcome and the ending in words (which stops and objectives decided it).
    /// </summary>
    public (List<ObjectiveOutcome> Outcomes, string Ending) Finish(string stop, long endTick)
    {
        long tick = endTick - 1;
        double end = endTick / (double)World.TicksPerSecond;
        for (int k = 0; k < specs.Count; k++)
        {
            if (result[k] != ObjectiveResult.Unresolved) continue;
            var s = specs[k];
            if (s.WholeRun) Decide(k, tick, ObjectiveResult.Met, string.Create(Ic, $"kept to the run's end at {end:0.##} s ({Kept(s)}; the run ended {stop})"), -1);
            else reason[k] = string.Create(Ic, $"the run ended ({stop}) at {end:0.##} s before it was decided{(s.UntilSeconds is { } u ? $": {u:0.##} s not reached" : "")}{(Pending(k, s) is { Length: > 0 } p ? $"; {p}" : "")}");
        }
        var outcomes = new List<ObjectiveOutcome>();
        for (int k = 0; k < specs.Count; k++)
        {
            var s = specs[k];
            outcomes.Add(new ObjectiveOutcome(s.Name, s.KindWord, s.Actor ?? $"side {s.Side}", Name(result[k]),
                result[k] == ObjectiveResult.Unresolved ? null : Math.Round(decidedTick[k] / (double)World.TicksPerSecond, 4), reason[k],
                eventId[k] >= 0 ? eventId[k] : null, cause[k] >= 0 ? cause[k] : null));
        }
        return (outcomes, Ending(stop, tick, outcomes));
    }

    string Ending(string stop, long tick, List<ObjectiveOutcome> outcomes)
    {
        var parts = new List<string>();
        var atEnd = Enumerable.Range(0, specs.Count).Where(k => result[k] != ObjectiveResult.Unresolved && decidedTick[k] >= tick - 1 && !(specs[k].WholeRun && result[k] == ObjectiveResult.Met && reason[k].StartsWith("kept to the run's end", StringComparison.Ordinal))).ToList();
        if (stop.StartsWith(StopNames.ObjectivesPrefix, StringComparison.Ordinal))
            parts.Add($"ended by its objectives ({stop["OBJECTIVES:".Length..]}): " + string.Join("; ", atEnd.Select(k => $"\"{specs[k].Name}\" {Name(result[k])} — {reason[k]}")));
        else if (stop is StopNames.ZeroHealth or StopNames.Severed or StopNames.Disarmed or "SIDE_LEFT")
        {
            var down = Enumerable.Range(0, fighters.Count).Where(i => Stopped(i) && stoppedTick[i] >= tick - 1).Select(StopWords).ToList();
            parts.Add($"ended by a harness stop ({stop}): {(down.Count > 0 ? string.Join("; ", down) : "as the tally says")}");
            if (atEnd.Count > 0) parts.Add("objectives decided with it: " + string.Join("; ", atEnd.Select(k => $"\"{specs[k].Name}\" {Name(result[k])}")));
        }
        else parts.Add($"ended by {stop}{(atEnd.Count > 0 ? "; objectives decided with it: " + string.Join("; ", atEnd.Select(k => $"\"{specs[k].Name}\" {Name(result[k])}")) : "")}");
        var open = outcomes.Where(o => o.Result == "UNRESOLVED").Select(o => o.Name).ToList();
        if (open.Count > 0) parts.Add($"unresolved: {string.Join(", ", open)}");
        return string.Join("; ", parts);
    }

    public static string Name(ObjectiveResult r) => r switch { ObjectiveResult.Met => "MET", ObjectiveResult.Failed => "FAILED", _ => "UNRESOLVED" };

    /// <summary>A point's objectives over its runs.</summary>
    public static List<ObjectiveAggregate> Aggregate(IReadOnlyList<ObjectiveSpec> specs, IReadOnlyList<(string RunId, IReadOnlyList<ObjectiveOutcome> Outcomes)> runs)
    {
        var list = new List<ObjectiveAggregate>();
        foreach (var s in specs)
        {
            var mine = runs.Select(r => (r.RunId, O: r.Outcomes.FirstOrDefault(o => o.Name == s.Name))).Where(x => x.O is not null).ToList();
            int met = mine.Count(x => x.O!.Result == "MET"), failed = mine.Count(x => x.O!.Result == "FAILED"), open = mine.Count - met - failed;
            double? Median(IEnumerable<double> xs) { var v = xs.Order().ToList(); return v.Count == 0 ? null : v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2; }
            var examples = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var res in new[] { "MET", "FAILED", "UNRESOLVED" })
                if (mine.FirstOrDefault(x => x.O!.Result == res) is { RunId: { } id }) examples[res] = id;
            var reasons = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var x in mine) { string key = $"{x.O!.Result}: {Generic(x.O.Reason)}"; reasons[key] = (reasons.TryGetValue(key, out int c) ? c : 0) + 1; }
            list.Add(new ObjectiveAggregate(s.Name, s.KindWord, s.Actor ?? $"side {s.Side}", mine.Count, met, failed, open,
                mine.Count > 0 ? Math.Round(met / (double)mine.Count, 6) : null,
                mine.Count > 0 && ScenarioStats.Wilson(met, mine.Count) is { } w ? [Math.Round(w[0], 6), Math.Round(w[1], 6)] : null,
                Median(mine.Where(x => x.O!.Result == "MET" && x.O.Seconds is not null).Select(x => x.O!.Seconds!.Value)),
                Median(mine.Where(x => x.O!.Result == "FAILED" && x.O.Seconds is not null).Select(x => x.O!.Seconds!.Value)),
                examples, reasons.Count <= 12 ? reasons : null));
        }
        return list;
    }

    /// <summary>A reason with its numbers taken out, so like reasons are counted together (<c>Guard reached ZERO_HEALTH_STOP at # s …</c>).</summary>
    static string Generic(string reason) => System.Text.RegularExpressions.Regex.Replace(reason, @"-?\d+(\.\d+)?", "#");
}
