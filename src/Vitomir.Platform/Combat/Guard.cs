// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: 1.1 has no block or parry motions beyond the shield's block (the combat
// survey), no parry, no clash; the guard and the parry here are composed from 1.1's block pose.
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Combat;

/// <summary>
/// The guard's numbers (the rules package's guard).
/// <b>SANDBOX</b> numbers: the enhanced module's <c>Data/combat/guard.json</c> fills them over these defaults (the platform's
/// own, for a world with no module). A service, set at load and read each tick. With <see cref="Enabled"/> false the guard is
/// The earlier build's exactly: attack refused while blocking, no parry window, no clash, a guard met holds at once (the module's switch
/// <c>--guard-off</c>).
/// </summary>
public sealed class GuardSettings
{
    /// <summary>
    /// Whether the earlier build's guard runs (default on): block and swing together (<i>"I favor being able to block and
    /// swing at the same time, so block wouldn't cancel"</i>), the parry window, the clash, the guard contacts marked for the
    /// rules (<see cref="ContactMade.Guard"/>). Off: the earlier build's.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether a rules module decides what a guard met does (<see cref="GuardResolved"/>; set by the module when its guard
    /// runs): a blade that meets a guard is held there until the verdict, the same tick. False (no module): the guard holds
    /// at once, the earlier build's rebound.
    /// </summary>
    public bool RulesDecide { get; set; }

    /// <summary>
    /// The cancel key's return (real s; SANDBOX 0.2): a swing cancelled in its wind-up (a feint) is drawn back to guard over
    /// this, and no new swing begins in it. 0.2 s is the action channel's own fade-out (1.1's fade, a fifth of a second,
    /// <see cref="ActionChannelSettings.FadeOutSeconds"/>): the same time any action takes to give the body back, so a feint
    /// is as quick as the body can let a motion go, and no quicker.
    /// </summary>
    public float CancelReturnSeconds { get; set; } = 0.2f;

    /// <summary>A swing a dodge cancels lets the body go over this (real s; SANDBOX 0.1, the channel's cross-fade), so the dodge's own motion takes it.</summary>
    public float DodgeLetGoSeconds { get; set; } = 0.1f;

    /// <summary>
    /// How long the block's press keeps the guard moving actively to meet a blow (real s; SANDBOX 0.25, of 0.2 to
    /// 0.3): a blade met by the attacker's sweep inside it is a parry for the rules to judge (<see cref="GuardMeet.Parry"/>).
    /// </summary>
    public float ParryWindowSeconds { get; set; } = 0.25f;

    /// <summary>How long after one parry window opens before a press may open another (real s; SANDBOX 0.6): no parry spam.</summary>
    public float ParryRepressSeconds { get; set; } = 0.6f;

    /// <summary>
    /// The guard's arc (degrees either side of the body's facing; SANDBOX 70, <i>"It does need to be
    /// somewhat general though, because looking directly at an incoming attack can be problematic when trying to land one
    /// yourself. Hence why I was somewhat leaning towards how Mount and Blade does it"</i>): while the block is held the guard
    /// turns by itself toward the nearest threatening blade whose blow's line falls within it; a blow from outside it (the far
    /// side, behind) finds the guard at rest and meets what is really in its path, the body.
    /// </summary>
    public float GuardArcDegrees { get; set; } = 70f;

    /// <summary>How far the guard turns the chest toward a threat at most (degrees; SANDBOX 50): the arms and the blade's or the shield's width cover the rest of the arc.</summary>
    public float GuardMostTwistDegrees { get; set; } = 50f;

    /// <summary>How far the guard bends up and down toward a blow's line (degrees; SANDBOX 25 up, the aim offset's most, and 35 down, about the knees for a blow at a sword's reach).</summary>
    public float GuardMostUpDegrees { get; set; } = 25f;
    public float GuardMostDownDegrees { get; set; } = 35f;

    /// <summary>
    /// How fast the held guard moves toward a threat's line (degrees a second; SANDBOX 200). Why: 1.1's quick forehand
    /// (<c>chrisAttackSword</c>) strikes about 0.5 s after it begins and its active window opens about 0.33 s in; at 200°/s
    /// the guard crosses from its rest to the twist's most (50°) in 0.25 s, so a blow seen from its start anywhere in the arc is
    /// met, while one that changes line in its last fifth of a second, or comes from the arc's edge late, can beat it (a
    /// timed block press is a more active kind of defence: the press is faster). The pitch moves at this speed
    /// too, within the aim offset's own rate.
    /// </summary>
    public float GuardDegreesPerSecond { get; set; } = 200f;

    /// <summary>How fast the guard moves during a parry window (degrees a second; SANDBOX 600, three times the held guard's: 50° in under a tenth of a second).</summary>
    public float ParryDegreesPerSecond { get; set; } = 600f;

    /// <summary>
    /// The guard's stance (SANDBOX): the weapon hand moves toward the side a blow comes from — fully at
    /// <see cref="GuardSideDegrees"/> (40) across the line to the foe, where the blade is a beat before it would reach the chest
    /// — and up or down with the height it would reach: level at <see cref="GuardLevelDegrees"/> (−10°: a level blow's line seen
    /// from the chest), fully high or low <see cref="GuardHeightSpanDegrees"/> (25) above or below it. The blade turns from the
    /// block's way toward upright on that side, across above the head for a high blow, down for a low one (Mount &amp; Blade's
    /// four directions, made continuous). Probed (<c>GuardTests.Probe_guard_interception</c>): against the combat rules' forehand and
    /// backhand at five heights, this stance met 3 of 10 blows with the lone sword where the first still guard met 1; a stance
    /// placing the blade across each blow's own swept path met none (1.1's blows arc steeply in their last twentieth of a
    /// second). Real guard motions are the models repository's.
    /// </summary>
    public float GuardSideDegrees { get; set; } = 40f;
    public float GuardLevelDegrees { get; set; } = -10f;
    public float GuardHeightSpanDegrees { get; set; } = 25f;

    /// <summary>How far the weapon hand moves for a full side, a full high and a full low (m; SANDBOX 0.28, 0.35, 0.40). Constants of the composed pose (the evaluator's), as the first composed guard's wrist is.</summary>
    public const float GuardSideMetres = 0.28f, GuardHighMetres = 0.35f, GuardLowMetres = 0.40f;

    /// <summary>A threat the guard turns to is a blade swinging (its wind-up or active window) within this (m, feet to feet; SANDBOX 3.5, contact's reach at most).</summary>
    public float GuardThreatMetres { get; set; } = 3.5f;

    /// <summary>A parried blade springs back this many times farther than a held one (SANDBOX 2: the slight rebound doubled — the blade turned aside).</summary>
    public float ParryReboundScale { get; set; } = 2f;

    /// <summary>Whether two blades both in their active windows that meet clash (on with <see cref="Enabled"/>).</summary>
    public bool Clash { get; set; } = true;

    // ---- the guard's sight ----
    /// <summary>
    /// (The rules package, RECONCILE_WITH_USER_CONTROL: <i>"Limited automatic guard tracking can implement
    /// the recent Mount-and-Blade-like preference if constrained by visible/known threat, player guard intent, facing, finite
    /// turn and authentic geometry. It must not become an omniscient all-angle successful Block"</i>): whether the guard turns only
    /// toward a threat its owner can <b>see</b> — by perception's own test (<see cref="Perception.PerceptionSystem.Clarity"/>:
    /// within its kind's sight cone and sight, the threat's body past cover), as well as within <see cref="GuardArcDegrees"/> and at
    /// <see cref="GuardDegreesPerSecond"/> (default on; the enhanced module's switch <c>guard-sight-off</c> turns it off: the earlier build's
    /// guard, which turned toward any threat in its arc). A blow from a foe it cannot see meets an unturned guard.
    /// </summary>
    public bool SightRequired { get; set; } = true;

    /// <summary>
    /// A development control: whether <b>the player's</b> guard (the <see cref="Control.Controlled"/>
    /// body with a <see cref="Control.CameraRig"/>) sees by the player's <b>view</b> — a fixed cone of
    /// <see cref="PlayerSightDegrees"/> round the look direction (the rig's yaw, free look's orbit and pitch: simulation state a tape
    /// carries; never the field-of-view setting, a presentation setting, so a replay drawn with another field does not diverge),
    /// its apex at the eyes in first person and <see cref="PlayerSightBehindMetres"/> behind them along the look in third person
    /// (where the camera stands), and the threat's body past the level and other bodies from the eyes (perception's line test,
    /// <see cref="Perception.PerceptionNumbers.CoverFloor"/> of its points). True by default; false (the enhanced module's switch
    /// <c>guard-sight-body</c>): the player's guard sees by the body's facing as everyone else does (<see cref="GuardAim.Sees"/>).
    /// The guard's arc still limits where it turns. Everyone else sees by its observer kind.
    /// </summary>
    public bool PlayerSightByView { get; set; } = true;

    /// <summary>
    /// The player's sight cone's half-angle round the look direction (degrees; ENGINE_SANDBOX 55): about what a 16:9 picture shows
    /// across at the default fields of view — first person 80° vertical is 112° across (±56°), third person 75° vertical is ±54°
    /// (<c>Cameras/Cameras.cs</c>' <c>CameraSettings</c>, <c>FirstPersonView.DefaultHorizontalField</c>). A circle, so it reaches as
    /// far up and down (the picture shows ±37.5° to ±40° vertically): a blow from above or below the picture still counts.
    /// </summary>
    public float PlayerSightDegrees { get; set; } = 55f;

    /// <summary>In third person, how far behind the eyes along the look the player's sight cone's apex stands (m; ENGINE_SANDBOX 2.5, the third-person camera's boom, <c>CameraSettings.Boom</c>'s default; the shoulder's offset and the wall's pull left out).</summary>
    public float PlayerSightBehindMetres { get; set; } = 2.5f;
    // ---- end

    // ---- the guard placed by an arm solver (the engine moves the guarding arm
    // itself) and the defender's give. ENGINE_SANDBOX, ours; the enhanced module's guard.json ("arm_solver", "give") fills them.
    // Animation/GuardSolver.cs. ----

    /// <summary>
    /// Whether the arm solver runs (default on): a lone blade's raised guard against a blow its owner sees is driven across
    /// the blow's predicted path (<see cref="Animation.GuardSolverSystem"/>), and a blow met at a guard makes the guarding arm give
    /// and spring back. Off (the enhanced module's switch <c>guard-solver-off</c>): the earlier build's composed guard exactly.
    /// </summary>
    public bool Solver { get; set; } = true;

    /// <summary>
    /// How fast the guarding hand moves toward the intercept (m/s; ENGINE_SANDBOX 2.0 for a held guard, 4.0 in a parry
    /// window). Why: a guard held up and tracking moves the hand about as a fencer's guard drifts with an opponent's blade (1–2 m/s);
    /// a parry is a short snap of the forearm and wrist (3–5 m/s at the hand). A quick forehand's blade arrives about 0.3 s after
    /// it begins, so a held guard moves its hand at most 0.6 m before it arrives; a blow seen late, sped by Celerity, or arriving
    /// far from where the guard stands can beat it.
    /// </summary>
    public float SolverHandMetresPerSecond { get; set; } = 2.0f;
    public float SolverParryHandMetresPerSecond { get; set; } = 4.0f;

    /// <summary>How fast the guarding blade turns to lie across the blow (degrees a second; ENGINE_SANDBOX 360 held, 900 in a parry window: the wrist and forearm's turn).</summary>
    public float SolverBladeDegreesPerSecond { get; set; } = 360f;
    public float SolverParryBladeDegreesPerSecond { get; set; } = 900f;

    /// <summary>The share of the arm's full reach (shoulder to wrist, the two bones) the solver asks of it (ENGINE_SANDBOX 0.95: never quite straight).</summary>
    public float SolverReachShare { get; set; } = 0.95f;

    /// <summary>The part of the guarding blade that meets the blow, from its guard (shares of its length; ENGINE_SANDBOX 0.15 to 0.7: the strong half and the middle, not the tip).</summary>
    public float SolverOwnFrom { get; set; } = 0.15f;
    public float SolverOwnTo { get; set; } = 0.7f;

    /// <summary>
    /// The guarding blade crosses the surface the incoming blade sweeps at no less than this (degrees; ENGINE_SANDBOX 35): it
    /// lies along the arm's line out from the shoulder to the crossing when that crosses the path well enough (so the arm reaches
    /// furthest), else turned toward the path's normal until it does. At 90 it would always lie square across the path (measured:
    /// against a level blow it then stands upright under the crossing, out of the arm's reach before the body).
    /// </summary>
    public float SolverLeastCrossDegrees { get; set; } = 35f;

    /// <summary>The part of the incoming blade the intercept is put on, from its guard to its tip (shares; ENGINE_SANDBOX 0.45 to 1: its outer half, the part that would reach the body).</summary>
    public float SolverIncomingFrom { get; set; } = 0.45f;

    /// <summary>
    /// The intercept stands at least this far from the defender's upright axis (m; ENGINE_SANDBOX 0.25: outside the chest, from
    /// the hips), so the blade is met before the body; and the first place on the blow's path where its outer half comes this near is
    /// the last one tried (measured: without that cut-off, low forehands were given an intercept after they would meet the thigh).
    /// </summary>
    public float SolverClearanceMetres { get; set; } = 0.25f;

    /// <summary>
    /// The shoulder's range for the guarding hand (the wrist, m in the body's frame; ENGINE_SANDBOX): at least
    /// <see cref="SolverHandForward"/> 0.05 before the body's middle, at most <see cref="SolverHandAcross"/> 0.35 across to the
    /// off side, between <see cref="SolverHandLowest"/> 0.6 and <see cref="SolverHandHighest"/> 2.1 above the feet.
    /// </summary>
    public float SolverHandForward { get; set; } = 0.05f;
    public float SolverHandAcross { get; set; } = 0.35f;
    public float SolverHandLowest { get; set; } = 0.6f;
    public float SolverHandHighest { get; set; } = 2.1f;

    /// <summary>
    /// The solver may turn the chest beyond the guard's own twist toward the foe, to bring the sword's shoulder toward the
    /// intercept (degrees each way; ENGINE_SANDBOX 40, at 300 a second held and 600 in a parry window, the guard's own 200 and 600
    /// rounded up for a turn of the shoulders alone). Why: 1.1's block stands the body back (its hips 0.22 m behind the feet's
    /// place) with the sword's shoulder drawn back; from there a right hand cannot reach across to meet a blow coming at the left
    /// side (measured: such blows met the left thigh with the intercept out of reach).
    /// </summary>
    public float SolverTwistMostDegrees { get; set; } = 40f;
    public float SolverTwistDegreesPerSecond { get; set; } = 300f;
    public float SolverParryTwistDegreesPerSecond { get; set; } = 600f;

    /// <summary>
    /// How long a blow must have been under way before the guard reads its line and follows it (s; ENGINE_SANDBOX 0.15, the
    /// duelling brain's own reaction, <see cref="Combat.DuelBrainSettings.ReactSeconds"/>): the solver predicts the blow's whole
    /// path from its first tick, and a guard should not move before the blow shows its line (<i>"It must not become an
    /// omniscient all-angle successful Block"</i>). Measured: it changes little for a guard already up (a blocking dummy's lone rapier
    /// met 96 % of an untrained attacker's blows in the arena with it and without it). The time is the blow's own (its clip time over
    /// its rate): a blow quickened by Celerity is read as late and has less left.
    /// </summary>
    public float SolverReadSeconds { get; set; } = 0.15f;

    /// <summary>With no threat seen, the solved guard fades back into the composed guard over this (s; ENGINE_SANDBOX 0.4).</summary>
    public float SolverReturnSeconds { get; set; } = 0.4f;

    /// <summary>The blow's path is predicted at clip times this far apart through its active window (s of clip time; ENGINE_SANDBOX 1/120, a tick at 1.1's rate), at most 64 places.</summary>
    public float SolverSampleSeconds { get; set; } = 1f / 120f;

    /// <summary>
    /// The give (Bannerlord's developers: <i>"both the shield and arm will absorb the impact and bounce back on that side"</i>,
    /// more for <i>"a cleaner strike or a heavier weapon"</i>): a blow met at the guard (held or parried) pushes the guarding hand
    /// along the blow by <see cref="GiveMetresPerNewtonSecond"/> × its impulse (<see cref="ContactMade.NormalImpulse"/>, N·s), at
    /// least <see cref="GiveLeastMetres"/> and at most <see cref="GiveMostMetres"/>, and it springs back critically damped at
    /// <see cref="GiveHertz"/>; an overpowered guard is knocked aside <see cref="GiveOverpoweredScale"/> times as far (and its
    /// ceiling as much higher). ENGINE_SANDBOX: 0.005 m per N·s (a broadsword met at 10 m/s, 14 N·s, gives 7 cm; at 25 m/s, 35 N·s,
    /// the most, 15 cm), 3 cm least, 15 cm most, ×2.5 overpowered, 2.5 Hz (the furthest at 0.064 s, back within 0.3 s).
    /// </summary>
    public float GiveMetresPerNewtonSecond { get; set; } = 0.005f;
    public float GiveLeastMetres { get; set; } = 0.03f;
    public float GiveMostMetres { get; set; } = 0.15f;
    public float GiveOverpoweredScale { get; set; } = 2.5f;
    public float GiveHertz { get; set; } = 2.5f;
    // ---- end

    // ---- the duelling brain's parry timed by its hand's travel (the finding: its press mostly fell while its own
    // swing kept its guard down, so it rarely opened a window). ENGINE_SANDBOX, ours; guard.json's "brain_parry" fills them. Only while
    // the solver runs (Solver): with guard-solver-off the brain is the earlier build's exactly. ----

    /// <summary>
    /// Whether the duelling brain times its parry by its hand's travel (default on; the module's switch <c>brain-parry-fixed</c>
    /// turns it off: the earlier build's press 0.25 s before the strike mark, whatever its own swing): it chooses to parry a blow only when its own
    /// swing (and stun) will be over in time to raise the guard and get its hand to the blow, holds its own next blow while the parry
    /// waits, and presses so the window opens when the hand can just reach the blade (<see cref="Combat.DuelBrainSystem"/>).
    /// </summary>
    public bool BrainParryByTravel { get; set; } = true;

    /// <summary>
    /// How far the guarding hand travels from the ready stance to a blow's intercept, for the brain's press (m; ENGINE_SANDBOX 0.8,
    /// measured in a duel: the windows that opened left the hand about 0.2 s at 4 m/s from the ready stance to the
    /// intercept). The press comes this over the hand's parry speed (<see cref="SolverParryHandMetresPerSecond"/> × the fighter's
    /// <see cref="GuardSkill.Hand"/>) plus <see cref="BrainParryMarginSeconds"/> before the blow's strike mark — never so early that the
    /// window (<see cref="ParryWindowSeconds"/>) closes before it.
    /// </summary>
    public float BrainParryTravelMetres { get; set; } = 0.8f;

    /// <summary>The margin the brain keeps on its press (s; ENGINE_SANDBOX 0.03, a little under two ticks).</summary>
    public float BrainParryMarginSeconds { get; set; } = 0.03f;

    /// <summary>
    /// About how long before a blow's strike mark its blade reaches a guard's intercept (s; ENGINE_SANDBOX 0.05, from the earlier build's
    /// coverage: the best press was 0.25 s before the mark with the hand 0.2 s from the intercept). At the combat rules' trained guard's sheet the
    /// press's lead is then the window's 0.25 s, the earlier build's own <see cref="DuelBrainSettings.ParryLeadSeconds"/>.
    /// </summary>
    public float BrainParryArriveSeconds { get; set; } = 0.05f;
    // ---- end

    // ---- the shield read — a skilled attacker aims where a shield is not ----
    /// <summary>
    /// The least room (m) between the top of a foe's shield, as it is placed now, and the top of his body (his posed hit
    /// volumes) for a blow that reads the shield (<see cref="GuardSkill.ShieldRead"/>) to be aimed over it (ENGINE_SANDBOX, the
    /// module's <c>guard.json</c> <c>shield_read</c>): a head and neck's worth (0.15), so a shield held at the chin is not read as
    /// leaving room.
    /// </summary>
    public float ShieldReadOverMetres { get; set; } = 0.15f;

    /// <summary>Where in that room the blow is aimed: this share of it above the shield's top (0.5, its middle: the head).</summary>
    public float ShieldReadOverShare { get; set; } = 0.5f;

    /// <summary>
    /// The least room (m) under a foe's shield's bottom, above the lowest height a blow is aimed at (<see cref="SwingAim.Lowest"/>),
    /// for a read blow to be aimed under it when there is no room over it (ENGINE_SANDBOX 0.25: a thigh's worth).
    /// </summary>
    public float ShieldReadUnderMetres { get; set; } = 0.25f;
    // ---- end
}

// ---- the guard by skill ("it could be a function of an actor's stats and how capable they
// are at the various kinds of combat. So a high melee ability person would block more often"; no stamina pool) ----

/// <summary>
/// How well and how often a person guards: factors over the earlier build's guard (1 is the earlier build's: the combat rules' trained
/// guard's sheet), set by a rules module from the person's Dexterity and Melee (Brawl bare-handed) — the enhanced module's curve,
/// <c>guard.json</c>'s <c>skill</c> — and read by the guard's arm solver and the duelling brain:
/// <list type="bullet">
/// <item><see cref="Hand"/>: the solver's hand speed, blade turn and chest turn (held and in a parry window) times this (and times
/// Celerity's attack factor while it is live: the hand speeds as the swing does);</item>
/// <item><see cref="Read"/>: the time a blow must have been under way before the guard follows it
/// (<see cref="GuardSettings.SolverReadSeconds"/>) times this;</item>
/// <item><see cref="Guard"/> and <see cref="Parry"/>: the duelling brain's chances of raising its guard against a blow and of
/// parrying it (its <see cref="DuelBrainSettings.BlockChance"/> and <see cref="DuelBrainSettings.ParryChance"/>) times these, at most
/// 1;</item>
/// <item><see cref="React"/>: the brain's reaction (<see cref="DuelBrainSettings.ReactSeconds"/>) times this;</item>
/// <item><see cref="Error"/>: the brain's parry press is off its time by up to this either way (s; drawn): late or early.</item>
/// </list>
/// <b>Absent means the earlier build's guard</b> (every factor 1, no error), and so does a factor of 0 or less (<see cref="GuardSkills.Of"/>). The
/// component is registered only by a module that sets it (<see cref="GuardSkills.Register"/>). Plain data, digested.
/// </summary>
public struct GuardSkill
{
    public float Hand, Read, Guard, Parry, React, Error;

    /// <summary>The earlier build's guard: every factor 1, no timing error.</summary>
    public static GuardSkill Normal => new() { Hand = 1f, Read = 1f, Guard = 1f, Parry = 1f, React = 1f, Error = 0f };

    /// <summary>A factor as used: itself above nothing, else 1.</summary>
    public static float Of(float factor) => factor > 0f ? factor : 1f;

    /// <summary>Field for field (no boxing).</summary>
    public readonly bool Same(in GuardSkill o) => Hand == o.Hand && Read == o.Read && Guard == o.Guard && Parry == o.Parry && React == o.React && Error == o.Error
        && untrained == o.untrained && ShieldRead == o.ShieldRead;   // the shield read

    // ---- the blade guard by training ----
    int untrained;

    /// <summary>
    /// Untrained (the module's <c>guard.json</c> <c>skill.blade_guard_least</c>: Melee, Brawl bare-handed, below it): the duelling
    /// brain never raises a blade or its fists to block or parry (its guard and parry chances 0), unless it carries a shield, which
    /// blocks as ever. The player's own guard is not the brain's and is unchanged. False: the earlier build's (everyone guards by skill).
    /// </summary>
    public bool Untrained { readonly get => untrained != 0; set => untrained = value ? 1 : 0; }

    // ---- the shield read ----
    /// <summary>
    /// The chance (0 to 1) that the duelling brain, beginning a blow at a foe who carries a shield, reads the shield and aims
    /// the blow where it is not — over its top, else under its bottom (<see cref="GuardSettings.ShieldReadOverMetres"/>) — drawn per
    /// blow from its own stream <c>combat.duel-brain.shield</c>. Set by the module from the fighter's Melee (Brawl bare-handed);
    /// 0 (the default, and the module's switch <c>shield-read-off</c>): no read, no draw, the earlier build's brain exactly.
    /// </summary>
    public float ShieldRead;
}

/// <summary>The guard's skill registered and looked up.</summary>
public static class GuardSkills
{
    /// <summary>Registers the <see cref="GuardSkill"/> component unless it is already (a module that sets it calls this).</summary>
    public static WorldBuilder Register(WorldBuilder b)
    {
        if (!b.Peek.TryStore<GuardSkill>(out _)) b.Component<GuardSkill>();
        return b;
    }

    /// <summary>A person's guard skill (<see cref="GuardSkill.Normal"/> when the world or the person has none).</summary>
    public static GuardSkill Of(World world, EntityId e)
    {
        if (!world.TryStore<GuardSkill>(out var store)) return GuardSkill.Normal;
        ref var s = ref store.Find(e);
        if (System.Runtime.CompilerServices.Unsafe.IsNullRef(ref s)) return GuardSkill.Normal;
        return new GuardSkill
        {
            Hand = GuardSkill.Of(s.Hand), Read = GuardSkill.Of(s.Read), Guard = GuardSkill.Of(s.Guard), Parry = GuardSkill.Of(s.Parry),
            React = GuardSkill.Of(s.React), Error = MathF.Max(0f, s.Error), Untrained = s.Untrained,
            ShieldRead = Math.Clamp(s.ShieldRead, 0f, 1f),   // the shield read, 0 none
        };
    }
}
// ---- end

/// <summary>A parry window opened: who, when, and the threat it turns to (the yaw from the facing and the pitch, radians; 0, the look's pitch, when none).</summary>
public readonly record struct ParryBegun(EntityId Entity, long Tick, float Yaw, float Pitch, EntityId Threat);

/// <summary>A guard pushed through (the follow-through, on an <see cref="GuardVerdict.Overpowered"/>): whose, by whom, and for how long it cannot be raised (s).</summary>
public readonly record struct GuardBroken(EntityId Who, EntityId By, long Tick, long ContactId, float Seconds);

/// <summary>
/// The guard's pose: the shield arm's layer — the block's raised pose held on the off arm's bones while the weapon arm
/// swings — and the parry's turn of the chest toward the threat. The shield arm is found per kind from the held
/// shield's bone (the arm whose chain holds it, from its upper arm down, with the shoulder bone above it when that holds
/// nothing else), once, and kept on its <see cref="ActionSet"/>. Scalar arithmetic and <see cref="DetMath"/> only.
/// </summary>
public static class GuardLayer
{
    /// <summary>The shield arm's bones of a kind (true for each), or null when it holds no shield; found once and kept.</summary>
    public static bool[]? ArmOf(CharacterAssets assets, int kind)
    {
        if ((uint)kind >= (uint)assets.KindCount) return null;
        var k = assets.Kind(kind);
        if (k.Actions is not { } set) return null;
        if (set.GuardArmKnown) return set.GuardArm;
        set.GuardArmKnown = true;
        int shield = -1;
        foreach (var m in k.Held)
            foreach (var t in m.Things)
                if (t.Kind == HeldKind.Shield && shield < 0) shield = t.Bone;
        if (shield < 0) return null;
        var skeleton = assets.ModelOf(kind).Skeleton;
        var bones = skeleton.Bones;
        int top = -1;
        if (k.Limbs is { } limbs)
        {
            if (limbs.LeftArm.Has && Under(skeleton, shield, limbs.LeftArm.Upper)) top = limbs.LeftArm.Upper;
            else if (limbs.RightArm.Has && Under(skeleton, shield, limbs.RightArm.Upper)) top = limbs.RightArm.Upper;
        }
        if (top < 0)
        {
            // No limb map: up from the shield's bone while the bone above holds only this chain and is not the spine.
            top = shield;
            while (bones[top].Parent is var p && p >= 0 && p < top && Array.IndexOf(set.Spine, p) < 0 && Children(skeleton, p) == 1) top = p;
        }
        else if (bones[top].Parent is var sh && sh >= 0 && sh < top && Array.IndexOf(set.Spine, sh) < 0 && Children(skeleton, sh) == 1) top = sh;   // the shoulder
        var mask = new bool[bones.Length];
        for (int b = 0; b < bones.Length; b++) mask[b] = Under(skeleton, b, top);
        set.GuardArm = mask;
        return mask;
    }

    /// <summary>Whether bone <paramref name="b"/> is <paramref name="top"/> or under it.</summary>
    static bool Under(Assets.Skeleton skeleton, int b, int top)
    {
        var bones = skeleton.Bones;
        for (int i = b, n = 0; i >= 0 && n < bones.Length; i = bones[i].Parent, n++)
        {
            if (i == top) return true;
            if (bones[i].Parent >= i) return false;
        }
        return false;
    }

    static int Children(Assets.Skeleton skeleton, int p)
    {
        int n = 0;
        foreach (var b in skeleton.Bones) if (b.Parent == p) n++;
        return n;
    }

    /// <summary>
    /// The shield arm's layer laid over a pose (the evaluator calls it after the action channel): the guard's clip
    /// (<see cref="CharacterAnimation.GuardClip"/>) at its time, on the shield arm's bones, by the layer's weight. Nothing when
    /// the weight is 0 or the kind holds no shield. <paramref name="scratch"/> is the caller's buffer, the skeleton's length.
    /// </summary>
    public static void Over(CharacterAssets assets, in CharacterAnimation state, Assets.Skeleton skeleton, Span<BoneTransform> result, Span<BoneTransform> scratch)
    {
        if (state.GuardWeight <= 0f || state.GuardClip < 0 || assets.Kind(state.Kind).Actions?.GuardArm is not { } arm) return;
        int n = Math.Min(result.Length, Math.Min(scratch.Length, arm.Length));
        assets.Clip(state.GuardClip).Sample(state.GuardTime, skeleton, scratch[..skeleton.Count]);
        float w = MathF.Min(1f, state.GuardWeight);
        // The arm's top bone held at the block's turn in the model's frame, not its parent's: the swing turns the chest and
        // the hips, and the shield arm turns back against them so the shield keeps facing where the block holds it.
        int top = -1;
        for (int b = 0; b < n && top < 0; b++)
            if (arm[b] && (skeleton.Bones[b].Parent is var p && (p < 0 || p >= n || !arm[p]))) top = b;
        Quaternion want = Quaternion.Identity;
        if (top >= 0)
        {
            var block = LimbIK.ModelRotation(skeleton, scratch, top);
            var parentNow = LimbIK.ModelRotation(skeleton, result, skeleton.Bones[top].Parent);
            want = LimbIK.Normalize(PoseMath.Multiply(LimbIK.Conjugate(parentNow), block));
        }
        for (int b = 0; b < n; b++)
        {
            if (!arm[b]) continue;
            var to = b == top ? scratch[b] with { Rotation = want } : scratch[b];
            result[b] = w >= 1f ? to
                : new BoneTransform(DetMath.Slerp(result[b].Rotation, to.Rotation, w), PoseMath.Lerp(result[b].Translation, to.Translation, w));
        }
    }

    /// <summary>
    /// Turns <paramref name="local"/> by <paramref name="yaw"/> radians about the body's up over <paramref name="spine"/> (left
    /// positive, as yaw is): each bone its share, about the model's +Z as its parent sees it, so the turns add up to one turn
    /// of the chest — the parry's guard turned toward the threat (as <see cref="AimOffset.Apply"/> bends it up and down).
    /// </summary>
    public static void Twist(Assets.Skeleton skeleton, ReadOnlySpan<int> spine, float yaw, Span<BoneTransform> local)
    {
        if (yaw == 0f || spine.Length == 0) return;
        var (s, c) = DetMath.SinCos(yaw / spine.Length * 0.5f);
        for (int i = 0; i < spine.Length; i++)
        {
            int b = spine[i];
            if ((uint)b >= (uint)local.Length) continue;
            int parent = skeleton.Bones[b].Parent;
            var seen = parent >= 0 && parent < b ? LimbIK.Rotate(LimbIK.Conjugate(LimbIK.ModelRotation(skeleton, local, parent)), Vector3.UnitZ) : Vector3.UnitZ;
            seen = LimbIK.Unit(seen);
            var turn = new Quaternion(seen.X * s, seen.Y * s, seen.Z * s, c);
            local[b] = local[b] with { Rotation = LimbIK.Normalize(PoseMath.Multiply(turn, local[b].Rotation)) };
        }
    }

    /// <summary>
    /// The guard's stance: while the block plays, the weapon arm placed by IK (<see cref="LimbIK.Solve"/>) —
    /// its hand moved from where the block holds it toward the side a threat's blade comes from
    /// (<see cref="CharacterAnimation.GuardSide"/>) and up or down to its height (<see cref="CharacterAnimation.GuardHeight"/>), and
    /// the blade turned from the block's way toward upright on that side, across above the head for a high blow, down for a low
    /// one — by the block's weight. Nothing at the stance's rest (both 0) or without a limb map, a weapon hand or a block.
    /// <paramref name="model"/> is scratch.
    /// </summary>
    public static void Aim(Assets.Skeleton skeleton, CharacterKind kind, in CharacterAnimation state, Span<BoneTransform> local, Span<Matrix4x4> model)
    {
        if (state.Action != ActionRole.Block || (state.GuardSide == 0f && state.GuardHeight == 0f) || state.ActionWeight <= 0f) return;
        if (kind.Limbs is not { } limbs || kind.Actions is not { WeaponHand: >= 0 } set) return;
        var arm = limbs.RightArm.Has && Under(skeleton, set.WeaponHand, limbs.RightArm.Upper) ? limbs.RightArm
            : limbs.LeftArm.Has && Under(skeleton, set.WeaponHand, limbs.LeftArm.Upper) ? limbs.LeftArm : Limb.None;
        if (!arm.Has) return;
        PoseEvaluator.ToModel(skeleton, local, model);
        float sd = Math.Clamp(state.GuardSide, -1f, 1f), h = Math.Clamp(state.GuardHeight, -1f, 1f);
        var wrist = model[arm.End].Translation;
        // Model space: +Z up, +Y forward, +X the body's right (the side's positive is the body's left).
        var target = wrist + new Vector3(-GuardSettings.GuardSideMetres * sd, 0f, h >= 0f ? GuardSettings.GuardHighMetres * h : GuardSettings.GuardLowMetres * h);
        Quaternion? turn = null;
        var heldModels = kind.Held;
        for (int mi = 0; mi < heldModels.Count; mi++)
            for (int ti = 0; ti < heldModels[mi].Things.Count; ti++)
            {
                var t = heldModels[mi].Things[ti];
                if (t.Kind != HeldKind.Blade || turn is not null || (uint)t.Bone >= (uint)model.Length) continue;
                var along = LimbIK.Unit(PoseMath.TransformNormal(LimbIK.Unit(t.Tip - t.Guard), model[t.Bone]));
                var upright = LimbIK.Unit(new Vector3(-0.1f * sd, 0.25f, 0.95f));
                var high = LimbIK.Unit(new Vector3(-1f, 0.25f, 0.2f));
                var low = LimbIK.Unit(new Vector3(-0.35f, 0.35f, -0.85f));
                var stance = h >= 0f ? PoseMath.Lerp(upright, high, h) : PoseMath.Lerp(upright, low, -h);
                float most = MathF.Max(MathF.Abs(sd), MathF.Abs(h));
                var want = LimbIK.Unit(PoseMath.Lerp(along, LimbIK.Unit(stance), most));
                turn = DetMath.Slerp(Quaternion.Identity, LimbIK.FromTo(along, want), MathF.Min(1f, state.ActionWeight));
            }
        LimbIK.Solve(skeleton, local, model, arm, target, MathF.Min(1f, state.ActionWeight), new Vector3(0.6f, -0.2f, -0.8f), turnEnd: turn);
    }

    /// <summary>The layer's weight moved toward its target by a tick (the channel's fade-in time either way).</summary>
    public static void Advance(ref CharacterAnimation a, float seconds, float fadeSeconds)
    {
        if (a.GuardWeight == a.GuardTarget) return;
        float step = fadeSeconds > 0f ? seconds / fadeSeconds : 1f;
        a.GuardWeight = a.GuardWeight < a.GuardTarget ? MathF.Min(a.GuardTarget, a.GuardWeight + step) : MathF.Max(a.GuardTarget, a.GuardWeight - step);
    }
}

/// <summary>
/// The guard's eye (Mount &amp; Blade's automatic block direction): the nearest threatening blade
/// and the line of its blow. A threat is another fighter within <see cref="GuardSettings.GuardThreatMetres"/> whose blow plays
/// (its wind-up or its active window: an attack on its action channel, not ending, not held by a stop); the line of its blow
/// is where its blade's middle will be at its <c>Strike</c> mark — its pose evaluated at that clip time with the aim it is
/// bending to (its <see cref="PoseRig.AimTarget"/>), placed where it stands now — or, past the mark, where it is. The answer is
/// the line's yaw from the guard's body's facing and its pitch from the chest (<see cref="AimOffset.Toward"/>); a threat whose
/// line falls outside <see cref="GuardSettings.GuardArcDegrees"/> is passed over. One instance per system (it owns its pose's
/// buffers): nothing allocated per tick once they are made.
/// </summary>
public sealed class GuardAim
{
    readonly PoseEvaluator poser = new();
    Matrix4x4[] model = [], skin = [];
    int rigsKnown;

    /// <summary>
    /// The guard's threat for <paramref name="e"/>: true with the line's <paramref name="yaw"/> (radians from the body's facing,
    /// left positive) and <paramref name="pitch"/> (from the chest, up positive), and who it is.
    /// </summary>
    public bool Threat(World world, CharacterAssets assets, EntityId e, GuardSettings g, AimOffsetSettings aim, out float yaw, out float pitch, out EntityId threat) =>
        Threat(world, assets, e, g, aim, out yaw, out pitch, out _, out threat);

    /// <summary>The threat as <see cref="Threat(World, CharacterAssets, EntityId, GuardSettings, AimOffsetSettings, out float, out float, out EntityId)"/>, with the <paramref name="side"/> its blade comes from (radians across the line to the foe, left positive).</summary>
    public bool Threat(World world, CharacterAssets assets, EntityId e, GuardSettings g, AimOffsetSettings aim, out float yaw, out float pitch, out float side, out EntityId threat) =>
        Threat(world, assets, e, g, aim, out yaw, out pitch, out side, out _, out threat);

    /// <summary>The threat with the height its blow would reach, <paramref name="rise"/> (m above the guard's chest; below negative).</summary>
    public bool Threat(World world, CharacterAssets assets, EntityId e, GuardSettings g, AimOffsetSettings aim, out float yaw, out float pitch, out float side, out float rise, out EntityId threat) =>
        Threat(world, assets, e, g, aim, out yaw, out pitch, out side, out rise, out _, out _, out _, out threat);

    /// <summary>
    /// The threat in full: also where its blade's middle would reach the chest (<paramref name="hit"/>), where it is a beat before
    /// (<paramref name="before"/>) and the way its blade lies then (<paramref name="axis"/>, guard to tip), in the world.
    /// </summary>
    public bool Threat(World world, CharacterAssets assets, EntityId e, GuardSettings g, AimOffsetSettings aim, out float yaw, out float pitch, out float side, out float rise,
        out Vector3 hitPoint, out Vector3 beforePoint, out Vector3 axis, out EntityId threat)
    {
        yaw = pitch = side = rise = 0f;
        hitPoint = beforePoint = axis = default;
        threat = EntityId.None;
        var fighters = world.Store<Control.Combatant>();
        var anims = world.Store<CharacterAnimation>();
        var bodies = world.Store<Bodies.Body>();
        ref var body = ref bodies.Find(e);
        if (System.Runtime.CompilerServices.Unsafe.IsNullRef(ref body)) return false;
        float most = g.GuardThreatMetres * g.GuardThreatMetres, arc = g.GuardArcDegrees * DetMath.DegToRad;
        float best = float.MaxValue;
        var ents = fighters.Entities;
        var data = fighters.Data;
        for (int i = 0; i < ents.Length; i++)
        {
            var f = ents[i];
            if (f == e) continue;
            ref var fa = ref anims.Find(f);
            ref var fb = ref bodies.Find(f);
            if (System.Runtime.CompilerServices.Unsafe.IsNullRef(ref fa) || System.Runtime.CompilerServices.Unsafe.IsNullRef(ref fb)) continue;
            if (!ActionRoles.IsAttack(fa.Action) || fa.ActionEnding || FollowThrough.Held(world, f, data[i].Swing)) continue;
            float dx = fb.Feet.X - body.Feet.X, dy = fb.Feet.Y - body.Feet.Y, d2 = dx * dx + dy * dy;
            if (d2 > most || d2 >= best) continue;
            // Where the blow comes from: the foe's bearing, outside the arc passed over (a blow from the far side or behind).
            float bearing = DetMath.WrapAngle(DetMath.Atan2(dy, dx) - body.Yaw);
            if (MathF.Abs(bearing) > arc) continue;
            if (g.SightRequired && !Sees(world, e, in body, f, g)) continue;   // only a threat it can see
            var chest = body.Feet + new Vector3(0f, 0f, aim.ChestHeight);
            if (!Line(world, assets, f, in fa, in data[i], chest, out var hit, out var before, out var ax)) continue;
            // The side its blade comes from: a beat before it reaches the chest, across the line to the foe, seen from at least
            // the aim offset's least reach (a point at the chest has no bearing of its own).
            float d = MathF.Sqrt(d2);
            float ux = d > 1e-4f ? dx / d : 1f, uy = d > 1e-4f ? dy / d : 0f;
            float rx = before.X - chest.X, ry = before.Y - chest.Y;
            float along = rx * ux + ry * uy, across = ux * ry - uy * rx;
            best = d2;
            yaw = bearing;
            side = DetMath.Atan2(across, MathF.Max(along, aim.LeastReach));
            rise = hit.Z - chest.Z;
            (hitPoint, beforePoint, axis) = (hit, before, ax);
            pitch = AimOffset.Toward(body.Feet, hit, aim);
            threat = f;
        }
        return !threat.IsNone;
    }

    // ---- the guard turns only toward a threat its owner sees ----

    /// <summary>
    /// Whether <paramref name="e"/> (its body <paramref name="body"/>) can see <paramref name="threat"/> now, by perception's own
    /// test: <see cref="Perception.PerceptionSystem.Clarity"/> above nothing from its eyes
    /// (<see cref="Bodies.Locomotion.EyeHeight"/> over its feet) along its facing, with the kind of observer it is — its
    /// <see cref="Perception.Observer"/>'s, else the world's default kind (<see cref="Perception.PerceptionConfig.DefaultKind"/>:
    /// the arena's fighters and the player are no observers) — <i>tracking</i> (a foe it is engaged with is watched: its motion
    /// counts at least as a run's, so it is not lost for standing still to swing). Its cone, its sight in the light there, and
    /// the threat's body past the level and every other body (<see cref="Perception.PerceptionNumbers.CoverFloor"/> of its points
    /// in clear sight). With no kind to see by, the cone of an ordinary observer (<see cref="Perception.ObserverNumbers.Ordinary"/>)
    /// and the same line test, without the light. A world without perception (no kinds, no level) sees everything: the earlier build's guard.
    /// Every tick (not on perception's look ticks): the guard is the body's own and moves each tick. Allocates nothing.
    /// </summary>
    public static bool Sees(World world, EntityId e, in Bodies.Body body, EntityId threat, GuardSettings? g = null)
    {
        // The player's guard sees by the player's view (PlayerSightByView).
        if ((g?.PlayerSightByView ?? true) && world.TryStore<Control.Controlled>(out var tags) && tags.Has(e)
            && world.TryStore<Control.CameraRig>(out var rigs) && rigs.Has(e))
            return SeesByView(world, e, in body, threat, in rigs.Get(e), g ?? DefaultSettings);
        if (!world.TryService<Perception.PerceptionConfig>(out var config) || config.Kinds.Count == 0) return true;
        if (!world.TryService<Perception.PerceptionState>(out var state) || !world.TryService<Space.LevelSpace>(out var level)) return true;
        if (!world.TryStore<Bodies.Locomotion>(out var locos)) return true;
        ref var loco = ref locos.Find(e);
        if (System.Runtime.CompilerServices.Unsafe.IsNullRef(ref loco)) return true;
        var eyes = body.Feet + new Vector3(0f, 0f, loco.EyeHeight);
        var (sin, cos) = DetMath.SinCos(body.Yaw);
        var facing = new Vector3(cos, sin, 0f);
        var bodies = world.Store<Bodies.Body>();
        int kind = KindOf(world, config, e);
        if (kind >= 0)
            return Perception.PerceptionSystem.Clarity(config, state.Areas, level.Collision, bodies, locos, kind, eyes, facing, threat, e, tracking: true) > 0f;
        // No kind: an ordinary observer's cone, and the threat's body past cover.
        ref var tb = ref bodies.Find(threat);
        ref var tl = ref locos.Find(threat);
        if (System.Runtime.CompilerServices.Unsafe.IsNullRef(ref tb) || System.Runtime.CompilerServices.Unsafe.IsNullRef(ref tl)) return false;
        float height = tb.CapsuleHeight(tl);
        var to = tb.Feet + new Vector3(0f, 0f, height * Perception.PerceptionSystem.BodyPoints[1]) - eyes;
        float d = to.Length();
        if (d > 1e-3f && Vector3.Dot(facing, to) < DetMath.Cos(Perception.ObserverNumbers.Ordinary.ConeDegrees * DetMath.DegToRad) * d) return false;
        int clear = 0;
        for (int p = 0; p < Perception.PerceptionSystem.BodyPoints.Length; p++)
            if (Perception.PerceptionSystem.Clear(level.Collision, bodies, locos, eyes, tb.Feet + new Vector3(0f, 0f, height * Perception.PerceptionSystem.BodyPoints[p]), e, threat)) clear++;
        return clear / (float)Perception.PerceptionSystem.BodyPoints.Length >= config.Numbers.CoverFloor;
    }

    static readonly GuardSettings DefaultSettings = new();

    /// <summary>
    /// A development control: whether the player's body <paramref name="e"/> sees <paramref name="threat"/> by its
    /// view (<see cref="GuardSettings.PlayerSightByView"/>): the threat's chest within <see cref="GuardSettings.PlayerSightDegrees"/>
    /// of the look direction (<paramref name="rig"/>'s yaw plus free look's orbit, and its pitch), seen from the eyes in first person
    /// and from <see cref="GuardSettings.PlayerSightBehindMetres"/> behind them in third; and its body past cover from the eyes
    /// (perception's line test, <see cref="Perception.PerceptionSystem.Clear"/>, at least the cover floor of its points; with no
    /// level, nothing hides it). No light, no range: a threat is within the guard's reach. Allocates nothing.
    /// </summary>
    public static bool SeesByView(World world, EntityId e, in Bodies.Body body, EntityId threat, in Control.CameraRig rig, GuardSettings g)
    {
        if (!world.TryStore<Bodies.Locomotion>(out var locos)) return true;
        ref var loco = ref locos.Find(e);
        var bodies = world.Store<Bodies.Body>();
        ref var tb = ref bodies.Find(threat);
        if (System.Runtime.CompilerServices.Unsafe.IsNullRef(ref loco) || System.Runtime.CompilerServices.Unsafe.IsNullRef(ref tb)) return true;
        ref var tl = ref locos.Find(threat);
        if (System.Runtime.CompilerServices.Unsafe.IsNullRef(ref tl)) return true;
        float yaw = rig.Orbit == 0f ? rig.Yaw : DetMath.WrapAngle(rig.Yaw + rig.Orbit);
        var look = DetMath.Direction(yaw, rig.Pitch);
        var eyes = body.Feet + new Vector3(0f, 0f, loco.EyeHeight);
        var apex = rig.View == Control.ViewKind.First ? eyes : eyes - look * g.PlayerSightBehindMetres;
        float height = tb.CapsuleHeight(tl);
        var to = tb.Feet + new Vector3(0f, 0f, height * Perception.PerceptionSystem.BodyPoints[1]) - apex;
        float d = to.Length();
        if (d > 1e-3f && Vector3.Dot(look, to) < DetMath.Cos(g.PlayerSightDegrees * DetMath.DegToRad) * d) return false;
        if (!world.TryService<Space.LevelSpace>(out var level)) return true;
        float floor = world.TryService<Perception.PerceptionConfig>(out var config) ? config.Numbers.CoverFloor : Perception.PerceptionNumbers.Sandbox.CoverFloor;
        int clear = 0;
        for (int p = 0; p < Perception.PerceptionSystem.BodyPoints.Length; p++)
            if (Perception.PerceptionSystem.Clear(level.Collision, bodies, locos, eyes, tb.Feet + new Vector3(0f, 0f, height * Perception.PerceptionSystem.BodyPoints[p]), e, threat)) clear++;
        return clear / (float)Perception.PerceptionSystem.BodyPoints.Length >= floor;
    }

    /// <summary>The kind of observer <paramref name="e"/> sees as: its <see cref="Perception.Observer"/>'s, else the world's default kind, else −1.</summary>
    static int KindOf(World world, Perception.PerceptionConfig config, EntityId e)
    {
        if (world.TryStore<Perception.Observer>(out var observers))
        {
            ref var o = ref observers.Find(e);
            if (!System.Runtime.CompilerServices.Unsafe.IsNullRef(ref o) && o.Kind >= 0 && o.Kind < config.Kinds.Count) return o.Kind;
        }
        return config.DefaultKind is { } d ? config.IndexOf(d) : -1;
    }
    // ---- end

    /// <summary>
    /// The line of <paramref name="f"/>'s blow: of its blade's middle at four times from its active window's start (or now, if
    /// later) to its <c>Strike</c> mark — its pose evaluated at each with the aim it is bending to, placed where it stands now —
    /// the one nearest the guard's chest (where the blow would reach it). Probed: 1.1's forehand passes its strike mark
    /// after crossing its target's front, so the mark's point alone set the guard on the side the blade leaves, not the side it
    /// comes from.
    /// </summary>
    bool Line(World world, CharacterAssets assets, EntityId f, in CharacterAnimation anim, in Control.Combatant c, Vector3 chest, out Vector3 point, out Vector3 before, out Vector3 axis)
    {
        point = before = axis = default;
        if (!HeldItems.Placement(world, f, out var place)) return false;
        int bones = assets.ModelOf(anim.Kind).Skeleton.Count;
        if (model.Length < bones) { model = new Matrix4x4[bones]; skin = new Matrix4x4[bones]; }
        float strike = c.StrikeAt >= 0f ? c.StrikeAt : anim.ActionTime;
        float from = strike;
        if (world.TryService<ActiveWindows>(out var windows) && world.TryService<ContactConfig>(out var config))
        {
            var w = windows.For(assets, anim.Kind, anim.ActionClip, c.StrikeAt, config.Window);
            if (w.Has) from = MathF.Min(w.From, strike);
        }
        from = MathF.Max(from, anim.ActionTime);
        if (strike < from) strike = from;
        var rigs = PoseRigSystem.Optional<PoseRig>(world, ref rigsKnown);
        bool hasRig = rigs is not null && rigs.Has(f);
        var rig = hasRig ? rigs!.Get(f) : default;
        rig.AimPitch = rig.AimTarget;
        float best = float.MaxValue;
        Vector3 last = Vector3.Zero, lastAxis = Vector3.UnitZ;
        for (int k = 0; k < 4; k++)
        {
            var state = anim;
            state.ActionTime = from + (strike - from) * k / 3f;
            state.ActionWeight = 1f;
            state.ActionFrom = ActionRole.None;
            state.ActionBlend = 1f;
            if (hasRig) poser.Evaluate(assets, state, rig, model.AsSpan(0, bones), skin.AsSpan(0, bones));
            else poser.Evaluate(assets, state, model.AsSpan(0, bones), skin.AsSpan(0, bones));
            if (!Middle(assets, anim.Kind, bones, place, out var p, out var ax)) return false;
            float d = LimbIK.Length(p - chest);
            if (d < best) { best = d; before = k == 0 ? p : last; axis = k == 0 ? ax : lastAxis; point = p; }
            last = p;
            lastAxis = ax;
        }
        return true;
    }

    /// <summary>The blade's middle (bare-handed, the weapon hand) in the world for the pose in <see cref="model"/>.</summary>
    bool Middle(CharacterAssets assets, int kind, int bones, Matrix4x4 place, out Vector3 point, out Vector3 axis)
    {
        point = default;
        axis = Vector3.UnitZ;
        var held = assets.Kind(kind).Held;
        for (int m = 0; m < held.Count; m++)
            for (int t = 0; t < held[m].Things.Count; t++)
            {
                var thing = held[m].Things[t];
                if (thing.Kind != HeldKind.Blade || (uint)thing.Bone >= (uint)bones) continue;
                var mtx = PoseMath.Multiply(model[thing.Bone], place);
                point = PoseMath.Transform((thing.Guard + thing.Tip) * 0.5f, mtx);
                axis = PoseMath.Transform(thing.Tip, mtx) - PoseMath.Transform(thing.Guard, mtx);
                return true;
            }
        if (assets.Kind(kind).Actions is not { WeaponHand: >= 0 } set || set.WeaponHand >= bones) return false;
        point = PoseMath.Multiply(model[set.WeaponHand], place).Translation;
        return true;
    }

    /// <summary>A value moved toward a target by at most a step.</summary>
    public static float Towards(float from, float to, float step) => from < to ? MathF.Min(to, from + step) : MathF.Max(to, from - step);
}
