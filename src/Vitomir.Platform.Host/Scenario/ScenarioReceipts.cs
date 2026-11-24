// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Host.Arena;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Powers;

namespace Vitomir.Platform.Host.Scenario;

// ---- the per-run receipts: Health's books, the defences, the blows' outcomes, the
// power activations with Celerity's rights, the effects by source, the decisions. Written in actors.csv (columns at the end), activations.csv
// and results.json (`receipts`, the runs whose streams are kept). Read from the run's events and the world; nothing reads them back. ----

/// <summary>An actor's Health and Blood at a moment: the purchased maximum (the sheet's; the bands' denominator), the
/// effective maximum (lowered by a part lost: healing stops there), the Health now, the wounds by region and the parts severed so far, the
/// band's label and the harness stop (<c>ZERO_HEALTH_STOP</c>, <c>SEVERED_STOP</c>: never death), Blood.</summary>
public sealed record HealthBook(double? PurchasedMax, double? EffectiveMax, double? Current, string Band, double? Blood, double? BloodMax,
    IReadOnlyDictionary<string, int> WoundsByRegion, IReadOnlyList<string> Severed, string? HarnessStop, bool FinalDeath = false);

/// <summary>The defences an actor made against blows that came to it: each blow met at its guard or shield, or dodged, by kind; a
/// blade stopped at a guard is counted here, never as a miss.</summary>
public sealed record DefenceCounts(int GuardHeld, int ReactiveBlock, int TimedParry, int Dodge, int ShieldInterception, int BladeInterception, int Clash);

/// <summary>What became of an actor's own blows (each swing once, by its <c>SwingEnded</c>): landed on a body, stopped at a guard or shield,
/// dodged, missed (met nothing, no dodge), met the level.</summary>
public sealed record BlowOutcomes(int Swings, int Landed, int Stopped, int Dodged, int Missed, int MetLevel);

/// <summary>
/// One extra blow of a Celerity swing: a right Celerity granted, exercised as a
/// <b>logical strike</b> — its own id (<see cref="Strike"/> = <see cref="FlurryBlows.Id"/>: contact × 16 + its place), the swing and the one
/// physical contact it came from (<see cref="ParentContact"/>: no swing of its own, no contact of its own), its moment, what its own
/// check met then (<see cref="Check"/>: <c>Met</c> and the <see cref="Region"/>; <c>Guarded</c> and which <see cref="Guard"/> (with the part
/// behind it); <c>Dodged</c>; <c>OutOfReach</c>; <c>AttackerStopped</c>; <c>TargetDown</c>; <c>Compressed</c> under <c>flurry-compressed</c>), the
/// harm it caused if any, and <see cref="Resolution"/> in words. <see cref="Used"/>: the right was exercised as a strike (met the body or
/// a guard); <see cref="Hit"/>: the rules resolved harm on the body for it (a guarded blow is a hit only when it overpowered the guard;
/// an out-of-reach or dodged one never). Never folded into the swing's own contact: that contact's own blow is strike 0, not here.
/// </summary>
public sealed record CelerityRight(long Strike, long ParentContact, int Swing, int Index, int Of, string Check, double Seconds, bool Used,
    bool RecheckedAtOwnMoment, int Wounds, string? Defence,
    string Attacker = "", string? Target = null, string? Region = null, string? Guard = null, string? GuardVerdict = null, bool Hit = false,
    int? HealthBefore = null, int? HealthAfter = null, string? ResistedBy = null, double MovedMetres = 0, string Resolution = "", long? ActionId = null);

/// <summary>The words for how an extra blow was resolved (<see cref="CelerityRight.Resolution"/>).</summary>
public static class ExtraBlowResolution
{
    public const string Sweep = "line sweep at its own moment (no swing of its own)";
    public const string Compressed = "resolved on the swing's contact (flurry-compressed: no check of its own)";
}

/// <summary>
/// A power activation tried: paid (<c>activated</c>, its window, Blood and action id), renewed, or <c>denied</c> with its
/// reason in words (not learned, not enough Blood, recast running, an empty slot; or a script's step refused: no slot shows it); an
/// unrecognised power is denied, never an observed zero-magnitude use. For Celerity, its rights: the swings begun while live (its tempo),
/// the extra blows its landed swings carried (granted), those used, those that found nothing (unused, and why), the physical contacts
/// the window's swings made and the strikes that wounded.
/// </summary>
public sealed record PowerActivation(string Power, string? Option, string Result, string? Why, long ActionId, int Rank, double Seconds,
    double? EndSeconds, string? EndCause, int Cost, int? BloodBefore, int? BloodAfter, string Source,
    int SwingsLive = 0, int RightsGranted = 0, int RightsUsed = 0, int RightsUnused = 0, string? UnusedWhy = null,
    int PhysicalContacts = 0, int StrikesWounding = 0, double? TempoSwing = null, IReadOnlyList<CelerityRight>? Rights = null, double? TempoSwingBefore = null);

/// <summary>An effect on an actor and its source: <c>purchased</c> (the sheet's own value), <c>resting</c> (a learned passive power:
/// always on, free), <c>activated</c> (paid: its action id).</summary>
public sealed record EffectSource(string Effect, string Source, string? Power = null, int Rank = 0, long? ActionId = null, double? Seconds = null);

/// <summary>An actor's receipts in one run.</summary>
public sealed record ActorReceipts(string ActorId, string Name, HealthBook Start, HealthBook End, DefenceCounts Defences, BlowOutcomes Blows,
    IReadOnlyList<PowerActivation> Activations, IReadOnlyList<EffectSource> Effects, int? Decisions, int? DecisionsNotExecuted);

/// <summary>One run's receipts (results.json's <c>receipts</c>, for the runs whose streams are kept).</summary>
public sealed record RunReceipts(string RunId, ulong Seed, IReadOnlyList<ActorReceipts> Actors, IReadOnlyList<CelerityRight>? ExtraBlows = null);   // every extra blow of the run, a row each (strikes.csv)

/// <summary>
/// Collects one run's receipts: <see cref="Collect"/> after each tick (the vitals: the first seen is the start, the
/// last the end), <see cref="Finish"/> at its end (the rest from the log's events). Reads only the world and the events.
/// </summary>
public sealed class ScenarioReceipts(World world, IReadOnlyList<EntityId> actors, IReadOnlyList<string> names, ScenarioEventLog log)
{
    static readonly CultureInfo Ic = CultureInfo.InvariantCulture;
    readonly (double Health, double Max, double Cap, double Blood, double BloodMax, bool HasBlood, HealthBand Band)?[] first = new (double, double, double, double, double, bool, HealthBand)?[actors.Count];
    readonly (double Health, double Max, double Cap, double Blood, double BloodMax, bool HasBlood, HealthBand Band)?[] last = new (double, double, double, double, double, bool, HealthBand)?[actors.Count];
    readonly string?[] sheets = new string?[actors.Count];
    bool sheetsRead;

    /// <summary>Reads the vitals after a tick.</summary>
    public void Collect()
    {
        if (!sheetsRead)
        {
            sheetsRead = true;
            if (world.TryService<IPersonSheets>(out var ps))
                for (int i = 0; i < actors.Count; i++) if (ps.TryDescribe(actors[i], out string json)) sheets[i] = json;
        }
        if (!world.TryStore<Vitals>(out var vitals)) return;
        for (int i = 0; i < actors.Count; i++)
        {
            ref var v = ref vitals.Find(actors[i]);
            if (Unsafe.IsNullRef(ref v) || v.HealthMax <= 0f) continue;
            var now = ((double)v.Health, (double)v.HealthMax, (double)(v.HealthCap > 0f ? v.HealthCap : v.HealthMax), (double)v.Blood, (double)v.BloodMax, v.HasBlood, v.Band);
            first[i] ??= now;
            last[i] = now;
        }
    }

    /// <summary>The run's receipts.</summary>
    public RunReceipts Finish(string runId, ulong seed)
    {
        var list = new List<ActorReceipts>();
        for (int i = 0; i < actors.Count && i < names.Count; i++) list.Add(Of(i));
        var blows = new List<CelerityRight>();
        foreach (var e in log.Events) if (e.Raw is FlurryStrike fs) blows.Add(Blow(fs, e));
        return new RunReceipts(runId, seed, list, blows);
    }

    ActorReceipts Of(int i)
    {
        string me = names[i];
        var events = log.Events;
        // Health's books: the wounds by region, the parts severed, the stop.
        var wounds = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var severed = new List<string>();
        string? stop = null;
        var defences = new int[7];
        int swings = 0, landed = 0, stopped = 0, dodged = 0, missed = 0, level = 0, decisions = 0, notExecuted = 0;
        foreach (var e in events)
        {
            switch (e.Raw)
            {
                case HarmResolved h when e.On == me && h.Wounds > 0:
                {
                    string region = e.Field("harmRegion") as string ?? "unknown";
                    if (e.Field("harmSide") is string side) region += " " + side;
                    wounds[region] = wounds.GetValueOrDefault(region) + h.Wounds;
                    break;
                }
                case PartSevered ps when e.On == me:
                    severed.Add(BodyRegions.Name(ps.Region));
                    break;
                case ActorDied ad when e.On == me:
                    stop ??= ArenaTables.StopName(ad.Stop);
                    break;
                case ContactMade when e.On == me:
                    Count(defences, e.Field("defence") as string);
                    break;
                case SwingEnded when e.By == me:
                    swings++;
                    switch (e.Field("outcome") as string)
                    {
                        case "landed": landed++; break;
                        case "stopped": stopped++; break;
                        case "dodged": dodged++; break;
                        case "metLevel": level++; break;
                        default: missed++; break;
                    }
                    break;
                case BrainDecided when e.By == me:
                    decisions++;
                    if (e.Field("executed") is false) notExecuted++;
                    break;
            }
            if (e.Kind == "SwingEnded" && e.Field("defender") as string == me && e.Field("defence") as string == "dodge") defences[3]++;
        }
        var start = Book(first[i], new SortedDictionary<string, int>(), [], null);
        var end = Book(last[i], wounds, severed, stop);
        var (activations, effects) = Activations(me, i);
        return new ActorReceipts($"a{i.ToString(Ic)}", me, start, end,
            new DefenceCounts(defences[0], defences[1], defences[2], defences[3], defences[4], defences[5], defences[6]),
            new BlowOutcomes(swings, landed, stopped, dodged, missed, level), activations, effects,
            BrainDecisions.On(world) ? decisions : null, BrainDecisions.On(world) ? notExecuted : null);   // null: decisions not asked for in this run
    }

    static void Count(int[] d, string? defence)
    {
        switch (defence)
        {
            case "guardHeld": d[0]++; break;
            case "reactiveBlock": d[1]++; break;
            case "timedParry": d[2]++; break;
            case "shieldInterception": d[4]++; break;
            case "bladeInterception": d[5]++; break;
            case "clash": d[6]++; break;
        }
    }

    static HealthBook Book((double Health, double Max, double Cap, double Blood, double BloodMax, bool HasBlood, HealthBand Band)? v,
        IReadOnlyDictionary<string, int> wounds, IReadOnlyList<string> severed, string? stop) =>
        v is not { } x
            ? new HealthBook(null, null, null, "", null, null, wounds, severed, stop)
            : new HealthBook(Math.Round(x.Max, 3), Math.Round(x.Cap, 3), Math.Round(x.Health, 3), x.Band.ToString(),
                x.HasBlood ? Math.Round(x.Blood, 3) : null, x.HasBlood ? Math.Round(x.BloodMax, 3) : null, wounds, severed,
                stop ?? (x.Band == HealthBand.Stopped ? "ZERO_HEALTH_STOP" : null));

    /// <summary>Words for a power's refusal (the module's typed reason).</summary>
    public static string WhyDenied(PowerDenial d) => d switch
    {
        PowerDenial.EmptySlot => "EmptySlot: the slot shows no power",
        PowerDenial.Unlearned => "Unlearned: the power or its option is not learned",
        PowerDenial.NoSemantics => "NoSemantics: no rules for this rank (unrecognised: nothing cast, never a zero-magnitude use)",
        PowerDenial.NoBlood => "NoBlood: not enough Blood",
        PowerDenial.Recast => "Recast: its recast is still running",
        PowerDenial.Dead => "Dead: the person is stopped (zero Health)",
        PowerDenial.NoSheet => "NoSheet: the person has no sheet",
        PowerDenial.Passive => "Passive: resting (always on, free): nothing to use",
        PowerDenial.NoEffect => "NoEffect: it would do nothing now",
        _ => d.ToString(),
    };

    (List<PowerActivation>, List<EffectSource>) Activations(string me, int i)
    {
        var events = log.Events;
        var acts = new List<PowerActivation>();
        var effects = new List<EffectSource>();
        // Resting: the sheet's learned passive powers (always on, free; read by the rules where they apply).
        if (sheets[i] is { } json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement.TryGetProperty("sheet", out var sh) ? sh : doc.RootElement;
                if (root.TryGetProperty("powers", out var powers) && powers.ValueKind == JsonValueKind.Array)
                    foreach (var p in powers.EnumerateArray())
                    {
                        string power = p.TryGetProperty("power", out var pn) ? pn.GetString() ?? "" : "";
                        int rank = p.TryGetProperty("rank", out var rk) && rk.TryGetInt32(out int r) ? r : 0;
                        if (rank <= 0 || !p.TryGetProperty("options", out var opts) || opts.ValueKind != JsonValueKind.Array) continue;
                        foreach (var o in opts.EnumerateArray())
                        {
                            string opt = o.GetString() ?? "";
                            if (opt.EndsWith(".passive", StringComparison.Ordinal)) effects.Add(new EffectSource($"{power} (passive)", "resting", power, rank));
                        }
                    }
            }
            catch (JsonException) { }
        }
        var ends = new Dictionary<long, (long Tick, string Cause)>();
        foreach (var e in events) if (e.By == me && e.Raw is PowerEnded pe && pe.ActionId != 0) ends[pe.ActionId] = (pe.Tick, pe.Cause.ToString());
        foreach (var e in events)
        {
            if (e.By != me) continue;
            switch (e.Raw)
            {
                case PowerUsed u:
                {
                    string power = FighterTally.PowerOf(u.Power);
                    long endTick = ends.TryGetValue(u.ActionId, out var en) ? Math.Min(en.Tick, u.EndTick > 0 ? u.EndTick : en.Tick) : u.EndTick;
                    var act = new PowerActivation(power, u.Power, u.Renewed ? "renewed" : "paid", null, u.ActionId, u.Rank, Sec(u.Tick),
                        u.EndTick > 0 ? Sec(u.EndTick) : null, ends.TryGetValue(u.ActionId, out var ec) ? ec.Cause : null, u.Cost, u.BloodBefore, u.BloodAfter, "activated");
                    if (power == "celerity") act = Rights(act, me, u.Tick, endTick > u.Tick ? endTick : long.MaxValue);
                    acts.Add(act);
                    effects.Add(new EffectSource(power == "celerity" ? "tempo and flurry" : u.Amount != 0 ? $"{power} +{u.Amount.ToString(Ic)}" : power,
                        "activated", power, u.Rank, u.ActionId, u.Seconds));
                    break;
                }
                case PowerRefused pr:
                    acts.Add(new PowerActivation(pr.Power is null ? "" : FighterTally.PowerOf(pr.Power), pr.Power, "denied", WhyDenied(pr.Reason), 0, 0, e.Seconds,
                        null, null, 0, null, null, "none"));
                    break;
                case StepRefused sr when sr.Kind == ScenarioStepKind.UsePower:
                    acts.Add(new PowerActivation(sr.Label ?? "", null, "denied",
                        sr.Why == StepRefusal.NoSlot ? "NoSlot: no quick slot shows it (not learned, or rank 0): the script's step refused, nothing pressed" : sr.Why.ToString(),
                        0, 0, e.Seconds, null, null, 0, null, null, "none"));
                    break;
            }
        }
        return (acts, effects);
    }

    static double Sec(long tick) => Math.Round(tick / (double)World.TicksPerSecond, 4);

    Dictionary<long, HarmResolved>? harmOfStrike;
    Dictionary<string, List<(long From, long To, long Id)>>? windows;

    /// <summary>An extra blow's whole row: what its check met, the harm (if any) and how it was resolved; the Celerity activation live then.</summary>
    CelerityRight Blow(FlurryStrike s, ScenarioEvent e)
    {
        if (harmOfStrike is null)
        {
            harmOfStrike = [];
            windows = new(StringComparer.Ordinal);
            foreach (var x in log.Events)
            {
                if (x.Raw is HarmResolved h && h.Flurry > 0) harmOfStrike[h.Strike] = h;
                if (x.Raw is PowerUsed u && x.By is { } by && FighterTally.PowerOf(u.Power) == "celerity")
                    (windows.TryGetValue(by, out var l) ? l : windows[by] = []).Add((u.Tick, u.EndTick > u.Tick ? u.EndTick : long.MaxValue, u.ActionId));
            }
        }
        bool harmed = harmOfStrike.TryGetValue(s.Strike, out var hr);
        string? region = s.Region == BodyRegion.None ? null : BodyRegions.Name(s.Region);
        string? guard = s.Guard == GuardMeet.None ? null : s.Guard.ToString().ToLowerInvariant();
        string? verdict = s.Check != FlurryCheck.Guarded ? null
            : harmed ? "overpowered (it went on to the part behind the guard)" : "held or parried (no harm; the verdict is in the harm trace)";
        long? action = null;
        if (e.By is { } who && windows!.TryGetValue(who, out var ws))
            foreach (var w in ws) if (e.Tick >= w.From && e.Tick < w.To) action = w.Id;
        return new CelerityRight(s.Strike, s.ContactId, s.Swing, s.Index, s.Count, s.Check.ToString(), e.Seconds,
            s.Check is FlurryCheck.Met or FlurryCheck.Guarded or FlurryCheck.Compressed, s.Check != FlurryCheck.Compressed,
            harmed ? hr.Wounds : 0, e.Field("defence") as string, e.By ?? "", e.On, region, guard, verdict, harmed,
            harmed ? hr.HealthBefore : null, harmed ? hr.HealthAfter : null, harmed ? ArenaTables.ResistedName(hr) : null,
            Math.Round(s.Moved, 3), s.Check == FlurryCheck.Compressed ? ExtraBlowResolution.Compressed : ExtraBlowResolution.Sweep, action);
    }

    /// <summary>Celerity's rights in its live window [from, to): the swings begun, the extra blows granted, used and unused, the contacts.</summary>
    PowerActivation Rights(PowerActivation act, string me, long from, long to)
    {
        int swingsLive = 0;
        var contacts = new HashSet<long>();
        var granted = new Dictionary<int, int>();            // swing → extra blows its landing carried
        var strikes = new List<(FlurryStrike S, ScenarioEvent E)>();
        double tempo = 0;
        var swingOf = new Dictionary<long, int>();
        foreach (var e in log.Events) if (e.By == me && e.Raw is ContactMade sc && sc.Shot == 0) swingOf[sc.ContactId] = sc.Swing;
        double before = 0;
        foreach (var e in log.Events)
        {
            if (e.Tick < from && e.By == me && e.Raw is ScenarioEventLog.TempoLine tb) before = tb.Swing;   // the body's swing tempo before the window (its skill's)
            if (e.Tick < from || e.Tick >= to) continue;
            switch (e.Raw)
            {
                case AttackBegun when e.By == me: swingsLive++; break;
                case ContactMade c when e.By == me && c.Shot == 0: contacts.Add(c.ContactId); break;
                case HarmResolved h when e.By == me:
                    if (h.Flurry == 0 && h.FlurryBlows > 0 && swingOf.TryGetValue(h.ContactId, out int hs))
                        granted[hs] = Math.Max(granted.GetValueOrDefault(hs), h.FlurryBlows);
                    break;
                case FlurryStrike fs when e.By == me:
                    strikes.Add((fs, e));
                    granted[fs.Swing] = Math.Max(granted.GetValueOrDefault(fs.Swing), fs.Count);
                    break;
                case ScenarioEventLog.TempoLine t when e.By == me: tempo = Math.Max(tempo, t.Swing); break;
            }
        }
        var rights = new List<CelerityRight>();
        var unusedWhy = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int used = 0;
        foreach (var (s, e) in strikes)
        {
            bool u = s.Check is FlurryCheck.Met or FlurryCheck.Guarded or FlurryCheck.Compressed;
            if (u) used++;
            else unusedWhy[s.Check.ToString()] = unusedWhy.GetValueOrDefault(s.Check.ToString()) + 1;
            rights.Add(Blow(s, e) with { ActionId = act.ActionId });
        }
        int total = granted.Values.Sum();
        int notTried = Math.Max(0, total - strikes.Count);
        if (notTried > 0) unusedWhy["NotTried"] = notTried;
        return act with
        {
            SwingsLive = swingsLive, RightsGranted = total, RightsUsed = used, RightsUnused = total - used,
            UnusedWhy = unusedWhy.Count == 0 ? null : string.Join(";", unusedWhy.Select(kv => $"{kv.Key}:{kv.Value.ToString(Ic)}")),
            PhysicalContacts = contacts.Count, StrikesWounding = rights.Count(r => r.Hit && r.Wounds > 0), TempoSwing = tempo > 0 ? Math.Round(tempo, 4) : null,
            TempoSwingBefore = before > 0 ? Math.Round(before, 4) : null,
            Rights = rights,
        };
    }
}
