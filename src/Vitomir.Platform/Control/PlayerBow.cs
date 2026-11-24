// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the player's bow — the swap, the draw, the loose, the let-down and the aim at the crosshair.
using System.Numerics;
using System.Runtime.CompilerServices;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Cameras;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Core;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Control;

/// <summary>
/// The player's bow (<i>"Holding down the attack button starts the draw sequence, ending at
/// full draw. As soon as attack is released, the player releases the arrow … The draw is cancellable at any point"</i>), from the
/// player's controls into the body's <see cref="BowIntent"/>, by <see cref="PlayerControlSystem"/> for a controlled body with a
/// <see cref="Bow"/> (a world with a bow only):
/// <list type="bullet">
/// <item><b>Swap</b> (<see cref="Buttons.SwapWeapon"/>, F by default): the melee weapon and the bow swap over the bow's
/// <c>swap_seconds</c> (<see cref="BowData.SwapSeconds"/>, 0.6 s, ENGINE_SANDBOX): at its start a draw is let down and the bow lowered,
/// no blow or guard begins through it, and at its end the other is in hand (<see cref="WeaponSwap"/> at both).</item>
/// <item><b>Draw</b>: with the bow in hand, attack held raises the bow (an arrow nocked) and draws it to full draw (1.0 s), held there
/// while held; <b>loose</b> on its release, at the speed of the draw reached. <see cref="Buttons.Aim"/> (unbound by default; the
/// aim mode) raises the bow with an arrow nocked undrawn while held, and lets a draw down when let go. With the bow low the next
/// arrow is nocked by itself (a body with a <see cref="BowHand"/>: <see cref="BowSystem"/>).</item>
/// <item><b>Let down</b>: block pressed, a dodge pressed (the dodge goes on), or cancel pressed (<see cref="CanLetDown"/>: the press does nothing
/// else then), while drawn.</item>
/// <item><b>The aim is the crosshair</b>: the arrow leaves from the release point toward the point the reticle's ray (the view's, as the
/// last picture was drawn from) meets first — the level or another body's hit volumes — or the point <see cref="AimReachMetres"/> out
/// along it: so in third person the camera's offset from the bow (the parallax) is solved, and the arrow goes where the crosshair is.
/// The arrow's drop is the archer's to allow for (no aid holds it up). While the bow is up the body faces the view.</item>
/// </list>
/// </summary>
public static class PlayerBow
{
    /// <summary>How far the crosshair's point is looked for (m; ENGINE_SANDBOX, ours): beyond, the aim is along the view at this distance.</summary>
    public const float AimReachMetres = 150f;

    /// <summary>Whether a press of cancel would let <paramref name="e"/>'s drawn bow down now (it then does nothing else).</summary>
    public static bool CanLetDown(World world, EntityId e)
    {
        if (!world.TryStore<Bow>(out var bows)) return false;
        ref var bow = ref bows.Find(e);
        return !Unsafe.IsNullRef(ref bow) && bow.InHand && bow.Drawn;
    }

    /// <summary>Lets <paramref name="e"/>'s drawn bow down (the press of cancel).</summary>
    public static void LetDown(World world, EntityId e)
    {
        ref var intent = ref world.Store<BowIntent>().Find(e);
        if (!Unsafe.IsNullRef(ref intent)) intent.LetDown = true;
    }

    /// <summary>One tick of the player's bow for <paramref name="e"/> (see the class); nothing when it has no bow.</summary>
    public static void Step(World world, EntityId e, in ActionFrame frame, Buttons pressed, ref CombatIntent fight, in CameraRig rig, ref MoveIntent move)
    {
        if (!world.TryStore<Bow>(out var bows)) return;
        ref var bow = ref bows.Find(e);
        ref var intent = ref world.Store<BowIntent>().Find(e);
        if (Unsafe.IsNullRef(ref bow) || Unsafe.IsNullRef(ref intent)) return;
        var config = world.Service<BowConfig>();
        if ((uint)bow.Weapon >= (uint)config.Bows.Count) return;
        var d = config.Bows[bow.Weapon];
        if (!world.TryStore<BowHand>(out var hands)) return;   // a world without the player's bow (a script's archer)
        if (!hands.Has(e)) world.Set(e, new BowHand());
        ref var hand = ref hands.Get(e);
        bool fightable = !Unsafe.IsNullRef(ref fight);

        // The swap: begun by a press, over the bow's swap time; through it no blow, guard or draw.
        bool stopped = world.TryStore<Dead>(out var dead) && dead.Has(e);
        // Not mid-blow: a swing or a flinch playing finishes first (the press is not kept).
        ref var anim = ref world.TryStore<Animation.CharacterAnimation>(out var anims) ? ref anims.Find(e) : ref Unsafe.NullRef<Animation.CharacterAnimation>();
        bool midBlow = !Unsafe.IsNullRef(ref anim) && !anim.ActionEnding && (Animation.ActionRoles.IsAttack(anim.Action) || anim.Action == Animation.ActionRole.Hurt);
        if ((pressed & Buttons.SwapWeapon) != 0 && hand.SwapLeft <= 0f && !stopped && !midBlow)
        {
            hand.ToRanged = !intent.Ranged;
            hand.SwapLeft = MathF.Max(World.TickSeconds, d.SwapSeconds);
            world.Events<WeaponSwap>().Publish(new WeaponSwap(e, world.Tick, hand.ToRanged, false, hand.SwapLeft));
        }
        if (hand.SwapLeft > 0f)
        {
            intent.Aim = false;
            intent.Draw = false;
            if (fightable) { fight.Attack = false; fight.Block = false; }
            hand.SwapLeft -= World.TickSeconds;
            if (hand.SwapLeft <= 1e-6f)
            {
                hand.SwapLeft = 0f;
                intent.Ranged = hand.ToRanged;
                world.Events<WeaponSwap>().Publish(new WeaponSwap(e, world.Tick, hand.ToRanged, true, d.SwapSeconds));
            }
            return;
        }
        if (!intent.Ranged) { intent.Aim = false; intent.Draw = false; return; }

        bool attack = frame.Holds(Buttons.Attack) && !rig.PointerFree;
        bool aim = frame.Holds(Buttons.Aim) && !rig.PointerFree;
        var settings = world.TryService<PlayerBowSettings>(out var given) ? given : PlayerBowSettings.Default;
        if (settings.NoAim)
        {
            // The earlier build's (bow-aim-off): attack held (not while the pointer is free) raises the bow and draws; aim held raises it undrawn. A
            // drawn bow stays up through the tick attack is let go, so the release looses it. Aim let go while drawn lets the draw down
            // and lowers the bow, and attack must then be let go before another draw.
            bool aimLetGo = AimHeldBefore(world) && !aim;
            if (!attack) hand.WaitRelease = false;
            if (aimLetGo && attack) hand.WaitRelease = true;
            bool drawing = attack && !hand.WaitRelease;
            intent.Draw = drawing;
            intent.Aim = aim || drawing || (bow.Drawn && !attack && !aimLetGo);
        }
        else
        {
            // The aim mode as designed (to aim, Christof brings
            // the bow up to almost full vertical, held as if ready to draw it back): aim on (held, or latched by its
            // toggle) raises the bow into the ready pose with an arrow nocked; attack held while aiming draws, its release looses; aim let
            // go lets any draw down and lowers the bow (the bow is up only in aim mode, so the bow up with aim off is aim let go), and an
            // attack still held then waits to be let go. Attack without aim does nothing with the bow.
            if (!attack) hand.WaitRelease = false;
            if (!aim && attack && bow.State >= BowState.Raising) hand.WaitRelease = true;
            intent.Aim = aim;
            intent.Draw = aim && attack && !hand.WaitRelease;
        }
        if ((pressed & (Buttons.Block | Buttons.Dodge)) != 0 && bow.Drawn) intent.LetDown = true;   // block, or a dodge (as a dodge cancels a swing)
        if (fightable) { fight.Attack = false; fight.Block = false; }

        // The aim at the crosshair, and the body facing the view while the bow is up.
        if (intent.Aim || bow.State >= BowState.Raising)
        {
            ref var body = ref world.Store<Body>().Find(e);
            // While free look swings the camera round him its crosshair is not his aim: the bow aims along the view's own
            // yaw and pitch (the way he faces), as it was before the swing.
            if (!Unsafe.IsNullRef(ref body) && rig.Orbit != 0f && !settings.NoAim)
            {
                intent.AimGiven = true;
                intent.AimsAt = false;
                intent.AimYaw = rig.Yaw;
                intent.AimPitch = rig.Pitch;
            }
            else if (!Unsafe.IsNullRef(ref body) && Crosshair(world, e, out var point, out bool met))
            {
                // The launch solved when the crosshair is on something; on the open sky (nothing within reach) it leaves along the line
                // to the point that far out (a solve for an empty point would loft it high over the crosshair).
                if (!settings.NoAim && settings.Solve && met)
                {
                    // The launch solved to meet the crosshair's point at the draw reached (its drop allowed for).
                    var (yaw, pitch) = Solve(world, e, in body, in bow, d, point);
                    intent.AimGiven = true;
                    intent.AimsAt = false;
                    intent.AimYaw = yaw;
                    intent.AimPitch = pitch;
                }
                else
                {
                    float yaw = DetMath.Atan2(point.Y - body.Feet.Y, point.X - body.Feet.X);
                    var from = BowServices.ReleasePoint(world, e, in body, yaw, d);
                    var way = point - from;
                    float flat = MathF.Sqrt(way.X * way.X + way.Y * way.Y);
                    if (flat > 1e-3f || MathF.Abs(way.Z) > 1e-3f)
                    {
                        intent.AimGiven = true;
                        intent.AimsAt = false;
                        intent.AimYaw = flat > 1e-3f ? DetMath.Atan2(way.Y, way.X) : yaw;
                        intent.AimPitch = DetMath.Atan2(way.Z, MathF.Max(flat, 1e-4f));
                    }
                }
            }
            move.Facing = DetMath.WrapAngle(rig.Yaw - BowShots.TurnOf(world, e));   // the drawn arrow along the view (1.1's archer stands side-on)
            move.Faces = true;
        }
    }

    /// <summary>
    /// The yaw and pitch (radians)
    /// that carry <paramref name="e"/>'s arrow from its release point to <paramref name="point"/> at the speed of the draw reached
    /// (<see cref="Bow.Share"/>; full draw while undrawn): the yaw straight at it, the pitch by <see cref="Ballistics.SolveAim"/> (the real
    /// flight: gravity and the arrow's drag; only the angle is chosen), the body's own velocity (which the arrow takes with it) allowed
    /// for by aiming that much the other way over the flight's time. Beyond the draw's reach: the farthest angle (it falls short).
    /// </summary>
    public static (float Yaw, float Pitch) Solve(World world, EntityId e, in Body body, in Bow bow, BowData d, Vector3 point)
    {
        float share = bow.Drawn ? MathF.Max(bow.Share, d.LeastShareToLoose) : 1f;
        float speed = d.SpeedAt(share);
        var aimAt = point;
        float yaw = DetMath.Atan2(point.Y - body.Feet.Y, point.X - body.Feet.X), pitch = 0f;
        for (int k = 0; k < 3; k++)
        {
            var from = BowServices.ReleasePoint(world, e, in body, yaw, d);
            var way = aimAt - from;
            float flat = MathF.Sqrt(way.X * way.X + way.Y * way.Y);
            if (flat > 1e-3f) yaw = DetMath.Atan2(way.Y, way.X);
            pitch = Ballistics.SolveAim(from, aimAt, speed, d.ArrowDragPerMetre, out _);
            var v = body.Velocity;
            if (v.LengthSquared() < 1e-8f || speed <= 0f) break;
            float t = MathF.Sqrt((point - from).LengthSquared()) / MathF.Max(1f, speed * DetMath.Cos(pitch));
            aimAt = point - v * t;
        }
        return (yaw, pitch);
    }

    /// <summary>Whether aim mode was held on the tick before (the aim key's let-go lets a draw down: ).</summary>
    static bool AimHeldBefore(World world) => world.TryService<TickInput>(out var input) && input.Previous.Holds(Buttons.Aim);

    /// <summary>
    /// The point under the crosshair for <paramref name="e"/>: the view's ray (<see cref="ViewState"/>, as the last picture was drawn
    /// from) to the first of the level and the other bodies' hit volumes within <see cref="AimReachMetres"/>, else that far along it.
    /// False when there is no view from <paramref name="e"/>.
    /// </summary>
    public static bool Crosshair(World world, EntityId e, out Vector3 point) => Crosshair(world, e, out point, out _);

    /// <summary><see cref="Crosshair(World, EntityId, out Vector3)"/>, and whether the ray met something (the level or a body) within reach.</summary>
    public static bool Crosshair(World world, EntityId e, out Vector3 point, out bool met)
    {
        point = default;
        met = false;
        if (!world.TryService<ViewState>(out var view) || !view.HasView || view.Of != e) return false;
        var eye = view.View.Eye;
        var dir = view.View.Forward;
        float best = AimReachMetres;
        if (world.TryService<LevelSpace>(out var level) && level.Collision.Ray(eye, dir, best) is { } hit) best = hit.Distance;
        if (world.TryService<HitVolumes>(out var volumes))
        {
            var bodies = world.Store<Body>();
            var ents = bodies.Entities;
            for (int i = 0; i < ents.Length; i++)
            {
                if (ents[i] == e) continue;
                if (volumes.Ray(world, ents[i], eye, dir, best, out var on) && on.Hit.Time < best) best = on.Hit.Time;
            }
        }
        met = best < AimReachMetres;
        point = eye + dir * best;
        return true;
    }
}

/// <summary>
/// The player's bow's settings — a service the session gives a world with the player's bow (not digested: it says how the
/// controls read, and a tape carries it in its head, <see cref="TapeNote"/>). A world without it has <see cref="Default"/>.
/// </summary>
public sealed class PlayerBowSettings
{
    /// <summary>Aim mode as designed, the arrow along the line to the crosshair's point, flying as a real arrow (no solve).</summary>
    public static readonly PlayerBowSettings Default = new();

    /// <summary>
    /// The earlier build's player's bow (the switch <see cref="NoAimSwitch"/>, and an older tape that carried the player's bow): attack
    /// held raises and draws without aim, the arrow leaves along the straight line to the crosshair's point with the module's spread.
    /// </summary>
    public bool NoAim { get; init; }

    /// <summary>
    /// The launch solved so the arrow meets the crosshair's point (the setting <c>bowAimSolve</c>, <b>off by default</b>:,
    /// <i>"Go with the flies like real arrow"</i>; the switch <see cref="SolveOnSwitch"/>): off, it leaves along the straight
    /// line to that point and drops as a real arrow does.
    /// </summary>
    public bool Solve { get; init; }

    /// <summary>The switch for the earlier build's player's bow.</summary>
    public const string NoAimSwitch = "bow-aim-off";

    /// <summary>The switch that turns the solve on for one run.</summary>
    public const string SolveOnSwitch = "bow-solve-on";

    /// <summary>
    /// The tape head's note, written for every world with a bow: <c>solve</c> or <c>straight</c> (the earlier build's rules), <c>off</c>.
    /// A tape whose world had a bow and that has no note was made earlier, and replays by the earlier build's rules.
    /// </summary>
    public const string TapeNote = "bowAim";

    /// <summary>The note's value for these settings.</summary>
    public string Note => NoAim ? "noaim" : Solve ? "solve" : "straight";

    /// <summary>The settings a tape's note gives; null when it gives none (an older tape, or one with no bow).</summary>
    public static PlayerBowSettings? FromNote(string? note) => note switch
    {
        "solve" => new PlayerBowSettings { Solve = true },
        "straight" => new PlayerBowSettings(),
        "noaim" => new PlayerBowSettings { NoAim = true },
        _ => null,
    };

    /// <summary>
    /// Whether <paramref name="e"/>'s arrows leave exactly along its aim (no spread drawn at the loose): the player's bow under the earlier build's
    /// rules (the arrow goes where the player aims) and an archer brain's (its error is in its aim, seen before the shot).
    /// </summary>
    public static bool Exact(World world, EntityId e)
    {
        if (world.TryStore<Combat.ArcherBrain>(out var archers) && archers.Has(e)) return true;
        if (!world.TryStore<Combat.BowHand>(out var hands) || !hands.Has(e)) return false;
        return !(world.TryService<PlayerBowSettings>(out var s) ? s : Default).NoAim;
    }
}
