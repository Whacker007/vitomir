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
/// 1.1's creatures as 1.1 has them: a 1.1 template's walk and run and its AI file's numbers read from the install
/// (<see cref="OneOneCreature"/>), and the duelling brain's approach <c>oneone</c> (<see cref="DuelOneOnes"/>): a szlachta at its own
/// speeds closing on a standing Christof from 15 m across the proving ground's free yard in steps turned within ±30° of the line to him,
/// each at least the floor and at most its chaseDist or the gap, standing when a step ends before its next think, and fighting as the
/// duelling brain once he is within its striking range; without the approach's store the brain is the duel's, digest for digest.
/// Skips without the 1.1 install (VTMR_GAME_DIR) or the proving ground (VITOMIR_COURSE_LEVEL).
/// </summary>
public sealed class DuelApproachTests
{
    static readonly Lazy<OneOneLibrary?> Library = new(() => GameInstall.TryOpen() is { } i ? new OneOneLibrary(i) : null);

    static readonly Lazy<LevelSpace?> Course = new(() =>
    {
        var path = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        return path is not null && File.Exists(Path.Combine(path, "level.json")) ? LevelSpace.Open(path) : null;
    });

    static readonly Lazy<(OneOneCharacter Christof, OneOneCharacter Szlachta)?> Characters = new(() =>
        Library.Value is { } l && l.Character("Christof", "BroadSword") is { } c && l.Character("Szlachta") is { } s ? (c, s) : null);

    static OneOneCreature Szlachta()
    {
        Assert.SkipUnless(Library.Value is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        return OneOneCreature.Read(Library.Value!.Templates, Library.Value.Install, "Szlachta") ?? throw new Xunit.Sdk.XunitException("Szlachta has no speeds");
    }

    sealed record Approach(World World, EntityId Christof, EntityId Szlachta, DuelBrainSettings Numbers);

    /// <summary>Christof standing at 24 9, the szlachta at 39 9 (15 m) facing him, its brain the duel's with <paramref name="approach"/>.</summary>
    static Approach Make(DuelApproach? approach, bool register, ulong seed = 5, bool templateSpeeds = true)
    {
        Assert.SkipUnless(Course.Value is not null, "no proving ground (VITOMIR_COURSE_LEVEL: a cooked proving-ground.l2)");
        Assert.SkipUnless(Characters.Value is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        var creature = Szlachta();
        var ai = creature.Ai ?? throw new Xunit.Sdk.XunitException("Szlachta has no AI file");
        var assets = new CharacterAssets();
        int christof = OneOneLibrary.Register(assets, Characters.Value!.Value.Christof);
        int szlachta = OneOneLibrary.Register(assets, Characters.Value!.Value.Szlachta);
        var b = new WorldBuilder(seed);
        MovementServices.Register(b, Course.Value!);
        MasqueradeServices.Register(b);
        b.Animation(assets);
        BodyMotionSystem.Register(b);
        CombatServices.Register(b);
        DuelBrains.Register(b);
        if (register) DuelOneOnes.Register(b);
        var w = b.Build();
        var own = templateSpeeds ? Locomotion.Sandbox with { WalkSpeed = creature.WalkMetresPerSecond, RunSpeed = creature.RunMetresPerSecond, SprintSpeed = creature.RunMetresPerSecond } : Locomotion.Sandbox;
        var ec = MovementServices.SpawnBody(w, new(24f, 9f, 0.02f), 0f, Locomotion.Sandbox);
        var es = MovementServices.SpawnBody(w, new(39f, 9f, 0.02f), DetMath.Pi, own);
        foreach (var (e, k) in new[] { (ec, christof), (es, szlachta) })
        {
            w.Set(e, CharacterAnimation.For(assets, k));
            w.Set(e, MotionInput.Standing);
            CombatServices.Arm(w, e);
        }
        var book = w.Service<DuelBrainBook>();
        var numbers = new DuelBrainSettings { Approach = approach, OneOneMoveSeconds = ai.MoveSeconds, OneOneStepMostMetres = ai.ChaseMetres, OneOneRuns = ai.ClosesAtRun };
        float rc = DuelBrains.ReachOf(assets, christof), rs = DuelBrains.ReachOf(assets, szlachta);
        w.Set(ec, DuelBrain.For(BrainKind.Stand, es, book.Add(new DuelBrainSettings()), rc, rs));
        w.Set(es, DuelBrain.For(BrainKind.Duel, ec, book.Add(numbers), rs, rc));
        return new Approach(w, ec, es, numbers);
    }

    static Vector2 Feet(World w, EntityId e) => new(w.Get<Body>(e).Feet.X, w.Get<Body>(e).Feet.Y);

    [Fact]
    public void A_template_s_walk_run_and_AI_file_are_read_from_the_install()
    {
        var c = Szlachta();
        var tags = Library.Value!.Templates.Resolve("Szlachta");
        // The template's own tags at 72 units to the metre, nothing of ours.
        Assert.Equal(tags[OneOneCreature.WalkSpeedTag].AsFloat() / OneOneModels.UnitsPerMetre, c.WalkMetresPerSecond, 4);
        Assert.Equal(tags[OneOneCreature.RunSpeedTag].AsFloat() / OneOneModels.UnitsPerMetre, c.RunMetresPerSecond, 4);
        Assert.Equal(tags[OneOneCreature.AiTag].AsString(), c.AiFile);
        // Slower than the human profile's walk (1.3) and run (3.0): they move much more slowly.
        Assert.True(c.WalkMetresPerSecond < 1.3f && c.RunMetresPerSecond < 3f, $"walk {c.WalkMetresPerSecond}, run {c.RunMetresPerSecond}");
        Assert.True(c.WalkMetresPerSecond < c.RunMetresPerSecond);
        var ai = c.Ai!;
        Assert.True(ai.MoveSeconds > 0f && ai.AttackSeconds > 0f && ai.ChaseMetres > DuelOneOnes.FloorMetres, $"{ai}");
        // A template that is not there reads as nothing.
        Assert.Null(OneOneCreature.Read(Library.Value.Templates, Library.Value.Install, "NoSuchTemplate"));
    }

    /// <summary>
    /// Its walk clip at its 1.1 run (<c>SZLACHTA.NMS</c> plays its walk for its run): its motion set marks no feet, so no stride is
    /// measured and the platform plays the clip at its own rate at any speed — as 1.1 itself plays every locomotion cycle (a flat 30
    /// frames a second; scaling by speed is the old engine's fix, not 1.1's). The pace the clip was drawn for, from its two lowest
    /// bones' sweep (ours), and the feet's slide at its walk and run are written to <c>VITOMIR_SLIDE_OUT</c> when asked.
    /// </summary>
    [Fact]
    public void Its_walk_clip_plays_at_its_own_rate_as_1_1_plays_it()
    {
        var c = Szlachta();
        Assert.SkipUnless(Characters.Value is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        var szlachta = Characters.Value!.Value.Szlachta;
        var walk = szlachta.Roles[(int)MotionRole.Walk];
        var run = szlachta.Roles[(int)MotionRole.Run];
        Assert.NotNull(walk.Clip);
        Assert.Same(walk.Clip, run.Clip);                       // its run is its walk clip
        Assert.Equal(0f, run.NaturalSpeed);                     // no feet marked: no stride
        Assert.Equal(1f, Stride.RateFor(c.RunMetresPerSecond, run.NaturalSpeed));   // played at its own rate
        if (Environment.GetEnvironmentVariable("VITOMIR_SLIDE_OUT") is { } o)
        {
            var skeleton = szlachta.Model.Skeleton;
            var rest = Enumerable.Range(0, skeleton.Count).Select(b => Matrix4x4.Invert(skeleton.InverseBind[b], out var m) ? m.Translation : Vector3.Zero).ToArray();
            int a = Enumerable.Range(0, skeleton.Count).OrderBy(b => rest[b].Z).First();
            int other = Enumerable.Range(0, skeleton.Count).Where(b => new Vector2(rest[b].X - rest[a].X, rest[b].Y - rest[a].Y).Length() > 0.12f).OrderBy(b => rest[b].Z).First();
            float? stride = Stride.Sweep(walk.Clip!, skeleton, a, other);
            float drawn = stride is { } st ? Stride.NaturalSpeed(st, walk.Clip!.Duration) : 0f;
            File.WriteAllText(o, FormattableString.Invariant(
                $"feet {skeleton.Bones[a].Name}, {skeleton.Bones[other].Name}; clip {walk.Clip!.Duration:0.###} s, stride {stride ?? 0f:0.###} m, drawn for {drawn:0.###} m/s; at its walk {c.WalkMetresPerSecond:0.###} the feet slide {(drawn > 0 ? 1f - drawn / c.WalkMetresPerSecond : 0f):0.###} of it, at its run {c.RunMetresPerSecond:0.###} {(drawn > 0 ? 1f - drawn / c.RunMetresPerSecond : 0f):0.###}\n"));
        }
    }

    [Fact]
    public void Each_step_keeps_within_its_bounds_and_a_short_one_ends_in_a_stand()
    {
        int steps = 0, ended = 0, stoodTicks = 0;
        for (ulong seed = 1; seed <= 3; seed++)
        {
            var a = Make(DuelApproach.OneOne, register: true, seed);
            var w = a.World;
            var oneOnes = w.Store<DuelOneOne>();
            int seen = 0;
            float most = 0f;
            float strike = w.Get<DuelBrain>(a.Szlachta).Reach * a.Numbers.StrikeShare;
            for (int tick = 0; tick < 8 * World.TicksPerSecond; tick++)
            {
                var before = Feet(w, a.Szlachta);
                var foe = Feet(w, a.Christof);
                MovementServices.Step(w, ActionFrame.Idle);
                if (Vector2.Distance(before, foe) <= strike + 0.05f) break;   // within its striking range: the duel's
                ref var st = ref oneOnes.Find(a.Szlachta);
                Assert.False(System.Runtime.CompilerServices.Unsafe.IsNullRef(ref st));
                var v = w.Get<Body>(a.Szlachta).Velocity;
                float speed = new Vector2(v.X, v.Y).Length();
                most = MathF.Max(most, speed);
                if (speed < 0.05f && tick > 20) stoodTicks++;
                if (st.Steps == seen) continue;
                seen = st.Steps;
                steps++;
                // The step just chosen, from where the feet were at its think.
                var step = st.Spot - before;
                float length = step.Length();
                float gap = Vector2.Distance(before, foe) - 0.6f;
                Assert.InRange(length, DuelOneOnes.FloorMetres - 1e-3f, MathF.Max(DuelOneOnes.FloorMetres, MathF.Min(a.Numbers.OneOneStepMostMetres, gap)) + 1e-3f);
                var line = Vector2.Normalize(foe - before);
                float angle = MathF.Acos(Math.Clamp(Vector2.Dot(line, step / length), -1f, 1f)) * 180f / MathF.PI;
                Assert.True(angle <= DuelOneOnes.ArcDegrees / 2f + 0.5f, $"a step {angle:0.0}° off the line to its foe");
            }
            ended += oneOnes.Find(a.Szlachta).StepsEnded;
            // At its run (its 1.1 template's), never faster.
            float run = Szlachta().RunMetresPerSecond;
            Assert.InRange(most, 0.9f * run, run + 0.02f);
        }
        Assert.True(steps >= 12, $"only {steps} steps in three closings");
        Assert.True(ended > 0 && stoodTicks > 0, $"no step ended before its think ({ended}) or it never stood ({stoodTicks} ticks)");
    }

    [Fact]
    public void Within_its_striking_range_it_fights_as_the_duelling_brain()
    {
        var a = Make(DuelApproach.OneOne, register: true);
        var begun = new List<AttackBegun>();
        for (int tick = 0; tick < 20 * World.TicksPerSecond && begun.Count == 0; tick++)
        {
            MovementServices.Step(a.World, ActionFrame.Idle);
            begun.AddRange(a.World.Events<AttackBegun>().ThisTick.Where(x => x.Entity == a.Szlachta));
        }
        Assert.NotEmpty(begun);
        float d = Vector2.Distance(Feet(a.World, a.Szlachta), Feet(a.World, a.Christof));
        Assert.True(d <= a.World.Get<DuelBrain>(a.Szlachta).Reach * a.Numbers.StrikeShare + 0.05f, $"its first blow began {d:0.00} m off");
    }

    [Fact]
    public void Without_its_store_the_approach_is_the_duel_s_digest_for_digest()
    {
        // The store is registered only where a brain uses the approach (DuelOneOnes.Register); without it the settings' approach does nothing.
        var duel = Make(null, register: false);
        var asked = Make(DuelApproach.OneOne, register: false);
        for (int tick = 0; tick < 6 * World.TicksPerSecond; tick++)
        {
            MovementServices.Step(duel.World, ActionFrame.Idle);
            MovementServices.Step(asked.World, ActionFrame.Idle);
        }
        Assert.Equal(duel.World.Digest(), asked.World.Digest());
        // And with it, the closing goes another way (its steps off the line to him: it leaves the straight line the duel's keeps).
        var oneOne = Make(DuelApproach.OneOne, register: true);
        float wander = 0f;
        for (int tick = 0; tick < 6 * World.TicksPerSecond; tick++)
        {
            MovementServices.Step(oneOne.World, ActionFrame.Idle);
            wander = MathF.Max(wander, MathF.Abs(Feet(oneOne.World, oneOne.Szlachta).Y - 9f));
        }
        Assert.NotEqual(duel.World.Digest(), oneOne.World.Digest());
        Assert.True(wander > 0.05f, $"its path kept to the line ({wander:0.000} m off it)");
        Assert.True(MathF.Abs(Feet(duel.World, duel.Szlachta).Y - 9f) < 0.01f);
    }
}
