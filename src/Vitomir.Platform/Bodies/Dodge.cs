// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Runtime.CompilerServices;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Bodies;

/// <summary>
/// <b>A dodge asked for</b> (ours): Dodge pressed this tick (a press, not a hold) and the level way to go (a unit
/// vector in world X and Y; zero for straight back from the body's facing). The player's control writes it from the Dodge
/// button and the move keys (<see cref="PlayerControlSystem"/>), a brain from its own choice (<see cref="Combat.DuelBrainSystem"/>):
/// the same intent, the same rules. Put on a body at its first press, so a world where nobody dodges is unchanged; taken and
/// cleared by <see cref="DodgeSystem"/> on the tick it is read. Plain data.
/// </summary>
public struct DodgeIntent
{
    int pressed;

    /// <summary>Dodge pressed this tick.</summary>
    public bool Pressed { readonly get => pressed != 0; set => pressed = value ? 1 : 0; }

    /// <summary>The level way wanted (unit; zero: back from the facing).</summary>
    public Vector2 Way;
}

/// <summary>Where a dodge is: none, the start (the weight shifted before the push), the burst (the body moving), the recovery (balance found again).</summary>
public enum DodgePhase : byte
{
    None = 0,
    Start = 1,
    Burst = 2,
    Recover = 3,
}

/// <summary>
/// <b>A body's dodge</b> (digested, plain data): its phase and the time spent in it, the level way, this dodge's own
/// numbers fixed as it began (the start, the burst and the recovery, s; the burst's speed, m/s; the distance wanted, m),
/// where the feet were as it began and the tick, how far the burst has carried the body so far (m), and the dodges begun.
/// Put on a body by its first dodge; <see cref="DodgePhase.None"/> between dodges.
/// </summary>
public struct Dodging
{
    public DodgePhase Phase;
    public float Seconds;
    public Vector2 Way;
    public float Ready, Move, Recover, Speed, Distance;
    public Vector3 From;
    public long Began;
    public float Moved;
    public int Count;

    /// <summary>Whether a dodge is under way (its start, burst or recovery).</summary>
    public readonly bool Active => Phase != DodgePhase.None;

    /// <summary>Whether the body is moving in it (the burst).</summary>
    public readonly bool Bursting => Phase == DodgePhase.Burst;

    /// <summary>The dodge's whole length (s).</summary>
    public readonly float Total => Ready + Move + Recover;

    /// <summary>The time since it began (s).</summary>
    public readonly float Elapsed => Phase switch
    {
        DodgePhase.Start => Seconds,
        DodgePhase.Burst => Ready + Seconds,
        DodgePhase.Recover => Ready + Move + Seconds,
        _ => 0f,
    };

    /// <summary>The time left until it ends (s; 0 when none).</summary>
    public readonly float Left => Active ? MathF.Max(0f, Total - Elapsed) : 0f;

    /// <summary>
    /// Where the dodge's motion is drawn (0 to 1; <see cref="BodyMotionSystem"/>): the start and the burst take the motion up to
    /// <paramref name="low"/> (its lowest, furthest pose), the recovery the rest, so the pose is out when the body is.
    /// </summary>
    public readonly float Progress(float low)
    {
        float r = Ready + Move;
        return Phase switch
        {
            DodgePhase.Start or DodgePhase.Burst => r > 0f ? Math.Clamp(Elapsed / r, 0f, 1f) * low : low,
            DodgePhase.Recover => Recover > 0f ? low + (1f - low) * Math.Clamp(Seconds / Recover, 0f, 1f) : 1f,
            _ => 0f,
        };
    }
}

/// <summary>
/// <b>A body's own dodge numbers</b>, written by a rules module from what the body is (the enhanced module: the Dodge
/// Ability shortens the start and the recovery, Dexterity sets the burst's time; <c>skill.json</c>'s <c>dodge</c>): the start,
/// the burst and the recovery (s) and the distance (m) unbound, and the burst's time held to a human's
/// (<see cref="HumanMove"/>, s), which the masquerade's mode uses. Absent, the platform's
/// <see cref="DodgeSettings"/> (a human's). Celerity's body tempo (the contract's <see cref="Powers.PaceScale"/>) is applied
/// by the platform as the dodge begins, as it is to the pace, and not in the masquerade. Plain data, digested.
/// </summary>
public struct DodgeTempo
{
    public float Ready, Move, Recover, Distance, HumanMove;
}

/// <summary>
/// The platform's dodge numbers (<b>ENGINE_SANDBOX</b>, ours): a human's quick evasive step, used for a body the rules
/// give no <see cref="DodgeTempo"/>. The enhanced module's data (<c>skill.json</c>, <c>dodge</c>) gives the same for a
/// human of Dexterity 23 with no Dodge.
/// </summary>
public sealed record DodgeSettings
{
    /// <summary>How far the burst carries the body (m): a quick step and a half, about a stride and a lunge's length.</summary>
    public float Distance { get; init; } = 1.0f;
    /// <summary>The burst's time (s): 1 m in 0.3 s, 3.3 m/s, a fencer's lunge's pace.</summary>
    public float MoveSeconds { get; init; } = 0.3f;
    /// <summary>The start (s): the weight shifted onto the pushing leg before the body moves.</summary>
    public float ReadySeconds { get; init; } = 0.15f;
    /// <summary>The recovery (s): the feet under the body again; no attack, no block, no new dodge.</summary>
    public float RecoverSeconds { get; init; } = 0.4f;
    /// <summary>
    /// Where along the dodge's motion its lowest, furthest pose is (0 to 1): 1.1's <c>ChrisDodgeSword</c> and
    /// <c>ChrisDodgeFists</c> are lowest at frame 18 of 39 (hips 0.79 m against 1.04 m standing; the survey).
    /// </summary>
    public float MotionLow { get; init; } = 18f / 39f;

    /// <summary>The numbers as a <see cref="DodgeTempo"/> (the burst held to itself).</summary>
    public DodgeTempo Tempo => new() { Ready = ReadySeconds, Move = MoveSeconds, Recover = RecoverSeconds, Distance = Distance, HumanMove = MoveSeconds };
}

/// <summary>Why a dodge was not made.</summary>
public enum DodgeRefusal : byte
{
    /// <summary>A blow is playing (the body's attack is swinging or recovering).</summary>
    MidSwing,
    /// <summary>A dodge is under way (its start or its burst).</summary>
    Dodging,
    /// <summary>A dodge is recovering.</summary>
    Recovering,
    /// <summary>The body is in the air.</summary>
    InAir,
    /// <summary>The body is on a ladder.</summary>
    Ladder,
    /// <summary>The body is in a move (a vault, a climb, a hang).</summary>
    Moving,
    /// <summary>The body is stopped (its Health at nothing, or dead).</summary>
    Stopped,
}

/// <summary>A dodge begun: who, the way, the feet, this dodge's start, burst and recovery (s), distance (m) and speed (m/s), and whether the masquerade held the burst to a human's.</summary>
public readonly record struct DodgeBegun(EntityId Entity, long Tick, Vector2 Way, Vector3 Feet, float Ready, float Move, float Recover,
    float Distance, float Speed, bool Held);

/// <summary>A dodge refused, and why; nothing comes of the press.</summary>
public readonly record struct DodgeRefused(EntityId Entity, long Tick, DodgeRefusal Why);

/// <summary>A dodge ended: its recovery over, or cut off by a stop (<paramref name="Cut"/>); where the feet are, and how far the burst carried the body against the distance wanted (m).</summary>
public readonly record struct DodgeEnded(EntityId Entity, long Tick, Vector3 Feet, float Moved, float Distance, bool Cut);

/// <summary>
/// <b>The dodge's press</b> (<see cref="Phase.Act"/>, before the attacks; ours). For each body whose
/// <see cref="DodgeIntent"/> is pressed this tick: refused (<see cref="DodgeRefused"/>, nothing else happens) while a blow
/// plays — unless the swing's side lets a dodge cancel it (<see cref="SwingCancel"/> asked with
/// <see cref="SwingCancelCause.Dodge"/>, and only when nothing else would refuse: the swing is then cancelled and the dodge
/// begins, the next blow waiting for the later of the dodge's end and the cancelled swing's) —, while it dodges or recovers from one, in the air, on a ladder, in a move, or stopped; else <b>begun</b>
/// (<see cref="DodgeBegun"/>): its numbers are the body's <see cref="DodgeTempo"/> (or <see cref="DodgeSettings"/>), the burst
/// held to a human's in the masquerade's mode and every time divided by the pace scale in force (Celerity's body tempo,
/// which the masquerade ignores: <see cref="BodyMotor.PaceFactor"/>); a raised block is lowered; and a fighter is
/// <b>busy</b> for the whole dodge (<see cref="Combatant.Busy"/>), so no blow begins and no block rises until the recovery
/// ends — a press of attack meanwhile is kept for then, as during a swing. The body itself is moved by the motor
/// (<see cref="BodyMotor.DodgeStep"/>, in <see cref="BodySystem"/>). A dodge under way on a body that stops is ended
/// (<see cref="DodgeEnded"/>, cut). Allocates nothing.
/// </summary>
public sealed class DodgeSystem : ISystem
{
    int fightersKnown, animsKnown, pacesKnown, masksKnown, movesKnown;

    public void Run(World world)
    {
        var asks = world.Store<DodgeIntent>();
        var dodges = world.Store<Dodging>();
        if (asks.Count == 0 && dodges.Count == 0) return;
        var bodies = world.Store<Body>();
        var tempos = world.Store<DodgeTempo>();
        var fighters = PoseRigSystem.Optional<Combatant>(world, ref fightersKnown);
        var anims = PoseRigSystem.Optional<CharacterAnimation>(world, ref animsKnown);
        var paces = PoseRigSystem.Optional<Powers.PaceScale>(world, ref pacesKnown);
        var masks = PoseRigSystem.Optional<MasqueradeState>(world, ref masksKnown);
        var moves = PoseRigSystem.Optional<Traversal>(world, ref movesKnown);
        var settings = world.TryService<DodgeSettings>(out var ds) ? ds : Default;
        long tick = world.Tick;

        // A dodge under way on a body that has stopped ends there.
        var dd = dodges.Data;
        var de = dodges.Entities;
        for (int i = 0; i < dd.Length; i++)
        {
            if (!dd[i].Active || !Stopped(world, anims, de[i])) continue;
            ref var gone = ref bodies.Find(de[i]);
            var feet = Unsafe.IsNullRef(ref gone) ? dd[i].From : gone.Feet;
            world.Events<DodgeEnded>().Publish(new DodgeEnded(de[i], tick, feet, dd[i].Moved, dd[i].Distance, Cut: true));
            dd[i].Phase = DodgePhase.None;
            dd[i].Seconds = 0f;
        }

        var data = asks.Data;
        var ents = asks.Entities;
        for (int i = 0; i < data.Length; i++)
        {
            if (!data[i].Pressed) continue;
            var e = ents[i];
            var way = data[i].Way;
            data[i].Pressed = false;
            ref var body = ref bodies.Find(e);
            if (Unsafe.IsNullRef(ref body)) continue;
            ref var fighter = ref fighters is null ? ref Unsafe.NullRef<Combatant>() : ref fighters.Find(e);
            ref var anim = ref anims is null ? ref Unsafe.NullRef<CharacterAnimation>() : ref anims.Find(e);
            ref var now = ref dodges.Find(e);

            DodgeRefusal? why = null;
            // A dodge during a swing cancels the swing when the swing's side allows it (SwingCancel);
            // it is asked only when nothing else would refuse the dodge, and the swing is cancelled only then. Else, as before, refused.
            bool midSwing = !Unsafe.IsNullRef(ref fighter) && !Unsafe.IsNullRef(ref anim) && fighter.Busy > 0f && ActionRoles.IsAttack(anim.Action);
            DodgeRefusal? besides = body.State == BodyState.Ladder ? DodgeRefusal.Ladder
                : body.State is BodyState.Traversing or BodyState.Hanging
                  || (moves is not null && moves.Has(e) && moves.Get(e).Kind != TraversalKind.None) ? DodgeRefusal.Moving
                : body.State != BodyState.Ground ? DodgeRefusal.InAir
                : null;
            if (Stopped(world, anims, e)) why = DodgeRefusal.Stopped;
            else if (!Unsafe.IsNullRef(ref now) && now.Active)
                why = now.Phase == DodgePhase.Recover ? DodgeRefusal.Recovering : DodgeRefusal.Dodging;
            else if (midSwing && (besides is not null || !SwingCancelCalls.Can(world, e, SwingCancelCause.Dodge) || !SwingCancelCalls.Do(world, e, SwingCancelCause.Dodge)))
                why = DodgeRefusal.MidSwing;
            else why = besides;
            if (why is { } refused)
            {
                world.Events<DodgeRefused>().Publish(new DodgeRefused(e, tick, refused));
                continue;
            }

            // The way: as asked (level, unit), else straight back from the facing.
            float wl = way.Length();
            if (wl > 1e-4f) way /= wl;
            else
            {
                var (s, c) = DetMath.SinCos(body.Yaw);
                way = new Vector2(-c, -s);
            }
            var t = tempos.Has(e) ? tempos.Get(e) : settings.Tempo;
            bool held = masks is not null && masks.Has(e) && masks.Get(e).Mode == MasqueradeMode.Masquerade;
            float move = held ? MathF.Max(t.Move, t.HumanMove) : t.Move;
            float f = BodyMotor.PaceFactor(paces, masks, e);     // 1 in the masquerade
            float ready = MathF.Max(0f, t.Ready) / f, recover = MathF.Max(0f, t.Recover) / f;
            move = MathF.Max(World.TickSeconds, move / f);
            float distance = MathF.Max(0f, t.Distance);
            var dodge = Unsafe.IsNullRef(ref now) ? default : now;
            dodge.Phase = ready > 0f ? DodgePhase.Start : DodgePhase.Burst;
            dodge.Seconds = 0f;
            dodge.Way = way;
            dodge.Ready = ready;
            dodge.Move = move;
            dodge.Recover = recover;
            dodge.Distance = distance;
            dodge.Speed = distance / move;
            dodge.From = body.Feet;
            dodge.Began = tick;
            dodge.Moved = 0f;
            dodge.Count++;
            dodges.Set(e, dodge);

            // A raised block is lowered; a fighter is busy for the whole dodge (no blow, no block until it ends).
            if (!Unsafe.IsNullRef(ref fighter))
            {
                if (fighter.Blocking)
                {
                    if (!Unsafe.IsNullRef(ref anim) && anim.Action == ActionRole.Block) ActionChannel.Release(ref anim);
                    fighter.Blocking = false;
                    if (world.TryService<EventQueue<BlockLowered>>(out var lowered)) lowered.Publish(new BlockLowered(e, tick, fighter.BlockSeconds));
                }
                fighter.Busy = MathF.Max(fighter.Busy, dodge.Total);
            }
            world.Events<DodgeBegun>().Publish(new DodgeBegun(e, tick, way, body.Feet, ready, move, recover, distance, dodge.Speed, held));
        }
    }

    static readonly DodgeSettings Default = new();

    /// <summary>Whether a body is stopped: dead (<see cref="Combat.Dead"/>) or playing a death.</summary>
    static bool Stopped(World world, Store<CharacterAnimation>? anims, EntityId e)
    {
        if (world.TryStore<Combat.Dead>(out var dead) && dead.Has(e)) return true;
        if (anims is null) return false;
        ref var a = ref anims.Find(e);
        return !Unsafe.IsNullRef(ref a) && ActionRoles.IsDeath(a.Action);
    }
}

/// <summary>The dodge registered.</summary>
public static class Dodges
{
    /// <summary>Its place: <c>Act/96</c>, after the module's numbers (90) and before the attacks (100).</summary>
    public const int Order = 96;

    /// <summary>
    /// Registers the components <see cref="DodgeIntent"/>, <see cref="Dodging"/> and <see cref="DodgeTempo"/>, the events
    /// <see cref="DodgeBegun"/>, <see cref="DodgeRefused"/> and <see cref="DodgeEnded"/>, the service <see cref="DodgeSettings"/>
    /// (<paramref name="settings"/> or a human's) and <c>Act/96 platform.dodge</c>. The motor's part runs in
    /// <see cref="BodySystem"/>. Once per world (with the movement, <see cref="MovementServices.Register"/>).
    /// </summary>
    public static WorldBuilder Register(WorldBuilder b, DodgeSettings? settings = null)
    {
        b.Component<DodgeIntent>().Component<Dodging>().Component<DodgeTempo>();
        b.Event<DodgeBegun>().Event<DodgeRefused>().Event<DodgeEnded>();
        b.Service(settings ?? new DodgeSettings());
        b.System(Phase.Act, Order, "platform.dodge", new DodgeSystem());
        return b;
    }

    /// <summary>Asks <paramref name="e"/> for a dodge this tick along <paramref name="way"/> (zero: back), putting the intent on it if it has none.</summary>
    public static void Ask(World world, EntityId e, Vector2 way)
    {
        var asks = world.Store<DodgeIntent>();
        ref var a = ref asks.Find(e);
        if (Unsafe.IsNullRef(ref a)) asks.Set(e, new DodgeIntent { Pressed = true, Way = way });
        else { a.Pressed = true; a.Way = way; }
    }
}
