// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The jump tapped and held (ours): a tap is a human's jump for every body and the
/// obstacle decides the move — the human mantles 1.5 m, grabs 2.4 m and climbs onto 1.0 m with a tap, a block of 0.6 m
/// from the floor is hopped onto moving towards it and climbed onto from a stand, an elder unbound mantles a wall of
/// 4 m with a tap and held to a human does not —; the human jumps on the press; a neonate's hold charges, the higher
/// the longer up to its own jump, crouched and at most at a walk, jumps on the release and is cancelled with nothing
/// jumping; a charged jump's moves reach with the body's own numbers in the masquerade; at a grab's end forward held
/// pulls up and nothing held hangs; a move refused at a marked face is told once; the events and the cancel key in a
/// world; and a tape with a charge replays digest for digest.
/// </summary>
public sealed class JumpTests
{
    static readonly Locomotion Human = Locomotion.Sandbox;

    /// <summary>A neonate as the enhanced module's profile has it (SANDBOX): its own jump 1 m, a little more reach; its tap a human's.</summary>
    static readonly Locomotion Neonate = Masquerade.WithHumanTap(Human with
    {
        RunSpeed = 3.5f, SprintSpeed = 7f, JumpImpulseBudget = Locomotion.ImpulseFor(1f, 9.81f), StandingReach = 2.35f,
        SprintVaultMax = 1.0f, SprintVaultDepth = 0.7f, VaultMax = 1.6f, MantleMax = 2.4f, CrouchSpeed = 1.2f,
    }, Human);

    /// <summary>An elder (SANDBOX): its own jump 3 m, reach 2.5 m, vaults to 3 m, mantles to 4 m; its tap a human's.</summary>
    static readonly Locomotion Elder = Masquerade.WithHumanTap(Human with
    {
        RunSpeed = 4.5f, SprintSpeed = 9f, JumpImpulseBudget = Locomotion.ImpulseFor(3f, 9.81f), StandingReach = 2.5f,
        SprintVaultMax = 1.0f, SprintVaultDepth = 0.7f, VaultMax = 3f, MantleMax = 4f, MantleSeconds = 0.5f, CrouchSpeed = 1.5f,
    }, Human);

    /// <summary>A charge held to its full: the hold, the charge and a little more (ticks).</summary>
    static readonly int Full = (int)((BodyMotor.HoldSeconds + 1f) * World.TicksPerSecond) + 12;

    sealed class Drove
    {
        public readonly List<TraversalKind> Started = [], Refused = [];
        public readonly List<(int Tick, JumpKind Kind, float Height)> Launched = [];
        public readonly List<ChargeStep> Charge = [];
        public int TookOffAt = -1, Cancelled, ChargingFrom = -1;
        public float Highest = float.MinValue, Stood = float.MinValue, FastestCharging;
        public bool CrouchedCharging;
        public Body End;
        public override string ToString() => $"moves [{string.Join(", ", Started)}], jumps [{string.Join(", ", Launched)}], charge [{string.Join(", ", Charge)}], ended at {End.Feet} ({End.State}), highest {Highest:0.000}, stood {Stood:0.000}";
    }

    static Drove Go(LevelSpace level, Vector3 from, int ticks, Func<int, Body, MoveIntent> intent, Locomotion? numbers = null)
    {
        var d = new Drove();
        var body = Drive.Settle(level, from);
        var move = default(Traversal);
        d.End = Drive.Moves(level, body, ref move, ticks, intent, (i, b, m, o) =>
        {
            if (o.MoveStarted != TraversalKind.None) d.Started.Add(o.MoveStarted);
            if (o.Refused != TraversalKind.None) d.Refused.Add(o.Refused);
            if (o.Launched) d.Launched.Add((i, o.LaunchKind, o.LaunchHeight));
            if (o.TookOff && d.TookOffAt < 0) d.TookOffAt = i;
            if (o.Charge) d.Charge.Add(o.ChargeStep);
            if (o.Cancelled) d.Cancelled++;
            if (BodyMotor.IsCharging(b))
            {
                // Its speed once it has had a tenth of a second to slow from a run.
                if (d.ChargingFrom < 0) d.ChargingFrom = i;
                d.CrouchedCharging |= b.Crouched != 0;
                if (i >= d.ChargingFrom + 12) d.FastestCharging = MathF.Max(d.FastestCharging, new Vector2(b.Velocity.X, b.Velocity.Y).Length());
            }
            d.Highest = MathF.Max(d.Highest, b.Feet.Z);
            if (b.State == BodyState.Ground && m.Kind == TraversalKind.None) d.Stood = MathF.Max(d.Stood, b.Feet.Z);
        }, numbers);
        return d;
    }

    /// <summary>Along +X at a gait (or standing facing it once past <paramref name="stop"/>), jump down from <paramref name="press"/> for <paramref name="hold"/> ticks, cancel at <paramref name="cancel"/>.</summary>
    static Func<int, Body, MoveIntent> Along(Gait gait, int press, int hold = 1, float stop = float.PositiveInfinity, int cancel = -1) => (i, b) =>
    {
        var m = b.Feet.X > stop ? new MoveIntent { Facing = 0f, Faces = true, Gait = gait } : Drive.Toward(1, 0, gait);
        m.Jump = i == press;
        m.JumpHeld = i >= press && i < press + hold;
        m.Cancel = i == cancel;
        return m;
    };

    /// <summary>Along +X at a gait, a tap once the feet are at <paramref name="x"/> on the ground.</summary>
    static Func<int, Body, MoveIntent> PressAt(float x, Gait gait)
    {
        int pressed = -1;
        return (i, b) =>
        {
            if (pressed < 0 && b.Feet.X >= x && b.State == BodyState.Ground) pressed = i;
            return Along(gait, pressed)(i, b);
        };
    }

    static TestLevel Ground() => new TestLevel().Floor(-10, -10, 12, 10);

    /// <summary>A block across the way from x 1, 2 m deep, <paramref name="height"/> high.</summary>
    static LevelSpace Block(float height) => Ground().Box(new(1, -3, 0), new(3, 3, height)).Build($"block-{height}");

    static void Near(float expected, float actual, float within, string what) =>
        Assert.True(MathF.Abs(actual - expected) <= within, $"{what}: {actual} (expected {expected} within {within})");

    // ---------------------------------------------------------------- a tap: the obstacle decides

    [Theory]
    [InlineData(1.0f, TraversalKind.ClimbOnto)]
    [InlineData(1.5f, TraversalKind.Mantle)]
    [InlineData(2.4f, TraversalKind.Grab)]
    public void A_tap_climbs_onto_1_0_m_mantles_1_5_m_and_grabs_2_4_m_for_the_human(float height, TraversalKind want)
    {
        // Walking at it, a tap 0.55 m from the face; forward held through, so the grab pulls up at its end.
        var d = Go(Block(height), new(-1f, 0, 0.01f), 480, PressAt(0.45f, Gait.Walk));
        Assert.True(d.Started.FirstOrDefault() == want, d.ToString());
        if (want == TraversalKind.Grab) Assert.Equal([TraversalKind.Grab, TraversalKind.PullUp], d.Started);
        Assert.True(d.Stood >= height - 0.02f, d.ToString());
        Assert.All(d.Launched, l => Assert.Equal(JumpKind.Tap, l.Kind));
        Near(0.5f, d.Launched[0].Height, 0.01f, "the tap's height");
    }


    [Fact]
    public void A_block_of_0_6_m_from_the_floor_is_hopped_onto_moving_towards_it_and_climbed_onto_from_a_stand()
    {
        // Measured from the ground, not from the feet a skin above it: 0.6 m is the top of the hop's reach (the
        // tap's 0.5 m and 0.1 m for the knees) and the foot of the climb's (VaultMin); the hop, on the ground, is tried
        // first, so moving towards the block a tap hops onto it, and standing facing it the tap's jump climbs onto it.
        var level = Block(0.6f);
        var moving = Go(level, new(0.35f, 0, 0.01f), 200, Along(Gait.Walk, press: 6));
        Assert.Equal([TraversalKind.HopUp], moving.Started);
        Near(0.6f, moving.Stood, 0.01f, "on the block");
        var standing = Go(level, new(0.5f, 0, 0.01f), 200, Along(Gait.Walk, press: 6, stop: 0f));
        Assert.Equal([TraversalKind.ClimbOnto], standing.Started);
        Near(0.6f, standing.Stood, 0.01f, "on the block");
    }

    [Fact]
    public void An_elder_unbound_mantles_a_wall_of_4_m_with_a_tap_and_held_to_a_human_does_not()
    {
        var level = Block(4f);
        var free = Go(level, new(0.45f, 0, 0.01f), 300, Along(Gait.Walk, press: 6, stop: 0f), Elder);
        Assert.Equal([TraversalKind.Mantle], free.Started);
        Near(4f, free.Stood, 0.01f, "on the wall");
        Near(0.5f, free.Launched[0].Height, 0.01f, "a human's tap");
        var held = Go(level, new(0.45f, 0, 0.01f), 300, Along(Gait.Walk, press: 6, stop: 0f), Masquerade.HeldToHuman(Elder, Human));
        Assert.Empty(held.Started);
        Assert.True(held.End.Feet.Z < 0.02f, held.ToString());
    }

    // ---------------------------------------------------------------- the press and the hold

    [Fact]
    public void The_human_has_nothing_to_charge_and_jumps_on_the_press()
    {
        Assert.False(Human.CanCharge);
        var d = Go(Ground().Build(), new(0, 0, 0.01f), 200, Along(Gait.Walk, press: 6, hold: 120, stop: -5f));
        Assert.Equal(6, d.TookOffAt);
        Assert.Equal([(6, JumpKind.Tap, d.Launched[0].Height)], d.Launched);
        Near(0.5f, d.Highest, 0.01f, "a half-metre jump");
        Assert.Empty(d.Charge);
    }

    [Theory]
    [InlineData(1, 0.5f)]       // a tap: on its release, a human's jump
    [InlineData(18 + 60, 0.75f)] // half the charge: halfway from the tap to its own
    [InlineData(18 + 120, 1.0f)] // the whole charge: its own jump
    [InlineData(18 + 240, 1.0f)] // and no higher however long
    public void A_neonate_s_hold_charges_the_higher_the_longer_up_to_its_own_jump_and_jumps_on_the_release(int hold, float height)
    {
        Assert.True(Neonate.CanCharge);
        var d = Go(Ground().Build(), new(0, 0, 0.01f), 600, Along(Gait.Walk, press: 6, hold: hold, stop: -5f), Neonate);
        Assert.Equal(6 + hold, d.TookOffAt);                                             // on the release, never the press
        var launched = Assert.Single(d.Launched);
        Assert.Equal(hold >= 18 ? JumpKind.Charged : JumpKind.Tap, launched.Kind);
        Near(height, launched.Height, 0.01f, "the jump's height");
        Near(height, d.Highest, 0.01f, "the apex");
        Assert.Equal(hold >= 18 ? [ChargeStep.Began, ChargeStep.LetGo] : [], d.Charge);
    }

    [Fact]
    public void Charging_the_body_crouches_and_goes_at_most_at_a_walk()
    {
        // Running along while jump is held: charging, the neonate crouches and slows to its walk (1.3 m/s) — at its
        // braking, in a tenth of a second.
        var d = Go(Ground().Build(), new(-8, 0, 0.01f), 300, Along(Gait.Run, press: 30, hold: 120), Neonate);
        Assert.True(d.CrouchedCharging, d.ToString());
        Assert.InRange(d.FastestCharging, 1.0f, Neonate.WalkSpeed + 1e-3f);
        Assert.Equal(JumpKind.Charged, Assert.Single(d.Launched).Kind);
    }

    [Fact]
    public void Cancel_ends_a_charge_and_nothing_jumps_even_when_jump_is_let_go_after()
    {
        var d = Go(Ground().Build(), new(0, 0, 0.01f), 400, Along(Gait.Walk, press: 6, hold: 120, stop: -5f, cancel: 60), Neonate);
        Assert.Empty(d.Launched);
        Assert.Equal(-1, d.TookOffAt);
        Assert.Equal(1, d.Cancelled);
        Assert.Equal([ChargeStep.Began, ChargeStep.Cancelled], d.Charge);
        Assert.True(d.Highest < 0.02f, d.ToString());
        Assert.Equal(0, d.End.Crouched);                                                // standing again
    }

    [Fact]
    public void A_charged_jump_s_moves_reach_with_the_body_s_own_numbers_in_the_masquerade()
    {
        // An elder held to a human: a tap at a wall of 3.5 m reaches nothing (a human's grab goes to 2.85 m); the whole
        // charge (its own 3 m) mantles it (its own mantle, to 4 m), the masquerade or not (the powered leap).
        var level = Block(3.5f);
        var masq = Masquerade.HeldToHuman(Elder, Human);
        var tap = Go(level, new(0.45f, 0, 0.01f), 300, Along(Gait.Walk, press: 6, stop: 0f), masq);
        Assert.Empty(tap.Started);
        var charged = Go(level, new(0.45f, 0, 0.01f), 600, Along(Gait.Walk, press: 6, hold: 18 + 130, stop: 0f), masq);
        Assert.Equal([TraversalKind.Mantle], charged.Started);
        Near(3.5f, charged.Stood, 0.01f, "on the wall");
        Assert.Equal(JumpKind.Charged, Assert.Single(charged.Launched).Kind);
    }

    [Fact]
    public void A_charge_s_moves_reach_from_the_mode_s_numbers_to_the_body_s_own_as_far_as_the_charge_went()
    {
        // A walk in play: a neonate in the masquerade, jump held a hair past the hold (a jump of 0.51 m),
        // climbed onto a top of 1.3 m with its own reach. Now a charge reaches between a human's numbers and its own in
        // the proportion it went: a hair of one does as a tap (a mantle: a human vaults and climbs to 1.2 m), a whole one
        // climbs onto it with its own (to 1.6 m), half of one reaches half way (to 1.4 m: onto 1.3 m, a mantle at 1.5 m).
        var masq = Masquerade.HeldToHuman(Neonate, Human);
        Assert.Equal(1.2f, masq.VaultMax, 3);
        Assert.Equal(1.6f, masq.PoweredReach().VaultMax, 3);
        int hair = (int)(BodyMotor.HoldSeconds * World.TicksPerSecond) + 3, half = 18 + 60;
        TraversalKind First(float height, int hold, float rise)
        {
            var d = Go(Block(height), new(0.45f, 0, 0.01f), 600, Along(Gait.Walk, press: 6, hold: hold, stop: 0f), masq);
            var launched = Assert.Single(d.Launched);
            Assert.Equal(hold > 1 ? JumpKind.Charged : JumpKind.Tap, launched.Kind);
            Near(rise, launched.Height, 0.015f, "the jump's height (unchanged)");
            Assert.True(d.Stood >= height - 0.02f, d.ToString());
            return d.Started.FirstOrDefault();
        }
        Assert.Equal(TraversalKind.Mantle, First(1.3f, 1, 0.5f));          // a tap
        Assert.Equal(TraversalKind.Mantle, First(1.3f, hair, 0.51f));      // a hair of a charge: as a tap
        Assert.Equal(TraversalKind.ClimbOnto, First(1.3f, Full, 1.0f));    // the whole charge: its own, as before
        Assert.Equal(TraversalKind.ClimbOnto, First(1.3f, half, 0.75f));   // half: to 1.4 m
        Assert.Equal(TraversalKind.Mantle, First(1.5f, half, 0.75f));
        Assert.Equal(TraversalKind.ClimbOnto, First(1.5f, Full, 1.0f));

        // The numbers themselves: half way between the mode's and the body's own; a tap's are the mode's.
        var body = new Body { Powered = 1, JumpRise = 0.75f };
        Assert.Equal(0.5f, BodyMotor.ChargeShare(body, masq), 3);
        var reach = BodyMotor.MovesReach(body, masq);
        var own = masq.PoweredReach();
        Near((masq.VaultMax + own.VaultMax) / 2, reach.VaultMax, 1e-3f, "the vault");
        Near((masq.MantleMax + own.MantleMax) / 2, reach.MantleMax, 1e-3f, "the mantle");
        Near((masq.StandingReach + own.StandingReach) / 2, reach.StandingReach, 1e-3f, "the hands");
        Assert.Equal(masq.VaultMax, BodyMotor.MovesReach(body with { Powered = 0 }, masq).VaultMax);
    }

    // ---------------------------------------------------------------- hanging

    [Fact]
    public void Running_at_a_wall_of_2_4_m_with_forward_held_a_tap_goes_up_and_over_and_standing_it_hangs()
    {
        var level = Block(2.4f);
        var run = Go(level, new(-3f, 0, 0.01f), 400, PressAt(1f - 0.9f, Gait.Run));
        Assert.Equal([TraversalKind.Grab, TraversalKind.PullUp], run.Started);
        Near(2.4f, run.Stood, 0.01f, "up and over");
        var still = Go(level, new(0.5f, 0, 0.01f), 300, Along(Gait.Walk, press: 6, stop: 0f));
        Assert.Equal([TraversalKind.Grab, TraversalKind.Hang], still.Started);
        Assert.Equal(BodyState.Hanging, still.End.State);
    }

    // ---------------------------------------------------------------- a marked face

    [Fact]
    public void A_tap_at_a_face_marked_not_to_be_climbed_is_refused_and_told_once()
    {
        var built = Ground().Box(new(1, -3, 0), new(3, 3, 1.6f)).Build("marked");
        var level = new LevelSpace("marked", built.Collision.WithClimb(
            LevelClimb.Of([new NoClimbArea("edge", new TurnedBox(new(0.5f, -4f, 1.1f), 0f, new(1.5f, 8f, 1f)))])));
        var d = Go(level, new(0.45f, 0, 0.01f), 300, Along(Gait.Walk, press: 6));
        Assert.Empty(d.Started);
        Assert.Equal([TraversalKind.Mantle], d.Refused);
        var open = Go(built, new(0.45f, 0, 0.01f), 300, Along(Gait.Walk, press: 6));
        Assert.Equal([TraversalKind.Mantle], open.Started);
        Assert.Empty(open.Refused);
    }

    // ---------------------------------------------------------------- in a world: the events, the cancel key, a tape

    sealed record Rig(World W, EntityId Player);

    static Rig Make(LevelSpace level, ulong seed = 1)
    {
        var b = new WorldBuilder(seed);
        MovementServices.Register(b, level);
        MasqueradeServices.Register(b, new MasqueradeSettings { AutoSwitch = false });
        var w = b.Build();
        var profiles = w.Service<MovementProfiles>();
        profiles.Add(new MovementProfile(AssetId.Of("test", "movement/human"), "a human", Human, true), isHuman: true);
        int n = profiles.Add(new MovementProfile(AssetId.Of("test", "movement/neonate"), "a neonate", Neonate, true));
        var p = MovementServices.SpawnControlled(w, new(0, 0, 0.01f), 0f, Locomotion.Sandbox);
        profiles.Give(w, p, n);
        return new Rig(w, p);
    }

    static ActionFrame Frame(Buttons held) => new(Vector2.Zero, Vector2.Zero, held, default);

    [Fact]
    public void In_a_world_the_jump_s_events_tell_a_tap_a_charge_begun_and_let_go_and_a_charge_cancelled_by_the_crouch_key()
    {
        var r = Make(Block(5f));
        var launched = new List<JumpLaunched>();
        var charges = new List<JumpCharge>();
        void Run(int ticks, Buttons held)
        {
            for (int i = 0; i < ticks; i++)
            {
                MovementServices.Step(r.W, Frame(held));
                launched.AddRange(r.W.Events<JumpLaunched>().ThisTick);
                charges.AddRange(r.W.Events<JumpCharge>().ThisTick);
            }
        }
        Run(30, Buttons.None);
        Run(2, Buttons.Jump); Run(120, Buttons.None);                                     // a tap
        Run(Full, Buttons.Jump); Run(240, Buttons.None);                                  // a whole charge
        Assert.Equal([JumpKind.Tap, JumpKind.Charged], launched.Select(l => l.Kind));
        Near(0.5f, launched[0].Height, 0.01f, "the tap");
        Near(1.0f, launched[1].Height, 0.01f, "the charge");
        Assert.Equal([ChargeStep.Began, ChargeStep.LetGo], charges.Select(c => c.Step));
        Near(0.5f, charges[0].Height, 0.01f, "begun at the tap's height");
        Near(1.0f, charges[0].Most, 0.01f, "up to its own");

        // One key carrying crouch and cancel (his X): pressed while charging it cancels and does not crouch; letting
        // jump go then jumps nothing.
        launched.Clear(); charges.Clear();
        Run(60, Buttons.Jump);
        Assert.True(BodyMotor.IsCharging(r.W.Get<Body>(r.Player)));
        Run(1, Buttons.Jump | Buttons.Crouch | Buttons.Cancel);
        Assert.False(BodyMotor.IsCharging(r.W.Get<Body>(r.Player)));
        Run(20, Buttons.Jump | Buttons.Crouch | Buttons.Cancel);                           // both still held: swallowed
        Assert.Equal(0, r.W.Get<Body>(r.Player).Crouched);
        Run(120, Buttons.None);
        Assert.Empty(launched);
        Assert.Equal([ChargeStep.Began, ChargeStep.Cancelled], charges.Select(c => c.Step));
        Assert.True(r.W.Get<Body>(r.Player).Feet.Z < 0.02f);
    }

    [Fact]
    public void A_tape_with_a_charge_replays_digest_for_digest()
    {
        var level = Block(1.5f);
        static ActionFrame At(int t)
        {
            var held = Buttons.None;
            if (t % 400 is >= 50 and < 52) held |= Buttons.Jump;                         // a tap
            if (t % 400 is >= 150 and < 150 + 100) held |= Buttons.Jump;                  // a charge
            if (t % 800 == 330) held |= Buttons.Cancel;
            var move = t % 400 < 300 ? new Vector2(0, 0.6f) : new Vector2(0.4f, -1f);
            return new ActionFrame(move, new Vector2(t % 400 < 20 ? 0.01f : 0f, 0f), held, default);
        }
        var r = Make(level, seed: 9);
        var tape = new Tape { Header = new TapeHeader { Build = "test", Seed = 9 } };
        int charged = 0;
        for (int t = 0; t < 1600; t++)
        {
            var f = At(t);
            MovementServices.Step(r.W, f);
            foreach (var l in r.W.Events<JumpLaunched>().ThisTick) if (l.Kind == JumpKind.Charged) charged++;
            tape.Frames.Add(f);
            if (r.W.Tick % 60 == 0) tape.Digests[r.W.Tick] = r.W.Digest();
        }
        Assert.True(charged >= 1, "no charged jump in the drive");
        var path = Path.Combine(Directory.CreateTempSubdirectory("vitomir-jump-").FullName, "t.tape");
        tape.Write(path);
        var back = Tape.Read(path);
        var again = Make(level, seed: 9);
        int agreed = 0;
        foreach (var f in back.Frames)
        {
            MovementServices.Step(again.W, f);
            if (back.Digests.TryGetValue(again.W.Tick, out var want)) { Assert.Equal(want, again.W.Digest()); agreed++; }
        }
        Assert.True(agreed >= 26, $"only {agreed} digests compared");
    }
}
