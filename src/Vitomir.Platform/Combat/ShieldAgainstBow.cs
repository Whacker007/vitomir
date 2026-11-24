// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: a duelling brain with a shield raises it against a bow drawn and aimed at it.
using System.Numerics;
using System.Runtime.CompilerServices;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Combat;

/// <summary>
/// When a duelling brain raises its shield against a drawn bow (every number ENGINE_SANDBOX, ours, the testbed's
/// policy, not a rule): the bow within <see cref="RangeMetres"/>, drawn to at least <see cref="LeastDrawShare"/>, aimed within
/// <see cref="AimedWithinDegrees"/> of the line from its release point to the brain's chest, and the archer within
/// <see cref="ViewHalfDegrees"/> of the brain's facing; it reacts after its block's reaction (the brain's <c>reactSeconds</c> times its
/// guard skill's factor, <see cref="GuardSkills.Of"/>). The switch <c>shield-at-bow-off</c> turns it off (<see cref="On"/>).
/// </summary>
public sealed class ShieldAgainstBowSettings
{
    /// <summary>The switch that turns it off (the earlier build's brain: no answer to an archer).</summary>
    public const string OffSwitch = "shield-at-bow-off";

    public bool On { get; set; } = true;

    /// <summary>The farthest bow it answers (m): a war bow's aimed range for a man-sized mark.</summary>
    public float RangeMetres { get; init; } = 40f;

    /// <summary>How much of the draw before it reacts (share of full): a bow raised and only begun is not yet a threat.</summary>
    public float LeastDrawShare { get; init; } = 0.25f;

    /// <summary>How near the bow's aim is to the line to its chest (degrees) for it to count as aimed at it.</summary>
    public float AimedWithinDegrees { get; init; } = 12f;

    /// <summary>How far off its facing it sees an archer (degrees each side).</summary>
    public float ViewHalfDegrees { get; init; } = 80f;

    /// <summary>Where its chest is, as a share of its height.</summary>
    public const float ChestShare = 0.72f;
}

/// <summary>
/// A duelling brain's watch on the bows drawn at it (digested, a world with a bow only; put on a brain the first time a
/// bow is drawn at it): the archer it answers, how long it has seen the bow aimed at it, and whether its shield is up against it.
/// </summary>
public struct ShieldWatch
{
    public EntityId Archer;
    public float SeenFor;
    int up;

    public bool Up { readonly get => up != 0; set => up = value ? 1 : 0; }
}

/// <summary>
/// The shield against a drawn bow (<see cref="Phase.Think"/>, right after the duelling brain): for each
/// duelling brain holding a shield, the nearest bow drawn and aimed at it within range and in its view (<see cref="ShieldAgainstBowSettings"/>):
/// once seen for its reaction, the brain's block is held (its shield raised) and it faces the archer, while the bow stays drawn and aimed
/// at it; then it fights as before (<see cref="ShieldAgainstBow"/> as it rises and falls). Nothing in a world without a bow.
/// </summary>
public sealed class ShieldAgainstBowSystem : ISystem
{
    public void Run(World world)
    {
        if (!world.TryService<ShieldAgainstBowSettings>(out var s) || !s.On) return;
        if (!world.TryStore<Bow>(out var bows) || bows.Count == 0 || !world.TryStore<DuelBrain>(out var brains) || brains.Count == 0) return;
        if (!world.TryService<DuelBrainBook>(out var book) || !world.TryService<BowConfig>(out var config)) return;
        var bodies = world.Store<Body>();
        var intents = world.Store<CombatIntent>();
        var moves = world.Store<MoveIntent>();
        var watches = world.Store<ShieldWatch>();
        var locos = world.Store<Locomotion>();
        var anims = world.TryStore<CharacterAnimation>(out var an) ? an : null;
        float cosAimed = DetMath.Cos(s.AimedWithinDegrees * DetMath.DegToRad);
        float cosView = DetMath.Cos(s.ViewHalfDegrees * DetMath.DegToRad);
        var brainEnts = brains.Entities;
        for (int i = 0; i < brainEnts.Length; i++)
        {
            var e = brainEnts[i];
            ref var brain = ref brains.Data[i];
            ref var body = ref bodies.Find(e);
            ref var intent = ref intents.Find(e);
            ref var move = ref moves.Find(e);
            if (brain.Kind != BrainKind.Duel || Unsafe.IsNullRef(ref body) || Unsafe.IsNullRef(ref intent) || Unsafe.IsNullRef(ref move)) continue;
            bool dead = anims is not null && anims.Has(e) && ActionRoles.IsDeath(anims.Get(e).Action);
            ref var watch = ref watches.Find(e);
            if (dead || !HeldItems.HasShield(world, e))
            {
                if (!Unsafe.IsNullRef(ref watch) && watch.Up) Lower(world, e, ref watch, 0f);
                continue;
            }

            // The nearest bow drawn and aimed at it, in range and in its view.
            float height = locos.Has(e) ? body.CapsuleHeight(locos.Get(e)) : 1.8f;
            var chest = body.Feet + new Vector3(0f, 0f, height * ShieldAgainstBowSettings.ChestShare);
            var (fs, fc) = DetMath.SinCos(body.Yaw);
            var archer = EntityId.None;
            float nearest = s.RangeMetres;
            var bowEnts = bows.Entities;
            for (int j = 0; j < bowEnts.Length; j++)
            {
                var a = bowEnts[j];
                ref var bow = ref bows.Data[j];
                if (a == e || !bow.InHand || !bow.Drawn || bow.Share + 1e-5f < s.LeastDrawShare || (uint)bow.Weapon >= (uint)config.Bows.Count) continue;
                ref var ab = ref bodies.Find(a);
                if (Unsafe.IsNullRef(ref ab)) continue;
                var flat = new Vector2(ab.Feet.X - body.Feet.X, ab.Feet.Y - body.Feet.Y);
                float d = flat.Length();
                if (d > nearest || d < 1e-3f) continue;
                if ((flat.X * fc + flat.Y * fs) / d < cosView) continue;                     // not in its view
                var from = BowServices.ReleasePoint(world, a, in ab, bow.AimYaw, config.Bows[bow.Weapon]);
                var line = chest - from;
                float len = line.Length();
                if (len < 1e-3f || Vector3.Dot(DetMath.Direction(bow.AimYaw, bow.AimPitch), line / len) < cosAimed) continue;   // not aimed at it
                nearest = d;
                archer = a;
            }

            if (archer.IsNone)
            {
                if (!Unsafe.IsNullRef(ref watch) && (watch.Up || watch.SeenFor > 0f)) Lower(world, e, ref watch, 0f);
                continue;
            }
            if (Unsafe.IsNullRef(ref watch)) { world.Set(e, new ShieldWatch()); watch = ref watches.Get(e); }
            if (watch.Archer != archer) { watch.Archer = archer; watch.SeenFor = 0f; }
            watch.SeenFor += World.TickSeconds;
            var settings = book.All[Math.Clamp(brain.Book, 0, book.All.Count - 1)];
            float react = settings.ReactSeconds * GuardSkills.Of(world, e).React;
            if (!watch.Up && watch.SeenFor + 1e-5f >= react)
            {
                watch.Up = true;
                world.Events<ShieldAgainstBow>().Publish(new ShieldAgainstBow(e, world.Tick, archer, true, nearest));
            }
            if (!watch.Up) continue;
            intent.Block = true;
            intent.Attack = false;
            ref var ab2 = ref bodies.Get(archer);
            move.Facing = DetMath.Atan2(ab2.Feet.Y - body.Feet.Y, ab2.Feet.X - body.Feet.X);
            move.Faces = true;
        }
    }

    static void Lower(World world, EntityId e, ref ShieldWatch watch, float distance)
    {
        if (watch.Up) world.Events<ShieldAgainstBow>().Publish(new ShieldAgainstBow(e, world.Tick, watch.Archer, false, distance));
        watch.Up = false;
        watch.SeenFor = 0f;
        watch.Archer = EntityId.None;
    }
}

/// <summary>The shield against a drawn bow registered (by <see cref="BowServices.Register"/>: a world with a bow only).</summary>
public static class ShieldAgainstBows
{
    /// <summary>Its place: <see cref="Phase.Think"/> with the duelling brain (95), after it by name, before the scenario director (96).</summary>
    public const int Order = DuelBrains.Order;

    public static WorldBuilder Register(WorldBuilder b, ShieldAgainstBowSettings? settings = null)
    {
        b.Component<ShieldWatch>();
        if (!b.Peek.TryService<EventQueue<ShieldAgainstBow>>(out _)) b.Event<ShieldAgainstBow>();
        if (!b.Peek.TryService<ShieldAgainstBowSettings>(out _)) b.Service(settings ?? new ShieldAgainstBowSettings());
        b.System(Phase.Think, Order, "platform.duel-brain.shield-at-bow", new ShieldAgainstBowSystem());
        return b;
    }
}
