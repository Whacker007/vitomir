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
using Vitomir.Platform.Powers;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The arena's duelling brain (<see cref="DuelBrainSystem"/>) on the proving ground's free yard: it closes on its foe
/// and strikes when in reach, blocks when the foe begins a blow (by its chance), backs off a step after its own blow (by its
/// chance); a blocking dummy holds the block and a standing one does nothing; the reach it closes to is the drawn blade's.
/// Everything goes through the same systems as the player's buttons. the quick slots, against a stand-in for the module's
/// powers (it shows the slots' views and reads the slot pressed): a ready slot is pressed once engaged and not before, not
/// again while live, renewed under the margin, only the slots it may use; nothing at all without views (digest for digest).
/// Skips without the 1.1 install (VTMR_GAME_DIR) or the proving ground (VITOMIR_COURSE_LEVEL).
/// </summary>
public sealed class DuelBrainTests
{
    static readonly Lazy<OneOneLibrary?> Library = new(() => GameInstall.TryOpen() is { } i ? new OneOneLibrary(i) : null);

    static readonly Lazy<LevelSpace?> Course = new(() =>
    {
        var path = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        return path is not null && File.Exists(Path.Combine(path, "level.json")) ? LevelSpace.Open(path) : null;
    });

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

    /// <summary>Two fighters in the free yard, 4 m apart facing each other: Christof (broadsword) at x 38 and the guard (rapier and shield) at x 42.</summary>
    sealed class Bout
    {
        public required World World { get; init; }
        public required EntityId A { get; init; }
        public required EntityId B { get; init; }
        public readonly List<AttackBegun> Begun = [];
        public readonly List<StrikeMoment> Strikes = [];
        public readonly List<BlockRaised> Raised = [];
        public readonly List<BlockLowered> Lowered = [];
        public readonly List<float> Distance = [];

        public void Run(float seconds)
        {
            int ticks = (int)MathF.Round(seconds * World.TicksPerSecond);
            for (int i = 0; i < ticks; i++)
            {
                MovementServices.Step(World, ActionFrame.Idle);
                Begun.AddRange(World.Events<AttackBegun>().ThisTick);
                Strikes.AddRange(World.Events<StrikeMoment>().ThisTick);
                Raised.AddRange(World.Events<BlockRaised>().ThisTick);
                Lowered.AddRange(World.Events<BlockLowered>().ThisTick);
                Distance.Add(Gap());
            }
        }

        public float Gap()
        {
            var a = World.Get<Body>(A).Feet;
            var b = World.Get<Body>(B).Feet;
            return Vector2.Distance(new(a.X, a.Y), new(b.X, b.Y));
        }

        public ref DuelBrain Brain(EntityId e) => ref World.Get<DuelBrain>(e);
    }

    static Bout Make(BrainKind a, BrainKind b, DuelBrainSettings? aNumbers = null, DuelBrainSettings? bNumbers = null, ulong seed = 11, Action<WorldBuilder>? register = null)
    {
        Assert.SkipUnless(Course.Value is not null, "no proving ground (VITOMIR_COURSE_LEVEL: a cooked proving-ground.l2)");
        var assets = new CharacterAssets();
        int christof = OneOneLibrary.Register(assets, Character("Christof", "BroadSword"));
        int guard = OneOneLibrary.Register(assets, Character("rudolfGuard"));
        var builder = new WorldBuilder(seed);
        MovementServices.Register(builder, Course.Value!);
        MasqueradeServices.Register(builder);
        builder.Animation(assets);
        BodyMotionSystem.Register(builder);
        CombatServices.Register(builder);
        DuelBrains.Register(builder);
        register?.Invoke(builder);
        var w = builder.Build();
        var ea = MovementServices.SpawnBody(w, new(38f, 3f, 0.02f), 0f, Locomotion.Sandbox);
        var eb = MovementServices.SpawnBody(w, new(42f, 3f, 0.02f), DetMath.Pi, Locomotion.Sandbox);
        foreach (var (e, k) in new[] { (ea, christof), (eb, guard) })
        {
            w.Set(e, CharacterAnimation.For(assets, k));
            w.Set(e, MotionInput.Standing);
            CombatServices.Arm(w, e);
        }
        var book = w.Service<DuelBrainBook>();
        float ra = DuelBrains.ReachOf(assets, christof), rb = DuelBrains.ReachOf(assets, guard);
        w.Set(ea, DuelBrain.For(a, eb, book.Add(aNumbers ?? new DuelBrainSettings()), ra, rb));
        w.Set(eb, DuelBrain.For(b, ea, book.Add(bNumbers ?? new DuelBrainSettings()), rb, ra));
        return new Bout { World = w, A = ea, B = eb };
    }

    [Fact]
    public void The_reach_is_the_drawn_blade_s_tip_from_the_shoulder()
    {
        Assert.SkipUnless(Library.Value is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        var assets = new CharacterAssets();
        int christof = OneOneLibrary.Register(assets, Character("Christof", "BroadSword"));
        int guard = OneOneLibrary.Register(assets, Character("rudolfGuard"));
        int bare = OneOneLibrary.Register(assets, Character("Christof", "Punch"));
        float r = DuelBrains.ReachOf(assets, christof);
        // The arm (about 0.65 m) and the broadsword's tip 1.02 m from the hand.
        Assert.InRange(r, 1.5f, 1.9f);
        Assert.InRange(DuelBrains.ReachOf(assets, guard), 1.3f, 1.9f);
        Assert.Equal(DuelBrains.BareReach, DuelBrains.ReachOf(assets, bare));
    }

    [Fact]
    public void A_duelling_brain_closes_on_a_standing_foe_and_strikes_it_in_reach()
    {
        var bout = Make(BrainKind.Duel, BrainKind.Stand, new DuelBrainSettings { BackOffChance = 0f });
        var guardFeet = bout.World.Get<Body>(bout.B).Feet;
        float guardYaw = bout.World.Get<Body>(bout.B).Yaw;
        bout.Run(4f);
        float reach = bout.Brain(bout.A).Reach;
        var s = new DuelBrainSettings();
        // It closed from 4 m to its striking distance, and stopped about where it closes to.
        Assert.True(bout.Distance.Min() <= reach * s.StrikeShare, $"closest {bout.Distance.Min():0.00} m, reach {reach:0.00} m");
        Assert.InRange(bout.Distance[^1], reach * s.TooCloseShare - 0.2f, reach * s.StrikeShare + 0.05f);
        // Once in reach it keeps out of a clinch: never nearer than a little inside the too-close share (a strong blow's step).
        int inReach = bout.Distance.FindIndex(d => d <= reach * s.StrikeShare);
        Assert.True(bout.Distance.Skip(inReach).Min() > reach * s.TooCloseShare - 0.35f, $"nearest {bout.Distance.Skip(inReach).Min():0.00} m");
        // Blows were begun, all by Christof, and the first swing's tip at its Strike was by the guard (within 0.8 m of his
        // feet's axis). The second swing (chrisAttack2Sword) has its Strike mark early in its arc, the tip 1.9 m to the side:
        // the mark is not where a blade meets anyone (contact sweeps the whole arc).
        Assert.NotEmpty(bout.Begun);
        Assert.All(bout.Begun, b => Assert.Equal(bout.A, b.Entity));
        Assert.NotEmpty(bout.Strikes);
        var first = bout.Strikes[0];
        Assert.True(Vector2.Distance(new(first.Tip.X, first.Tip.Y), new(guardFeet.X, guardFeet.Y)) < 0.8f,
            $"the first tip at {first.Tip} is {Vector2.Distance(new(first.Tip.X, first.Tip.Y), new(guardFeet.X, guardFeet.Y)):0.00} m from the guard");
        // The standing dummy did nothing and did not turn.
        Assert.True(Vector3.Distance(guardFeet, bout.World.Get<Body>(bout.B).Feet) < 0.05f);
        Assert.Equal(0f, DetMath.WrapAngle(guardYaw - bout.World.Get<Body>(bout.B).Yaw), 3);
        Assert.Empty(bout.Raised);
        // It faces its foe.
        Assert.Equal(0f, DetMath.WrapAngle(bout.World.Get<Body>(bout.A).Yaw), 1);
    }

    [Fact]
    public void It_blocks_when_the_foe_begins_a_blow_by_its_chance()
    {
        // The guard attacks and never blocks; Christof attacks rarely (a strong share of nothing; he strikes only when ready).
        var never = new DuelBrainSettings { BlockChance = 0f };
        var always = new DuelBrainSettings { BlockChance = 1f, StrikeShare = 0.2f };    // he never comes close enough to strike
        var blocking = Make(BrainKind.Duel, BrainKind.Duel, always, never);
        blocking.Run(6f);
        var guardBlows = blocking.Begun.Where(b => b.Entity == blocking.B).ToList();
        Assert.NotEmpty(guardBlows);
        var mine = blocking.Raised.Where(r => r.Entity == blocking.A).ToList();
        Assert.NotEmpty(mine);
        // Each block followed one of the guard's blows by about the reaction (and before the next).
        foreach (var r in mine)
            Assert.Contains(guardBlows, b => r.Tick - b.Tick >= 0 && r.Tick - b.Tick <= (int)(0.15f * World.TicksPerSecond) + 2);

        var open = Make(BrainKind.Duel, BrainKind.Duel, never with { StrikeShare = 0.2f }, never);
        open.Run(6f);
        Assert.Contains(open.Begun, b => b.Entity == open.B);
        Assert.DoesNotContain(open.Raised, r => r.Entity == open.A);
    }

    // ---- the pace and the blade guard by training ----

    /// <summary>The gaps (s) between one fighter's blows begun.</summary>
    static List<float> Gaps(Bout bout, EntityId e)
    {
        var ticks = bout.Begun.Where(b => b.Entity == e).Select(b => b.Tick).ToList();
        return [.. ticks.Zip(ticks.Skip(1), (a, b) => (b - a) / (float)World.TicksPerSecond)];
    }

    [Fact]
    public void With_a_pace_it_pauses_after_each_run_and_without_one_it_strikes_as_soon_as_its_blow_is_over()
    {
        var numbers = new DuelBrainSettings { BackOffChance = 0f };
        var constant = Make(BrainKind.Duel, BrainKind.Stand, numbers, register: b => DuelPaces.Register(b));
        constant.Run(10f);                                                  // the store, but no pace on him: the earlier build's
        var paced = Make(BrainKind.Duel, BrainKind.Stand, numbers, register: b => DuelPaces.Register(b));
        paced.World.Set(paced.A, new DuelPace { PauseLeast = 1f, PauseMost = 1f, RunMost = 1 });
        paced.Run(10f);
        var steady = Gaps(constant, constant.A);
        var gaps = Gaps(paced, paced.A);
        float busy = steady.Min();
        // Constant: each blow begun as the last one's busy time ends (now and then a step back into reach first, range keeping's).
        Assert.True(steady.Count >= 6 && steady.Count(g => g < busy + 0.1f) >= steady.Count / 2 && steady.Max() < busy + 0.6f,
            $"constant: [{string.Join(", ", steady.Select(g => g.ToString("0.00")))}]");
        // A run of one: every gap is its blow's busy time, the 1 s pause after it, and the step back into striking reach (the pause
        // keeps it at its blade's length, a little beyond where it strikes).
        Assert.True(gaps.Count >= 3, $"paced: {gaps.Count} gaps");
        Assert.All(gaps, g => Assert.InRange(g, busy + 0.98f, busy + 1.8f));
        Assert.True(gaps.Average() - steady.Average() > 0.9f, $"paced: [{string.Join(", ", gaps.Select(g => g.ToString("0.00")))}]");
        ref var brain = ref paced.Brain(paced.A);
        Assert.Equal(gaps.Count + 1, brain.Blows);
        Assert.InRange(brain.Pauses, gaps.Count, gaps.Count + 1);
        Assert.Equal(brain.Pauses * 1f, brain.PausedSeconds, 3);

        // Its settings' pace in place of its own: none (an old spec's "pace": "none"), or a run of 1 to 2 with 0.5 to 1.5 s.
        var none = Make(BrainKind.Duel, BrainKind.Stand, numbers with { PaceLeastSeconds = 0f, PaceMostSeconds = 0f }, register: b => DuelPaces.Register(b));
        none.World.Set(none.A, new DuelPace { PauseLeast = 1f, PauseMost = 1f, RunMost = 1 });
        none.Run(10f);
        Assert.Equal(steady, Gaps(none, none.A));
        var given = Make(BrainKind.Duel, BrainKind.Stand, numbers with { PaceLeastSeconds = 0.5f, PaceMostSeconds = 1.5f, PaceRunMost = 2 }, register: b => DuelPaces.Register(b));
        given.Run(20f);
        var g2 = Gaps(given, given.A);
        Assert.Contains(g2, g => g < busy + 0.1f);                                  // within a run of two
        Assert.Contains(g2, g => g > busy + 0.48f);                                 // after a run
        Assert.All(g2, g => Assert.True(g < busy + 1.5f + 0.8f, $"{g:0.00} s"));
        Assert.InRange(given.Brain(given.A).Pauses, (g2.Count + 1) / 2, g2.Count + 1);
        // Without the component (pace-off) a pace in its settings is nothing: the earlier build's constant pressure.
        var off = Make(BrainKind.Duel, BrainKind.Stand, numbers with { PaceLeastSeconds = 0.5f, PaceMostSeconds = 1.5f });
        off.Run(10f);
        Assert.Equal(steady, Gaps(off, off.A));
    }

    [Fact]
    public void A_pause_keeps_it_at_its_blade_s_distance_circling_and_an_opening_ends_it()
    {
        var numbers = new DuelBrainSettings { BackOffChance = 0f };
        var bout = Make(BrainKind.Duel, BrainKind.Stand, numbers, register: b => DuelPaces.Register(b));
        bout.World.Set(bout.A, new DuelPace { PauseLeast = 3f, PauseMost = 3f, RunMost = 1, RiposteChance = 1f });   // every opening
        bout.Run(2.5f);                                            // closes, strikes once, its pause begins
        Assert.Single(bout.Begun, b => b.Entity == bout.A);
        var foe = bout.World.Get<Body>(bout.B).Feet;
        var before = bout.World.Get<Body>(bout.A).Feet;
        int from = bout.Distance.Count;
        bout.Run(1.5f);
        float reach = bout.Brain(bout.A).Reach;
        var during = bout.Distance.Skip(from + 60).ToList();       // after half a second to settle
        Assert.InRange(during.Min(), reach * 0.85f, reach * 1.45f);
        Assert.True(during.Max() < reach * 1.45f, $"{during.Max():0.00} m against reach {reach:0.00}");
        var after = bout.World.Get<Body>(bout.A).Feet;
        float turned = MathF.Abs(DetMath.WrapAngle(DetMath.Atan2(after.Y - foe.Y, after.X - foe.X) - DetMath.Atan2(before.Y - foe.Y, before.X - foe.X)));
        Assert.True(turned > 0.2f, $"circled {turned * 180 / MathF.PI:0} degrees round its foe");
        Assert.Single(bout.Begun, b => b.Entity == bout.A);        // no blow during the pause
        // An opening: its foe's guard broken (its stun or a stagger the same) — the pause ends at once and it strikes.
        Assert.True(bout.Brain(bout.A).PauseLeft > 0.5f);
        bout.World.Get<Combatant>(bout.B).GuardBroken = 0.5f;
        bout.Run(0.4f);
        Assert.True(bout.Brain(bout.A).PausesCut >= 1);   // and the pause after that blow too, while the guard is still broken
        Assert.Equal(2, bout.Begun.Count(b => b.Entity == bout.A));
    }

    [Theory]
    [InlineData(0f, 0)]   // never ripostes: the pause runs out
    [InlineData(1f, 1)]   // always: the opening ends it at once
    public void An_opening_ends_a_pause_by_its_riposte_chance_drawn_once_an_opening(float chance, int cut)
    {
        // The chance by Melee is the module's (guard.json's brain_pace.riposte); here given.
        var bout = Make(BrainKind.Duel, BrainKind.Stand, new DuelBrainSettings { BackOffChance = 0f }, register: b => DuelPaces.Register(b));
        bout.World.Set(bout.A, new DuelPace { PauseLeast = 3f, PauseMost = 3f, RunMost = 1, RiposteChance = chance });
        bout.Run(2.5f);
        Assert.True(bout.Brain(bout.A).PauseLeft > 0.5f);
        bout.World.Get<Combatant>(bout.B).GuardBroken = 0.5f;                     // an opening, lasting 0.5 s
        bout.Run(0.4f);
        Assert.Equal(cut, bout.Brain(bout.A).PausesCut);
        Assert.Equal(1 + cut, bout.Begun.Count(b => b.Entity == bout.A));
        Assert.Equal(1, bout.Brain(bout.A).OpeningSeen);                          // drawn once for it, not every tick
    }

    [Fact]
    public void Untrained_and_unshielded_it_raises_no_blade_to_block_but_a_shield_it_carries_blocks_as_ever()
    {
        // Christof (broadsword, no shield) and the guard (rapier and shield) both always block, both untrained; the guard attacks.
        var always = new DuelBrainSettings { BlockChance = 1f, StrikeShare = 0.2f };
        var attacks = new DuelBrainSettings { BlockChance = 1f };
        void Untrained(WorldBuilder b) => GuardSkills.Register(b);
        var bout = Make(BrainKind.Duel, BrainKind.Duel, always, attacks, register: Untrained);
        bout.World.Set(bout.A, GuardSkill.Normal with { Untrained = true });
        bout.World.Set(bout.B, GuardSkill.Normal with { Untrained = true });
        bout.Run(6f);
        Assert.Contains(bout.Begun, b => b.Entity == bout.B);
        Assert.DoesNotContain(bout.Raised, r => r.Entity == bout.A);        // no shield: no blade raised
        Assert.Equal(0, bout.Brain(bout.A).Blocks);                          // its draws made, none succeeding
        // Trained, the same bout blocks.
        var trained = Make(BrainKind.Duel, BrainKind.Duel, always, attacks, register: Untrained);
        trained.World.Set(trained.A, GuardSkill.Normal);
        trained.Run(6f);
        Assert.Contains(trained.Raised, r => r.Entity == trained.A);
        // The guard carries a shield: untrained, he still raises it against Christof's blows.
        var shield = Make(BrainKind.Duel, BrainKind.Duel, new DuelBrainSettings(), new DuelBrainSettings { BlockChance = 1f, StrikeShare = 0.2f }, register: Untrained);
        Assert.True(HeldItems.HasShield(shield.World, shield.B));
        Assert.False(HeldItems.HasShield(shield.World, shield.A));
        shield.World.Set(shield.B, GuardSkill.Normal with { Untrained = true });
        shield.Run(6f);
        Assert.Contains(shield.Begun, b => b.Entity == shield.A);
        Assert.Contains(shield.Raised, r => r.Entity == shield.B);
    }
    // ---- end

    [Fact]
    public void It_backs_off_a_step_after_its_blow_by_its_chance()
    {
        var always = Make(BrainKind.Duel, BrainKind.Stand, new DuelBrainSettings { BackOffChance = 1f, StrongShare = 0f });
        always.Run(5f);
        Assert.True(always.Brain(always.A).BackOffs > 0);
        // After a blow's busy time the gap grows by a step (a walk for 0.5 s), then closes again.
        int first = always.Begun[0].Tick is var t ? (int)t : 0;
        int end = first + (int)MathF.Round(1.1f * World.TicksPerSecond);     // the broadsword's quick blow keeps him busy 1.1 s
        float atEnd = always.Distance[Math.Min(end, always.Distance.Count - 1)];
        float widest = always.Distance.Skip(end).Take(World.TicksPerSecond).Max();
        Assert.True(widest > atEnd + 0.3f, $"the gap at the blow's end {atEnd:0.00} m, at most {widest:0.00} m after");

        var never = Make(BrainKind.Duel, BrainKind.Stand, new DuelBrainSettings { BackOffChance = 0f, StrongShare = 0f });
        never.Run(5f);
        Assert.Equal(0, never.Brain(never.A).BackOffs);
        int closeAt = never.Distance.FindIndex(d => d <= never.Brain(never.A).Reach * 0.8f);
        Assert.True(closeAt > 0);
        Assert.True(never.Distance.Skip(closeAt).Max() < never.Brain(never.A).Reach * 0.85f + 0.05f, "it never stepped back");
    }

    [Fact]
    public void A_blocking_dummy_holds_the_block_and_a_standing_one_does_nothing()
    {
        var bout = Make(BrainKind.Block, BrainKind.Stand);
        bout.Run(2f);
        Assert.Single(bout.Raised);
        Assert.Equal(bout.A, bout.Raised[0].Entity);
        Assert.Empty(bout.Lowered);
        Assert.True(bout.World.Get<Combatant>(bout.A).Blocking);
        Assert.Empty(bout.Begun);
        Assert.Equal(4f, bout.Gap(), 1);
    }

    [Fact]
    public void The_same_seed_makes_the_same_duel()
    {
        var one = Make(BrainKind.Duel, BrainKind.Duel, seed: 5);
        var two = Make(BrainKind.Duel, BrainKind.Duel, seed: 5);
        one.Run(4f);
        two.Run(4f);
        Assert.Equal(one.World.Digest(), two.World.Digest());
        Assert.Equal(one.Begun, two.Begun);
        var other = Make(BrainKind.Duel, BrainKind.Duel, seed: 6);
        other.Run(4f);
        Assert.NotEqual(one.World.Digest(), other.World.Digest());
    }

    // ---- active Dodge ----

    static (List<DodgeBegun> Begun, List<DodgeEnded> Ended, List<DodgeRefused> Refused) RunDodges(Bout bout, float seconds)
    {
        var begun = new List<DodgeBegun>();
        var ended = new List<DodgeEnded>();
        var refused = new List<DodgeRefused>();
        int ticks = (int)MathF.Round(seconds * World.TicksPerSecond);
        for (int i = 0; i < ticks; i++)
        {
            MovementServices.Step(bout.World, ActionFrame.Idle);
            bout.Begun.AddRange(bout.World.Events<AttackBegun>().ThisTick);
            bout.Raised.AddRange(bout.World.Events<BlockRaised>().ThisTick);
            begun.AddRange(bout.World.Events<DodgeBegun>().ThisTick);
            ended.AddRange(bout.World.Events<DodgeEnded>().ThisTick);
            refused.AddRange(bout.World.Events<DodgeRefused>().ThisTick);
            bout.Distance.Add(bout.Gap());
        }
        return (begun, ended, refused);
    }

    [Fact]
    public void It_dodges_instead_of_blocking_when_the_foe_begins_a_blow_by_its_chance_back_from_its_foe()
    {
        // Christof dodges every blow (drawn before the block, which he would also always raise) and never comes to strike.
        var dodger = new DuelBrainSettings { DodgeChance = 1f, BlockChance = 1f, StrikeShare = 0.2f, DodgeBackShare = 1f, DodgeSideShare = 0f };
        var bout = Make(BrainKind.Duel, BrainKind.Duel, dodger, new DuelBrainSettings { BlockChance = 0f });
        var (begun, ended, _) = RunDodges(bout, 6f);
        var guardBlows = bout.Begun.Where(b => b.Entity == bout.B).ToList();
        Assert.NotEmpty(guardBlows);
        var mine = begun.Where(b => b.Entity == bout.A).ToList();
        Assert.NotEmpty(mine);
        Assert.DoesNotContain(bout.Raised, r => r.Entity == bout.A);                  // dodged, never blocked
        // Each dodge followed one of the guard's blows by about the reaction, straight back from him.
        foreach (var d in mine)
        {
            Assert.Contains(guardBlows, b => d.Tick - b.Tick >= 0 && d.Tick - b.Tick <= (int)(0.15f * World.TicksPerSecond) + 2);
            Assert.True(d.Way.X < -0.9f, $"the way {d.Way}: not back from the guard (he stands to +X)");
        }
        Assert.Contains(ended, x => x.Entity == bout.A && x.Moved > 0.5f);
        Assert.Equal(mine.Count, bout.Brain(bout.A).Dodges);
    }

    [Fact]
    public void It_dodges_to_a_side_by_its_shares_and_with_no_chance_it_never_draws_for_a_dodge()
    {
        var aside = new DuelBrainSettings { DodgeChance = 1f, StrikeShare = 0.2f, DodgeBackShare = 0f, DodgeSideShare = 1f };
        var bout = Make(BrainKind.Duel, BrainKind.Duel, aside, new DuelBrainSettings { BlockChance = 0f });
        var foe = new Dictionary<long, Vector3>();
        var mine = new List<DodgeBegun>();
        for (int i = 0; i < 6 * World.TicksPerSecond; i++)
        {
            MovementServices.Step(bout.World, ActionFrame.Idle);
            foreach (var d in bout.World.Events<DodgeBegun>().ThisTick)
                if (d.Entity == bout.A) { mine.Add(d); foe[d.Tick] = bout.World.Get<Body>(bout.B).Feet; }
        }
        Assert.NotEmpty(mine);
        foreach (var d in mine)
        {
            var toward = Vector2.Normalize(new Vector2(foe[d.Tick].X - d.Feet.X, foe[d.Tick].Y - d.Feet.Y));
            Assert.True(MathF.Abs(Vector2.Dot(d.Way, toward)) < 0.1f, $"the way {d.Way}: not to a side of the guard ({toward})");
        }

        // DodgeChance 0 (the default): no dodge asked, no draw from the dodge's stream, and the same duel whatever its shares.
        var plain = Make(BrainKind.Duel, BrainKind.Duel, seed: 5);
        var shares = Make(BrainKind.Duel, BrainKind.Duel, new DuelBrainSettings { DodgeBackShare = 0.1f, DodgeSideShare = 3f, DodgeWaitSeconds = 2f },
                          new DuelBrainSettings { DodgeBackShare = 1f, DodgeSideShare = 0f }, seed: 5);
        var (none, _, _) = RunDodges(plain, 6f);
        RunDodges(shares, 6f);
        Assert.Empty(none);
        Assert.Equal(0, plain.World.Store<DodgeIntent>().Count);
        Assert.Equal(plain.World.Digest(), shares.World.Digest());
        Assert.Equal(plain.Begun, shares.Begun);
    }

    [Fact]
    public void Timed_to_the_blow_its_dodge_begins_later_than_reacting_and_after_the_reaction_still()
    {
        static List<(long Dodge, long Swing)> Delays(bool atStrike)
        {
            var dodger = new DuelBrainSettings { DodgeChance = 1f, StrikeShare = 0.2f, DodgeAtStrike = atStrike };
            var bout = Make(BrainKind.Duel, BrainKind.Duel, dodger, new DuelBrainSettings { BlockChance = 0f });
            var (begun, _, _) = RunDodges(bout, 8f);
            var guard = bout.Begun.Where(b => b.Entity == bout.B).Select(b => b.Tick).ToList();
            return [.. begun.Where(d => d.Entity == bout.A).Select(d => (d.Tick, guard.Where(g => g <= d.Tick).Max()))];
        }
        var react = Delays(false);
        var timed = Delays(true);
        Assert.NotEmpty(react);
        Assert.NotEmpty(timed);
        int reaction = (int)MathF.Round(0.15f * World.TicksPerSecond);
        Assert.All(react, x => Assert.InRange(x.Dodge - x.Swing, reaction - 1, reaction + 1));
        Assert.All(timed, x => Assert.True(x.Dodge - x.Swing >= reaction - 1, $"dodged {x.Dodge - x.Swing} ticks after the blow began"));
        Assert.True(timed.Average(x => x.Dodge - x.Swing) > react.Average(x => x.Dodge - x.Swing));
    }

    // ---- the quick slots ----

    /// <summary>
    /// A stand-in for the module's powers (Act/1): the first fighter's slots show a power in each of <see cref="Shown"/>; a
    /// press of one makes it live for <see cref="Seconds"/> (a press while live renews it, 1.1's cast) with a recast; it
    /// records each press with the time that was left, publishes <see cref="PowerUsed"/>, clears the intent, then writes the
    /// views the brain reads next tick.
    /// </summary>
    sealed class StandInPowers : ISystem
    {
        public EntityId Who;
        public int[] Shown = [1];
        public float Seconds = 30f, Recast = 1f;
        public float Tempo = 1f;   // the power's own tempo the views show while live
        public bool WriteViews = true;
        /// <summary>The slots shown for use when hurt (a heal: instant, no live window, its recast only; a press adds <see cref="Heal"/> Health), and the Health the vitals show (none written while <see cref="HealthMax"/> is 0).</summary>
        public int[] HurtSlots = [];
        public float Health, HealthMax, Heal = 20f;
        public readonly List<(long Tick, int Slot, float Left)> Presses = [];
        readonly long[] liveUntil = [-1, -1, -1, -1, -1, -1, -1, -1];
        readonly long[] recastUntil = [-1, -1, -1, -1, -1, -1, -1, -1];

        public void Run(World world)
        {
            if (Who.IsNone || !WriteViews) return;
            long now = world.Tick;
            if (world.TryStore<PowerIntent>(out var intents) && intents.Has(Who))
            {
                ref var intent = ref intents.Get(Who);
                int slot = intent.Slot;
                intent.Slot = 0;
                if (slot >= 1 && Array.IndexOf(Shown, slot) >= 0 && Array.IndexOf(HurtSlots, slot) >= 0 && recastUntil[slot - 1] <= now)
                {
                    Presses.Add((now, slot, 0f));
                    recastUntil[slot - 1] = now + (long)(Recast * World.TicksPerSecond);
                    Health = MathF.Min(HealthMax, Health + Heal);
                }
                else if (slot >= 1 && Array.IndexOf(Shown, slot) >= 0 && recastUntil[slot - 1] <= now)
                {
                    bool live = liveUntil[slot - 1] > now;
                    Presses.Add((now, slot, live ? (liveUntil[slot - 1] - now) / (float)World.TicksPerSecond : 0f));
                    liveUntil[slot - 1] = now + (long)(Seconds * World.TicksPerSecond);
                    recastUntil[slot - 1] = now + (long)(Recast * World.TicksPerSecond);
                    world.Events<PowerUsed>().Publish(new PowerUsed(Who, "celerity.active", 1, slot, now, Presses.Count, 30, 100, 70, Seconds, live));
                }
            }
            var views = new PowerSlots();
            for (int i = 0; i < QuickSlots.Count; i++)
            {
                if (Array.IndexOf(Shown, i + 1) < 0) { views.Slots[i] = SlotView.None; continue; }
                bool live = liveUntil[i] > now;
                views.Slots[i] = new SlotView
                {
                    Power = 0, State = live ? SlotState.Live : recastUntil[i] > now ? SlotState.Recast : SlotState.Ready,
                    SecondsLeft = live ? (liveUntil[i] - now) / (float)World.TicksPerSecond : 0f, SecondsTotal = Seconds,
                    RecastLeft = MathF.Max(0f, (recastUntil[i] - now) / (float)World.TicksPerSecond), Cost = 30, Tempo = live ? Tempo : 1f,
                    Use = Array.IndexOf(HurtSlots, i + 1) >= 0 ? SlotUse.Hurt : SlotUse.Engage,
                };
            }
            world.Set(Who, views);
            if (HealthMax > 0f) world.Set(Who, new Vitals { Health = Health, HealthMax = HealthMax, Blood = 100, BloodMax = 100, HasBlood = true });
        }
    }

    static (Bout Bout, StandInPowers Powers) WithPowers(DuelBrainSettings? numbers = null, StandInPowers? stand = null, ulong seed = 11)
    {
        stand ??= new StandInPowers();
        var bout = Make(BrainKind.Duel, BrainKind.Stand, numbers ?? new DuelBrainSettings { BlockChance = 0f, BackOffChance = 0f }, seed: seed, register: b =>
        {
            PowerContract.Register(b);
            b.System(Phase.Act, 1, "test.powers-stand-in", stand);
        });
        stand.Who = bout.A;
        return (bout, stand);
    }

    [Fact]
    public void It_uses_a_ready_slot_once_engaged_and_not_when_far_or_while_live()
    {
        var (bout, powers) = WithPowers();
        bout.Run(6f);
        float reach = bout.Brain(bout.A).Reach, within = reach * new DuelBrainSettings().PowerShare;
        var press = Assert.Single(powers.Presses);                          // once: a 30 s power stays live, nothing renewed
        Assert.Equal(1, press.Slot);
        Assert.Equal(0f, press.Left);
        // Not when far: every tick before the press the foe was beyond 1.5 x the reach; the press came once within it.
        int at = (int)press.Tick;
        Assert.All(bout.Distance.Take(at - 1), d => Assert.True(d > within - 0.05f, $"{d} m"));
        Assert.True(bout.Distance[at - 1] <= within + 0.05f, $"{bout.Distance[at - 1]} m, within {within} m");
        Assert.True(bout.Distance[0] > within, "they started apart");
        var state = bout.World.Get<DuelBrainPowers>(bout.A);
        Assert.Equal((1, 0), (state.Presses, state.Renewals));
        Assert.Equal(0, bout.World.Get<PowerIntent>(bout.A).Slot);         // a press is for one tick
        Assert.False(bout.World.Store<DuelBrainPowers>().Has(bout.B));    // a standing dummy presses nothing
        Assert.True(bout.Begun.Count > 0, "it still fights");
    }

    [Fact]
    public void While_a_power_of_its_own_is_live_it_backs_off_for_the_usual_time_over_its_tempo_and_not_otherwise()
    {
        // The brain keeps its rhythm to its body's tempo only while a power of its own is live (PaceWithPowers); by
        // the tempo the live power itself gives (the views' Tempo), not the whole body tempo.
        float Longest(bool live, bool pace)
        {
            var numbers = new DuelBrainSettings { BlockChance = 0f, BackOffChance = 1f, StrongShare = 0f, PaceWithPowers = pace };
            var stand = new StandInPowers { WriteViews = true, Shown = live ? [1] : [], Tempo = 4f };
            var bout = Make(BrainKind.Duel, BrainKind.Stand, numbers, register: b =>
            {
                PowerContract.Register(b);
                CombatTempos.Register(b);
                b.System(Phase.Act, 1, "test.powers-stand-in", stand);
            });
            stand.Who = bout.A;
            bout.World.Set(bout.A, new CombatTempo(4f, 4f, 4f));
            float longest = 0f;
            for (int i = 0; i < 4 * World.TicksPerSecond; i++)
            {
                bout.Run(World.TickSeconds);
                longest = MathF.Max(longest, bout.Brain(bout.A).BackOffLeft);
            }
            Assert.True(bout.Brain(bout.A).BackOffs > 0);
            return longest;
        }
        Assert.InRange(Longest(live: true, pace: true), 0.5f / 4f - 0.02f, 0.5f / 4f + 0.01f);    // live: 0.5 s over the tempo
        Assert.InRange(Longest(live: false, pace: true), 0.48f, 0.51f);                         // nothing live: real time
        Assert.InRange(Longest(live: true, pace: false), 0.48f, 0.51f);                         // switched off: real time
    }

    [Fact]
    public void It_heals_when_its_Health_is_below_its_share_engaged_or_not_and_never_when_whole()
    {
        // Whole (and at exactly half): the heal is never pressed, engaged or not; the slot for a fight is.
        var (bout, powers) = WithPowers(stand: new StandInPowers { Shown = [1, 2], HurtSlots = [2], Health = 100f, HealthMax = 100f });
        bout.Run(3f);
        Assert.Equal(1, Assert.Single(powers.Presses).Slot);
        powers.Health = 50f;                                                   // not below 0.5 of 100
        bout.Run(2f);
        Assert.DoesNotContain(powers.Presses, p => p.Slot == 2);

        // Hurt: pressed at once (the press wait passed); again after its recast while still below half; then no more.
        powers.Health = 10f;
        long hurtAt = bout.World.Tick;
        bout.Run(3f);
        var heals = powers.Presses.Where(p => p.Slot == 2).ToList();
        Assert.Equal(2, heals.Count);                                          // 10 → 30 → 50, which is not below half: stop
        Assert.InRange(heals[0].Tick - hurtAt, 0, 3);
        Assert.True(heals[1].Tick - heals[0].Tick >= World.TicksPerSecond, "the second waits for the recast");
        Assert.Equal(50f, powers.Health);

        // Hurt from the start and far from the foe: a heal does not wait to be engaged; a slot for a fight does.
        var (far, farPowers) = WithPowers(stand: new StandInPowers { Shown = [1, 2], HurtSlots = [2], Health = 10f, HealthMax = 100f, Heal = 100f });
        far.Run(0.1f);
        var first = Assert.Single(farPowers.Presses);
        Assert.Equal(2, first.Slot);
        Assert.InRange(first.Tick, 0, 3);
        float within = far.Brain(far.A).Reach * new DuelBrainSettings().PowerShare;
        Assert.All(far.Distance, d => Assert.True(d > within, $"{d} m: the heal came before it was engaged"));

        // healBelow 0: never.
        var (never, neverPowers) = WithPowers(new DuelBrainSettings { BlockChance = 0f, BackOffChance = 0f, HealBelow = 0f },
            new StandInPowers { Shown = [2], HurtSlots = [2], Health = 10f, HealthMax = 100f });
        never.Run(4f);
        Assert.Empty(neverPowers.Presses);
    }

    [Fact]
    public void It_renews_a_live_power_when_its_time_left_falls_under_the_margin()
    {
        var (bout, powers) = WithPowers(stand: new StandInPowers { Seconds = 2f, Recast = 0.25f });
        bout.Run(8f);
        Assert.True(powers.Presses.Count >= 5, $"{powers.Presses.Count} presses");
        Assert.Equal(0f, powers.Presses[0].Left);                           // the first: from Ready
        foreach (var p in powers.Presses.Skip(1))
            Assert.InRange(p.Left, 0.01f, 1f);                               // each renewal under the margin (1 s), before it ran out
        var state = bout.World.Get<DuelBrainPowers>(bout.A);
        Assert.Equal((powers.Presses.Count, powers.Presses.Count - 1), (state.Presses, state.Renewals));
        // A wider margin renews sooner.
        var (early, earlyPowers) = WithPowers(new DuelBrainSettings { BlockChance = 0f, BackOffChance = 0f, RenewSeconds = 1.5f }, new StandInPowers { Seconds = 2f, Recast = 0.25f });
        early.Run(8f);
        Assert.True(earlyPowers.Presses.Count > powers.Presses.Count, $"{earlyPowers.Presses.Count} against {powers.Presses.Count}");
        Assert.All(earlyPowers.Presses.Skip(1), p => Assert.InRange(p.Left, 1f, 1.5f));
    }

    [Fact]
    public void It_presses_only_the_slots_it_may_use()
    {
        var (bout, powers) = WithPowers(new DuelBrainSettings { BlockChance = 0f, BackOffChance = 0f, PowerSlots = 1 << 1 }, new StandInPowers { Shown = [1, 2] });
        bout.Run(5f);
        var press = Assert.Single(powers.Presses);
        Assert.Equal(2, press.Slot);
        // Both may be used: the first in slot order, then the other once the wait is over.
        var (both, bothPowers) = WithPowers(stand: new StandInPowers { Shown = [1, 2] });
        both.Run(5f);
        Assert.Equal([1, 2], bothPowers.Presses.Select(p => p.Slot));
        Assert.True(bothPowers.Presses[1].Tick - bothPowers.Presses[0].Tick >= (long)(new DuelBrainSettings().PowerWaitSeconds * World.TicksPerSecond));
    }

    [Fact]
    public void Without_views_or_with_no_slot_to_use_the_brain_does_nothing_digest_for_digest()
    {
        // The powers' contract in the world, and no view written: the brain adds nothing and presses nothing.
        var (none, nonePowers) = WithPowers(stand: new StandInPowers { WriteViews = false });
        none.Run(4f);
        Assert.False(none.World.Store<PowerIntent>().Has(none.A));
        Assert.False(none.World.Store<DuelBrainPowers>().Has(none.A));
        // Views written, but no slot it may use ("slots": []): the same world, digest for digest.
        var (unused, unusedPowers) = WithPowers(new DuelBrainSettings { BlockChance = 0f, BackOffChance = 0f, PowerSlots = 0 });
        unused.Run(4f);
        Assert.Empty(unusedPowers.Presses);
        Assert.Equal(none.World.Digest(), unused.World.Digest());
        Assert.Equal(none.Begun, unused.Begun);
        // Used, it is another world.
        var (used, _) = WithPowers();
        used.Run(4f);
        Assert.NotEqual(none.World.Digest(), used.World.Digest());
    }
}
