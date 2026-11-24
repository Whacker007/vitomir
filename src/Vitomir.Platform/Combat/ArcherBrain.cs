// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: an archer brain, v0 (ranged AI). Some randomness for the
// AI, so that it does not always shoot with laser accuracy. The rules package: no hidden aim misses — the error is in where the bow points.
using System.Numerics;
using System.Runtime.CompilerServices;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Combat;

/// <summary>
/// An archer brain's numbers (ENGINE_SANDBOX, ours: a testbed's archer, not the game's AI; the module's data
/// <c>Data/combat/ranged.json</c>'s <c>archer_brain</c>, a scenario's <c>control.ai</c> over them).
/// </summary>
/// <param name="KeepMetres">It comes on (at a run) until its foe is this near across the ground, then stands and shoots.</param>
/// <param name="SwordMetres">Its foe this near, it puts the bow away and fights with its melee weapon (the duelling brain) from then on.</param>
/// <param name="HoldSeconds">How long it holds a full draw on its aim before it looses.</param>
/// <param name="ErrorScale">Its aim's error is the bow's spread by its ranged Skill (<see cref="BowConfig.SpreadDegrees"/>: 2.0° at Range 0 to 0.35° at 5, one standard deviation each way) times this.</param>
/// No hold sway and no draw fatigue (<i>"Leave out sway and draw fatigue for now"</i>).
public sealed record ArcherBrainSettings(float KeepMetres = 20f, float SwordMetres = 4f, float HoldSeconds = 0.5f, float ErrorScale = 1f)
{
    /// <summary>The numbers' ranges, for a reader's checks: (least, most) by name.</summary>
    public static readonly IReadOnlyDictionary<string, (float Least, float Most)> Ranges = new Dictionary<string, (float, float)>
    {
        ["keep_metres"] = (2f, 150f), ["sword_metres"] = (0f, 20f), ["hold_seconds"] = (0f, 10f),
        ["error_scale"] = (0f, 10f),
    };
}

/// <summary>The archer brains' numbers (a service): the brains index them, as the duelling brain's book.</summary>
public sealed class ArcherBrainBook
{
    readonly List<ArcherBrainSettings> all = [];
    public IReadOnlyList<ArcherBrainSettings> All => all;

    /// <summary>Adds settings and gives their index.</summary>
    public int Add(ArcherBrainSettings settings)
    {
        all.Add(settings);
        return all.Count - 1;
    }
}

/// <summary>Where an archer brain is.</summary>
public enum ArcherPhase
{
    /// <summary>Coming on toward its foe (beyond its keep range).</summary>
    Closing = 0,
    /// <summary>Standing, its bow up, aiming, drawing, holding and loosing.</summary>
    Shooting = 1,
    /// <summary>Its bow put away: the duelling brain fights with its melee weapon.</summary>
    Sword = 2,
    /// <summary>No foe to shoot (gone or dead): it stands.</summary>
    Idle = 3,
}

/// <summary>
/// An archer brain (digested; registered only in a world with an archer, <see cref="ArcherBrains.Register"/>): its foe, its
/// numbers (<see cref="Book"/>), its phase, the shot its error was drawn for and that error (radians, in the direction it aims), the
/// seconds it has held the draw, the sway's phases, and for the sword the duelling brain's numbers it fights with then.
/// </summary>
public struct ArcherBrain
{
    public EntityId Foe;
    public int Book;
    int phase;
    /// <summary>The shot (<see cref="Bow.Shots"/> + 1 as it is nocked) its aim error was drawn for; 0 none yet.</summary>
    public int ErrorFor;
    public float ErrorYaw, ErrorPitch;
    public float HeldSeconds;
    /// <summary>For the sword: the duelling brain's book index, its reach and its foe's.</summary>
    public int DuelBook;
    public float Reach, FoeReach;
    /// <summary>The shots it has loosed.</summary>
    public int Loosed;
    /// <summary>A foe placed in play waits, its bow low, until its foe is this near (m, across the ground) or a blow or an arrow meets it (0: it does not wait; as <see cref="FoeWaits"/>).</summary>
    public float WakeMetres;

    public ArcherPhase Phase { readonly get => (ArcherPhase)phase; set => phase = (int)value; }

    public static ArcherBrain For(EntityId foe, int book, int duelBook, float reach, float foeReach) =>
        new() { Foe = foe, Book = book, DuelBook = duelBook, Reach = reach, FoeReach = foeReach };
}

/// <summary>An archer brain's shot aimed (for the trace and tests): the aim's error drawn for it (degrees, yaw right negative, pitch up positive) and the archer's Skill's spread it was drawn from.</summary>
public readonly record struct ArcherAimed(EntityId Archer, long Tick, int Shot, EntityId Foe, float ErrorYawDegrees, float ErrorPitchDegrees, float SpreadDegrees, float DistanceMetres);

/// <summary>An archer brain put its bow away for its melee weapon, its foe this near (m) or its quiver empty.</summary>
public readonly record struct ArcherToSword(EntityId Archer, long Tick, EntityId Foe, float DistanceMetres, bool OutOfArrows);

/// <summary>
/// The archer brain (<see cref="Phase.Think"/>, <see cref="ArcherBrains.Order"/>; v0). For each body with an
/// <see cref="ArcherBrain"/>, a <see cref="Bow"/> and a <see cref="BowIntent"/>:
/// <list type="bullet">
/// <item>its foe farther than <see cref="ArcherBrainSettings.KeepMetres"/>: the bow low, it runs toward it;</item>
/// <item>within: it stands, the bow in hand and up (aim mode), facing its aim; once an arrow is nocked it draws its <b>aim error</b> for
/// that shot — a normal draw in yaw and pitch of the bow's spread by its ranged Skill (<see cref="BowConfig.SpreadDegrees"/>) times
/// <see cref="ArcherBrainSettings.ErrorScale"/> (stream <see cref="ErrorStream"/>) — and draws to full; it holds
/// <see cref="ArcherBrainSettings.HoldSeconds"/> and looses; the bow nocks the next by itself. The aim is held on its foe's chest
/// (<see cref="BowServices.RegionPoint"/>), the pitch solved for a full draw's drop (<see cref="Ballistics.SolvePitch"/>; the foe's motion
/// not led), then turned by the error (no hold sway: ): <b>the bow points where the arrow
/// goes</b>, and the arrow leaves exactly along it (<see cref="Control.PlayerBowSettings.Exact"/>: no spread drawn at the loose) — the
/// miss is seen before the shot, never a hidden roll after it;</item>
/// <item>its foe within <see cref="ArcherBrainSettings.SwordMetres"/>, or its quiver empty: it puts the bow away and the duelling brain
/// takes it on with its melee weapon (<see cref="DuelBrain.For"/>) from then on (<see cref="ArcherToSword"/>).</item>
/// </list>
/// Deterministic (its draws only as a shot is nocked); allocates nothing per tick.
/// </summary>
public sealed class ArcherBrainSystem : ISystem
{
    /// <summary>The random stream an archer's aim errors are drawn from.</summary>
    public const string ErrorStream = "platform.archer.aim";

    Rng? errors;

    public void Run(World world)
    {
        if (!world.TryStore<ArcherBrain>(out var brains) || brains.Count == 0 || !world.TryService<ArcherBrainBook>(out var book) || book.All.Count == 0) return;
        if (!world.TryStore<Bow>(out var bows) || !world.TryStore<BowIntent>(out var intents) || !world.TryService<BowConfig>(out var config)) return;
        var bodies = world.Store<Body>();
        var moves = world.Store<MoveIntent>();
        world.TryStore<CharacterAnimation>(out var anims);
        var ents = brains.Entities;
        var data = brains.Data;
        float dt = World.TickSeconds;
        for (int i = 0; i < ents.Length; i++)
        {
            var e = ents[i];
            ref var brain = ref data[i];
            if (brain.Phase == ArcherPhase.Sword) continue;
            ref var bow = ref bows.Find(e);
            ref var intent = ref intents.Find(e);
            ref var body = ref bodies.Find(e);
            ref var move = ref moves.Find(e);
            if (Unsafe.IsNullRef(ref bow) || Unsafe.IsNullRef(ref intent) || Unsafe.IsNullRef(ref body) || Unsafe.IsNullRef(ref move)) continue;
            if ((uint)bow.Weapon >= (uint)config.Bows.Count) continue;
            var d = config.Bows[bow.Weapon];
            var s = book.All[Math.Clamp(brain.Book, 0, book.All.Count - 1)];
            move.Direction = Vector2.Zero;
            move.Jump = move.JumpHeld = move.Crouch = move.Cancel = move.Use = false;
            ref var foe = ref world.Alive(brain.Foe) ? ref bodies.Find(brain.Foe) : ref Unsafe.NullRef<Body>();
            if (Dead(world, e) || Unsafe.IsNullRef(ref foe) || Dead(world, brain.Foe))
            {
                brain.Phase = ArcherPhase.Idle;
                intent.Aim = false;
                intent.Draw = false;
                continue;
            }
            var to = new Vector2(foe.Feet.X - body.Feet.X, foe.Feet.Y - body.Feet.Y);
            float distance = to.Length();
            var toward = distance > 1e-4f ? to / distance : new Vector2(1f, 0f);
            if (brain.WakeMetres > 0f)
            {
                // Waiting for its foe (a foe placed in play): until it comes near, or something met it last tick.
                bool struck = false;
                if (world.TryService<EventQueue<ContactMade>>(out var met)) foreach (var c in met.LastTick) if (c.Target == e) { struck = true; break; }
                if (!struck && distance > brain.WakeMetres) { brain.Phase = ArcherPhase.Idle; intent.Aim = false; intent.Draw = false; continue; }
                brain.WakeMetres = 0f;
                if (world.TryService<EventQueue<FoeWoke>>(out var woke)) woke.Publish(new FoeWoke(e, brain.Foe, world.Tick, struck));
            }

            // Up close, or out of arrows: the bow away and the duelling brain with its melee weapon.
            if (distance <= s.SwordMetres || (bow.Arrows <= 0 && !bow.OnString))
            {
                intent.Ranged = false;
                intent.Aim = false;
                intent.Draw = false;
                intent.AimGiven = false;
                brain.Phase = ArcherPhase.Sword;
                if (world.TryStore<DuelBrain>(out var duels) && !duels.Has(e)) world.Set(e, DuelBrain.For(BrainKind.Duel, brain.Foe, brain.DuelBook, brain.Reach, brain.FoeReach));
                world.Events<ArcherToSword>().Publish(new ArcherToSword(e, world.Tick, brain.Foe, distance, bow.Arrows <= 0 && !bow.OnString));
                continue;
            }
            intent.Ranged = true;
            if (distance > s.KeepMetres && !bow.Drawn)
            {
                // Coming on: the bow low, at a run, facing the way.
                brain.Phase = ArcherPhase.Closing;
                intent.Aim = false;
                intent.Draw = false;
                move.Direction = toward;
                move.Gait = Gait.Run;
                move.Facing = DetMath.Atan2(toward.Y, toward.X);
                move.Faces = true;
                continue;
            }

            // Shooting: the bow up, the aim on the foe's chest with this shot's error and the sway, the draw, the hold and the loose.
            brain.Phase = ArcherPhase.Shooting;
            intent.Aim = true;
            intent.DrawToShare = 1f;
            move.Gait = Gait.Walk;
            if (bow.State == BowState.Nocked && brain.ErrorFor != bow.Shot && bow.Shot > 0)
            {
                // A new arrow on the string: this shot's error drawn, as the aim begins (seen in where the bow points, never after the shot).
                float spread = (config.SpreadDegrees?.Invoke(world, e) ?? 0f) * s.ErrorScale;
                errors ??= world.Random.For(ErrorStream);
                float u1 = MathF.Max(1e-7f, errors.NextFloat()), u2 = errors.NextFloat();
                float r = MathF.Sqrt(-2f * DetMath.Log(u1));
                var (sn, cs) = DetMath.SinCos(2f * MathF.PI * u2);
                brain.ErrorYaw = r * cs * spread * DetMath.DegToRad;
                brain.ErrorPitch = r * sn * spread * DetMath.DegToRad;
                brain.ErrorFor = bow.Shot;
                brain.HeldSeconds = 0f;
                world.Events<ArcherAimed>().Publish(new ArcherAimed(e, world.Tick, bow.Shot, brain.Foe, brain.ErrorYaw * DetMath.RadToDeg, brain.ErrorPitch * DetMath.RadToDeg, spread, distance));
            }
            // The aim: the foe's chest from the release point, the drop of a full draw allowed for, then the error and the sway.
            float yaw = DetMath.Atan2(toward.Y, toward.X), pitch = 0f;
            var from = BowServices.ReleasePoint(world, e, in body, yaw, d);
            if (BowServices.RegionPoint(world, brain.Foe, BodyRegion.Chest, from, out var target))
            {
                yaw = DetMath.Atan2(target.Y - from.Y, target.X - from.X);
                pitch = Ballistics.SolvePitch(from, target, d.SpeedAt(1f), d.ArrowDragPerMetre);
            }
            if (brain.ErrorFor == bow.Shot && bow.Shot > 0)
            {
                yaw += brain.ErrorYaw;
                pitch += brain.ErrorPitch;
            }
            intent.AimsAt = false;
            intent.AimGiven = true;
            intent.AimYaw = DetMath.WrapAngle(yaw);
            intent.AimPitch = pitch;
            move.Facing = DetMath.WrapAngle(yaw - BowShots.TurnOf(world, e));   // the drawn arrow along the aim (1.1's archer stands side-on)
            move.Faces = true;
            // The draw (after a let-down — a flinch — the draw let go a tick so it can be pressed again), the hold, the loose.
            if (bow.WaitRelease) { intent.Draw = false; continue; }
            if (bow.State == BowState.Nocked && brain.ErrorFor == bow.Shot) intent.Draw = true;
            if (bow.State == BowState.Held)
            {
                brain.HeldSeconds += dt;
                if (brain.HeldSeconds >= s.HoldSeconds - 1e-5f)
                {
                    intent.Loose = true;
                    intent.Draw = false;
                    brain.HeldSeconds = 0f;
                    brain.Loosed++;
                }
            }
        }
    }

    static bool Dead(World world, EntityId e) => world.TryStore<Dead>(out var dead) && dead.Has(e);
}

/// <summary>The archer brain registered: only in a world with an archer (a foe preset's or a scenario's), so every other world is as before, digest for digest.</summary>
public static class ArcherBrains
{
    /// <summary>The brain's place: <c>Think/94</c>, before the duelling brain (95), which takes an archer on once it has put its bow away.</summary>
    public const int Order = 94;

    /// <summary>Registers <see cref="ArcherBrain"/>, the <see cref="ArcherBrainBook"/>, the events <see cref="ArcherAimed"/> and <see cref="ArcherToSword"/>, and <c>Think/94 platform.archer-brain</c>. After the bow's (<see cref="BowServices.Register(WorldBuilder, BowConfig?)"/>).</summary>
    public static WorldBuilder Register(WorldBuilder b)
    {
        if (b.Peek.TryService<ArcherBrainBook>(out _)) return b;
        b.Component<ArcherBrain>();
        b.Service(new ArcherBrainBook());
        b.Event<ArcherAimed>().Event<ArcherToSword>();
        b.System(Phase.Think, Order, "platform.archer-brain", new ArcherBrainSystem());
        return b;
    }
}
