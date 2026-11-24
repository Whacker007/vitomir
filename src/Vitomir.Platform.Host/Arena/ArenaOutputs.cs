// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Text;
using Vitomir.Platform.Combat;

namespace Vitomir.Platform.Host.Arena;

/// <summary>
/// The arena's tables: <c>bouts.csv</c>, one row per bout, and <c>contacts.csv</c>, one row per contact and
/// its harm. Plain CSV (a comma between fields, a field with a comma or a quote quoted), numbers in the invariant culture, an
/// empty field for "none".
/// </summary>
public static class ArenaTables
{
    static readonly CultureInfo Ic = CultureInfo.InvariantCulture;

    /// <summary>The per-fighter columns of <c>bouts.csv</c>, each after the fighter's name and a dot.</summary>
    public static readonly string[] FighterColumns =
    [
        "swings", "strikes", "contacts_body", "contacts_shield", "contacts_weapon", "contacts_level", "hits",
        "wounds_dealt", "wounds_taken", "health_left", "blocks_raised", "attacks_refused", "mean_damage_dice", "mean_successes", "stopped",
    ];
    // The rules package's finding 3: the last column was "died", set on every zero-Health stop; it
    // is "stopped" now, in its place: a harness stop (a zero-Health stop, a severed stop, or disarmed), never a death — no torpor
    // or Final Death exists yet. Which stop: the fighter's "stop" (the rules block's block at the end).

    /// <summary>The header of <c>bouts.csv</c>.</summary>
    public static List<string> BoutHeader(ArenaSpec spec)
    {
        var h = new List<string> { "point" };
        h.AddRange(spec.Sweeps.Select(s => s.Label(spec)));
        h.AddRange(["bout", "seed", "winner", "winner_index", "end", "ticks", "seconds", "kill_seconds", "digest"]);
        foreach (var f in spec.Fighters) h.AddRange(FighterColumns.Select(c => $"{f.Name}.{c}"));
        // At the end (the columns before keep their order): what stopped the bout, and each fighter's contest.
        h.Add("stop_state");
        foreach (var f in spec.Fighters) h.AddRange(ContestColumns.Select(c => $"{f.Name}.{c}"));
        // At the end: each fighter's powers (whatever a power's effect is, what it cost and how often and long it was on).
        foreach (var f in spec.Fighters) h.AddRange(PowerColumns.Select(c => $"{f.Name}.{c}"));
        // At the end: each fighter's flurry blows and its Health at the start and the end (the 1–1000 scale).
        foreach (var f in spec.Fighters) h.AddRange(FlurryColumns.Select(c => $"{f.Name}.{c}"));
        // At the end: each fighter's severances, the chain and the structural loss.
        foreach (var f in spec.Fighters) h.AddRange(SeveranceColumns.Select(c => $"{f.Name}.{c}"));
        // ---- the dodge work, at the end: each fighter's dodges.
        foreach (var f in spec.Fighters) h.AddRange(DodgeColumns.Select(c => $"{f.Name}.{c}"));
        // ---- the severance block, at the end: each fighter's blows by angle band, its cuts by band and its mean cut share.
        foreach (var f in spec.Fighters) h.AddRange(AngleColumns.Select(c => $"{f.Name}.{c}"));
        // ---- the armour block, at the end: each fighter's armour and what it did.
        foreach (var f in spec.Fighters) h.AddRange(ArmorColumns.Select(c => $"{f.Name}.{c}"));
        // ---- the guard block, at the end: each fighter's guard.
        foreach (var f in spec.Fighters) h.AddRange(GuardColumns.Select(c => $"{f.Name}.{c}"));
        // ---- the rules block, at the end: each fighter's own stop (beside the bout's stop_state).
        foreach (var f in spec.Fighters) h.AddRange(StopColumns.Select(c => $"{f.Name}.{c}"));
        // ---- end
        // ---- the flurry block, at the end: each fighter's extra blows by what their checks found.
        foreach (var f in spec.Fighters) h.AddRange(FlurryStrikeColumns.Select(c => $"{f.Name}.{c}"));
        // ---- the quality block, at the end: each fighter's blow quality.
        foreach (var f in spec.Fighters) h.AddRange(QualityColumns.Select(c => $"{f.Name}.{c}"));
        // ---- the rebound work, at the end: each fighter's blades' stops, rebounds and stuns.
        foreach (var f in spec.Fighters) h.AddRange(ReboundColumns.Select(c => $"{f.Name}.{c}"));
        // ---- end
        // ---- the guard block, at the end: each fighter's guard's arm solver and gives.
        foreach (var f in spec.Fighters) h.AddRange(SolverColumns.Select(c => $"{f.Name}.{c}"));
        // ---- end
        // ---- the direction block, at the end: each fighter's blows by direction.
        foreach (var f in spec.Fighters) h.AddRange(DirectionColumns.Select(c => $"{f.Name}.{c}"));
        // ---- end
        // ---- the fight work, at the end: each fighter's staggers and its duelling brain's parries.
        foreach (var f in spec.Fighters) h.AddRange(FightColumns.Select(c => $"{f.Name}.{c}"));
        // ---- end
        // ---- the stagger work, at the end: how long each fighter was kept from acting by staggers in a row.
        foreach (var f in spec.Fighters) h.AddRange(StaggerRunColumns.Select(c => $"{f.Name}.{c}"));
        // ---- end
        // ---- the pace work, at the end: each fighter's brain's pauses.
        foreach (var f in spec.Fighters) h.AddRange(PaceColumns.Select(c => $"{f.Name}.{c}"));
        // ---- end
        // ---- the tempo work, at the end: blow to blow, and the next blow after a stagger it gave.
        foreach (var f in spec.Fighters) h.AddRange(TempoColumns.Select(c => $"{f.Name}.{c}"));
        // ---- end
        // ---- the foes work, at the end: one against several — the side left standing and the order the fighters went down, then
        // each fighter's side, when it went down and how often its brain was given another foe.
        h.AddRange(GroupColumns);
        foreach (var f in spec.Fighters) h.AddRange(SideColumns.Select(c => $"{f.Name}.{c}"));
        // ---- end
        // ---- the guards work, at the end: each fighter's brain's shield reads (over, under, no room, thrusts cut instead).
        foreach (var f in spec.Fighters) h.AddRange(ShieldReadColumns.Select(c => $"{f.Name}.{c}"));
        // ---- end
        return h;
    }

    /// <summary>
    /// The bout's columns for one against several (empty without sides): <c>side_won</c>, the side left standing (its
    /// name; empty when time ran out or every side fell at once), and <c>down_order</c>, the fighters in the order they went down
    /// with the seconds (<c>Club1@12.5;Club2@20.25</c>).
    /// </summary>
    public static readonly string[] GroupColumns = ["side_won", "down_order"];

    /// <summary>
    /// The per-fighter columns for one against several: its <c>side</c> (empty without sides), <c>down_s</c> when it went
    /// down (its stop, or its weapon hand cut off; empty when it did not, or without sides) and <c>foe_switches</c>, the times its
    /// duelling brain was given another foe (the nearest of another side: <see cref="Combat.DuelSideSystem"/>).
    /// </summary>
    public static readonly string[] SideColumns = ["side", "down_s", "foe_switches"];

    /// <summary>
    /// The per-fighter columns of <c>bouts.csv</c> at the end: its blades that sprang back (off a guard or the level)
    /// and the mean tip travel asked for (m; empty with none), its blades stopped in a body, the mean seconds from a swing's beginning
    /// to its stop (empty with none), its stuns (Bannerlord's attacker stun after a blade held or parried) and their seconds summed,
    /// and its attack presses the stun refused (and buffered).
    /// </summary>
    public static readonly string[] ReboundColumns = ["rebounds", "rebound_m_mean", "body_stops", "stop_s_mean", "stuns", "stun_s", "stun_refused"];

    /// <summary>
    /// The per-fighter columns of <c>bouts.csv</c> after the dodge's: its own blows on a body by the band they met in
    /// (normal, acute, glancing; unmeasured: no speed at the contact), its cuts by the band of the cutting blow, and the mean cut share
    /// of its blows (the share of a blow's harm that counted toward cutting; 1 with the angle off; empty with no blow). The parts cut
    /// off by region are <see cref="SeveranceColumns"/>' <c>severed_regions</c>.
    /// </summary>
    public static readonly string[] AngleColumns = ["blows_normal", "blows_acute", "blows_glancing", "blows_unmeasured",
        "cuts_normal", "cuts_acute", "cuts_glancing", "mean_cut_share"];

    /// <summary>
    /// The per-fighter columns of <c>bouts.csv</c> at the end: the extra blows its swings carried (Celerity's flurry)
    /// that were tried (each its own logical strike), landed (resolved as harm: a blow that met the body, or a guard it
    /// overpowered; under <c>flurry-compressed</c> every one tried), met a guard (held, parried or overpowered: the trace says which),
    /// found the target out of reach, found it dodged away, ended by the attacker's own stop (a cancel, a new swing, its stop, its weapon
    /// hand) and by its target's stop.
    /// </summary>
    public static readonly string[] FlurryStrikeColumns = ["flurry_tried", "flurry_landed", "flurry_guarded", "flurry_out_of_reach", "flurry_dodged",
        "flurry_attacker_stopped", "flurry_target_down"];

    /// <summary>
    /// The per-fighter columns of <c>bouts.csv</c> at the end — its swings' own blows on a body with a measured
    /// quality and a reference, their mean Q and mean factor (empty with none; the factor 1 with <c>blow-quality-off</c>), its blows
    /// resisted by low work (<see cref="ResistedBy.LowWork"/>) and its blows the hilt made bashing.
    /// </summary>
    public static readonly string[] QualityColumns = ["quality_blows", "mean_quality", "mean_quality_factor", "low_work", "hilt_blows",
        "range_retreats", "range_hilt_retreats", "range_side_steps", "range_forced"];   // the follow-up: the duelling brain's range keeping

    /// <summary>
    /// The per-fighter columns of <c>bouts.csv</c> after the dodge's: parry windows opened, blows met at its guard
    /// that the rules held, parried and let overpower it, guards its own blows overpowered, its blades' clashes and those it
    /// dominated, its feints (swings cancelled by the cancel key) and its swings cancelled by a dodge.
    /// </summary>
    public static readonly string[] GuardColumns = ["parries_tried", "guard_held", "guard_parried", "guard_overpowered", "overpowered_foe",
        "clashes", "clashes_won", "feints", "dodge_cancels"];

    /// <summary>
    /// The per-fighter columns of <c>bouts.csv</c> at the end: the foe's blows its guard's arm solver followed
    /// (<see cref="Animation.GuardSolverFollows"/>), of them those its hand could not reach in time (no intercept in reach before the
    /// blade), its guard's gives (<see cref="Animation.GuardGave"/>: blows met at its guard held, parried or overpowered) and the
    /// furthest give (cm; empty with none). All 0 with <c>guard-solver-off</c>.
    /// </summary>
    public static readonly string[] SolverColumns = ["guard_followed", "guard_followed_late", "guard_gives", "guard_give_most_cm"];

    /// <summary>
    /// The per-fighter columns of <c>bouts.csv</c> at the end: its blows from the left, the right, overhead and
    /// thrusts (all 0 with <c>attack-spin</c>: 1.1's blows have no direction), its body contacts by its swing's direction, its blows
    /// held ready past their ready point and their mean seconds held (empty with none), and the blows its duelling brain struck from
    /// the side its foe's guard was not on.
    /// </summary>
    public static readonly string[] DirectionColumns = ["swings_left", "swings_right", "swings_overhead", "swings_thrust",
        "body_left", "body_right", "body_overhead", "body_thrust", "held_blows", "held_s_mean", "away_from_guard"];
    /// The per-fighter columns of <c>bouts.csv</c> at the end: the staggers it took (<see cref="Staggered"/>), those from its
    /// guard overpowered, its swings a stagger cut short before their strike, the seconds it spent staggered; its duelling brain's parries
    /// chosen, those given up because its own swing would not be over in time (the brain's parry timed by its hand), and those pressed.
    /// </summary>
    /// <summary>
    /// The stagger work (the calibration rule: what is uncontrollable for one player is likely an issue for the majority of
    /// other people): the per-fighter columns at the end — the longest time it was kept from acting by staggers in a row (s: from a
    /// stagger's start to the end of the last of a run whose gaps, one's end to the next's start, are at most
    /// <see cref="FighterTally.StaggerRunGapSeconds"/>, 1 s), the staggers that began within that gap of the last one's end, and the
    /// least gap seen (s; empty with fewer than two).
    /// </summary>
    public static readonly string[] StaggerRunColumns = ["stagger_run_most_s", "staggers_in_a_row", "stagger_gap_least_s"];

    public static readonly string[] FightColumns = ["staggered", "staggered_overpowered", "swings_interrupted", "stagger_seconds", "parries_chosen", "parries_not_free", "parries_pressed"];

    /// <summary>
    /// The per-fighter columns of <c>bouts.csv</c> at the end: its duelling brain's pauses (<see cref="Vitomir.Platform.Combat.DuelPace"/>),
    /// their mean drawn length (s; empty with none) and those an opening cut short. All 0 with <c>pace-off</c>.
    /// </summary>
    public static readonly string[] PaceColumns = ["pauses", "pause_s_mean", "pauses_cut"];

    /// <summary>
    /// The per-fighter columns of <c>bouts.csv</c> at the very end: the gaps from one of its blows begun to the next
    /// (press to press, s: how many, their mean and median; empty with none), the staggers its blows gave, and of them those followed
    /// by another blow of its own before the bout ended — the mean seconds from the stagger's start to that next blow's first
    /// contact or <c>Strike</c> moment (whichever came first; empty with none) and how many of those came while its foe was still
    /// staggered.
    /// </summary>
    /// <summary>Each fighter's brain's shield reads — its blows aimed over its foe's shield, under it, the reads that found no room either way, and the read blows that were to be thrusts and were cut instead.</summary>
    public static readonly string[] ShieldReadColumns = ["shield_reads_over", "shield_reads_under", "shield_reads_no_room", "shield_reads_unthrust"];
    public static readonly string[] TempoColumns = ["swing_gaps", "swing_gap_mean_s", "swing_gap_median_s", "staggers_given", "stagger_next_n", "stagger_next_blow_s", "stagger_next_inside"];

    /// <summary>
    /// The per-fighter columns of <c>bouts.csv</c> after the flurry's: the parts its blows cut off and by region
    /// (<c>right forearm:1;neck:1</c>), its blows that went on past a cut, its contacts later in a chain and its swings with a
    /// chain longer than one (the platform's follow-through), the parts cut off it by region, and the Health its effective
    /// maximum lost to them (the union's increments).
    /// </summary>
    /// <summary>
    /// The per-fighter columns of <c>bouts.csv</c> at the end: what it wore (the spec's pieces, a <c>+</c> between;
    /// empty for none), the blows it took on armour and on parts no piece covered, the armour's dice there summed, the wounds the
    /// armour kept off it (<see cref="HarmResolved.ArmorKept"/>), and the parts cut off it where armour covered them and where none did.
    /// </summary>
    public static readonly string[] ArmorColumns = ["armor", "armor_blows", "bare_blows", "armor_dice", "armor_kept", "cuts_covered", "cuts_bare"];

    public static readonly string[] SeveranceColumns = ["severances", "severed_regions", "continued", "chain_contacts", "chained_swings",
        "parts_lost", "structural_loss"];

    /// <summary>
    /// The per-fighter columns of <c>bouts.csv</c> after the flurry's: dodges begun and refused, the foe's swings
    /// it dodged (a dodge begun within <see cref="BoutTally.DodgeAnswerSeconds"/> of the swing's start) and of them those that met
    /// no part of its body (<see cref="FighterTally.DodgedClean"/>: as near as the events tell to blows that met nothing because
    /// of a dodge).
    /// </summary>
    public static readonly string[] DodgeColumns = ["dodges_begun", "dodges_refused", "swings_dodged", "dodged_clean"];

    /// <summary>
    /// The per-fighter columns of <c>bouts.csv</c> after the powers': the extra blows its swings' flurries resolved
    /// (Celerity's speed plus flurry, actions = swings + flurry blows), and its Health at the start and at the end
    /// (the 1–1000 scale; empty with none to show).
    /// </summary>
    public static readonly string[] FlurryColumns = ["flurry_blows", "health_start", "health_end"];

    /// <summary>The per-fighter contest columns of <c>bouts.csv</c>, after <c>stop_state</c>.</summary>
    public static readonly string[] ContestColumns = ["contested_hits", "mean_contest_change"];

    /// <summary>
    /// The per-fighter power columns of <c>bouts.csv</c>, after the contest's: Blood at the start, at the end and spent
    /// (empty with none to show), uses (renewals included), renewals, refusals and their reasons (<c>NoBlood:2;Recast:1</c>),
    /// and the seconds its powers were live (one number with at most one power, else <c>celerity:12.5;potence:3</c>); then the
    /// rules package's entitlement receipt: its swings begun while a power of its was live and its swings
    /// a second while none was, so the extra actions a power bought read <c>swings_live − live_seconds × swings_unpaid_per_s</c>.
    /// </summary>
    public static readonly string[] PowerColumns = ["blood_start", "blood_end", "blood_spent", "uses", "renewals", "refused", "refused_reasons", "live_seconds",
        "swings_live", "swings_unpaid_per_s"];

    /// <summary>
    /// The per-fighter column of <c>bouts.csv</c> at the end — the stop
    /// that ended this fighter's part in the bout, <c>ZERO_HEALTH_STOP</c>, <c>SEVERED_STOP</c> or <c>DISARMED</c> (empty for none),
    /// beside the bout's <c>stop_state</c>. Harness stops, never deaths: torpor and Final Death are not built.
    /// </summary>
    public static readonly string[] StopColumns = ["stop"];

    /// <summary>The stop of a bout ended by a weapon hand cut off (and of the fighter disarmed).</summary>
    public const string DisarmedName = "DISARMED";

    /// <summary>The rules package's name for a stop: <c>ZERO_HEALTH_STOP</c>.</summary>
    public static string StopName(ActorStop s) => s switch { ActorStop.ZeroHealthStop => "ZERO_HEALTH_STOP", ActorStop.SeveredStop => "SEVERED_STOP", _ => s.ToString() };   // the severed stop

    /// <summary>A bout's row of <c>bouts.csv</c>, in <see cref="BoutHeader"/>'s order.</summary>
    public static List<string> BoutRow(ArenaSpec spec, BoutResult r)
    {
        var row = new List<string> { r.Point.ToString(Ic) };
        row.AddRange(r.Values.Select(v => v.ToString(Ic)));
        row.Add(r.Bout.ToString(Ic));
        row.Add(r.Seed.ToString(Ic));
        row.Add(r.Winner >= 0 ? spec.Fighters[r.Winner].Name : "");
        row.Add(r.Winner.ToString(Ic));
        row.Add(r.KillSeconds is null ? "time" : "kill");
        row.Add(r.Ticks.ToString(Ic));
        row.Add(Num(r.Ticks / (double)Core.World.TicksPerSecond));
        row.Add(r.KillSeconds is { } k ? Num(k) : "");
        row.Add(r.Digest.ToString("x16", Ic));
        foreach (var f in r.Fighters)
        {
            row.AddRange([
                f.Swings.ToString(Ic), f.Strikes.ToString(Ic), f.ContactsBody.ToString(Ic), f.ContactsShield.ToString(Ic),
                f.ContactsWeapon.ToString(Ic), f.ContactsLevel.ToString(Ic), f.Hits.ToString(Ic), f.WoundsDealt.ToString(Ic),
                f.WoundsTaken.ToString(Ic), f.HealthLeft?.ToString(Ic) ?? "", f.BlocksRaised.ToString(Ic), f.Refused.ToString(Ic),
                f.MeanDice is { } d ? Num(d) : "", f.MeanSuccesses is { } s ? Num(s) : "", f.Stopped ? "1" : "0",   // was "died"
            ]);
        }
        row.Add(r.Stop is { } stop ? StopName(stop) : r.Disarmed ? DisarmedName : "");   // a weapon hand cut off ends the bout
        foreach (var f in r.Fighters) row.AddRange([f.ContestedHits.ToString(Ic), f.MeanContestChange is { } m ? Num(m) : ""]);
        foreach (var f in r.Fighters)
            row.AddRange([
                f.BloodStart?.ToString(Ic) ?? "", f.BloodEnd?.ToString(Ic) ?? "", f.BloodSpent.ToString(Ic), f.Uses.ToString(Ic), f.Renewals.ToString(Ic),
                f.PowersRefused.ToString(Ic), f.RefusedReasons(), f.LiveSeconds(),
                f.SwingsLive.ToString(Ic), f.SwingsUnpaidPerSecond(r.Ticks) is { } u ? Num(u) : "",
            ]);
        foreach (var f in r.Fighters)
            row.AddRange([f.FlurryBlows.ToString(Ic), f.HealthStart?.ToString(Ic) ?? "", f.HealthEnd?.ToString(Ic) ?? ""]);
        foreach (var f in r.Fighters)
            row.AddRange([f.Severances.ToString(Ic), FighterTally.Regions(f.SeveredByRegion), f.Continued.ToString(Ic), f.ChainContacts.ToString(Ic),
                f.ChainedSwings.ToString(Ic), FighterTally.Regions(f.LostByRegion), f.StructuralLost.ToString(Ic)]);
        foreach (var f in r.Fighters)
            row.AddRange([f.DodgesBegun.ToString(Ic), f.DodgesRefused.ToString(Ic), f.SwingsDodged.ToString(Ic), f.DodgedClean.ToString(Ic)]);
        foreach (var f in r.Fighters)
        {
            int blows = f.BlowsByBand[0] + f.BlowsByBand[1] + f.BlowsByBand[2] + f.BlowsByBand[3];
            row.AddRange([f.BlowsByBand[(int)AngleBand.Normal].ToString(Ic), f.BlowsByBand[(int)AngleBand.Acute].ToString(Ic), f.BlowsByBand[(int)AngleBand.Glancing].ToString(Ic),
                f.BlowsByBand[(int)AngleBand.None].ToString(Ic), f.CutsByBand[(int)AngleBand.Normal].ToString(Ic), f.CutsByBand[(int)AngleBand.Acute].ToString(Ic),
                f.CutsByBand[(int)AngleBand.Glancing].ToString(Ic), blows > 0 ? Num(f.CutShareSum / blows) : ""]);
        }
        for (int i = 0; i < r.Fighters.Length; i++)
        {
            var f = r.Fighters[i];
            row.AddRange([string.Join("+", spec.Fighters[i].Armor ?? []), f.ArmorBlows.ToString(Ic), f.BareBlows.ToString(Ic), f.ArmorDice.ToString(Ic),
                f.ArmorKept.ToString(Ic), f.CutsCovered.ToString(Ic), f.CutsBare.ToString(Ic)]);
        }
        foreach (var f in r.Fighters)
            row.AddRange([f.ParriesTried.ToString(Ic), f.GuardHeld.ToString(Ic), f.GuardParried.ToString(Ic), f.GuardOverpowered.ToString(Ic),
                f.OverpoweredFoe.ToString(Ic), f.Clashes.ToString(Ic), f.ClashesWon.ToString(Ic), f.Feints.ToString(Ic), f.DodgeCancels.ToString(Ic)]);
        foreach (var f in r.Fighters) row.Add(f.Stop);   // ---- its own stop
        foreach (var f in r.Fighters)
            row.AddRange([f.FlurryTried.ToString(Ic), f.FlurryLanded.ToString(Ic), f.FlurryGuarded.ToString(Ic), f.FlurryOutOfReach.ToString(Ic),
                f.FlurryDodged.ToString(Ic), f.FlurryAttackerStopped.ToString(Ic), f.FlurryTargetDown.ToString(Ic)]);
        foreach (var f in r.Fighters)
            row.AddRange([f.QualityBlows.ToString(Ic), f.QualityBlows > 0 ? Num(f.QualitySum / f.QualityBlows) : "", f.QualityBlows > 0 ? Num(f.QualityFactorSum / f.QualityBlows) : "",
                f.LowWork.ToString(Ic), f.HiltBlows.ToString(Ic),
                f.RangeRetreats.ToString(Ic), f.RangeHiltRetreats.ToString(Ic), f.RangeSideSteps.ToString(Ic), f.RangeForced.ToString(Ic)]);
        foreach (var f in r.Fighters)
            row.AddRange([f.Rebounds.ToString(Ic), f.Rebounds > 0 ? (f.ReboundMetres / f.Rebounds).ToString("0.###", Ic) : "", f.BodyStops.ToString(Ic),
                f.StopsTimed > 0 ? (f.StopSeconds / f.StopsTimed).ToString("0.###", Ic) : "", f.Stuns.ToString(Ic), f.StunSeconds.ToString("0.##", Ic), f.StunRefused.ToString(Ic)]);
        foreach (var f in r.Fighters)
            row.AddRange([f.GuardFollowed.ToString(Ic), f.GuardFollowedLate.ToString(Ic), f.GuardGives.ToString(Ic), f.GuardGives > 0 ? Num(f.GuardGiveMost * 100f) : ""]);
        foreach (var f in r.Fighters)
            row.AddRange([f.SwingsByDirection[1].ToString(Ic), f.SwingsByDirection[2].ToString(Ic), f.SwingsByDirection[3].ToString(Ic), f.SwingsByDirection[4].ToString(Ic),
                f.BodyByDirection[1].ToString(Ic), f.BodyByDirection[2].ToString(Ic), f.BodyByDirection[3].ToString(Ic), f.BodyByDirection[4].ToString(Ic),
                f.HeldBlows.ToString(Ic), f.HeldBlows > 0 ? (f.HeldSeconds / f.HeldBlows).ToString("0.###", Ic) : "", f.AwayFromGuard.ToString(Ic)]);
        foreach (var f in r.Fighters)
            row.AddRange([f.Staggered.ToString(Ic), f.StaggeredOverpowered.ToString(Ic), f.SwingsInterrupted.ToString(Ic), f.StaggerSeconds.ToString("0.##", Ic),
                f.ParriesChosen.ToString(Ic), f.ParriesNotFree.ToString(Ic), f.ParriesPressed.ToString(Ic)]);
        foreach (var f in r.Fighters)
            row.AddRange([f.StaggerRunMost.ToString("0.###", Ic), f.StaggersInARow.ToString(Ic), f.StaggerGapLeast < double.MaxValue ? f.StaggerGapLeast.ToString("0.###", Ic) : ""]);
        foreach (var f in r.Fighters)
            row.AddRange([f.Pauses.ToString(Ic), f.Pauses > 0 ? (f.PausedSeconds / f.Pauses).ToString("0.###", Ic) : "", f.PausesCut.ToString(Ic)]);
        foreach (var f in r.Fighters)
            row.AddRange([f.SwingGaps.Count.ToString(Ic), f.SwingGaps.Count > 0 ? f.SwingGaps.Average().ToString("0.###", Ic) : "",
                f.SwingGaps.Count > 0 ? FighterTally.Median(f.SwingGaps).ToString("0.###", Ic) : "", f.StaggersGiven.ToString(Ic),
                f.NextBlows.ToString(Ic), f.NextBlows > 0 ? (f.NextBlowSeconds / f.NextBlows).ToString("0.###", Ic) : "", f.NextInside.ToString(Ic)]);
        // ----
        var sideNames = spec.SideNames;
        row.Add(r.SideWon >= 0 && r.SideWon < sideNames.Count ? sideNames[r.SideWon] : "");
        row.Add(string.Join(";", r.DownOrder.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(x =>
            int.TryParse(x.AsSpan(0, x.IndexOf('@')), NumberStyles.Integer, Ic, out int who) && who >= 0 && who < spec.Fighters.Count ? spec.Fighters[who].Name + x[x.IndexOf('@')..] : x)));
        for (int i = 0; i < r.Fighters.Length; i++)
        {
            var f = r.Fighters[i];
            row.AddRange([spec.Fighters[i].Side ?? "", f.DownSeconds is { } ds ? Num(ds) : "", f.FoeSwitches.ToString(Ic)]);
        }
        // ---- end
        foreach (var f in r.Fighters)
            row.AddRange([f.ShieldReadsOver.ToString(Ic), f.ShieldReadsUnder.ToString(Ic), f.ShieldReadsNoRoom.ToString(Ic), f.ShieldReadsUnthrust.ToString(Ic)]);
        return row;
    }

    /// <summary>Writes <c>bouts.csv</c>.</summary>
    public static void WriteBouts(string path, ArenaSpec spec, IEnumerable<BoutResult> results)
    {
        var sb = new StringBuilder();
        Line(sb, BoutHeader(spec));
        foreach (var r in results) Line(sb, BoutRow(spec, r));
        File.WriteAllText(path, sb.ToString());
    }

    /// <summary>The header of <c>contacts.csv</c>.</summary>
    public static List<string> ContactHeader(ArenaSpec spec)
    {
        var h = new List<string> { "point" };
        h.AddRange(spec.Sweeps.Select(s => s.Label(spec)));
        h.AddRange([
            "bout", "seed", "contact_id", "tick", "seconds", "attacker", "swing", "target", "met", "site", "material",
            "point_x", "point_y", "point_z", "normal_x", "normal_y", "normal_z", "impact_speed", "normal_impulse",
            "pre_contact_blocked", "hit_cover_or_shield", "line_of_fire", "protected",
            "harm_kind", "damage_dice", "damage_successes", "auto_successes", "soak_dice", "soak_successes", "wounds", "health_before", "health_after",
            // At the end: the contest on contact (empty when it did not run)
            "contested", "attack_dice", "attack_successes", "defence_dice", "defence_successes", "contest_change",
            // At the end: the attacker's powers live at the contact, its swing's tempo, and why a hit dealt no wound
            "attacker_live_powers", "attacker_swing_tempo", "resisted_by",
            // At the end: the extra blows Celerity's flurry resolved on the contact (the row's harm is its own blow), their wounds and Health
            "flurry_blows", "flurry_wounds", "flurry_health",
            // At the end: the blow's quality as measured (gate-08, open: nothing reads these for harm yet)
            "point_speed", "along_blade", "tip_speed", "peak_speed",
            // At the end: the contact's place in its swing's chain, the part its blow cut off, the swing's budget left
            // after it (0: the blade stopped) and the Health the cut took off the effective maximum (empty when no harm was resolved)
            "chain", "severed", "budget_left", "structural_loss",
            // At the end: the sine of the angle the blow met the surface at (impact speed over point speed; empty without a
            // point speed), the band the rules put it in and the share of its harm that counted toward cutting (empty when no harm was resolved)
            "sine", "angle_band", "cut_share",
            // ---- the armour block, at the end: the armour over the part struck (its pieces, a + between; empty for none), its dice
            // (the pool, included in soak_dice) and the wounds it kept off (empty when no harm was resolved)
            "armor", "armor_dice", "armor_kept",
            // ---- the guard block, at the end: the guard met (shield, blade, parry, clash, incidental; empty for none) and the rules' verdict on it
            "guard", "guard_verdict",
            // ---- the flurry block, at the end (logical strikes apart from physical contacts): the extra blows the contact's swing
            // tried and each one's place, check, moment and part (1:met@129:left forearm;2:guarded@134:chest); flurry_blows above counts those that landed
            "flurry_tried", "flurry_strikes",
            // ---- the rules block, at the end: the blow the contact's swing was — quick or strong, its clip's name, the aim it bent to
            // (degrees) — for the blow-quality measurement (gate-08); empty for a stranger's
            "swing_kind", "swing_clip", "swing_aim",
            // ---- the quality block, at the end: blow quality — q (impact speed over peak speed; empty without a peak speed), the blow's
            // reference q_ref (empty: none), Q, the factor applied, the damage potential before and after it, whether the hilt made the
            // blow bashing, and where severance's cut share came from (quality, band, off); empty when no harm was resolved
            "q", "q_ref", "quality", "quality_factor", "potential_before", "potential_after", "hilt", "cut_share_from",
            // The quality work's follow-up: the fighters' distance at the contact (feet to feet, m) and the attacker's reach (its duelling brain's)
            "distance", "attacker_reach",
            // ---- the direction block, at the end: the direction of the contact's swing (left, right, overhead, thrust; empty with attack-spin or a stranger's)
            "swing_direction",
            // ---- the receipts, at the end: the physical action's identity, joinable without the event stream —
            // the arrow's shot (empty for a blade's or a fixed contact), the contact's own logical strike (FlurryBlows.Id: contact × 16; the
            // row's harm is that strike's) and its swing's extra blows' strike ids (contact × 16 + k, a ; between; each re-meets this one
            // contact at its own moment: no new swing, no new contact), and whether the contact was a fixed one
            "shot", "strike", "flurry_strike_ids", "fixed",
        ]);
        return h;
    }

    /// <summary>Writes <c>contacts.csv</c>.</summary>
    public static void WriteContacts(string path, ArenaSpec spec, IEnumerable<ContactRow> rows)
    {
        var sb = new StringBuilder();
        Line(sb, ContactHeader(spec));
        foreach (var r in rows)
        {
            var c = r.Contact;
            string Who(int i) => i >= 0 ? spec.Fighters[i].Name : c.Target == Core.EntityId.None ? "" : "other";
            var row = new List<string> { r.Point.ToString(Ic) };
            row.AddRange(r.Values.Select(v => v.ToString(Ic)));
            row.AddRange([
                r.Bout.ToString(Ic), r.Seed.ToString(Ic), c.ContactId.ToString(Ic), c.Tick.ToString(Ic), Num(c.Tick / (double)Core.World.TicksPerSecond),
                Who(r.AttackerIndex), c.Swing.ToString(Ic), Who(r.TargetIndex), c.Met.ToString().ToLowerInvariant(), c.Site ?? "", c.Material.ToString().ToLowerInvariant(),
                Num(c.Point.X), Num(c.Point.Y), Num(c.Point.Z), Num(c.Normal.X), Num(c.Normal.Y), Num(c.Normal.Z), Num(c.ImpactSpeed), Num(c.NormalImpulse),
                Bit(c.PreContactBlocked), Bit(c.HitCoverOrShield), Bit(c.LineOfFire), Bit(c.Protected),
            ]);
            if (r.Harm is { } h)
                row.AddRange([
                    h.Kind.ToString().ToLowerInvariant(), h.DamageDice.ToString(Ic), h.DamageSuccesses.ToString(Ic), h.AutoSuccesses.ToString(Ic),
                    h.SoakDice.ToString(Ic), h.SoakSuccesses.ToString(Ic), h.Wounds.ToString(Ic), h.HealthBefore.ToString(Ic), h.HealthAfter.ToString(Ic),
                ]);
            else row.AddRange(Enumerable.Repeat("", 9));
            if (r.Harm is { Contested: true } k)
                row.AddRange(["1", k.AttackDice.ToString(Ic), k.AttackSuccesses.ToString(Ic), k.DefenceDice.ToString(Ic), k.DefenceSuccesses.ToString(Ic), k.ContestChange.ToString(Ic)]);
            else row.AddRange([r.Harm is null ? "" : "0", "", "", "", "", ""]);
            row.AddRange([r.AttackerLive, r.SwingTempo is { } tempo ? Num(tempo) : "", r.Harm is { } hr ? ResistedName(hr) : ""]);
            if (r.Harm is null) row.AddRange(["", "", ""]);
            else row.AddRange([r.FlurryBlows.ToString(Ic), r.FlurryWounds.ToString(Ic), r.FlurryHealth.ToString(Ic)]);
            row.AddRange([Num(c.PointSpeed), Num(c.AlongBlade), Num(c.TipSpeed), Num(c.PeakSpeed)]);
            row.Add(c.Chain.ToString(Ic));
            if (r.Harm is { } sv) row.AddRange([BodyRegions.Name(sv.Severed), Num(sv.BudgetLeft), sv.StructuralLoss.ToString(Ic)]);
            else row.AddRange(["", "", ""]);
            row.Add(c.PointSpeed > 0f ? Num(Math.Clamp(c.ImpactSpeed / c.PointSpeed, 0f, 1f)) : "");
            if (r.Harm is { } an) row.AddRange([an.Angle == AngleBand.None ? "" : an.Angle.ToString().ToLowerInvariant(), Num(an.CutShare)]);
            else row.AddRange(["", ""]);
            if (r.Harm is { } ar) row.AddRange([ar.Armor ?? "", ar.ArmorDice.ToString(Ic), ar.ArmorKept.ToString(Ic)]);
            else row.AddRange(["", "", ""]);
            row.AddRange([c.Guard == GuardMeet.None ? "" : c.Guard.ToString().ToLowerInvariant(), r.Guard is { } gv ? gv.Verdict.ToString().ToLowerInvariant() : ""]);
            row.AddRange([r.FlurryTried.ToString(Ic), r.FlurryStrikes]);
            row.AddRange([r.SwingKind, r.SwingClip, r.SwingAim is { } aim ? Num(aim) : ""]);
            row.Add(c.PeakSpeed > 0f ? Num(MathF.Max(0f, c.ImpactSpeed) / c.PeakSpeed) : "");
            if (r.Harm is { } bq)
                row.AddRange([bq.QualityRef > 0f ? Num(bq.QualityRef) : "", Num(bq.Quality), Num(bq.QualityFactor), Num(bq.PotentialBefore), Num(bq.PotentialAfter),
                    Bit(bq.Hilt), bq.CutShareFrom ?? ""]);
            else row.AddRange(["", "", "", "", "", "", ""]);
            row.AddRange([r.Distance is { } dist ? Num(dist) : "", r.AttackerReach is { } reach ? Num(reach) : ""]);
            row.Add(r.SwingDirection);
            row.AddRange([c.Shot > 0 ? c.Shot.ToString(Ic) : "", FlurryBlows.Id(c.ContactId, 0).ToString(Ic), FlurryStrikeIds(c.ContactId, r.FlurryStrikes), Bit(c.Fixed)]);
            Line(sb, row);
        }
        File.WriteAllText(path, sb.ToString());
    }

    static string Bit(bool b) => b ? "1" : "0";

    /// <summary>The extra blows' strike ids (<see cref="FlurryBlows.Id"/>) from <see cref="ContactRow.FlurryStrikes"/>' places (<c>1:met@129:…;2:…</c>).</summary>
    static string FlurryStrikeIds(long contact, string strikes)
    {
        if (string.IsNullOrEmpty(strikes)) return "";
        var ids = new List<string>();
        foreach (var part in strikes.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = part.IndexOf(':');
            if (colon > 0 && int.TryParse(part.AsSpan(0, colon), System.Globalization.NumberStyles.Integer, Ic, out int k)) ids.Add(FlurryBlows.Id(contact, k).ToString(Ic));
        }
        return string.Join(';', ids);
    }

    /// <summary>Why a hit dealt no wound — <c>no_damage</c>, <c>soak</c>, <c>halving</c>; <c>armor</c> — or empty when it dealt one.</summary>
    public static string ResistedName(HarmResolved h) => h.Wounds > 0 ? "" : h.ResistedBy switch
    {
        ResistedBy.NoDamage => "no_damage",
        ResistedBy.Soak => "soak",
        ResistedBy.Halving => "halving",
        ResistedBy.Armor => "armor",   // the armour's successes alone turned the wounds to nothing
        ResistedBy.LowWork => "low_work",   // the blow's low work alone kept the wound off
        _ => "",
    };

    /// <summary>A number as the tables write it: up to six decimals, invariant.</summary>
    public static string Num(double v) => v.ToString("0.######", Ic);

    static void Line(StringBuilder sb, List<string> fields)
    {
        for (int i = 0; i < fields.Count; i++)
        {
            if (i > 0) sb.Append(',');
            var f = fields[i];
            if (f.IndexOfAny([',', '"', '\n', '\r']) >= 0) sb.Append('"').Append(f.Replace("\"", "\"\"")).Append('"');
            else sb.Append(f);
        }
        sb.Append('\n');
    }

    /// <summary>Reads a CSV written here back into rows of fields (the header first).</summary>
    public static List<string[]> Read(string path)
    {
        var rows = new List<string[]>();
        foreach (var line in File.ReadAllLines(path))
        {
            var fields = new List<string>();
            var cur = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (quoted)
                {
                    if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                    else if (ch == '"') quoted = false;
                    else cur.Append(ch);
                }
                else if (ch == '"') quoted = true;
                else if (ch == ',') { fields.Add(cur.ToString()); cur.Clear(); }
                else cur.Append(ch);
            }
            fields.Add(cur.ToString());
            rows.Add([.. fields]);
        }
        return rows;
    }
}

/// <summary>One swept point's numbers in the summary.</summary>
public sealed record PointSummary
{
    public required int Point { get; init; }
    public required int[] Values { get; init; }
    public required int Bouts { get; init; }
    public required int Kills { get; init; }
    /// <summary>Each fighter's wins.</summary>
    public required int[] Wins { get; init; }
    /// <summary>The subject's (the first sweep's fighter, else the first) win rate and its 95 % interval (Wilson's).</summary>
    public required double WinRate { get; init; }
    public required double WinLow { get; init; }
    public required double WinHigh { get; init; }
    /// <summary>The time to the kill over the bouts with one: mean, sample standard deviation, least, median, most (s); null with none.</summary>
    public double? KillMean { get; init; }
    public double? KillSd { get; init; }
    public double? KillMin { get; init; }
    public double? KillMedian { get; init; }
    public double? KillMax { get; init; }
    /// <summary>All hits at the point (harm resolved), and per hit: wounds, damage dice, damage successes; null with no hit.</summary>
    public required int Hits { get; init; }
    public double? WoundsPerHit { get; init; }
    public double? DicePerHit { get; init; }
    public double? SuccessesPerHit { get; init; }
    /// <summary>Each fighter's wounds dealt per bout (mean).</summary>
    public required double[] WoundsPerBout { get; init; }
    /// <summary>Contacts per bout (mean, all fighters) by what they met: body, shield, weapon, level.</summary>
    public required double[] ContactsPerBout { get; init; }
    /// <summary>Blows begun per bout (mean, all fighters).</summary>
    public required double SwingsPerBout { get; init; }
    /// <summary>Each fighter's mean net contest per contested hit (the change to its damage pool), null with none; and the contested hits.</summary>
    public double?[] ContestPerHit { get; init; } = [];
    public int ContestedHits { get; init; }

    // What the powers did and what each swing came to — the same whatever a power's effect is (a faster swing, or one
    // swing carrying several attacks): per fighter, means over the point's bouts.

    /// <summary>Each fighter's Blood spent, powers used (renewals included), seconds with a power live, and powers refused, per bout.</summary>
    public double[] BloodSpentPerBout { get; init; } = [];
    public double[] UsesPerBout { get; init; } = [];
    public double[] LiveSecondsPerBout { get; init; } = [];
    public double[] RefusedPerBout { get; init; } = [];
    /// <summary>Each fighter's swings a second (its blows begun over the bouts' time), body contacts a swing and wounds a swing; null with no swing.</summary>
    public double[] SwingsPerSecond { get; init; } = [];
    public double?[] BodyContactsPerSwing { get; init; } = [];
    public double?[] WoundsPerSwing { get; init; } = [];
    /// <summary>
    /// The entitlement receipt per fighter: swings begun while a power of its was live, per bout; its swings a second while one
    /// was, and while none was (each pooled over the point's bouts; null without such time); and the extra swings a bout its
    /// powers bought: (swings while live − seconds live × the swings a second while none was) per bout — that unpaid rate
    /// <b>pooled over the whole run</b> (every point's time with no power live), because within a point whose fighter casts as it
    /// engages the unpaid time is mostly the approach, where nobody swings (the rank-0 point of a sweep carries the baseline).
    /// </summary>
    public double[] SwingsLivePerBout { get; init; } = [];
    public double?[] LiveSwingsPerSecond { get; init; } = [];
    public double?[] UnpaidSwingsPerSecond { get; init; } = [];
    public double?[] ExtraSwingsPerBout { get; init; } = [];
    /// <summary>The run's swings a second while none of the fighter's powers was live (every point pooled): the extra swings' baseline.</summary>
    public double?[] RunUnpaidSwingsPerSecond { get; init; } = [];
    /// <summary>All powers used at the point (any fighter, renewals included) and refused.</summary>
    public int Uses { get; init; }
    public int PowersRefused { get; init; }

    /// <summary>
    /// Each fighter's flurry blows (the extra blows its swings carried, Celerity's speed plus flurry), Health lost (the
    /// 1–1000 scale) per bout, and the receipt's <b>extra actions</b> a bout its powers bought: (swings while live + flurry
    /// blows − seconds live × the run's swings a second while none was) per bout — the actions a swing carries count as the
    /// actions they are.
    /// </summary>
    public double[] FlurryBlowsPerBout { get; init; } = [];
    public double[] HealthLostPerBout { get; init; } = [];
    public double?[] ExtraActionsPerBout { get; init; } = [];

    // Severance and the chain, per bout (means over the point's bouts).

    /// <summary>Each fighter's severances (parts its blows cut off), swings with a chain longer than one, and Health its effective maximum lost to parts cut off it.</summary>
    public double[] SeverancesPerBout { get; init; } = [];
    public double[] ChainedSwingsPerBout { get; init; } = [];
    public double[] StructuralLostPerBout { get; init; } = [];

    // ---- armour, per bout (means over the point's bouts): each fighter's blows taken on armour and the
    // wounds the armour kept off it, and the parts cut off it where armour covered them.
    public double[] ArmorBlowsPerBout { get; init; } = [];
    public double[] ArmorKeptPerBout { get; init; } = [];
    public double[] CutsCoveredPerBout { get; init; } = [];
    /// <summary>Severances per bout by region (every fighter's), in the names' order; and the bouts a severance stopped (<see cref="ActorStop.SeveredStop"/>).</summary>
    public SortedDictionary<string, double> SeveredByRegionPerBout { get; init; } = new(StringComparer.Ordinal);
    public int SeveredStops { get; init; }

    // ---- the point named (the rules package's finding 8) and each fighter's stops by kind (finding 3) ----
    /// <summary>The point in words: per sweep the actor, its sheet and the variable (<see cref="ArenaSummary.PointName"/>).</summary>
    public string Name { get; init; } = "";
    /// <summary>Each fighter's stops at the point by kind (<c>ZERO_HEALTH_STOP:120;DISARMED:3</c>, empty for none): harness stops, never deaths.</summary>
    public string[] StopsBy { get; init; } = [];
    // ---- end
}

/// <summary>
/// The arena's summary: per swept point, the subject's win rate with its 95 % interval, the time to the kill (mean and
/// spread), damage per hit, wounds per bout and contacts by kind — computed from the bouts (the same numbers <c>bouts.csv</c>
/// holds) — and the page that shows them.
/// </summary>
public static class ArenaSummary
{
    /// <summary>The subject: the first sweep's fighter, else the first fighter.</summary>
    public static int Subject(ArenaSpec spec) => spec.Sweeps.Count > 0 ? spec.Sweeps[0].Fighter : 0;

    /// <summary>The numbers per point, in the points' order.</summary>
    public static List<PointSummary> Compute(ArenaSpec spec, IReadOnlyList<BoutResult> results)
    {
        int subject = Subject(spec), n = spec.Fighters.Count;
        var list = new List<PointSummary>();
        var all = results.ToList();
        double?[] runUnpaid = [.. Enumerable.Range(0, n).Select(i => Unpaid(all, i))];   // the extra swings' baseline
        var points = spec.Points;
        for (int p = 0; p < points.Count; p++)
        {
            var bouts = results.Where(r => r.Point == p).ToList();
            int count = bouts.Count;
            var wins = new int[n];
            foreach (var b in bouts) if (b.Winner >= 0) wins[b.Winner]++;
            var (low, high) = Wilson(wins[subject], count);
            var kills = bouts.Where(b => b.KillSeconds is not null).Select(b => b.KillSeconds!.Value).Order().ToList();
            long hits = bouts.Sum(b => b.Fighters.Sum(f => (long)f.Hits));
            long wounds = bouts.Sum(b => b.Fighters.Sum(f => f.WoundsDealt));
            long dice = bouts.Sum(b => b.Fighters.Sum(f => f.DamageDice));
            long successes = bouts.Sum(b => b.Fighters.Sum(f => f.DamageSuccesses));
            double per = Math.Max(1, count);
            double seconds = bouts.Sum(b => b.Ticks) / (double)Core.World.TicksPerSecond;
            list.Add(new PointSummary
            {
                Point = p,
                Values = points[p],
                Bouts = count,
                Kills = kills.Count,
                Wins = wins,
                WinRate = count == 0 ? 0 : wins[subject] / (double)count,
                WinLow = low,
                WinHigh = high,
                KillMean = kills.Count == 0 ? null : kills.Average(),
                KillSd = kills.Count < 2 ? (kills.Count == 1 ? 0 : null) : Math.Sqrt(kills.Sum(k => (k - kills.Average()) * (k - kills.Average())) / (kills.Count - 1)),
                KillMin = kills.Count == 0 ? null : kills[0],
                KillMedian = kills.Count == 0 ? null : kills.Count % 2 == 1 ? kills[kills.Count / 2] : (kills[kills.Count / 2 - 1] + kills[kills.Count / 2]) / 2,
                KillMax = kills.Count == 0 ? null : kills[^1],
                Hits = (int)hits,
                WoundsPerHit = hits == 0 ? null : wounds / (double)hits,
                DicePerHit = hits == 0 ? null : dice / (double)hits,
                SuccessesPerHit = hits == 0 ? null : successes / (double)hits,
                WoundsPerBout = [.. Enumerable.Range(0, n).Select(i => bouts.Sum(b => b.Fighters[i].WoundsDealt) / per)],
                ContactsPerBout =
                [
                    bouts.Sum(b => b.Fighters.Sum(f => f.ContactsBody)) / per, bouts.Sum(b => b.Fighters.Sum(f => f.ContactsShield)) / per,
                    bouts.Sum(b => b.Fighters.Sum(f => f.ContactsWeapon)) / per, bouts.Sum(b => b.Fighters.Sum(f => f.ContactsLevel)) / per,
                ],
                SwingsPerBout = bouts.Sum(b => b.Fighters.Sum(f => f.Swings)) / per,
                ContestPerHit = [.. Enumerable.Range(0, n).Select(i =>
                {
                    long hitsI = bouts.Sum(b => (long)b.Fighters[i].ContestedHits);
                    return hitsI == 0 ? (double?)null : bouts.Sum(b => b.Fighters[i].ContestChange) / (double)hitsI;
                })],
                ContestedHits = bouts.Sum(b => b.Fighters.Sum(f => f.ContestedHits)),
                BloodSpentPerBout = [.. Enumerable.Range(0, n).Select(i => bouts.Sum(b => b.Fighters[i].BloodSpent) / per)],
                UsesPerBout = [.. Enumerable.Range(0, n).Select(i => bouts.Sum(b => b.Fighters[i].Uses) / per)],
                LiveSecondsPerBout = [.. Enumerable.Range(0, n).Select(i => bouts.Sum(b => b.Fighters[i].LiveTicks.Values.Sum()) / (double)Core.World.TicksPerSecond / per)],
                RefusedPerBout = [.. Enumerable.Range(0, n).Select(i => bouts.Sum(b => b.Fighters[i].PowersRefused) / per)],
                SwingsPerSecond = [.. Enumerable.Range(0, n).Select(i => seconds <= 0 ? 0 : bouts.Sum(b => b.Fighters[i].Swings) / seconds)],
                BodyContactsPerSwing = [.. Enumerable.Range(0, n).Select(i => Per(bouts.Sum(b => (long)b.Fighters[i].ContactsBody), bouts.Sum(b => (long)b.Fighters[i].Swings)))],
                WoundsPerSwing = [.. Enumerable.Range(0, n).Select(i => Per(bouts.Sum(b => b.Fighters[i].WoundsDealt), bouts.Sum(b => (long)b.Fighters[i].Swings)))],
                SwingsLivePerBout = [.. Enumerable.Range(0, n).Select(i => bouts.Sum(b => b.Fighters[i].SwingsLive) / per)],
                LiveSwingsPerSecond = [.. Enumerable.Range(0, n).Select(i => bouts.Sum(b => b.Fighters[i].LiveAnyTicks) is var t and > 0
                    ? bouts.Sum(b => b.Fighters[i].SwingsLive) / (t / (double)Core.World.TicksPerSecond) : (double?)null)],
                UnpaidSwingsPerSecond = [.. Enumerable.Range(0, n).Select(i => Unpaid(bouts, i))],
                RunUnpaidSwingsPerSecond = runUnpaid,
                ExtraSwingsPerBout = [.. Enumerable.Range(0, n).Select(i => runUnpaid[i] is { } rate
                    ? (bouts.Sum(b => b.Fighters[i].SwingsLive) - bouts.Sum(b => b.Fighters[i].LiveAnyTicks) / (double)Core.World.TicksPerSecond * rate) / per : (double?)null)],
                Uses = bouts.Sum(b => b.Fighters.Sum(f => f.Uses)),
                PowersRefused = bouts.Sum(b => b.Fighters.Sum(f => f.PowersRefused)),
                FlurryBlowsPerBout = [.. Enumerable.Range(0, n).Select(i => bouts.Sum(b => b.Fighters[i].FlurryBlows) / per)],
                HealthLostPerBout = [.. Enumerable.Range(0, n).Select(i => bouts.Sum(b => b.Fighters[i].HealthLost) / per)],
                ExtraActionsPerBout = [.. Enumerable.Range(0, n).Select(i => runUnpaid[i] is { } rate
                    ? (bouts.Sum(b => b.Fighters[i].SwingsLive + b.Fighters[i].FlurryBlows) - bouts.Sum(b => b.Fighters[i].LiveAnyTicks) / (double)Core.World.TicksPerSecond * rate) / per : (double?)null)],
                //
                SeverancesPerBout = [.. Enumerable.Range(0, n).Select(i => bouts.Sum(b => b.Fighters[i].Severances) / per)],
                ChainedSwingsPerBout = [.. Enumerable.Range(0, n).Select(i => bouts.Sum(b => b.Fighters[i].ChainedSwings) / per)],
                StructuralLostPerBout = [.. Enumerable.Range(0, n).Select(i => bouts.Sum(b => b.Fighters[i].StructuralLost) / per)],
                SeveredByRegionPerBout = new SortedDictionary<string, double>(bouts.SelectMany(b => b.Fighters.SelectMany(f => f.SeveredByRegion))
                    .GroupBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Sum(kv => kv.Value) / per, StringComparer.Ordinal), StringComparer.Ordinal),
                SeveredStops = bouts.Count(b => b.Stop == ActorStop.SeveredStop),
                ArmorBlowsPerBout = [.. Enumerable.Range(0, n).Select(i => bouts.Sum(b => b.Fighters[i].ArmorBlows) / per)],
                ArmorKeptPerBout = [.. Enumerable.Range(0, n).Select(i => bouts.Sum(b => b.Fighters[i].ArmorKept) / per)],
                CutsCoveredPerBout = [.. Enumerable.Range(0, n).Select(i => bouts.Sum(b => b.Fighters[i].CutsCovered) / per)],
                Name = PointName(spec, points[p]),
                StopsBy = [.. Enumerable.Range(0, n).Select(i => string.Join(";", bouts.Where(b => b.Fighters[i].Stop.Length > 0)
                    .GroupBy(b => b.Fighters[i].Stop, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key}:{g.Count()}")))],
            });
        }
        return list;
    }

    static double? Per(long a, long b) => b == 0 ? null : a / (double)b;

    // ---- each swept point named (the rules package's finding 8: "export which exact actor, sheet, and variable
    // each `point` represents so counterfactuals cannot be misread") ----

    /// <summary>
    /// A swept point in words: for each sweep the actor, its sheet (the template's id, or a sheet file; and the
    /// values the spec sets over it) and the variable with its value — <c>Christof (sandbox.newly_embraced): Strength 200</c>,
    /// <c>Guard (sandbox.guard_trained; strength 30): Celerity rank 3 (celerity.active)</c>; "no sweep" without one.
    /// </summary>
    public static string PointName(ArenaSpec spec, int[] values)
    {
        if (spec.Sweeps.Count == 0) return "no sweep";
        return string.Join("; ", spec.Sweeps.Select((s, i) =>
        {
            var f = spec.Fighters[s.Fighter];
            string variable = s.Power ? $"{Title(s.Id)} rank" : Title(s.Id);
            string options = s.Power && s.Options is { Count: > 0 } o && values[i] > 0 ? $" ({string.Join(", ", o)})" : "";
            return $"{f.Name} ({SheetName(f, s)}): {variable} {values[i].ToString(Ic)}{options}";
        }));
    }

    /// <summary>A fighter's sheet as the point's name says it: the template's id (or a sheet file, or the module's default for its character), and the spec's own values over it but the swept one.</summary>
    public static string SheetName(SpecFighter f, ArenaSweep? swept = null)
    {
        string sheet = f.Sheet.Template ?? (f.Sheet.Text is not null ? "a sheet file" : $"the module's sheet for {f.Template}");
        var over = f.Sheet.Values.Where(v => swept is null || v.Id != swept.Id || v.Power != swept.Power)
            .Select(v => $"{(v.Power ? v.Id + " rank" : v.Id)} {v.Value.ToString(Ic)}").ToList();
        return over.Count == 0 ? sheet : $"{sheet}; {string.Join(", ", over)}";
    }

    /// <summary>A registry id as a name: <c>melee</c> → Melee, <c>blood_pool</c> → Blood pool.</summary>
    static string Title(string id) => id.Length == 0 ? id : char.ToUpperInvariant(id[0]) + id[1..].Replace('_', ' ');
    // ---- end

    /// <summary>Severances per bout by region, <c>neck 0.12, right hand 0.40</c> (<c>none</c> without).</summary>
    public static string SeveredText(PointSummary p) => p.SeveredByRegionPerBout.Count == 0 ? "none"
        : string.Join(", ", p.SeveredByRegionPerBout.Select(kv => $"{kv.Key} {kv.Value.ToString("0.00", CultureInfo.InvariantCulture)}"));

    /// <summary>A fighter's swings a second while none of its powers was live, pooled over bouts (null when it never was without).</summary>
    static double? Unpaid(List<BoutResult> bouts, int i)
    {
        long ticks = bouts.Sum(b => b.Ticks - b.Fighters[i].LiveAnyTicks);
        return ticks <= 0 ? null : bouts.Sum(b => b.Fighters[i].Swings - b.Fighters[i].SwingsLive) / (ticks / (double)Core.World.TicksPerSecond);
    }

    /// <summary>Wilson's 95 % score interval for <paramref name="k"/> successes in <paramref name="n"/> (0 to 1 with none).</summary>
    public static (double Low, double High) Wilson(int k, int n)
    {
        if (n == 0) return (0, 1);
        const double z = 1.959963984540054;
        double p = k / (double)n, z2 = z * z;
        double centre = (p + z2 / (2 * n)) / (1 + z2 / n);
        double half = z * Math.Sqrt(p * (1 - p) / n + z2 / (4.0 * n * n)) / (1 + z2 / n);
        return (Math.Max(0, centre - half), Math.Min(1, centre + half));
    }

    static readonly CultureInfo Ic = CultureInfo.InvariantCulture;

    /// <summary>The numbers as JSON (the page carries them, inert, for checking and for other tools).</summary>
    public static string Json(IReadOnlyList<PointSummary> points) =>
        System.Text.Json.JsonSerializer.Serialize(points, new System.Text.Json.JsonSerializerOptions { WriteIndented = false });

    /// <summary>What the page says at its head beside the spec.</summary>
    public sealed record Head(string Version, string ConfigHash, string Profile, ulong SeedBase, string Speed, string Switches = "", string? Banner = null);

    /// <summary>The summary page: self-contained HTML with inline SVG charts, no scripts.</summary>
    public static string Page(ArenaSpec spec, IReadOnlyList<PointSummary> points, Head head)
    {
        int subject = Subject(spec);
        string subjectName = spec.Fighters[subject].Name;
        var sb = new StringBuilder();
        string title = spec.Title.Length > 0 ? spec.Title : "Arena";
        sb.Append($$"""
<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>{{Esc(title)}}</title>
<style>
:root { color-scheme: light; --surface-1: #fcfcfb; --surface-2: #f3f2ef; --text-primary: #0b0b0b; --text-secondary: #52514e; --text-muted: #8a8984;
  --grid: #e4e3df; --axis: #b9b8b2; --series-1: #2a78d6; --series-2: #eb6834; --series-3: #1baf7a; --series-4: #eda100; --band: #2a78d633; }
@media (prefers-color-scheme: dark) { :root:not([data-theme="light"]) { color-scheme: dark; --surface-1: #1a1a19; --surface-2: #242423; --text-primary: #f0efec;
  --text-secondary: #c3c2b7; --text-muted: #8f8e86; --grid: #2e2e2c; --axis: #55544f; --series-1: #3987e5; --series-2: #d95926; --series-3: #199e70; --series-4: #c98500; --band: #3987e544; } }
:root[data-theme="dark"] { color-scheme: dark; --surface-1: #1a1a19; --surface-2: #242423; --text-primary: #f0efec; --text-secondary: #c3c2b7; --text-muted: #8f8e86;
  --grid: #2e2e2c; --axis: #55544f; --series-1: #3987e5; --series-2: #d95926; --series-3: #199e70; --series-4: #c98500; --band: #3987e544; }
body { margin: 0; background: var(--surface-1); color: var(--text-primary); font: 15px/1.45 system-ui, -apple-system, "Segoe UI", sans-serif; }
main { max-width: 1100px; margin: 0 auto; padding: 20px 16px 48px; }
h1 { font-size: 1.5rem; margin: 0 0 4px; } h2 { font-size: 1.05rem; margin: 28px 0 8px; } .muted { color: var(--text-secondary); }
.head { display: grid; grid-template-columns: max-content 1fr; gap: 2px 14px; margin: 10px 0 0; font-size: 0.9rem; }
.head dt { color: var(--text-secondary); } .head dd { margin: 0; font-family: ui-monospace, Consolas, monospace; word-break: break-all; }
details { margin-top: 10px; } pre { background: var(--surface-2); padding: 10px; overflow-x: auto; font-size: 0.8rem; border-radius: 6px; }
.charts { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 480px), 1fr)); gap: 18px; }
figure { margin: 0; background: var(--surface-2); border-radius: 8px; padding: 12px 12px 6px; }
figcaption { font-weight: 600; font-size: 0.95rem; } figcaption span { font-weight: 400; color: var(--text-secondary); font-size: 0.85rem; display: block; }
svg { width: 100%; height: auto; display: block; } svg text { fill: var(--text-secondary); font-size: 12.5px; font-family: inherit; }
.legend { display: flex; flex-wrap: wrap; gap: 4px 14px; font-size: 0.82rem; color: var(--text-secondary); margin: 4px 0 0; }
.legend i { display: inline-block; width: 14px; height: 3px; border-radius: 2px; vertical-align: middle; margin-right: 5px; }
.tiles { display: grid; grid-template-columns: repeat(auto-fit, minmax(170px, 1fr)); gap: 12px; }
.tile { background: var(--surface-2); border-radius: 8px; padding: 12px; } .tile b { display: block; font-size: 1.6rem; } .tile span { color: var(--text-secondary); font-size: 0.85rem; }
.table-wrap { overflow-x: auto; } table { border-collapse: collapse; font-size: 0.82rem; min-width: 100%; }
th, td { padding: 4px 8px; text-align: right; border-bottom: 1px solid var(--grid); white-space: nowrap; } th { color: var(--text-secondary); font-weight: 600; }
.note { color: var(--text-muted); font-size: 0.85rem; }
.warn { border-left: 4px solid var(--series-4); background: var(--surface-2); padding: 8px 12px; margin: 12px 0 0; font-size: 0.9rem; border-radius: 0 6px 6px 0; }
</style></head><body><main>
<h1>{{Esc(title)}}</h1>
<div class="muted">The arena: {{spec.Fighters.Count}} fighters, {{spec.Bouts}} bouts per value, at most {{F(spec.Seconds)}} s each{{(spec.Sweeps.Count == 0 ? ", nothing swept" : $", sweeping {string.Join(" × ", spec.Sweeps.Select(s => Esc(s.Label(spec))))}")}}. The subject is <b>{{Esc(subjectName)}}</b>.</div>
<dl class="head">
<dt>Version</dt><dd>{{Esc(head.Version)}}</dd>
<dt>Module config</dt><dd>{{Esc(head.ConfigHash)}} ({{Esc(head.Profile)}})</dd>
<dt>Seed base</dt><dd>{{head.SeedBase}} (bout b at every value has seed {{head.SeedBase}} + b)</dd>
<dt>Level</dt><dd>{{Esc(spec.Level)}}</dd>
<dt>Fighters</dt><dd>{{Esc(string.Join("; ", spec.Fighters.Select(f => $"{f.Name}: {f.Template}{(f.Weapon is null ? "" : " with " + f.Weapon)}, {f.Brain.ToString().ToLowerInvariant()}{(f.Brain == CastBrain.Script ? "" : $" against {spec.Fighters[f.Foe].Name}")}")))}}</dd>
<dt>Switches</dt><dd>{{Esc(head.Switches.Length == 0 ? "none" : head.Switches)}}</dd>
<dt>Speed</dt><dd>{{Esc(head.Speed)}}</dd>
</dl>
{{(head.Banner is { Length: > 0 } banner ? $"<p class=\"warn\">{string.Join("<br>", banner.Split("   ", StringSplitOptions.RemoveEmptyEntries).Select(Esc))}</p>" : "")}}
<details><summary>The spec</summary><pre>{{Esc(spec.Text)}}</pre></details>
""");
        bool anyKill = points.Any(p => p.Kills > 0), anyHit = points.Any(p => p.Hits > 0);
        if (!anyKill) sb.Append("<p class=\"note\">No bout ended in a zero-Health stop: every bout ran to its time.</p>\n");
        sb.Append("<p class=\"note\">A bout ends at the first zero-Health stop (Health at nothing: a harness stop, ENGINE_SANDBOX, not a ruled death) or severed stop (the neck or the trunk cut through, whatever Health is left); its \"winner\" is the one who struck it.</p>\n");
        // ---- the rules package's finding 3 (Priority A), plainly; and each point named (finding 8).
        sb.Append("<p class=\"warn\">No torpor and no Final Death exist yet: every stop here is a test harness's stop (ZERO_HEALTH_STOP, SEVERED_STOP, DISARMED), not a death, and no win rate or stop count is a mortality statistic. In bouts.csv each fighter's <b>stopped</b> (once called \"died\") and <b>stop</b> say whose part a stop ended and which.</p>\n");
        if (spec.Sweeps.Count > 0)
        {
            sb.Append("<h2>The points</h2><p class=\"note\">What each swept point is: the actor, its sheet and the variable (the columns ");
            sb.Append(Esc(string.Join(", ", spec.Sweeps.Select(s => s.Label(spec)))));
            sb.Append(").</p><ul class=\"note\">");
            foreach (var p in points) sb.Append($"<li>point {p.Point}: {Esc(p.Name)}</li>");
            sb.Append("</ul>\n");
        }
        // ---- end

        if (spec.Sweeps.Count == 0 || points.Count == 1)
        {
            var p = points[0];
            sb.Append("<h2>At a glance</h2><div class=\"tiles\">");
            Tile(sb, $"{F(p.WinRate * 100, "0")} %", $"{Esc(subjectName)} wins (95 %: {F(p.WinLow * 100, "0")}–{F(p.WinHigh * 100, "0")} %), {p.Bouts} bouts");
            Tile(sb, p.KillMean is { } km ? $"{F(km, "0.0")} s" : "—", p.KillMean is null ? "time to the zero-Health stop: none" : $"time to the zero-Health stop, mean (sd {F(p.KillSd ?? 0, "0.0")} s; {p.Kills} stops)");
            Tile(sb, p.WoundsPerHit is { } wh ? F(wh, "0.00") : "—", p.Hits == 0 ? "wounds per hit: no hit" : $"wounds per hit ({p.Hits} hits; dice {F(p.DicePerHit ?? 0, "0.0")}, successes {F(p.SuccessesPerHit ?? 0, "0.0")})");
            Tile(sb, F(p.ContactsPerBout.Sum(), "0.0"), $"contacts per bout (body {F(p.ContactsPerBout[0], "0.0")}, shield {F(p.ContactsPerBout[1], "0.0")}, weapon {F(p.ContactsPerBout[2], "0.0")}, level {F(p.ContactsPerBout[3], "0.0")})");
            Tile(sb, F(p.SwingsPerBout, "0.0"), "blows begun per bout");
            if (p.ContestedHits > 0)
                Tile(sb, string.Join(" / ", p.ContestPerHit.Select(c => c is { } v ? F(v, "+0.00;-0.00;0.00") : "—")), $"the contest per hit, dice ({string.Join(" / ", spec.Fighters.Select(f => Esc(f.Name)))}; {p.ContestedHits} contested hits)");
            // Each swing's worth and the powers.
            string names = string.Join(" / ", spec.Fighters.Select(f => Esc(f.Name)));
            if (p.SwingsPerSecond.Length > 0)
                Tile(sb, string.Join(" / ", p.SwingsPerSecond.Select(v => F(v, "0.00"))), $"swings a second ({names}); body contacts a swing {string.Join(" / ", p.BodyContactsPerSwing.Select(v => O(v, "0.00")))}, wounds a swing {string.Join(" / ", p.WoundsPerSwing.Select(v => O(v, "0.00")))}");
            if (p.Uses > 0 || p.PowersRefused > 0)
                Tile(sb, string.Join(" / ", p.UsesPerBout.Select(v => F(v, "0.0"))), $"powers used per bout ({names}); Blood spent {string.Join(" / ", p.BloodSpentPerBout.Select(v => F(v, "0")))}, seconds live {string.Join(" / ", p.LiveSecondsPerBout.Select(v => F(v, "0.0")))}, refused {string.Join(" / ", p.RefusedPerBout.Select(v => F(v, "0.0")))} per bout");
            // The flurry's blows and Health lost (the 1–1000 scale).
            if (p.FlurryBlowsPerBout.Any(v => v > 0))
                Tile(sb, string.Join(" / ", p.FlurryBlowsPerBout.Select(v => F(v, "0.0"))), $"flurry blows per bout ({names}); extra actions its powers bought {string.Join(" / ", p.ExtraActionsPerBout.Select(v => O(v, "+0.0;-0.0;0.0")))}");
            if (p.HealthLostPerBout.Length > 0)
                Tile(sb, string.Join(" / ", p.HealthLostPerBout.Select(v => F(v, "0"))), $"Health lost per bout ({names}; 100 a person by default, 15 a wound)");
            // Severance and the chain.
            if (p.SeverancesPerBout.Any(v => v > 0) || p.ChainedSwingsPerBout.Any(v => v > 0))
                Tile(sb, string.Join(" / ", p.SeverancesPerBout.Select(v => F(v, "0.00"))), $"severances per bout ({names}; {SeveredText(p)}); swings cut through and on {string.Join(" / ", p.ChainedSwingsPerBout.Select(v => F(v, "0.00")))}; Health lost to structure {string.Join(" / ", p.StructuralLostPerBout.Select(v => F(v, "0.0")))}; {p.SeveredStops} stops by a severance");
            // ---- armour, when any blow met it.
            if (p.ArmorBlowsPerBout.Any(v => v > 0))
                Tile(sb, string.Join(" / ", p.ArmorKeptPerBout.Select(v => F(v, "0.00"))), $"wounds kept off by armour per bout ({names}); blows taken on armour {string.Join(" / ", p.ArmorBlowsPerBout.Select(v => F(v, "0.00")))}; parts cut off where armour covered {string.Join(" / ", p.CutsCoveredPerBout.Select(v => F(v, "0.00")))}");
            sb.Append("</div>\n");
        }
        else
        {
            var x = spec.Sweeps[0];
            string xLabel = x.Label(spec);
            // A grid: one line per value of the second sweep (up to four), else the first sweep alone.
            var groups = spec.Sweeps.Count == 2
                ? spec.Sweeps[1].Values.Take(4).Select(v => ($"{spec.Sweeps[1].Label(spec)} = {v}", points.Where(p => p.Values[1] == v).ToList())).ToList()
                : [(subjectName, points.ToList())];
            sb.Append("<h2>Against the swept value</h2>\n");
            if (spec.Sweeps.Count == 2 && spec.Sweeps[1].Values.Count > 4)
                sb.Append($"<p class=\"note\">The grid's charts show the first four values of {Esc(spec.Sweeps[1].Label(spec))}; the table below has them all.</p>\n");
            sb.Append("<div class=\"charts\">\n");
            Chart(sb, $"{Esc(subjectName)}'s win rate", "with its 95 % interval (Wilson)", xLabel, "win rate", true,
                groups.Select(g => new Series(g.Item1, g.Item2.Select(p => (double)p.Values[0]).ToArray(), g.Item2.Select(p => (double?)p.WinRate).ToArray(),
                    g.Item2.Select(p => (double?)p.WinLow).ToArray(), g.Item2.Select(p => (double?)p.WinHigh).ToArray())).ToList(), 0, 1);
            Chart(sb, "Time to the zero-Health stop", "mean, with ± one standard deviation (s); a harness stop, not a ruled death", xLabel, "seconds", false,
                groups.Select(g => new Series(g.Item1, g.Item2.Select(p => (double)p.Values[0]).ToArray(), g.Item2.Select(p => p.KillMean).ToArray(),
                    g.Item2.Select(p => p.KillMean - p.KillSd).ToArray(), g.Item2.Select(p => p.KillMean + p.KillSd).ToArray())).ToList(), 0, null);
            if (spec.Sweeps.Count == 1)
            {
                var xs = points.Select(p => (double)p.Values[0]).ToArray();
                Chart(sb, "Damage per hit", "wounds, damage dice and damage successes per resolved hit (a contact; its wounds count its flurry's blows too, its dice and successes its own blow's)", xLabel, "per hit", false,
                    [new Series("wounds", xs, points.Select(p => p.WoundsPerHit).ToArray()), new Series("damage dice", xs, points.Select(p => p.DicePerHit).ToArray()),
                     new Series("successes", xs, points.Select(p => p.SuccessesPerHit).ToArray())], 0, null);
                Chart(sb, "Wounds per bout", "dealt, by each fighter (mean)", xLabel, "wounds", false,
                    [.. spec.Fighters.Take(4).Select((f, i) => new Series(f.Name, xs, points.Select(p => (double?)p.WoundsPerBout[i]).ToArray()))], 0, null);
                string[] kinds = ["body", "shield", "weapon", "level"];
                Chart(sb, "Contacts per bout", "by what the blow met (mean, all fighters)", xLabel, "contacts", false,
                    [.. kinds.Select((k, i) => new Series(k, xs, points.Select(p => (double?)p.ContactsPerBout[i]).ToArray()))], 0, null);
                if (points.Any(p => p.ContestedHits > 0))   // the contest on contact, per hit, by the attacker (it may be below nothing)
                {
                    var contest = spec.Fighters.Take(4).Select((f, i) => new Series(f.Name, xs, points.Select(p => p.ContestPerHit.Length > i ? p.ContestPerHit[i] : null).ToArray())).ToList();
                    var ys = contest.SelectMany(c => c.Y).Where(y => y is not null).Select(y => y!.Value).DefaultIfEmpty(0).ToList();
                    // Four whole steps of 1, 2, 5 or 10 dice from a multiple of the step at or below the least (and nothing).
                    double least = Math.Min(0, ys.Min()), most = Math.Max(1, ys.Max()), step = 1;
                    foreach (double st in new double[] { 1, 2, 5, 10, 20 }) { step = st; if (Math.Floor(least / st) * st + 4 * st >= most) break; }
                    double lo = Math.Floor(least / step) * step, hi = lo + 4 * step;
                    Chart(sb, "The contest per hit", "the net change to the damage pool per contested hit, by the attacker (dice; mean)", xLabel, "dice", false, contest, lo, hi);
                }
                // What each swing came to, by fighter (whatever makes a swing faster or carries more in it).
                Series[] ByFighter(Func<PointSummary, int, double?> y) => [.. spec.Fighters.Take(4).Select((f, i) => new Series(f.Name, xs, points.Select(p => y(p, i)).ToArray()))];
                Chart(sb, "Swings a second", "blows begun over the bouts' time, by fighter", xLabel, "swings/s", false,
                    [.. ByFighter((p, i) => p.SwingsPerSecond.Length > i ? p.SwingsPerSecond[i] : null)], 0, null);
                Chart(sb, "Body contacts a swing", "contacts on a body per blow begun, by the attacker", xLabel, "contacts/swing", false,
                    [.. ByFighter((p, i) => p.BodyContactsPerSwing.Length > i ? p.BodyContactsPerSwing[i] : null)], 0, null);
                Chart(sb, "Wounds a swing", "wounds dealt per blow begun, by the attacker", xLabel, "wounds/swing", false,
                    [.. ByFighter((p, i) => p.WoundsPerSwing.Length > i ? p.WoundsPerSwing[i] : null)], 0, null);
                if (points.Any(p => p.Uses > 0 || p.PowersRefused > 0))
                {
                    Chart(sb, "Blood spent per bout", "by fighter (mean; 1.1's scale, a vampire's pool 100)", xLabel, "Blood", false,
                        [.. ByFighter((p, i) => p.BloodSpentPerBout.Length > i ? p.BloodSpentPerBout[i] : null)], 0, null);
                    Chart(sb, "Powers used per bout", "uses, renewals included, by fighter (mean)", xLabel, "uses", false,
                        [.. ByFighter((p, i) => p.UsesPerBout.Length > i ? p.UsesPerBout[i] : null)], 0, null);
                    // What a live power did to the pace of blows: the swings a second while one was live, against the run's while none was.
                    var pace = new List<Series>();
                    for (int i = 0; i < Math.Min(2, spec.Fighters.Count); i++)
                    {
                        int k = i;
                        if (!points.Any(p => p.LiveSwingsPerSecond.Length > k && p.LiveSwingsPerSecond[k] is not null)) continue;
                        pace.Add(new Series($"{spec.Fighters[k].Name} while live", xs, points.Select(p => p.LiveSwingsPerSecond.Length > k ? p.LiveSwingsPerSecond[k] : null).ToArray()));
                        pace.Add(new Series($"{spec.Fighters[k].Name} while none (the run's)", xs, points.Select(p => p.RunUnpaidSwingsPerSecond.Length > k ? p.RunUnpaidSwingsPerSecond[k] : null).ToArray()));
                    }
                    if (pace.Count > 0)
                        Chart(sb, "Swings a second, a power live or not", "blows begun a second while a power of the fighter's was live, and while none was over the whole run", xLabel, "swings/s", false, pace, 0, null);
                    // The extra actions may come out below nothing (fewer while live than the unpaid rate would give): the axis goes there.
                    // An action is a swing or a blow a swing carried (Celerity's flurry), so the receipt counts both.
                    var extra = ByFighter((p, i) => p.ExtraActionsPerBout.Length > i ? p.ExtraActionsPerBout[i] : null);
                    double extraLeast = Math.Min(0, extra.SelectMany(x => x.Y).Where(y => y is not null).Select(y => y!.Value).DefaultIfEmpty(0).Min());
                    Chart(sb, "Extra actions a bout its powers bought", "swings while live + flurry blows − seconds live × the run's swings a second while none was (the rules package's entitlement receipt), by fighter", xLabel, "actions", false,
                        [.. extra], Math.Floor(extraLeast), null);
                    if (points.Any(p => p.FlurryBlowsPerBout.Any(x => x > 0)))
                        Chart(sb, "Flurry blows a bout", "the extra blows each swing that landed on a body carried (Celerity's speed plus flurry), each its own staged harm, by the attacker (mean)", xLabel, "blows", false,
                            [.. ByFighter((p, i) => p.FlurryBlowsPerBout.Length > i ? p.FlurryBlowsPerBout[i] : null)], 0, null);
                }
                // Health lost a bout, by fighter (the 1–1000 scale; a wound is the module's hp_per_wound of it).
                Chart(sb, "Health lost a bout", "the Health each fighter's harm took (mean; 100 a person by default, 15 a wound)", xLabel, "Health", false,
                    [.. ByFighter((p, i) => p.HealthLostPerBout.Length > i ? p.HealthLostPerBout[i] : null)], 0, null);
            }
            else
            {
                Chart(sb, "Wounds per hit", "mean wounds per resolved hit (a contact, its flurry's blows' wounds with it)", xLabel, "wounds", false,
                    groups.Select(g => new Series(g.Item1, g.Item2.Select(p => (double)p.Values[0]).ToArray(), g.Item2.Select(p => p.WoundsPerHit).ToArray())).ToList(), 0, null);
                Chart(sb, $"{Esc(subjectName)}'s wounds per bout", "dealt (mean)", xLabel, "wounds", false,
                    groups.Select(g => new Series(g.Item1, g.Item2.Select(p => (double)p.Values[0]).ToArray(), g.Item2.Select(p => (double?)p.WoundsPerBout[subject]).ToArray())).ToList(), 0, null);
                Chart(sb, "Contacts per bout", "all kinds, all fighters (mean)", xLabel, "contacts", false,
                    groups.Select(g => new Series(g.Item1, g.Item2.Select(p => (double)p.Values[0]).ToArray(), g.Item2.Select(p => (double?)p.ContactsPerBout.Sum()).ToArray())).ToList(), 0, null);
            }
            sb.Append("</div>\n");
            if (!anyHit) sb.Append("<p class=\"note\">No hit was resolved, so the damage charts are empty.</p>\n");
        }

        // The table view: every number the charts show.
        sb.Append("<h2>The numbers</h2><div class=\"table-wrap\"><table><thead><tr>");
        foreach (var s in spec.Sweeps) sb.Append($"<th>{Esc(s.Label(spec))}</th>");
        sb.Append("<th>the point</th><th>bouts</th><th>stops</th>");   // the point named
        foreach (var f in spec.Fighters) sb.Append($"<th>{Esc(f.Name)} wins</th>");
        sb.Append($"<th>{Esc(subjectName)} win rate</th><th>95 % interval</th><th>stop mean s</th><th>stop sd s</th><th>stop median s</th><th>stop min–max s</th>"
                  + "<th>hits</th><th>wounds/hit</th><th>dice/hit</th><th>successes/hit</th>");
        foreach (var f in spec.Fighters) sb.Append($"<th>{Esc(f.Name)} wounds/bout</th>");
        sb.Append("<th>body</th><th>shield</th><th>weapon</th><th>level</th><th>blows/bout</th>");
        foreach (var f in spec.Fighters) sb.Append($"<th>{Esc(f.Name)} contest/hit</th>");
        foreach (var f in spec.Fighters)
            sb.Append($"<th>{Esc(f.Name)} swings/s</th><th>{Esc(f.Name)} body/swing</th><th>{Esc(f.Name)} wounds/swing</th><th>{Esc(f.Name)} Blood/bout</th><th>{Esc(f.Name)} uses/bout</th><th>{Esc(f.Name)} live s/bout</th><th>{Esc(f.Name)} refused/bout</th>"
                      + $"<th>{Esc(f.Name)} swings live/bout</th><th>{Esc(f.Name)} swings/s live</th><th>{Esc(f.Name)} unpaid swings/s</th><th>{Esc(f.Name)} extra swings/bout</th>"
                      + $"<th>{Esc(f.Name)} flurry blows/bout</th><th>{Esc(f.Name)} extra actions/bout</th><th>{Esc(f.Name)} Health lost/bout</th>");
        foreach (var f in spec.Fighters) sb.Append($"<th>{Esc(f.Name)} stopped by</th>");
        sb.Append("</tr></thead><tbody>\n");
        foreach (var p in points)
        {
            sb.Append("<tr>");
            foreach (var v in p.Values) sb.Append($"<td>{v}</td>");
            sb.Append($"<td style=\"text-align:left\">{Esc(p.Name)}</td><td>{p.Bouts}</td><td>{p.Kills}</td>");
            foreach (var w in p.Wins) sb.Append($"<td>{w}</td>");
            sb.Append($"<td>{F(p.WinRate, "0.000")}</td><td>{F(p.WinLow, "0.000")}–{F(p.WinHigh, "0.000")}</td><td>{O(p.KillMean, "0.00")}</td><td>{O(p.KillSd, "0.00")}</td><td>{O(p.KillMedian, "0.00")}</td>"
                      + $"<td>{(p.KillMin is null ? "—" : $"{F(p.KillMin.Value, "0.00")}–{F(p.KillMax!.Value, "0.00")}")}</td><td>{p.Hits}</td><td>{O(p.WoundsPerHit, "0.00")}</td><td>{O(p.DicePerHit, "0.00")}</td><td>{O(p.SuccessesPerHit, "0.00")}</td>");
            foreach (var w in p.WoundsPerBout) sb.Append($"<td>{F(w, "0.00")}</td>");
            foreach (var c in p.ContactsPerBout) sb.Append($"<td>{F(c, "0.00")}</td>");
            sb.Append($"<td>{F(p.SwingsPerBout, "0.00")}</td>");
            for (int i = 0; i < spec.Fighters.Count; i++) sb.Append($"<td>{(p.ContestPerHit.Length > i ? O(p.ContestPerHit[i], "+0.00;-0.00;0.00") : "—")}</td>");
            for (int i = 0; i < spec.Fighters.Count; i++)
                sb.Append($"<td>{At(p.SwingsPerSecond, i, "0.00")}</td><td>{(p.BodyContactsPerSwing.Length > i ? O(p.BodyContactsPerSwing[i], "0.00") : "—")}</td><td>{(p.WoundsPerSwing.Length > i ? O(p.WoundsPerSwing[i], "0.00") : "—")}</td>"
                          + $"<td>{At(p.BloodSpentPerBout, i, "0.0")}</td><td>{At(p.UsesPerBout, i, "0.00")}</td><td>{At(p.LiveSecondsPerBout, i, "0.0")}</td><td>{At(p.RefusedPerBout, i, "0.00")}</td>"
                          + $"<td>{At(p.SwingsLivePerBout, i, "0.0")}</td><td>{(p.LiveSwingsPerSecond.Length > i ? O(p.LiveSwingsPerSecond[i], "0.00") : "—")}</td><td>{(p.UnpaidSwingsPerSecond.Length > i ? O(p.UnpaidSwingsPerSecond[i], "0.00") : "—")}</td><td>{(p.ExtraSwingsPerBout.Length > i ? O(p.ExtraSwingsPerBout[i], "+0.0;-0.0;0.0") : "—")}</td>"
                          + $"<td>{At(p.FlurryBlowsPerBout, i, "0.00")}</td><td>{(p.ExtraActionsPerBout.Length > i ? O(p.ExtraActionsPerBout[i], "+0.0;-0.0;0.0") : "—")}</td><td>{At(p.HealthLostPerBout, i, "0.0")}</td>");
            for (int i = 0; i < spec.Fighters.Count; i++) sb.Append($"<td>{(p.StopsBy.Length > i && p.StopsBy[i].Length > 0 ? Esc(p.StopsBy[i]) : "—")}</td>");
            sb.Append("</tr>\n");
        }
        sb.Append("</tbody></table></div>\n");
        // Severance and the chain, per point, when any bout had one.
        if (points.Any(p => p.SeverancesPerBout.Any(v => v > 0) || p.ChainedSwingsPerBout.Any(v => v > 0)))
        {
            sb.Append("<h2>Severance</h2><p class=\"note\">Parts cut off by a blade (rules/severance, ENGINE_SANDBOX thresholds, Health after soak as a share of the victim's purchased maximum: hand 45 %, forearm 60 %, upper arm 75 %, neck 90 %, trunk 200 %), swings that cut through and met another body (the chain), Health the effective maximum lost to missing anatomy (the union), per bout.</p><div class=\"wide\"><table><thead><tr>");
            foreach (var s in spec.Sweeps) sb.Append($"<th>{Esc(s.Label(spec))}</th>");
            foreach (var f in spec.Fighters) sb.Append($"<th>{Esc(f.Name)} severances</th>");
            sb.Append("<th>by region</th>");
            foreach (var f in spec.Fighters) sb.Append($"<th>{Esc(f.Name)} chained swings</th>");
            foreach (var f in spec.Fighters) sb.Append($"<th>{Esc(f.Name)} Health lost to structure</th>");
            sb.Append("<th>severed stops</th></tr></thead><tbody>\n");
            foreach (var p in points)
            {
                sb.Append("<tr>");
                foreach (var v in p.Values) sb.Append($"<td>{v}</td>");
                foreach (var v in p.SeverancesPerBout) sb.Append($"<td>{F(v, "0.00")}</td>");
                sb.Append($"<td>{Esc(SeveredText(p))}</td>");
                foreach (var v in p.ChainedSwingsPerBout) sb.Append($"<td>{F(v, "0.00")}</td>");
                foreach (var v in p.StructuralLostPerBout) sb.Append($"<td>{F(v, "0.0")}</td>");
                sb.Append($"<td>{p.SeveredStops}</td></tr>\n");
            }
            sb.Append("</tbody></table></div>\n");
        }
        sb.Append("<p class=\"note\">Every bout is a row of bouts.csv and every contact a row of contacts.csv, beside this page; the first bouts of each value are tapes (tapes/, each passes --replay-check). The win rate's interval is Wilson's score interval at 95 %. Numbers marked SANDBOX in the docs (the brain's, the module's) are a testbed's, not the game's.</p>\n");
        sb.Append($"<script type=\"application/json\" id=\"arena-numbers\">{Json(points).Replace("</", "<\\/")}</script>\n");
        sb.Append("</main></body></html>\n");
        return sb.ToString();
    }

    static void Tile(StringBuilder sb, string big, string words) => sb.Append($"<div class=\"tile\"><b>{big}</b><span>{words}</span></div>");

    /// <summary>A line of a chart: its name, its x and y (null where there is none), and an interval round each y (or none).</summary>
    sealed record Series(string Name, double[] X, double?[] Y, double?[]? Low = null, double?[]? High = null);

    /// <summary>A line chart (one y-axis, a recessive grid, 2 px lines, 8 px markers, intervals as whiskers, a title tooltip on each point).</summary>
    static void Chart(StringBuilder sb, string title, string subtitle, string xLabel, string yLabel, bool percent, List<Series> series, double yMin, double? yMaxFixed)
    {
        const double W = 560, H = 250, L = 64, R = 16, T = 12, B = 40;
        var xsAll = series.SelectMany(s => s.X).ToList();
        double xMin = xsAll.Count == 0 ? 0 : xsAll.Min(), xMax = xsAll.Count == 0 ? 1 : xsAll.Max();
        if (xMax <= xMin) { xMin -= 1; xMax += 1; }
        double pad = (xMax - xMin) * 0.04;     // the first and last points off the axes
        (xMin, xMax) = (xMin - pad, xMax + pad);
        var ysAll = series.SelectMany(s => s.Y.Concat(s.High ?? []).Where(v => v is not null).Select(v => v!.Value)).ToList();
        double yMax = yMaxFixed ?? (ysAll.Count == 0 || ysAll.Max() <= yMin ? yMin + 1 : NiceCeiling(ysAll.Max()));
        if (yMax <= yMin) yMax = yMin + 1;
        double PxAt(double x) => L + (x - xMin) / (xMax - xMin) * (W - L - R);
        double Py(double y) => T + (1 - (Math.Clamp(y, yMin, yMax) - yMin) / (yMax - yMin)) * (H - T - B);
        sb.Append($"<figure><figcaption>{title}<span>{Esc(subtitle)}</span></figcaption>");
        if (series.Count > 1)
        {
            sb.Append("<div class=\"legend\">");
            for (int i = 0; i < series.Count; i++) sb.Append($"<span><i style=\"background:var(--series-{i % 4 + 1})\"></i>{Esc(series[i].Name)}</span>");
            sb.Append("</div>");
        }
        sb.Append(FormattableString.Invariant($"<svg viewBox=\"0 0 {W} {H}\" role=\"img\" aria-label=\"{Esc(title)} against {Esc(xLabel)}\">"));
        // The grid and the y-axis's numbers: five steps.
        for (int i = 0; i <= 4; i++)
        {
            double v = yMin + (yMax - yMin) * i / 4, y = Py(v);
            sb.Append(FormattableString.Invariant($"<line x1=\"{L}\" x2=\"{W - R}\" y1=\"{y:0.#}\" y2=\"{y:0.#}\" stroke=\"var(--grid)\" stroke-width=\"1\"/>"));
            double step = (yMax - yMin) / 4;
            sb.Append(FormattableString.Invariant($"<text x=\"{L - 6}\" y=\"{y + 4:0.#}\" text-anchor=\"end\">{(percent ? F(v * 100, "0") + " %" : F(v, Math.Abs(step - Math.Round(step)) < 1e-9 ? "0" : "0.##"))}</text>"));
        }
        // The x-axis: each swept value (thinned to about eight labels).
        var ticks = xsAll.Distinct().Order().ToList();
        int every = Math.Max(1, (int)Math.Ceiling(ticks.Count / 8.0));
        sb.Append(FormattableString.Invariant($"<line x1=\"{L}\" x2=\"{W - R}\" y1=\"{H - B}\" y2=\"{H - B}\" stroke=\"var(--axis)\" stroke-width=\"1\"/>"));
        for (int i = 0; i < ticks.Count; i += every)
            sb.Append(FormattableString.Invariant($"<text x=\"{PxAt(ticks[i]):0.#}\" y=\"{H - B + 16}\" text-anchor=\"middle\">{F(ticks[i], "0.##")}</text>"));
        sb.Append(FormattableString.Invariant($"<text x=\"{(L + W - R) / 2:0.#}\" y=\"{H - 6}\" text-anchor=\"middle\">{Esc(xLabel)}</text>"));
        sb.Append(FormattableString.Invariant($"<text x=\"12\" y=\"{(T + H - B) / 2:0.#}\" text-anchor=\"middle\" transform=\"rotate(-90 12 {(T + H - B) / 2:0.#})\">{Esc(yLabel)}</text>"));
        bool intervals = series.Any(x => x.Low is not null);
        for (int s = 0; s < series.Count; s++)
        {
            var se = series[s];
            string colour = $"var(--series-{s % 4 + 1})";
            // Several lines with intervals: each a few pixels aside, so the whiskers at one value do not hide each other.
            double dodge = intervals && series.Count > 1 ? (s - (series.Count - 1) / 2.0) * 6 : 0;
            double Px(double x) => PxAt(x) + dodge;
            // Whiskers for the interval.
            if (se.Low is not null && se.High is not null)
                for (int i = 0; i < se.X.Length; i++)
                    if (se.Low[i] is { } lo && se.High[i] is { } hi)
                        sb.Append(FormattableString.Invariant($"<line x1=\"{Px(se.X[i]):0.#}\" x2=\"{Px(se.X[i]):0.#}\" y1=\"{Py(lo):0.#}\" y2=\"{Py(hi):0.#}\" stroke=\"{colour}\" stroke-width=\"2\" stroke-linecap=\"round\" opacity=\"0.45\"/>"));
            // The line, broken where a value is missing.
            var path = new StringBuilder();
            bool pen = false;
            for (int i = 0; i < se.X.Length; i++)
            {
                if (se.Y[i] is not { } y) { pen = false; continue; }
                path.Append(FormattableString.Invariant($"{(pen ? "L" : "M")}{Px(se.X[i]):0.#} {Py(y):0.#} "));
                pen = true;
            }
            if (path.Length > 0) sb.Append($"<path d=\"{path.ToString().Trim()}\" fill=\"none\" stroke=\"{colour}\" stroke-width=\"2\" stroke-linejoin=\"round\" stroke-linecap=\"round\"/>");
            for (int i = 0; i < se.X.Length; i++)
            {
                if (se.Y[i] is not { } y) continue;
                string tip = $"{se.Name}: {xLabel} {F(se.X[i], "0.##")} → {(percent ? F(y * 100, "0.0") + " %" : F(y, "0.###"))}"
                             + (se.Low?[i] is { } lo2 && se.High?[i] is { } hi2 ? $" ({(percent ? F(lo2 * 100, "0.0") + "–" + F(hi2 * 100, "0.0") + " %" : F(lo2, "0.##") + "–" + F(hi2, "0.##"))})" : "");
                sb.Append(FormattableString.Invariant($"<circle cx=\"{Px(se.X[i]):0.#}\" cy=\"{Py(y):0.#}\" r=\"4\" fill=\"{colour}\" stroke=\"var(--surface-2)\" stroke-width=\"2\"><title>{Esc(tip)}</title></circle>"));
            }
        }
        sb.Append("</svg></figure>\n");
    }

    static double NiceCeiling(double v)
    {
        if (v <= 0) return 1;
        double p = Math.Pow(10, Math.Floor(Math.Log10(v)));
        // Ceilings whose quarters are round (the grid has four steps): 1.2 → 0.3, 1.6 → 0.4, 2.4 → 0.6, 6 → 1.5, …
        foreach (double m in new[] { 1, 1.2, 1.6, 2, 2.4, 3, 4, 6, 8, 10 }) if (m * p >= v - 1e-12) return m * p;
        return 10 * p;
    }

    static string F(double v, string format = "0.###") => v.ToString(format, Ic);

    static string O(double? v, string format) => v is { } x ? F(x, format) : "—";

    static string At(double[] v, int i, string format) => v.Length > i ? F(v[i], format) : "—";

    static string Esc(string s) => System.Net.WebUtility.HtmlEncode(s);
}
