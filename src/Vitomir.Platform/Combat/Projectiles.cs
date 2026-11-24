// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: projectiles as bodies in flight (the bow and bullet
// mechanics largely follow real life in projectile speed and gravity response). 1.1 decides a missile's hit by a test
// against the target's box (src/Vitomir.Engine/Rpg/Projectiles.cs, read for reference only; nothing taken).
using System.Numerics;
using System.Runtime.CompilerServices;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Space;
using static Vitomir.Platform.Combat.Geometry;

namespace Vitomir.Platform.Combat;

/// <summary>
/// An arrow (digested): an entity of its own, made at its loose (<see cref="BowSystem"/>). In flight it is a point — its
/// head — with a velocity, moved and swept each tick by <see cref="ProjectileSystem"/>; once it meets something it stops there (stuck,
/// <see cref="StuckIn"/> the person met, none for the level) and is not moved again; it stays for the picture.
/// </summary>
public struct Arrow
{
    public EntityId Archer;
    /// <summary>The archer's shot number (<see cref="ArrowLoosed.Shot"/>).</summary>
    public int Shot;
    /// <summary>The bow it was loosed from (an index into <see cref="BowConfig.Bows"/>).</summary>
    public int Bow;
    /// <summary>Where its head is now, where it was loosed from (m), and its velocity (m/s).</summary>
    public Vector3 Position, From, Velocity;
    public long LoosedTick;
    /// <summary>The arrow's own numbers, copied from its bow's data at the loose: mass (kg), drag (per metre: the deceleration is drag × speed²), radius (m) and its speed as loosed (m/s).</summary>
    public float MassKg, DragPerMetre, RadiusMetres, LaunchSpeed;
    int flying;
    /// <summary>The person it stopped in or on (a body, a shield, a blade), none for the level or while it flies.</summary>
    public EntityId StuckIn;
    /// <summary>The contact it stopped with (<see cref="ContactMade.ContactId"/>), 0 while it flies or when it met nothing.</summary>
    public long ContactId;

    public bool Flying { readonly get => flying != 0; set => flying = value ? 1 : 0; }

    /// <summary>Its heading (a unit vector): along its velocity in flight, kept as it was when it stopped (for the picture).</summary>
    public Vector3 Heading;

    /// <summary>
    /// What it is stuck in, for the picture: 0 nothing (the level, or in flight), 1 + the index of the body's hit volume, −1 the shield,
    /// −2 the blade; and its head and heading in that thing's frame then (<see cref="StuckLocal"/>, <see cref="StuckHeadingLocal"/>), so
    /// it is drawn moving with it.
    /// </summary>
    public int StuckPart;
    public Vector3 StuckLocal, StuckHeadingLocal;

    /// <summary>The length of an arrow drawn in a picture (m; presentation only).</summary>
    public const float DrawnLength = 0.8f;

    /// <summary>
    /// Every arrow as a line for the picture (<see cref="Presentation.SwingTrailKind.Arrow"/>: its shaft from its head back
    /// <see cref="DrawnLength"/> along its heading), in flight and stuck. Presentation only; nothing when the world has no arrows.
    /// </summary>
    /// <summary>
    /// Where <paramref name="a"/> is drawn now — its head and heading, in flight or where it stopped, moving with the body
    /// part, shield or blade it is stuck in (presentation only).
    /// </summary>
    public static void Placed(World world, in Arrow a, out Vector3 head, out Vector3 heading)
    {
        head = a.Position;
        heading = a.Heading;
        if (a.StuckPart != 0 && world.Alive(a.StuckIn) && ProjectileSystem.FrameOf(world, a.StuckIn, a.StuckPart, out var frame))
        {
            head = frame.FromLocal(a.StuckLocal);
            heading = frame.DirectionFromLocal(a.StuckHeadingLocal);
        }
    }

    public static void Fill(World world, List<Presentation.SwingTrailLine> into)
    {
        if (!world.TryStore<Arrow>(out var arrows)) return;
        foreach (var a in arrows.Data)
        {
            var head = a.Position;
            var heading = a.Heading;
            if (a.StuckPart != 0 && world.Alive(a.StuckIn) && ProjectileSystem.FrameOf(world, a.StuckIn, a.StuckPart, out var frame))
            {
                head = frame.FromLocal(a.StuckLocal);
                heading = frame.DirectionFromLocal(a.StuckHeadingLocal);
            }
            into.Add(new Presentation.SwingTrailLine(head - heading * DrawnLength, head, Presentation.SwingTrailKind.Arrow, 1f));
        }
    }
}

/// <summary>
/// The flight of a projectile: gravity 9.81 m/s² down (+Z up) and a quadratic air drag, <c>a = g − k·|v|·v</c> with
/// <c>k</c> the arrow's <see cref="Arrow.DragPerMetre"/> (SANDBOX, the bow's data), stepped by the midpoint rule a tick at a time —
/// exact for gravity alone (a parabola, to the float's rounding), and within micrometres of a fine integration with the drag over a
/// bow's ranges. The aim's solve (<see cref="SolvePitch"/>) flies the same steps, so the aim and the shot cannot disagree.
/// </summary>
public static class Ballistics
{
    /// <summary>Gravity (m/s²), down.</summary>
    public const float Gravity = 9.81f;

    /// <summary>The steepest pitch an aim is solved to (45°: the farthest shot without drag).</summary>
    public const float MaxPitch = 0.7853982f;

    /// <summary>The most ticks <see cref="HeightAt"/> flies (10 s).</summary>
    public const int MaxSteps = 1200;

    /// <summary>The acceleration at velocity <paramref name="v"/> (m/s²).</summary>
    public static Vector3 Acceleration(Vector3 v, float drag) => new Vector3(0f, 0f, -Gravity) - v * (drag * Length(v));

    /// <summary>One step of <paramref name="dt"/> seconds: the midpoint rule (the velocity at the step's middle carries the position; the acceleration there changes the velocity).</summary>
    public static void Step(ref Vector3 position, ref Vector3 velocity, float drag, float dt)
    {
        var a0 = Acceleration(velocity, drag);
        var mid = velocity + a0 * (0.5f * dt);
        var am = Acceleration(mid, drag);
        position += mid * dt;
        velocity += am * dt;
    }

    /// <summary>
    /// The height (m, from the start) at which a shot loosed at <paramref name="pitch"/> (radians) and <paramref name="speed"/> (m/s)
    /// crosses <paramref name="horizontal"/> metres out, flown a tick at a time and taken between the two ticks either side; false when
    /// it never gets there (too slow, or longer than <see cref="MaxSteps"/>).
    /// </summary>
    public static bool HeightAt(float pitch, float speed, float drag, float horizontal, out float height)
    {
        var (s, c) = DetMath.SinCos(pitch);
        var p = Vector3.Zero;
        var v = new Vector3(c * speed, 0f, s * speed);
        for (int i = 0; i < MaxSteps; i++)
        {
            var p0 = p;
            Step(ref p, ref v, drag, World.TickSeconds);
            if (p.X >= horizontal)
            {
                float t = (horizontal - p0.X) / MathF.Max(1e-9f, p.X - p0.X);
                height = p0.Z + (p.Z - p0.Z) * t;
                return true;
            }
            if (v.X <= 1e-4f) break;
        }
        height = 0f;
        return false;
    }

    /// <summary>
    /// The pitch (radians, up positive) that carries a shot from <paramref name="from"/> through <paramref name="to"/> at
    /// <paramref name="speed"/> m/s with <paramref name="drag"/>: the low arc, by Newton's steps on the height missed at the target's
    /// distance (a few flights of <see cref="HeightAt"/>); the straight line's pitch when the target is nearly overhead; at most
    /// <see cref="MaxPitch"/> when it is out of reach.
    /// </summary>
    public static float SolvePitch(Vector3 from, Vector3 to, float speed, float drag)
    {
        float dx = to.X - from.X, dy = to.Y - from.Y, dz = to.Z - from.Z;
        float h = MathF.Sqrt(dx * dx + dy * dy);
        float pitch = DetMath.Atan2(dz, MathF.Max(h, 1e-4f));
        if (h < 0.05f || speed <= 0f) return pitch;
        for (int k = 0; k < 10; k++)
        {
            if (!HeightAt(pitch, speed, drag, h, out float z)) { pitch = MathF.Min(MaxPitch, pitch + 0.1f); continue; }
            float miss = dz - z;
            if (MathF.Abs(miss) < 1e-4f) break;
            float c = DetMath.Cos(pitch);
            pitch = Math.Clamp(pitch + miss * c * c / h, -MaxPitch, MaxPitch);
        }
        return pitch;
    }

    /// <summary>
    /// <see cref="SolvePitch"/>, checked: <paramref name="reaches"/> true when the pitch found carries the shot within a
    /// centimetre of <paramref name="to"/>; else (beyond the speed's reach) the pitch that carries it highest at the target's distance
    /// (the farthest shot toward it: it falls short), found by a golden-section search between level and <see cref="MaxPitch"/>.
    /// </summary>
    public static float SolveAim(Vector3 from, Vector3 to, float speed, float drag, out bool reaches)
    {
        float pitch = SolvePitch(from, to, speed, drag);
        float dx = to.X - from.X, dy = to.Y - from.Y, dz = to.Z - from.Z;
        float h = MathF.Sqrt(dx * dx + dy * dy);
        reaches = true;
        if (h < 0.05f || speed <= 0f) return pitch;
        if (HeightAt(pitch, speed, drag, h, out float z) && MathF.Abs(z - dz) < ReachTolerance) return pitch;
        reaches = false;
        float Height(float p) => HeightAt(p, speed, drag, h, out float y) ? y : float.NegativeInfinity;
        const float G = 0.618034f;
        float a = 0f, b = MaxPitch;
        float c = b - G * (b - a), e = a + G * (b - a);
        float fc = Height(c), fe = Height(e);
        for (int k = 0; k < 24; k++)
        {
            if (fc < fe) { a = c; c = e; fc = fe; e = a + G * (b - a); fe = Height(e); }
            else { b = e; e = c; fe = fc; c = b - G * (b - a); fc = Height(c); }
        }
        return 0.5f * (a + b);
    }

    /// <summary>How near the solved flight must pass its point to count as reaching it (m).</summary>
    public const float ReachTolerance = 0.01f;

    /// <summary>
    /// The horizontal distance (m) a shot at <paramref name="speed"/> m/s flies before it comes back down to the height it
    /// left at, loosed at its best angle over flat ground (a golden-section search between level and <see cref="MaxPitch"/>): how far a
    /// draw reaches on the flat.
    /// </summary>
    public static float FlatReach(float speed, float drag, out float bestPitch)
    {
        float Range(float p)
        {
            var (s, c) = DetMath.SinCos(p);
            var pos = Vector3.Zero;
            var v = new Vector3(c * speed, 0f, s * speed);
            for (int i = 0; i < MaxSteps * 2; i++)
            {
                var p0 = pos;
                Step(ref pos, ref v, drag, World.TickSeconds);
                if (pos.Z < 0f && i > 0) return p0.X + (pos.X - p0.X) * (p0.Z / MathF.Max(1e-9f, p0.Z - pos.Z));
            }
            return pos.X;
        }
        const float G = 0.618034f;
        float a = 0.02f, b = MaxPitch + 0.1f;
        float c = b - G * (b - a), e = a + G * (b - a);
        float fc = Range(c), fe = Range(e);
        for (int k = 0; k < 30; k++)
        {
            if (fc < fe) { a = c; c = e; fc = fe; e = a + G * (b - a); fe = Range(e); }
            else { b = e; e = c; fe = fc; c = b - G * (b - a); fc = Range(c); }
        }
        bestPitch = 0.5f * (a + b);
        return Range(bestPitch);
    }
}

/// <summary>
/// Projectiles in flight (<see cref="Phase.Contact"/>, <see cref="BowServices.ProjectileOrder"/>, after the blades'
/// contact: the schedule's "blows and missiles swept"). Each tick, each <see cref="Arrow"/> still flying is moved by
/// <see cref="Ballistics.Step"/> and its head is <b>swept</b> from where it was to where it is now — never sampled, so a fast one never
/// passes through a thin thing (an arrow at 55 m/s goes 0.46 m a tick) — against, as the blades are:
/// <list type="bullet">
/// <item>every other person's <b>shield</b> (an oriented box), from last tick's place to this tick's;</item>
/// <item>their held <b>blade</b> (a capsule), from last tick's to this tick's — any held blade, raised or not: a blade in the way stops an arrow;</item>
/// <item>their <b>bodies</b>: the broad capsule, then each hit volume, from last tick's volumes to this tick's (a part cut off is met no more);</item>
/// <item><b>the level</b>, its head as a moving point.</item>
/// </list>
/// Never its own archer. The first thing met wins (at one moment: a shield, a blade, the level, a body): a <see cref="ContactMade"/>
/// for the arrow (<see cref="ContactMade.Shot"/> its shot, <see cref="ContactMade.ProjectileMassKg"/> its mass, the swing 0, the id
/// after the blades' of the tick), and the arrow stops there (<see cref="ArrowSpent"/>, <see cref="ArrowEnd.Met"/>). A body met plays its
/// hurt as a blade's contact does (unless the rules wait for the harm, <see cref="HurtRule"/>); a protected person is found and
/// protests; violence is seen in a public place; the masquerade's catch lets go. An arrow that meets nothing is given up once it has
/// flown <see cref="BowData.FlightSecondsAtMost"/> or fallen <see cref="BowData.FallenBelowMetres"/> below its release
/// (<see cref="ArrowEnd.TimedOut"/>, <see cref="ArrowEnd.Fell"/>). The people near a flying arrow have their volumes, shield and blade
/// kept for the next tick's sweep (this system's own memory, derived from digested state, not digested). Deterministic, the stores'
/// order; nothing allocated per tick once each person's buffers are made.
/// </summary>
public sealed class ProjectileSystem : ISystem
{
    /// <summary>An arrow's contact id is the tick × 1000 + this + its number among the tick's arrows: after the blades' (which count from 0).</summary>
    public const int ContactIdBase = 500;

    /// <summary>A person is kept for the next tick's sweep when within this of a flying arrow's head (m), over what the arrow flies in two ticks.</summary>
    public const float KeepWithin = 3f;

    sealed class Slot
    {
        public EntityId Of = EntityId.None;
        public long Tick = long.MinValue;
        public HitVolume[] Volumes = [];
        public int Count;
        public OrientedCapsule Broad;
        public BoxPlacement Shield;
        public BladePlacement Blade;
        public bool HasBroad, HasShield, HasBlade;
    }

    struct Hit
    {
        public float Time;
        public ContactMet Met;
        public EntityId Target;
        public Vector3 Point, Normal, TargetVelocity;
        public int Volume;
        public BodyRegion Region;
        public ContactMaterial Material;
        /// <summary>For the picture: what it met (as <see cref="Arrow.StuckPart"/>) and that thing's frame at the moment.</summary>
        public int Part;
        public OrientedBox Frame;
    }

    Slot?[] slots = [];

    /// <summary>A capsule's frame (its first end the origin, its axis Z; the turn about the axis is not kept): for an arrow stuck in it.</summary>
    internal static OrientedBox CapsuleFrame(in OrientedCapsule c)
    {
        var z = Normalize(c.B - c.A, Vector3.UnitZ);
        var x = Normalize(Cross(z, Vector3.UnitZ), Vector3.UnitX);
        if (LengthSquared(Cross(z, Vector3.UnitZ)) < 1e-8f) x = Normalize(Cross(z, Vector3.UnitX), Vector3.UnitY);
        var y = Cross(z, x);
        return new OrientedBox(c.A, x, y, z, Vector3.Zero);
    }

    /// <summary>The frame of what an arrow is stuck in now (<see cref="Arrow.StuckPart"/>): a hit volume, the shield or the blade of <paramref name="who"/>.</summary>
    internal static bool FrameOf(World world, EntityId who, int part, out OrientedBox frame)
    {
        frame = default;
        if (part == -1 && HeldItems.Shield(world, who, out var s)) { frame = Box(s); return true; }
        if (part == -2 && HeldItems.Blade(world, who, out var b)) { frame = CapsuleFrame(new OrientedCapsule(b.Guard, b.Tip, b.Radius)); return true; }
        if (part > 0 && world.TryService<HitVolumes>(out var volumes))
        {
            var posed = volumes.Posed(world, who);
            if (part - 1 < posed.Length) { frame = CapsuleFrame(posed[part - 1].Capsule); return true; }
        }
        return false;
    }
    EntityId[] people = new EntityId[16];
    static readonly ContactConfig Defaults = new();

    public void Run(World world)
    {
        if (!world.TryStore<Arrow>(out var arrows) || arrows.Count == 0) return;
        var data = arrows.Data;
        bool any = false;
        for (int i = 0; i < data.Length && !any; i++) any = data[i].Flying;
        if (!any) return;

        var level = world.Service<LevelSpace>().Collision;
        world.TryService<HitVolumes>(out var volumes);
        world.TryService<CharacterAssets>(out var assets);
        world.TryService<StruckVolumes>(out var struck);
        var config = world.TryService<ContactConfig>(out var cc) ? cc : Defaults;
        var bowConfig = world.Service<BowConfig>();
        var bodies = world.Store<Body>();
        long tick = world.Tick;
        float dt = World.TickSeconds;

        // The people: every body that is drawn (has an animation).
        int peopleCount = 0;
        var aents = world.TryStore<CharacterAnimation>(out var anims) ? anims.Entities : default;
        for (int i = 0; i < aents.Length; i++)
        {
            if (!bodies.Has(aents[i])) continue;
            if (peopleCount == people.Length) Array.Resize(ref people, people.Length * 2);
            people[peopleCount++] = aents[i];
        }

        int serial = 0;
        var ents = arrows.Entities;
        for (int i = 0; i < data.Length; i++)
        {
            ref var a = ref data[i];
            if (!a.Flying) continue;
            var p0 = a.Position;
            var v0 = a.Velocity;
            var p1 = p0;
            var v1 = v0;
            Ballistics.Step(ref p1, ref v1, a.DragPerMetre, dt);

            bool found = false;
            Hit best = default;
            // The level: the head as a moving point.
            if (ContactMath.Moving(p0, p1, 0f, level, out var lh, out int tri))
            {
                best = new Hit { Time = lh.Time, Met = ContactMet.Level, Target = EntityId.None, Point = lh.Point, Normal = lh.Normal, Volume = -3,
                    Material = config.LevelSurface(level, tri) };
                found = true;
            }
            // The people.
            for (int k = 0; k < peopleCount; k++)
            {
                var t = people[k];
                if (t == a.Archer) continue;
                ref var tb = ref bodies.Get(t);
                if (!Near(p0, p1, tb.Feet, 2.5f)) continue;
                var kept = Kept(t, tick - 1);
                if (HeldItems.Shield(world, t, out var s1))
                {
                    var box1 = Box(s1);
                    var box0 = kept is { HasShield: true } ? Box(kept.Shield) : box1;
                    if (ContactMath.Sweep(p0, p0, p1, p1, a.RadiusMetres, box0, box1, out var h))
                        Consider(ref found, ref best, new Hit { Time = h.Time, Met = ContactMet.Shield, Target = t, Point = h.Point, Normal = h.Normal, Volume = -1,
                            TargetVelocity = (box1.Centre - box0.Centre) * World.TicksPerSecond, Material = assets is null ? config.Body : config.ShieldOf(assets, Kind(world, t)),
                            Part = -1, Frame = box1 });
                }
                if (HeldItems.Blade(world, t, out var w1) && w1.Length > 1e-4f)
                {
                    var c1 = new OrientedCapsule(w1.Guard, w1.Tip, w1.Radius);
                    var c0 = kept is { HasBlade: true } && kept.Blade.Length > 1e-4f ? new OrientedCapsule(kept.Blade.Guard, kept.Blade.Tip, kept.Blade.Radius) : c1;
                    if (ContactMath.Sweep(p0, p0, p1, p1, a.RadiusMetres, c0, c1, out var h))
                        Consider(ref found, ref best, new Hit { Time = h.Time, Met = ContactMet.Weapon, Target = t, Point = h.Point, Normal = h.Normal, Volume = -2,
                            TargetVelocity = VelocityOn(h.Point, c0, c1, h.Time), Material = assets is null ? ContactMaterial.Steel : config.WeaponOf(assets, Kind(world, t)).Material,
                            Part = -2, Frame = CapsuleFrame(c1) });
                }
                if (volumes is null || !volumes.Broad(world, t, out var broad1)) continue;
                var broad0 = kept is { HasBroad: true } ? kept.Broad : broad1;
                if (!ContactMath.Sweep(p0, p0, p1, p1, a.RadiusMetres, broad0, broad1, out _)) continue;
                var now = volumes.Posed(world, t);
                bool same = kept is not null && kept.Count == now.Length;
                uint missing = volumes.Missing(world, t, out var collapse);
                for (int v = 0; v < now.Length; v++)
                {
                    if (missing != 0 && collapse is not null && collapse.VolumeGone(missing, now[v].Region, now[v].Bone)) continue;
                    var cap1 = now[v].Capsule;
                    var cap0 = same ? kept!.Volumes[v].Capsule : cap1;
                    if (!ContactMath.Sweep(p0, p0, p1, p1, a.RadiusMetres, cap0, cap1, out var h)) continue;
                    Consider(ref found, ref best, new Hit { Time = h.Time, Met = ContactMet.Body, Target = t, Point = h.Point, Normal = h.Normal, Volume = v,
                        Region = now[v].Region, TargetVelocity = VelocityOn(h.Point, cap0, cap1, h.Time), Material = config.Body,
                        Part = v + 1, Frame = CapsuleFrame(cap1) });
                }
            }

            float flown = (tick - a.LoosedTick + 1) * dt;
            if (found)
            {
                var velocity = Lerp(v0, v1, best.Time);
                long id = tick * 1000 + ContactIdBase + serial++;
                Publish(world, config, struck, in a, in best, velocity, tick, id, bodies);
                a.Flying = false;
                a.Position = best.Point;
                a.Heading = Normalize(velocity, a.Heading);
                a.Velocity = Vector3.Zero;
                a.StuckIn = best.Target;
                a.ContactId = id;
                a.StuckPart = best.Part;
                if (best.Part != 0) { a.StuckLocal = best.Frame.ToLocal(best.Point); a.StuckHeadingLocal = best.Frame.DirectionToLocal(a.Heading); }
                world.Events<ArrowSpent>().Publish(new ArrowSpent(a.Archer, tick, a.Shot, ArrowEnd.Met, best.Met, best.Target, id, best.Point,
                    (tick - a.LoosedTick + best.Time) * dt, Length(best.Point - a.From), Length(velocity),
                    best.Met == ContactMet.Body ? BodyRegions.Name(best.Region) : null));
                continue;
            }
            a.Position = p1;
            a.Velocity = v1;
            a.Heading = Normalize(v1, a.Heading);
            var data0 = (uint)a.Bow < (uint)bowConfig.Bows.Count ? bowConfig.Bows[a.Bow] : null;
            float most = data0?.FlightSecondsAtMost ?? 10f, below = data0?.FallenBelowMetres ?? 50f;
            var end = flown >= most ? ArrowEnd.TimedOut : p1.Z < a.From.Z - below ? ArrowEnd.Fell : (ArrowEnd?)null;
            if (end is { } ended)
            {
                a.Flying = false;
                world.Events<ArrowSpent>().Publish(new ArrowSpent(a.Archer, tick, a.Shot, ended, ContactMet.Level, EntityId.None, 0L, p1,
                    flown, Length(p1 - a.From), Length(v1), null));
            }
        }

        // The people near an arrow still flying are kept for the next tick's sweep.
        for (int k = 0; k < peopleCount; k++)
        {
            var t = people[k];
            ref var tb = ref bodies.Get(t);
            bool near = false;
            for (int i = 0; i < data.Length && !near; i++)
            {
                if (!data[i].Flying) continue;
                float reach = KeepWithin + 2f * Length(data[i].Velocity) * dt;
                float dx = tb.Feet.X - data[i].Position.X, dy = tb.Feet.Y - data[i].Position.Y;
                near = dx * dx + dy * dy <= reach * reach;
            }
            if (near) Keep(world, volumes, t, tick);
        }
    }

    /// <summary>Whether the segment's ground track passes within <paramref name="within"/> of the feet (a cheap test before the shapes).</summary>
    static bool Near(Vector3 p0, Vector3 p1, Vector3 feet, float within)
    {
        var a = new Vector3(p0.X, p0.Y, 0f);
        var b = new Vector3(p1.X, p1.Y, 0f);
        var q = ClosestOnSegment(new Vector3(feet.X, feet.Y, 0f), a, b, out _);
        float dx = q.X - feet.X, dy = q.Y - feet.Y;
        return dx * dx + dy * dy <= within * within;
    }

    static void Consider(ref bool found, ref Hit best, in Hit h)
    {
        if (!found || h.Time < best.Time || (h.Time == best.Time && Rank(h.Met) < Rank(best.Met))) { best = h; found = true; }
    }

    static int Rank(ContactMet m) => m switch { ContactMet.Shield => 0, ContactMet.Weapon => 1, ContactMet.Level => 2, _ => 3 };

    static int Kind(World world, EntityId e) => world.Get<CharacterAnimation>(e).Kind;

    static OrientedBox Box(in BoxPlacement b) => new(b.Centre, b.AxisX, b.AxisY, b.AxisZ, b.HalfSize);

    static Vector3 VelocityOn(Vector3 p, in OrientedCapsule c0, in OrientedCapsule c1, float t)
    {
        ClosestOnSegment(p, Lerp(c0.A, c1.A, t), Lerp(c0.B, c1.B, t), out float u);
        return Lerp(c1.A - c0.A, c1.B - c0.B, u) * World.TicksPerSecond;
    }

    Slot? Kept(EntityId e, long tick)
    {
        if ((uint)e.Index >= (uint)slots.Length || slots[e.Index] is not { } s) return null;
        return s.Of == e && s.Tick == tick ? s : null;
    }

    void Keep(World world, HitVolumes? volumes, EntityId e, long tick)
    {
        if (e.Index >= slots.Length) Array.Resize(ref slots, Math.Max(e.Index + 1, slots.Length * 2));
        var s = slots[e.Index] ??= new Slot();
        s.Of = e;
        s.Tick = tick;
        s.HasShield = HeldItems.Shield(world, e, out s.Shield);
        s.HasBlade = HeldItems.Blade(world, e, out s.Blade);
        s.HasBroad = false;
        s.Count = 0;
        if (volumes is null || !volumes.Broad(world, e, out s.Broad)) return;
        s.HasBroad = true;
        var now = volumes.Posed(world, e);
        if (s.Volumes.Length < now.Length) s.Volumes = new HitVolume[now.Length];
        now.CopyTo(s.Volumes);
        s.Count = now.Length;
    }

    void Publish(World world, ContactConfig config, StruckVolumes? struck, in Arrow a, in Hit hit, Vector3 velocity, long tick, long id, Store<Body> bodies)
    {
        var relative = velocity - hit.TargetVelocity;
        float impact = MathF.Max(0f, -Dot(relative, hit.Normal));
        var t = hit.Target;
        bool person = !t.IsNone;
        bool protectedPerson = person && ProtectedPeople.Is(world, t);
        string? site = hit.Met == ContactMet.Body ? BodyRegions.Name(hit.Region) : null;
        var made = new ContactMade(id, a.Archer, 0, t, hit.Met, site, hit.Material, tick, hit.Point, hit.Normal, impact, a.MassKg * impact,
            PreContactBlocked: false, HitCoverOrShield: hit.Met is ContactMet.Shield or ContactMet.Level, LineOfFire: true, Protected: protectedPerson,
            PointSpeed: relative.Length(), AlongBlade: 0f, TipSpeed: velocity.Length(), PeakSpeed: a.LaunchSpeed, Chain: 0, Guard: GuardMeet.None,
            Shot: a.Shot, ProjectileMassKg: a.MassKg);
        world.Events<ContactMade>().Publish(made);
        struck?.Add(t, hit.Volume, hit.Met, tick, hit.Point);
        if (!person) return;

        // What it does to the body, as a blade's contact does (Contact.cs): a hurt unless protected, dead or dying, or the rules wait for the harm.
        if (hit.Met == ContactMet.Body && !protectedPerson && (!world.TryService<HurtRule>(out var hurtRule) || hurtRule.OnContact))
        {
            ref var ta = ref world.Store<CharacterAnimation>().Find(t);
            ref var intent = ref world.TryStore<CombatIntent>(out var fights) ? ref fights.Find(t) : ref Unsafe.NullRef<CombatIntent>();
            if (!Unsafe.IsNullRef(ref ta) && !ActionRoles.IsDeath(ta.Action) && !Unsafe.IsNullRef(ref intent) && intent.Play == ActionRole.None)
                intent.Play = ActionRole.Hurt;
        }
        if (protectedPerson) world.Events<ProtectedProtest>().Publish(new ProtectedProtest(t, a.Archer, tick, id));
        ref var ab = ref bodies.Find(a.Archer);
        if (world.TryService<EventQueue<MasqueradeRisk>>(out var risks) && !Unsafe.IsNullRef(ref ab) && world.Service<LevelSpace>().Places.At(ab.Feet) == PlaceKind.Public)
            risks.Publish(new MasqueradeRisk(a.Archer, tick, MasqueradeAct.Violence, TraversalKind.None, ab.Feet, impact, 0f));
        MasqueradeCatch.LetGo(world, t, a.Archer, id);
    }
}
