// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Text;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Host.Arena;

namespace Vitomir.Platform.Host.Scenario;

/// <summary>
/// The scenario's normalized tables (long form, stable join keys): <c>runs.csv</c> (a row a run),
/// <c>actors.csv</c> (a row an actor a run: who it was and its counts, start and end), <c>actions.csv</c> (a row an action tried:
/// admitted or denied, with why and what it met), <c>sweeps.csv</c> (a row a point: its values and its run counts). Every row carries
/// <c>run_id</c> (<c>p000-r003</c>), the point, the run and the seed; times in seconds, distances in metres.
/// </summary>
public static class ScenarioTables
{
    static readonly CultureInfo Ic = CultureInfo.InvariantCulture;

    /// <summary>A field as CSV (quoted when it holds a comma, a quote or a line break).</summary>
    public static string Csv(string? f)
    {
        f ??= "";
        return f.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + f.Replace("\"", "\"\"") + "\"" : f;
    }

    static string N(double? v) => v is { } x ? ArenaTables.Num(x) : "";

    static void Line(StringBuilder sb, IEnumerable<string> fields) => sb.Append(string.Join(",", fields.Select(Csv))).Append('\n');

    // ---- actors.csv ----

    /// <summary>
    /// At the end of <c>actors.csv</c> (<see cref="ScenarioReceipts"/>): the defences it made against blows that came
    /// to it (a blade stopped at its guard is counted here, never as the attacker's miss); what became of its own blows; Health's books at
    /// the start and the end (purchased and effective maximum, Health now, its band, the wounds by region, the parts severed, the harness
    /// stop: never death); its power activations paid and denied (and why), Celerity's rights granted, used and unused; the effects on it by
    /// source (resting, activated); the brain's decisions and those not executed (traced runs only: empty otherwise).
    /// </summary>
    public static readonly string[] ReceiptColumns =
    [
        "def_guard_held", "def_reactive_block", "def_timed_parry", "def_dodge", "def_shield_interception", "def_blade_interception", "def_clash",
        "blows_landed", "blows_stopped", "blows_dodged", "blows_missed", "blows_met_level",
        "health_max_purchased_start", "health_max_effective_start", "health_max_purchased_end", "health_max_effective_end", "health_current_end",
        "band_end", "wounds_by_region", "severed_parts", "harness_stop", "final_death",
        "activations_paid", "activations_denied", "denied_why", "celerity_rights_granted", "celerity_rights_used", "celerity_rights_unused",
        "effects", "decisions", "decisions_not_executed",
    ];

    public static readonly string[] ActorHeader =
    [
        "run_id", "point", "run", "seed", "actor_id", "actor", "template", "weapon", "shield", "armor", "side", "brain",
        .. BuiltInMeasures.PerActor.Select(b => b.Column), "blood_start", "blood_end", "severances", "staggers_given", "flurry_blows",
        "tempo", "clip_rates",   // its own combat tempo (empty: the global) and its clips' rates (clip=rate;…, empty: none)
        .. ReceiptColumns,       // at the end
    ];


    /// <summary>An actor's row per run: who it was (template, weapon, gear, side, brain) and its built-in counts, Health and Blood at the start and the end.</summary>
    public static List<string[]> ActorRows(string runId, ScenarioPlanPoint point, int run, ulong seed, IReadOnlyList<string> names, ArenaCast cast, BoutResult result,
        RunReceipts? receipts = null)
    {
        var rows = new List<string[]>();
        for (int i = 0; i < result.Fighters.Length && i < names.Count; i++)
        {
            var f = result.Fighters[i];
            var c = i < cast.Fighters.Count ? cast.Fighters[i] : null;
            var row = new List<string>
            {
                runId, point.Index.ToString(Ic), run.ToString(Ic), seed.ToString(Ic), $"a{i}", names[i], c?.Template ?? "", c?.Weapon ?? "",
                c?.Shield is { } sh ? (sh ? "true" : "false") : "", string.Join("+", c?.Armor ?? []), c?.Side ?? "", c?.Brain.ToString().ToLowerInvariant() ?? "",
            };
            foreach (var b in BuiltInMeasures.PerActor) row.Add(N(Value(f, b.Name, result)));
            row.AddRange([N(f.BloodStart), N(f.BloodEnd), f.Severances.ToString(Ic), f.StaggersGiven.ToString(Ic), f.FlurryBlows.ToString(Ic)]);
            row.AddRange([c?.Tempo is { } tempo ? tempo.ToString("R", Ic) : "",
                string.Join(";", (c?.ClipRates ?? []).Select(r => $"{r.Clip}={r.Rate.ToString("R", Ic)}"))]);
            row.AddRange(ReceiptCells(receipts is not null && i < receipts.Actors.Count ? receipts.Actors[i] : null));
            rows.Add([.. row]);
        }
        return rows;
    }

    static double? Value(FighterTally f, string name, BoutResult r) => name switch
    {
        "swings" => f.Swings, "strikes" => f.Strikes, "contactsBody" => f.ContactsBody, "contactsShield" => f.ContactsShield,
        "contactsWeapon" => f.ContactsWeapon, "contactsLevel" => f.ContactsLevel, "hits" => f.Hits, "woundsDealt" => f.WoundsDealt,
        "woundsTaken" => f.WoundsTaken, "healthStart" => f.HealthStart, "healthEnd" => f.HealthEnd, "bloodSpent" => f.BloodSpent,
        "powerUses" => f.Uses, "renewals" => f.Renewals, "powersRefused" => f.PowersRefused,
        "liveSeconds" => f.LiveAnyTicks / (double)Core.World.TicksPerSecond, "dodges" => f.DodgesBegun, "guardHeld" => f.GuardHeld,
        "guardParried" => f.GuardParried, "guardOverpowered" => f.GuardOverpowered, "staggers" => f.Staggered, "stopped" => f.Stopped ? 1 : 0,
        _ => null,
    };

    /// <summary>An actor's receipt cells (<see cref="ReceiptColumns"/>); empty when none.</summary>
    static IEnumerable<string> ReceiptCells(ActorReceipts? a)
    {
        if (a is null) return Enumerable.Repeat("", ReceiptColumns.Length);
        string I(int v) => v.ToString(Ic);
        var d = a.Defences;
        var b = a.Blows;
        var paid = a.Activations.Where(x => x.Result is "paid" or "renewed").ToList();
        var denied = a.Activations.Where(x => x.Result == "denied").ToList();
        var cel = paid.Where(x => x.Power == "celerity").ToList();
        return
        [
            I(d.GuardHeld), I(d.ReactiveBlock), I(d.TimedParry), I(d.Dodge), I(d.ShieldInterception), I(d.BladeInterception), I(d.Clash),
            I(b.Landed), I(b.Stopped), I(b.Dodged), I(b.Missed), I(b.MetLevel),
            N(a.Start.PurchasedMax), N(a.Start.EffectiveMax), N(a.End.PurchasedMax), N(a.End.EffectiveMax), N(a.End.Current),
            a.End.Band, string.Join(";", a.End.WoundsByRegion.Select(kv => $"{kv.Key}:{kv.Value.ToString(Ic)}")), string.Join(";", a.End.Severed),
            a.End.HarnessStop ?? "", a.End.FinalDeath ? "true" : "false",
            I(paid.Count), I(denied.Count), string.Join(";", denied.Select(x => $"{x.Power}:{(x.Why ?? "").Split(':')[0]}")),
            I(cel.Sum(x => x.RightsGranted)), I(cel.Sum(x => x.RightsUsed)), I(cel.Sum(x => x.RightsUnused)),
            string.Join(";", a.Effects.Select(e => $"{e.Effect}:{e.Source}{(e.ActionId is { } id ? "#" + id.ToString(Ic) : "")}")),
            N(a.Decisions), N(a.DecisionsNotExecuted),
        ];
    }

    // ---- activations.csv ----

    /// <summary><c>activations.csv</c>' columns: a row a power activation tried (paid, renewed or denied), with Celerity's rights.</summary>
    public static readonly string[] ActivationHeader =
    [
        "run_id", "point", "run", "seed", "actor_id", "actor", "power", "option", "result", "why", "action_id", "rank", "start_seconds", "end_seconds",
        "end_cause", "cost", "blood_before", "blood_after", "source", "swings_live", "tempo_swing_live", "tempo_swing_before", "rights_granted", "rights_used", "rights_unused",
        "unused_why", "physical_contacts", "strikes_wounding", "rights",
    ];

    /// <summary><c>activations.csv</c>: a row an activation tried, every run (<see cref="ActivationHeader"/>); <c>rights</c> lists each extra
    /// blow as <c>strike/parentContact:check:used:hit</c> (<c>2321/145:Met:1:1</c>; each extra blow's whole row is in <c>strikes.csv</c>).</summary>
    internal static void WriteActivations(string path, IEnumerable<ScenarioRunner.RunOutput> done)
    {
        var sb = new StringBuilder();
        Line(sb, ActivationHeader);
        foreach (var o in done.OrderBy(o => o.Point).ThenBy(o => o.Run))
        {
            if (o.Receipts is not { } r) continue;
            foreach (var a in r.Actors)
                foreach (var x in a.Activations)
                    Line(sb, [r.RunId, o.Point.ToString(Ic), o.Run.ToString(Ic), o.Seed.ToString(Ic), a.ActorId, a.Name, x.Power, x.Option ?? "", x.Result, x.Why ?? "",
                        x.ActionId == 0 ? "" : x.ActionId.ToString(Ic), x.Rank == 0 ? "" : x.Rank.ToString(Ic), N(x.Seconds), N(x.EndSeconds), x.EndCause ?? "",
                        x.Result == "denied" ? "" : x.Cost.ToString(Ic), N(x.BloodBefore), N(x.BloodAfter), x.Source,
                        x.Power == "celerity" && x.Result != "denied" ? x.SwingsLive.ToString(Ic) : "", N(x.TempoSwing), N(x.TempoSwingBefore),
                        x.Power == "celerity" && x.Result != "denied" ? x.RightsGranted.ToString(Ic) : "", x.Power == "celerity" && x.Result != "denied" ? x.RightsUsed.ToString(Ic) : "",
                        x.Power == "celerity" && x.Result != "denied" ? x.RightsUnused.ToString(Ic) : "", x.UnusedWhy ?? "",
                        x.Power == "celerity" && x.Result != "denied" ? x.PhysicalContacts.ToString(Ic) : "", x.Power == "celerity" && x.Result != "denied" ? x.StrikesWounding.ToString(Ic) : "",
                        string.Join(";", (x.Rights ?? []).Select(g => $"{g.Strike.ToString(Ic)}/{g.ParentContact.ToString(Ic)}:{g.Check}:{(g.Used ? 1 : 0)}:{(g.Hit ? 1 : 0)}"))]);
        }
        File.WriteAllText(path, sb.ToString());
    }

    // ---- strikes.csv ----

    /// <summary><c>strikes.csv</c>' columns: a row each extra blow of a Celerity swing (<see cref="CelerityRight"/>).</summary>
    public static readonly string[] StrikeHeader =
    [
        "run_id", "point", "run", "seed", "attacker_id", "attacker", "target", "action_id", "strike", "parent_contact", "swing", "index", "of",
        "seconds", "check", "region", "guard", "guard_verdict", "moved_metres", "used", "hit", "wounds", "health_before", "health_after",
        "resisted_by", "defence", "rechecked_at_own_moment", "resolution",
    ];

    /// <summary>
    /// <c>strikes.csv</c>: every extra blow of every run, a row each — its logical strike id (contact × 16 + its place), the swing and the one
    /// contact it came from (never a contact of its own), its moment, what its own check met (met and the region; guarded and which guard;
    /// dodged; out of reach; the attacker stopped; the target down), the harm it caused if any (<c>hit</c> 1 only when the rules resolved
    /// harm on the body for it: a guarded blow only when it overpowered the guard), and how it was resolved in words.
    /// </summary>
    internal static void WriteStrikes(string path, IEnumerable<ScenarioRunner.RunOutput> done)
    {
        var sb = new StringBuilder();
        Line(sb, StrikeHeader);
        foreach (var o in done.OrderBy(o => o.Point).ThenBy(o => o.Run))
        {
            if (o.Receipts is not { ExtraBlows: { } blows } r) continue;
            foreach (var b in blows)
            {
                int ai = -1;
                for (int k = 0; k < r.Actors.Count; k++) if (r.Actors[k].Name == b.Attacker) ai = k;
                Line(sb, [r.RunId, o.Point.ToString(Ic), o.Run.ToString(Ic), o.Seed.ToString(Ic), ai >= 0 ? r.Actors[ai].ActorId : "", b.Attacker, b.Target ?? "",
                    b.ActionId is { } id ? id.ToString(Ic) : "", b.Strike.ToString(Ic), b.ParentContact.ToString(Ic), b.Swing.ToString(Ic), b.Index.ToString(Ic), b.Of.ToString(Ic),
                    N(b.Seconds), b.Check, b.Region ?? "", b.Guard ?? "", b.GuardVerdict ?? "", N(b.MovedMetres), b.Used ? "1" : "0", b.Hit ? "1" : "0",
                    b.Hit ? b.Wounds.ToString(Ic) : "", N(b.HealthBefore), N(b.HealthAfter), b.ResistedBy ?? "", b.Defence ?? "", b.RecheckedAtOwnMoment ? "1" : "0", b.Resolution]);
            }
        }
        File.WriteAllText(path, sb.ToString());
    }

    // ---- actions.csv ----

    public static readonly string[] ActionHeader =
    [
        "run_id", "point", "run", "seed", "event_id", "tick", "seconds", "actor_id", "actor", "action", "result", "detail", "why", "swing",
        "target", "cost", "contacts", "body_contacts", "wounds",
    ];

    /// <summary>
    /// Each action tried in a run: a blow begun (admitted) or refused (denied, why), a power used (admitted, its cost) or refused, a dodge
    /// begun or refused, a block raised, a parry begun, a step the director could not ask (denied) — with, for a blow, the contacts it
    /// made (all, on a body) and the wounds they dealt (joined by the event stream's parents).
    /// </summary>
    public static List<string[]> ActionRows(string runId, ScenarioPlanPoint point, int run, ulong seed, ScenarioEventLog log, IReadOnlyList<string> names)
    {
        var rows = new List<string[]>();
        var events = log.Events;
        // A blow's contacts and their wounds, by the blow's event id.
        var contactsOf = new Dictionary<int, (int All, int Body, long Wounds)>();
        var contactEvent = new Dictionary<long, int>();
        foreach (var e in events)
        {
            if (e.Raw is ContactMade c && e.Parent is { } p)
            {
                var t = contactsOf.TryGetValue(p, out var had) ? had : default;
                contactsOf[p] = (t.All + 1, t.Body + (c.Met == ContactMet.Body ? 1 : 0), t.Wounds);
                contactEvent[c.ContactId] = p;
            }
            else if (e.Raw is HarmResolved h && contactEvent.TryGetValue(h.ContactId, out int blow))
            {
                var t = contactsOf[blow];
                contactsOf[blow] = (t.All, t.Body, t.Wounds + h.Wounds);
            }
        }
        string Id(string? name) { int i = -1; for (int k = 0; name is not null && k < names.Count; k++) if (names[k] == name) { i = k; break; } return i >= 0 ? $"a{i}" : ""; }
        void Add(ScenarioEvent e, string action, string result, string detail = "", string why = "", string swing = "", string target = "", string cost = "")
        {
            var t = contactsOf.TryGetValue(e.Id, out var k) ? k : default;
            bool blow = action == "attack" && result == "admitted";
            rows.Add([runId, point.Index.ToString(Ic), run.ToString(Ic), seed.ToString(Ic), e.Id.ToString(Ic), e.Tick.ToString(Ic), ArenaTables.Num(e.Seconds),
                Id(e.By), e.By ?? "", action, result, detail, why, swing, target, cost,
                blow ? t.All.ToString(Ic) : "", blow ? t.Body.ToString(Ic) : "", blow ? t.Wounds.ToString(Ic) : ""]);
        }
        foreach (var e in events)
        {
            switch (e.Raw)
            {
                case AttackBegun a:
                    Add(e, "attack", "admitted", $"{a.Strength.ToString().ToLowerInvariant()} {(a.Direction == AttackDirection.None ? "" : a.Direction.ToString().ToLowerInvariant())}".Trim(),
                        swing: a.Swing.ToString(Ic));
                    break;
                case AttackRefused r: Add(e, "attack", "denied", why: r.Why.ToString()); break;
                case Powers.PowerUsed u: Add(e, "power", "admitted", FighterTally.PowerOf(u.Power) + $" rank {u.Rank.ToString(Ic)}", cost: u.Cost.ToString(Ic)); break;
                case Powers.PowerRefused pr: Add(e, "power", "denied", pr.Power is null ? $"slot {pr.Slot.ToString(Ic)}" : FighterTally.PowerOf(pr.Power), why: pr.Reason.ToString()); break;
                case Bodies.DodgeBegun: Add(e, "dodge", "admitted"); break;
                case Bodies.DodgeRefused dr: Add(e, "dodge", "denied", why: e.Field("Why") as string ?? e.Field("Reason") as string ?? ""); break;
                case BlockRaised: Add(e, "block", "admitted"); break;
                case ParryBegun pb: Add(e, "parry", "admitted", target: e.On ?? ""); break;
                case StepRefused sr: Add(e, ScenarioTally.Step(sr.Kind), "denied", sr.Label ?? "", sr.Why.ToString()); break;
                default:
                    if (e.Kind == "ArrowLoosed") Add(e, "loose", "admitted", target: e.On ?? "");
                    break;
            }
        }
        return rows;
    }

    // ---- writers ----

    /// <summary><c>runs.csv</c>: a row a run — its id, point, run, seed, the swept values, how it ended, who won, its length, its digest, valid, anomalies, error.</summary>
    internal static void WriteRuns(string path, ScenarioPlan plan, ScenarioMeasuresModel model, IReadOnlyList<string> paths,
        IReadOnlyDictionary<int, ScenarioPlanPoint> byPoint, IEnumerable<ScenarioRunner.RunOutput> done)
    {
        var sb = new StringBuilder();
        var named = model.Measures.Select(m => m.Name).ToList();
        // With objectives, at the end: the ending in words and each objective's result and second (none: as before).
        var objectives = plan.Objectives.Select(o => o.Name).ToList();
        string[] goalColumns = objectives.Count == 0 ? [] : ["ending", .. objectives.SelectMany(n => new[] { $"objective_{n}", $"objective_{n}_seconds" })];
        Line(sb, ["run_id", "point", "run", "seed", .. paths, "stop", "winner", "seconds", "ticks", "digest", "valid", "execution_mode",
            "anomalies", "error", "events", "tape", "scenario_sha256", .. named, .. goalColumns]);
        foreach (var o in done.OrderBy(o => o.Point).ThenBy(o => o.Run))
        {
            var r = o.Record;
            var p = byPoint[o.Point];
            Line(sb, [r.RunId, o.Point.ToString(Ic), o.Run.ToString(Ic), o.Seed.ToString(Ic), .. paths.Select(x => p.Values.FirstOrDefault(v => v.Path == x).Value ?? ""),
                r.Stop, r.Winner ?? "", ArenaTables.Num(r.Seconds), r.Ticks.ToString(Ic), r.Digest, o.Threw ? "" : r.Valid ? "true" : "false", o.Mode,
                string.Join("; ", r.Anomalies ?? []), r.Error ?? "", r.Events ?? "", r.Tape ?? "", plan.Sha256,
                .. named.Select(n => r.Measures.TryGetValue(n, out var v) ? N(v) : ""),
                .. (objectives.Count == 0 ? [] : (string[])[r.Ending ?? "", .. objectives.SelectMany(n => r.Objectives?.FirstOrDefault(x => x.Name == n) is { } x
                    ? new[] { x.Result, x.Seconds is { } sec ? ArenaTables.Num(sec) : "" } : ["", ""])])]);
        }
        File.WriteAllText(path, sb.ToString());
    }

    /// <summary><c>actors.csv</c>: a row an actor a run (<see cref="ActorHeader"/>).</summary>
    internal static void WriteActors(string path, IEnumerable<ScenarioRunner.RunOutput> done)
    {
        var sb = new StringBuilder();
        Line(sb, ActorHeader);
        foreach (var o in done.OrderBy(o => o.Point).ThenBy(o => o.Run)) foreach (var row in o.ActorRows) Line(sb, row);
        File.WriteAllText(path, sb.ToString());
    }

    /// <summary><c>actions.csv</c>: a row an action tried (<see cref="ActionHeader"/>).</summary>
    internal static void WriteActions(string path, IEnumerable<ScenarioRunner.RunOutput> done)
    {
        var sb = new StringBuilder();
        Line(sb, ActionHeader);
        foreach (var o in done.OrderBy(o => o.Point).ThenBy(o => o.Run)) foreach (var row in o.ActionRows) Line(sb, row);
        File.WriteAllText(path, sb.ToString());
    }

    /// <summary><c>sweeps.csv</c>: a row a point — its name, its values, the runs asked for, completed, valid, censored (by time), failed, and the stop's time.</summary>
    internal static void WriteSweeps(string path, ScenarioPlan plan, IReadOnlyList<string> paths, IReadOnlyList<ScenarioPlanPoint> points,
        IEnumerable<ScenarioRunner.RunOutput> done, ScenarioStop stop, int runs)
    {
        var sb = new StringBuilder();
        Line(sb, ["point", "name", .. paths, "switches", "combat_tempo", "night", "runs_requested", "runs_completed", "runs_valid", "runs_censored",
            "runs_failed", "seed_first", "seed_last", "after_seconds"]);
        var all = done.ToList();
        foreach (var p in points)
        {
            var mine = all.Where(o => o.Point == p.Index).ToList();
            int completed = mine.Count(o => !o.Threw);
            Line(sb, [p.Index.ToString(Ic), p.Name, .. paths.Select(x => p.Values.FirstOrDefault(v => v.Path == x).Value ?? ""),
                string.Join("+", p.Switches), p.CombatTempo is { } t ? ArenaTables.Num(t) : "", p.Night ? "true" : "false",
                runs.ToString(Ic), completed.ToString(Ic), mine.Count(o => !o.Threw && o.Valid).ToString(Ic),
                mine.Count(o => !o.Threw && o.Record.Stop == StopNames.Time).ToString(Ic), mine.Count(o => o.Threw).ToString(Ic),
                plan.Seed.ToString(Ic), (plan.Seed + (ulong)Math.Max(0, runs - 1)).ToString(Ic), ArenaTables.Num(stop.AfterSeconds)]);
        }
        File.WriteAllText(path, sb.ToString());
    }
}
