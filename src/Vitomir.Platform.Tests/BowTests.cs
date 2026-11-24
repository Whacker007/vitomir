// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.OneOne;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Perception;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The bow v0, headless: an arrow's flight against ballistics (level at 55 m/s over 20 m,
/// with and without drag); the aim's solve; the bow's states (the nock, the draw over time, a part draw and its slower arrow, the
/// let-down, aim let go, too little a draw, the quiver emptied, the melee weapon put away); an arrow meeting a standing body at each
/// region it is aimed at; a shield and the level stopping one; a moving body swept; replays; nothing allocated per tick. The flight
/// and the states need no game; the bodies need the 1.1 install (VTMR_GAME_DIR) and skip without it.
/// </summary>
public sealed class BowTests(ITestOutputHelper output)
{
    /// <summary>The war bow as <c>Data/combat/ranged.json</c> gives it (SANDBOX): 55 m/s at full draw in 1 s, a 75 g arrow.</summary>
    internal static BowData WarBow(float drag = 0.0015f) => new("war_bow", DrawWeightNewtons: 445f, FullDrawSeconds: 1.0f, SpeedAtFullDrawMetresPerSecond: 55f,
        SpeedExponent: 1f, LeastShareToLoose: 0.2f, ArrowMassKg: 0.075f, ArrowDragPerMetre: drag, ArrowRadiusMetres: 0.005f, NockSeconds: 0.6f,
        RaiseSeconds: 0.25f, ReleaseHeightShare: 0.8f, ReleaseForwardMetres: 0.45f, FlightSecondsAtMost: 8f, FallenBelowMetres: 30f);

    static int Ticks(float seconds) => (int)MathF.Round(seconds * World.TicksPerSecond);

    // ---------------------------------------------------------------- the flight, against ballistics

    /// <summary>A reference flight in double precision with a step of 10 µs (fourth-order Runge–Kutta): the height at <paramref name="x"/> metres out.</summary>
    static double Reference(double speed, double drag, double x)
    {
        double px = 0, pz = 0, vx = speed, vz = 0, h = 1e-5;
        (double, double) A(double ax, double az) { double s = Math.Sqrt(ax * ax + az * az); return (-drag * s * ax, -9.81 - drag * s * az); }
        while (true)
        {
            var (k1x, k1z) = A(vx, vz);
            var (k2x, k2z) = A(vx + k1x * h / 2, vz + k1z * h / 2);
            var (k3x, k3z) = A(vx + k2x * h / 2, vz + k2z * h / 2);
            var (k4x, k4z) = A(vx + k3x * h, vz + k3z * h);
            double nx = px + h * (vx + 2 * (vx + k1x * h / 2) + 2 * (vx + k2x * h / 2) + (vx + k3x * h)) / 6;
            double nz = pz + h * (vz + 2 * (vz + k1z * h / 2) + 2 * (vz + k2z * h / 2) + (vz + k3z * h)) / 6;
            vx += h * (k1x + 2 * k2x + 2 * k3x + k4x) / 6;
            vz += h * (k1z + 2 * k2z + 2 * k3z + k4z) / 6;
            if (nx >= x) return pz + (nz - pz) * (x - px) / (nx - px);
            (px, pz) = (nx, nz);
        }
    }

    /// <summary>A world with the movement and the bow on a floor (no characters): an archer body, the bow given and in hand.</summary>
    static (World World, EntityId Archer) Range(BowData bow, LevelSpace? level = null, float spread = 0f)
    {
        level ??= new TestLevel().Floor(-10, -60, 160, 60).Build();
        var b = new WorldBuilder(11);
        MovementServices.Register(b, level);
        b.Component<CombatIntent>();    // what the bow clears while it is in hand (no attacks registered here)
        var config = new BowConfig();
        config.Bows.Add(bow);
        if (spread > 0f) config.SpreadDegrees = (_, _) => spread;
        BowServices.Register(b, config);
        var w = b.Build();
        var e = MovementServices.SpawnBody(w, new(0, 0, 0.01f), 0f, Locomotion.Sandbox);
        Assert.True(BowServices.Give(w, e, bow.Id, 12));
        for (int i = 0; i < 10; i++) MovementServices.Step(w, ActionFrame.Idle);
        return (w, e);
    }

    static void Run(World w, int ticks, List<ArrowLoosed>? loosed = null, List<ArrowSpent>? spent = null, Action? each = null)
    {
        for (int i = 0; i < ticks; i++)
        {
            MovementServices.Step(w, ActionFrame.Idle);
            loosed?.AddRange(w.Events<ArrowLoosed>().ThisTick);
            spent?.AddRange(w.Events<ArrowSpent>().ThisTick);
            each?.Invoke();
        }
    }

    /// <summary>Raises, nocks, draws to <paramref name="share"/> and looses along a given level aim; returns the arrow's entity.</summary>
    static EntityId Shoot(World w, EntityId archer, float share = 1f, float pitch = 0f, float yaw = 0f)
    {
        ref var intent = ref w.Get<BowIntent>(archer);
        intent.Ranged = true;
        intent.Aim = true;
        intent.AimGiven = true;
        intent.AimYaw = yaw;
        intent.AimPitch = pitch;
        intent.DrawToShare = share;
        intent.Draw = true;
        Run(w, Ticks(0.6f + share * 1.0f) + 4);
        Assert.Equal(BowState.Held, w.Get<Bow>(archer).State);
        w.Get<BowIntent>(archer).Draw = false;
        int before = w.Store<Arrow>().Count;
        Run(w, 1);
        Assert.Equal(before + 1, w.Store<Arrow>().Count);
        return w.Store<Arrow>().Entities[^1];
    }

    /// <summary>Follows an arrow until its head passes <paramref name="x"/> metres out from where it left; the drop (m) there, taken between the ticks either side.</summary>
    static float DropAt(World w, EntityId arrow, float x)
    {
        var a = w.Get<Arrow>(arrow);
        var from = a.From;
        var last = a.Position;
        for (int i = 0; i < 600; i++)
        {
            Run(w, 1);
            var p = w.Get<Arrow>(arrow).Position;
            if (p.X - from.X >= x)
            {
                float t = (from.X + x - last.X) / (p.X - last.X);
                return from.Z - (last.Z + (p.Z - last.Z) * t);
            }
            last = p;
        }
        throw new Xunit.Sdk.XunitException("the arrow never got there");
    }

    [Fact]
    public void An_arrow_loosed_level_at_55_metres_a_second_drops_as_ballistics_says_over_20_metres()
    {
        // Without drag: the parabola exactly, g t² / 2 with t = 20 / 55 s (0.6486 m).
        var (w, e) = Range(WarBow(drag: 0f));
        var arrow = Shoot(w, e);
        Assert.Equal(55f, w.Get<Arrow>(arrow).LaunchSpeed, 3);
        float drop = DropAt(w, arrow, 20f);
        double t = 20.0 / 55.0, expected = 9.81 * t * t / 2;
        output.WriteLine($"no drag: dropped {drop:0.0000} m over 20 m; ballistics {expected:0.0000} m");
        Assert.InRange(drop, expected - 0.002, expected + 0.002);
        // With the war arrow's drag (k = 0.0015 per metre): against a fine integration of the same law.
        var (w2, e2) = Range(WarBow());
        var arrow2 = Shoot(w2, e2);
        float drop2 = DropAt(w2, arrow2, 20f);
        double expected2 = -Reference(55.0, 0.0015, 20.0);
        output.WriteLine($"drag 0.0015/m: dropped {drop2:0.0000} m over 20 m; a 10 µs Runge–Kutta flight {expected2:0.0000} m; speed at 20 m {w2.Get<Arrow>(arrow2).Velocity.Length():0.00} m/s");
        Assert.InRange(drop2, expected2 - 0.002, expected2 + 0.002);
        Assert.True(drop2 > drop, "drag slows the arrow, so it falls further over the same distance");
    }

    [Fact]
    public void The_aim_solved_for_a_target_carries_the_arrow_through_it()
    {
        var bow = WarBow();
        foreach (var (distance, rise) in new[] { (10f, 0f), (20f, 0.3f), (30f, -0.4f), (45f, 0f) })
        {
            var from = new Vector3(0, 0, 1.4f);
            var to = new Vector3(distance * 0.6f, distance * 0.8f, 1.4f + rise);
            float pitch = Ballistics.SolvePitch(from, to, bow.SpeedAtFullDrawMetresPerSecond, bow.ArrowDragPerMetre);
            Assert.True(Ballistics.HeightAt(pitch, bow.SpeedAtFullDrawMetresPerSecond, bow.ArrowDragPerMetre, distance, out float z));
            output.WriteLine($"{distance} m, {rise:+0.0;-0.0} m: pitch {pitch * DetMath.RadToDeg:0.000}°, passes {z - rise:+0.0000;-0.0000} m off");
            Assert.InRange(z, rise - 0.001f, rise + 0.001f);
        }
    }

    // ---------------------------------------------------------------- the bow's states

    [Fact]
    public void Aim_nocks_attack_draws_over_time_and_letting_go_looses_at_the_speed_the_draw_reached()
    {
        var (w, e) = Range(WarBow());
        var nocked = new List<ArrowNocked>();
        var began = new List<DrawBegan>();
        ref var intent = ref w.Get<BowIntent>(e);
        intent.Ranged = true;
        intent.Aim = true;
        Run(w, 1);
        Assert.Equal(BowState.Raising, w.Get<Bow>(e).State);
        Run(w, Ticks(0.6f) - 2, each: () => nocked.AddRange(w.Events<ArrowNocked>().ThisTick));
        Assert.Empty(nocked);
        Run(w, 2, each: () => nocked.AddRange(w.Events<ArrowNocked>().ThisTick));
        var n = Assert.Single(nocked);
        Assert.Equal((1, 11), (n.Shot, n.ArrowsLeft));
        Assert.Equal(BowState.Nocked, w.Get<Bow>(e).State);
        // Half a second of draw is half the draw; let go, the arrow leaves at half the full speed.
        w.Get<BowIntent>(e).Draw = true;
        Run(w, Ticks(0.5f), each: () => began.AddRange(w.Events<DrawBegan>().ThisTick));
        Assert.Single(began);
        Assert.Equal(BowState.Drawing, w.Get<Bow>(e).State);
        Assert.InRange(w.Get<Bow>(e).Share, 0.49f, 0.51f);
        w.Get<BowIntent>(e).Draw = false;
        var loosed = new List<ArrowLoosed>();
        Run(w, 1, loosed);
        var l = Assert.Single(loosed);
        Assert.Equal(1, l.Shot);
        Assert.InRange(l.DrawShare, 0.49f, 0.51f);
        Assert.InRange(l.SpeedMetresPerSecond, 55f * 0.49f, 55f * 0.51f);
        Assert.InRange(l.From.Z, 1.2f, 1.6f);   // shoulder height
        // Still aiming: the next arrow is nocked by itself.
        Run(w, Ticks(0.6f) + 1, each: () => nocked.AddRange(w.Events<ArrowNocked>().ThisTick));
        Assert.Equal(2, nocked.Count);
        Assert.Equal((2, 10), (nocked[1].Shot, nocked[1].ArrowsLeft));
    }

    [Fact]
    public void A_part_draw_flies_slower_and_lower_than_a_full_one()
    {
        float Landing(float share)
        {
            var (w, e) = Range(WarBow());
            var arrow = Shoot(w, e, share, pitch: 5f * DetMath.DegToRad);
            var spent = new List<ArrowSpent>();
            Run(w, Ticks(4f), spent: spent);
            var s = Assert.Single(spent);
            Assert.Equal((ArrowEnd.Met, ContactMet.Level), (s.End, s.Met));
            return s.Point.X;
        }
        float full = Landing(1f), half = Landing(0.5f);
        output.WriteLine($"5° up: a full draw lands {full:0.0} m out, a half draw {half:0.0} m");
        Assert.True(full > 2.5f * half, "a half draw (half the speed) carries much less than half as far");
    }

    [Fact]
    public void The_draw_is_let_down_by_the_press_by_aim_let_go_and_when_too_little_and_nothing_is_loosed()
    {
        var (w, e) = Range(WarBow());
        var cancelled = new List<DrawCancelled>();
        var loosed = new List<ArrowLoosed>();
        void Go(int ticks) => Run(w, ticks, loosed, each: () => cancelled.AddRange(w.Events<DrawCancelled>().ThisTick));
        ref var intent = ref w.Get<BowIntent>(e);
        intent.Ranged = true;
        intent.Aim = true;
        intent.Draw = true;
        Go(Ticks(1.2f));
        Assert.Equal(BowState.Drawing, w.Get<Bow>(e).State);
        w.Get<BowIntent>(e).LetDown = true;
        Go(1);
        Assert.Equal(DrawCancelCause.LetDown, Assert.Single(cancelled).Cause);
        Assert.Equal(BowState.Nocked, w.Get<Bow>(e).State);
        // Still held after the let-down: the draw waits for a new press.
        Go(30);
        Assert.Equal(BowState.Nocked, w.Get<Bow>(e).State);
        w.Get<BowIntent>(e).Draw = false;
        Go(1);
        w.Get<BowIntent>(e).Draw = true;
        Go(Ticks(1.2f));
        Assert.Equal(BowState.Held, w.Get<Bow>(e).State);
        // Aim let go at full draw: let down, the bow back low.
        w.Get<BowIntent>(e).Aim = false;
        Go(1);
        Assert.Equal(DrawCancelCause.AimLetGo, cancelled[^1].Cause);
        Assert.Equal(BowState.Ready, w.Get<Bow>(e).State);
        Assert.True(w.Get<Bow>(e).OnString, "the arrow stays nocked, low at the waist");
        // Too little a draw: let go at 0.1 of full is let down, not loosed.
        w.Get<BowIntent>(e).Aim = true;
        w.Get<BowIntent>(e).Draw = false;
        Go(Ticks(0.25f) + 2);    // raised again, the arrow already on the string
        Assert.Equal(BowState.Nocked, w.Get<Bow>(e).State);
        w.Get<BowIntent>(e).Draw = true;
        Go(Ticks(0.1f));
        w.Get<BowIntent>(e).Draw = false;
        Go(1);
        Assert.Equal(DrawCancelCause.TooLittle, cancelled[^1].Cause);
        Assert.Empty(loosed);
        Assert.Equal(11, w.Get<Bow>(e).Arrows);
    }

    [Fact]
    public void The_quiver_empties_and_the_bow_in_hand_puts_the_melee_weapon_away()
    {
        var (w, e) = Range(WarBow(), level: new TestLevel().Floor(-10, -60, 400, 60).Build());
        w.Get<Bow>(e).Arrows = 2;
        w.Set(e, new CombatIntent { Attack = true, Block = true });
        var loosed = new List<ArrowLoosed>();
        ref var intent = ref w.Get<BowIntent>(e);
        intent.Ranged = true;
        intent.Aim = true;
        intent.AimGiven = true;
        for (int k = 0; k < 3; k++)
        {
            w.Get<BowIntent>(e).Draw = true;
            Run(w, Ticks(1.7f), loosed);
            w.Get<BowIntent>(e).Loose = true;
            Run(w, 1, loosed);
            Assert.False(w.Get<CombatIntent>(e).Attack || w.Get<CombatIntent>(e).Block, "the bow in hand: no melee press, no guard");
        }
        Assert.Equal([1, 2], loosed.Select(l => l.Shot));
        Assert.Equal((0, 2, BowState.Ready), (w.Get<Bow>(e).Arrows, w.Get<Bow>(e).Shots, w.Get<Bow>(e).State));
        // Put away: the melee intents stand again.
        w.Get<BowIntent>(e).Ranged = false;
        w.Set(e, new CombatIntent { Attack = true });
        Run(w, 1);
        Assert.Equal(BowState.Away, w.Get<Bow>(e).State);
        Assert.True(w.Get<CombatIntent>(e).Attack);
    }

    [Fact]
    public void An_arrow_that_meets_nothing_is_given_up_and_every_arrow_is_spent_once()
    {
        // No floor out there: the arrow falls past 30 m below its release.
        var (w, e) = Range(WarBow(), level: new TestLevel().Floor(-5, -5, 5, 5).Build());
        var arrow = Shoot(w, e, pitch: 0.2f);
        var spent = new List<ArrowSpent>();
        Run(w, Ticks(9f), spent: spent);
        var s = Assert.Single(spent);
        Assert.Equal(ArrowEnd.Fell, s.End);
        Assert.False(w.Get<Arrow>(arrow).Flying);
        Assert.Equal(0L, s.ContactId);
    }

    [Fact]
    public void A_spread_turns_the_arrow_off_its_aim_by_draws_from_the_bows_own_stream()
    {
        var yaws = new List<float>();
        for (int k = 0; k < 2; k++)
        {
            var (w, e) = Range(WarBow(), spread: 2f);
            var loosed = new List<ArrowLoosed>();
            Shoot(w, e);
            Run(w, 1, loosed);
            ref var bow = ref w.Get<Bow>(e);
            yaws.Add(w.Get<Arrow>(w.Store<Arrow>().Entities[^1]).Velocity.Y);
        }
        Assert.Equal(yaws[0], yaws[1]);          // the same seed: the same draw
        Assert.NotEqual(0f, yaws[0]);            // turned off the aim
    }

    // ---------------------------------------------------------------- against bodies (the 1.1 install)

    static readonly Lazy<OneOneLibrary?> Library = new(() => GameInstall.TryOpen() is { } i ? new OneOneLibrary(i) : null);
    static readonly Dictionary<(string, string?), OneOneCharacter> Loaded = [];

    static OneOneCharacter Character(string name, string? weapon = null)
    {
        Assert.SkipUnless(Library.Value is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        lock (Loaded)
        {
            if (!Loaded.TryGetValue((name, weapon), out var c))
                Loaded[(name, weapon)] = c = Library.Value!.Character(name, weapon) ?? throw new Xunit.Sdk.XunitException($"{name} did not load");
            return c;
        }
    }

    sealed class Yard
    {
        public required World World { get; init; }
        public required Dictionary<string, int> Kinds { get; init; }
        public readonly List<ContactMade> Contacts = [];
        public readonly List<ArrowSpent> Spent = [];
        public readonly List<ArrowLoosed> Loosed = [];

        public void Step(int ticks = 1)
        {
            for (int i = 0; i < ticks; i++)
            {
                MovementServices.Step(World, ActionFrame.Idle);
                Contacts.AddRange(World.Events<ContactMade>().ThisTick);
                Spent.AddRange(World.Events<ArrowSpent>().ThisTick);
                Loosed.AddRange(World.Events<ArrowLoosed>().ThisTick);
            }
        }

        public EntityId Put(string kind, float x, float y, float yawDegrees)
        {
            var e = MovementServices.SpawnBody(World, new(x, y, 0.01f), yawDegrees * DetMath.DegToRad, Locomotion.Sandbox);
            World.Set(e, CharacterAnimation.For(World.Service<CharacterAssets>(), Kinds[kind]));
            World.Set(e, MotionInput.Standing);
            CombatServices.Arm(World, e);
            return e;
        }

        /// <summary>An archer at (x, y) with the war bow, its aim held on <paramref name="target"/>'s <paramref name="region"/>: raised, drawn to full, loosed.</summary>
        public EntityId Shoot(EntityId archer, EntityId target, BodyRegion region)
        {
            ref var intent = ref World.Get<BowIntent>(archer);
            intent.Ranged = true;
            intent.Aim = true;
            intent.AimsAt = true;
            intent.AimAt = target;
            intent.AimRegion = region;
            intent.Draw = true;
            Step(Ticks(1.7f));
            World.Get<BowIntent>(archer).Loose = true;
            Step(1);
            return World.Store<Arrow>().Entities[^1];
        }
    }

    static Yard MakeYard(LevelSpace? level = null, ulong seed = 7, bool bow = true)
    {
        var assets = new CharacterAssets();
        var kinds = new Dictionary<string, int>();
        foreach (var k in new[] { "Christof", "rudolfGuard", "Civ12male" }) kinds[k] = OneOneLibrary.Register(assets, Character(k));
        level ??= new TestLevel().Floor(-60, -60, 60, 60).Build();
        var b = new WorldBuilder(seed);
        MovementServices.Register(b, level);
        MasqueradeServices.Register(b, new MasqueradeSettings { AutoSwitch = false });
        b.Animation(assets);
        b.HitVolumes();
        BodyMotionSystem.Register(b);
        CombatServices.Register(b);
        ContactServices.Register(b, new ContactConfig());
        if (bow)
        {
            var config = new BowConfig();
            config.Bows.Add(WarBow());
            BowServices.Register(b, config);
        }
        var w = b.Build();
        w.Service<HitVolumes>().Prepare(assets);
        ContactServices.Prepare(w);
        return new Yard { World = w, Kinds = kinds };
    }

    [Fact]
    public void An_arrow_meets_a_standing_body_at_each_region_it_is_aimed_at()
    {
        var missed = new List<string>();
        // The regions the townsman's skeleton has volumes for (his has no neck: the upper chest is there).
        var has = new HashSet<BodyRegion>();
        {
            var y0 = MakeYard();
            var t0 = y0.Put("Civ12male", 0, 0, 180f);
            y0.Step(2);
            foreach (var v in y0.World.Service<HitVolumes>().Posed(y0.World, t0)) has.Add(v.Region);
        }
        output.WriteLine($"his regions: {string.Join(", ", has.Order())}");
        Assert.True(has.Count >= 16, $"{has.Count} regions");
        foreach (var region in BodyRegions.All)
        {
            if (!has.Contains(region)) continue;
            // From the front at 10 m; a part on one side from that side (the other arm and the body hide it from the front).
            float bearing = BodyRegions.IsLeft(region) ? 90f : BodyRegions.IsRight(region) ? -90f : 0f;
            var y = MakeYard();
            var target = y.Put("Civ12male", 0, 0, 180f);
            // The townsman faces −X, so his left side faces −Y: the archer stands in front of the side asked.
            var (ax, ay) = bearing == 0f ? (-10f, 0f) : bearing > 0f ? (0f, -10f) : (0f, 10f);
            var archer = y.Put("Christof", ax, ay, 0f);
            Assert.True(BowServices.Give(y.World, archer, "war_bow", 6));
            y.Step(30);
            y.Shoot(archer, target, region);
            y.Step(Ticks(0.5f));
            var c = y.Contacts.FirstOrDefault(x => x.Shot > 0);
            output.WriteLine($"{BodyRegions.Name(region),-16} from ({ax}, {ay}): met {c.Met} {c.Site ?? "-"} at {c.Point.X:0.00} {c.Point.Y:0.00} {c.Point.Z:0.00}, {c.ImpactSpeed:0.0} m/s, impulse {c.NormalImpulse:0.00} N·s");
            if (c.Shot == 0 || c.Met != ContactMet.Body || c.Site != BodyRegions.Name(region)) missed.Add($"{BodyRegions.Name(region)}: met {c.Met} {c.Site}");
            else
            {
                Assert.Equal(target, c.Target);
                Assert.Equal(0, c.Swing);
                Assert.Equal(0.075f, c.ProjectileMassKg);
                Assert.True(c.ImpactSpeed > 40f && c.LineOfFire && !c.HitCoverOrShield);
            }
        }
        Assert.Empty(missed);
    }

    [Fact]
    public void A_raised_shield_stops_an_arrow_and_the_body_behind_is_not_met()
    {
        var y = MakeYard();
        var guard = y.Put("rudolfGuard", 0, 0, 180f);
        var archer = y.Put("Christof", -15f, 0, 0f);
        Assert.True(BowServices.Give(y.World, archer, "war_bow", 6));
        y.World.Get<CombatIntent>(guard).Block = true;
        y.Step(40);
        var arrow = y.Shoot(archer, guard, BodyRegion.Chest);
        y.Step(Ticks(0.6f));
        var c = Assert.Single(y.Contacts, x => x.Shot > 0);
        Assert.Equal(ContactMet.Shield, c.Met);
        Assert.Equal(guard, c.Target);
        Assert.True(c.HitCoverOrShield);
        Assert.Equal(ContactMaterial.Wood, c.Material);
        var s = Assert.Single(y.Spent);
        Assert.Equal((ArrowEnd.Met, ContactMet.Shield, c.ContactId), (s.End, s.Met, s.ContactId));
        Assert.Equal(guard, y.World.Get<Arrow>(arrow).StuckIn);
        Assert.NotEqual(ActionRole.Hurt, y.World.Get<CharacterAnimation>(guard).Action);
    }

    [Fact]
    public void The_level_stops_an_arrow_and_the_person_behind_the_wall_is_not_met()
    {
        var wall = new TestLevel().Floor(-60, -60, 60, 60).Box(new(-5f, -1.5f, 0f), new(-4.8f, 1.5f, 2.5f)).Build();
        var y = MakeYard(wall);
        var target = y.Put("Civ12male", 0, 0, 180f);
        var archer = y.Put("Christof", -12f, 0, 0f);
        Assert.True(BowServices.Give(y.World, archer, "war_bow", 6));
        y.Step(30);
        var arrow = y.Shoot(archer, target, BodyRegion.Chest);
        y.Step(Ticks(0.5f));
        var c = Assert.Single(y.Contacts, x => x.Shot > 0);
        Assert.Equal(ContactMet.Level, c.Met);
        Assert.True(c.Target.IsNone);
        Assert.InRange(c.Point.X, -5.05f, -4.95f);
        Assert.Equal(ContactMaterial.Stone, c.Material);
        Assert.Equal(ArrowEnd.Met, Assert.Single(y.Spent).End);
        Assert.NotEqual(ActionRole.Hurt, y.World.Get<CharacterAnimation>(target).Action);
        Assert.False(y.World.Get<Arrow>(arrow).Flying);
    }

    [Fact]
    public void An_arrow_meets_a_man_walking_across_its_path_where_he_is_when_it_gets_there()
    {
        var y = MakeYard();
        var target = y.Put("Civ12male", 0, -3f, 90f);
        var archer = y.Put("Christof", -20f, 0, 0f);
        Assert.True(BowServices.Give(y.World, archer, "war_bow", 6));
        y.Step(30);
        // He walks along +Y across the line of fire at y 0; the archer looses level along +X at chest height when he is short of it.
        ref var intent = ref y.World.Get<BowIntent>(archer);
        intent.Ranged = true; intent.Aim = true; intent.AimGiven = true; intent.AimYaw = 0f; intent.AimPitch = 0.02f; intent.Draw = true;
        y.Step(Ticks(1.7f));
        ref var move = ref y.World.Get<MoveIntent>(target);
        move.Direction = new Vector2(0, 1);
        move.Gait = Gait.Walk;
        int k = 0;
        while (y.World.Get<Body>(target).Feet.Y < -0.62f && k++ < 600) y.Step(1);
        y.World.Get<BowIntent>(archer).Loose = true;
        y.Step(Ticks(0.6f));
        var c = Assert.Single(y.Contacts, x => x.Shot > 0);
        output.WriteLine($"met {c.Met} {c.Site} at {c.Point.Y:0.000} while he walked; his feet at {y.World.Get<Body>(target).Feet.Y:0.00} now");
        Assert.Equal(ContactMet.Body, c.Met);
        Assert.Equal(target, c.Target);
    }

    static (ulong[] Digests, List<ContactMade> Contacts) Volley(ulong seed)
    {
        var y = MakeYard(seed: seed);
        var target = y.Put("rudolfGuard", 0, 0, 180f);
        var archer = y.Put("Christof", -14f, 2f, 0f);
        Assert.True(BowServices.Give(y.World, archer, "war_bow", 6));
        y.World.Service<BowConfig>().SpreadDegrees = (_, _) => 1.5f;
        y.Step(20);
        var digests = new List<ulong>();
        for (int k = 0; k < 4; k++)
        {
            y.Shoot(archer, target, k % 2 == 0 ? BodyRegion.Chest : BodyRegion.LeftThigh);
            digests.Add(y.World.Digest());
        }
        y.Step(Ticks(0.6f));
        digests.Add(y.World.Digest());
        return ([.. digests], y.Contacts);
    }

    [Fact]
    public void A_volley_replays_to_the_same_digests_and_the_same_contacts()
    {
        var (d1, c1) = Volley(21);
        var (d2, c2) = Volley(21);
        Assert.Equal(d1, d2);
        Assert.Equal(c1, c2);
        Assert.True(c1.Count(c => c.Shot > 0) >= 3, $"{c1.Count(c => c.Shot > 0)} arrow contacts");
    }

    [Fact]
    public void Arrows_in_flight_allocate_nothing_per_tick_once_going()
    {
        var y = MakeYard(level: new TestLevel().Floor(-60, -60, 200, 60).Build());
        y.Put("Civ12male", 30f, 6f, 180f);
        var archer = y.Put("Christof", 0, 0, 0f);
        Assert.True(BowServices.Give(y.World, archer, "war_bow", 1000));
        ref var intent = ref y.World.Get<BowIntent>(archer);
        intent.Ranged = true; intent.Aim = true; intent.AimGiven = true; intent.AimPitch = 0.05f; intent.AimYaw = 0.2f; intent.DrawToShare = 0.9f;
        // A loose every 200 ticks (draw held 190, let go): arrows in the air past him, the next nocked and drawn meanwhile.
        void Round(int i)
        {
            y.World.Get<BowIntent>(archer).Draw = i % 200 < 190;
            MovementServices.Step(y.World, ActionFrame.Idle);
        }
        int tick = 0;
        for (; tick < 1600; tick++) Round(tick);
        Allocations.Warm(y.World);
        Assert.True(y.World.Store<Arrow>().Count >= 6);
        Allocations.None("an arrow's flight and the next draw, 150 ticks", () =>
        {
            while (tick % 200 != 191) Round(tick++);     // just after a loose (its entity made outside the window)
            long start = GC.GetAllocatedBytesForCurrentThread();
            int flying = 0;
            for (int i = 0; i < 150; i++)
            {
                Round(tick++);
                var arrows = y.World.Store<Arrow>().Data;
                for (int k = 0; k < arrows.Length; k++) if (arrows[k].Flying) flying++;
            }
            long bytes = GC.GetAllocatedBytesForCurrentThread() - start;
            Assert.True(flying > 0, "arrows flew while measured");
            return bytes;
        });
    }

    [Fact]
    public void A_world_without_a_bow_registers_none_of_it()
    {
        var y = MakeYard(bow: false);
        Assert.False(y.World.TryStore<Bow>(out _));
        Assert.False(y.World.TryStore<Arrow>(out _));
        Assert.False(y.World.TryService<BowConfig>(out _));
        Assert.DoesNotContain(WorldBuilder.RunOrder(y.World), n => n.Contains("platform.bow", StringComparison.Ordinal) || n.Contains("projectile", StringComparison.Ordinal));
    }
}
