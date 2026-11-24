// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Formats.L2;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The masquerade on small levels
/// built in memory: places read from a manifest's areas (an unmarked place public, the smallest area deciding,
/// a whole level free), the numbers a body is held to, the key and the places switching the mode (both ways, the
/// key toggling in between), the risks reported once per act and never in a free place, in the masquerade or by
/// a human body, the same digests from the same inputs, and a tape carrying the key.
/// </summary>
public sealed class MasqueradeTests
{
    /// <summary>A human by the human's numbers (SANDBOX), as the enhanced module's file says it.</summary>
    static readonly Locomotion Human = Locomotion.Sandbox with
    {
        StepHeight = 0.35f, SteepestGroundDegrees = 40f, JumpImpulseBudget = Locomotion.ImpulseFor(0.5f, 9.81f),
        FallTolerance = 8f,
    };

    /// <summary>An elder with Celerity and Potence: a 3 m jump, a 9 m/s sprint, falls of 15 m.</summary>
    static readonly Locomotion Elder = Human with
    {
        RunSpeed = 4.5f, SprintSpeed = 9f, AccelerationCapacity = 60f, BrakingDeceleration = 60f,
        JumpImpulseBudget = Locomotion.ImpulseFor(3f, 9.81f), AirControl = 0.5f, FallTolerance = 17f, StandingReach = 2.5f,
        SprintVaultMax = 2f, VaultMax = 3f, MantleMax = 4f, MantleSeconds = 0.5f, MantleMinDepth = 0.3f,
    };

    static LevelSpace Floor(LevelPlaces? places = null, Action<TestLevel>? more = null)
    {
        var t = new TestLevel().Floor(-20, -20, 60, 20);
        more?.Invoke(t);
        var built = t.Build("floor");
        return new LevelSpace("floor", built.Collision) { Places = places ?? LevelPlaces.None };
    }

    /// <summary>A free area over x 10 to 30 (the rest public).</summary>
    static LevelPlaces FreeStrip() => LevelPlaces.Of([new LevelPlace("ruin", new TurnedBox(new(10, -20, -2), 0f, new(20, 40, 10)), PlaceKind.Free)]);

    sealed record Rig(World W, EntityId Player, int HumanIndex, int ElderIndex);

    static Rig Make(LevelSpace level, bool elder = true, bool auto = true, ulong seed = 1, Vector3? at = null)
    {
        var b = new WorldBuilder(seed);
        MovementServices.Register(b, level);
        MasqueradeServices.Register(b, new MasqueradeSettings { AutoSwitch = auto });
        var w = b.Build();
        var profiles = w.Service<MovementProfiles>();
        int h = profiles.Add(new MovementProfile(AssetId.Of("test", "movement/human"), "a human", Human, true), isHuman: true);
        int e = profiles.Add(new MovementProfile(AssetId.Of("test", "movement/elder"), "an elder", Elder, true));
        var p = MovementServices.SpawnControlled(w, at ?? new(0, 0, 0.01f), 0f, Locomotion.Sandbox);
        profiles.Give(w, p, elder ? e : h);
        return new Rig(w, p, h, e);
    }

    static readonly ActionFrame Forward = new(new Vector2(0, 1), default, Buttons.None, default);

    /// <summary>Steps <paramref name="ticks"/> ticks of <paramref name="frame"/>, collecting what the masquerade raised.</summary>
    static void Run(Rig r, int ticks, ActionFrame frame, List<MasqueradeModeChanged>? changes = null, List<MasqueradeRisk>? risks = null)
    {
        for (int i = 0; i < ticks; i++)
        {
            MovementServices.Step(r.W, frame);
            changes?.AddRange(r.W.Events<MasqueradeModeChanged>().ThisTick);
            risks?.AddRange(r.W.Events<MasqueradeRisk>().ThisTick);
        }
    }

    /// <summary>One tick with the masquerade key pressed (and <paramref name="also"/> held), then one with it let go.</summary>
    static void PressKey(Rig r, ActionFrame also, List<MasqueradeModeChanged>? changes = null, List<MasqueradeRisk>? risks = null)
    {
        Run(r, 1, also with { Held = also.Held | Buttons.Masquerade }, changes, risks);
        Run(r, 1, also, changes, risks);
    }

    MasqueradeState State(Rig r) => r.W.Get<MasqueradeState>(r.Player);

    // ---- places ----

    const string Manifest = """
        { "format": "vitomir-l2", "version": 2, "name": "t", "bounds": { "min": [-100, -100, -10], "max": [100, 100, 50] }, "cells": [],
          "areas": [
            { "name": "district", "kind": "zone", "box": { "origin": [-50, -50, -5], "rotZ": 0, "size": [100, 100, 40] }, "masquerade": "free" },
            { "name": "square", "kind": "zone", "box": { "origin": [0, 0, -5], "rotZ": 0.5, "size": [10, 10, 20] }, "masquerade": "public" },
            { "name": "smithy", "kind": "interior", "box": { "origin": [60, 60, 0], "rotZ": 0, "size": [8, 6, 5] } },
            { "name": "odd", "kind": "zone", "box": { "origin": [70, 0, 0], "rotZ": 0, "size": [5, 5, 5] }, "masquerade": "Free" } ] }
        """;

    [Fact]
    public void Areas_marked_public_or_free_are_read_and_a_place_not_marked_is_public()
    {
        var places = LevelPlaces.From(L2Manifest.Read(Manifest));
        Assert.Equal(["square", "district"], places.Places.Select(p => p.Name));       // the smallest first
        Assert.Null(places.Whole);
        Assert.Equal(PlaceKind.Free, places.At(new(-20, -20, 0)));                     // in the district
        Assert.Equal(PlaceKind.Public, places.At(places.Places[0].Box.Centre));         // the square inside it: the smaller decides
        Assert.Equal(PlaceKind.Public, places.At(new(62, 62, 1)));                      // the smithy says nothing, and nothing holds it
        Assert.Equal(PlaceKind.Public, places.At(new(72, 2, 1)));                       // "Free" is no word: unmarked, said once
        Assert.Equal("square", places.NameAt(places.Places[0].Box.Centre));
        Assert.Single(places.Problems);
        Assert.Contains("area odd", places.Problems[0]);
        Assert.Equal(PlaceKind.Public, LevelPlaces.None.At(Vector3.Zero));
    }

    [Fact]
    public void A_level_marked_free_as_a_whole_is_free_where_no_area_says_otherwise()
    {
        var m = L2Manifest.Read("""
            { "format": "vitomir-l2", "version": 2, "name": "mines", "bounds": { "min": [0, 0, 0], "max": [1, 1, 1] }, "cells": [], "masquerade": "free",
              "areas": [ { "name": "chapel", "kind": "interior", "box": { "origin": [0, 0, 0], "rotZ": 0, "size": [4, 4, 4] }, "masquerade": "public" } ] }
            """);
        var places = LevelPlaces.From(m);
        Assert.Equal(PlaceKind.Free, places.Whole);
        Assert.Equal(PlaceKind.Free, places.At(new(50, 50, 0)));
        Assert.Equal(PlaceKind.Public, places.At(new(2, 2, 1)));
        Assert.Empty(places.Problems);
    }

    // ---- the numbers ----

    [Fact]
    public void Held_to_a_human_a_body_keeps_its_size_and_takes_the_lesser_of_each_capability()
    {
        var child = Elder with { Radius = 0.2f, Height = 1.2f, EyeHeight = 1.05f, CrouchHeight = 0.8f, WalkSpeed = 1.0f };
        var held = Masquerade.HeldToHuman(child, Human);
        Assert.Equal((0.2f, 1.2f, 1.05f, 0.8f), (held.Radius, held.Height, held.EyeHeight, held.CrouchHeight));
        Assert.Equal(1.0f, held.WalkSpeed);                         // weaker than a human: never made stronger
        Assert.Equal(Human.SprintSpeed, held.SprintSpeed);
        Assert.Equal(Human.JumpImpulseBudget, held.JumpImpulseBudget);
        Assert.Equal(Human.FallTolerance, held.FallTolerance);
        Assert.Equal((Human.SprintVaultMax, Human.VaultMax, Human.MantleMax), (held.SprintVaultMax, held.VaultMax, held.MantleMax));
        Assert.Equal(Human.MantleSeconds, held.MantleSeconds);       // no quicker than a human's
        Assert.Equal(0.5f, Masquerade.JumpHeight(Human), 3);
    }

    [Fact]
    public void The_mode_starts_in_the_masquerade_and_the_key_switches_the_numbers_between_ticks()
    {
        var r = Make(Floor(), auto: false);
        var profiles = r.W.Service<MovementProfiles>();
        Assert.Equal(MasqueradeMode.Masquerade, State(r).Mode);
        Assert.Equal(profiles.Numbers(r.ElderIndex, MasqueradeMode.Masquerade), r.W.Get<Locomotion>(r.Player));
        Assert.Equal(Human.SprintSpeed, r.W.Get<Locomotion>(r.Player).SprintSpeed);

        var changes = new List<MasqueradeModeChanged>();
        PressKey(r, ActionFrame.Idle, changes);
        Assert.Equal(MasqueradeMode.Unbound, State(r).Mode);
        Assert.Equal(profiles.Numbers(r.ElderIndex, MasqueradeMode.Unbound), r.W.Get<Locomotion>(r.Player));
        Assert.Equal(Elder.SprintSpeed, r.W.Get<Locomotion>(r.Player).SprintSpeed);
        PressKey(r, ActionFrame.Idle, changes);
        Assert.Equal(MasqueradeMode.Masquerade, State(r).Mode);
        Assert.Equal(Human.SprintSpeed, r.W.Get<Locomotion>(r.Player).SprintSpeed);
        Assert.Equal([MasqueradeMode.Unbound, MasqueradeMode.Masquerade], changes.Select(c => c.Mode));
        Assert.All(changes, c => Assert.Equal(MasqueradeCause.Key, c.Cause));
    }

    [Fact]
    public void An_elder_s_tap_is_a_human_s_half_metre_and_its_full_charge_its_own_three_metres_in_either_mode()
    {
        // A tap is a human-sized jump for every body, everywhere; a hold charges to the body's own jump,
        // the masquerade or not (the powered leap works anywhere, at a risk).
        float Apex(bool unbound, int hold)
        {
            var r = Make(Floor(), auto: false);
            Run(r, 30, ActionFrame.Idle);
            if (unbound) PressKey(r, ActionFrame.Idle);
            float top = 0;
            Run(r, hold, ActionFrame.Idle with { Held = Buttons.Jump });
            for (int i = 0; i < 240; i++)
            {
                Run(r, 1, ActionFrame.Idle);
                top = MathF.Max(top, r.W.Get<Body>(r.Player).Feet.Z);
            }
            return top;
        }
        int full = (int)((BodyMotor.HoldSeconds + Elder.ChargeSeconds) * World.TicksPerSecond) + 12;
        Assert.InRange(Apex(unbound: true, hold: 1), 0.45f, 0.55f);
        Assert.InRange(Apex(unbound: false, hold: 1), 0.45f, 0.55f);
        Assert.InRange(Apex(unbound: true, hold: full), 2.9f, 3.1f);
        Assert.InRange(Apex(unbound: false, hold: full), 2.9f, 3.1f);
    }

    // ---- switching by place ----

    [Fact]
    public void The_mode_switches_by_itself_entering_a_free_place_and_a_public_one_and_the_key_toggles_in_between()
    {
        var r = Make(Floor(FreeStrip()));
        var changes = new List<MasqueradeModeChanged>();
        Assert.Equal((MasqueradeMode.Masquerade, PlaceKind.Public), (State(r).Mode, State(r).Place));
        Run(r, 3 * World.TicksPerSecond, Forward, changes);                  // to x 9: still public
        Assert.Empty(changes);
        Run(r, World.TicksPerSecond, Forward, changes);                      // over x 10 into the ruin
        Assert.Equal((MasqueradeMode.Unbound, MasqueradeCause.EnteredFree, PlaceKind.Free), (changes[^1].Mode, changes[^1].Cause, changes[^1].Place));
        Assert.Equal(Elder.RunSpeed, r.W.Get<Locomotion>(r.Player).RunSpeed);
        Run(r, World.TicksPerSecond / 2, Forward, changes);
        PressKey(r, Forward, changes);                                         // the key, in the free place: back in the masquerade
        Assert.Equal((MasqueradeMode.Masquerade, MasqueradeCause.Key), (changes[^1].Mode, changes[^1].Cause));
        Assert.Equal(Human.RunSpeed, r.W.Get<Locomotion>(r.Player).RunSpeed);
        Run(r, World.TicksPerSecond / 2, Forward, changes);
        PressKey(r, Forward, changes);                                         // and unbound again
        Assert.Equal((MasqueradeMode.Unbound, MasqueradeCause.Key), (changes[^1].Mode, changes[^1].Cause));
        Run(r, 5 * World.TicksPerSecond, Forward, changes);                  // out past x 30: public
        Assert.True(r.W.Get<Body>(r.Player).Feet.X > 31f);
        Assert.Equal((MasqueradeMode.Masquerade, MasqueradeCause.EnteredPublic, PlaceKind.Public), (changes[^1].Mode, changes[^1].Cause, changes[^1].Place));
        Assert.Equal(
            [(MasqueradeMode.Unbound, MasqueradeCause.EnteredFree), (MasqueradeMode.Masquerade, MasqueradeCause.Key),
             (MasqueradeMode.Unbound, MasqueradeCause.Key), (MasqueradeMode.Masquerade, MasqueradeCause.EnteredPublic)],
            changes.Select(c => (c.Mode, c.Cause)));
        Assert.Equal(PlaceKind.Public, State(r).Place);
    }

    [Fact]
    public void A_body_lingering_at_a_place_s_edge_does_not_switch_until_it_has_been_inside_a_quarter_second()
    {
        var r = Make(Floor(FreeStrip()), at: new(9.8f, 0, 0.01f));
        Run(r, 30, ActionFrame.Idle);
        var changes = new List<MasqueradeModeChanged>();
        // In and out of the ruin's edge, never a quarter second inside.
        for (int i = 0; i < 6; i++)
        {
            r.W.Get<Body>(r.Player).Feet.X = i % 2 == 0 ? 10.2f : 9.8f;
            Run(r, (int)(Masquerade.SettleSeconds * World.TicksPerSecond) - 6, ActionFrame.Idle, changes);
        }
        Assert.Empty(changes);
        r.W.Get<Body>(r.Player).Feet.X = 10.2f;
        Run(r, World.TicksPerSecond / 2, ActionFrame.Idle, changes);
        Assert.Single(changes);
        Assert.Equal(MasqueradeCause.EnteredFree, changes[0].Cause);
    }

    [Fact]
    public void A_player_starting_in_a_free_place_starts_unbound_and_with_the_switching_off_only_the_key_changes_it()
    {
        var inRuin = Make(Floor(FreeStrip()), at: new(20, 0, 0.01f));
        Assert.Equal((MasqueradeMode.Unbound, PlaceKind.Free), (State(inRuin).Mode, State(inRuin).Place));

        var r = Make(Floor(FreeStrip()), auto: false);
        var changes = new List<MasqueradeModeChanged>();
        Run(r, 6 * World.TicksPerSecond, Forward, changes);                  // well into the ruin
        Assert.Equal((MasqueradeMode.Masquerade, PlaceKind.Free), (State(r).Mode, State(r).Place));
        Assert.Empty(changes);
        PressKey(r, Forward, changes);
        Assert.Equal(MasqueradeMode.Unbound, State(r).Mode);
    }

    // ---- risks ----

    [Fact]
    public void A_charged_jump_beyond_a_human_s_is_a_risk_once_in_public_in_either_mode_and_never_free_tapped_or_by_a_human()
    {
        int full = (int)((BodyMotor.HoldSeconds + Elder.ChargeSeconds) * World.TicksPerSecond) + 12;
        List<MasqueradeRisk> Jump(bool elder, bool unbound, int hold, LevelPlaces? places = null, Vector3? at = null)
        {
            var r = Make(Floor(places), elder, auto: false, at: at);
            var risks = new List<MasqueradeRisk>();
            Run(r, 30, ActionFrame.Idle);
            if (unbound) PressKey(r, ActionFrame.Idle);
            Run(r, hold, ActionFrame.Idle with { Held = Buttons.Jump }, risks: risks);
            Run(r, 3 * World.TicksPerSecond, ActionFrame.Idle, risks: risks);
            return risks;
        }
        foreach (bool unbound in new[] { false, true })
        {
            var risk = Assert.Single(Jump(elder: true, unbound, full));                                // the powered leap, either mode
            Assert.Equal((MasqueradeAct.Jump, TraversalKind.None), (risk.Act, risk.Move));
            Assert.InRange(risk.Measured, 2.9f, 3.1f);
            Assert.Equal(0.5f, risk.HumanLimit, 3);
            Assert.InRange(risk.Beyond, 2.4f, 2.6f);
            Assert.Contains("a jump of 3", risk.Words);
        }
        var half = Jump(elder: true, unbound: false, (int)((BodyMotor.HoldSeconds + 0.5f) * World.TicksPerSecond));
        Assert.InRange(Assert.Single(half).Measured, 1.7f, 1.8f);                                        // half a charge: 0.5 + 2.5 / 2
        Assert.Empty(Jump(elder: true, unbound: true, hold: 1));                                        // a tap: a human's jump
        Assert.Empty(Jump(elder: true, unbound: false, full, FreeStrip(), new(20, 0, 0.01f)));          // in a free place
        Assert.Empty(Jump(elder: false, unbound: true, full));                                          // a human body: nothing to charge
    }

    [Fact]
    public void In_the_masquerade_a_charged_jump_at_a_wall_of_3_5_m_is_the_leap_s_risk_and_the_mantle_s_and_a_tap_is_none()
    {
        // The powered leap works anywhere; its moves reach with the body's own numbers, and in public both are risks.
        List<MasqueradeRisk> Leap(int hold)
        {
            var r = Make(Floor(more: t => t.Box(new(0.6f, -3, 0), new(3, 3, 3.5f))), auto: false);
            var risks = new List<MasqueradeRisk>();
            Run(r, 30, ActionFrame.Idle);
            Run(r, hold, ActionFrame.Idle with { Held = Buttons.Jump }, risks: risks);
            Run(r, 3 * World.TicksPerSecond, ActionFrame.Idle, risks: risks);
            Assert.Equal(MasqueradeMode.Masquerade, State(r).Mode);
            return risks;
        }
        var seen = Leap((int)((BodyMotor.HoldSeconds + Elder.ChargeSeconds) * World.TicksPerSecond) + 12);
        Assert.Equal([(MasqueradeAct.Jump, TraversalKind.None), (MasqueradeAct.Move, TraversalKind.Mantle)], seen.Select(x => (x.Act, x.Move)));
        Assert.InRange(seen[1].Measured, 3.45f, 3.55f);
        Assert.Empty(Leap(1));
    }

    [Fact]
    public void In_the_masquerade_a_hair_of_a_charge_at_a_top_of_1_3_m_mantles_as_a_tap_does_and_a_whole_one_climbs_onto_it_a_risk()
    {
        // A walk in play: a neonate (its own jump 1 m, vault and climb to 1.6 m) in public, jump held just
        // past the hold — a jump of 0.51 m — climbed onto a top of 1.3 m with its own reach, a risk ("a climbonto of
        // 1.3 m (a human's at most 1.2)"). A charge's moves reach as far towards the body's own as the charge went: a
        // hair of one mantles as a tap does, with no risk; the whole charge climbs onto it, the leap's risk and the climb's.
        var neonate = Human with { RunSpeed = 3.5f, SprintSpeed = 7f, JumpImpulseBudget = Locomotion.ImpulseFor(1f, 9.81f), StandingReach = 2.35f, VaultMax = 1.6f, MantleMax = 2.4f };
        (List<TraversalKind> Moves, List<MasqueradeRisk> Risks) Leap(int hold)
        {
            var b = new WorldBuilder(1);
            MovementServices.Register(b, Floor(more: t => t.Box(new(0.6f, -3, 0), new(3, 3, 1.3f))));
            MasqueradeServices.Register(b, new MasqueradeSettings { AutoSwitch = false });
            var w = b.Build();
            var profiles = w.Service<MovementProfiles>();
            profiles.Add(new MovementProfile(AssetId.Of("test", "movement/human"), "a human", Human, true), isHuman: true);
            int n = profiles.Add(new MovementProfile(AssetId.Of("test", "movement/neonate"), "a neonate", neonate, true));
            var p = MovementServices.SpawnControlled(w, new(0, 0, 0.01f), 0f, Locomotion.Sandbox);
            profiles.Give(w, p, n);
            var r = new Rig(w, p, 0, n);
            var moves = new List<TraversalKind>();
            var risks = new List<MasqueradeRisk>();
            for (int i = 0; i < 30 + hold + 3 * World.TicksPerSecond; i++)
            {
                Run(r, 1, i >= 30 && i < 30 + hold ? ActionFrame.Idle with { Held = Buttons.Jump } : ActionFrame.Idle, risks: risks);
                foreach (var s in w.Events<TraversalStarted>().ThisTick) moves.Add(s.Kind);
            }
            Assert.Equal(MasqueradeMode.Masquerade, State(r).Mode);
            return (moves, risks);
        }
        var hair = Leap((int)(BodyMotor.HoldSeconds * World.TicksPerSecond) + 3);
        Assert.Equal([TraversalKind.Mantle], hair.Moves);
        Assert.Empty(hair.Risks);
        var tap = Leap(1);
        Assert.Equal([TraversalKind.Mantle], tap.Moves);
        Assert.Empty(tap.Risks);
        var whole = Leap((int)((BodyMotor.HoldSeconds + neonate.ChargeSeconds) * World.TicksPerSecond) + 12);
        Assert.Equal([TraversalKind.ClimbOnto], whole.Moves);
        Assert.Equal([(MasqueradeAct.Jump, TraversalKind.None), (MasqueradeAct.Move, TraversalKind.ClimbOnto)], whole.Risks.Select(x => (x.Act, x.Move)));
        Assert.InRange(whole.Risks[1].Measured, 1.28f, 1.32f);
    }

    [Fact]
    public void A_sprint_beyond_a_human_s_is_one_act_reported_once_at_its_top()
    {
        var r = Make(Floor(), auto: false, at: new(-15, 0, 0.01f));
        var risks = new List<MasqueradeRisk>();
        Run(r, 30, ActionFrame.Idle);
        PressKey(r, ActionFrame.Idle);
        var sprint = Forward with { Held = Buttons.Sprint };
        Run(r, 3 * World.TicksPerSecond, sprint, risks: risks);
        var risk = Assert.Single(risks);
        Assert.Equal(MasqueradeAct.Speed, risk.Act);
        Assert.InRange(risk.Measured, 8.9f, 9.05f);
        Assert.Equal(Human.SprintSpeed, risk.HumanLimit);
        Run(r, 1, ActionFrame.Idle with { Held = Buttons.Jump }, risks: risks);                        // a hop mid-act is no new act (a tap: an earlier build)
        Run(r, World.TicksPerSecond / 4 - 1, ActionFrame.Idle, risks: risks);
        risks.RemoveAll(x => x.Act == MasqueradeAct.Jump);
        Run(r, World.TicksPerSecond / 4, sprint, risks: risks);
        Assert.Single(risks);
        Run(r, 3 * World.TicksPerSecond, ActionFrame.Idle, risks: risks);                               // down from the hop and stopped: the act is over
        Assert.Equal(0, State(r).SpeedAct);
        r.W.Get<Body>(r.Player).Feet = new(-15, 0, 0.01f);
        Run(r, 2 * World.TicksPerSecond, sprint, risks: risks);                                         // a second sprint, a second act
        Assert.Equal(2, risks.Count(x => x.Act == MasqueradeAct.Speed));
    }

    [Fact]
    public void A_drop_landed_faster_than_a_human_s_tolerance_is_a_risk_on_landing()
    {
        var level = Floor(more: t => t.Box(new(-2, -2, 0), new(2, 2, 6f)));
        var r = Make(level, auto: false, at: new(0, 0, 6.01f));
        var risks = new List<MasqueradeRisk>();
        Run(r, 30, ActionFrame.Idle);
        PressKey(r, ActionFrame.Idle);
        // At a run: a walk stops at a drop beyond the safe fall.
        Run(r, 3 * World.TicksPerSecond, Forward, risks: risks);
        var fall = Assert.Single(risks, x => x.Act == MasqueradeAct.Fall);
        Assert.InRange(fall.Measured, 5.8f, 6.2f);
        Assert.Equal(Masquerade.SafeFall(Human), fall.HumanLimit, 3);
        Assert.True(r.W.Get<Body>(r.Player).Feet.Z < 0.1f);
    }

    [Fact]
    public void A_traversal_beyond_a_human_s_band_is_a_risk_as_it_starts_and_a_human_one_is_not()
    {
        var b = new WorldBuilder(1);
        MovementServices.Register(b, Floor());
        if (!b.Peek.Stores.Any(s => s is Store<Traversal>)) b.Component<Traversal>();   // until the body writes it
        MasqueradeServices.Register(b, new MasqueradeSettings { AutoSwitch = false });
        var w = b.Build();
        var profiles = w.Service<MovementProfiles>();
        profiles.Add(new MovementProfile(AssetId.Of("test", "movement/human"), "a human", Human, true), isHuman: true);
        int elder = profiles.Add(new MovementProfile(AssetId.Of("test", "movement/elder"), "an elder", Elder, true));
        var p = MovementServices.SpawnControlled(w, new(0, 0, 0.01f), 0f, Locomotion.Sandbox);
        profiles.Give(w, p, elder);
        w.Get<MasqueradeState>(p).Mode = MasqueradeMode.Unbound;
        var system = new MasqueradeRiskSystem();
        var risks = w.Events<MasqueradeRisk>();

        void See(TraversalKind kind, float progress, float height, Vector3 lip = default)
        {
            w.Set(p, new Traversal { Kind = kind, Progress = progress, Seconds = 0.7f, From = new(0, 0, 0), To = new(1, 0, height), Lip = lip, Height = height });
            system.Run(w);
        }
        See(TraversalKind.Vault, 0.0f, 1.0f);           // a human's vault
        See(TraversalKind.Vault, 0.5f, 1.0f);
        See(TraversalKind.None, 0f, 0f);
        See(TraversalKind.Mantle, 0.0f, 3.5f);          // beyond a human's 1.9
        See(TraversalKind.Mantle, 0.3f, 3.5f);          // the same act
        See(TraversalKind.Mantle, 0.9f, 3.5f);
        See(TraversalKind.PullUp, 0.0f, 3.5f);          // what follows is no new act
        See(TraversalKind.None, 0f, 0f);
        See(TraversalKind.Grab, 0.0f, 4.5f, new(0.4f, 0, 5f));   // a lip 5 m over the ground left: a human reaches 2.8
        var seen = risks.ThisTick.ToList();
        Assert.Equal([TraversalKind.Mantle, TraversalKind.Grab], seen.Select(s => s.Move));
        Assert.All(seen, s => Assert.Equal(MasqueradeAct.Move, s.Act));
        Assert.Equal(Human.MantleMax, seen[0].HumanLimit);
        Assert.InRange(seen[1].Measured, 4.95f, 5.0f);                 // the lip above the ground the jump left
        Assert.Equal(Human.StandingReach + 0.5f, seen[1].HumanLimit, 3);
    }

    // ---- the same every time ----

    [Fact]
    public void Two_runs_toggling_the_masquerade_and_crossing_places_have_the_same_digest_at_every_tick()
    {
        List<ulong> Once()
        {
            var r = Make(Floor(FreeStrip()), seed: 7);
            var rng = new Rng(3);
            var digests = new List<ulong>();
            var frame = Forward;
            for (int t = 0; t < 1500; t++)
            {
                if (t % 40 == 0)
                {
                    var held = Buttons.None;
                    if (rng.NextInt(3) == 0) held |= Buttons.Masquerade;
                    if (rng.NextInt(3) == 0) held |= Buttons.Sprint;
                    if (rng.NextInt(4) == 0) held |= Buttons.Jump;
                    frame = new ActionFrame(new Vector2(rng.NextFloat(-0.3f, 0.3f), rng.NextFloat(0.2f, 1f)), new Vector2(rng.NextFloat(-0.02f, 0.02f), 0), held, default);
                }
                else frame = frame with { Held = frame.Held & ~(Buttons.Masquerade | Buttons.Jump), Look = Vector2.Zero };
                MovementServices.Step(r.W, frame);
                digests.Add(r.W.Digest());
            }
            return digests;
        }
        Assert.Equal(Once(), Once());
    }

    [Fact]
    public void A_tape_carries_the_masquerade_key_and_its_replay_agrees_digest_for_digest()
    {
        var level = Floor(FreeStrip());
        var r = Make(level, seed: 5);
        var tape = new Tape { Header = new TapeHeader { Build = "test", Seed = 5, Notes = new() { ["bodyProfile"] = "elder" } } };
        for (int t = 0; t < 900; t++)
        {
            var held = (t % 150 == 10 ? Buttons.Masquerade : Buttons.None) | (t % 300 < 150 ? Buttons.Sprint : Buttons.None);
            var frame = Forward with { Held = held };
            MovementServices.Step(r.W, frame);
            tape.Frames.Add(frame);
            if (r.W.Tick % 60 == 0) tape.Digests[r.W.Tick] = r.W.Digest();
        }
        var path = Path.Combine(Directory.CreateTempSubdirectory("vitomir-masq-").FullName, "t.tape");
        tape.Write(path);
        var back = Tape.Read(path);
        Assert.Equal("elder", back.Header.Notes["bodyProfile"]);
        Assert.Contains(back.Frames, f => f.Holds(Buttons.Masquerade));
        var again = Make(level, seed: 5);
        int agreed = 0;
        foreach (var f in back.Frames)
        {
            MovementServices.Step(again.W, f);
            if (back.Digests.TryGetValue(again.W.Tick, out ulong want)) { Assert.Equal(want, again.W.Digest()); agreed++; }
        }
        Assert.Equal(15, agreed);
        Assert.Equal(State(r).Mode, State(again).Mode);
    }
}
