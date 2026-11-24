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
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The blocked blade's rebound (<i>"a kind of 'swing and weapon bounces off' animation kind of how it
/// looks in Bannerlord when a swing is blocked"</i>): the tip's travel back from the contact's impulse between the floor and the
/// ceiling, a parried blade's further, a wall's by its material; the arm's recoil (an additive layer moving the weapon hand out of
/// what was met, back to zero by a critically damped spring); the attacker's stun (an attack refused and buffered, a guard and a
/// dodge allowed, the buffered blow beginning when the stun and the busy time are over); the earlier build's rebound with the rule's switch;
/// the same digests twice; nothing allocated per tick. Scenes skip without the 1.1 install (VTMR_GAME_DIR). in the
/// allocation tests' collection and measured as they are (<see cref="Allocations"/>: the queues warmed, the window measured more than
/// once) — the single measure failed once in the earlier build's full run and passed alone and on the rerun.
/// </summary>
[Collection(AllocationCollection.Name)]
public sealed class ReboundTests(ITestOutputHelper output)
{
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

    static int Ticks(float seconds) => (int)MathF.Round(seconds * World.TicksPerSecond);

    /// <summary>A stand-in for the rules' guard verdict: every guard contact gets <see cref="Verdict"/> at Resolve 95 (the enhanced module's place).</summary>
    sealed class StandInGuard(GuardVerdict verdict) : ISystem
    {
        public GuardVerdict Verdict { get; } = verdict;

        public void Run(World world)
        {
            var made = world.Events<ContactMade>().ThisTick;
            for (int i = 0; i < made.Count; i++)
            {
                var c = made[i];
                if (c.Guard == GuardMeet.None) continue;
                world.Events<GuardResolved>().Publish(new GuardResolved(c.ContactId, c.Attacker, c.Target, c.Tick, c.Met, c.Guard, Verdict, 1f, 1f, 1f, 0f, 1f, 0.5, 0.5,
                    1f, 1f, Verdict == GuardVerdict.Parried ? 0.3f : 0f, 0f));
            }
        }
    }

    sealed class Ring
    {
        public required World World { get; init; }
        public required Dictionary<string, int> Kinds { get; init; }
        public CharacterAssets Assets => World.Service<CharacterAssets>();
        public readonly List<ContactMade> Contacts = [];
        public readonly List<SwingFollowThrough> Follow = [];
        public readonly List<AttackBegun> Begun = [];
        public readonly List<AttackRefused> Refused = [];
        public readonly List<AttackerStunned> Stunned = [];
        public readonly List<BlockRaised> Raised = [];

        public void Step(int ticks = 1)
        {
            for (int i = 0; i < ticks; i++)
            {
                MovementServices.Step(World, ActionFrame.Idle);
                Contacts.AddRange(World.Events<ContactMade>().ThisTick);
                Follow.AddRange(World.Events<SwingFollowThrough>().ThisTick);
                Begun.AddRange(World.Events<AttackBegun>().ThisTick);
                Refused.AddRange(World.Events<AttackRefused>().ThisTick);
                Stunned.AddRange(World.Events<AttackerStunned>().ThisTick);
                Raised.AddRange(World.Events<BlockRaised>().ThisTick);
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

        /// <summary>A tap of attack aimed <paramref name="aimDegrees"/> (the blow begins as it is let go, the next tick).</summary>
        public void Swing(EntityId who, float aimDegrees = 0f)
        {
            ref var intent = ref World.Get<CombatIntent>(who);
            intent.Aims = true;
            intent.AimPitch = aimDegrees * DetMath.DegToRad;
            intent.Attack = true;
            Step();
            World.Get<CombatIntent>(who).Attack = false;
        }
    }

    static readonly string[] AllKinds = ["Christof", "rudolfGuard", "Civ12male"];

    static Ring Make(LevelSpace? level = null, Action<ContactConfig>? tune = null, GuardVerdict? verdict = null, bool dodge = false, bool brains = false)
    {
        var assets = new CharacterAssets();
        var kinds = new Dictionary<string, int>();
        foreach (var k in AllKinds) kinds[k] = OneOneLibrary.Register(assets, Character(k, k == "Christof" ? "BroadSword" : null));
        level ??= new TestLevel().Floor(-60, -60, 60, 60).Build();
        var b = new WorldBuilder(7);
        MovementServices.Register(b, level);
        MasqueradeServices.Register(b, new MasqueradeSettings { AutoSwitch = false });
        b.Animation(assets);
        b.HitVolumes();
        BodyMotionSystem.Register(b);
        if (verdict is not null) b.Service(new GuardSettings { RulesDecide = true });
        CombatServices.Register(b);
        if (brains) DuelBrains.Register(b);
        var config = new ContactConfig();
        tune?.Invoke(config);
        b.Event<HarmResolved>();
        b.Service(new HurtRule { OnContact = false });
        ContactServices.Register(b, config);
        if (verdict is { } v) b.System(Phase.Resolve, 95, "test.stand-in-guard", new StandInGuard(v));
        var w = b.Build();
        w.Service<HitVolumes>().Prepare(assets);
        ContactServices.Prepare(w);
        return new Ring { World = w, Kinds = kinds };
    }

    /// <summary>Christof's quick forehand at rudolfGuard 1.3 m off holding his shield up (or, with <paramref name="wall"/>, at a wall 0.6 m off).</summary>
    static (Ring R, EntityId A, EntityId T) Shield(Action<ContactConfig>? tune = null, GuardVerdict? verdict = null, bool wall = false, bool dodge = false)
    {
        var level = wall ? new TestLevel().Floor(-60, -60, 60, 60).Box(new(-1.5f, 0.6f, 0f), new(1.5f, 0.75f, 2.5f)).Build() : null;
        var r = Make(level, tune, verdict, dodge);
        var a = r.Put("Christof", 0, 0, 90);
        var t = r.Put(wall ? "Civ12male" : "rudolfGuard", wall ? 4f : 0f, wall ? 4f : 1.3f, -90);
        if (!wall) r.World.Get<CombatIntent>(t).Block = true;
        r.Step(30);
        r.Swing(a, wall ? 15f : 0f);
        return (r, a, t);
    }

    // ---- the distance ----

    [Fact]
    public void The_rebound_comes_from_the_contacts_impulse_between_the_floor_and_the_ceiling_and_a_wall_by_its_material()
    {
        var rule = new BladeStopRule();
        ContactMade At(float impulse, ContactMet met = ContactMet.Shield, ContactMaterial m = ContactMaterial.Wood) =>
            new(1, default, 1, default, met, null, m, 0, default, Vector3.UnitY, 10f, impulse, false, true, true, false, TipSpeed: 40f);
        Assert.Equal(0.15f, FollowThrough.ReboundDistance(rule, At(0f)), 4);       // the floor (the earlier build's whole rebound)
        Assert.Equal(0.15f, FollowThrough.ReboundDistance(rule, At(5f)), 4);
        Assert.Equal(0.342f, FollowThrough.ReboundDistance(rule, At(28.5f)), 3);   // the survey's shield: 28.5 N·s
        Assert.Equal(0.5f, FollowThrough.ReboundDistance(rule, At(100f)), 4);      // the ceiling
        Assert.Equal(0.5f, FollowThrough.ReboundDistance(rule, At(47.3f, ContactMet.Level, ContactMaterial.Stone)), 4);   // the survey's stone wall
        Assert.Equal(0.2f, FollowThrough.ReboundDistance(rule, At(47.3f, ContactMet.Level, ContactMaterial.Earth)), 4);   // earth gives
        // A shield of wood is a guard, not the level: its material does not scale it.
        Assert.Equal(FollowThrough.ReboundDistance(rule, At(28.5f, ContactMet.Shield, ContactMaterial.Wood)), FollowThrough.ReboundDistance(rule, At(28.5f, ContactMet.Shield, ContactMaterial.Stone)));
        // The sizes: halved, nearly doubled; the earlier build's rule takes no distance from the impulse at all.
        Assert.Equal(0.171f, FollowThrough.ReboundDistance(rule.Sized(0.5f), At(28.5f)), 3);
        Assert.Equal(0.616f, FollowThrough.ReboundDistance(rule.Sized(1.8f), At(28.5f)), 3);
        Assert.False(rule.FixedRebound().ReboundByImpulse);
    }

    [Fact]
    public void A_blade_on_a_shield_springs_back_as_far_as_its_impulse_asks_and_the_fixed_rule_springs_it_back_0_15_m()
    {
        foreach (bool old in new[] { false, true })
        {
            // The recoil off here: the clip's own travel back is measured (the recoil is measured below).
            var (r, a, _) = Shield(c => c.Stop = old ? c.Stop.FixedRebound() : c.Stop with { RecoilMostMetres = 0f });
            var tips = new List<Vector3>();
            for (int i = 0; i < Ticks(1.2f); i++)
            {
                r.Step();
                if (r.Contacts.Count > 0 && tips.Count < 40 && HeldItems.Blade(r.World, a, out var bl)) tips.Add(bl.Tip);
            }
            var c = Assert.Single(r.Contacts);
            Assert.Equal(ContactMet.Shield, c.Met);
            var f = Assert.Single(r.Follow);
            Assert.Equal(BladeOutcome.Rebounds, f.Outcome);
            int span = old ? Ticks(0.1f) : Ticks(0.12f);
            float sprang = Vector3.Distance(tips[0], tips[span]);
            float asked = old ? 0.15f : FollowThrough.ReboundDistance(new BladeStopRule(), in c);
            output.WriteLine($"{(old ? "fixed" : "scaled")}: impulse {c.NormalImpulse:0.0} N·s, tip speed {c.TipSpeed:0.0} m/s: asked {asked:0.000} m (event {f.ReboundMetres:0.000}), the tip {sprang:0.000} m from the contact after {span} ticks");
            Assert.Equal(asked, f.ReboundMetres, 4);
            // The clip's speed back is taken from the tip's speed at the meeting: the travel is the asked distance within a third.
            Assert.InRange(sprang, asked * 0.6f, asked * 1.35f);
            if (old) Assert.Empty(r.Stunned);
            else Assert.Single(r.Stunned);
        }
    }

    [Fact]
    public void A_parried_blade_springs_back_twice_as_far_recoils_twice_as_far_and_is_stunned_longer()
    {
        var held = Shield(verdict: GuardVerdict.Held);
        var parried = Shield(verdict: GuardVerdict.Parried);
        float most = 0f, mostParried = 0f;
        for (int i = 0; i < Ticks(1.2f); i++)
        {
            held.R.Step(); parried.R.Step();
            most = MathF.Max(most, held.R.World.Get<CharacterAnimation>(held.A).RecoilBack.Length());
            mostParried = MathF.Max(mostParried, parried.R.World.Get<CharacterAnimation>(parried.A).RecoilBack.Length());
        }
        var fh = Assert.Single(held.R.Follow);
        var fp = Assert.Single(parried.R.Follow);
        var sh = Assert.Single(held.R.Stunned);
        var sp = Assert.Single(parried.R.Stunned);
        output.WriteLine($"held: {fh.ReboundMetres:0.000} m, recoil {most * 100:0.0} cm, stun {sh.Seconds:0.00} s; parried: {fp.ReboundMetres:0.000} m, recoil {mostParried * 100:0.0} cm, stun {sp.Seconds:0.00} s");
        var g = new GuardSettings();
        Assert.Equal(fh.ReboundMetres * g.ParryReboundScale, fp.ReboundMetres, 3);
        Assert.InRange(mostParried / most, g.ParryReboundScale * 0.97f, g.ParryReboundScale * 1.03f);
        Assert.False(sh.Parried);
        Assert.True(sp.Parried);
        var rule = new BladeStopRule();
        Assert.Equal(rule.StunHeldSeconds, sh.Seconds);
        Assert.Equal(rule.StunParriedSeconds, sp.Seconds);
    }

    // ---- the recoil ----

    [Fact]
    public void The_recoils_spring_kicks_to_its_most_and_returns_to_zero_critically_damped()
    {
        var rule = new BladeStopRule();
        float peak = rule.RecoilPeakSeconds, settle = rule.RecoilSettleSeconds;
        Assert.Equal(0f, Recoil.Share(0f, peak, settle));
        Assert.Equal(1f, Recoil.Share(peak, peak, settle), 5);
        float last = 1f;
        for (float t = peak + 0.01f; t < settle; t += 0.01f)
        {
            float s = Recoil.Share(t, peak, settle);
            Assert.True(s < last, $"at {t:0.00} s the recoil {s:0.000} did not fall from {last:0.000}");   // no overshoot: critically damped
            Assert.True(s > 0f);
            last = s;
        }
        Assert.True(last < 0.02f, $"{last:0.000} of its most left as it is let go");
        Assert.Equal(0f, Recoil.Share(settle, peak, settle));
    }

    [Fact]
    public void A_blade_on_a_shield_kicks_the_weapon_hand_back_out_of_the_shield_and_the_recoil_settles_to_nothing()
    {
        var (r, a, _) = Shield();
        var hands = new List<(long Tick, Vector3 Back, float Length)>();
        var kind = r.Assets.Kind(r.World.Get<CharacterAnimation>(a).Kind);
        for (int i = 0; i < Ticks(1.2f); i++)
        {
            r.Step();
            var an = r.World.Get<CharacterAnimation>(a);
            if (r.Contacts.Count > 0) hands.Add((r.World.Tick, an.RecoilBack, an.RecoilBack.Length()));
        }
        var c = Assert.Single(r.Contacts);
        var rule = new BladeStopRule();
        float want = MathF.Min(c.NormalImpulse * rule.RecoilMetresPerImpulse, rule.RecoilMostMetres);
        float most = hands.Max(h => h.Length);
        var at = hands.First(h => h.Length == most);
        output.WriteLine($"impulse {c.NormalImpulse:0.0} N·s: the hand pushed back at most {most * 100:0.0} cm (want {want * 100:0.0}) {(at.Tick - c.Tick) * World.TickSeconds:0.000} s after the meeting; normal {c.Normal}");
        Assert.InRange(most, want * 0.98f, want * 1.001f);
        Assert.InRange((at.Tick - c.Tick) * World.TickSeconds, rule.RecoilPeakSeconds - 0.01f, rule.RecoilPeakSeconds + 0.01f);
        // Out of the shield: along the contact's normal (in the body's frame its back half: the guard stands before it).
        Assert.True(at.Back.Y < 0f, $"the push {at.Back} goes back from the facing (+Y)");
        Assert.Equal(Vector3.Zero, hands[^1].Back);
        Assert.Equal(0f, r.World.Get<CharacterAnimation>(a).RecoilPeak);
    }

    [Fact]
    public void The_recoil_layer_puts_the_weapon_hand_where_it_asks_and_leaves_the_other_arm()
    {
        var r = Make();
        var a = r.Put("Christof", 0, 0, 90);
        r.Swing(a);
        r.Step(Ticks(0.45f));
        var state = r.World.Get<CharacterAnimation>(a);
        var kind = r.Assets.Kind(state.Kind);
        var skeleton = r.Assets.ModelOf(state.Kind).Skeleton;
        var limbs = kind.Limbs!;
        var poser = new PoseEvaluator();
        int n = skeleton.Count;
        var m0 = new Matrix4x4[n]; var s0 = new Matrix4x4[n];
        var m1 = new Matrix4x4[n]; var s1 = new Matrix4x4[n];
        poser.Evaluate(r.Assets, state, default(PoseRig), m0, s0);
        var push = new Vector3(0.02f, -0.08f, 0.01f);
        var kicked = state with { RecoilBack = push, RecoilLean = 0f, RecoilWrist = 0f };
        poser.Evaluate(r.Assets, kicked, default(PoseRig), m1, s1);
        float scale = kind.Scale > 0f ? kind.Scale : 1f;
        var moved = (m1[limbs.RightArm.End].Translation - m0[limbs.RightArm.End].Translation) * scale;
        var other = (m1[limbs.LeftArm.End].Translation - m0[limbs.LeftArm.End].Translation) * scale;
        output.WriteLine($"the weapon hand moved {moved} for {push}; the other hand {other.Length() * 1000:0.0} mm");
        Assert.True(Vector3.Distance(moved, push) < 0.005f);
        Assert.True(other.Length() < 1e-4f);
        // With the lean and the wrist the hand still goes back and the chest leans with it.
        var leaned = state with { RecoilBack = push, RecoilLean = 0.1f, RecoilWrist = 0.2f };
        poser.Evaluate(r.Assets, leaned, default(PoseRig), m1, s1);
        Assert.True((m1[limbs.RightArm.End].Translation - m0[limbs.RightArm.End].Translation).Y < -0.04f / scale);
    }

    // ---- the stun ----

    [Fact]
    public void The_stun_refuses_an_attack_but_buffers_it_and_lets_a_guard_up_after_the_rebound()
    {
        // A lone broadsword (Christof) on the shield: stunned 0.25 s from the meeting; attack pressed in it is refused (said)
        // and buffered; block pressed after the rebound raises his guard though his busy time runs on.
        var (r, a, _) = Shield();
        while (r.Contacts.Count == 0) r.Step();
        long met = r.Contacts[0].Tick;
        Assert.True(r.World.Get<Combatant>(a).Stun > 0f);
        r.Step(Ticks(0.05f));
        r.Swing(a);   // pressed in the stun
        r.Step();     // let go: the blow waits
        var refused = Assert.Single(r.Refused);
        Assert.Equal(AttackRefusal.Stunned, refused.Why);
        Assert.True(r.World.Get<Combatant>(a).Pending != AttackStrength.None);
        // The guard: block held from just after the rebound (0.12 s).
        r.Step(Ticks(0.1f));
        r.World.Get<CombatIntent>(a).Block = true;
        r.Step(2);
        var c = r.World.Get<Combatant>(a);
        output.WriteLine($"stun left {c.Stun:0.000} s, busy {c.Busy:0.000} s, blocking {c.Blocking}, raised {(r.Raised.Any(x => x.Entity == a) ? (r.Raised.First(x => x.Entity == a).Tick - met) * World.TickSeconds : -1f):0.000} s after the meeting");
        Assert.True(c.Busy > 0f);
        // The buffered blow keeps the guard down (a blow waits); without one, the guard comes up.
        var (r2, a2, _) = Shield();
        while (r2.Contacts.Count == 0) r2.Step();
        long met2 = r2.Contacts[0].Tick;
        r2.Step(Ticks(0.15f));
        r2.World.Get<CombatIntent>(a2).Block = true;
        r2.Step(2);
        var c2 = r2.World.Get<Combatant>(a2);
        output.WriteLine($"no blow buffered: blocking {c2.Blocking}, raised {(r2.Raised.Any(x => x.Entity == a2) ? (r2.Raised.First(x => x.Entity == a2).Tick - met2) * World.TickSeconds : -1f):0.000} s after the meeting, busy {c2.Busy:0.000} s left");
        Assert.True(c2.Blocking);
        Assert.True(c2.Busy > 0.2f);
        // The earlier build's rule: the guard waits for the busy time.
        var (r3, a3, _) = Shield(c3 => c3.Stop = c3.Stop.FixedRebound());
        while (r3.Contacts.Count == 0) r3.Step();
        r3.Step(Ticks(0.15f));
        r3.World.Get<CombatIntent>(a3).Block = true;
        r3.Step(2);
        Assert.False(r3.World.Get<Combatant>(a3).Blocking);
        Assert.Empty(r3.Stunned);
    }

    [Fact]
    public void A_blow_pressed_in_a_stun_longer_than_the_swing_begins_when_the_stun_ends()
    {
        foreach (var (stun, after) in new[] { (0.25f, false), (1.5f, false), (0.25f, true) })
        {
            var (r, a, _) = Shield(c => c.Stop = c.Stop with { StunHeldSeconds = stun, StunFromSwingEnd = after });
            while (r.Contacts.Count == 0) r.Step();
            long begun = r.Begun[0].Tick;
            long met = r.Contacts[0].Tick;
            r.Step(Ticks(0.1f));
            r.Swing(a);
            r.Step(Ticks(2f));
            Assert.True(r.Begun.Count >= 2);
            float next = (r.Begun[1].Tick - met) * World.TickSeconds, sinceFirst = (r.Begun[1].Tick - begun) * World.TickSeconds;
            output.WriteLine($"stun {stun:0.00} s{(after ? " from the swing's end" : "")}: refused {string.Join(",", r.Refused.Select(x => x.Why))}; the next blow {next:0.000} s after the meeting, {sinceFirst:0.000} s after the first began");
            Assert.Contains(r.Refused, x => x.Why == AttackRefusal.Stunned);
            if (after) Assert.InRange(sinceFirst, 1.1f + stun - 1.5f / World.TicksPerSecond, 1.1f + stun + 1.5f / World.TicksPerSecond);   // on top of the swing
            else if (stun < 1f) Assert.InRange(sinceFirst, 1.1f - 1.5f / World.TicksPerSecond, 1.1f + 1.5f / World.TicksPerSecond);   // the busy time rules
            else Assert.InRange(next, stun - 1.5f / World.TicksPerSecond, stun + 1.5f / World.TicksPerSecond);                  // the stun rules
        }
    }

    [Fact]
    public void A_dodge_is_allowed_in_the_stun()
    {
        var (r, a, _) = Shield(dodge: true);
        while (r.Contacts.Count == 0) r.Step();
        r.Step(Ticks(0.05f));
        Assert.True(r.World.Get<Combatant>(a).Stun > 0f);
        r.World.Set(a, new DodgeIntent { Pressed = true, Way = new Vector2(-1f, 0f) });
        var refusedDodges = new List<DodgeRefused>();
        var begunDodges = new List<DodgeBegun>();
        for (int i = 0; i < 3; i++)
        {
            r.Step();
            refusedDodges.AddRange(r.World.Events<DodgeRefused>().ThisTick);
            begunDodges.AddRange(r.World.Events<DodgeBegun>().ThisTick);
            ref var di = ref r.World.Get<DodgeIntent>(a);
            di.Pressed = false;
        }
        output.WriteLine($"dodges begun {begunDodges.Count}, refused {string.Join(",", refusedDodges.Select(d => d.Why))}");
        Assert.Single(begunDodges);
    }

    [Fact]
    public void The_duelling_brain_waits_out_the_stun()
    {
        foreach (float stun in new[] { 0f, 1.0f })
        {
            var r = Make(brains: true);
            var a = r.Put("Christof", 0, 0, 90);
            var t = r.Put("rudolfGuard", 0, 1.3f, -90);
            var book = r.World.Service<DuelBrainBook>();
            float ra = DuelBrains.ReachOf(r.Assets, r.Kinds["Christof"]), rb = DuelBrains.ReachOf(r.Assets, r.Kinds["rudolfGuard"]);
            r.World.Set(a, DuelBrain.For(BrainKind.Duel, t, book.Add(new DuelBrainSettings { BlockChance = 0f, BackOffChance = 0f }), ra, rb));
            r.World.Set(t, DuelBrain.For(BrainKind.Block, a, book.Add(new DuelBrainSettings()), rb, ra));
            r.World.Get<Combatant>(a).Stun = stun;
            r.Step(Ticks(1.6f));
            var first = r.Begun.Where(x => x.Entity == a).Select(x => x.Tick * World.TickSeconds).DefaultIfEmpty(-1f).First();
            output.WriteLine($"stunned {stun:0.0} s: the brain's first blow at {first:0.000} s; refused {r.Refused.Count(x => x.Entity == a)}");
            Assert.True(first > 0f);
            if (stun > 0f) Assert.True(first >= stun, $"the brain swung at {first:0.000} s, inside its {stun:0.0} s stun");
            else Assert.True(first < 0.5f);
            Assert.DoesNotContain(r.Refused, x => x.Entity == a);   // it never pressed into the stun
        }
    }

    // ---- determinism, allocation ----

    [Fact]
    public void Rebounds_recoils_and_stuns_give_the_same_digests_twice_and_allocate_nothing_once_going()
    {
        Ring Run(out EntityId a)
        {
            var (r, x, _) = Shield();
            a = x;
            return r;
        }
        var p = Run(out var a1);
        var q = Run(out var a2);
        for (int i = 0; i < 900; i++)
        {
            if (i % 140 == 0) { p.Swing(a1, (i / 140 % 3 - 1) * 12f); q.Swing(a2, (i / 140 % 3 - 1) * 12f); }
            else { p.Step(); q.Step(); }
            Assert.Equal(p.World.Digest(), q.World.Digest());
        }
        output.WriteLine($"{p.Follow.Count} follow-throughs, {p.Stunned.Count} stuns, {p.Refused.Count} refused");
        Assert.True(p.Stunned.Count >= 3);
        // Measured as the platform's allocation tests measure (the queues warmed; each window of 900 ticks the
        // same world's next, up to Allocations.Measures of them; failing only when every one allocated).
        Allocations.Warm(p.World);
        Allocations.None("900 ticks of rebounds, recoils and stuns", () =>
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            int stuns = 0;
            for (int i = 0; i < 900; i++)
            {
                if (i % 140 == 0)
                {
                    ref var intent = ref p.World.Get<CombatIntent>(a1);
                    intent.Attack = true;
                }
                else if (i % 140 == 1) p.World.Get<CombatIntent>(a1).Attack = false;
                MovementServices.Step(p.World, ActionFrame.Idle);
                stuns += p.World.Events<AttackerStunned>().ThisTick.Count;
            }
            long spent = GC.GetAllocatedBytesForCurrentThread() - before;
            output.WriteLine($"{spent} bytes over 900 ticks ({stuns} stuns)");
            Assert.True(stuns >= 3);
            return spent;
        });
    }
}
