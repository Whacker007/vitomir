// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The archer brain (<i>"some kind of "randomness" factor for the AI so they don't always shoot with
/// laser accuracy"</i>): it stands within its keep range and shoots — nocks, draws, holds, looses — its aim's error drawn as each arrow
/// is nocked (<see cref="ArcherAimed"/>) and put into where the bow points, the arrow leaving exactly along the bow (no spread drawn at
/// the loose: no hidden roll after the shot); farther than its keep range it comes on; up close it puts the bow away; the same seed
/// replays to the same digests. No game needed: bodies without characters (the aim falls back to 0.72 of the target's height).
/// </summary>
public sealed class ArcherBrainTests(ITestOutputHelper output)
{
    static int Ticks(float seconds) => (int)MathF.Round(seconds * World.TicksPerSecond);

    /// <summary>An archer at the origin facing +X and a target <paramref name="distance"/> m along +X; the bow's spread <paramref name="spread"/>° (as the module's by Range).</summary>
    static (World World, EntityId Archer, EntityId Target) Range(float distance, float spread = 1f, ulong seed = 3, ArcherBrainSettings? settings = null)
    {
        var level = new TestLevel().Floor(-60, -40, 80, 40).Build();
        var b = new WorldBuilder(seed);
        MovementServices.Register(b, level);
        b.Component<CombatIntent>();
        var config = new BowConfig();
        config.Bows.Add(BowTests.WarBow());
        config.SpreadDegrees = (_, _) => spread;
        BowServices.Register(b, config, BowExtras.None);
        ArcherBrains.Register(b);
        var w = b.Build();
        var archer = MovementServices.SpawnBody(w, new(0, 0, 0.01f), 0f, Locomotion.Sandbox);
        var target = MovementServices.SpawnBody(w, new(distance, 0, 0.01f), MathF.PI, Locomotion.Sandbox);
        w.Set(archer, new CombatIntent());
        Assert.True(BowServices.Give(w, archer, "war_bow", 12));
        w.Set(archer, ArcherBrain.For(target, w.Service<ArcherBrainBook>().Add(settings ?? new ArcherBrainSettings()), 0, 1f, 1f));
        return (w, archer, target);
    }

    static void Run(World w, int ticks, List<ArrowLoosed>? loosed = null, List<ArcherAimed>? aimed = null, List<ArcherToSword>? sword = null, List<ulong>? digests = null)
    {
        for (int i = 0; i < ticks; i++)
        {
            MovementServices.Step(w, ActionFrame.Idle);
            loosed?.AddRange(w.Events<ArrowLoosed>().ThisTick);
            aimed?.AddRange(w.Events<ArcherAimed>().ThisTick);
            sword?.AddRange(w.Events<ArcherToSword>().ThisTick);
            digests?.Add(w.Digest());
        }
    }

    [Fact]
    public void It_shoots_with_its_error_in_where_the_bow_points_and_no_spread_at_the_loose()
    {
        var (w, archer, target) = Range(20f, spread: 1f);
        var loosed = new List<ArrowLoosed>();
        var aimed = new List<ArcherAimed>();
        Run(w, Ticks(12f), loosed, aimed);
        output.WriteLine($"{loosed.Count} loosed in 12 s; errors {string.Join(", ", aimed.Select(a => $"{a.ErrorYawDegrees:+0.00;-0.00}/{a.ErrorPitchDegrees:+0.00;-0.00}"))}");
        Assert.InRange(loosed.Count, 4, 7);                                    // a shot about every 2.1 s: nock 0.6, draw 1.0, hold 0.5
        Assert.All(loosed, l => Assert.Equal(0f, l.SpreadDegrees));          // the arrow leaves exactly along the bow
        Assert.All(loosed, l => Assert.Equal(1f, l.DrawShare, 3));
        Assert.True(aimed.Count >= loosed.Count);
        // Each loose points off the exact aim by its shot's error (no sway: ).
        var d = BowTests.WarBow();
        ref var body = ref w.Get<Body>(archer);
        var targetPoint = w.Get<Body>(target).Feet + new Vector3(0f, 0f, 0.72f * w.Get<Body>(target).CapsuleHeight(w.Get<Locomotion>(target)));
        var from = BowServices.ReleasePoint(w, archer, in body, 0f, d);
        float exactPitch = Ballistics.SolvePitch(from, targetPoint, d.SpeedAt(1f), d.ArrowDragPerMetre) * DetMath.RadToDeg;
        foreach (var l in loosed)
        {
            var a = aimed.Last(x => x.Shot == l.Shot);
            Assert.True(a.Tick < l.Tick);                                       // drawn before the shot, as the arrow was nocked
            Assert.InRange(l.YawDegrees - a.ErrorYawDegrees, -0.01f, 0.01f);
            Assert.InRange(l.PitchDegrees - exactPitch - a.ErrorPitchDegrees, -0.01f, 0.01f);
        }
        Assert.Contains(aimed, a => MathF.Abs(a.ErrorYawDegrees) > 0.2f);    // it errs (1 degree, one standard deviation)
        Assert.All(aimed, a => Assert.Equal(1f, a.SpreadDegrees, 3));
    }

    [Fact]
    public void A_perfect_archer_hits_where_it_aims_and_its_error_scales_with_its_numbers()
    {
        var (w, _, _) = Range(20f, spread: 1f, settings: new ArcherBrainSettings(ErrorScale: 0f));
        var aimed = new List<ArcherAimed>();
        Run(w, Ticks(5f), aimed: aimed);
        Assert.NotEmpty(aimed);
        Assert.All(aimed, a => { Assert.Equal(0f, a.ErrorYawDegrees); Assert.Equal(0f, a.ErrorPitchDegrees); });
    }

    [Fact]
    public void Farther_than_its_keep_range_it_comes_on_and_within_its_sword_range_it_puts_the_bow_away()
    {
        var (w, archer, _) = Range(30f, settings: new ArcherBrainSettings(KeepMetres: 20f, SwordMetres: 4f));
        Run(w, Ticks(0.5f));
        Assert.Equal(ArcherPhase.Closing, w.Get<ArcherBrain>(archer).Phase);
        Assert.True(w.Get<Body>(archer).Feet.X > 0.3f);                         // running toward it
        Run(w, Ticks(5f));
        Assert.Equal(ArcherPhase.Shooting, w.Get<ArcherBrain>(archer).Phase);  // within 20 m: it stands and shoots
        var (w2, archer2, _) = Range(3f);
        var sword = new List<ArcherToSword>();
        Run(w2, 2, sword: sword);
        Assert.Equal(ArcherPhase.Sword, w2.Get<ArcherBrain>(archer2).Phase);
        Assert.False(w2.Get<BowIntent>(archer2).Ranged);
        Assert.Single(sword);
    }

    [Fact]
    public void The_same_seed_replays_to_the_same_digests()
    {
        ulong[] Play()
        {
            var (w, _, _) = Range(15f, spread: 1.4f, seed: 11);
            var digests = new List<ulong>();
            Run(w, Ticks(6f), digests: digests);
            return [.. digests];
        }
        Assert.Equal(Play(), Play());
    }
}
