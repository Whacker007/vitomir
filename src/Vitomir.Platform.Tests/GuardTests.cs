// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Reflection;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.OneOne;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The guard: the swing's cancel (the key only before the strike, the dodge at any point, the next swing's wait, nothing
/// met after); block and swing together (a lone weapon lowers and raises again, a shield is kept on its arm's layer, block
/// pressed mid-swing); the guard turning toward a threat within its arc and not to one outside it; the parry window (the
/// press, the re-press wait, a blade met in it and outside it); each verdict and what the follow-through does with it; the
/// clash as one paired transaction whichever body comes first; a load mid-parry; nothing allocated per tick. Every test skips
/// without the 1.1 install (VTMR_GAME_DIR).
/// </summary>
public sealed class GuardTests(ITestOutputHelper output)
{
    static readonly Lazy<OneOneLibrary?> Library = new(() => GameInstall.TryOpen() is { } i ? new OneOneLibrary(i) : null);

    static OneOneLibrary Lib()
    {
        Assert.SkipUnless(Library.Value is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        return Library.Value!;
    }

    static readonly Dictionary<(string, string?), OneOneCharacter> Loaded = [];

    static OneOneCharacter Character(string name, string? weapon = null)
    {
        var lib = Lib();
        lock (Loaded)
        {
            if (!Loaded.TryGetValue((name, weapon), out var c))
                Loaded[(name, weapon)] = c = lib.Character(name, weapon) ?? throw new Xunit.Sdk.XunitException($"{name} did not load");
            return c;
        }
    }

    static int Ticks(float seconds) => (int)MathF.Round(seconds * World.TicksPerSecond);

    /// <summary>The rules' stand-in: every guard contact answered with the verdict <see cref="Verdict"/> gives it (none: no line).</summary>
    sealed class StandInGuard : ISystem
    {
        public Func<ContactMade, GuardVerdict?> Verdict { get; set; } = _ => GuardVerdict.Held;
        public float Riposte { get; set; } = 0.3f;
        public float Broken { get; set; } = 0.5f;

        public void Run(World world)
        {
            var made = world.Events<ContactMade>().ThisTick;
            for (int i = 0; i < made.Count; i++)
            {
                var c = made[i];
                if (c.Guard == GuardMeet.None || Verdict(c) is not { } v) continue;
                world.Events<GuardResolved>().Publish(new GuardResolved(c.ContactId, c.Attacker, c.Target, c.Tick, c.Met, c.Guard, v, 1, 1, 1, 0.5f, 0.5f, 0.1, 0.1,
                    1f, v is GuardVerdict.Overpowered or GuardVerdict.ClashDominates ? 0.6f : 0f, v == GuardVerdict.Parried ? Riposte : 0f, v == GuardVerdict.Overpowered ? Broken : 0f));
            }
        }
    }

    sealed class Ring
    {
        public required World World { get; init; }
        public required Dictionary<string, int> Kinds { get; init; }
        public StandInGuard? Rules { get; init; }
        public CharacterAssets Assets => World.Service<CharacterAssets>();
        public readonly List<ContactMade> Contacts = [];
        public readonly List<AttackBegun> Begun = [];
        public readonly List<AttackRefused> Refused = [];
        public readonly List<BlockRaised> Raised = [];
        public readonly List<BlockLowered> Lowered = [];
        public readonly List<StrikeMoment> Strikes = [];
        public readonly List<SwingCancelled> Cancelled = [];
        public readonly List<ParryBegun> Parries = [];
        public readonly List<SwingFollowThrough> Follow = [];
        public readonly List<GuardResolved> Verdicts = [];
        public readonly List<GuardBroken> Broken = [];

        public void Step(int ticks = 1)
        {
            for (int i = 0; i < ticks; i++)
            {
                MovementServices.Step(World, ActionFrame.Idle);
                Contacts.AddRange(World.Events<ContactMade>().ThisTick);
                Begun.AddRange(World.Events<AttackBegun>().ThisTick);
                Refused.AddRange(World.Events<AttackRefused>().ThisTick);
                Raised.AddRange(World.Events<BlockRaised>().ThisTick);
                Lowered.AddRange(World.Events<BlockLowered>().ThisTick);
                Strikes.AddRange(World.Events<StrikeMoment>().ThisTick);
                Cancelled.AddRange(World.Events<SwingCancelled>().ThisTick);
                Parries.AddRange(World.Events<ParryBegun>().ThisTick);
                Follow.AddRange(World.Events<SwingFollowThrough>().ThisTick);
                Verdicts.AddRange(World.Events<GuardResolved>().ThisTick);
                Broken.AddRange(World.Events<GuardBroken>().ThisTick);
            }
        }

        public EntityId Put(string kind, float x, float y, float yawDegrees)
        {
            var e = MovementServices.SpawnBody(World, new(x, y, 0.01f), yawDegrees * DetMath.DegToRad, Locomotion.Sandbox);
            World.Set(e, CharacterAnimation.For(Assets, Kinds[kind]));
            World.Set(e, MotionInput.Standing);
            CombatServices.Arm(World, e);
            return e;
        }

        public ref CombatIntent Intent(EntityId e) => ref World.Get<CombatIntent>(e);
        public ref Combatant Fighter(EntityId e) => ref World.Get<Combatant>(e);
        public ref CharacterAnimation Anim(EntityId e) => ref World.Get<CharacterAnimation>(e);

        /// <summary><paramref name="who"/> taps attack (a quick blow, made on release), aimed <paramref name="aimDegrees"/>.</summary>
        public void Swing(EntityId who, float aimDegrees = 0f)
        {
            ref var intent = ref Intent(who);
            intent.Aims = true;
            intent.AimPitch = aimDegrees * DetMath.DegToRad;
            intent.Attack = true;
            Step();
            Intent(who).Attack = false;
            Step();     // let go: the blow begins
        }

        /// <summary>Cancels <paramref name="who"/>'s swing between ticks (as a system would in the tick), keeping the event.</summary>
        public bool Cancel(EntityId who, SwingCancelCause cause)
        {
            bool done = SwingCancel.Do(World, who, cause);
            Cancelled.AddRange(World.Events<SwingCancelled>().ThisTick);
            return done;
        }
    }

    static readonly string[] AllKinds = ["Christof", "rudolfGuard", "Civ12male"];

    /// <summary>A world as the program makes it, contact and the follow-through; <paramref name="rules"/> a stand-in deciding guard contacts; <paramref name="guard"/> tunes the guard.</summary>
    static Ring Make(bool rules = false, Action<GuardSettings>? guard = null, ulong seed = 7, bool brains = false)
    {
        var assets = new CharacterAssets();
        var kinds = new Dictionary<string, int>();
        foreach (var k in AllKinds) kinds[k] = OneOneLibrary.Register(assets, Character(k, k == "Christof" ? "BroadSword" : null));
        var level = new TestLevel().Floor(-60, -60, 60, 60).Build();
        var b = new WorldBuilder(seed);
        MovementServices.Register(b, level);
        MasqueradeServices.Register(b, new MasqueradeSettings { AutoSwitch = false });
        b.Animation(assets);
        b.HitVolumes();
        BodyMotionSystem.Register(b);
        CombatServices.Register(b);
        var g = b.Peek.Service<GuardSettings>();
        g.RulesDecide = rules;
        guard?.Invoke(g);
        b.Service(new HurtRule { OnContact = false });   // the struck stand still (no rules here to flinch them by harm)
        b.Event<HarmResolved>();
        ContactServices.Register(b, new ContactConfig());
        StandInGuard? stand = null;
        if (rules) b.System(Phase.Resolve, 95, "test.stand-in-guard", stand = new StandInGuard());
        if (brains) DuelBrains.Register(b);
        var w = b.Build();
        w.Service<HitVolumes>().Prepare(assets);
        ContactServices.Prepare(w);
        return new Ring { World = w, Kinds = kinds, Rules = stand };
    }

    // ---- the swing's cancel ----

    [Fact]
    public void The_cancel_key_ends_a_swing_in_its_wind_up_with_no_blow_and_the_body_swings_again_after_the_short_return()
    {
        var r = Make();
        var a = r.Put("Christof", 0, 0, 90);
        var t = r.Put("Civ12male", 0, 1.3f, -90);
        r.Step(30);
        r.Swing(a);
        r.Step(Ticks(0.1f));
        Assert.True(SwingCancel.Can(r.World, a, SwingCancelCause.Cancel));
        Assert.True(r.Cancel(a, SwingCancelCause.Cancel));
        var cancelled = Assert.Single(r.Cancelled);
        Assert.Equal((a, 1, SwingCancelCause.Cancel, true), (cancelled.Entity, cancelled.Swing, cancelled.Cause, cancelled.BeforeStrike));
        Assert.Equal(0.2f, r.Fighter(a).Busy, 3);
        r.Step(Ticks(1.5f));
        Assert.Empty(r.Contacts);           // no blow, nothing met
        Assert.Empty(r.Strikes);
        Assert.False(SwingCancel.Can(r.World, a, SwingCancelCause.Cancel));   // nothing left to cancel
        // The next swing comes after the return, well before the whole swing would have ended (a feint is quick).
        var r2 = Make();
        var a2 = r2.Put("Christof", 0, 0, 90);
        r2.Step(30);
        r2.Swing(a2);
        r2.Step(Ticks(0.1f));
        SwingCancel.Do(r2.World, a2, SwingCancelCause.Cancel);
        long cancelledAt = r2.World.Tick;
        r2.Intent(a2).Attack = true;    // pressed at once: buffered, made as the return ends
        r2.Step();
        r2.Intent(a2).Attack = false;
        r2.Step(Ticks(0.5f));
        Assert.Equal(2, r2.Begun.Count);
        float wait = (r2.Begun[1].Tick - cancelledAt) * World.TickSeconds;
        output.WriteLine($"the next swing {wait:0.000} s after the cancel");
        Assert.InRange(wait, 0.19f, 0.25f);
    }

    [Fact]
    public void The_cancel_key_does_nothing_after_the_strike_and_the_swing_plays_out()
    {
        var r = Make();
        var a = r.Put("Christof", 0, 0, 90);
        var t = r.Put("Civ12male", 0, 1.3f, -90);
        r.Step(30);
        r.Swing(a);
        while (r.Strikes.Count == 0 && r.Contacts.Count == 0) r.Step();
        Assert.False(SwingCancel.Can(r.World, a, SwingCancelCause.Cancel));
        Assert.False(r.Cancel(a, SwingCancelCause.Cancel));
        Assert.Empty(r.Cancelled);
        r.Step(Ticks(1f));
        Assert.Contains(r.Contacts, c => c.Target == t);
    }

    [Theory]
    [InlineData(0.1f)]    // the wind-up
    [InlineData(0.36f)]   // the active window (it opens about a third of a second in)
    [InlineData(0.7f)]    // after the bite: the stop's bring-back
    public void A_dodge_cancels_the_swing_at_any_point_nothing_is_met_after_and_the_next_swing_waits_for_the_whole_swing(float at)
    {
        var r = Make();
        var a = r.Put("Christof", 0, 0, 90);
        var t = r.Put("Civ12male", 0, 1.3f, -90);
        r.Step(30);
        r.Swing(a);
        r.Step(Ticks(at));
        float busy = r.Fighter(a).Busy;
        int metBefore = r.Contacts.Count;
        Assert.True(SwingCancel.Can(r.World, a, SwingCancelCause.Dodge));
        Assert.True(r.Cancel(a, SwingCancelCause.Dodge));
        Assert.Equal(SwingCancelCause.Dodge, Assert.Single(r.Cancelled).Cause);
        Assert.Equal(busy, r.Fighter(a).Busy);      // the busy time stands: dodging never quickens attacks
        Assert.True(r.Anim(a).ActionEnding);
        r.Step(Ticks(1.5f));
        Assert.Equal(metBefore, r.Contacts.Count);  // nothing met after the cancel
        output.WriteLine($"at {at}: busy {busy:0.000} s left, {metBefore} contacts before");
        // Pressed at once: the blow comes when the cancelled swing would have ended, no sooner.
        var r2 = Make();
        var a2 = r2.Put("Christof", 0, 0, 90);
        r2.Step(30);
        r2.Swing(a2);
        r2.Step(Ticks(at));
        float left = r2.Fighter(a2).Busy;
        SwingCancel.Do(r2.World, a2, SwingCancelCause.Dodge);
        long cancelledAt = r2.World.Tick;
        r2.Intent(a2).Attack = true;
        r2.Step();
        r2.Intent(a2).Attack = false;
        r2.Step(Ticks(2f));
        Assert.Equal(2, r2.Begun.Count);
        Assert.InRange((r2.Begun[1].Tick - cancelledAt) * World.TickSeconds, left - 0.02f, left + 0.03f);
    }

    /// <summary>
    /// The whole path, the control's and the guard's together — a dodge asked as the player's
    /// control asks it (<see cref="Dodges.Ask"/>) while a real swing plays cancels the swing through <see cref="SwingCancel"/>
    /// and begins the dodge, in the wind-up and after the strike alike; nothing is met after it, and the body moves.
    /// </summary>
    [Theory]
    [InlineData(0.1f)]    // the wind-up
    [InlineData(0.7f)]    // after the strike: the bring-back
    public void Lead_a_dodge_asked_mid_swing_cancels_the_swing_and_the_body_dodges(float at)
    {
        var r = Make();
        var a = r.Put("Christof", 0, 0, 90);
        var t = r.Put("Civ12male", 0, 1.3f, -90);
        r.Step(30);
        r.Swing(a);
        r.Step(Ticks(at));
        Assert.True(r.Fighter(a).Busy > 0f && ActionRoles.IsAttack(r.Anim(a).Action));
        int metBefore = r.Contacts.Count;
        output.WriteLine($"at {at}: strikes {r.Strikes.Count}, contacts {metBefore}, clip {r.Anim(a).ActionTime:0.000} of strike {r.Fighter(a).StrikeAt:0.000}, rate {r.Anim(a).ActionRate:0.00}");
        var from = r.World.Get<Body>(a).Feet;
        var begun = new List<DodgeBegun>();
        var refused = new List<DodgeRefused>();
        Dodges.Ask(r.World, a, new Vector2(-1f, 0f));
        for (int i = 0; i < Ticks(1.2f); i++)
        {
            r.Step();
            begun.AddRange(r.World.Events<DodgeBegun>().ThisTick);
            refused.AddRange(r.World.Events<DodgeRefused>().ThisTick);
        }
        Assert.Empty(refused);
        Assert.Single(begun);
        Assert.Equal(SwingCancelCause.Dodge, Assert.Single(r.Cancelled).Cause);
        Assert.Equal(metBefore == 0, r.Cancelled[0].BeforeStrike);   // a blade that met the body is past its strike, Strike mark or not
        if (at > 0.3f) Assert.True(metBefore > 0, "the blade met the body before the dodge");
        Assert.Equal(metBefore, r.Contacts.Count);
        Assert.True(Vector3.Distance(from, r.World.Get<Body>(a).Feet) > 0.5f, "the dodge moved the body");
    }

    /// <summary>
    /// A blade that bit a body before its clip's <c>Strike</c> mark (1.1's mark comes late in
    /// some clips) has struck: the cancel key cannot cancel it in its bring-back (that would cut the swing's time short after a
    /// blow landed); only a dodge can.
    /// </summary>
    [Fact]
    public void Lead_the_cancel_key_cannot_cancel_a_swing_whose_blade_met_a_body_before_its_strike_mark()
    {
        var r = Make();
        var a = r.Put("Christof", 0, 0, 90);
        var t = r.Put("Civ12male", 0, 1.3f, -90);
        r.Step(30);
        r.Swing(a);
        r.Step(Ticks(0.7f));
        Assert.NotEmpty(r.Contacts);
        Assert.Empty(r.Strikes);   // met before the mark
        Assert.False(SwingCancel.Can(r.World, a, SwingCancelCause.Cancel));
        Assert.False(r.Cancel(a, SwingCancelCause.Cancel));
        Assert.True(SwingCancel.Can(r.World, a, SwingCancelCause.Dodge));
    }

    // ---- block and swing together ----

    [Fact]
    public void A_lone_sword_attacks_from_its_raised_guard_and_the_guard_comes_back_up_when_the_swing_is_over()
    {
        var r = Make();
        var a = r.Put("Christof", 0, 0, 90);
        r.Intent(a).Block = true;
        r.Step(30);
        Assert.True(r.Fighter(a).Blocking);
        r.Swing(a);                                   // block still held
        Assert.Empty(r.Refused);                      // not refused (an earlier build refused it: Blocking)
        Assert.Single(r.Begun);
        Assert.Single(r.Lowered);                     // the lone weapon's guard lowered for the swing
        Assert.False(r.Fighter(a).Blocking);
        Assert.True(ActionRoles.IsAttack(r.Anim(a).Action));
        r.Step(Ticks(2f));
        Assert.Equal(2, r.Raised.Count);              // back up when the swing was over, block still held
        Assert.True(r.Fighter(a).Blocking);
        Assert.Equal(ActionRole.Block, r.Anim(a).Action);
        // With the guard off, the earlier build's: refused while blocking.
        var off = Make(guard: g => g.Enabled = false);
        var b = off.Put("Christof", 0, 0, 90);
        off.Intent(b).Block = true;
        off.Step(30);
        off.Swing(b);
        Assert.Equal(AttackRefusal.Blocking, Assert.Single(off.Refused).Why);
    }

    [Fact]
    public void Block_pressed_during_a_swing_raises_the_guard_when_the_swing_is_over()
    {
        var r = Make();
        var a = r.Put("Christof", 0, 0, 90);
        r.Step(30);
        r.Swing(a);
        r.Step(Ticks(0.2f));
        r.Intent(a).Block = true;
        r.Step(Ticks(0.2f));
        Assert.Empty(r.Raised);                       // not during the swing (a lone weapon)
        Assert.True(ActionRoles.IsAttack(r.Anim(a).Action));
        r.Step(Ticks(2f));
        var raised = Assert.Single(r.Raised);
        Assert.True(raised.Tick >= r.Begun[0].Tick + Ticks(0.9f), $"raised at {raised.Tick}, the swing began at {r.Begun[0].Tick}");
        Assert.Empty(r.Parries);                      // a press during a swing opens no parry window
    }

    [Fact]
    public void A_shield_is_kept_raised_on_its_arm_while_the_sword_arm_swings_and_meets_blows_all_the_while()
    {
        var r = Make();
        var g = r.Put("rudolfGuard", 0, 0, 90);
        r.Intent(g).Block = true;
        r.Step(40);
        Assert.True(HeldItems.Shield(r.World, g, out var raised));
        r.Swing(g);
        r.Step(Ticks(0.3f));
        Assert.True(ActionRoles.IsAttack(r.Anim(g).Action));
        Assert.True(r.Fighter(g).Blocking && r.Fighter(g).ShieldGuard);
        Assert.Equal(1f, r.Anim(g).GuardWeight);
        Assert.True(HeldItems.Shield(r.World, g, out var swinging));
        float moved = Vector3.Distance(raised.Centre, swinging.Centre);
        // With the guard off the shield goes where 1.1's attack puts the off arm.
        var off = Make(guard: x => x.Enabled = false);
        var g2 = off.Put("rudolfGuard", 0, 0, 90);
        off.Step(40);
        off.Swing(g2);
        off.Step(Ticks(0.3f));
        Assert.True(HeldItems.Shield(off.World, g2, out var loose));
        float looseMoved = Vector3.Distance(raised.Centre, loose.Centre);
        output.WriteLine($"the shield's centre mid-swing: {moved:0.000} m from its raised place (kept), {looseMoved:0.000} m without the layer");
        // Kept: the arm turned back against the swing's turn of the chest (the shoulder still moves with the chest).
        Assert.True(moved < 0.6f * looseMoved, $"kept {moved}, loose {looseMoved}");
        Assert.Empty(r.Lowered);
        // When the swing is over the block is back on the channel at its raised frame and the layer let go.
        r.Step(Ticks(2f));
        Assert.Equal(ActionRole.Block, r.Anim(g).Action);
        Assert.False(r.Fighter(g).ShieldGuard);
        Assert.Equal(0f, r.Anim(g).GuardWeight);
    }

    [Fact]
    public void A_blow_meets_the_shield_held_up_while_its_owner_swings()
    {
        var r = Make();
        var g = r.Put("rudolfGuard", 0, 1.3f, -90);
        var a = r.Put("Christof", 0, 0, 90);
        r.Intent(g).Block = true;
        r.Step(40);
        r.Swing(g);                 // the guard swings (his blade swings over Christof's head? either way his shield stays up)
        r.Step(Ticks(0.05f));
        r.Swing(a);
        r.Step(Ticks(1.2f));
        output.WriteLine(string.Join("; ", r.Contacts.Select(c => $"t{c.Tick} {c.Attacker}->{c.Target} {c.Met} {c.Guard} {c.Site}")));
        var mine = r.Contacts.Where(c => c.Attacker == a).ToList();
        Assert.NotEmpty(mine);
        Assert.Equal((ContactMet.Shield, GuardMeet.Shield), (mine[0].Met, mine[0].Guard));
    }

    // ---- the guard's direction ----

    /// <summary>The guard's turn after a foe at <paramref name="bearing"/>° (from the guard's facing) begins a blow at <paramref name="aim"/>°.</summary>
    (float Twist, float Pitch, Ring R, EntityId G, EntityId A) Turned(float bearing, float aim, float d = 1.4f, float after = 0.3f)
    {
        var r = Make();
        var g = r.Put("Christof", 0, 0, 90);
        float b = (90f + bearing) * DetMath.DegToRad;
        var (s, c) = DetMath.SinCos(b);
        var a = r.Put("Christof", c * d, s * d, 90f + bearing + 180f);
        r.Intent(g).Block = true;
        r.Step(40);
        Assert.Equal(0f, r.Anim(g).GuardTwist);
        r.Swing(a, aim);
        r.Step(Ticks(after));
        return (r.Anim(g).GuardTwist * DetMath.RadToDeg, r.Fighter(g).GuardPitch * DetMath.RadToDeg, r, g, a);
    }

    [Fact]
    public void The_raised_guard_turns_toward_a_blow_begun_within_its_arc_at_its_speed_and_not_toward_one_outside_it()
    {
        var front = Turned(0f, 0f);
        var left = Turned(40f, 0f);
        var right = Turned(-40f, 0f);
        var behind = Turned(120f, 0f);
        var high = Turned(0f, 20f);
        var low = Turned(0f, -25f);
        output.WriteLine($"twist (deg): front {front.Twist:0.0}, left 40 {left.Twist:0.0}, right 40 {right.Twist:0.0}, behind 120 {behind.Twist:0.0}");
        output.WriteLine($"pitch (deg): front {front.Pitch:0.0}, high {high.Pitch:0.0}, low {low.Pitch:0.0}");
        Assert.True(left.Twist > 10f && right.Twist < -10f, "toward the side the blow comes from");
        Assert.InRange(MathF.Abs(left.Twist), 0f, 50.01f);
        Assert.InRange(front.Twist, -1f, 1f);         // straight ahead: no turn
        Assert.Equal(0f, behind.Twist);               // outside the arc: the guard stays at rest
        Assert.True(left.R.Anim(left.G).GuardSide != 0f || left.R.Anim(left.G).GuardHeight != 0f, "the stance moved toward the blow");
        Assert.True(high.Pitch > low.Pitch + 5f, $"high {high.Pitch}, low {low.Pitch}");
        // The speed: after a tenth of a second the guard has turned at most 200°/s × 0.1 s.
        var early = Turned(40f, 0f, after: 0.1f);
        Assert.InRange(MathF.Abs(early.Twist), 0f, 200f * 0.11f + 0.5f);
        // The guard off: no turn at all.
        var r = Make(guard: x => x.Enabled = false);
        var g = r.Put("Christof", 0, 0, 90);
        var a = r.Put("Christof", -0.9f, 1.07f, -50f);
        r.Intent(g).Block = true;
        r.Step(40);
        r.Swing(a);
        r.Step(Ticks(0.3f));
        Assert.Equal(0f, r.Anim(g).GuardTwist);
    }

    /// <summary>
    /// The guard's coverage, measured: the combat rules' forehand and backhand at five heights against a held guard 1.3 m away — a
    /// lone sword (Christof's broadsword) and a rapier with a shield (rudolfGuard) — with the directed guard and with the first
    /// still one (the guard off). What each blow met first is written out; the directed lone sword meets more of them.
    /// </summary>
    [Fact]
    public void The_directed_guard_meets_more_blows_than_the_still_one_and_says_what_each_met()
    {
        int Count(string kind, bool on, string what)
        {
            var line = new System.Text.StringBuilder($"{kind}, the guard {(on ? "directed" : "still (off)")}: ");
            int met = 0;
            foreach (var aim in new[] { -25f, -10f, 0f, 15f, 25f })
                foreach (var variant in new[] { 0, 1 })
                {
                    var r = Make(guard: g => g.Enabled = on);
                    var a = r.Put("Christof", 0, 0, 90);
                    var d = r.Put(kind, 0, 1.3f, -90);
                    r.Intent(d).Block = true;
                    r.Step(40);
                    r.Fighter(a).NextVariant = variant;
                    r.Swing(a, aim);
                    r.Step(Ticks(1.2f));
                    var c = r.Contacts.FirstOrDefault();
                    string got = r.Contacts.Count == 0 ? "nothing" : c.Met == ContactMet.Body ? c.Site! : c.Met.ToString();
                    if (got == what || what == "guard" && c.Met is ContactMet.Weapon or ContactMet.Shield && r.Contacts.Count > 0) met++;
                    line.Append($"[{aim:+0;-0}° {(variant == 0 ? "fore" : "back")}: {got}] ");
                }
            output.WriteLine(line.ToString());
            return met;
        }
        int on = Count("Christof", true, "guard"), off = Count("Christof", false, "guard");
        int shieldOn = Count("rudolfGuard", true, "guard"), shieldOff = Count("rudolfGuard", false, "guard");
        output.WriteLine($"met at the guard: lone sword {on} of 10 directed, {off} still; sword and shield {shieldOn} directed, {shieldOff} still");
        Assert.True(on > off, $"directed {on}, still {off}");
        Assert.True(shieldOn >= 6, $"sword and shield {shieldOn}");
    }

    // ---- the parry window ----

    [Fact]
    public void The_block_press_opens_a_parry_window_and_another_press_opens_one_only_after_the_wait()
    {
        var r = Make();
        var g = r.Put("Christof", 0, 0, 90);
        r.Step(30);
        r.Intent(g).Block = true;
        r.Step();
        var p = Assert.Single(r.Parries);
        Assert.Equal(g, p.Entity);
        Assert.Equal(0.25f, r.Fighter(g).ParryLeft, 3);
        r.Step(Ticks(0.25f));
        Assert.Equal(0f, r.Fighter(g).ParryLeft);
        Assert.True(r.Fighter(g).Blocking);           // held after the window: an ordinary guard
        r.Intent(g).Block = false; r.Step(2);
        r.Intent(g).Block = true; r.Step();
        Assert.Single(r.Parries);                     // pressed again 0.27 s after: within the 0.6 s wait, no window
        Assert.Equal(0f, r.Fighter(g).ParryLeft);
        r.Intent(g).Block = false; r.Step(Ticks(0.4f));
        r.Intent(g).Block = true; r.Step();
        Assert.Equal(2, r.Parries.Count);             // after the wait: a new window
    }

    /// <summary>Christof strikes (aimed 15° up) at a second Christof 1.3 m in front holding his guard, pressed <paramref name="pressBefore"/> s before the blow begins (null: held from the start).</summary>
    Ring Guarded(float? pressBefore, bool rules = true, Func<ContactMade, GuardVerdict?>? verdict = null)
    {
        var r = Make(rules: rules);
        if (verdict is not null) r.Rules!.Verdict = verdict;
        var a = r.Put("Christof", 0, 0, 90);
        var d = r.Put("Christof", 0, 1.3f, -90);
        r.Step(30);
        if (pressBefore is null) { r.Intent(d).Block = true; r.Step(30); }
        int lead = pressBefore is { } pb ? Ticks(pb) : 0;
        if (pressBefore is not null && lead < 0) { r.Swing(a, 15f); r.Step(-lead - 1); r.Intent(d).Block = true; }
        else if (pressBefore is not null) { r.Intent(d).Block = true; r.Step(lead); r.Swing(a, 15f); }
        if (pressBefore is null) r.Swing(a, 15f);
        r.Step(Ticks(1.5f));
        output.WriteLine(string.Join("; ", r.Contacts.Select(c => $"t{c.Tick} {c.Met} {c.Guard} {c.Site} #{c.Chain}")) + " | "
            + string.Join("; ", r.Follow.Select(f => $"t{f.Tick} {f.Outcome} {f.Met}")));
        return r;
    }

    [Fact]
    public void A_blade_met_in_the_parry_window_is_a_parry_and_outside_it_a_held_blade()
    {
        // Pressed as the blow begins: its blade meets the guard about a third of a second later, outside a 0.25 s window...
        var held = Guarded(null, rules: false);
        var c = held.Contacts.First();
        Assert.Equal((ContactMet.Weapon, GuardMeet.Blade), (c.Met, c.Guard));
        // ...pressed 0.3 s after the blow began (the window, 0.3 to 0.55 s, spans the blade's arrival): a parry.
        var timed = Guarded(-0.3f, rules: false);
        var p = timed.Contacts.First();
        Assert.Equal((ContactMet.Weapon, GuardMeet.Parry), (p.Met, p.Guard));
    }

    [Fact]
    public void Each_verdict_does_what_it_says_held_springs_back_parried_springs_back_farther_and_lengthens_the_recovery_overpowered_goes_on()
    {
        var held = Guarded(null, verdict: _ => GuardVerdict.Held);
        var h = held.Follow.First();
        Assert.Equal((BladeOutcome.Rebounds, ContactMet.Weapon), (h.Outcome, h.Met));
        Assert.Single(held.Verdicts);
        Assert.DoesNotContain(held.Contacts, x => x.Met == ContactMet.Body);
        // No verdict at all (no rules for it): held, as before.
        var none = Guarded(null, verdict: _ => null);
        Assert.Equal(BladeOutcome.Rebounds, none.Follow.First().Outcome);

        // Parried: a larger rebound, the attacker's recovery longer by the riposte (no harm, no hit for anyone).
        var parried = Guarded(null, verdict: _ => GuardVerdict.Parried);
        Assert.Equal(BladeOutcome.Rebounds, parried.Follow.First().Outcome);
        Assert.DoesNotContain(parried.Contacts, x => x.Met == ContactMet.Body);
        // The attacker swings again later: the busy left and the clip sprung back a tenth of a second after the meeting.
        Rebuilt(GuardVerdict.Held, out float busyHeld, out float backHeld);
        Rebuilt(GuardVerdict.Parried, out float busyParried, out float backParried);
        output.WriteLine($"busy after the meeting: held {busyHeld:0.000} s, parried {busyParried:0.000} s; clip sprung back {backHeld:0.000} / {backParried:0.000} s");
        Assert.Equal(busyHeld + 0.3f, busyParried, 2);
        Assert.True(backParried > backHeld * 1.5f, "a parried blade springs back farther");

        // Overpowered: the blade goes on through the guard to the body behind it; the guard is broken and lowered.
        var over = Guarded(null, verdict: c => GuardVerdict.Overpowered);
        Assert.Equal(BladeOutcome.GoesOn, over.Follow.First().Outcome);
        var broken = Assert.Single(over.Broken);
        Assert.Equal(0.5f, broken.Seconds);
        var body = over.Contacts.FirstOrDefault(x => x.Met == ContactMet.Body);
        Assert.True(body.Met == ContactMet.Body, "the blade went on to the body behind the guard");
        Assert.Equal(1, body.Chain);
        Assert.Equal(1, over.Contacts.Count(x => x.Met == ContactMet.Weapon));   // the pushed-through guard is met no more
    }

    /// <summary>A guard met with the stand-in's <paramref name="verdict"/>: the attacker's busy time left and the clip time sprung back a tick after the verdict.</summary>
    Ring Rebuilt(GuardVerdict verdict, out float busy, out float back)
    {
        var r = Make(rules: true);
        r.Rules!.Verdict = _ => verdict;
        var a = r.Put("Christof", 0, 0, 90);
        var d = r.Put("Christof", 0, 1.3f, -90);
        r.Step(30);
        r.Intent(d).Block = true;
        r.Step(30);
        r.Swing(a, 15f);
        while (r.Verdicts.Count == 0 && r.World.Tick < 400) r.Step();
        float atMeet = r.Anim(a).ActionTime;
        r.Step(Ticks(0.1f));
        busy = r.Fighter(a).Busy;
        back = atMeet - r.Anim(a).ActionTime;
        return r;
    }

    // ---- the clash ----

    (Ring R, EntityId A, EntityId B) Clashing(bool swap, bool rules = false, Func<ContactMade, GuardVerdict?>? verdict = null)
    {
        var r = Make(rules: rules);
        if (verdict is not null) r.Rules!.Verdict = verdict;
        EntityId a, b;
        if (!swap) { a = r.Put("Christof", 0, 0, 90); b = r.Put("Christof", 0, 1.5f, -90); }
        else { b = r.Put("Christof", 0, 1.5f, -90); a = r.Put("Christof", 0, 0, 90); }
        r.Step(30);
        r.Fighter(a).NextVariant = r.Fighter(b).NextVariant = 1;    // both backhands at 1.5 m: their blades cross (probed)
        r.Intent(a).Aims = r.Intent(b).Aims = true;
        r.Intent(a).Attack = r.Intent(b).Attack = true;
        r.Step();
        r.Intent(a).Attack = r.Intent(b).Attack = false;
        r.Step(Ticks(1.5f));
        output.WriteLine($"swap {swap}: " + string.Join("; ", r.Contacts.Select(c => $"t{c.Tick} {(c.Attacker == a ? "a" : "b")} {c.Met} {c.Guard} {c.Site}")));
        return (r, a, b);
    }

    [Fact]
    public void Two_blades_swinging_into_each_other_clash_as_one_paired_transaction_whichever_body_comes_first()
    {
        var (r, a, b) = Clashing(swap: false);
        var (s, sa, sb) = Clashing(swap: true);
        var clash = r.Contacts.Where(c => c.Guard == GuardMeet.Clash).ToList();
        Assert.Equal(2, clash.Count);
        Assert.Equal(clash[0].Tick, clash[1].Tick);
        Assert.Contains(clash, c => c.Attacker == a && c.Target == b && c.Met == ContactMet.Weapon);
        Assert.Contains(clash, c => c.Attacker == b && c.Target == a && c.Met == ContactMet.Weapon);
        Assert.DoesNotContain(r.Contacts, c => c.Met == ContactMet.Body);     // no harm: neither blade reaches a body
        Assert.Equal(2, r.Follow.Count(f => f.Outcome == BladeOutcome.Rebounds));
        // The same whichever body the stores hold first: the same contacts (by who struck whom) on the same tick.
        Assert.Equal(r.Contacts.Select(c => (c.Tick, c.Attacker == a, c.Met, c.Guard)).OrderBy(x => x.Item2),
                     s.Contacts.Select(c => (c.Tick, c.Attacker == sa, c.Met, c.Guard)).OrderBy(x => x.Item2));
        // The guard off: no clash (the blades pass by each other).
        var off = Make(guard: g => g.Enabled = false);
        var oa = off.Put("Christof", 0, 0, 90);
        var ob = off.Put("Christof", 0, 1.5f, -90);
        off.Step(30);
        off.Fighter(oa).NextVariant = off.Fighter(ob).NextVariant = 1;
        off.Intent(oa).Attack = off.Intent(ob).Attack = true;
        off.Step();
        off.Intent(oa).Attack = off.Intent(ob).Attack = false;
        off.Step(Ticks(1.5f));
        Assert.DoesNotContain(off.Contacts, c => c.Met == ContactMet.Weapon);
    }

    [Fact]
    public void A_clash_one_blade_dominates_goes_on_and_the_other_stops()
    {
        var (r, a, b) = Clashing(swap: false, rules: true, verdict: c => c.Attacker.Index < c.Target.Index ? GuardVerdict.ClashDominates : GuardVerdict.Clashed);
        var low = a.Index < b.Index ? a : b;
        Assert.Contains(r.Follow, f => f.Attacker == low && f.Outcome == BladeOutcome.GoesOn);
        Assert.Contains(r.Follow, f => f.Attacker != low && f.Outcome == BladeOutcome.Rebounds);
        Assert.DoesNotContain(r.Contacts, c => c.Attacker != low && c.Met == ContactMet.Body);
    }

    // ---- the duelling brain's parry ----

    /// <summary>A Christof who swings on cue at a second Christof 1.3 m off whose brain stands its ground (never strikes, closes or backs off) with <paramref name="numbers"/>.</summary>
    (Ring R, EntityId A, EntityId D) Braced(DuelBrainSettings numbers)
    {
        var r = Make(brains: true);
        var a = r.Put("Christof", 0, 0, 90);
        var d = r.Put("Christof", 0, 1.3f, -90);
        var book = r.World.Service<DuelBrainBook>();
        float reach = DuelBrains.ReachOf(r.Assets, r.Kinds["Christof"]);
        r.World.Set(d, DuelBrain.For(BrainKind.Duel, a, book.Add(numbers with { StrikeShare = 0.1f, CloseShare = 3f, TooCloseShare = 0f, BackOffChance = 0f }), reach, reach));
        r.Step(30);
        return (r, a, d);
    }

    [Fact]
    public void The_brain_parries_by_its_chance_timed_to_the_blow_and_a_feint_draws_its_answer_as_a_blow_does()
    {
        var (r, a, d) = Braced(new DuelBrainSettings { ParryChance = 1f, BlockChance = 0f });
        r.Swing(a, 15f);
        r.Step(Ticks(1.2f));
        var p = Assert.Single(r.Parries);
        Assert.Equal(d, p.Entity);
        float before = (r.Contacts.Count > 0 ? r.Contacts[0].Tick - p.Tick : 0) * World.TickSeconds;
        output.WriteLine($"parry pressed {(p.Tick - r.Begun[0].Tick) * World.TickSeconds:0.000} s after the blow began; met: {string.Join("; ", r.Contacts.Select(c => $"{c.Met} {c.Guard} {c.Site} {(c.Tick - p.Tick) * World.TickSeconds:0.000} s after the press"))}");
        Assert.Equal(1, r.World.Get<DuelBrain>(d).Parries);
        Assert.InRange((p.Tick - r.Begun[0].Tick) * World.TickSeconds, 0.2f, 0.5f);       // pressed late in the wind-up, not at once
        Assert.Contains(r.Contacts, c => c.Guard == GuardMeet.Parry);                      // met in the window
        // A feint draws a blocking brain's block as a blow does (it answers a begun blow).
        var (f, fa, fd) = Braced(new DuelBrainSettings { BlockChance = 1f });
        f.Swing(fa);
        f.Step(Ticks(0.1f));
        Assert.True(f.Cancel(fa, SwingCancelCause.Cancel));
        f.Step(Ticks(0.5f));
        Assert.Single(f.Raised);
        Assert.Equal(1, f.World.Get<DuelBrain>(fd).Blocks);
        // A parrying brain gives its parry up when the blow it answered is cancelled before its strike.
        var (g, ga, gd) = Braced(new DuelBrainSettings { ParryChance = 1f, BlockChance = 0f });
        g.Swing(ga);
        g.Step(Ticks(0.1f));
        g.Cancel(ga, SwingCancelCause.Cancel);
        g.Step(Ticks(0.8f));
        Assert.Empty(g.Parries);
        // The default brain draws nothing for a parry: today's brain.
        var (h, ha, _) = Braced(new DuelBrainSettings());
        h.Swing(ha);
        h.Step(Ticks(1.2f));
        Assert.Equal(0, h.World.Random.For("combat.duel-brain.parry").Draws);
        Assert.Empty(h.Parries);
    }

    // ---- a load mid-parry, allocation ----

    [Fact]
    public void A_load_mid_parry_goes_on_the_same_and_a_guarded_bout_allocates_nothing_per_tick_once_going()
    {
        Ring Bout() => Make(rules: true);
        void Act(Ring r, EntityId a, EntityId d, int i)
        {
            int at = i % 120;
            r.Intent(a).Aims = true;
            r.Intent(a).AimPitch = (i / 120 % 2 == 0 ? 15f : 0f) * DetMath.DegToRad;
            r.Intent(a).Attack = at == 2;
            r.Intent(d).Block = at >= 30 && at < 90;
        }
        var x = Bout();
        var xa = x.Put("Christof", 0, 0, 90);
        var xd = x.Put("Christof", 0, 1.3f, -90);
        int parryAt = -1;
        for (int i = 0; i < 600; i++)
        {
            Act(x, xa, xd, i);
            x.Step();
            if (parryAt < 0 && x.Fighter(xd).ParryLeft > 0.1f && i > 200) parryAt = i;
        }
        Assert.True(parryAt > 0);
        var src = Bout(); var sa = src.Put("Christof", 0, 0, 90); var sd = src.Put("Christof", 0, 1.3f, -90);
        for (int i = 0; i <= parryAt; i++) { Act(src, sa, sd, i); src.Step(); }
        var dst = Bout(); dst.Put("Christof", 0, 0, 90); dst.Put("Christof", 0, 1.3f, -90);
        dst.Step(parryAt + 1);
        Copy(src.World, dst.World);
        Assert.Equal(src.World.Digest(), dst.World.Digest());
        for (int i = parryAt + 1; i < parryAt + 240; i++)
        {
            Act(src, sa, sd, i); Act(dst, sa, sd, i);
            src.Step(); dst.Step();
            Assert.Equal(src.World.Digest(), dst.World.Digest());
        }
        // Allocation: a guarded bout run on, then measured (the event lists the harness keeps are cleared first).
        for (int i = 600; i < 1200; i++) { Act(x, xa, xd, i); MovementServices.Step(x.World, ActionFrame.Idle); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 1200; i < 1440; i++) { Act(x, xa, xd, i); MovementServices.Step(x.World, ActionFrame.Idle); }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"{bytes} bytes over 240 ticks of a guarded bout");
        Assert.True(bytes < 2048, $"{bytes} bytes allocated");
    }

    static void Copy(World from, World to)
    {
        var method = typeof(GuardTests).GetMethod(nameof(CopyStore), BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var s in from.Stores)
        {
            if (!s.Digested) continue;
            var other = to.Stores.First(o => o.ComponentType == s.ComponentType);
            method.MakeGenericMethod(s.ComponentType).Invoke(null, [s, other]);
        }
    }

    static void CopyStore<T>(Store<T> from, Store<T> to) where T : struct
    {
        var ents = to.Entities.ToArray();
        foreach (var e in ents) if (!from.Has(e)) to.Remove(e);
        var src = from.Entities.ToArray();
        for (int i = 0; i < src.Length; i++) to.Set(src[i], from.Get(src[i]));
    }
}
