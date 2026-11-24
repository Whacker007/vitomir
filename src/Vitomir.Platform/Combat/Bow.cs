// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the bow v0, headless. 1.1's own missiles (src/Vitomir.Engine/Rpg/Projectiles.cs) were read for reference
// only; nothing is taken from them (1.1 launches a missile along the shooter's facing at a fixed speed and decides its hit by
// a test against the target's box; here the arrow is a body in flight, swept tick to tick: Projectiles.cs).
using System.Numerics;
using System.Runtime.CompilerServices;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using static Vitomir.Platform.Combat.Geometry;

namespace Vitomir.Platform.Combat;

/// <summary>
/// What an archer wants of its bow this tick (<i>"aim mode … holds up the bow and nocks
/// an arrow. Holding down the attack button starts the draw sequence, ending at full draw. As soon as attack is released, the player
/// releases the arrow … The draw is cancellable at any point"</i>). Plain data, written by a script (the scenario director's bow
/// steps) and, by the player's controls; read by <see cref="BowSystem"/>. Nobody else writes it (the duelling brain knows
/// no bow), so its fields stand from tick to tick until changed. Its own component, not fields of <see cref="CombatIntent"/>: that
/// one is digested in every world, and a field added there would change every digest; this one is registered only in a world with
/// a bow (<see cref="BowServices.Register"/>).
/// </summary>
public struct BowIntent
{
    int ranged, aim, draw, loose, letDown, aimGiven, aimsAt, aimRegion;

    /// <summary>The bow in hand (the script's <c>equip: "ranged"</c>); false, the melee weapon (<c>equip: "melee"</c>). Instant.</summary>
    public bool Ranged { readonly get => ranged != 0; set => ranged = value ? 1 : 0; }

    /// <summary>Aim mode held: the bow raised and an arrow nocked by itself (the bow's <c>nock_seconds</c>). Let go: the bow back low, any draw let down.</summary>
    public bool Aim { readonly get => aim != 0; set => aim = value ? 1 : 0; }

    /// <summary>Attack held while aiming: the draw rises with time to <see cref="DrawToShare"/> and holds there. Let go after a draw: the arrow is loosed.</summary>
    public bool Draw { readonly get => draw != 0; set => draw = value ? 1 : 0; }

    /// <summary>The share of full draw the draw stops at and holds (0, or 1 and above: full draw).</summary>
    public float DrawToShare;

    /// <summary>A press: loose now at the draw reached (the script's <c>loose</c>; letting <see cref="Draw"/> go does the same). Consumed by the bow.</summary>
    public bool Loose { readonly get => loose != 0; set => loose = value ? 1 : 0; }

    /// <summary>A press: let the string down without loosing (the player's cancel, the script's <c>cancelDraw</c>). Consumed by the bow.</summary>
    public bool LetDown { readonly get => letDown != 0; set => letDown = value ? 1 : 0; }

    /// <summary>Whether the aim is held on a body (<see cref="AimAt"/>, <see cref="AimRegion"/>): the bow keeps it there itself each tick, the arrow's drop allowed for.</summary>
    public bool AimsAt { readonly get => aimsAt != 0; set => aimsAt = value ? 1 : 0; }

    /// <summary>The body the aim is held on, when <see cref="AimsAt"/>.</summary>
    public EntityId AimAt;

    /// <summary>The part of it the aim is held on (<see cref="BodyRegion.None"/>: the chest).</summary>
    public BodyRegion AimRegion { readonly get => (BodyRegion)aimRegion; set => aimRegion = (int)value; }

    /// <summary>Whether <see cref="AimYaw"/> and <see cref="AimPitch"/> give the aim (when not <see cref="AimsAt"/>); else the body's facing and level.</summary>
    public bool AimGiven { readonly get => aimGiven != 0; set => aimGiven = value ? 1 : 0; }

    /// <summary>The aim's direction when <see cref="AimGiven"/>: radians, the yaw counter-clockwise from +X in the world, the pitch up positive.</summary>
    public float AimYaw, AimPitch;

}

/// <summary>Where a bow is (<see cref="Bow.State"/>).</summary>
public enum BowState
{
    /// <summary>Put away: the melee weapon is in hand.</summary>
    Away = 0,
    /// <summary>In hand, low at the waist (with or without an arrow on the string).</summary>
    Ready = 1,
    /// <summary>Being raised in aim mode, an arrow being nocked (the bow's <c>nock_seconds</c>, or <c>raise_seconds</c> with one already on the string).</summary>
    Raising = 2,
    /// <summary>Raised, an arrow nocked, undrawn.</summary>
    Nocked = 3,
    /// <summary>The draw rising.</summary>
    Drawing = 4,
    /// <summary>Held at the draw it stopped at (full, or <see cref="BowIntent.DrawToShare"/>).</summary>
    Held = 5,
}

/// <summary>
/// A body's bow (digested): which bow it carries (an index into <see cref="BowConfig.Bows"/>), the arrows left in its
/// quiver, the shots loosed, the state and how long it has been in it, the draw's share and the aim it holds now (radians).
/// </summary>
public struct Bow
{
    public int Weapon;
    public int Arrows;
    public int Shots;
    /// <summary>The number the arrow on the string will carry when loosed (<see cref="Shots"/> + 1 once nocked), 0 with none.</summary>
    public int Shot;
    int state, onString, inHand, wasDraw, waitRelease;
    public float Seconds;
    public float Share;
    public float AimYaw, AimPitch;

    public BowState State { readonly get => (BowState)state; set => state = (int)value; }
    /// <summary>Whether an arrow is on the string.</summary>
    public bool OnString { readonly get => onString != 0; set => onString = value ? 1 : 0; }
    /// <summary>Whether the bow is in hand (its melee weapon put away).</summary>
    public bool InHand { readonly get => inHand != 0; set => inHand = value ? 1 : 0; }
    /// <summary>Whether <see cref="BowIntent.Draw"/> was held last tick.</summary>
    public bool WasDraw { readonly get => wasDraw != 0; set => wasDraw = value ? 1 : 0; }
    /// <summary>After a let-down with the draw still held: the draw waits for it to be let go and pressed again.</summary>
    public bool WaitRelease { readonly get => waitRelease != 0; set => waitRelease = value ? 1 : 0; }
    /// <summary>Whether the string is drawn (drawing or held).</summary>
    public readonly bool Drawn => State is BowState.Drawing or BowState.Held;
}

/// <summary>
/// One bow's numbers (the module's data, <c>Data/combat/ranged.json</c>, every number SANDBOX with its source there):
/// the draw (its weight, for the record; the seconds to full draw), the arrow's speed at full draw and how it goes with the draw's
/// share (<c>speed = full × share^exponent</c>), the least share that looses, the arrow (mass, drag, radius), the nock and the raise,
/// where the arrow leaves the body, and when a flight that met nothing is given up.
/// </summary>
public sealed record BowData(string Id, float DrawWeightNewtons, float FullDrawSeconds, float SpeedAtFullDrawMetresPerSecond, float SpeedExponent,
    float LeastShareToLoose, float ArrowMassKg, float ArrowDragPerMetre, float ArrowRadiusMetres, float NockSeconds, float RaiseSeconds,
    float ReleaseHeightShare, float ReleaseForwardMetres, float FlightSecondsAtMost, float FallenBelowMetres, float SwapSeconds = BowData.DefaultSwapSeconds)
{
    /// <summary>The player's swap between the melee weapon and the bow, each way (ENGINE_SANDBOX; ranged.json's <c>swap_seconds</c>).</summary>
    public const float DefaultSwapSeconds = 0.6f;

    /// <summary>The arrow's speed loosed at <paramref name="share"/> of full draw (m/s).</summary>
    public float SpeedAt(float share)
    {
        share = Math.Clamp(share, 0f, 1f);
        return SpeedAtFullDrawMetresPerSecond * (SpeedExponent == 1f ? share : DetMath.Pow(share, SpeedExponent));
    }
}

/// <summary>
/// The bows a world knows (a service the rules module fills from its data, as it fills <see cref="ContactConfig"/>), and
/// the module's hook for an archer's aim spread (degrees, one standard deviation each way: the module reads it from the archer's
/// sheet: its Firearms, the module's "Range"; none or 0: the arrow leaves exactly along the aim).
/// </summary>
public sealed class BowConfig
{
    public List<BowData> Bows { get; } = [];

    /// <summary>
    /// The module's seconds to full draw for an
    /// archer (by its Range), read as a draw begins; null or 0: the bow's own <see cref="BowData.FullDrawSeconds"/>. Not with the earlier build's rules.
    /// </summary>
    public Func<World, EntityId, float>? FullDrawSeconds { get; set; }

    /// <summary>The archer brain's numbers the module's data gives (<c>ranged.json</c>'s <c>archer_brain</c>), the defaults else.</summary>
    public ArcherBrainSettings Archer { get; set; } = new();

    /// <summary>The module's spread of an archer's aim (degrees), read at each loose; null: none.</summary>
    public Func<World, EntityId, float>? SpreadDegrees { get; set; }

    /// <summary>The index of the bow <paramref name="id"/>, or −1.</summary>
    public int IndexOf(string id)
    {
        for (int i = 0; i < Bows.Count; i++) if (string.Equals(Bows[i].Id, id, StringComparison.Ordinal)) return i;
        return -1;
    }
}

/// <summary>
/// The bow (<see cref="Phase.Act"/>, <see cref="BowServices.BowOrder"/>, before the attacks): for each body with a
/// <see cref="Bow"/>, from its <see cref="BowIntent"/>:
/// <list type="bullet">
/// <item><b>Equip</b>: <see cref="BowIntent.Ranged"/> puts the bow in hand (instant; its look is the earlier build's) and the melee weapon
/// away: while the bow is in hand the body's <see cref="CombatIntent.Attack"/> and <see cref="CombatIntent.Block"/> are cleared here,
/// so no melee blow begins and no guard rises whoever asks. Let go, the bow is put away (a draw let down, an arrow on the string back
/// in the quiver).</item>
/// <item><b>Aim</b> held raises the bow and nocks an arrow (<see cref="BowData.NockSeconds"/>; <see cref="BowData.RaiseSeconds"/> with one
/// already on the string): <see cref="ArrowNocked"/>. Let go, the bow goes back low, a draw let down (<see cref="DrawCancelCause.AimLetGo"/>:)
/// .</item>
/// <item><b>Draw</b> held with an arrow nocked draws (<see cref="DrawBegan"/>): the share rises by a tick over
/// <see cref="BowData.FullDrawSeconds"/> to <see cref="BowIntent.DrawToShare"/> (full by default) and holds there.</item>
/// <item><b>Loose</b> (the press, or the draw let go) looses the arrow at <see cref="BowData.SpeedAt"/> the share reached, along the
/// aim with the module's spread drawn (<c>platform.bow.spread</c>), from the release point (the body's height times
/// <see cref="BowData.ReleaseHeightShare"/>, <see cref="BowData.ReleaseForwardMetres"/> in front along the aim), the body's own velocity
/// added: an <see cref="Arrow"/> in flight (<see cref="ProjectileSystem"/>) and <see cref="ArrowLoosed"/>. Below
/// <see cref="BowData.LeastShareToLoose"/> it is let down instead. Still aiming with arrows left, the next is nocked.</item>
/// <item><b>Let down</b> (the press), a flinch (a hurt played), or the body stopped: the draw let down (<see cref="DrawCancelled"/>), the
/// arrow kept on the string.</item>
/// <item><b>The aim</b>, each tick the bow is up: on a body (<see cref="BowIntent.AimsAt"/>), the posed hit volume of the region
/// (the chest by default) from the release point, the pitch solved by <see cref="Ballistics.SolvePitch"/> for the speed the draw is
/// going to (the arrow's drop and drag allowed for, as an archer allows for them; the target's motion is not led); else the given
/// yaw and pitch; else the body's facing, level (or a blow's aim pitch).</item>
/// </list>
/// Deterministic; draws only at a loose with a spread; allocates nothing per tick (an arrow's entity is made at its loose).
/// </summary>
public sealed class BowSystem : ISystem
{
    Rng? spread;

    public void Run(World world)
    {
        if (!world.TryStore<Bow>(out var bows) || bows.Count == 0) return;
        var config = world.Service<BowConfig>();
        var intents = world.Store<BowIntent>();
        world.TryStore<CombatIntent>(out var fights);
        var bodies = world.Store<Body>();
        world.TryStore<Dead>(out var dead);
        world.TryStore<BowHand>(out var hands);   // the player's bow
        var ents = bows.Entities;
        var data = bows.Data;
        float dt = World.TickSeconds;
        for (int i = 0; i < ents.Length; i++)
        {
            var e = ents[i];
            ref var bow = ref data[i];
            ref var intent = ref intents.Find(e);
            if (Unsafe.IsNullRef(ref intent) || (uint)bow.Weapon >= (uint)config.Bows.Count) continue;
            var d = config.Bows[bow.Weapon];
            ref var fight = ref fights is null ? ref Unsafe.NullRef<CombatIntent>() : ref fights.Find(e);
            bool stopped = dead is not null && dead.Has(e);
            // The player's bow (a body with a BowHand) nocks the next arrow while it is low, so the next draw needs only the
            // raise (1.1's shot cycle is 1.33 s); a script's bow nocks only as aim raises it.
            bool nockReady = hands is not null && hands.Has(e);

            // A let-down asked with the bow put away in the same tick is the let-down (a script's cancelDraw then equip melee).
            if (intent.LetDown && bow.Drawn && !intent.Ranged) { Cancel(world, e, ref bow, DrawCancelCause.LetDown); bow.State = BowState.Nocked; }
            // Equip and put away.
            if (intent.Ranged && !bow.InHand && !stopped)
            {
                bow.InHand = true;
                bow.State = BowState.Ready;
                bow.Seconds = 0f;
            }
            else if (!intent.Ranged && bow.InHand)
            {
                if (bow.Drawn) Cancel(world, e, ref bow, DrawCancelCause.Unequipped);
                if (bow.OnString) { bow.OnString = false; bow.Arrows++; bow.Shot = 0; }
                bow.InHand = false;
                bow.State = BowState.Away;
            }
            if (!bow.InHand) { Consume(ref intent, ref bow); continue; }
            // The melee weapon is put away while the bow is in hand: no blow begins, no guard rises.
            if (!Unsafe.IsNullRef(ref fight)) { fight.Attack = false; fight.Block = false; }

            if (stopped)
            {
                if (bow.Drawn) Cancel(world, e, ref bow, DrawCancelCause.Stopped);
                bow.State = BowState.Ready;
                Consume(ref intent, ref bow);
                continue;
            }
            if (bow.Drawn && !Unsafe.IsNullRef(ref fight) && fight.Play == ActionRole.Hurt)
            {
                Cancel(world, e, ref bow, DrawCancelCause.Hurt);
                bow.State = BowState.Nocked;
                bow.WaitRelease = intent.Draw;
            }

            // Aim mode: the bow up and an arrow nocked, or back low.
            if (!intent.Aim)
            {
                if (bow.Drawn) Cancel(world, e, ref bow, DrawCancelCause.AimLetGo);
                if (bow.State is BowState.Raising or BowState.Nocked or BowState.Drawing or BowState.Held) { bow.State = BowState.Ready; bow.Seconds = 0f; }
                // The player's bow nocks the next arrow while it is low.
                else if (nockReady && bow.State == BowState.Ready && !bow.OnString && bow.Arrows > 0)
                {
                    bow.Seconds += dt;
                    if (bow.Seconds >= d.NockSeconds - 1e-5f) { Nock(world, e, ref bow); bow.Seconds = 0f; }
                }
            }
            else
            {
                // A nock begun while low (the player's bow) carries on as the bow is raised.
                if (bow.State == BowState.Ready && (bow.OnString || bow.Arrows > 0)) { bow.State = BowState.Raising; if (!nockReady || bow.OnString) bow.Seconds = 0f; }
                if (bow.State == BowState.Raising)
                {
                    bow.Seconds += dt;
                    float need = bow.OnString ? d.RaiseSeconds : d.NockSeconds;
                    if (bow.Seconds >= need - 1e-5f)
                    {
                        if (!bow.OnString) Nock(world, e, ref bow);
                        bow.State = BowState.Nocked;
                        bow.Seconds = 0f;
                    }
                }
            }
            if (bow.State >= BowState.Raising) AimNow(world, e, ref bow, in intent, in fight, d, bodies);

            // The draw, the loose and the let-down.
            if (!intent.Draw) bow.WaitRelease = false;
            float limit = intent.DrawToShare > 0f ? MathF.Min(1f, intent.DrawToShare) : 1f;
            if (bow.State == BowState.Nocked && intent.Draw && !bow.WaitRelease && !intent.LetDown)
            {
                bow.State = BowState.Drawing;
                bow.Share = 0f;
                bow.Seconds = 0f;
                world.Events<DrawBegan>().Publish(new DrawBegan(e, world.Tick, bow.Shot, limit));
            }
            else if (bow.Drawn)
            {
                if (intent.LetDown)
                {
                    Cancel(world, e, ref bow, DrawCancelCause.LetDown);
                    bow.State = BowState.Nocked;
                    bow.WaitRelease = intent.Draw;
                }
                else if (intent.Loose || (!intent.Draw && bow.WasDraw))
                {
                    if (bow.Share + 1e-5f >= d.LeastShareToLoose) Loose(world, e, ref bow, in intent, d, config, bodies);
                    else { Cancel(world, e, ref bow, DrawCancelCause.TooLittle); bow.State = BowState.Nocked; }
                }
                else
                {
                    bow.Seconds += dt;
                    if (bow.State == BowState.Held && limit > bow.Share + 1e-5f) bow.State = BowState.Drawing;
                    if (bow.State == BowState.Drawing)
                    {
                        bow.Share = MathF.Min(limit, bow.Share + dt / MathF.Max(dt, DrawSeconds(world, e, d, config)));   // by the archer's Range
                        if (bow.Share >= limit - 1e-5f) { bow.Share = limit; bow.State = BowState.Held; }
                    }
                }
            }
            bow.WasDraw = intent.Draw;
            Consume(ref intent, ref bow);
        }
    }

    /// <summary>The seconds to full draw for <paramref name="e"/>: the module's by its Range, else the bow's own (always the bow's own under the earlier build's rules).</summary>
    public static float DrawSeconds(World world, EntityId e, BowData d, BowConfig config)
    {
        if (config.FullDrawSeconds is null || (world.TryService<PlayerBowSettings>(out var s) && s.NoAim)) return d.FullDrawSeconds;
        float t = config.FullDrawSeconds(world, e);
        return t > 0f ? t : d.FullDrawSeconds;
    }

    static void Nock(World world, EntityId e, ref Bow bow)
    {
        bow.OnString = true;
        bow.Arrows--;
        bow.Shot = bow.Shots + 1;
        world.Events<ArrowNocked>().Publish(new ArrowNocked(e, world.Tick, bow.Shot, bow.Arrows));
    }

    static void Consume(ref BowIntent intent, ref Bow bow)
    {
        intent.Loose = false;
        intent.LetDown = false;
    }

    static void Cancel(World world, EntityId e, ref Bow bow, DrawCancelCause cause)
    {
        world.Events<DrawCancelled>().Publish(new DrawCancelled(e, world.Tick, bow.Shot, bow.Share, cause));
        bow.Share = 0f;
        bow.Seconds = 0f;
    }

    /// <summary>The aim held now (<see cref="Bow.AimYaw"/>, <see cref="Bow.AimPitch"/>): on a body's region, given, or the facing.</summary>
    static void AimNow(World world, EntityId e, ref Bow bow, in BowIntent intent, in CombatIntent fight, BowData d, Store<Body> bodies)
    {
        ref var body = ref bodies.Find(e);
        if (Unsafe.IsNullRef(ref body)) return;
        ref var aimed = ref intent.AimsAt && world.Alive(intent.AimAt) ? ref bodies.Find(intent.AimAt) : ref Unsafe.NullRef<Body>();
        if (!Unsafe.IsNullRef(ref aimed)
            && BowServices.RegionPoint(world, intent.AimAt, intent.AimRegion,
                BowServices.ReleasePoint(world, e, in body, DetMath.Atan2(aimed.Feet.Y - body.Feet.Y, aimed.Feet.X - body.Feet.X), d), out var target))
        {
            float yaw = DetMath.Atan2(target.Y - body.Feet.Y, target.X - body.Feet.X);
            var from = BowServices.ReleasePoint(world, e, in body, yaw, d);
            yaw = DetMath.Atan2(target.Y - from.Y, target.X - from.X);
            float limit = intent.DrawToShare > 0f ? MathF.Min(1f, intent.DrawToShare) : 1f;
            float speed = d.SpeedAt(MathF.Max(limit, d.LeastShareToLoose));
            bow.AimYaw = yaw;
            bow.AimPitch = Ballistics.SolvePitch(from, target, speed, d.ArrowDragPerMetre);
        }
        else if (intent.AimGiven)
        {
            bow.AimYaw = intent.AimYaw;
            bow.AimPitch = intent.AimPitch;
        }
        else
        {
            bow.AimYaw = body.Yaw;
            bow.AimPitch = !Unsafe.IsNullRef(in fight) && fight.Aims ? fight.AimPitch : 0f;
        }
    }

    void Loose(World world, EntityId e, ref Bow bow, in BowIntent intent, BowData d, BowConfig config, Store<Body> bodies)
    {
        ref var body = ref bodies.Find(e);
        if (Unsafe.IsNullRef(ref body)) return;
        float share = bow.Share;
        float speed = d.SpeedAt(share);
        float yaw = bow.AimYaw, pitch = bow.AimPitch;
        // The player's arrow goes where he aims, and an archer brain's error is in its aim: no spread drawn for either.
        float spreadDegrees = PlayerBowSettings.Exact(world, e) ? 0f : config.SpreadDegrees?.Invoke(world, e) ?? 0f;
        if (spreadDegrees > 0f)
        {
            spread ??= world.Random.For(BowServices.SpreadStream);
            float u1 = MathF.Max(1e-7f, spread.NextFloat()), u2 = spread.NextFloat();
            float r = MathF.Sqrt(-2f * DetMath.Log(u1));
            var (s, c) = DetMath.SinCos(2f * MathF.PI * u2);
            float sd = spreadDegrees * DetMath.DegToRad;
            yaw += r * c * sd;
            pitch += r * s * sd;
        }
        var from = BowServices.ReleasePoint(world, e, in body, yaw, d);
        var velocity = DetMath.Direction(yaw, pitch) * speed + body.Velocity;
        bow.Shots++;
        int shot = bow.Shot > 0 ? bow.Shot : bow.Shots;
        var arrow = world.Create();
        world.Set(arrow, new Arrow
        {
            Archer = e, Shot = shot, Bow = bow.Weapon, Position = from, From = from, Velocity = velocity, LoosedTick = world.Tick,
            MassKg = d.ArrowMassKg, DragPerMetre = d.ArrowDragPerMetre, RadiusMetres = d.ArrowRadiusMetres, LaunchSpeed = velocity.Length(),
            Flying = true, Heading = Normalize(velocity, Vector3.UnitX),
        });
        world.Events<ArrowLoosed>().Publish(new ArrowLoosed(e, world.Tick, shot, share, speed, from, velocity,
            intent.AimsAt ? intent.AimAt : EntityId.None, intent.AimsAt ? (intent.AimRegion == BodyRegion.None ? BodyRegion.Chest : intent.AimRegion) : BodyRegion.None,
            yaw * DetMath.RadToDeg, pitch * DetMath.RadToDeg, spreadDegrees, bow.Arrows));
        bow.OnString = false;
        bow.Shot = 0;
        bow.Share = 0f;
        bow.Seconds = 0f;
        bow.State = intent.Aim && bow.Arrows > 0 ? BowState.Raising : BowState.Ready;
    }
}

/// <summary>What of the earlier build's a bow world registers (<see cref="BowServices.Register(WorldBuilder, BowConfig?, BowExtras)"/>).</summary>
[Flags]
public enum BowExtras
{
    None = 0,
    /// <summary>The player's swap and nock (<see cref="BowHand"/>): a controlled body with a bow.</summary>
    PlayerHand = 1,
    /// <summary>The bow's 1.1 look (<see cref="BowLook"/>, <see cref="BowLookSystem"/>): not with <c>bow-look-off</c>.</summary>
    Look = 2,
    /// <summary>A shield raised against a drawn bow (<see cref="ShieldWatch"/>, <see cref="ShieldAgainstBowSystem"/>): not with <c>shield-at-bow-off</c>.</summary>
    Shield = 4,
    All = PlayerHand | Look | Shield,
}

/// <summary>The bow registered in one call, its helpers, and how a body is given one.</summary>
public static class BowServices
{
    /// <summary>The bow's place in <see cref="Phase.Act"/>: before the attacks (<see cref="CombatServices.AttackOrder"/>, 100), which then see no melee press from a body with its bow in hand.</summary>
    public const int BowOrder = 95;

    /// <summary>The arrows' place in <see cref="Phase.Contact"/>: after the blades' (<see cref="ContactServices.ContactOrder"/>, 60), so an arrow's contact id follows the blades' of the tick.</summary>
    public const int ProjectileOrder = 65;

    /// <summary>The bow's look (<see cref="BowLookSystem"/>) in <see cref="Phase.Act"/>, after the bow (95) and before the attacks (100).</summary>
    public const int BowLookOrder = 96;

    /// <summary>The random stream an aim's spread is drawn from.</summary>
    public const string SpreadStream = "platform.bow.spread";

    /// <summary>
    /// Registers the components <see cref="BowIntent"/>, <see cref="Bow"/> and <see cref="Arrow"/>; the events <see cref="ArrowNocked"/>,
    /// <see cref="DrawBegan"/>, <see cref="ArrowLoosed"/>, <see cref="ArrowSpent"/> and <see cref="DrawCancelled"/>; the service
    /// <see cref="BowConfig"/> (for the rules module to fill); the systems <c>Act/95 platform.bow</c> and <c>Contact/65
    /// platform.projectiles</c>. <b>Only in a world with a bow</b> (the session's: a cast fighter with a ranged weapon): a world
    /// without one has none of it, digest for digest. After contact (<see cref="ContactServices.Register"/>), before the modules.
    /// </summary>
    public static WorldBuilder Register(WorldBuilder b, BowConfig? config = null) => Register(b, config, BowExtras.All);

    /// <summary>
    /// <see cref="Register(WorldBuilder, BowConfig?)"/> with only <paramref name="extras"/> of the earlier build's (the player's swap and
    /// nock, <see cref="BowHand"/>; the bow's 1.1 look, <see cref="BowLook"/>; the shield raised against a drawn bow,
    /// <see cref="ShieldWatch"/>): a world with none of them is the earlier build's bow world, digest for digest (the session's, with <c>bow-look-off</c>
    /// and <c>shield-at-bow-off</c> and no player's bow).
    /// </summary>
    public static WorldBuilder Register(WorldBuilder b, BowConfig? config, BowExtras extras)
    {
        b.Component<BowIntent>().Component<Bow>().Component<Arrow>();
        if (!b.Peek.TryService<EventQueue<ContactMade>>(out _)) b.Event<ContactMade>();
        b.Event<ArrowNocked>().Event<DrawBegan>().Event<ArrowLoosed>().Event<ArrowSpent>().Event<DrawCancelled>();
        b.Service(config ?? new BowConfig());
        b.System(Phase.Act, BowOrder, "platform.bow", new BowSystem());
        b.System(Phase.Contact, ProjectileOrder, "platform.projectiles", new ProjectileSystem());
        // ---- the player's swap, the bow's look (its 1.1 kind and the shot's clip), a shield raised against a drawn bow
        b.Event<WeaponSwap>().Event<ShieldAgainstBow>();
        if ((extras & BowExtras.PlayerHand) != 0) b.Component<BowHand>();
        if ((extras & BowExtras.Look) != 0)
        {
            b.Component<BowLook>();
            b.System(Phase.Act, BowLookOrder, "platform.bow.look", new BowLookSystem());
        }
        if ((extras & BowExtras.Shield) != 0) ShieldAgainstBows.Register(b);
        // ---- end
        return b;
    }

    /// <summary>Gives <paramref name="e"/> the bow <paramref name="bowId"/> (<see cref="BowConfig"/>'s) and <paramref name="arrows"/> arrows, put away; false when the world has no bow by that id.</summary>
    public static bool Give(World world, EntityId e, string bowId, int arrows)
    {
        if (!world.TryService<BowConfig>(out var config)) return false;
        int index = config.IndexOf(bowId);
        if (index < 0) return false;
        world.Set(e, new Bow { Weapon = index, Arrows = Math.Max(0, arrows) });
        world.Set(e, new BowIntent());
        return true;
    }

    /// <summary>Where an arrow leaves <paramref name="e"/>'s body aiming along <paramref name="yaw"/>: the body's height times the bow's share, in front along the aim.</summary>
    public static Vector3 ReleasePoint(World world, EntityId e, in Body body, float yaw, BowData d)
    {
        ref var loco = ref world.Store<Locomotion>().Find(e);
        float height = Unsafe.IsNullRef(ref loco) ? 1.8f : body.CapsuleHeight(loco);
        var (s, c) = DetMath.SinCos(yaw);
        return body.Feet + new Vector3(c * d.ReleaseForwardMetres, s * d.ReleaseForwardMetres, height * d.ReleaseHeightShare);
    }

    /// <summary>
    /// The point an aim at <paramref name="region"/> of <paramref name="target"/> is held on, seen from <paramref name="from"/>: on that
    /// region's posed hit volume (<see cref="HitVolumes.Posed(World, EntityId)"/>; <see cref="BodyRegion.None"/> is the chest), the first of
    /// its middle, its two ends and its middle raised and lowered by 0.6 of its radius whose straight line from <paramref name="from"/>
    /// meets that region before any other part of the body (an archer aims at the part of it he can see: the belly hides the hips' middle
    /// from in front); its middle when none does. A body without that region (a townsman's skeleton has no neck volume), or without
    /// volumes: 0.72 of the body's height over its feet. False when the target has no body.
    /// </summary>
    public static bool RegionPoint(World world, EntityId target, BodyRegion region, Vector3 from, out Vector3 point)
    {
        point = default;
        if (region == BodyRegion.None) region = BodyRegion.Chest;
        if (world.TryService<HitVolumes>(out var volumes))
        {
            var posed = volumes.Posed(world, target);
            for (int v = 0; v < posed.Length; v++)
            {
                if (posed[v].Region != region) continue;
                var c = posed[v].Capsule;
                var mid = (c.A + c.B) * 0.5f;
                var up = new Vector3(0f, 0f, 0.6f * c.Radius);
                for (int k = 0; k < 5; k++)
                {
                    var p = k switch { 0 => mid, 1 => c.A, 2 => c.B, 3 => mid + up, _ => mid - up };
                    if (Seen(posed, from, p) == region) { point = p; return true; }
                }
                point = mid;
                return true;
            }
        }
        ref var body = ref world.Store<Body>().Find(target);
        if (Unsafe.IsNullRef(ref body)) return false;
        ref var loco = ref world.Store<Locomotion>().Find(target);
        float height = Unsafe.IsNullRef(ref loco) ? 1.8f : body.CapsuleHeight(loco);
        point = body.Feet + new Vector3(0f, 0f, height * 0.72f);
        return true;
    }

    /// <summary>The region of the first of <paramref name="posed"/> the straight line from <paramref name="from"/> to <paramref name="to"/> meets (none: none).</summary>
    static BodyRegion Seen(ReadOnlySpan<HitVolume> posed, Vector3 from, Vector3 to)
    {
        var way = to - from;
        float length = Length(way);
        if (length < 1e-4f) return BodyRegion.None;
        var d = way / length;
        float best = float.PositiveInfinity;
        var region = BodyRegion.None;
        for (int v = 0; v < posed.Length; v++)
            if (ContactMath.Ray(from, d, length + 0.5f, posed[v].Capsule, out var h) && h.Time < best) { best = h.Time; region = posed[v].Region; }
        return region;
    }
}
