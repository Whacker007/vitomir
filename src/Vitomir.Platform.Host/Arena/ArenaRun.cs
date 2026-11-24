// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Diagnostics;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Powers;

namespace Vitomir.Platform.Host.Arena;

/// <summary>One fighter's counts in a bout: what it did, met and suffered.</summary>
public sealed class FighterTally
{
    /// <summary>Blows begun (<see cref="AttackBegun"/>) and their <c>Strike</c> moments (<see cref="StrikeMoment"/>).</summary>
    public int Swings, Strikes;
    /// <summary>Contacts its blows made (<see cref="ContactMade"/>, as the attacker), by what they met.</summary>
    public int ContactsBody, ContactsShield, ContactsWeapon, ContactsLevel;
    /// <summary>Contacts the rules resolved (<see cref="HarmResolved"/>, as the attacker), and their staged numbers summed.</summary>
    public int Hits;
    public long DamageDice, DamageSuccesses, WoundsDealt, WoundsTaken;
    /// <summary>Its Health after the last harm it took, or null when it took none.</summary>
    public int? HealthLeft;
    /// <summary>Blocks raised (<see cref="BlockRaised"/>) and attacks refused (<see cref="AttackRefused"/>).</summary>
    public int BlocksRaised, Refused;
    /// <summary>
    /// The rules package's finding 3, Priority A: whether a stop ended this fighter's part in the bout — a
    /// zero-Health stop or a severed stop (<see cref="ActorDied"/> for it) or, of two, its weapon hand cut off (the bout's
    /// <c>DISARMED</c>) — and which (<see cref="Stop"/>). A harness stop, never a death: no torpor or Final Death exists yet
    ///. Earlier builds called it <c>Died</c> (the column <c>died</c>), set on a zero-Health or severed stop only.
    /// </summary>
    public bool Stopped;
    /// <summary>The stop that ended its part, <c>ZERO_HEALTH_STOP</c>, <c>SEVERED_STOP</c> or <c>DISARMED</c>; empty for none.</summary>
    public string Stop = "";
    /// <summary>Its hits the contest on contact ran for (<see cref="HarmResolved.Contested"/>, as the attacker), and the net change to the damage pool summed.</summary>
    public int ContestedHits;
    public long ContestChange;

    /// <summary>The mean net change the contest made to its damage pools, per contested hit (null with none).</summary>
    public double? MeanContestChange => ContestedHits == 0 ? null : ContestChange / (double)ContestedHits;

    public double? MeanDice => Hits == 0 ? null : DamageDice / (double)Hits;
    public double? MeanSuccesses => Hits == 0 ? null : DamageSuccesses / (double)Hits;

    // What its powers did, from the module's events and views (whatever a power's effect is, the arena counts the same).

    /// <summary>Its Blood at the start and at the end (the module's <see cref="Vitals"/>, or a use's before and after), null when it has none to show.</summary>
    public int? BloodStart, BloodEnd;
    /// <summary>The Blood its uses paid (<see cref="PowerUsed.Cost"/> summed).</summary>
    public long BloodSpent;
    /// <summary>Its powers used (<see cref="PowerUsed"/>, renewals included), of them renewals, and refusals (<see cref="PowerRefused"/>).</summary>
    public int Uses, Renewals, PowersRefused;
    /// <summary>Its refusals by reason, in the reasons' order.</summary>
    public readonly SortedDictionary<PowerDenial, int> RefusedBy = new();
    /// <summary>The ticks each power was live (by the power's id: <c>celerity</c>), in the ids' order.</summary>
    public readonly SortedDictionary<string, long> LiveTicks = new(StringComparer.Ordinal);
    /// <summary>The powers live now: the id, the rank and the tick it began (or was last renewed into).</summary>
    internal readonly SortedDictionary<string, (int Rank, long Since)> Live = new(StringComparer.Ordinal);
    /// <summary>Each of its swings' tempo (the <see cref="CombatTempo"/> swing rate after the tick it began), by its swing number.</summary>
    internal readonly Dictionary<int, float> SwingTempo = new();
    /// <summary>Each of its swings' strength, clip and aim (<see cref="AttackBegun"/>), by its swing number.</summary>
    internal readonly Dictionary<int, (AttackStrength Strength, int Clip, float Aim)> SwingBlow = new();

    /// <summary>
    /// The rules package's entitlement receipt (rights paid, used, and the actions really performed in the
    /// window): its swings begun while a power of its was live, and the ticks one was (any of them; counted tick by tick).
    /// </summary>
    public int SwingsLive;
    public long LiveAnyTicks;

    // Celerity's flurry and Health on the 1–1000 scale.

    /// <summary>
    /// The extra blows its swings' flurries resolved (<see cref="HarmResolved.Flurry"/> above 0, as the attacker) and their
    /// wounds. <see cref="Hits"/>, <see cref="DamageDice"/> and <see cref="DamageSuccesses"/> count each contact's own blow only;
    /// <see cref="WoundsDealt"/> and <see cref="WoundsTaken"/> every blow's. A flurry blow is an action the power bought: the
    /// entitlement receipt counts actions = swings + flurry blows.
    /// </summary>
    public int FlurryBlows;
    public long FlurryWounds;
    /// <summary>Its Health at the start and at the end (the module's <see cref="Vitals"/>, and each harm's before and after), and the Health its harm took (summed); null when it has none to show.</summary>
    public int? HealthStart, HealthEnd;
    public long HealthLost;

    // ---- active Dodge ----

    /// <summary>
    /// Its dodges begun (<see cref="Bodies.DodgeBegun"/>) and refused (<see cref="Bodies.DodgeRefused"/>); the foe's swings
    /// it dodged (a dodge of its begun within <see cref="BoutTally.DodgeAnswerSeconds"/> of the swing's start), and of them
    /// those that met no part of its body (a <see cref="ContactMade"/> of that swing on it, <see cref="ContactMet.Body"/>): as
    /// near as the events tell to "blows that met nothing because of a dodge" (a swing may have missed anyway).
    /// </summary>
    public int DodgesBegun, DodgesRefused, SwingsDodged, DodgedClean;

    // ---- the guard ----

    /// <summary>
    /// Its parry windows opened (<see cref="ParryBegun"/>); the rules' verdicts on blows met at its guard
    /// (<see cref="GuardResolved"/> as the defender): held, parried, overpowered; the guards its own blows overpowered; its
    /// blades' clashes and the clashes it dominated; its swings cancelled by the cancel key (feints) and by a dodge
    /// (<see cref="SwingCancelled"/>).
    /// </summary>
    public int ParriesTried, GuardHeld, GuardParried, GuardOverpowered, OverpoweredFoe, Clashes, ClashesWon, Feints, DodgeCancels;

    /// <summary>Its swings a second while none of its powers was live, over a bout of <paramref name="ticks"/> (null when none was not live).</summary>
    public double? SwingsUnpaidPerSecond(long ticks)
    {
        long unpaid = ticks - LiveAnyTicks;
        return unpaid <= 0 ? null : (Swings - SwingsLive) / (unpaid / (double)World.TicksPerSecond);
    }

    /// <summary>The seconds its powers were live: one number with at most one power, else <c>celerity:12.5;potence:3</c>.</summary>
    public string LiveSeconds()
    {
        if (LiveTicks.Count <= 1) return ArenaTables.Num((LiveTicks.Count == 0 ? 0 : LiveTicks.First().Value) / (double)World.TicksPerSecond);
        return string.Join(";", LiveTicks.Select(kv => $"{kv.Key}:{ArenaTables.Num(kv.Value / (double)World.TicksPerSecond)}"));
    }

    /// <summary>Its refusals' reasons, <c>NoBlood:2;Recast:1</c> (empty with none).</summary>
    public string RefusedReasons() => string.Join(";", RefusedBy.Select(kv => $"{kv.Key}:{kv.Value}"));

    /// <summary>The powers live now as <c>celerity:3;…</c> (empty with none).</summary>
    public string LiveNow() => string.Join(";", Live.Select(kv => $"{kv.Key}:{kv.Value.Rank}"));

    /// <summary>A power's id from its option's (<c>celerity.active</c> → <c>celerity</c>).</summary>
    public static string PowerOf(string option) =>
        option.StartsWith(CommonPrefix, StringComparison.Ordinal) ? option[CommonPrefix.Length..]       // common.blood_strength is blood_strength
        : option.IndexOf('.') is int dot and > 0 ? option[..dot] : option;

    /// <summary>The package's family of the common commands: its options are <c>common.&lt;power&gt;</c>, not <c>&lt;power&gt;.&lt;option&gt;</c>.</summary>
    const string CommonPrefix = "common.";

    /// <summary>Its uses of each power (by <see cref="PowerOf"/>, in the ids' order): uses, Blood, the effect's amount (<see cref="ArenaPowers"/>).</summary>
    public readonly SortedDictionary<string, PowerTally> ByPower = new(StringComparer.Ordinal);

    // ---- severance and the swing's chain ----

    /// <summary>The parts its blows cut off (<see cref="HarmResolved.Severed"/>, as the attacker), by region's name in the names' order.</summary>
    public int Severances;
    public readonly SortedDictionary<string, int> SeveredByRegion = new(StringComparer.Ordinal);
    /// <summary>Its blows that went on past a cut (<see cref="HarmResolved.Continues"/>), its contacts later in a chain (<see cref="ContactMade.Chain"/> above 0) and its swings with a chain longer than one.</summary>
    public int Continued, ChainContacts, ChainedSwings;
    /// <summary>The parts cut off it (by region's name) and the Health its effective maximum lost to them (<see cref="HarmResolved.StructuralLoss"/> summed: the union's increments).</summary>
    public readonly SortedDictionary<string, int> LostByRegion = new(StringComparer.Ordinal);
    public long StructuralLost;

    /// <summary>A region tally as <c>right forearm:1;neck:1</c> (empty with none).</summary>
    public static string Regions(SortedDictionary<string, int> by) => string.Join(";", by.Select(kv => $"{kv.Key}:{kv.Value}"));

    // ---- its blows' angle bands, its cuts by band and the cut share ----

    /// <summary>Its own blows on a body by the band they met it in (<see cref="HarmResolved.Angle"/>, as the attacker; the swing's own blow, not a flurry's), indexed by <see cref="AngleBand"/>.</summary>
    public readonly int[] BlowsByBand = new int[4];
    /// <summary>Its cuts (<see cref="HarmResolved.Severed"/>) by the band the cutting blow met in.</summary>
    public readonly int[] CutsByBand = new int[4];
    /// <summary>The cut shares of its own blows on a body, summed (<see cref="HarmResolved.CutShare"/>), for the mean.</summary>
    public double CutShareSum;
    // ---- end ----
    // ---- the blows it took on armour (HarmResolved.Armor set, as the target), the armour's dice there summed,
    // the wounds the armour kept off it, its blows taken on parts no piece covered, and the parts cut off it where armour
    // covered them and where none did.
    public int ArmorBlows, BareBlows, CutsCovered, CutsBare;
    public long ArmorDice, ArmorKept;
    // ---- its swings' extra blows (Celerity's flurry) by what each one's check found at its moment (FlurryStrike,
    // as the attacker): tried, landed (a HarmResolved for it: met, or a guard overpowered), met at a guard, out of reach, dodged,
    // cut off by its own stop (a cancel, a new swing, its stop, its weapon hand) and by its target's.
    public int FlurryTried, FlurryLanded, FlurryGuarded, FlurryOutOfReach, FlurryDodged, FlurryAttackerStopped, FlurryTargetDown;
    // ---- end
    // ---- blow quality, as the attacker — its swings' own blows on a body (not a flurry's): how many had a measured
    // quality with a reference, their Q and factor summed (for the means), those resisted by low work, and those its hilt made bashing.
    public int QualityBlows, LowWork, HiltBlows;
    public double QualitySum, QualityFactorSum;
    /// <summary>The quality work's follow-up: its duelling brain's range keeping (<see cref="DuelRange"/>): retreats out of a clinch, those after a blow met at its hilt, side-steps out of a pin, swings made anyway after holding.</summary>
    public int RangeRetreats, RangeHiltRetreats, RangeSideSteps, RangeForced;
    // ---- end
    // ---- its blades' stops — rebounds (off a guard or the level) and the tip's travel asked for, stops in a body,
    // the seconds from its swing's beginning to the stop (summed; mean = / StopsTimed) — its stuns (AttackerStunned: count and seconds)
    // and its presses refused by the stun (AttackRefused Stunned; the duelling brain waits a stun out, so a brain's are 0).
    public int Rebounds, BodyStops, StopsTimed, Stuns, StunRefused;
    public double ReboundMetres, StopSeconds, StunSeconds;
    public readonly Dictionary<int, long> SwingBegan = [];
    // ---- end
    // ---- the guard's arm solver (Animation/GuardSolver.cs) — the foe's blows its guard followed
    // (GuardSolverFollows, as the guard), of them those whose intercept its hand could not reach in time (late: the margin below
    // nothing, or no intercept in reach), its guard's gives (GuardGave) and the furthest one (m).
    public int GuardFollowed, GuardFollowedLate, GuardGives;
    public float GuardGiveMost;
    // ---- end
    // ---- its blows by direction (AttackBegun.Direction: left, right, overhead, thrust; none with attack-spin),
    // its body contacts by its swing's direction, its blows held ready (AttackReleased) and their seconds held, and the blows its
    // duelling brain struck away from its foe's guard.
    public readonly int[] SwingsByDirection = new int[5], BodyByDirection = new int[5];
    internal readonly Dictionary<int, AttackDirection> SwingDirection = new();
    public int Released, HeldBlows, AwayFromGuard;
    public float HeldSeconds;
    // ---- end
    // ---- the staggers it took (Staggered: all, those from its guard overpowered, its swings cut short before their
    // strike, the seconds), and its duelling brain's parries (DuelBrain: chosen, given up as its own swing would not be over in time, pressed).
    public int Staggered, StaggeredOverpowered, SwingsInterrupted, ParriesChosen, ParriesNotFree, ParriesPressed;
    public double StaggerSeconds;
    // ---- end
    // ---- staggers in a row — the run under way (its start and its last stagger's end, s), the longest, the staggers
    // that began within StaggerRunGapSeconds of the last one's end, the least gap.
    public const double StaggerRunGapSeconds = 1.0;
    public double StaggerRunStart = -1, StaggerRunEnd = -1, StaggerRunMost, StaggerGapLeast = double.MaxValue;
    public int StaggersInARow;

    /// <summary>A stagger begun at <paramref name="start"/> for <paramref name="seconds"/> (bout seconds): the run goes on or a new one begins.</summary>
    public void StaggerAt(double start, double seconds)
    {
        if (StaggerRunEnd >= 0)
        {
            double gap = start - StaggerRunEnd;
            StaggerGapLeast = Math.Min(StaggerGapLeast, gap);
            if (gap <= StaggerRunGapSeconds) StaggersInARow++;
            else StaggerRunStart = start;
        }
        else StaggerRunStart = start;
        StaggerRunEnd = start + seconds;
        StaggerRunMost = Math.Max(StaggerRunMost, StaggerRunEnd - StaggerRunStart);
    }
    // ---- end
    // ---- its duelling brain's pauses, their drawn seconds in all, and those cut short by an opening.
    public int Pauses, PausesCut;
    public double PausedSeconds;
    // ---- end
    // ---- the gaps from one of its blows begun to the next (s); the staggers its blows gave, and for each the next
    // blow of its own — its swing (−1 while none has begun since), the stagger's start and end — the seconds from the stagger's start
    // to that blow's first contact or Strike moment, summed, how many, and how many came while the foe was still staggered.
    public readonly List<double> SwingGaps = [];
    public double LastSwingAt = -1;
    public int StaggersGiven, NextBlows, NextInside;
    public double NextBlowSeconds;
    internal bool NextWaiting;
    internal int NextSwing = -1, StaggeringSwing = -1, LastSwing = -1;
    internal double NextFrom, NextEnds;

    /// <summary>A blow of its begun at <paramref name="at"/> (bout seconds) with swing number <paramref name="swing"/>.</summary>
    public void SwingAt(double at, int swing)
    {
        if (LastSwingAt >= 0) SwingGaps.Add(at - LastSwingAt);
        LastSwingAt = at;
        LastSwing = swing;
        if (NextWaiting && NextSwing < 0 && swing != StaggeringSwing) NextSwing = swing;
    }

    /// <summary>A stagger its blow (its last begun) gave at <paramref name="at"/> for <paramref name="seconds"/>: its next blow is waited for.</summary>
    public void StaggerGiven(double at, double seconds)
    {
        StaggersGiven++;
        (NextWaiting, NextSwing, StaggeringSwing, NextFrom, NextEnds) = (true, -1, LastSwing, at, at + seconds);
    }

    /// <summary>Its blow <paramref name="swing"/> met something or reached its Strike moment at <paramref name="at"/>: the next blow after a stagger, if it is.</summary>
    public void BlowArrived(double at, int swing)
    {
        if (!NextWaiting || NextSwing < 0 || swing != NextSwing) return;
        NextWaiting = false;
        NextBlows++;
        NextBlowSeconds += at - NextFrom;
        if (at < NextEnds) NextInside++;
    }

    /// <summary>The median of <paramref name="xs"/> (not empty).</summary>
    public static double Median(List<double> xs)
    {
        var s = xs.OrderBy(x => x).ToList();
        int n = s.Count;
        return n % 2 == 1 ? s[n / 2] : (s[n / 2 - 1] + s[n / 2]) / 2;
    }
    // ---- end

    // ---- one against several
    /// <summary>When this fighter went down (s: its stop, or its weapon hand cut off) in a bout with sides; null when it did not, or without sides.</summary>
    public double? DownSeconds { get; set; }
    /// <summary>How many times its duelling brain was given another foe by its side (<see cref="DuelSide.Switches"/>; 0 without sides).</summary>
    public int FoeSwitches { get; set; }
    // ---- end
    // ---- its duelling brain's shield reads — blows aimed over its foe's shield, under it, and reads that found no room.
    public int ShieldReadsOver, ShieldReadsUnder, ShieldReadsNoRoom, ShieldReadsUnthrust;
    // ---- end
}

/// <summary>One bout's result.</summary>
public sealed class BoutResult
{
    /// <summary>The swept point's index (<see cref="ArenaSpec.Points"/>) and its values.</summary>
    public required int Point { get; init; }
    public required int[] Values { get; init; }
    /// <summary>The bout's number at its point (0 up) and its seed (the seed base plus that number: every point sees the same seeds).</summary>
    public required int Bout { get; init; }
    public required ulong Seed { get; init; }
    public required FighterTally[] Fighters { get; init; }
    /// <summary>The winner's index (the killer of the first to die), or −1 when time ran out or nobody fighting killed.</summary>
    public int Winner { get; set; } = -1;
    /// <summary>The ticks run, and the time of the kill (s) when there was one.</summary>
    public long Ticks { get; set; }
    public double? KillSeconds { get; set; }
    /// <summary>What the stop that ended the bout was (<see cref="ActorDied.Stop"/>: a zero-Health stop), or null when time ran out.</summary>
    public ActorStop? Stop { get; set; }
    /// <summary>The bout ended because a fighter's weapon hand was cut off (it can no longer attack); its foe won, no kill.</summary>
    public bool Disarmed { get; set; }
    /// <summary>The world's digest at the end (the same spec and seed give the same).</summary>
    public ulong Digest { get; set; }
    /// <summary>The tape's path (relative to the output folder), when one was kept.</summary>
    public string? Tape { get; set; }
    /// <summary>The seconds the bout's world took to build, and to run.</summary>
    public double BuildSeconds { get; set; }
    public double RunSeconds { get; set; }
    // ---- one against several
    /// <summary>The side left standing (its index in <see cref="ArenaSpec.SideNames"/>), or −1 when time ran out, every side fell at once, or the bout had no sides.</summary>
    public int SideWon { get; set; } = -1;
    /// <summary>The fighters in the order they went down, each with its seconds (<c>Club1@12.5;Club2@20.25</c>); empty without sides or with none down.</summary>
    public string DownOrder { get; set; } = "";
    // ---- end
}

/// <summary>A contact and the harm it did, if any (a row of <c>contacts.csv</c>).</summary>
public sealed record ContactRow(int Point, int[] Values, int Bout, ulong Seed, int AttackerIndex, int TargetIndex, ContactMade Contact)
{
    public HarmResolved? Harm { get; set; }
    /// <summary>The attacker's powers live at the contact (<c>celerity:3;…</c>, empty for none or a stranger).</summary>
    public string AttackerLive { get; set; } = "";
    /// <summary>The attacker's swing tempo at the swing (<see cref="CombatTempo"/>'s swing rate; 1 without one), null for a stranger.</summary>
    public float? SwingTempo { get; set; }
    /// <summary>The extra blows Celerity's flurry resolved on this contact (<see cref="Harm"/> is its own blow), their wounds and the Health they took.</summary>
    public int FlurryBlows { get; set; }
    public long FlurryWounds { get; set; }
    public long FlurryHealth { get; set; }
    /// <summary>The rules' verdict on a contact with a guard (<see cref="GuardResolved"/>), null for none.</summary>
    public GuardResolved? Guard { get; set; }
    /// <summary>The logical strikes its swing's extra blows were — how many were tried, and each one's place,
    /// check, moment and the part its line met (<c>1:met@129:left forearm;2:guarded@134:chest</c>; this row's own blow is strike 0 of the contact).</summary>
    public int FlurryTried { get; set; }
    public string FlurryStrikes { get; set; } = "";
    // ---- the blow the contact's swing was (for the blow-quality measurement): quick or strong, its clip's name
    // (chrisAttackSword, chrisAttack2Sword, …) and the aim it bent to (degrees); empty for a stranger's.
    public string SwingKind { get; set; } = "";
    public string SwingClip { get; set; } = "";
    public float? SwingAim { get; set; }
    // ---- end
    // ---- the fighters' distance at the contact (feet to feet, m) and the attacker's reach (its duelling
    // brain's, m; null without one), for the brain's range keeping.
    public float? Distance { get; set; }
    public float? AttackerReach { get; set; }
    // ---- end
    // ---- the direction of the contact's swing (empty: none, attack-spin or a stranger's)
    public string SwingDirection { get; set; } = "";
}

/// <summary>
/// The arena's bouts: a fresh world per bout from its seed — a <see cref="Session"/> made with the bout's cast
/// (<see cref="SessionOptions.Arena"/>) on content loaded once (<see cref="SessionContent"/>) — stepped with no input
/// until a fighter dies (<see cref="ActorDied"/>) or the time runs out, every event of interest collected. The same spec and
/// seed give the same bout, digest for digest; a tape is the bout's ticks (all idle) and its digests, and replays with
/// <c>--replay-check</c> because the cast is in its head.
/// </summary>
public sealed class ArenaRun(ArenaSpec spec, SessionOptions baseline)
{
    SessionContent? content;

    /// <summary>The content loaded for the bouts (after the first), or null.</summary>
    public SessionContent? Content => content;

    /// <summary>The seconds spent loading the content (once).</summary>
    public double LoadSeconds { get; private set; }

    /// <summary>The options of a bout: the baseline's game and switches, the spec's level and time, no townsfolk, the cast and the seed.</summary>
    public SessionOptions OptionsFor(int point, int bout, Action<string>? say = null) => baseline with
    {
        // The spec's switches joined to the command line's (the Abilities' channels: skill-contest, skill-tempo-off).
        Switches = spec.Switches.Count == 0 ? baseline.Switches : new HashSet<string>(baseline.Switches.Concat(spec.Switches), StringComparer.Ordinal),
        CombatTempo = baseline.CombatTempo ?? spec.CombatTempo,   // the command line's --combat-tempo over the spec's combat_tempo
        Level = spec.Level,
        Start = "0",
        People = 0,
        Witnesses = [],
        Night = spec.Night,
        Place = null,
        TargetTest = null,
        NpcActions = null,
        Seed = spec.Seed + (ulong)bout,
        Arena = spec.Cast(spec.Points[point]).Write(),
        Content = content,
        Say = say ?? (_ => { }),
    };

    /// <summary>Loads the content the bouts are built on, once (quietly unless <paramref name="say"/> is given).</summary>
    public void Load(Action<string>? say = null, int point = 0, int bout = 0)
    {
        if (content is not null) return;
        var clock = Stopwatch.StartNew();
        content = Session.LoadContent(OptionsFor(point, bout, say));
        LoadSeconds = clock.Elapsed.TotalSeconds;
    }

    /// <summary>Runs one bout; its contacts are added to <paramref name="contacts"/>, its tape written to <paramref name="tapePath"/> when given.</summary>
    public BoutResult Bout(int point, int bout, List<ContactRow>? contacts = null, string? tapePath = null, Action<string>? say = null)
    {
        Load(say ?? baseline.Say, point, bout);
        var clock = Stopwatch.StartNew();
        var options = OptionsFor(point, bout, say);
        // A taped bout's event trace and harm trace beside its tape (the rules package's receipts).
        if (tapePath is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(tapePath))!);
            options = options with { EventTrace = Path.ChangeExtension(tapePath, ".events.jsonl"), CombatTrace = Path.ChangeExtension(tapePath, ".harm.jsonl") };
        }
        using var session = Session.Create(options);
        var w = session.World;
        var fighters = session.Fighters;
        var result = new BoutResult
        {
            Point = point,
            Values = spec.Points[point],
            Bout = bout,
            Seed = options.Seed,
            Fighters = [.. Enumerable.Range(0, fighters.Count).Select(_ => new FighterTally())],
            BuildSeconds = clock.Elapsed.TotalSeconds,
        };
        clock.Restart();
        var tape = tapePath is null ? null : new Tape { Header = session.TapeHeader(PlatformVersion.Text) };

        var tally = new BoutTally(w, fighters, result, contacts);
        long most = (long)MathF.Round(spec.Seconds * World.TicksPerSecond);
        while (!tally.Over && session.Tick < most)
        {
            session.Step(ActionFrame.Idle);
            tape?.Frames.Add(ActionFrame.Idle);
            if (tape is not null && session.Tick % Tape.DigestEvery == 0) tape.Digests[session.Tick] = session.Digest();
            tally.Collect();
        }
        tally.Finish(session.Tick);
        result.Ticks = session.Tick;
        result.Digest = session.Digest();
        if (tape is not null)
        {
            tape.Digests[session.Tick] = result.Digest;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(tapePath!))!);
            tape.Write(tapePath!);
        }
        result.RunSeconds = clock.Elapsed.TotalSeconds;
        return result;
    }
}

/// <summary>
/// A bout's events collected tick by tick, from any world that publishes them (an event type the world does not
/// register counts nothing): blows begun and their strikes, blocks raised and attacks refused, contacts by what they
/// met and the rows of <c>contacts.csv</c>, the harm each did (joined to its contact by its id), and the first fighter to die
/// — which ends the bout, the killer winning (or, of two, the other).
/// </summary>
public sealed class BoutTally
{
    readonly IReadOnlyList<EntityId> fighters;
    readonly BoutResult result;
    readonly List<ContactRow>? contacts;
    readonly EventQueue<AttackBegun>? begun;
    readonly EventQueue<StrikeMoment>? strikes;
    readonly EventQueue<BlockRaised>? raised;
    readonly EventQueue<AttackRefused>? refused;
    readonly EventQueue<ContactMade>? contactQueue;
    readonly EventQueue<HarmResolved>? harmQueue;
    readonly EventQueue<ActorDied>? deathQueue;
    readonly EventQueue<PowerUsed>? usedQueue;
    readonly EventQueue<PowerRefused>? powerRefusedQueue;
    readonly EventQueue<PowerEnded>? endedQueue;
    readonly Store<Vitals>? vitals;
    readonly Store<CombatTempo>? tempos;
    readonly Dictionary<long, ContactRow> open = [];
    readonly bool[] usedThisTick;
    // The dodges, each fighter's last swing (its number and tick), and the swings dodged: (attacker, swing,
    // dodger) to whether the swing met the dodger's body all the same.
    readonly EventQueue<Bodies.DodgeBegun>? dodgeQueue;
    readonly EventQueue<Bodies.DodgeRefused>? dodgeRefusedQueue;
    readonly (int Swing, long Tick)[] lastSwing;
    readonly SortedDictionary<(int Attacker, int Swing, int Dodger), bool> dodged = new();

    /// <summary>A dodge begun this long after a foe's swing began (s) answers that swing (a quick blow strikes about 0.4 to 0.5 s in).</summary>
    public const float DodgeAnswerSeconds = 0.8f;

    readonly World world;   // for the disarmed end (a weapon hand cut off)
    readonly Animation.CharacterAssets? clipNames;   // the swings' clips by name
    // The guard's events.
    readonly EventQueue<ParryBegun>? parryQueue;
    readonly EventQueue<GuardResolved>? guardQueue;
    readonly EventQueue<SwingCancelled>? cancelQueue;
    readonly EventQueue<FlurryStrike>? flurryQueue;
    readonly EventQueue<SwingFollowThrough>? followQueue;
    readonly EventQueue<AttackerStunned>? stunQueue;
    readonly EventQueue<Animation.GuardSolverFollows>? solverQueue;
    readonly EventQueue<Animation.GuardGave>? giveQueue;
    readonly EventQueue<AttackReleased>? releasedQueue;
    readonly EventQueue<AttackSwitched>? switchedQueue;
    readonly EventQueue<Staggered>? staggerQueue;
    readonly int[]? sideOf;   // each fighter's side, or null for a bout without sides

    public BoutTally(World world, IReadOnlyList<EntityId> fighters, BoutResult result, List<ContactRow>? contacts)
    {
        this.world = world;
        this.fighters = fighters;
        this.result = result;
        this.contacts = contacts;
        begun = Queue<AttackBegun>(world);
        strikes = Queue<StrikeMoment>(world);
        raised = Queue<BlockRaised>(world);
        refused = Queue<AttackRefused>(world);
        contactQueue = Queue<ContactMade>(world);
        harmQueue = Queue<HarmResolved>(world);
        deathQueue = Queue<ActorDied>(world);
        // The powers' events and views, and the tempo, when the world has them.
        usedQueue = Queue<PowerUsed>(world);
        powerRefusedQueue = Queue<PowerRefused>(world);
        endedQueue = Queue<PowerEnded>(world);
        usedThisTick = new bool[fighters.Count];
        dodgeQueue = Queue<Bodies.DodgeBegun>(world);
        dodgeRefusedQueue = Queue<Bodies.DodgeRefused>(world);
        lastSwing = new (int, long)[fighters.Count];
        Array.Fill(lastSwing, (-1, long.MinValue / 2));
        vitals = world.TryStore<Vitals>(out var vs) ? vs : null;
        tempos = world.TryStore<CombatTempo>(out var ts) ? ts : null;
        parryQueue = Queue<ParryBegun>(world);
        guardQueue = Queue<GuardResolved>(world);
        cancelQueue = Queue<SwingCancelled>(world);
        flurryQueue = Queue<FlurryStrike>(world);
        followQueue = Queue<SwingFollowThrough>(world);
        stunQueue = Queue<AttackerStunned>(world);
        releasedQueue = Queue<AttackReleased>(world);
        switchedQueue = Queue<AttackSwitched>(world);
        solverQueue = Queue<Animation.GuardSolverFollows>(world);
        giveQueue = Queue<Animation.GuardGave>(world);
        staggerQueue = Queue<Staggered>(world);
        clipNames = world.TryService<Animation.CharacterAssets>(out var ca) ? ca : null;
        if (world.TryStore<DuelSide>(out var sides) && sides.Count > 0)
        {
            sideOf = new int[fighters.Count];
            for (int i = 0; i < fighters.Count; i++) sideOf[i] = sides.Has(fighters[i]) ? sides.Get(fighters[i]).Side : -1 - i;
        }
        ReadVitals();
    }

    /// <summary>Each fighter's Blood as the module shows it (<see cref="Vitals"/>): the first seen is its start (unless a use came first), the last its end; its Health the same (a harm after the view says the Health after it).</summary>
    void ReadVitals()
    {
        if (vitals is null) return;
        for (int i = 0; i < fighters.Count; i++)
        {
            ref var v = ref vitals.Find(fighters[i]);
            if (System.Runtime.CompilerServices.Unsafe.IsNullRef(ref v)) continue;
            var f = result.Fighters[i];
            if (v.HealthMax > 0)
            {
                int health = (int)MathF.Round(v.Health);
                f.HealthStart ??= health;
                f.HealthEnd = health;
            }
            if (!v.HasBlood) continue;
            int blood = (int)MathF.Round(v.Blood);
            f.BloodStart ??= blood;
            if (!usedThisTick[i]) f.BloodEnd = blood;     // a use this tick says the Blood after it, whenever the view is written
        }
    }

    /// <summary>Ends the bout's tally at its last tick: the powers still live are counted to it.</summary>
    public void Finish(long tick)
    {
        // The swings dodged that met no part of the dodger's body.
        foreach (var ((_, _, dodger), met) in dodged) if (!met) result.Fighters[dodger].DodgedClean++;
        if (sideOf is not null && world.TryStore<DuelSide>(out var sides))   // the times each brain was given another foe
            for (int i = 0; i < fighters.Count; i++) if (sides.Has(fighters[i])) result.Fighters[i].FoeSwitches = sides.Get(fighters[i]).Switches;
        foreach (var f in result.Fighters)
        {
            foreach (var (power, live) in f.Live) Add(f, power, tick - live.Since);
            f.Live.Clear();
        }
    }

    static void Add(FighterTally f, string power, long ticks) => f.LiveTicks[power] = (f.LiveTicks.TryGetValue(power, out long had) ? had : 0) + Math.Max(0, ticks);

    static EventQueue<T>? Queue<T>(World world) where T : struct => world.TryService<EventQueue<T>>(out var q) ? q : null;

    /// <summary>Whether a fighter has died (the bout is over).</summary>
    public bool Over { get; private set; }

    // ---- the fighters' distance at a contact and the attacker's reach (the duelling brain's) ----
    void RangeOf(ContactRow row, in ContactMade c)
    {
        if (!world.TryStore<Bodies.Body>(out var bodies) || !bodies.Has(c.Attacker) || c.Target == EntityId.None || !bodies.Has(c.Target)) return;
        var a = bodies.Get(c.Attacker).Feet;
        var t = bodies.Get(c.Target).Feet;
        row.Distance = MathF.Sqrt((a.X - t.X) * (a.X - t.X) + (a.Y - t.Y) * (a.Y - t.Y));
        if (world.TryStore<DuelBrain>(out var brains) && brains.Has(c.Attacker)) row.AttackerReach = brains.Get(c.Attacker).Reach;
    }
    // ---- end

    int Of(EntityId e)
    {
        for (int i = 0; i < fighters.Count; i++) if (fighters[i] == e) return i;
        return -1;
    }

    // ---- one against several — a fighter down, and whether one side is left
    ActorStop? sideStop;
    int sideKiller = -1;
    long sideTick;

    /// <summary>A fighter down (its stop, or disarmed) at <paramref name="tick"/>: its time and its place in the order.</summary>
    void Down(int who, long tick)
    {
        var f = result.Fighters[who];
        if (f.DownSeconds is not null) return;
        f.DownSeconds = tick / (double)World.TicksPerSecond;
        result.DownOrder += (result.DownOrder.Length > 0 ? ";" : "") + string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{who}@{f.DownSeconds.Value:0.###}");
    }

    /// <summary>
    /// After this tick's stops: when one side alone has fighters up, the bout is over — its winner the killer of the last one down when
    /// that killer is on the side left and up, else that side's first fighter up; the time of the last stop is the bout's kill (a
    /// disarming instead ends it with no kill, as of two). When no side is left (the last two down together) nobody wins.
    /// </summary>
    void SidesLeft(long tick)
    {
        int left = -1, sidesUp = 0;
        for (int i = 0; i < fighters.Count; i++)
        {
            if (result.Fighters[i].Stopped) continue;
            if (sidesUp == 0 || sideOf![i] != left)
            {
                bool seen = false;
                for (int j = 0; j < i; j++) if (!result.Fighters[j].Stopped && sideOf![j] == sideOf[i]) { seen = true; break; }
                if (!seen) { sidesUp++; left = sideOf![i]; }
            }
        }
        if (sidesUp > 1) { sideStop = null; sideKiller = -1; return; }
        Over = true;
        if (sideStop is { } stop) { result.KillSeconds = sideTick / (double)World.TicksPerSecond; result.Stop = stop; }
        else result.Disarmed = true;
        if (sidesUp == 1)
        {
            result.SideWon = left;
            result.Winner = sideKiller >= 0 && !result.Fighters[sideKiller].Stopped && sideOf![sideKiller] == left ? sideKiller : -1;
            for (int i = 0; result.Winner < 0 && i < fighters.Count; i++) if (!result.Fighters[i].Stopped && sideOf![i] == left) result.Winner = i;
        }
    }
    // ---- end

    /// <summary>Collects the tick just stepped (its events, read after the step); true once the bout is over.</summary>
    public bool Collect()
    {
        // The powers, before this tick's contacts (a contact sees the powers live at it).
        Array.Clear(usedThisTick);
        if (usedQueue is not null)
            foreach (var u in usedQueue.ThisTick)
            {
                if (Of(u.Entity) is not (var i and >= 0)) continue;
                var f = result.Fighters[i];
                usedThisTick[i] = true;
                f.Uses++;
                if (u.Renewed) f.Renewals++;
                f.BloodSpent += u.Cost;
                f.BloodStart ??= u.BloodBefore;
                f.BloodEnd = u.BloodAfter;
                string power = FighterTally.PowerOf(u.Power);
                ArenaPowers.Count(f, u);                                                          // by command
                // A renewal goes on from the old record (its PowerEnded(Renewed) closed nothing); the rank is the new one's.
                // A use with no duration (a heal: PowerUsed.Seconds 0, the contract's "no duration") opens no live window.
                if (u.Seconds > 0f) f.Live[power] = f.Live.TryGetValue(power, out var was) ? (u.Rank, was.Since) : (u.Rank, u.Tick);
            }
        if (powerRefusedQueue is not null)
            foreach (var r in powerRefusedQueue.ThisTick)
            {
                if (Of(r.Entity) is not (var i and >= 0)) continue;
                var f = result.Fighters[i];
                f.PowersRefused++;
                f.RefusedBy[r.Reason] = (f.RefusedBy.TryGetValue(r.Reason, out int n) ? n : 0) + 1;
            }
        if (endedQueue is not null)
            foreach (var x in endedQueue.ThisTick)
            {
                if (x.Cause == PowerEndCause.Renewed || Of(x.Entity) is not (var i and >= 0)) continue;
                var f = result.Fighters[i];
                string power = FighterTally.PowerOf(x.Power);
                if (!f.Live.Remove(power, out var live)) continue;
                Add(f, power, x.Tick - live.Since);
            }
        ReadVitals();
        // A tick with a power live counts to its window (the receipt); a swing begun in it is a swing while live.
        foreach (var f in result.Fighters) if (f.Live.Count > 0) f.LiveAnyTicks++;
        if (begun is not null)
            foreach (var b in begun.ThisTick)
                if (Of(b.Entity) is var i and >= 0)
                {
                    var f = result.Fighters[i];
                    f.Swings++;
                    if (f.Live.Count > 0) f.SwingsLive++;
                    f.SwingTempo[b.Swing] = CombatTempos.Of(tempos, b.Entity).SwingRate;
                    f.SwingBlow[b.Swing] = (b.Strength, b.Clip, b.AimPitch);
                    lastSwing[i] = (b.Swing, b.Tick);
                }
        // Dodges begun and refused; a dodge answers each other fighter's swing begun within the window.
        if (dodgeQueue is not null)
            foreach (var d in dodgeQueue.ThisTick)
                if (Of(d.Entity) is var i and >= 0)
                {
                    result.Fighters[i].DodgesBegun++;
                    for (int j = 0; j < fighters.Count; j++)
                        if (j != i && lastSwing[j].Swing >= 0 && d.Tick - lastSwing[j].Tick <= (long)(DodgeAnswerSeconds * World.TicksPerSecond)
                            && dodged.TryAdd((j, lastSwing[j].Swing, i), false))
                            result.Fighters[i].SwingsDodged++;
                }
        if (dodgeRefusedQueue is not null)
            foreach (var d in dodgeRefusedQueue.ThisTick) if (Of(d.Entity) is var i and >= 0) result.Fighters[i].DodgesRefused++;
        if (strikes is not null) foreach (var s in strikes.ThisTick) if (Of(s.Entity) is var i and >= 0) result.Fighters[i].Strikes++;
        if (raised is not null) foreach (var r in raised.ThisTick) if (Of(r.Entity) is var i and >= 0) result.Fighters[i].BlocksRaised++;
        if (refused is not null) foreach (var r in refused.ThisTick) if (Of(r.Entity) is var i and >= 0) result.Fighters[i].Refused++;
        if (contactQueue is not null)
            foreach (var c in contactQueue.ThisTick)
            {
                int a = Of(c.Attacker), t = Of(c.Target);
                if (a >= 0 && t >= 0 && c.Met == ContactMet.Body && dodged.ContainsKey((a, c.Swing, t))) dodged[(a, c.Swing, t)] = true;   // met all the same
                if (a >= 0)
                {
                    var f = result.Fighters[a];
                    switch (c.Met)
                    {
                        case ContactMet.Body: f.ContactsBody++; break;
                        case ContactMet.Shield: f.ContactsShield++; break;
                        case ContactMet.Weapon: f.ContactsWeapon++; break;
                        default: f.ContactsLevel++; break;
                    }
                }
                if (a >= 0 && c.Chain > 0)                                                            // the chain
                {
                    var f = result.Fighters[a];
                    f.ChainContacts++;
                    if (c.Chain == 1) f.ChainedSwings++;
                }
                var row = new ContactRow(result.Point, result.Values, result.Bout, result.Seed, a, t, c);
                if (a >= 0)
                {
                    var f = result.Fighters[a];
                    row.AttackerLive = f.LiveNow();
                    row.SwingTempo = f.SwingTempo.TryGetValue(c.Swing, out float tempo) ? tempo : CombatTempos.Of(tempos, c.Attacker).SwingRate;
                    if (f.SwingBlow.TryGetValue(c.Swing, out var blow))                                     // the blow
                    {
                        row.SwingKind = blow.Strength == AttackStrength.Strong ? "strong" : "quick";
                        row.SwingClip = clipNames is { } assets && blow.Clip >= 0 ? assets.Clip(blow.Clip).Name : blow.Clip.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        row.SwingAim = blow.Aim * DetMath.RadToDeg;
                    }
                    if (f.SwingDirection.TryGetValue(c.Swing, out var sd)) row.SwingDirection = DirectionalAttacks.Name(sd);
                }
                open[c.ContactId] = row;
                RangeOf(row, in c);   // the distance and the reach
                contacts?.Add(row);
            }
        // ---- parries tried, the guard's verdicts, cancels.
        if (parryQueue is not null) foreach (var p in parryQueue.ThisTick) if (Of(p.Entity) is var i and >= 0) result.Fighters[i].ParriesTried++;
        if (cancelQueue is not null)
            foreach (var x in cancelQueue.ThisTick)
                if (Of(x.Entity) is var i and >= 0)
                {
                    if (x.Cause == SwingCancelCause.Cancel) result.Fighters[i].Feints++;
                    else result.Fighters[i].DodgeCancels++;
                }
        if (guardQueue is not null)
            foreach (var v in guardQueue.ThisTick)
            {
                if (open.TryGetValue(v.ContactId, out var gr)) gr.Guard = v;
                int a = Of(v.Attacker), dfd = Of(v.Defender);
                switch (v.Verdict)
                {
                    case GuardVerdict.Held: if (dfd >= 0) result.Fighters[dfd].GuardHeld++; break;
                    case GuardVerdict.Parried: if (dfd >= 0) result.Fighters[dfd].GuardParried++; break;
                    case GuardVerdict.Overpowered:
                        if (dfd >= 0) result.Fighters[dfd].GuardOverpowered++;
                        if (a >= 0) result.Fighters[a].OverpoweredFoe++;
                        break;
                    default:
                        if (a >= 0) { result.Fighters[a].Clashes++; if (v.Verdict == GuardVerdict.ClashDominates) result.Fighters[a].ClashesWon++; }
                        break;
                }
            }
        if (harmQueue is not null)
            foreach (var h in harmQueue.ThisTick)
            {
                int a = Of(h.Attacker), t = Of(h.Target);
                if (a >= 0)
                {
                    var f = result.Fighters[a];
                    if (h.Flurry == 0)
                    {
                        f.Hits++;
                        f.DamageDice += h.DamageDice;
                        f.DamageSuccesses += h.DamageSuccesses;
                        if (h.Contested) { f.ContestedHits++; f.ContestChange += h.ContestChange; }
                    }
                    else { f.FlurryBlows++; f.FlurryWounds += h.Wounds; }                     // a blow the swing carried
                    f.WoundsDealt += h.Wounds;
                    if (h.Flurry == 0 && h.Continues) f.Continued++;
                    if (h.Flurry == 0)                                                       // the band and the cut share
                    {
                        f.BlowsByBand[(int)h.Angle & 3]++;
                        f.CutShareSum += h.CutShare;
                        if (h.Severed != BodyRegion.None) f.CutsByBand[(int)h.Angle & 3]++;
                    }
                    if (h.Severed != BodyRegion.None)
                    {
                        f.Severances++;
                        string region = BodyRegions.Name(h.Severed);
                        f.SeveredByRegion[region] = (f.SeveredByRegion.TryGetValue(region, out int n) ? n : 0) + 1;
                    }
                }
                if (t >= 0)
                {
                    var g = result.Fighters[t];
                    g.WoundsTaken += h.Wounds;
                    g.HealthLeft = h.HealthAfter;
                    g.HealthStart ??= h.HealthBefore;
                    g.HealthEnd = h.HealthAfter;
                    g.HealthLost += h.HealthBefore - h.HealthAfter;
                    if (h.Severed != BodyRegion.None)
                    {
                        string region = BodyRegions.Name(h.Severed);
                        g.LostByRegion[region] = (g.LostByRegion.TryGetValue(region, out int n) ? n : 0) + 1;
                        g.StructuralLost += h.StructuralLoss;
                    }
                    if (h.Armor is not null) { g.ArmorBlows++; g.ArmorDice += h.ArmorDice; g.ArmorKept += h.ArmorKept; }
                    else g.BareBlows++;
                    if (h.Severed != BodyRegion.None) { if (h.Armor is not null) g.CutsCovered++; else g.CutsBare++; }
                }
                if (open.TryGetValue(h.ContactId, out var row))
                {
                    if (h.Flurry == 0) row.Harm = h;                                          // the contact's own blow
                    else { row.FlurryBlows++; row.FlurryWounds += h.Wounds; row.FlurryHealth += h.HealthBefore - h.HealthAfter; }
                }
            }
        if (deathQueue is not null)
            foreach (var d in deathQueue.ThisTick)
            {
                int who = Of(d.Who);
                if (who < 0) continue;
                result.Fighters[who].Stopped = true;                                   // a harness stop, not a death
                result.Fighters[who].Stop = ArenaTables.StopName(d.Stop);
                if (Over) continue;
                if (sideOf is not null) { Down(who, d.Tick); sideStop = d.Stop; sideKiller = Of(d.Killer); sideTick = d.Tick; continue; }
                Over = true;
                result.KillSeconds = d.Tick / (double)World.TicksPerSecond;
                result.Stop = d.Stop;
                int killer = Of(d.Killer);
                result.Winner = killer >= 0 && killer != who ? killer : fighters.Count == 2 ? 1 - who : -1;
            }
        if (sideOf is not null && !Over) SidesLeft(world.Tick);   // the bout ends when one side is left
        // ---- each extra blow tried, by what its check found (a compressed blow is tried and resolved at once),
        // and landed (its HarmResolved, Flurry above 0).
        if (harmQueue is not null)
            foreach (var h in harmQueue.ThisTick)
                if (h.Flurry > 0 && Of(h.Attacker) is var i and >= 0) result.Fighters[i].FlurryLanded++;
        if (flurryQueue is not null)
            foreach (var x in flurryQueue.ThisTick)
            {
                if (Of(x.Attacker) is var i and >= 0)
                {
                    var f = result.Fighters[i];
                    f.FlurryTried++;
                    switch (x.Check)
                    {
                        case FlurryCheck.Guarded: f.FlurryGuarded++; break;
                        case FlurryCheck.OutOfReach: f.FlurryOutOfReach++; break;
                        case FlurryCheck.Dodged: f.FlurryDodged++; break;
                        case FlurryCheck.AttackerStopped: f.FlurryAttackerStopped++; break;
                        case FlurryCheck.TargetDown: f.FlurryTargetDown++; break;
                    }
                }
                if (open.TryGetValue(x.ContactId, out var fr))
                {
                    fr.FlurryTried++;
                    fr.FlurryStrikes += (fr.FlurryStrikes.Length > 0 ? ";" : "") + string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{x.Index}:{FlurryBlows.Name(x.Check)}@{x.Tick}")
                        + (x.Region != BodyRegion.None ? ":" + BodyRegions.Name(x.Region) : "");   // the part the line met (behind a guard for one guarded)
                }
            }
        // ---- end
        // ---- blow quality, the attacker's own blows on a body
        if (harmQueue is not null)
            foreach (var h in harmQueue.ThisTick)
                if (h.Flurry == 0 && Of(h.Attacker) is var i and >= 0)
                {
                    var f = result.Fighters[i];
                    if (h.QualityRef > 0f) { f.QualityBlows++; f.QualitySum += h.Quality; f.QualityFactorSum += h.QualityFactor; }
                    if (h.Wounds == 0 && h.ResistedBy == ResistedBy.LowWork) f.LowWork++;
                    if (h.Hilt) f.HiltBlows++;
                }
        if (world.TryStore<DuelRange>(out var rangeStore) && rangeStore.Count > 0)   // the follow-up: the brain's range keeping, as it stands
            for (int i = 0; i < fighters.Count; i++)
                if (rangeStore.Has(fighters[i]))
                {
                    var rg = rangeStore.Get(fighters[i]);
                    var f = result.Fighters[i];
                    (f.RangeRetreats, f.RangeHiltRetreats, f.RangeSideSteps, f.RangeForced) = (rg.Retreats, rg.HiltRetreats, rg.SideSteps, rg.Forced);
                }
        // ---- end
        // ---- each blade's stop (a rebound's travel asked for, the time from its swing's beginning), the stuns,
        // and the presses the stun refused.
        if (begun is not null)
            foreach (var b in begun.ThisTick)
                if (Of(b.Entity) is var i and >= 0) result.Fighters[i].SwingBegan[b.Swing] = b.Tick;
        if (followQueue is not null)
            foreach (var x in followQueue.ThisTick)
            {
                if (Of(x.Attacker) is not (var i and >= 0) || x.Outcome == BladeOutcome.GoesOn) continue;
                var f = result.Fighters[i];
                if (x.Outcome == BladeOutcome.Rebounds) { f.Rebounds++; f.ReboundMetres += x.ReboundMetres; }
                else f.BodyStops++;
                if (f.SwingBegan.TryGetValue(x.Swing, out long since)) { f.StopsTimed++; f.StopSeconds += (x.Tick - since) * World.TickSeconds; }
            }
        if (stunQueue is not null)
            foreach (var x in stunQueue.ThisTick)
                if (Of(x.Attacker) is var i and >= 0) { result.Fighters[i].Stuns++; result.Fighters[i].StunSeconds += x.Seconds; }
        if (refused is not null)
            foreach (var x in refused.ThisTick)
                if (x.Why == AttackRefusal.Stunned && Of(x.Entity) is var i and >= 0) result.Fighters[i].StunRefused++;
        // ---- end
        // ---- the arm solver's follows and the gives.
        if (solverQueue is not null)
            foreach (var x in solverQueue.ThisTick)
                if (Of(x.Who) is var i and >= 0)
                {
                    result.Fighters[i].GuardFollowed++;
                    if (!(x.Margin >= 0f)) result.Fighters[i].GuardFollowedLate++;
                }
        if (giveQueue is not null)
            foreach (var x in giveQueue.ThisTick)
                if (Of(x.Who) is var i and >= 0)
                {
                    result.Fighters[i].GuardGives++;
                    result.Fighters[i].GuardGiveMost = MathF.Max(result.Fighters[i].GuardGiveMost, x.Metres);
                }
        // ---- end
        // ---- blows by direction, body contacts by their swing's direction, blows held ready, away from the guard.
        if (begun is not null)
            foreach (var b in begun.ThisTick)
                if (Of(b.Entity) is var i and >= 0)
                {
                    result.Fighters[i].SwingsByDirection[(int)b.Direction]++;
                    result.Fighters[i].SwingDirection[b.Swing] = b.Direction;
                }
        if (switchedQueue is not null)   // a held blow switched counts as its new direction's
            foreach (var x in switchedQueue.ThisTick)
                if (Of(x.Entity) is var i and >= 0)
                {
                    result.Fighters[i].SwingsByDirection[(int)x.From]--;
                    result.Fighters[i].SwingsByDirection[(int)x.To]++;
                    result.Fighters[i].SwingDirection[x.Swing] = x.To;
                }
        if (contactQueue is not null)
            foreach (var c in contactQueue.ThisTick)
                if (c.Met == ContactMet.Body && Of(c.Attacker) is var i and >= 0 && result.Fighters[i].SwingDirection.TryGetValue(c.Swing, out var d))
                    result.Fighters[i].BodyByDirection[(int)d]++;
        if (releasedQueue is not null)
            foreach (var x in releasedQueue.ThisTick)
                if (Of(x.Entity) is var i and >= 0)
                {
                    result.Fighters[i].Released++;
                    if (x.HeldSeconds > 0f) { result.Fighters[i].HeldBlows++; result.Fighters[i].HeldSeconds += x.HeldSeconds; }
                }
        if (world.TryStore<DuelBrain>(out var brainsNow))
            for (int i = 0; i < fighters.Count; i++)
                if (brainsNow.Has(fighters[i])) result.Fighters[i].AwayFromGuard = brainsNow.Get(fighters[i]).AwayFromGuard;
        // ---- end
        // ---- the staggers, and the brain's parries as they stand.
        if (staggerQueue is not null)
            foreach (var x in staggerQueue.ThisTick)
                if (Of(x.Who) is var i and >= 0)
                {
                    var f = result.Fighters[i];
                    f.Staggered++;
                    f.StaggerSeconds += x.Seconds;
                    if (x.Cause == StaggerCause.Overpowered) f.StaggeredOverpowered++;
                    if (x.Interrupted) f.SwingsInterrupted++;
                    f.StaggerAt(x.Tick * World.TickSeconds, x.Seconds);
                }
        // ---- blow to blow, and the next blow after a stagger given (its swings, contacts and strikes, then the staggers).
        if (begun is not null)
            foreach (var b in begun.ThisTick) if (Of(b.Entity) is var i and >= 0) result.Fighters[i].SwingAt(b.Tick * World.TickSeconds, b.Swing);
        if (strikes is not null)
            foreach (var s in strikes.ThisTick) if (Of(s.Entity) is var i and >= 0) result.Fighters[i].BlowArrived(s.Tick * World.TickSeconds, s.Swing);
        if (contactQueue is not null)
            foreach (var c in contactQueue.ThisTick) if (Of(c.Attacker) is var i and >= 0) result.Fighters[i].BlowArrived(c.Tick * World.TickSeconds, c.Swing);
        if (staggerQueue is not null)
            foreach (var x in staggerQueue.ThisTick) if (Of(x.By) is var i and >= 0) result.Fighters[i].StaggerGiven(x.Tick * World.TickSeconds, x.Seconds);
        // ---- end
        if (world.TryStore<DuelBrain>(out var brainStore) && brainStore.Count > 0)
            for (int i = 0; i < fighters.Count; i++)
                if (brainStore.Has(fighters[i]))
                {
                    var b = brainStore.Get(fighters[i]);
                    (result.Fighters[i].ParriesChosen, result.Fighters[i].ParriesNotFree, result.Fighters[i].ParriesPressed) = (b.ParriesChosen, b.ParriesNotFree, b.Parries);
                    (result.Fighters[i].Pauses, result.Fighters[i].PausesCut, result.Fighters[i].PausedSeconds) = (b.Pauses, b.PausesCut, b.PausedSeconds);
                    (result.Fighters[i].ShieldReadsOver, result.Fighters[i].ShieldReadsUnder, result.Fighters[i].ShieldReadsNoRoom, result.Fighters[i].ShieldReadsUnthrust) = (b.ShieldReadsOver, b.ShieldReadsUnder, b.ShieldReadsNoRoom, b.ShieldReadsUnthrust);
                }
        // ---- end
        // A fighter whose weapon hand is cut off can no longer attack (the attacks refuse it), only stall
        // behind a shield the blade cannot break yet (breaking one is later) — 22 of 200 shielded bouts ran out the clock so.
        // Its foe has won: the bout ends there, DISARMED (no kill: the time to the stop counts only stops). ENGINE_SANDBOX.
        if (!Over && sideOf is not null)   // with sides, a fighter disarmed is out of the fight, and its side may be done
        {
            bool any = false;
            for (int i = 0; i < fighters.Count; i++)
                if (!result.Fighters[i].Stopped && world.Alive(fighters[i]) && Animation.HeldItems.WeaponHandGone(world, fighters[i]))
                {
                    result.Fighters[i].Stopped = true;
                    result.Fighters[i].Stop = ArenaTables.DisarmedName;
                    Down(i, world.Tick);
                    any = true;
                }
            if (any) SidesLeft(world.Tick);
        }
        if (!Over && fighters.Count == 2 && sideOf is null)
            for (int i = 0; i < 2; i++)
                if (world.Alive(fighters[i]) && Animation.HeldItems.WeaponHandGone(world, fighters[i]))
                {
                    Over = true;
                    result.Disarmed = true;
                    result.Winner = 1 - i;
                    result.Fighters[i].Stopped = true;                                 // the fighter disarmed
                    result.Fighters[i].Stop = ArenaTables.DisarmedName;
                    break;
                }
        return Over;
    }
}
