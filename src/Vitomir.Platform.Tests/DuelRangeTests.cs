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
/// The duelling brain's range keeping (<see cref="DuelRange"/>): a blade fighter put inside its range
/// steps back to it before it swings; with <see cref="DuelRangeSettings.Enabled"/> off it swings from where it is (the brain before);
/// pinned (here: a body that cannot move) it side-steps, then swings anyway when it has held as long as it may; a bare-handed fighter
/// closes as ever; the same seed gives the same bout. Skips without the 1.1 install (VTMR_GAME_DIR) or the proving ground
/// (VITOMIR_COURSE_LEVEL).
/// </summary>
public sealed class DuelRangeTests
{
    static readonly Lazy<OneOneLibrary?> Library = new(() => GameInstall.TryOpen() is { } i ? new OneOneLibrary(i) : null);

    static readonly Lazy<LevelSpace?> Course = new(() =>
    {
        var path = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        return path is not null && File.Exists(Path.Combine(path, "level.json")) ? LevelSpace.Open(path) : null;
    });

    sealed record Rig(World World, EntityId A, EntityId B, List<AttackBegun> Begun, List<float> Gap)
    {
        public void Run(float seconds)
        {
            int ticks = (int)MathF.Round(seconds * World.TicksPerSecond);
            for (int i = 0; i < ticks; i++)
            {
                MovementServices.Step(World, ActionFrame.Idle);
                foreach (var b in World.Events<AttackBegun>().ThisTick) if (b.Entity == A) Begun.Add(b);
                var a = World.Get<Body>(A).Feet;
                var f = World.Get<Body>(B).Feet;
                Gap.Add(Vector2.Distance(new(a.X, a.Y), new(f.X, f.Y)));
            }
        }
    }

    /// <summary>Christof (a broadsword, or bare-handed) at x 38 facing a standing guard <paramref name="gap"/> m away; range keeping on or off.</summary>
    static Rig Make(float gap, bool range = true, string weapon = "BroadSword", bool frozen = false, ulong seed = 11)
    {
        Assert.SkipUnless(Library.Value is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        Assert.SkipUnless(Course.Value is not null, "no proving ground (VITOMIR_COURSE_LEVEL: a cooked proving-ground.l2)");
        var assets = new CharacterAssets();
        int christof = OneOneLibrary.Register(assets, Library.Value!.Character("Christof", weapon)!);
        int guard = OneOneLibrary.Register(assets, Library.Value!.Character("rudolfGuard")!);
        var builder = new WorldBuilder(seed);
        MovementServices.Register(builder, Course.Value!);
        MasqueradeServices.Register(builder);
        builder.Animation(assets);
        BodyMotionSystem.Register(builder);
        CombatServices.Register(builder);
        DuelBrains.Register(builder);
        builder.Peek.Service<DuelRangeSettings>().Enabled = range;
        var w = builder.Build();
        var loco = Locomotion.Sandbox;
        if (frozen) loco = loco with { WalkSpeed = 0f, RunSpeed = 0f, SprintSpeed = 0f };
        var ea = MovementServices.SpawnBody(w, new(38f, 3f, 0.02f), 0f, loco);
        var eb = MovementServices.SpawnBody(w, new(38f + gap, 3f, 0.02f), DetMath.Pi, Locomotion.Sandbox);
        foreach (var (e, k) in new[] { (ea, christof), (eb, guard) })
        {
            w.Set(e, CharacterAnimation.For(assets, k));
            w.Set(e, MotionInput.Standing);
            CombatServices.Arm(w, e);
        }
        var book = w.Service<DuelBrainBook>();
        float ra = DuelBrains.ReachOf(assets, christof), rb = DuelBrains.ReachOf(assets, guard);
        w.Set(ea, DuelBrain.For(BrainKind.Duel, eb, book.Add(new DuelBrainSettings { BackOffChance = 0f, BlockChance = 0f }), ra, rb));
        w.Set(eb, DuelBrain.For(BrainKind.Stand, ea, book.Add(new DuelBrainSettings()), rb, ra));
        return new Rig(w, ea, eb, [], []);
    }

    [Fact]
    public void Inside_its_range_a_blade_fighter_steps_back_to_it_before_it_swings()
    {
        var rig = Make(0.5f);
        var brain = rig.World.Get<DuelBrain>(rig.A);
        var s = new DuelBrainSettings();
        rig.Run(3f);
        var r = rig.World.Get<DuelRange>(rig.A);
        Assert.True(r.Retreats >= 1);
        Assert.NotEmpty(rig.Begun);
        int first = (int)(rig.Begun[0].Tick - 1);
        // Its first swing came once it had stepped back to its range, not from inside it.
        Assert.True(rig.Gap[Math.Clamp(first, 0, rig.Gap.Count - 1)] >= brain.Reach * s.RangeNearShare, $"first swing at {rig.Gap[first]:0.00} m");
        Assert.True(rig.Gap.Max() >= brain.Reach * s.CloseShare * 0.95f - 0.05f);
    }

    [Fact]
    public void Off_it_swings_from_inside_as_the_brain_before_did()
    {
        var rig = Make(0.5f, range: false);
        rig.Run(1f);
        Assert.NotEmpty(rig.Begun);
        Assert.True(rig.Gap[(int)rig.Begun[0].Tick - 1] < 0.8f);   // at once, the foe still at the hilt
        Assert.False(rig.World.Has<DuelRange>(rig.A));            // no range kept: its store stays empty
    }

    [Fact]
    public void Pinned_it_side_steps_and_swings_anyway_when_it_has_held_as_long_as_it_may()
    {
        var rig = Make(0.5f, frozen: true);   // a body that cannot move: pinned wherever it steps
        var s = new DuelBrainSettings();
        rig.Run(2.5f);
        var r = rig.World.Get<DuelRange>(rig.A);
        Assert.True(r.SideSteps >= 1, "it tried a side-step");
        Assert.True(r.Forced >= 1, "it swung anyway");
        Assert.NotEmpty(rig.Begun);
        Assert.True(rig.Begun[0].Tick >= (long)(s.RangeHoldMostSeconds * World.TicksPerSecond) - 2, $"its first swing at tick {rig.Begun[0].Tick}");
    }

    [Fact]
    public void A_bare_handed_fighter_closes_as_ever()
    {
        var rig = Make(0.5f, weapon: "Punch");
        rig.Run(1.5f);
        Assert.False(rig.World.Has<DuelRange>(rig.A));
        Assert.NotEmpty(rig.Begun);
    }

    [Fact]
    public void The_same_seed_gives_the_same_bout()
    {
        var a = Make(0.6f);
        var b = Make(0.6f);
        a.Run(3f);
        b.Run(3f);
        Assert.Equal(a.World.Digest(), b.World.Digest());
        Assert.Equal(a.Gap, b.Gap);
    }
}
