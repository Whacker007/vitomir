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
using Xunit;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The stagger when hit (<i>"I think we need to bring the stagger when hit back mechanic as
/// well"</i>): a wounding blow staggers — a swing before its strike cut short, the hurt for a time from the impulse, the upper body
/// pushed away from the blow — with a short guard against a lock; a soaked blow does not; an overpowered guard does.
/// </summary>
[Collection(AllocationCollection.Name)]
public sealed class StaggerTests(ITestOutputHelper output)
{
    static readonly Lazy<OneOneLibrary?> Library = new(() => GameInstall.TryOpen() is { } i ? new OneOneLibrary(i) : null);

    sealed record Scene(World World, EntityId Guard, EntityId Foe, List<Staggered> Staggers, List<StrikeMoment> Strikes)
    {
        public void Step(int ticks = 1)
        {
            for (int i = 0; i < ticks; i++)
            {
                MovementServices.Step(World, ActionFrame.Idle);
                Staggers.AddRange(World.Events<Staggered>().ThisTick);
                Strikes.AddRange(World.Events<StrikeMoment>().ThisTick);
            }
        }

        public void Wait(float seconds) => Step((int)MathF.Round(seconds * World.TicksPerSecond));

        public ref CharacterAnimation Anim => ref World.Get<CharacterAnimation>(Guard);
        public ref Combatant Fighter => ref World.Get<Combatant>(Guard);
        public HurtRule Rule => World.Service<HurtRule>();
    }

    static Scene Make(bool stagger = true, bool hurtClip = true, bool whole = false)
    {
        Assert.SkipUnless(Library.Value is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        var lib = Library.Value!;
        var assets = new CharacterAssets();
        int guardKind = OneOneLibrary.Register(assets, lib.Character("rudolfGuard", "Rapier")!);
        var level = new TestLevel().Floor(-60, -60, 60, 60).Build();
        var b = new WorldBuilder(5);
        MovementServices.Register(b, level);
        MasqueradeServices.Register(b);
        b.Animation(assets);
        b.HitVolumes();
        BodyMotionSystem.Register(b);
        CombatServices.Register(b);
        b.Service(new HurtRule { OnContact = false, Stagger = stagger, StaggerHurtClip = hurtClip, StaggerHurtWhole = whole });   // as the enhanced module sets it (guard.json's stagger)
        b.Event<HarmResolved>();
        b.Event<ContactMade>();
        DeathServices.Register(b);
        var w = b.Build();
        w.Service<HitVolumes>().Prepare(assets);
        // The guard stands at the origin facing +Y; his foe 1.8 m before him (only where blows come from).
        var guard = MovementServices.SpawnBody(w, new(0, 0, 0.01f), DetMath.HalfPi, Locomotion.Sandbox);
        w.Set(guard, CharacterAnimation.For(assets, guardKind));
        w.Set(guard, MotionInput.Standing);
        CombatServices.Arm(w, guard);
        var foe = MovementServices.SpawnBody(w, new(0, 1.8f, 0.01f), -DetMath.HalfPi, Locomotion.Sandbox);
        var s = new Scene(w, guard, foe, [], []);
        s.Step(30);
        return s;
    }

    /// <summary>A blow that wounds <paramref name="s"/>'s guard (and its contact: at <paramref name="point"/>, pushing along <paramref name="push"/>, <paramref name="impulse"/> N·s).</summary>
    static void Wound(Scene s, long id, int wounds = 1, float impulse = 20f, Vector3? point = null, Vector3? push = null, int healthAfter = 50)
    {
        var w = s.World;
        var p = point ?? new Vector3(0, 0.1f, 1.3f);
        var n = -(push ?? new Vector3(0, 1, 0));
        w.Events<ContactMade>().Publish(new ContactMade(id, s.Foe, 1, s.Guard, ContactMet.Body, "chest", ContactMaterial.Flesh, w.Tick, p, n, 12f, impulse,
            false, false, true, false));
        w.Events<HarmResolved>().Publish(new HarmResolved(id, s.Foe, s.Guard, w.Tick, HarmKind.Lethal, 4, 1, 0, 2, 0, wounds, 100, healthAfter));
    }

    [Fact]
    public void A_wounding_blow_cuts_a_swing_short_before_its_strike_plays_the_hurt_for_its_time_and_the_hurt_fades_then()
    {
        var s = Make();
        s.World.Get<CombatIntent>(s.Guard).Attack = true;   // a quick blow: pressed and let go
        s.Step();
        s.World.Get<CombatIntent>(s.Guard).Attack = false;
        s.Step(12);                                          // 0.1 s into its wind-up (its Strike mark is about 0.5 s in)
        Assert.True(ActionRoles.IsAttack(s.Anim.Action));
        Assert.True(s.Anim.ActionTime < s.Fighter.StrikeAt);
        float busyLeft = s.Fighter.Busy;
        Wound(s, 1, impulse: 20f);
        s.Step(2);
        Assert.Equal(ActionRole.Hurt, s.Anim.Action);
        var st = Assert.Single(s.Staggers);
        Assert.True(st.Interrupted);
        Assert.Equal(StaggerCause.Wound, st.Cause);
        float want = s.Rule.StaggerSeconds(20f);
        Assert.Equal(want, st.Seconds, 4);
        Assert.InRange(want, 0.49f, 0.51f);                                    // 0.3 + 0.01 × 20
        // The busy time: the larger of the cut-short swing's left (run on) and the stagger's — a blow is never quickened by being struck.
        output.WriteLine($"busy left when struck {busyLeft:0.000} s, the stagger {want:0.000} s, busy now {s.Fighter.Busy:0.000} s");
        Assert.InRange(s.Fighter.Busy, MathF.Max(want, busyLeft) - 4 * World.TickSeconds, MathF.Max(want, busyLeft) + World.TickSeconds);
        // No strike for the swing cut short.
        s.Wait(1f);
        Assert.Empty(s.Strikes);
        // The hurt fades out from the stagger's end (not 1.1's whole 1.3 s clip).
        var s2 = Make();
        Wound(s2, 2, impulse: 0f);                           // the least: 0.3 s
        int ticks = 0;
        while (s2.Anim.Action != ActionRole.Hurt && ticks < 10) { s2.Step(); ticks++; }   // the hurt begins (the tick after the rules')
        ticks = 0;
        while (s2.Anim.Action == ActionRole.Hurt && !s2.Anim.ActionEnding && ticks < 400) { s2.Step(); ticks++; }
        float ended = ticks * World.TickSeconds;
        output.WriteLine($"a 0.3 s stagger's hurt began to fade after {ended:0.000} s; the clip runs 1.3 s");
        Assert.InRange(ended, 0.27f, 0.35f);
        Assert.False(s2.Staggers[0].Interrupted);            // standing: nothing cut short
    }

    [Fact]
    public void A_soaked_blow_never_staggers_and_none_again_until_the_guard_after_the_last_has_passed()
    {
        var s = Make();
        Wound(s, 1, wounds: 0);                              // soaked to nothing (or low work): no stagger
        s.Step(3);
        Assert.Empty(s.Staggers);
        Assert.NotEqual(ActionRole.Hurt, s.Anim.Action);
        Wound(s, 2, impulse: 20f);                           // 0.5 s stagger
        s.Step(3);
        Assert.Single(s.Staggers);
        s.Wait(0.6f);                                        // over (0.5 s) but within its 0.5 s guard after
        Wound(s, 3, impulse: 20f);
        s.Step(3);
        Assert.Single(s.Staggers);
        s.Wait(0.5f);                                        // past the guard: 1.1 s after the first, which 1.1 would refuse till 3.5
        Wound(s, 4, impulse: 20f);
        s.Step(3);
        Assert.Equal(2, s.Staggers.Count);
        Assert.Equal(2, s.World.Get<Stagger>(s.Guard).Count);
        // A killing blow is the death's, not a stagger.
        s.Wait(1.2f);
        Wound(s, 5, wounds: 9, healthAfter: 0);
        s.Step(3);
        Assert.Equal(2, s.Staggers.Count);
    }

    [Fact]
    public void An_overpowered_guard_staggers_its_owner_and_a_held_one_does_not()
    {
        var s = Make();
        var w = s.World;
        w.Events<GuardResolved>().Publish(new GuardResolved(7, s.Foe, s.Guard, w.Tick, ContactMet.Weapon, GuardMeet.Blade, GuardVerdict.Held,
            1, 1, 1, 0, 0, 0.5, 0.5, 1, 1, 0, 0));
        s.Step(3);
        Assert.Empty(s.Staggers);
        w.Events<GuardResolved>().Publish(new GuardResolved(8, s.Foe, s.Guard, w.Tick, ContactMet.Weapon, GuardMeet.Blade, GuardVerdict.Overpowered,
            3, 1, 1, 0.9f, 0, 0.1, 0.5, 1, 0.5f, 0, 0.5f));
        s.Step(3);
        var st = Assert.Single(s.Staggers);
        Assert.Equal(StaggerCause.Overpowered, st.Cause);
        Assert.Equal(ActionRole.Hurt, s.Anim.Action);
    }

    [Fact]
    public void The_push_turns_the_upper_body_away_from_the_blow_on_the_side_struck_and_springs_back_to_nothing()
    {
        // The guard faces +Y (yaw 90°), his foe at +Y: his left is −X, and a blow pushes him back, −Y. At the push's most (0.08 s after the
        // blow), each region's place with the push and without it (the same tick, the push alone taken away): a blow to the middle of the
        // chest moves the head back; a blow at the left shoulder moves the left upper arm back more than the right.
        var push = new Vector3(0, -1, 0);
        (Vector3 Head, Vector3 Left, Vector3 Right, float Turn) Moved(Vector3 point)
        {
            var s = Make();
            Wound(s, 1, impulse: 20f, point: point, push: push);
            s.Step();
            while (s.World.Tick - s.Anim.StaggerSince < (long)MathF.Round(s.Rule.PushPeakSeconds * World.TicksPerSecond)) s.Step();
            Vector3 At(BodyRegion region)
            {
                var v = s.World.Service<HitVolumes>().Posed(s.World, s.Guard).ToArray().First(x => x.Region == region).Capsule;
                return (v.A + v.B) * 0.5f;
            }
            var poses = s.World.Service<CharacterPoses>();
            var (h1, l1, r1) = (At(BodyRegion.Head), At(BodyRegion.LeftUpperArm), At(BodyRegion.RightUpperArm));
            float turn = s.Anim.StaggerTurn;
            s.World.Get<CharacterAnimation>(s.Guard).StaggerTurn = 0f;
            poses.Invalidate();
            var (h0, l0, r0) = (At(BodyRegion.Head), At(BodyRegion.LeftUpperArm), At(BodyRegion.RightUpperArm));
            s.World.Get<CharacterAnimation>(s.Guard).StaggerTurn = turn;
            poses.Invalidate();
            return (h1 - h0, l1 - l0, r1 - r0, turn);
        }
        var chest = Moved(new Vector3(0, 0.15f, 1.35f));
        var shoulder = Moved(new Vector3(-0.22f, 0.1f, 1.45f));
        output.WriteLine($"a blow to the chest at 20 N·s: turned {chest.Turn * DetMath.RadToDeg:0.0}°, the head moved {Vector3.Dot(chest.Head, push):0.000} m back");
        output.WriteLine($"a blow at the left shoulder: the left upper arm {Vector3.Dot(shoulder.Left, push):0.000} m back, the right {Vector3.Dot(shoulder.Right, push):0.000} m");
        Assert.InRange(chest.Turn, 0.29f, 0.31f);                              // 0.015 × 20 = 0.3 rad at its most (17°)
        Assert.True(Vector3.Dot(chest.Head, push) > 0.05f);
        Assert.True(Vector3.Dot(shoulder.Left, push) > Vector3.Dot(shoulder.Right, push) + 0.02f);
        // Held to its most, and gone once settled.
        var s2 = Make();
        Wound(s2, 2, impulse: 60f);
        s2.Wait(0.12f);
        Assert.True(s2.Anim.StaggerPeak <= s2.Rule.PushMostRadians + 1e-6f);
        s2.Wait(0.8f);
        Assert.Equal(0f, s2.Anim.StaggerTurn);
        Assert.Equal(Vector3.Zero, s2.Anim.StaggerAxis);
    }

    [Fact]
    public void Whole_the_stagger_lasts_1_1s_hurt_clip_and_none_again_until_half_a_second_after_it()
    {
        // "the full stagger v1.1 base game animation": the hurt played whole, 1.3 s (39 frames), whatever the
        // impulse; busy for it; the swing in its wind-up cut short; the next stagger no sooner than 0.5 s after the clip's end.
        var s = Make(whole: true);
        s.World.Get<CombatIntent>(s.Guard).Attack = true;
        s.Step();
        s.World.Get<CombatIntent>(s.Guard).Attack = false;
        s.Step(12);
        Wound(s, 1, impulse: 5f);                            // a light blow: 0.35 s cut short, whole 1.3 s
        s.Step(2);
        var st = Assert.Single(s.Staggers);
        Assert.True(st.Interrupted);
        Assert.InRange(st.Seconds, 1.29f, 1.31f);
        Assert.Equal(ActionRole.Hurt, s.Anim.Action);
        Assert.True(s.Fighter.Busy >= 1.25f, $"busy {s.Fighter.Busy:0.00} s");
        int ticks = 0;
        while (s.Anim.Action == ActionRole.Hurt && !s.Anim.ActionEnding && ticks < 400) { s.Step(); ticks++; }
        output.WriteLine($"the whole hurt played {ticks * World.TickSeconds + 2 * World.TickSeconds:0.000} s before it began to fade");
        Assert.InRange(ticks * World.TickSeconds, 1.2f, 1.35f);
        Wound(s, 2, impulse: 20f);                           // just after the clip: within the guard
        s.Step(3);
        Assert.Single(s.Staggers);
        s.Wait(0.5f);
        Wound(s, 3, impulse: 20f);
        s.Step(3);
        Assert.Equal(2, s.Staggers.Count);
        Assert.Empty(s.Strikes);
    }

    [Fact]
    public void Push_only_cuts_the_swing_short_lowers_the_guard_for_its_time_and_plays_no_hurt()
    {
        // The push alone ( the enhanced module's default): the swing in its wind-up cut short, no strike; no hurt clip (the body stays in its
        // stance); busy and the guard kept down for the stagger's time; the push as ever.
        var s = Make(hurtClip: false);
        s.World.Get<CombatIntent>(s.Guard).Attack = true;
        s.Step();
        s.World.Get<CombatIntent>(s.Guard).Attack = false;
        s.Step(12);
        Assert.True(ActionRoles.IsAttack(s.Anim.Action));
        Wound(s, 1, impulse: 20f);
        s.Step(2);
        var st = Assert.Single(s.Staggers);
        Assert.True(st.Interrupted);
        Assert.NotEqual(ActionRole.Hurt, s.Anim.Action);
        Assert.True(s.Anim.Action == ActionRole.None || s.Anim.ActionEnding, $"the swing goes on: {s.Anim.Action}");
        Assert.True(s.Fighter.Busy >= st.Seconds - 3 * World.TickSeconds);
        Assert.True(s.Fighter.GuardBroken > 0f);
        Assert.True(s.Anim.StaggerPeak > 0f);
        // Block held through the stagger: not raised until it has passed.
        s.World.Get<CombatIntent>(s.Guard).Block = true;
        s.Wait(0.25f);
        Assert.False(s.Fighter.Blocking);
        s.Wait(1.2f);
        Assert.True(s.Fighter.Blocking);
        Assert.Empty(s.Strikes);
    }

    [Fact]
    public void Off_a_blow_flinches_the_whole_hurt_and_no_push()
    {
        var s = Make(stagger: false);
        Wound(s, 1, impulse: 30f);
        s.Step(3);
        Assert.Empty(s.Staggers);
        Assert.Equal(ActionRole.Hurt, s.Anim.Action);
        Assert.Equal(0f, s.Anim.StaggerPeak);
        Assert.True(s.World.Has<Flinch>(s.Guard));
        Assert.False(s.World.Has<Stagger>(s.Guard));
        Assert.True(s.Fighter.Busy > 1.2f);                  // the whole hurt clip, 1.3 s
    }

    [Fact]
    public void A_stagger_allocates_nothing_per_tick()
    {
        Allocations.None("a stagger's 90 ticks", () =>
        {
            var s = Make();
            var queues = Allocations.Warm(s.World);
            Wound(s, 1, impulse: 20f);                       // the first: the store's room for the stagger, once a world
            for (int i = 0; i < 160; i++) MovementServices.Step(s.World, ActionFrame.Idle);
            Wound(s, 2, impulse: 20f);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 90; i++) MovementServices.Step(s.World, ActionFrame.Idle);
            long spent = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(2, s.World.Get<Stagger>(s.Guard).Count);
            return spent;
        });
    }
}
