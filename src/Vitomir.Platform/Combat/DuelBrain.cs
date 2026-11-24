// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Runtime.CompilerServices;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Combat;

/// <summary>What drives a fighter in the arena (ours): the duelling brain, or a dummy that stands or blocks.</summary>
public enum BrainKind : byte
{
    /// <summary>The duelling brain: faces its foe, closes, attacks, blocks and backs off (<see cref="DuelBrainSystem"/>).</summary>
    Duel = 0,
    /// <summary>A dummy standing where it was put, facing where it was put, doing nothing.</summary>
    Stand = 1,
    /// <summary>A dummy standing where it was put with the block held always.</summary>
    Block = 2,
}

/// <summary>
/// The duelling brain's numbers (ours, <b>SANDBOX</b>: a testbed's opponent, not the game's AI). Data: the arena's
/// spec gives each fighter's, over these defaults. Shares are chances in 0..1; the aim's three shares are taken in
/// proportion (they need not add to one).
/// </summary>
public sealed record DuelBrainSettings
{
    /// <summary>The share of blows made strong (attack held past the strong threshold), the rest quick.</summary>
    public float StrongShare { get; init; } = 0.3f;
    /// <summary>The shares of blows aimed high, level and low.</summary>
    public float HighShare { get; init; } = 0.2f;
    public float LevelShare { get; init; } = 0.6f;
    public float LowShare { get; init; } = 0.2f;
    /// <summary>The pitch of a high blow and of a low one, degrees (up positive; the aim offset clamps to 25° up, 40° down).</summary>
    public float HighDegrees { get; init; } = 15f;
    public float LowDegrees { get; init; } = -20f;
    /// <summary>The chance of raising the block when the foe begins a blow within its reach.</summary>
    public float BlockChance { get; init; } = 0.5f;
    /// <summary>How long after the foe's blow begins the block is asked for (a reaction), s.</summary>
    public float ReactSeconds { get; init; } = 0.15f;
    /// <summary>How long the block is held once raised, s.</summary>
    public float BlockSeconds { get; init; } = 0.9f;
    /// <summary>The chance of backing off a step after one's own blow ends.</summary>
    public float BackOffChance { get; init; } = 0.3f;
    /// <summary>How long a back-off walks away from the foe, s (a step or two at a walk).</summary>
    public float BackOffSeconds { get; init; } = 0.5f;
    /// <summary>A blow is begun when the foe is within this share of the reach, centre to centre.</summary>
    public float StrikeShare { get; init; } = 0.85f;
    /// <summary>The brain closes in to this share of the reach and stops there.</summary>
    public float CloseShare { get; init; } = 0.75f;
    /// <summary>
    /// Nearer than this share of the reach the brain steps back to it at a walk (a strong blow's own step, 1.1's whirl, carries
    /// a fighter forwards into a clinch otherwise).
    /// </summary>
    public float TooCloseShare { get; init; } = 0.6f;
    /// <summary>Closing runs beyond this distance (m) from the foe, walks within it.</summary>
    public float WalkWithin { get; init; } = 2.5f;

    // The quick slots (Powers/PowerContract.cs). The brain reads only the slots' views (PowerSlots) and writes only the
    // slot pressed (PowerIntent); how a power works is the module's.

    /// <summary>
    /// The quick slots the brain may use (the arena's fighter's <c>slots</c>): bit <c>s − 1</c> for slot <c>s</c>
    /// (1 to <see cref="Powers.QuickSlots.Count"/>). Every slot by default; a slot that shows no power is passed over.
    /// </summary>
    public int PowerSlots { get; init; } = (1 << Powers.QuickSlots.Count) - 1;

    /// <summary>
    /// A power is used while the foe is within this share of the reach, centre to centre (1.5: 2.5 m with the
    /// broadsword, about <see cref="WalkWithin"/> — the brain is engaged, closing to strike).
    /// </summary>
    public float PowerShare { get; init; } = 1.5f;

    /// <summary>A live power is renewed (pressed again, paid again: 1.1's cast) when its time left falls under this (s), while still engaged.</summary>
    public float RenewSeconds { get; init; } = 1f;

    /// <summary>After a press the brain presses no slot for this long (s), so a view a tick old is not pressed twice.</summary>
    public float PowerWaitSeconds { get; init; } = 0.5f;

    /// <summary>
    /// A slot for use when hurt (<see cref="Powers.SlotUse.Hurt"/>: Blood Healing) is pressed while the brain's Health
    /// (<see cref="Powers.Vitals"/>) is below this share of its maximum, Ready and not within <see cref="PowerWaitSeconds"/> of
    /// its last press, engaged or not (0.3, 1.1's AI's: it heals below its <c>lowhealth</c>, 0.30 in every
    /// <c>brujah*.nai</c>, its discipline ladder's; 0.5 before). 0: never.
    /// </summary>
    public float HealBelow { get; init; } = 0.3f;

    /// <summary>
    /// While a power of its own is live, the brain holds its blocks and walks its back-offs for the usual times divided by its
    /// body's tempo (SANDBOX: the testbed's policy, so a Celerity body is played at its speed). False: real time always.
    /// </summary>
    public bool PaceWithPowers { get; init; } = true;

    // Active Dodge (Bodies/Dodge.cs). The brain asks for a dodge as the player's key does (DodgeIntent), and the
    // platform's rules begin or refuse it. With DodgeChance 0 (the default) the brain draws nothing for it: today's brain.

    /// <summary>
    /// The chance of dodging, instead of blocking, when the foe begins a blow within its reach (SANDBOX): drawn first,
    /// from the brain's own stream <c>combat.duel-brain.dodge</c>; when it says no, the block's chance is drawn as before.
    /// 0: never (no draw at all, so the block's draws are today's).
    /// </summary>
    public float DodgeChance { get; init; }

    /// <summary>The shares of dodges made straight back and to a side (taken in proportion; a side is left or right evenly).</summary>
    public float DodgeBackShare { get; init; } = 0.5f;
    public float DodgeSideShare { get; init; } = 0.5f;

    /// <summary>
    /// How long after the reaction a dodge may still wait for the brain's own blow to stop playing (s): a dodge is
    /// refused mid-swing, so the brain presses as soon as its swing is over, or gives the dodge up after this.
    /// </summary>
    public float DodgeWaitSeconds { get; init; } = 0.3f;

    /// <summary>
    /// After a dodge the brain does not close in again while the blow it dodged still plays (SANDBOX, the testbed's
    /// policy): the first sweeps showed a brain whose dodge ended sooner (a higher Dodge, a shorter recovery) walking back into
    /// the same blow's late arc — 0.6 to 0.9 s after it began — so a faster dodge met the blade more often. False: it closes as
    /// soon as the dodge ends.
    /// </summary>
    public bool DodgeHoldOff { get; init; } = true;

    /// <summary>
    /// When to press a chosen dodge (SANDBOX): false (the default, the brief's), as soon as the reaction has passed;
    /// true, timed to the blow — after the reaction, once the foe's blow is due to strike (its <c>Strike</c> mark, read from its
    /// action's time and rate) within the dodge's start and half its burst (its own <see cref="Bodies.DodgeTempo"/>), so the
    /// body is moving when the blade comes. The first sweeps showed why it matters: reacting at a fixed 0.15 s, a quicker
    /// dodge (a higher Dodge) had stopped by the time the late arc of a foe stepping in arrived.
    /// </summary>
    public bool DodgeAtStrike { get; init; }

    // At the end: the parry (the block's press timed to the blow). With ParryChance 0 (the default) the brain
    // draws nothing for it: today's brain.

    /// <summary>
    /// The chance of parrying, instead of blocking, when the foe begins a blow within its reach (SANDBOX): drawn after the
    /// dodge's and before the block's, from the brain's own stream <c>combat.duel-brain.parry</c>; a parry presses block (a
    /// press: let go first if held) once the reaction has passed and the foe's blow is due to strike within
    /// <see cref="ParryLeadSeconds"/>, so the parry window spans the blade's arrival, and holds it as a guard after. 0: never.
    /// </summary>
    public float ParryChance { get; init; }

    /// <summary>
    /// How long before the foe's blow is due to strike the parry's press comes (s; SANDBOX 0.25: a 0.25 s window then spans the
    /// last quarter second before the strike mark, where 1.1's blows meet what is in front of them; measured at 0.15 the guard,
    /// rising over the channel's 0.1 s fade-in, was not up in time and the blade met the forearm).
    /// </summary>
    public float ParryLeadSeconds { get; init; } = 0.25f;

    // ---- the quality work (the: range keeping; ENGINE_SANDBOX, the testbed's policy). With blow quality a blade that
    // meets inside its own good range (at its hilt, early in its arc) delivers little: a blade fighter keeps its range. Off with the
    // switch brain-range-off (DuelRangeSettings.Enabled): today's brain.

    /// <summary>
    /// The inner limit of a blade's good range, a share of its reach centre to centre (ENGINE_SANDBOX, measured: the
    /// docs' "Range keeping"): nearer than this a blade fighter (its reach above <see cref="DuelBrains.BareReach"/>) steps back to
    /// <see cref="CloseShare"/> of its reach before it swings again — a claw or fist fighter closes as ever.
    /// </summary>
    public float RangeNearShare { get; init; } = 0.65f;

    /// <summary>The most it holds its swing while it cannot open the range (s): then it swings anyway, so a pinned fighter is never passive for ever.</summary>
    public float RangeHoldMostSeconds { get; init; } = 0.75f;

    /// <summary>Stepping back (or aside) slower than this away from its foe (m/s) for <see cref="PinnedSeconds"/> while its own blow is not playing is being pinned (a wall, a corner, the foe's body).</summary>
    public float PinnedSpeed { get; init; } = 0.3f;
    public float PinnedSeconds { get; init; } = 0.25f;

    /// <summary>Pinned, it side-steps round its foe (and a little back) for this long (s), then steps back again; a side that is blocked too is changed for the other.</summary>
    public float SideStepSeconds { get; init; } = 0.6f;

    /// <summary>Its own blow met a body within this share of the blade from the guard (the hilt): it steps back after it, whatever the distance said.</summary>
    public float HiltAlong { get; init; } = 0.1f;

    /// <summary>
    /// Stepping back while the distance opens slower than this (m/s) for <see cref="PinnedSeconds"/> is being pressed (a foe following
    /// as fast as it backs): it then steps back at a run (its body's backpedal pace at a run) until it is at its range.
    /// </summary>
    public float PressedOpening { get; init; } = 0.3f;

    /// <summary>
    /// How far ahead it judges its range when it would swing (s; about its blows' time from the press to the blade's meeting): a foe
    /// closing on it is judged where it will be then, so a blow begun at range is not one that meets a foe already inside it (the
    /// szlachta's lunge). 0: where it is now.
    /// </summary>
    public float RangeLeadSeconds { get; init; } = 0.4f;

    /// <summary>A blow met at its hilt at a distance: its range against that foe begins this much further out (m), for the rest of the fight (at most just inside its strike range).</summary>
    public float HiltNearMargin { get; init; } = 0.2f;

    /// <summary>Its range is learned only while its blows keep meeting at the hilt: a running share of its body contacts at the hilt, each weighing this, at or above <see cref="HiltRateToLearn"/> (0.3 and 0.6: three hilt contacts running; a duel at range meets there one blow in twelve).</summary>
    public float HiltRateWeight { get; init; } = 0.3f;
    public float HiltRateToLearn { get; init; } = 0.6f;
    // ---- end

    // ---- the direction of its blows (directional attacks on: Control/AttackDirections.cs). With them
    // off (attack-spin) none of this is read or drawn: the earlier build's brain.

    /// <summary>
    /// The weights of its blows from the left, the right, overhead and the thrust (ENGINE_SANDBOX, ours; taken in proportion over
    /// the directions its body has blows for, from its own stream <c>combat.duel-brain.direction</c>): the two sides as often, the
    /// overhead and the thrust half as often each (a third, a third, a sixth, a sixth with all four).
    /// </summary>
    public float LeftShare { get; init; } = 1f;
    public float RightShare { get; init; } = 1f;
    public float OverheadShare { get; init; } = 0.5f;
    public float ThrustShare { get; init; } = 0.5f;

    /// <summary>
    /// The chance, when its foe's guard is raised and held to one side of the foe's centre line (its shield's middle or its blade's,
    /// as seen from the brain, beyond <see cref="GuardSideMetres"/>), of striking from the other side — where the guard is not (ENGINE_SANDBOX).
    /// </summary>
    public float AwayFromGuardShare { get; init; } = 0.6f;

    /// <summary>How far to a side of the foe's centre line its guard must be held to count as turned to it (m).</summary>
    public float GuardSideMetres { get; init; } = 0.1f;

    /// <summary>
    /// A parry waiting on a foe's blow held at its ready point is given up after the
    /// hold has lasted this long (s, from the first tick the blow is seen held there; ENGINE_SANDBOX, ours): the brain then keeps a
    /// block it has, or raises one by its block chance, and is free to act — it may choose the parry again on the release. 0.6 s:
    /// past every blow's own wind-up (the press to the ready point takes 0.21 to 0.58 s; a tap strikes 0.57 s after its press), so
    /// only a deliberate hold is waited out that long, and the brain stands still for at most about a second after the press.
    /// </summary>
    public float HeldParryGiveUpSeconds { get; init; } = 0.6f;

    /// <summary>
    /// The pace given in place of the person's kind's (<see cref="DuelPace"/>; an arena spec's <c>brain.pace</c>): the
    /// pause after a run, <see cref="PaceLeastSeconds"/> to <see cref="PaceMostSeconds"/> s (drawn); a most below 0 (the default) the
    /// kind's, 0 none. Nothing at all with the module's switch <c>pace-off</c>.
    /// </summary>
    public float PaceLeastSeconds { get; init; } = -1f;

    /// <inheritdoc cref="PaceLeastSeconds"/>
    public float PaceMostSeconds { get; init; } = -1f;

    /// <summary>The longest run of blows before a pause (1 to this, drawn) with <see cref="PaceMostSeconds"/> given; 0, the kind's (else 2).</summary>
    public int PaceRunMost { get; init; }

    /// <summary>
    /// How near it keeps during a pause, in its reaches (it steps back nearer than <see cref="PaceNearShare"/> and closes
    /// farther than <see cref="PaceFarShare"/>, circling between by a side drawn at the pause's start); ENGINE_SANDBOX, ours: at its
    /// blade's length, a step out of its foe's easy reach and a step from striking.
    /// </summary>
    public float PaceNearShare { get; init; } = 0.95f;

    /// <inheritdoc cref="PaceNearShare"/>
    public float PaceFarShare { get; init; } = 1.3f;

    /// <summary>The share of its walk given to circling during a pause (the rest keeps the range).</summary>
    public float PaceCircleShare { get; init; } = 0.6f;
    // ---- end

    // ---- how it comes on from beyond its striking range (DuelApproach.cs). Null (the default) is the
    // duelling brain's own approach, and the cast's text does not change. The 1.1 creature's numbers are not the cast's: the session
    // reads them from the player's install when it makes the brain (never written, so a tape's cast is the same text).

    /// <summary>Its approach (<see cref="DuelApproach"/>); null, the duelling brain's own.</summary>
    public DuelApproach? Approach { get; init; }

    /// <summary>1.1's approach's think, seconds (the creature's AI file's <c>moveRate</c>, read at run time); 0: not read, the duel's approach.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public float OneOneMoveSeconds { get; init; }

    /// <summary>1.1's approach's longest step, metres (its AI file's <c>chaseDist</c> at 72 units to the metre, read at run time).</summary>
    [System.Text.Json.Serialization.JsonIgnore] public float OneOneStepMostMetres { get; init; }

    /// <summary>Whether 1.1's approach walks its steps at its run (its AI file's <c>attackFlags</c> bit 0, read at run time), else its walk.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public bool OneOneRuns { get; init; }
    // ---- end
}

/// <summary>
/// The brain's use of the quick slots: the tick of its last press, and its presses (uses and renewals asked for).
/// <b>Not digested</b>: a world's digest names every digested store even when it is empty, so a digested one here would change
/// every arena bout's digest, powers or none; what a press does is in the world (the module's ledger and live records) and
/// digested there. Added to a fighter the first time it has slots to read.
/// </summary>
public struct DuelBrainPowers
{
    public long LastPress;
    public int Presses, Renewals;
}

// ---- range keeping ----
/// <summary>
/// Whether the duelling brain keeps its blade's range (on by default; the enhanced module's switch
/// <c>brain-range-off</c> turns it off: the brain before it). A service, registered by <see cref="DuelBrains.Register"/> or by the
/// module first, whichever comes first.
/// </summary>
public sealed class DuelRangeSettings
{
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// A blade fighter's range keeping (digested, plain data, on the duelling brain's body once it first keeps range):
/// whether it is stepping back out of a clinch, for how long it has held its swing for it, how long it has gained no ground, a
/// side-step under way and its side (+1 its left, −1 its right, as it faces its foe), its own swing last seen, and its counts —
/// retreats begun, those begun by a blow met at its hilt, side-steps out of a pin, and swings it made anyway after holding.
/// </summary>
public struct DuelRange
{
    int retreating, pressed;
    public float ClinchTime, StuckTime, SideLeft, PressTime, LastDistance;
    /// <summary>Its range against this foe learned from its blows met at its hilt (m, feet to feet; 0: none yet): the range begins there.</summary>
    public float LearnedNear;
    /// <summary>How often its blows have met bodies at its hilt lately (a running share: each body contact weighs <see cref="DuelBrainSettings.HiltRateWeight"/>).</summary>
    public float HiltRate;
    public int Side, LastSwing;
    public int Retreats, HiltRetreats, SideSteps, Forced;

    public bool Retreating { readonly get => retreating != 0; set => retreating = value ? 1 : 0; }
    /// <summary>Whether its foe presses it (follows as fast as it backs): it backs at a run.</summary>
    public bool Pressed { readonly get => pressed != 0; set => pressed = value ? 1 : 0; }
}
// ---- end

/// <summary>The settings of every brain in a world, by index (<see cref="DuelBrain.Book"/>; a service, so the component holds no reference).</summary>
public sealed class DuelBrainBook
{
    readonly List<DuelBrainSettings> all = [];

    public IReadOnlyList<DuelBrainSettings> All => all;

    /// <summary>Adds settings and gives their index.</summary>
    public int Add(DuelBrainSettings settings)
    {
        all.Add(settings);
        return all.Count - 1;
    }
}

/// <summary>
/// A fighter's brain (digested, plain data): its kind and foe, its settings' index, its weapon's reach and its
/// foe's (m, shoulder to tip with the arm out, <see cref="DuelBrains.ReachOf"/>), and what it is in the middle of: attack
/// held for so many more ticks (a press), a block asked for after a reaction and then held, a back-off, the foe's last blow
/// answered and its own last blow reviewed, and the blows, blocks and back-offs it chose.
/// </summary>
public struct DuelBrain
{
    public EntityId Foe;
    public BrainKind Kind;
    public int Book;
    public float Reach, FoeReach;
    public int HoldTicks;
    public float ReactIn, BlockLeft, BackOffLeft;
    public int AnsweredSwing, ReviewedSwing;
    public float AimPitch;
    public int Blows, Strong, Blocks, BackOffs;
    int wasAttack;
    // A dodge chosen and waiting for the reaction (DodgeIn, s; then up to DodgeWaitSeconds for its own swing to end),
    // its side (−1 left, 0 back, +1 right, as the brain faces its foe), and the dodges it asked for.
    int dodgePending;
    public float DodgeIn;
    public int DodgeSide, Dodges;
    /// <summary>The foe's swing its last dodge answered (0 for none): it holds off while that swing still plays.</summary>
    public int DodgedSwing;

    // At the end: a parry chosen and waiting for the reaction and the blow's timing (ParryIn, s), the
    // foe's swing it answers, and the parries it pressed.
    int parryPending;
    public float ParryIn;
    public int ParrySwing, Parries;

    /// <summary>Whether a parry is chosen and not yet pressed.</summary>
    public bool ParryPending { readonly get => parryPending != 0; set => parryPending = value ? 1 : 0; }

    // ---- the parry timed by the hand's travel — the press's lead before the blow's strike mark (s, drawn
    // when chosen: the hand's travel and the skill's timing error), and the parries chosen and those given up
    // because its own swing (or stun) would not be over in time. ----
    public float ParryLead;
    public int ParriesChosen, ParriesNotFree;
    // ---- end

    /// <summary>Whether a dodge is chosen and not yet asked for.</summary>
    public bool DodgePending { readonly get => dodgePending != 0; set => dodgePending = value ? 1 : 0; }

    /// <summary>Whether the brain held attack last tick (a new press must wait for the release).</summary>
    public bool WasAttack { readonly get => wasAttack != 0; set => wasAttack = value ? 1 : 0; }

    // ---- the direction it chose for its next blow, and how often it struck away from its foe's guard.
    public AttackDirection Direction;
    public int AwayFromGuard;
    // The direction work's follow-up (by design): the foe's blow seen held at its ready point (its swing number; 0 none) and for
    // how long (s); the parries given up while it was held, the parries chosen again on its release, the blocks kept up through a hold.
    public int HoldSeen;
    public float FoeHeld;
    public int ParriesGivenUp, ParriesOnRelease, BlocksHeld;

    // ---- the pace (DuelPace) ----
    /// <summary>The blows left in its run; the pause's seconds left (counted once its own blow is over); the side it circles (−1, 1).</summary>
    public int RunLeft;
    public float PauseLeft;
    public int CircleSide;
    /// <summary>The pauses it took, their seconds drawn in all, and those cut short by an opening (its foe staggered, stunned or its guard broken).</summary>
    public int Pauses, PausesCut;
    public float PausedSeconds;

    // ---- ----
    /// <summary>An opening seen during this pause already drawn for (1), so each opening is drawn for once (its riposte chance).</summary>
    public int OpeningSeen;
    // ---- end

    // ---- the shield read ----
    /// <summary>Its blows that read the foe's shield and were aimed over it, under it, and the reads that found no room either way.</summary>
    public int ShieldReadsOver, ShieldReadsUnder, ShieldReadsNoRoom;
    /// <summary>Its read blows that were to be thrusts and were cut instead (a thrust is driven at the shield's face whatever its aim).</summary>
    public int ShieldReadsUnthrust;
    // ---- end
    // ---- the draw's height for its blow (1 high, 0 level, −1 low), kept so its aim can follow its foe
    // through the blow (SwingAim.Track).
    public int AimDraw;
    // ---- end

    public static DuelBrain For(BrainKind kind, EntityId foe, int book, float reach, float foeReach) =>
        new() { Kind = kind, Foe = foe, Book = book, Reach = reach, FoeReach = foeReach, ReactIn = -1f };
}

/// <summary>
/// The duelling brain (<see cref="Phase.Think"/>; ours, simple and deterministic). It drives a fighter's
/// <see cref="CombatIntent"/> and <see cref="MoveIntent"/> as a player's buttons would, through the same systems — the
/// attacks' rules, the body's motor, the action channel; nothing it does reaches a hit by a shortcut:
/// <list type="bullet">
/// <item>it <b>faces</b> its foe and <b>closes</b> to <see cref="DuelBrainSettings.CloseShare"/> of its reach, braking in time
/// for its body's own deceleration (running beyond
/// <see cref="DuelBrainSettings.WalkWithin"/>, walking within; a walk while the block is held), and steps back to
/// <see cref="DuelBrainSettings.TooCloseShare"/> of it when nearer;</item>
/// <item>it <b>attacks</b> when the foe is within <see cref="DuelBrainSettings.StrikeShare"/> of the reach and it is ready
/// (no swing busy or waiting, no block, attack let go last tick): strong by a share (attack held past the strong
/// threshold), quick otherwise (a tap), aimed high, level or low by shares;</item>
/// <item>it <b>blocks</b> when the foe begins a blow (the foe's swing number moves on) within the foe's reach, by a chance,
/// after a reaction, for a while;</item>
/// <item>it <b>backs off</b> a step after its own blow ends, by a chance;</item>
/// <item>It <b>dodges</b> instead of blocking, by <see cref="DuelBrainSettings.DodgeChance"/> (drawn first, from its own
/// stream), back or to a side, after the same reaction and once its own blow is not playing — asking for the dodge through
/// the same intent as the player's key (<see cref="DodgeIntent"/>), which the platform's rules begin or refuse.</item>
/// </list>
/// Dummies stand (nothing held) or block (the block held always), facing where they were put. The dead and those
/// whose foe is dead stand and let go. Draws from <c>world.Random.For("combat.duel-brain")</c> in the store's order.
/// No allocation per tick.
/// </summary>
public sealed class DuelBrainSystem : ISystem
{
    public void Run(World world)
    {
        if (!world.TryService<DuelBrainBook>(out var book)) return;
        var brains = world.Store<DuelBrain>();
        if (brains.Count == 0) return;
        var bodies = world.Store<Body>();
        var moves = world.Store<MoveIntent>();
        var intents = world.Store<CombatIntent>();
        var fighters = world.Store<Combatant>();
        var anims = world.Store<CharacterAnimation>();
        var locos = world.Store<Locomotion>();
        var springs = world.TryStore<StartStopSpring>(out var ssp) ? ssp : null;   // bodies on the spring stop by it
        float strongAfter = world.TryService<CombatSettings>(out var cs) ? cs.StrongAfter : 0.3f;
        var rng = world.Random.For("combat.duel-brain");
        var directions = DirectionalAttacks.On(world, out var dirs) ? dirs : null;   // null, 1.1's blows
        // The quick slots' views and the slot pressed, when a module registers them (Powers/PowerContract.cs).
        var views = world.TryStore<Powers.PowerSlots>(out var vs) ? vs : null;
        var powerIntents = world.TryStore<Powers.PowerIntent>(out var pis) ? pis : null;
        var vitals = world.TryStore<Powers.Vitals>(out var vt) ? vt : null;   // its Health, for a slot used when hurt
        // While a power of its own is live (its slots' views show one), the brain keeps its rhythm to its body's tempo:
        // a block held and a back-off walked for the usual times divided by the tempo's rates; its reaction (perception) is
        // unchanged. Without this a (1 + rank)x body still held each block 0.9 s and walked each back-off 0.5 s, and those
        // real-time waits, not its swings, capped its blows a second (the earlier build's first sweep: 1.3 a second at every rank from 3).
        // Not while no power is live, so a bout with no powers is the earlier bout (the Abilities' tempo alone changes nothing
        // here: the brain's policy is not a second channel for Melee). SANDBOX, the testbed's; off with PaceWithPowers.
        var tempos = CombatTempos.Registered(world) ? world.Store<CombatTempo>() : null;
        var guardSettings = world.TryService<GuardSettings>(out var gss) ? gss : null;
        var swingAim = world.TryService<SwingAim>(out var sam) ? sam : null;             // the aim at the foe's body
        const float dt = World.TickSeconds;
        // ---- range keeping, its state, and last tick's contacts (a blow met at its own hilt)
        var ranges = world.TryService<DuelRangeSettings>(out var rangeSettings) && rangeSettings.Enabled && world.TryStore<DuelRange>(out var rst) ? rst : null;
        var lastContacts = ranges is not null && world.TryService<EventQueue<ContactMade>>(out var cq) ? cq.LastTick : null;
        // ---- end
        // ---- the pace (none unless the module registers it) and last tick's staggers (an opening ends a pause)
        var paces = world.TryStore<DuelPace>(out var pst) ? pst : null;
        var lastStaggers = paces is not null && world.TryService<EventQueue<Staggered>>(out var sq) ? sq.LastTick : null;
        // ---- end
        var oneOnes = world.TryStore<DuelOneOne>(out var oos) ? oos : null;   // 1.1's approach, only where registered
        var decisions = world.TryService<BrainDecisions>(out var bdz) ? bdz : null;   // null unless a traced run asks
        var routes = world.TryService<DuelRoutes>(out var drs) && drs.Enabled ? drs : null;   // closing round a wall
        var data = brains.Data;
        var ents = brains.Entities;
        for (int i = 0; i < data.Length; i++)
        {
            var e = ents[i];
            ref var brain = ref data[i];
            ref var body = ref bodies.Find(e);
            ref var move = ref moves.Find(e);
            ref var intent = ref intents.Find(e);
            ref var c = ref fighters.Find(e);
            if (Unsafe.IsNullRef(ref body) || Unsafe.IsNullRef(ref move) || Unsafe.IsNullRef(ref intent) || Unsafe.IsNullRef(ref c)) continue;
            var s = book.All[Math.Clamp(brain.Book, 0, book.All.Count - 1)];
            move.Direction = Vector2.Zero;
            move.Jump = move.JumpHeld = move.Crouch = move.Cancel = move.Use = false;

            bool dead = IsDead(anims, e);
            if (dead || brain.Kind == BrainKind.Stand)
            {
                Let(ref intent, ref brain);
                LetSlots(powerIntents, e);
                continue;
            }
            if (brain.Kind == BrainKind.Block)
            {
                if (decisions is not null && decisions.HeldOnce(e))   // the dummy's block, told once
                    decisions.Publish(new BrainDecided(e, world.Tick, BrainChoice.HoldBlock, BrainRule.Always, brain.Foe, 0f, brain.Reach, brain.FoeReach, 0, BrainFoePhase.None, c.Swing, c.Busy > 0f, c.Blocking, c.Stun, brain.PauseLeft, FormerTarget: EntityId.None));
                LetSlots(powerIntents, e);
                intent.Attack = false;
                intent.Block = true;
                brain.WasAttack = false;
                continue;
            }

            // The duel: the foe, where it is, and whether it still fights.
            ref var foeBody = ref bodies.Find(brain.Foe);
            if (!world.Alive(brain.Foe) || Unsafe.IsNullRef(ref foeBody) || IsDead(anims, brain.Foe))
            {
                Let(ref intent, ref brain);
                LetSlots(powerIntents, e);
                continue;
            }
            var delta = new Vector2(foeBody.Feet.X - body.Feet.X, foeBody.Feet.Y - body.Feet.Y);
            float d = MathF.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
            var toward = d > 1e-4f ? delta / d : Vector2.Zero;
            if (d > 1e-4f)
            {
                move.Facing = DetMath.Atan2(delta.Y, delta.X);
                move.Faces = true;
            }

            // The foe's blow begun within its reach: dodge or block, by a chance, after a reaction.
            // ---- the guard and the parry chosen — by the fighter's skill (GuardSkill, absent the earlier build's: the chances
            // and the reaction times its factors), the parry timed by its hand's travel and chosen only when its own swing will be over
            // in time (GuardSettings.BrainParryByTravel, only with the arm solver: guard-solver-off is the earlier build's brain exactly) ----
            ref var foeC = ref fighters.Find(brain.Foe);
            var gskill = GuardSkills.Of(world, e);
            // An untrained fighter with no shield raises no blade or fists to block or parry (its draws
            // still made, so its streams keep their places); a shield blocks as ever.
            if (gskill.Untrained && !HeldItems.HasShield(world, e)) { gskill.Guard = 0f; gskill.Parry = 0f; }
            bool timed = guardSettings is { Enabled: true, Solver: true, BrainParryByTravel: true };
            // What it saw, once a tick, only when decisions are asked for; its foe told when first seen or changed
            BrainDecided seen = decisions is null ? default : BrainDecisions.Seen(world, e, in brain, in c, ref foeC, anims, d);
            decisions?.Foe(in seen, BrainRule.FoeChanged);
            if (!Unsafe.IsNullRef(ref foeC) && foeC.Swing != brain.AnsweredSwing)
            {
                brain.AnsweredSwing = foeC.Swing;
                if (d <= brain.FoeReach)
                {
                    float react = s.ReactSeconds * gskill.React;
                    // The draws kept for the receipt (the same draws, in the same order, from the same streams)
                    float dodgeDraw = -1f, parryDraw = -1f, blockDraw = -1f;
                    bool parryFree = true;
                    float parryChance = MathF.Min(1f, s.ParryChance * gskill.Parry), blockChance = MathF.Min(1f, s.BlockChance * gskill.Guard);
                    if (s.DodgeChance > 0f && Dodge(world, s, ref brain, out dodgeDraw))
                    {
                        decisions?.Publish(seen with { Choice = BrainChoice.Dodge, Rule = BrainRule.FoeBlowInReach, Draw = dodgeDraw, Threshold = s.DodgeChance,
                            DodgeDraw = dodgeDraw, DodgeChance = s.DodgeChance, Seconds = brain.DodgeIn });
                    }
                    else if (s.ParryChance > 0f && (parryDraw = world.Random.For("combat.duel-brain.parry").NextFloat()) < parryChance
                             && (!timed || (parryFree = FreeInTime(world, s, guardSettings!, gskill, ref brain, in c, anims, in foeC, react))))
                    {
                        // A parry, pressed when the blow is due (Parry, below).
                        brain.ParryPending = true;
                        brain.ParryIn = react;
                        brain.ParrySwing = foeC.Swing;
                        decisions?.Publish(seen with { Choice = BrainChoice.Parry, Rule = BrainRule.FoeBlowInReach, Draw = parryDraw, Threshold = parryChance,
                            DodgeDraw = dodgeDraw, DodgeChance = s.DodgeChance, ParryDraw = parryDraw, ParryChance = parryChance, Seconds = react });
                    }
                    else if ((blockDraw = rng.NextFloat()) < blockChance)
                    {
                        brain.ReactIn = react;
                        decisions?.Publish(seen with { Choice = BrainChoice.Block, Rule = BrainRule.FoeBlowInReach, Draw = blockDraw, Threshold = blockChance,
                            DodgeDraw = dodgeDraw, DodgeChance = s.DodgeChance, ParryDraw = parryDraw, ParryChance = parryChance, BlockDraw = blockDraw,
                            BlockChance = blockChance, ParryFreeInTime = parryFree, Seconds = react });
                    }
                    else decisions?.Publish(seen with { Choice = BrainChoice.NoDefence, Rule = BrainRule.FoeBlowInReach, Draw = blockDraw, Threshold = blockChance,
                            DodgeDraw = dodgeDraw, DodgeChance = s.DodgeChance, ParryDraw = parryDraw, ParryChance = parryChance, BlockDraw = blockDraw,
                            BlockChance = blockChance, ParryFreeInTime = parryFree });
                }
                else decisions?.Publish(seen with { Choice = BrainChoice.NoDefence, Rule = BrainRule.FoeBlowOutOfReach });
            }
            if (brain.DodgePending) AskDodge(world, e, s, ref brain, in c, anims, toward, in foeC);
            // ---- a foe's blow held at its ready point — the reaction waits for the release,
            // a block stays up through the hold, a parry is given up after HeldParryGiveUpSeconds. A tap never reaches its ready point,
            // and attack-spin readies nothing: then none of this runs.
            int held = Unsafe.IsNullRef(ref foeC) ? 0 : FoeHolding(anims, brain.Foe, in foeC);
            bool released = held == 0 && brain.HoldSeen != 0 && !Unsafe.IsNullRef(ref foeC) && foeC.Swing == brain.HoldSeen;
            if (held != 0)
            {
                if (brain.HoldSeen != held)
                {
                    brain.HoldSeen = held;
                    brain.FoeHeld = 0f;
                    if (brain.ParryPending && brain.ParrySwing == held) brain.ParryIn = s.ReactSeconds * gskill.React;   // the reaction starts again from the release
                }
                else brain.FoeHeld += dt;
                if (brain.ParryPending && brain.ParrySwing == held && brain.FoeHeld >= s.HeldParryGiveUpSeconds)
                {
                    // Given up: free to act; a block kept, or raised by its chance (its own stream), held until the release.
                    brain.ParryPending = false;
                    brain.ParryIn = 0f;
                    brain.ParriesGivenUp++;
                    float heldDraw = -1f, heldChance = MathF.Min(1f, s.BlockChance * gskill.Guard);   // kept for the receipt
                    if (brain.BlockLeft <= 0f && (heldDraw = world.Random.For("combat.duel-brain.held").NextFloat()) < heldChance)
                    {
                        brain.BlockLeft = s.BlockSeconds / Paced(s, views, tempos, e, rise: true);
                        brain.Blocks++;
                    }
                    decisions?.Publish(seen with { Choice = brain.BlockLeft > 0f ? BrainChoice.Block : BrainChoice.NoDefence, Rule = BrainRule.HeldBlowParryGivenUp,
                        Draw = heldDraw, Threshold = heldDraw < 0f ? -1f : heldChance, BlockDraw = heldDraw, BlockChance = heldChance });
                }
            }
            else if (released)
            {
                brain.HoldSeen = 0;
                float react = s.ReactSeconds * gskill.React;
                // A block up through the hold: its usual time from the release.
                if (brain.BlockLeft > 0f) { brain.BlockLeft = s.BlockSeconds / Paced(s, views, tempos, e, rise: true); brain.BlocksHeld++; }
                // A parry given up may be chosen again on the release, by its chance and skill, at its reaction.
                float againDraw = -1f, againChance = MathF.Min(1f, s.ParryChance * gskill.Parry);   // kept for the receipt
                if (!brain.ParryPending && brain.ParrySwing == foeC.Swing && s.ParryChance > 0f && d <= brain.FoeReach
                    && (againDraw = world.Random.For("combat.duel-brain.held").NextFloat()) < againChance
                    && (!timed || FreeInTime(world, s, guardSettings!, gskill, ref brain, in c, anims, in foeC, react, fromRelease: true)))
                {
                    brain.ParryPending = true;
                    brain.ParryIn = react;
                    brain.ParriesOnRelease++;
                    decisions?.Publish(seen with { Choice = BrainChoice.Parry, Rule = BrainRule.FoeReleasedBlow, Draw = againDraw, Threshold = againChance,
                        ParryDraw = againDraw, ParryChance = againChance, Seconds = react });
                }
                // The reaction counted from the release: it was let go in the last tick's Act, a tick ago.
                if (brain.ParryPending && brain.ParrySwing == foeC.Swing && brain.ParryIn > 0f) brain.ParryIn = MathF.Max(0f, brain.ParryIn - dt);
            }
            else if (brain.HoldSeen != 0 && (Unsafe.IsNullRef(ref foeC) || foeC.Swing != brain.HoldSeen)) brain.HoldSeen = 0;
            // ---- end
            if (brain.ParryPending) Parry(s, ref brain, in c, anims, in foeC, views, tempos, e);
            bool parryHolds = timed && brain.ParryPending;   // a parry waiting holds its own next blow (else its swing keeps its guard down)
            // ---- end
            if (brain.ReactIn >= 0f)
            {
                brain.ReactIn -= dt;
                if (brain.ReactIn <= 0f)
                {
                    brain.ReactIn = -1f;
                    brain.BlockLeft = s.BlockSeconds / Paced(s, views, tempos, e, rise: true);
                    brain.Blocks++;
                }
            }
            if (brain.BlockLeft > 0f && held == 0) brain.BlockLeft -= dt;   // The direction work's follow-up: up through a foe's hold
            intent.Block = brain.BlockLeft > 0f;

            // Its own blow ended: back off a step, by a chance.
            bool busy = c.Busy > 0f || c.Pending != AttackStrength.None || c.Stun > 0f;   // it waits out the attacker's stun
            // ---- the pause runs once its own blow is over; an opening — its foe staggered, stunned by a guard or its
            // guard broken — ends it (no draw). Its guard, parry and dodge answer the foe's blows above, paused or not.
            // An
            // opening cuts the pause by its riposte chance (DuelPace.RiposteChance, by Melee), drawn once as each opening begins.
            bool open = brain.PauseLeft > 0f || brain.OpeningSeen != 0 ? Opening(lastStaggers, brain.Foe, ref foeC) : false;
            if (!open) brain.OpeningSeen = 0;   // the opening over: the next one is drawn for
            if (brain.PauseLeft > 0f)
            {
                if (open && brain.OpeningSeen == 0)
                {
                    brain.OpeningSeen = 1;
                    float ripDraw = -1f;   // kept for the receipt
                    if (DuelPaces.Of(paces, s, e, out var opened) && (ripDraw = world.Random.For("combat.duel-brain.pace").NextFloat()) < opened.RiposteChance)
                    {
                        brain.PauseLeft = 0f;
                        brain.PausesCut++;
                        decisions?.Publish(seen with { Choice = BrainChoice.Riposte, Rule = BrainRule.FoeOpened, Draw = ripDraw, Threshold = opened.RiposteChance });
                    }
                }
                if (brain.PauseLeft > 0f && !busy) brain.PauseLeft = MathF.Max(0f, brain.PauseLeft - dt);
            }
            bool paused = brain.PauseLeft > 0f && !busy;
            // ---- end
            if (c.Swing != brain.ReviewedSwing && !busy && !brain.WasAttack && brain.HoldTicks == 0)
            {
                brain.ReviewedSwing = c.Swing;
                float backDraw;   // kept for the receipt
                if ((backDraw = rng.NextFloat()) < s.BackOffChance)
                {
                    brain.BackOffLeft = s.BackOffSeconds / Paced(s, views, tempos, e, rise: false);
                    brain.BackOffs++;
                    decisions?.Publish(seen with { Choice = BrainChoice.BackOff, Rule = BrainRule.OwnBlowEnded, Draw = backDraw, Threshold = s.BackOffChance, Seconds = brain.BackOffLeft });
                }
            }

            // Where to go: away while backing off, toward the foe until close enough, else stand.
            // ---- 1.1's approach beyond its striking range (DuelOneOnes.Step), in place of the closing below
            bool oneOne = oneOnes is not null && DuelOneOnes.Uses(s) && brain.BackOffLeft <= 0f && !paused
                && !(s.DodgeHoldOff && brain.DodgedSwing != 0 && !Unsafe.IsNullRef(ref foeC) && foeC.Swing == brain.DodgedSwing && foeC.Busy > 0f)
                && DuelOneOnes.Step(oneOnes, e, s, in brain, ref move, in body, locos, brain.Foe, d, toward, busy, world.Random.For(DuelOneOnes.Stream), springs);
            // ---- end
            var feet = BrainChoice.Stand;   // its feet's choice, told when it changes
            if (oneOne) { feet = BrainChoice.Approach; }
            else if (brain.BackOffLeft > 0f)
            {
                feet = BrainChoice.BackOff;
                brain.BackOffLeft -= dt;
                move.Direction = -toward;
                move.Gait = Gait.Walk;
            }
            else if (s.DodgeHoldOff && brain.DodgedSwing != 0 && !Unsafe.IsNullRef(ref foeC) && foeC.Swing == brain.DodgedSwing && foeC.Busy > 0f)
            {
                // Holding off after a dodge while the blow it dodged still plays (no closing back into its arc).
                feet = BrainChoice.HoldOff;
            }
            else if (paused)
            {
                feet = BrainChoice.Pause;
                // At its blade's distance, circling — out of reach and a step from striking.
                var side = new Vector2(-toward.Y, toward.X) * (brain.CircleSide < 0 ? -1f : 1f);
                float radial = d < brain.Reach * s.PaceNearShare ? -1f : d - Stopping(in body, locos, springs, e, toward) > brain.Reach * s.PaceFarShare ? 1f : 0f;
                var way = toward * radial + side * s.PaceCircleShare;   // back or in first when out of its band, round its foe within it
                float len = way.Length();
                move.Direction = len > 1e-4f ? way / len : Vector2.Zero;
                move.Gait = Gait.Walk;
            }
            else if (d - Stopping(in body, locos, springs, e, toward) > brain.Reach * s.CloseShare)
            {
                // Closing, braking in time: the body slows at its own braking (a grounded body slides on a while it stops).
                feet = BrainChoice.Close;
                float ahead = d - Stopping(in body, locos, springs, e, toward);
                move.Direction = toward;
                move.Gait = ahead > s.WalkWithin ? Gait.Run : Gait.Walk;
                // ---- round a wall — only when the straight line to the foe meets the level (DuelRoutes; off with brain-straight)
                if (routes is not null && routes.Way(world, e, body.Feet, foeBody.Feet, out var routed))
                {
                    move.Direction = routed;
                    move.Facing = DetMath.Atan2(routed.Y, routed.X);   // facing the way while it goes round
                }
                // ---- end
            }
            else if (d < brain.Reach * s.TooCloseShare)
            {
                feet = BrainChoice.StepBack;
                move.Direction = -toward;
                move.Gait = Gait.Walk;
            }
            decisions?.Feet(in seen, feet);
            if (intent.Block || c.Blocking) move.Gait = Gait.Walk;
            // ---- a blade fighter keeps its range — out of a clinch before it swings again
            int range = ranges is null ? RangeNone : KeepRange(e, s, in brain, ref move, in body, in c, toward, d, ranges, lastContacts, foeBody.Velocity);
            // ---- end

            // Attack: a press in progress is held on; else begin one when in reach and ready.
            bool down = false;
            if (brain.HoldTicks > 0)
            {
                down = true;
                brain.HoldTicks--;
            }
            else if (!brain.WasAttack && !busy && !c.Blocking && !intent.Block && brain.ReactIn < 0f && brain.BackOffLeft <= 0f
                     && !brain.DodgePending && !parryHolds   // not while a timed parry waits
                     && brain.PauseLeft <= 0f                // not during a pause
                     && (d <= brain.Reach * s.StrikeShare && range != RangeHold || range == RangeStrike))   // range keeping
            {
                float strongDraw = rng.NextFloat();   // kept for the receipt
                bool strong = strongDraw < s.StrongShare;
                float total = MathF.Max(1e-6f, s.HighShare + s.LevelShare + s.LowShare);
                float r = rng.NextFloat() * total;
                brain.AimPitch = (r < s.HighShare ? s.HighDegrees : r < s.HighShare + s.LevelShare ? 0f : s.LowDegrees) * DetMath.DegToRad;
                // ---- the same draw aimed at a height on the foe's own body (SwingAim; registered unless swings-oneone)
                brain.AimDraw = r < s.HighShare ? 1 : r < s.HighShare + s.LevelShare ? 0 : -1;   // kept for SwingAim.Track
                if (swingAim is not null)
                    brain.AimPitch = swingAim.PitchAt(world, in body, brain.Foe, in foeBody, brain.AimDraw);
                // ---- end
                // ---- directional attacks — no strong blow (the draw is kept, so the stream's later draws are the earlier build's), a
                // tap from the direction it chooses.
                if (directions is not null)
                {
                    strong = false;
                    brain.Direction = ChooseDirection(world, directions, e, brain.Foe, in body, in foeBody, s, ref brain, fighters);
                    // A thrust aimed at the centre of the foe's chest, not by the draw (kept, so the stream is the earlier build's).
                    if (swingAim is not null && brain.Direction == AttackDirection.Thrust)
                        brain.AimPitch = swingAim.ThrustPitchAt(world, e, in body, brain.Foe, in foeBody);
                }
                // ---- end
                // ---- the shield read — by its skill's chance (GuardSkill.ShieldRead, drawn from its own
                // stream, and never drawn when the chance is 0: the earlier build's brain exactly), a blow at a foe who carries a shield is aimed
                // where the shield is not, over its top or under its bottom as it is held now (ReadShield).
                if (gskill.ShieldRead > 0f && swingAim is not null)
                    ReadShield(world, guardSettings ?? ShieldReadDefaults, swingAim, directions, s, e, in body, brain.Foe, in foeBody, gskill.ShieldRead, ref brain);
                // ---- end
                brain.HoldTicks = strong ? (int)MathF.Ceiling((strongAfter + 0.05f) * World.TicksPerSecond) - 1 : 0;
                brain.Blows++;
                if (strong) brain.Strong++;
                down = true;
                float pauseBefore = brain.PauseLeft;
                if (DuelPaces.Of(paces, s, e, out var pace)) RunBlow(world, ref brain, in pace);
                if (decisions is not null)   // the blow, and the pause its run's end brought
                {
                    decisions.Attack(seen with { Choice = BrainChoice.Attack, Rule = range == RangeStrike ? BrainRule.RangeKeeping : BrainRule.InStrikingRange,
                        Draw = strongDraw, Threshold = s.StrongShare, Direction = brain.Direction, Strong = strong,
                        AimPitchDegrees = brain.AimPitch / DetMath.DegToRad, Range = range });
                    if (brain.PauseLeft > 0f && pauseBefore <= 0f)
                        decisions.Publish(seen with { Choice = BrainChoice.Pause, Rule = BrainRule.RunEnded, Seconds = brain.PauseLeft });
                }
            }
            // ---- the aim kept on the foe's body through its own blow, from where he is now and as his pose has him now
            // (SwingAim.Track; as a player's look follows his foe through a blow): the same draw's height, the thrust at his chest.
            else if (swingAim is not null && swingAim.Track && busy && c.Stun <= 0f)
                brain.AimPitch = directions is not null && brain.Direction == AttackDirection.Thrust
                    ? swingAim.ThrustPitchAt(world, e, in body, brain.Foe, in foeBody)
                    : swingAim.PitchAt(world, in body, brain.Foe, in foeBody, brain.AimDraw);
            // ---- end
            intent.Attack = down;
            if (directions is not null) intent.Direction = brain.Direction;
            intent.Aims = true;
            intent.AimPitch = brain.AimPitch;
            brain.WasAttack = down;

            // The quick slots, when the module shows this fighter's.
            if (views is not null && powerIntents is not null) UsePowers(world, e, brain, s, d, views, powerIntents, vitals, decisions, in seen);
        }
    }

    /// <summary>
    /// The quick slots (deterministic, no draw): while the foe is within <see cref="DuelBrainSettings.PowerShare"/> of the
    /// reach, press the first slot it may use (<see cref="DuelBrainSettings.PowerSlots"/>, in slot order) whose view is
    /// <see cref="Powers.SlotState.Ready"/>, or <see cref="Powers.SlotState.Live"/> with less than
    /// <see cref="DuelBrainSettings.RenewSeconds"/> left and no recast running (a renewal, as 1.1's cast is timed); not
    /// again within <see cref="DuelBrainSettings.PowerWaitSeconds"/> of its last press. It writes <see cref="Powers.PowerIntent.Slot"/>
    /// for the tick (0 when it presses nothing), which the module reads and clears in <see cref="Phase.Act"/>. Nothing at all for
    /// a fighter whose slots the module does not show. 1.1's AI (a danger above 2, a 50 % roll, a 15 s cooldown) is not copied.
    /// By the slot's use (<see cref="Powers.SlotView.Use"/>, the module's): first, while its Health is below
    /// <see cref="DuelBrainSettings.HealBelow"/> of its maximum, the first <see cref="Powers.SlotUse.Hurt"/> slot that is Ready,
    /// engaged or not (1.1's ladder heals first); else the <see cref="Powers.SlotUse.Engage"/> slots as above. A slot for use when
    /// hurt is never pressed because the brain is engaged.
    /// </summary>
    static void UsePowers(World world, EntityId e, in DuelBrain brain, DuelBrainSettings s, float d, Store<Powers.PowerSlots> views, Store<Powers.PowerIntent> powerIntents,
        Store<Powers.Vitals>? vitals, BrainDecisions? decisions = null, in BrainDecided seen = default)
    {
        if (s.PowerSlots == 0) return;          // it may use none: nothing at all, as without views
        ref var slots = ref views.Find(e);
        if (Unsafe.IsNullRef(ref slots)) return;
        var states = world.Store<DuelBrainPowers>();
        if (!states.Has(e)) states.Set(e, new DuelBrainPowers { LastPress = long.MinValue / 2 });
        if (!powerIntents.Has(e)) powerIntents.Set(e, default);
        ref var state = ref states.Find(e);
        ref var intent = ref powerIntents.Find(e);

        int press = 0;
        bool renewal = false;
        long wait = (long)MathF.Ceiling(s.PowerWaitSeconds * World.TicksPerSecond);
        bool waited = world.Tick - state.LastPress >= wait;
        bool hurt = false;   // which rule pressed it
        if (waited && (hurt = Hurt(vitals, e, s.HealBelow)))
            for (int slot = 1; slot <= Powers.QuickSlots.Count && press == 0; slot++)
            {
                if ((s.PowerSlots & (1 << (slot - 1))) == 0) continue;
                var v = slots.Slots[slot - 1];
                if (v.Power >= 0 && v.Use == Powers.SlotUse.Hurt && v.State == Powers.SlotState.Ready) press = slot;
            }
        if (press == 0 && waited && d <= brain.Reach * s.PowerShare)
            for (int slot = 1; slot <= Powers.QuickSlots.Count && press == 0; slot++)
            {
                if ((s.PowerSlots & (1 << (slot - 1))) == 0) continue;
                var v = slots.Slots[slot - 1];
                if (v.Power < 0 || v.Use != Powers.SlotUse.Engage) continue;
                if (v.State == Powers.SlotState.Ready) press = slot;
                else if (v.State == Powers.SlotState.Live && v.SecondsLeft < s.RenewSeconds && v.RecastLeft <= 0f) { press = slot; renewal = true; }
            }
        intent.Slot = press;
        if (press == 0) return;
        if (decisions is not null)
        {
            int pw = slots.Slots[press - 1].Power;
            string? id = pw >= 0 && world.TryService<Powers.IPowerCatalogue>(out var cat) ? cat.Id(pw) : null;
            bool byHurt = hurt && slots.Slots[press - 1].Use == Powers.SlotUse.Hurt;
            decisions.Publish(seen with { Choice = BrainChoice.Power, Rule = renewal ? BrainRule.Renewal : byHurt ? BrainRule.Hurt : BrainRule.Engaged, Slot = press, Power = id });
        }
        state.LastPress = world.Tick;
        state.Presses++;
        if (renewal) state.Renewals++;
    }

    /// <summary>Whether the fighter's Health (the module's <see cref="Powers.Vitals"/>) is below <paramref name="share"/> of its maximum; false with no vitals, no maximum or a share of 0.</summary>
    static bool Hurt(Store<Powers.Vitals>? vitals, EntityId e, float share)
    {
        if (vitals is null || share <= 0f) return false;
        ref var v = ref vitals.Find(e);
        return !Unsafe.IsNullRef(ref v) && v.HealthMax > 0f && v.Health < share * v.HealthMax;
    }

    /// <summary>
    /// The dodge's draw, when the foe begins a blow within reach and <see cref="DuelBrainSettings.DodgeChance"/> is above
    /// 0: from the brain's own stream <c>combat.duel-brain.dodge</c>, whether to dodge, then back or to a side (and which).
    /// Answers whether it dodges (the block's chance is then not drawn).
    /// </summary>
    static bool Dodge(World world, DuelBrainSettings s, ref DuelBrain brain, out float draw)
    {
        var dr = world.Random.For("combat.duel-brain.dodge");
        if ((draw = dr.NextFloat()) >= s.DodgeChance) return false;   // the draw kept for the receipt
        float total = MathF.Max(1e-6f, s.DodgeBackShare + s.DodgeSideShare);
        brain.DodgeSide = dr.NextFloat() * total < s.DodgeBackShare ? 0 : dr.NextFloat() < 0.5f ? -1 : 1;
        brain.DodgePending = true;
        brain.DodgeIn = s.ReactSeconds;
        return true;
    }

    /// <summary>
    /// A chosen dodge: after the reaction, asked for (<see cref="Bodies.Dodges.Ask"/>) as soon as the brain's own blow is
    /// not playing — back from the foe, or to its left or right as it faces it — or given up when its own swing still plays
    /// <see cref="DuelBrainSettings.DodgeWaitSeconds"/> after the reaction.
    /// </summary>
    static void AskDodge(World world, EntityId e, DuelBrainSettings s, ref DuelBrain brain, in Combatant c, Store<CharacterAnimation> anims, Vector2 toward,
                         in Combatant foe)
    {
        brain.DodgeIn -= World.TickSeconds;
        if (brain.DodgeIn > 0f) return;
        if (s.DodgeAtStrike && !Unsafe.IsNullRef(in foe) && !StrikeDue(world, brain.Foe, in foe, anims, e)) return;
        ref var a = ref anims.Find(e);
        bool swinging = c.Busy > 0f && !Unsafe.IsNullRef(ref a) && ActionRoles.IsAttack(a.Action);
        if (swinging)
        {
            if (brain.DodgeIn <= -s.DodgeWaitSeconds) brain.DodgePending = false;   // given up: its own blow still plays
            return;
        }
        brain.DodgePending = false;
        if (!world.TryStore<DodgeIntent>(out _)) return;
        var left = new Vector2(-toward.Y, toward.X);
        var way = brain.DodgeSide switch { 0 => -toward, < 0 => left, _ => -left };
        Dodges.Ask(world, e, way);
        brain.Dodges++;
        brain.DodgedSwing = brain.AnsweredSwing;
    }

    /// <summary>
    /// Whether the foe's blow is due to strike within this body's dodge start and half its burst
    /// (<see cref="DuelBrainSettings.DodgeAtStrike"/>): its <c>Strike</c> mark less its action's time, over its action's rate. True
    /// when the foe plays no blow, its mark is unknown, or the strike is past (press at once).
    /// </summary>
    static bool StrikeDue(World world, EntityId foeId, in Combatant foe, Store<CharacterAnimation> anims, EntityId self)
    {
        ref var fa = ref anims.Find(foeId);
        if (Unsafe.IsNullRef(ref fa) || !ActionRoles.IsAttack(fa.Action) || foe.StrikeAt < 0f || fa.ActionRate <= 0f) return true;
        if (foe.Readied) return false;   // a blow held ready is not coming until it is released
        float toStrike = (foe.StrikeAt - fa.ActionTime) / fa.ActionRate;
        var t = world.TryStore<DodgeTempo>(out var ts) && ts.Has(self) ? ts.Get(self)
              : (world.TryService<DodgeSettings>(out var ds) ? ds : new DodgeSettings()).Tempo;
        return toStrike <= t.Ready + 0.5f * t.Move;
    }

    // ---- the foe's blow held at its ready point ----
    /// <summary>The foe's swing number when its blow is held ready at its ready point now (<see cref="Combatant.Readied"/> and <see cref="ActionChannel.AtReady"/>), else 0: a blow still in its wind-up (a tap's, or a hold's first moments) is not held.</summary>
    static int FoeHolding(Store<CharacterAnimation> anims, EntityId foe, in Combatant foeC)
    {
        if (!foeC.Readied || foeC.Swing == 0) return 0;
        ref var fa = ref anims.Find(foe);
        return !Unsafe.IsNullRef(ref fa) && ActionChannel.AtReady(fa) ? foeC.Swing : 0;
    }
    // ---- end's follow-up

    // ---- the direction of its blow ----
    /// <summary>
    /// The direction of the brain's next blow (deterministic, from its own stream <c>combat.duel-brain.direction</c>): when its
    /// foe holds its guard raised and turned to a side of its centre line (<see cref="GuardSideSeen"/>), by
    /// <see cref="DuelBrainSettings.AwayFromGuardShare"/> the other side (where the guard is not); else by the weights
    /// (<see cref="DuelBrainSettings.LeftShare"/> …) over the directions its body has blows for (with only 1.1's two quick blows: left
    /// and right evenly).
    /// </summary>
    static AttackDirection ChooseDirection(World world, DirectionalAttacks dirs, EntityId e, EntityId foe, in Body body, in Body foeBody, DuelBrainSettings s,
        ref DuelBrain brain, Store<Combatant> fighters)
    {
        if (!world.TryService<CharacterAssets>(out var assets) || !world.TryStore<CharacterAnimation>(out var anims) || !anims.Has(e)) return AttackDirection.None;
        int kind = anims.Get(e).Kind;
        var r = world.Random.For("combat.duel-brain.direction");
        bool left = dirs.Has(world, assets, kind, AttackDirection.Left), right = dirs.Has(world, assets, kind, AttackDirection.Right);
        bool over = dirs.Has(world, assets, kind, AttackDirection.Overhead), thrust = dirs.Has(world, assets, kind, AttackDirection.Thrust);
        ref var fc = ref fighters.Find(foe);
        if (!Unsafe.IsNullRef(ref fc) && fc.Blocking && s.AwayFromGuardShare > 0f && r.NextFloat() < s.AwayFromGuardShare)
        {
            float side = GuardSideSeen(world, foe, in body, in foeBody);
            if (side > s.GuardSideMetres && left) { brain.AwayFromGuard++; return AttackDirection.Left; }
            if (side < -s.GuardSideMetres && right) { brain.AwayFromGuard++; return AttackDirection.Right; }
        }
        float wl = left ? s.LeftShare : 0f, wr = right ? s.RightShare : 0f, wo = over ? s.OverheadShare : 0f, wt = thrust ? s.ThrustShare : 0f;
        float total = wl + wr + wo + wt;
        if (total <= 0f) return right ? AttackDirection.Right : left ? AttackDirection.Left : AttackDirection.None;
        float x = r.NextFloat() * total;
        return x < wl ? AttackDirection.Left : x < wl + wr ? AttackDirection.Right : x < wl + wr + wo ? AttackDirection.Overhead : AttackDirection.Thrust;
    }

    /// <summary>
    /// Where <paramref name="foe"/>'s guard is held across its centre line as seen from the brain's body (m, to the brain's right
    /// positive): its shield's middle when it holds one, else its blade's middle (the guard to the tip), from its feet, across the line
    /// from the brain to it; 0 with neither.
    /// </summary>
    static float GuardSideSeen(World world, EntityId foe, in Body body, in Body foeBody)
    {
        Vector3 at;
        if (HeldItems.Shield(world, foe, out var box)) at = box.Centre;
        else if (HeldItems.Blade(world, foe, out var blade)) at = (blade.Guard + blade.Tip) * 0.5f;
        else return 0f;
        float dx = foeBody.Feet.X - body.Feet.X, dy = foeBody.Feet.Y - body.Feet.Y;
        float d = MathF.Sqrt(dx * dx + dy * dy);
        if (d < 1e-4f) return 0f;
        var right = new Vector2(dy / d, -dx / d);
        return (at.X - foeBody.Feet.X) * right.X + (at.Y - foeBody.Feet.Y) * right.Y;
    }
    // ---- end

    // ---- the shield read ----
    static readonly GuardSettings ShieldReadDefaults = new();

    /// <summary>
    /// Skill should tell against a shielded guard: a skilled attacker finds his way round a shield. With
    /// chance <paramref name="chance"/> (its <see cref="GuardSkill.ShieldRead"/>, by Melee; drawn from its own stream
    /// <c>combat.duel-brain.shield</c>), when <paramref name="foe"/> carries a shield, the blow just chosen is re-aimed at the foe's
    /// body where the shield is not, as the shield is held now (raised or resting, its box's highest and lowest corners): <b>over</b>
    /// its top when there is <see cref="GuardSettings.ShieldReadOverMetres"/> or more of him above it (at
    /// <see cref="GuardSettings.ShieldReadOverShare"/> of that room: the head and neck over a shield held at the chest), else
    /// <b>under</b> its bottom when there is <see cref="GuardSettings.ShieldReadUnderMetres"/> or more above the lowest aim
    /// (<see cref="SwingAim.Lowest"/>; the middle of that room), else nothing changes. A drawn thrust becomes a cut (left, right or
    /// overhead by the brain's weights over those its body has, a second draw from the same stream): the stitched thrust is driven at
    /// the shield's face whatever its aim. The pitch is worked as <see cref="SwingAim"/> works its own (a side blow's level sweep at
    /// <see cref="SwingAim.SweepHeight"/>). Geometry, not a roll: the blade still has to get there, and a guard who raises his shield
    /// in time meets it (the guard follows the blow's height). Why it is the lever (the earlier build's arena, the newly Embraced at Melee 3 against
    /// the shielded guard, 200 bouts): every blow aimed high and none a thrust won 52.5 % against 20.5 %; aimed high with the thrust
    /// still at the chest 39.5 %; choosing the direction alone (no thrust, one side, overhead alone) 20 to 31.5 %.
    /// </summary>
    static void ReadShield(World world, GuardSettings g, SwingAim aim, DirectionalAttacks? dirs, DuelBrainSettings s, EntityId e, in Body body, EntityId foe,
        in Body foeBody, float chance, ref DuelBrain brain)
    {
        if (!HeldItems.HasShield(world, foe) || !HeldItems.Shield(world, foe, out var box)) return;
        var r = world.Random.For("combat.duel-brain.shield");
        if (r.NextFloat() >= chance) return;
        float up = MathF.Abs(box.AxisX.Z) * box.HalfSize.X + MathF.Abs(box.AxisY.Z) * box.HalfSize.Y + MathF.Abs(box.AxisZ.Z) * box.HalfSize.Z;
        float top = box.Centre.Z + up - foeBody.Feet.Z, bottom = box.Centre.Z - up - foeBody.Feet.Z;
        float foeTop = SwingAim.Top(world, foe, in foeBody);
        float target;
        if (foeTop - top >= g.ShieldReadOverMetres) { target = top + (foeTop - top) * g.ShieldReadOverShare; brain.ShieldReadsOver++; }
        else if (bottom - aim.Lowest >= g.ShieldReadUnderMetres) { target = aim.Lowest + (bottom - aim.Lowest) * 0.5f; brain.ShieldReadsUnder++; }
        else { brain.ShieldReadsNoRoom++; return; }
        // A thrust is driven at the centre of the shield's face whatever its aim (measured: the read thrust met the body no more often):
        // a read blow is a cut instead, from the left, the right or overhead by the brain's weights over those its body has.
        if (brain.Direction == AttackDirection.Thrust && dirs is not null && world.TryService<CharacterAssets>(out var assets)
            && world.TryStore<CharacterAnimation>(out var anims) && anims.Has(e))
        {
            int kind = anims.Get(e).Kind;
            float wl = dirs.Has(world, assets, kind, AttackDirection.Left) ? s.LeftShare : 0f, wr = dirs.Has(world, assets, kind, AttackDirection.Right) ? s.RightShare : 0f;
            float wo = dirs.Has(world, assets, kind, AttackDirection.Overhead) ? s.OverheadShare : 0f;
            float x = r.NextFloat() * (wl + wr + wo);
            if (wl + wr + wo > 0f) { brain.Direction = x < wl ? AttackDirection.Left : x < wl + wr ? AttackDirection.Right : AttackDirection.Overhead; brain.ShieldReadsUnthrust++; }
        }
        target = Math.Clamp(target, aim.Lowest, MathF.Max(aim.Lowest, MathF.Min(aim.Highest, foeTop)));
        float from = brain.Direction == AttackDirection.Thrust ? SwingAim.Top(world, e, in body) * aim.ThrustFromShare : aim.SweepHeight;
        float dx = foeBody.Feet.X - body.Feet.X, dy = foeBody.Feet.Y - body.Feet.Y;
        float d = MathF.Max(aim.LeastDistance, MathF.Sqrt(dx * dx + dy * dy));
        brain.AimPitch = DetMath.Atan2(foeBody.Feet.Z + target - (body.Feet.Z + from), d);
    }
    // ---- end

    // ---- range keeping ----
    /// <summary>
    /// Range keeping (the; ENGINE_SANDBOX): a blade fighter (its reach above <see cref="DuelBrains.BareReach"/>)
    /// nearer its foe than <see cref="DuelBrainSettings.RangeNearShare"/> of its reach, or whose own blow met a body at its hilt last
    /// tick (<see cref="DuelBrainSettings.HiltAlong"/>), <b>steps back</b> — a backpedal at a walk, through the body's own motor, so at
    /// its real pace and direction pace — until it is at <see cref="DuelBrainSettings.CloseShare"/> of its reach again, and holds its
    /// next swing meanwhile (at most <see cref="DuelBrainSettings.RangeHoldMostSeconds"/>, then it swings anyway). <b>Pinned</b> —
    /// gaining no ground for <see cref="DuelBrainSettings.PinnedSeconds"/> while its own blow is not playing — it <b>side-steps round its
    /// foe</b> (and a little back) for <see cref="DuelBrainSettings.SideStepSeconds"/>, to its left first, the other side when that is
    /// blocked too, then steps back again. A claw or fist fighter closes as ever. Answers whether the swing is held. No draw; allocates
    /// nothing after its first tick.
    /// </summary>
    static int KeepRange(EntityId e, DuelBrainSettings s, in DuelBrain brain, ref MoveIntent move, in Body body, in Combatant c, Vector2 toward, float d,
        Store<DuelRange> ranges, IReadOnlyList<ContactMade>? lastContacts, Vector3 foeVelocity)
    {
        if (brain.Reach <= DuelBrains.BareReach + 0.05f || toward == Vector2.Zero) return RangeNone;
        ref var r = ref ranges.Find(e);
        if (Unsafe.IsNullRef(ref r))
        {
            ranges.Set(e, new DuelRange { Side = 1, LastSwing = c.Swing, LastDistance = d });
            r = ref ranges.Find(e);
        }
        const float dt = World.TickSeconds;
        float strike = brain.Reach * s.StrikeShare;
        if (c.Swing != r.LastSwing) { r.LastSwing = c.Swing; r.ClinchTime = 0f; }   // a new blow of its own: the hold counts afresh
        // Its own blow met a body at its hilt last tick: this foe is nearer than the feet say (a crouch, a head held forward) — its
        // range against it begins further out (learned for the bout), and it steps back now.
        bool hilt = false;
        if (lastContacts is not null)
            for (int i = 0; i < lastContacts.Count; i++)
            {
                var lc = lastContacts[i];
                if (lc.Attacker != e || lc.Met != ContactMet.Body || !(lc.PeakSpeed > 0f)) continue;
                bool atHilt = lc.AlongBlade < s.HiltAlong;
                r.HiltRate = r.HiltRate * (1f - s.HiltRateWeight) + (atHilt ? s.HiltRateWeight : 0f);
                if (!atHilt) continue;
                hilt = true;
                // Learned only when its blows keep meeting at the hilt (a duel's arcs meet there now and then at any range).
                if (r.HiltRate >= s.HiltRateToLearn) r.LearnedNear = MathF.Max(r.LearnedNear, MathF.Min(d + s.HiltNearMargin, strike - 0.15f));
                break;
            }
        float near = MathF.Max(brain.Reach * s.RangeNearShare, r.LearnedNear);
        float far = MathF.Max(brain.Reach * s.CloseShare * 0.95f, near + 0.15f);
        if (!r.Retreating && (d < near || hilt && d < far))
        {
            r.Retreating = true;
            r.Retreats++;
            if (hilt) r.HiltRetreats++;
            r.LastDistance = d;
        }
        if (r.Retreating && d >= far) { r.Retreating = false; r.Pressed = false; r.ClinchTime = r.StuckTime = r.SideLeft = r.PressTime = 0f; }
        // Where the foe will be when a blow begun now meets (RangeLeadSeconds ahead, at the speed the foe itself comes on: its own
        // approach brakes to its range, and is not judged so).
        float closing = foeVelocity.X * -toward.X + foeVelocity.Y * -toward.Y;
        float then = closing > 0f ? d - closing * s.RangeLeadSeconds : d;
        if (!r.Retreating)
        {
            r.LastDistance = d;
            if (then < near && d <= strike) return Held(ref r, s);              // it would meet the foe inside its range: not yet
            if (d > strike && then <= strike && then >= near) return RangeStrike;   // a foe coming in: meet it as it enters the range
            return RangeNone;
        }

        bool own = c.Busy > 0f;   // its own blow playing: the feet are not judged (a swing roots the body)
        var left = new Vector2(-toward.Y, toward.X);
        Vector2 way;
        if (r.SideLeft > 0f)
        {
            r.SideLeft -= dt;
            way = Vector2.Normalize(left * r.Side - toward * 0.35f);
        }
        else way = -toward;
        move.Direction = way;
        // Pressed: the distance not opening while it backs (its foe follows as fast) — it backs at a run, its body's own backpedal pace.
        bool opening = (d - r.LastDistance) / dt >= s.PressedOpening;
        r.LastDistance = d;
        if (!r.Pressed && !own && !opening && (r.PressTime += dt) >= s.PinnedSeconds) r.Pressed = true;
        else if (opening) r.PressTime = 0f;
        move.Gait = r.Pressed && !c.Blocking ? Gait.Run : Gait.Walk;   // a guard held walks, as ever
        float gained = body.Velocity.X * way.X + body.Velocity.Y * way.Y;
        if (own || gained >= s.PinnedSpeed) r.StuckTime = 0f;
        else if ((r.StuckTime += dt) >= s.PinnedSeconds)
        {
            r.StuckTime = 0f;
            if (r.SideLeft > 0f) r.Side = -r.Side;        // that side is blocked too: the other
            else r.SideSteps++;
            r.SideLeft = s.SideStepSeconds;
        }
        return Held(ref r, s);
    }

    /// <summary>A swing held for range — for at most <see cref="DuelBrainSettings.RangeHoldMostSeconds"/> a blow of its own, then its next swing comes anyway.</summary>
    static int Held(ref DuelRange r, DuelBrainSettings s)
    {
        bool hold = r.ClinchTime < s.RangeHoldMostSeconds;
        r.ClinchTime += World.TickSeconds;
        if (hold && r.ClinchTime >= s.RangeHoldMostSeconds) r.Forced++;   // held as long as it may: its next swing comes anyway
        return hold ? RangeHold : RangeNone;
    }

    /// <summary>What range keeping says of a swing this tick: nothing, hold it, or begin it now (a foe coming in, met as it enters the range).</summary>
    const int RangeNone = 0, RangeHold = 1, RangeStrike = 2;
    // ---- end

    /// <summary>How far the body would slide toward its foe before it stopped, braking at its own deceleration (m; on the spring, by the spring).</summary>
    static float Stopping(in Body body, Store<Locomotion> locos, Store<StartStopSpring>? springs, EntityId e, Vector2 toward)
    {
        float v = body.Velocity.X * toward.X + body.Velocity.Y * toward.Y;
        if (v <= 0f) return 0f;
        if (springs is not null)
        {
            ref var spring = ref springs.Find(e);
            if (!Unsafe.IsNullRef(ref spring) && spring.HalfLife > 0f) return BodyMotor.SpringStoppingAlong(spring, v, toward);
        }
        ref var loco = ref locos.Find(e);
        float brake = Unsafe.IsNullRef(ref loco) || loco.BrakingDeceleration <= 0f ? 8f : loco.BrakingDeceleration;
        return v * v / (2f * brake) + v * World.TickSeconds;
    }

    /// <summary>
    /// The rate the brain divides its own durations by: the body's block-rise or swing rate while a power of its own is
    /// live and <see cref="DuelBrainSettings.PaceWithPowers"/> is on; 1 otherwise.
    /// </summary>
    static float Paced(DuelBrainSettings s, Store<Powers.PowerSlots>? views, Store<CombatTempo>? tempos, EntityId e, bool rise)
    {
        // The tempo the live powers themselves give (the slots' views), never the whole body tempo — a
        // Blood rise live with Melee above 0 no longer paces the brain by the Abilities' tempo (the common work's finding).
        if (!s.PaceWithPowers || views is null) return 1f;
        ref var v = ref views.Find(e);
        if (Unsafe.IsNullRef(ref v)) return 1f;
        float pace = 1f;
        for (int k = 0; k < Powers.QuickSlots.Count; k++)
            if (v.Slots[k].State == Powers.SlotState.Live) pace = MathF.Max(pace, v.Slots[k].Tempo);
        return pace;
    }

    /// <summary>
    /// A blow of its run begun — a run of 1 to <see cref="DuelPace.RunMost"/> blows drawn as it begins, and after
    /// its last a pause of <see cref="DuelPace.PauseLeast"/> to <see cref="DuelPace.PauseMost"/> s and the side it circles, all from
    /// its own stream <c>combat.duel-brain.pace</c> (its other streams keep their places).
    /// </summary>
    static void RunBlow(World world, ref DuelBrain brain, in DuelPace pace)
    {
        var pr = world.Random.For("combat.duel-brain.pace");
        if (brain.RunLeft <= 0) brain.RunLeft = 1 + Math.Min(pace.RunMost - 1, (int)(pr.NextFloat() * pace.RunMost));
        if (--brain.RunLeft > 0) return;
        brain.PauseLeft = pace.PauseLeast + (pace.PauseMost - pace.PauseLeast) * pr.NextFloat();
        brain.CircleSide = pr.NextFloat() < 0.5f ? -1 : 1;
        brain.Pauses++;
        brain.PausedSeconds += brain.PauseLeft;
    }

    /// <summary>A clearly better opening — its foe staggered last tick, stunned after its blade was stopped at a guard, or its guard broken.</summary>
    static bool Opening(IReadOnlyList<Staggered>? lastStaggers, EntityId foe, ref Combatant foeC)
    {
        if (!Unsafe.IsNullRef(ref foeC) && (foeC.Stun > 0f || foeC.GuardBroken > 0f)) return true;
        if (lastStaggers is null) return false;
        for (int i = 0; i < lastStaggers.Count; i++) if (lastStaggers[i].Who == foe) return true;
        return false;
    }

    static bool IsDead(Store<CharacterAnimation> anims, EntityId e)
    {
        ref var a = ref anims.Find(e);
        return !Unsafe.IsNullRef(ref a) && ActionRoles.IsDeath(a.Action);
    }

    /// <summary>No slot pressed: a press is for one tick, and a brain that stops presses nothing.</summary>
    static void LetSlots(Store<Powers.PowerIntent>? powerIntents, EntityId e)
    {
        if (powerIntents is null) return;
        ref var pi = ref powerIntents.Find(e);
        if (!Unsafe.IsNullRef(ref pi)) pi.Slot = 0;
    }

    // ---- the parry timed by the hand's travel ----
    /// <summary>
    /// Whether a parry of the foe's blow just begun can be pressed in time, and its press's lead (kept in
    /// <see cref="DuelBrain.ParryLead"/>): the blow is due at its strike mark (from its action's time and rate); the window should open
    /// when the guarding hand can just reach the blade — the hand's travel (<see cref="GuardSettings.BrainParryTravelMetres"/>) over its
    /// parry speed (<see cref="GuardSettings.SolverParryHandMetresPerSecond"/> × the fighter's <see cref="GuardSkill.Hand"/>) plus the
    /// margin before the blade arrives (about <see cref="GuardSettings.BrainParryArriveSeconds"/> before the strike mark) — but never so
    /// early that the window closes before it arrives; the skill's timing error drawn either way. It can be pressed only once its own
    /// swing and stun are over and no blow of its own waits (a press in its own swing raises no window: the finding); when they will
    /// not be over before the press is due, no parry (the block's chance is drawn instead).
    /// </summary>
    static bool FreeInTime(World world, DuelBrainSettings s, GuardSettings g, in GuardSkill skill, ref DuelBrain brain, in Combatant c,
        Store<CharacterAnimation> anims, in Combatant foeC, float react, bool fromRelease = false)
    {
        brain.ParriesChosen++;
        ref var fa = ref anims.Find(brain.Foe);
        if (Unsafe.IsNullRef(ref fa) || foeC.StrikeAt < 0f || fa.ActionRate <= 0f) { brain.ParryLead = 0f; return true; }
        // A blow held ready strikes only on its release — no sooner than the rest of its
        // wind-up and its strike, so it is judged as if let go now (the soonest it can come). Every blow is readied at its press, and a tap
        // is let go on the next tick, after the brain has judged it here: counting a held blow as never coming (+∞) made every blow look
        // free in time, and a brain busy with its own swing chose a parry it could not press (no window), in place of its block's draw.
        float due = (foeC.StrikeAt - fa.ActionTime) / fa.ActionRate;
        float speed = MathF.Max(1e-3f, g.SolverParryHandMetresPerSecond * skill.Hand);
        float lead = MathF.Min(g.ParryWindowSeconds, g.BrainParryArriveSeconds + g.BrainParryTravelMetres / speed + g.BrainParryMarginSeconds);
        if (skill.Error > 0f) lead += (world.Random.For("combat.duel-brain.parry").NextFloat() * 2f - 1f) * skill.Error;
        lead = MathF.Max(World.TickSeconds, lead);
        brain.ParryLead = lead;
        float own = MathF.Max(c.Busy, c.Stun);
        bool waiting = c.Pending != AttackStrength.None || brain.HoldTicks > 0;
        // The direction work's follow-up: chosen on a held blow's release, the press comes no sooner than the reaction from it — the blow
        // is often due within its lead already — so its own swing must be over by the later of the two (the press as soon as allowed).
        float pressIn = fromRelease ? MathF.Max(due - lead, react) : due - lead;
        if (waiting || own > pressIn) { brain.ParriesNotFree++; brain.ParryLead = 0f; return false; }
        return true;
    }
    // ---- end

    static void Let(ref CombatIntent intent, ref DuelBrain brain)
    {
        intent.Attack = false;
        intent.Block = false;
        brain.WasAttack = false;
        brain.HoldTicks = 0;
        brain.BlockLeft = 0f;
        brain.ReactIn = -1f;
        brain.DodgePending = false;
        brain.ParryPending = false;
    }

    /// <summary>
    /// A chosen parry — after the reaction, once the foe's blow (the swing it answers) is due to strike within
    /// <see cref="DuelBrainSettings.ParryLeadSeconds"/> (its <c>Strike</c> mark, from its action's time and rate), block is let go
    /// (if held) and pressed the tick after, then held as a block; given up when that blow is over or past its mark.
    /// </summary>
    static void Parry(DuelBrainSettings s, ref DuelBrain brain, in Combatant c, Store<CharacterAnimation> anims, in Combatant foeC,
        Store<Powers.PowerSlots>? views, Store<CombatTempo>? tempos, EntityId e)
    {
        ref var fa = ref anims.Find(brain.Foe);
        // The direction work's follow-up: no reaction while the foe holds its blow at its ready point; it runs from the release.
        if (!Unsafe.IsNullRef(ref fa) && foeC.Readied && ActionChannel.AtReady(fa)) return;
        if (brain.ParryIn > 0f) { brain.ParryIn -= World.TickSeconds; if (brain.ParryIn > 0f) return; brain.ParryIn = 0f; }
        if (Unsafe.IsNullRef(ref fa) || foeC.Swing != brain.ParrySwing || !ActionRoles.IsAttack(fa.Action) || fa.ActionEnding || foeC.StrikeAt < 0f || fa.ActionRate <= 0f
            || fa.ActionTime >= foeC.StrikeAt)
        {
            brain.ParryPending = false;
            return;
        }
        if (foeC.Readied) return;   // a blow held ready — the parry waits for its release, not baited by the hold
        float due = (foeC.StrikeAt - fa.ActionTime) / fa.ActionRate;
        if (due > (brain.ParryLead > 0f ? brain.ParryLead : s.ParryLeadSeconds)) return;   // its lead drawn when chosen
        if (brain.ParryIn == 0f && brain.BlockLeft > 0f) { brain.BlockLeft = 0f; brain.ParryIn = -1f; return; }   // held: let go this tick
        brain.ParryPending = false;
        brain.BlockLeft = s.BlockSeconds / Paced(s, views, tempos, e, rise: true);
        brain.Parries++;
        brain.Blocks++;
    }
}

/// <summary>The brains registered, and the weapon's reach they close to.</summary>
public static class DuelBrains
{
    /// <summary>The brain's place: <c>Think/95</c>, after the action script (90) and before the routes (100).</summary>
    public const int Order = 95;

    /// <summary>
    /// Registers the <see cref="DuelBrain"/> component, the earlier build's <see cref="DuelBrainPowers"/> (undigested), the
    /// <see cref="DuelBrainBook"/> service and <c>Think/95 platform.duel-brain</c>. After <see cref="CombatServices.Register"/>.
    /// It does not register the powers' contract (<see cref="Powers.PowerContract"/>): the module that shows slots does.
    /// </summary>
    public static WorldBuilder Register(WorldBuilder b)
    {
        b.Component<DuelBrain>();
        b.Component<DuelBrainPowers>(digested: false);      // undigested (see the type)
        b.Service(new DuelBrainBook());
        b.Component<DuelRange>();                                                              // range keeping
        if (!b.Peek.TryService<DuelRangeSettings>(out _)) b.Service(new DuelRangeSettings());   // (the module may have set it first)
        b.System(Phase.Think, Order, "platform.duel-brain", new DuelBrainSystem());
        return b;
    }

    /// <summary>The reach when a kind has no arm or no blade (a bare-handed blow): 0.9 m.</summary>
    public const float BareReach = 0.9f;

    /// <summary>
    /// A kind's reach (m): the blade's tip from the shoulder with the arm held out — the arm's bones from the
    /// shoulder down to the bone that carries the first blade, measured in the kind's rest pose, plus the tip's distance
    /// from that bone (the held model's fit) — times the kind's scale. <see cref="BareReach"/> when it has no blade or
    /// no arm. Ours: 1.1's reach is a gap between cylinders (the survey, 2); the platform's contact is the drawn blade.
    /// </summary>
    public static float ReachOf(CharacterAssets assets, int kind)
    {
        var k = assets.Kind(kind);
        HeldThing? blade = null;
        foreach (var h in k.Held)
            foreach (var t in h.Things)
                if (t.Kind == HeldKind.Blade) { blade ??= t; }
        if (blade is null || k.Limbs is not { } limbs) return BareReach;
        var skeleton = assets.Model(k.Model).Skeleton;
        int shoulder = limbs.RightArm.Has ? limbs.RightArm.Upper : -1;
        if (shoulder < 0 || (uint)blade.Bone >= (uint)skeleton.Count) return BareReach;
        var local = new BoneTransform[skeleton.Count];
        var model = new Matrix4x4[skeleton.Count];
        PoseEvaluator.Rest(skeleton, local);
        PoseEvaluator.ToModel(skeleton, local, model);
        float arm = 0f;
        int b = blade.Bone;
        for (int guard = 0; b != shoulder && guard < skeleton.Count; guard++)
        {
            int p = skeleton.Bones[b].Parent;
            if (p < 0 || p >= skeleton.Count) return BareReach;     // the blade's bone is not under the right shoulder
            arm += LimbIK.Length(model[b].Translation - model[p].Translation);
            b = p;
        }
        if (b != shoulder) return BareReach;
        return (arm + LimbIK.Length(blade.Tip)) * k.Scale;
    }
}
