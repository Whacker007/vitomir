// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Runtime.CompilerServices;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Combat;

// ---- the duelling brain's decisions as events ("Distinguish knowledge/reaction from
// physical execution"; "Inputs, AI-known evidence, chosen vs executed actions"). Written only into a world that asks for them
// (BrainDecisions.Enable: the scenario runner's traced runs); never digested, and the brain's choices are the same with or without
// them (every draw is made in the same order from the same streams; the receipt only copies what was drawn). ----

/// <summary>What a duelling brain chose (<see cref="BrainDecided.Choice"/>).</summary>
public enum BrainChoice : byte
{
    /// <summary>Its foe: the one given it (the scenario's or the arena's <c>foe</c>, the world's fight), or another its side's chooser picked (<see cref="DuelSides"/>).</summary>
    Target = 0,
    /// <summary>A blow begun: its direction, strength and aimed height (<see cref="BrainDecided.Direction"/>, <see cref="BrainDecided.Strong"/>, <see cref="BrainDecided.AimPitchDegrees"/>).</summary>
    Attack = 1,
    /// <summary>A block to be raised after its reaction (<see cref="BrainDecided.Seconds"/>), against the foe's blow.</summary>
    Block = 2,
    /// <summary>A parry: the block's press timed to the foe's blow.</summary>
    Parry = 3,
    /// <summary>A dodge, asked after its reaction (<see cref="BrainDecided.Seconds"/>).</summary>
    Dodge = 4,
    /// <summary>The foe's blow seen and no defence chosen (every draw said no, or the blow began out of its reach).</summary>
    NoDefence = 5,
    /// <summary>A step back after its own blow (<see cref="BrainDecided.Seconds"/>: how long it walks back).</summary>
    BackOff = 6,
    /// <summary>Closing on its foe (run or walk).</summary>
    Close = 7,
    /// <summary>A pause after its run of blows (the pace; <see cref="BrainDecided.Seconds"/> its length), circling its foe at its blade's distance.</summary>
    Pause = 8,
    /// <summary>A pause cut short: its foe opened (staggered, stunned or its guard broken) and the riposte's draw said yes.</summary>
    Riposte = 9,
    /// <summary>A step back because its foe is too close.</summary>
    StepBack = 10,
    /// <summary>Holding off after a dodge while the blow it dodged still plays.</summary>
    HoldOff = 11,
    /// <summary>Standing: in its striking range, or waiting.</summary>
    Stand = 12,
    /// <summary>1.1's own approach (the szlachta's; <see cref="DuelOneOnes"/>) beyond its striking range.</summary>
    Approach = 13,
    /// <summary>A quick slot pressed (<see cref="BrainDecided.Slot"/>, <see cref="BrainDecided.Power"/>).</summary>
    Power = 14,
    /// <summary>A block held always (the arena's blocking dummy).</summary>
    HoldBlock = 15,
}

/// <summary>The rule that fired (<see cref="BrainDecided.Rule"/>).</summary>
public enum BrainRule : byte
{
    /// <summary>Its foe as given (the scenario, the arena, the world's fight): the brain never picks one itself.</summary>
    FoeGiven = 0,
    /// <summary>Its side's chooser (<see cref="DuelSides"/>): the nearest foe still up, nearer than the present one by its margin, or the present one down.</summary>
    NearestFoe = 1,
    /// <summary>Its foe changed by something else (a script, the world's fight).</summary>
    FoeChanged = 2,
    /// <summary>The foe began a blow within its reach: the dodge's, the parry's and the block's draws, in that order.</summary>
    FoeBlowInReach = 3,
    /// <summary>The foe began a blow out of its reach: nothing drawn.</summary>
    FoeBlowOutOfReach = 4,
    /// <summary>A parry waiting on a blow held at its ready point, given up after its time (a block drawn in its place).</summary>
    HeldBlowParryGivenUp = 5,
    /// <summary>The foe let go a blow it held: the parry drawn again.</summary>
    FoeReleasedBlow = 6,
    /// <summary>In its striking range, free (no blow, block, back-off, dodge, parry or pause of its own pending).</summary>
    InStrikingRange = 7,
    /// <summary>Its range keeping said strike (a blade fighter out of a clinch).</summary>
    RangeKeeping = 8,
    /// <summary>Its own blow ended: the back-off's draw.</summary>
    OwnBlowEnded = 9,
    /// <summary>Its run of blows ended (the pace).</summary>
    RunEnded = 10,
    /// <summary>Its foe opened during a pause: the riposte's draw.</summary>
    FoeOpened = 11,
    /// <summary>Its feet: where it goes, by its distance from its foe (closing, too close, holding off, circling, standing).</summary>
    Distance = 12,
    /// <summary>A quick slot for when hurt: its Health below its share of the maximum.</summary>
    Hurt = 13,
    /// <summary>A quick slot for a fight: its foe within its share of the reach.</summary>
    Engaged = 14,
    /// <summary>A live power renewed: less than its renewal seconds left.</summary>
    Renewal = 15,
    /// <summary>The blocking dummy's rule: always.</summary>
    Always = 16,
}

/// <summary>Where the foe's blow was when the brain decided.</summary>
public enum BrainFoePhase : byte
{
    /// <summary>No blow of the foe's playing.</summary>
    None = 0,
    /// <summary>Its wind-up, before its strike mark.</summary>
    WindUp = 1,
    /// <summary>Held at its ready point (a blow held, not yet let go).</summary>
    Holding = 2,
    /// <summary>At or past its strike mark (its active part and recovery).</summary>
    Striking = 3,
    /// <summary>Stunned (its blade stopped at a guard) or its guard broken.</summary>
    Stunned = 4,
}

/// <summary>
/// One decision of a duelling brain that starts something : <b>what it chose</b>
/// (<paramref name="Choice"/>), <b>what it saw</b> (its foe <paramref name="Target"/>, the distance between their feet, its own reach
/// and its foe's, the foe's swing and where that blow was, its own swing, whether it was busy, blocking, stunned or pausing) and
/// <b>why</b> (<paramref name="Rule"/>; the deciding draw and the threshold it was drawn against — a draw below the threshold says
/// yes — and, for a defence, each of the three draws: dodge, parry, block; −1 for a draw not made). Published in
/// <see cref="Phase.Think"/> by <see cref="DuelBrainSystem"/> (and <see cref="DuelSideSystem"/> for a foe it changes) only into a world
/// with <see cref="BrainDecisions.Enable"/>. What it caused (its swing, its block raised, its parry, its dodge, its power) is linked
/// by the scenario stream (<c>parentEventId</c> of the caused event; <c>derived.caused</c> and <c>derived.executed</c> on this one).
/// </summary>
/// <param name="Seconds">A block's or dodge's reaction, a back-off's or a pause's length (s); 0 for none.</param>
/// <param name="Range">The range keeping's verdict (0 none, 1 hold, 2 strike).</param>
/// <param name="FormerTarget">For <see cref="BrainChoice.Target"/>: the foe it had (none for the first).</param>
/// <param name="FormerDistance">For <see cref="BrainChoice.Target"/>: the distance to the foe it had (m; −1 when none or down).</param>
/// <param name="Repeats">For <see cref="BrainChoice.Attack"/>: the same choice made again since the last one told while no blow of its own
/// began (its attack muted, or refused): told at most once a second (<see cref="BrainDecisions.RepeatSeconds"/>), with this count.</param>
public readonly record struct BrainDecided(EntityId Entity, long Tick, BrainChoice Choice, BrainRule Rule, EntityId Target,
    float Distance, float Reach, float FoeReach, int FoeSwing, BrainFoePhase FoePhase,
    int Swing, bool Busy, bool Blocking, float Stun, float PauseLeft,
    float Draw = -1f, float Threshold = -1f,
    float DodgeDraw = -1f, float DodgeChance = -1f, float ParryDraw = -1f, float ParryChance = -1f, float BlockDraw = -1f, float BlockChance = -1f,
    bool ParryFreeInTime = true, float Seconds = 0f, AttackDirection Direction = AttackDirection.None, bool Strong = false,
    float AimPitchDegrees = 0f, int Slot = 0, string? Power = null, int Range = 0,
    EntityId FormerTarget = default, float FormerDistance = -1f, int Repeats = 0);

/// <summary>
/// The brains' decisions' service: present only in a world that asks for decisions (<see cref="Enable"/>), with
/// what each brain last chose for its feet and its foe (so a change is told once). Not digested: nothing the world does reads it.
/// </summary>
public sealed class BrainDecisions
{
    readonly EventQueue<BrainDecided> queue;
    record struct Last(BrainChoice Feet, EntityId Foe, bool Held);
    readonly Dictionary<EntityId, Last> last = [];
    readonly Dictionary<EntityId, (int Swing, long Tick, int Repeats)> attacks = [];

    /// <summary>An attack chosen again while no blow of its own began since the last one told is told at most this often (s), with its repeats.</summary>
    public const float RepeatSeconds = 1f;

    /// <summary>Publishes an attack decision unless it repeats one told less than <see cref="RepeatSeconds"/> ago with no blow begun since (then counted).</summary>
    public void Attack(in BrainDecided d)
    {
        if (attacks.TryGetValue(d.Entity, out var a) && a.Swing == d.Swing && d.Tick - a.Tick < (long)(RepeatSeconds * World.TicksPerSecond))
        {
            attacks[d.Entity] = a with { Repeats = a.Repeats + 1 };
            return;
        }
        int repeats = a.Swing == d.Swing && attacks.ContainsKey(d.Entity) ? a.Repeats : 0;
        attacks[d.Entity] = (d.Swing, d.Tick, 0);
        Publish(d with { Repeats = repeats });
    }

    BrainDecisions(EventQueue<BrainDecided> queue) => this.queue = queue;

    /// <summary>
    /// Asks the world's duelling brains to publish their decisions (<see cref="BrainDecided"/>): registers the event's queue and this
    /// service. Once, after the world is built (a second call does nothing). Changes nothing the brains do.
    /// </summary>
    public static void Enable(World world)
    {
        if (world.TryService<BrainDecisions>(out _)) return;
        if (!world.TryService<EventQueue<BrainDecided>>(out var q))
        {
            q = new EventQueue<BrainDecided>();
            world.AddService(q);
        }
        world.AddService(new BrainDecisions(q));
    }

    /// <summary>Whether this world's brains publish their decisions.</summary>
    public static bool On(World world) => world.TryService<BrainDecisions>(out _);

    /// <summary>What a brain saw this tick (built once a brain a tick, only when decisions are on).</summary>
    public static BrainDecided Seen(World world, EntityId e, in DuelBrain brain, in Combatant c, ref Combatant foeC, Store<CharacterAnimation> anims, float distance) =>
        new(e, world.Tick, BrainChoice.Stand, BrainRule.Distance, brain.Foe, distance, brain.Reach, brain.FoeReach,
            Unsafe.IsNullRef(ref foeC) ? 0 : foeC.Swing, PhaseOf(anims, brain.Foe, ref foeC), c.Swing, c.Busy > 0f, c.Blocking, c.Stun, brain.PauseLeft,
            FormerTarget: EntityId.None);

    /// <summary>Where the foe's blow is (for the receipt).</summary>
    public static BrainFoePhase PhaseOf(Store<CharacterAnimation> anims, EntityId foe, ref Combatant foeC)
    {
        if (Unsafe.IsNullRef(ref foeC)) return BrainFoePhase.None;
        if (foeC.Stun > 0f || foeC.GuardBroken > 0f) return BrainFoePhase.Stunned;
        ref var fa = ref anims.Find(foe);
        if (Unsafe.IsNullRef(ref fa) || !ActionRoles.IsAttack(fa.Action) || foeC.Busy <= 0f) return BrainFoePhase.None;
        if (foeC.Readied && ActionChannel.AtReady(fa)) return BrainFoePhase.Holding;
        return foeC.StrikeAt >= 0f && fa.ActionTime < foeC.StrikeAt ? BrainFoePhase.WindUp : BrainFoePhase.Striking;
    }

    /// <summary>Publishes a decision.</summary>
    public void Publish(in BrainDecided d) => queue.Publish(in d);

    /// <summary>Publishes a choice for its feet when it differs from the last one told (closing, standing, …).</summary>
    public void Feet(in BrainDecided seen, BrainChoice feet)
    {
        var was = last.TryGetValue(seen.Entity, out var l) ? l : new Last(BrainChoice.Stand, EntityId.None, false);
        if (last.ContainsKey(seen.Entity) && was.Feet == feet) return;
        last[seen.Entity] = was with { Feet = feet };
        // A back-off and a pause are told by their own draws (OwnBlowEnded, RunEnded); their walking is not told again.
        if (feet is BrainChoice.BackOff or BrainChoice.Pause) return;
        Publish(seen with { Choice = feet, Rule = BrainRule.Distance });
    }

    /// <summary>Publishes its foe when it is first seen or has changed since (<paramref name="rule"/>: why).</summary>
    public void Foe(in BrainDecided seen, BrainRule rule, float formerDistance = -1f)
    {
        var was = last.TryGetValue(seen.Entity, out var l) ? l : new Last((BrainChoice)255, EntityId.None, false);
        if (last.ContainsKey(seen.Entity) && was.Foe == seen.Target) return;
        last[seen.Entity] = was with { Foe = seen.Target };
        var why = was.Foe.IsNone && rule == BrainRule.FoeChanged ? BrainRule.FoeGiven : rule;
        Publish(seen with { Choice = BrainChoice.Target, Rule = why, FormerTarget = was.Foe, FormerDistance = formerDistance });
    }

    /// <summary>Whether the blocking dummy's held block has been told (it is told once).</summary>
    public bool HeldOnce(EntityId e)
    {
        var was = last.TryGetValue(e, out var l) ? l : new Last((BrainChoice)255, EntityId.None, false);
        if (was.Held) return false;
        last[e] = was with { Held = true };
        return true;
    }
}
