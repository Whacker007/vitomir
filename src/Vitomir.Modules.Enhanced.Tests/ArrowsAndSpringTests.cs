// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using Vitomir.Modules.Enhanced.Rules;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Modules.Enhanced.Tests;

/// <summary>
/// The enhanced module's side of the spring and of the items set's arrows. Every
/// movement profile gives <c>StartStopHalfLife</c> (the human 0.2 s, the neonate 0.15, the elder 0.1), refused by name when missing
/// or above a second; the module registers the spring unless <c>accel-ramp</c>; the war bow looses the items set's war arrow
/// (54.5 g at 58.2 m/s) and, with <c>arrows-heavy</c>, the heavy war arrow (87.5 g at 53.3 m/s); the drop at 10, 20, 30 m and the harm of
/// each.
/// </summary>
public sealed class ArrowsAndSpringTests(ITestOutputHelper output)
{
    [Fact]
    public void Every_profile_gives_its_half_life_and_the_masquerade_holds_it_to_the_human_s()
    {
        var m = TestData.Sandbox();
        Assert.Equal((0.2f, 0.15f, 0.1f), (m.Movement.Single(p => p.Name == "human").HalfLife, m.Movement.Single(p => p.Name == "neonate").HalfLife,
                                           m.Movement.Single(p => p.Name == "elder").HalfLife));
        var (w, _, _) = World();
        var profiles = w.Service<MovementProfiles>();
        int elder = profiles.IndexOf("elder");
        Assert.Equal(0.1f, profiles.HalfLife(elder, MasqueradeMode.Unbound));
        Assert.Equal(0.2f, profiles.HalfLife(elder, MasqueradeMode.Masquerade));
    }

    [Fact]
    public void A_profile_without_its_half_life_or_with_one_above_a_second_is_refused_by_name()
    {
        using var copy = new TestData.Copy();
        copy.Edit("movement/neonate.json", s => s.Replace("\"StartStopHalfLife\": 0.15,\n", ""));
        copy.Edit("movement/elder.json", s => s.Replace("\"StartStopHalfLife\": 0.1,", "\"StartStopHalfLife\": 2,"));
        var refused = Assert.Throws<ModuleRefusedException>(() => TestData.Sandbox(copy.Folder));
        Assert.Contains(refused.Reasons, r => r.Contains("enhanced:movement/neonate: numbers: 'StartStopHalfLife' is missing", StringComparison.Ordinal));
        Assert.Contains(refused.Reasons, r => r.Contains("enhanced:movement/elder: numbers: 'StartStopHalfLife' is 2; a half-life above 1 s", StringComparison.Ordinal));
    }

    static (World, EnhancedModule, MovementProfiles) World(params string[] switches) => World(false, switches);

    /// <summary>A world of the module (and, with <paramref name="player"/>, a player on the spring, as the program gives play one).</summary>
    static (World, EnhancedModule, MovementProfiles) World(bool player, params string[] switches)
    {
        var m = TestData.Sandbox(null, switches);
        var b = new WorldBuilder(1);
        MovementServices.Register(b, new Platform.Space.LevelSpace("none", Platform.Space.CollisionWorld.FromTriangles([], [])));
        MasqueradeServices.Register(b);
        m.Register(b);
        if (player) StartStopSprings.RegisterPlayer(b);
        var w = b.Build();
        return (w, m, w.Service<MovementProfiles>());
    }

    [Fact]
    public void The_module_gives_the_spring_s_numbers_unless_accel_ramp_and_only_the_player_carries_one()
    {
        var (sprung, _, _) = World();
        Assert.True(sprung.TryService<StartStopSprings>(out var numbers));
        Assert.Equal(0.2f, numbers.Default);
        Assert.False(StartStopSprings.In(sprung));                        // the numbers alone: no store (an arena's bout is the earlier build's)
        var (withPlayer, _, _) = World(player: true);
        Assert.True(StartStopSprings.In(withPlayer));
        Assert.True(withPlayer.Has<StartStopSpring>(MovementServices.SpawnControlled(withPlayer, default, 0f, Locomotion.Sandbox)));
        Assert.False(withPlayer.Has<StartStopSpring>(MovementServices.SpawnBody(withPlayer, default, 0f, Locomotion.Sandbox)));   // any other body: no spring
        var (ramp, _, _) = World(player: true, EnhancedModule.AccelRampSwitch);
        Assert.False(ramp.TryService<StartStopSprings>(out _));
        Assert.False(StartStopSprings.In(ramp));
        // Landings keep only the speed along the way asked, unless landing-slide.
        Assert.False(numbers.LandingSlide);
        var (slides, _, _) = World(player: true, EnhancedModule.LandingSlideSwitch);
        Assert.True(slides.Service<StartStopSprings>().LandingSlide);
    }

    [Fact]
    public void The_war_bow_looses_the_items_stream_s_arrow_and_arrows_heavy_gives_the_heavy_one_with_the_drop_and_the_harm_of_each()
    {
        var items = Assert.Single(TestData.Sandbox().Ranged.Bows);
        var eh1 = Assert.Single(TestData.Sandbox(null, EnhancedModule.ArrowsHeavySwitch).Ranged.Bows);
        Assert.Equal((58.2f, 0.0545f, 533.8f), (items.SpeedAtFullDrawMetresPerSecond, items.ArrowMassKg, items.DrawWeightNewtons));
        Assert.Equal((53.34f, 0.08748f, 445f), (eh1.SpeedAtFullDrawMetresPerSecond, eh1.ArrowMassKg, eh1.DrawWeightNewtons));
        Assert.Equal(items.ArrowDragPerMetre, eh1.ArrowDragPerMetre);   // the drag kept (an open question: a lighter shaft of the same shape has more)
        var harm = TestData.Sandbox().Ranged.Harm;
        // The work at full draw at the muzzle: the items set's 92.3 J of the bow's 123.2 J stored (3.08 dice), the heavy arrow's 124.4 J (4.15).
        Assert.Equal(92.30, ArrowHarm.Work(items.ArrowMassKg, items.SpeedAt(1f)), 1);
        Assert.Equal(3.077, harm.Dice(items.ArrowMassKg, items.SpeedAt(1f)), 2);
        Assert.Equal(4.148, harm.Dice(eh1.ArrowMassKg, eh1.SpeedAt(1f)), 2);
        output.WriteLine("| fired level at full draw | 10 m | 20 m | 30 m | speed at 30 m | work at 30 m (dice) |");
        output.WriteLine("|---|---|---|---|---|---|");
        foreach (var (name, bow) in new[] { ("the heavy arrow (arrows-heavy): 87.5 g at 53.3 m/s", eh1), ("the items set's (the default): 54.5 g at 58.2 m/s", items) })
        {
            var drops = new float[3];
            for (int i = 0; i < 3; i++)
            {
                Assert.True(Ballistics.HeightAt(0f, bow.SpeedAt(1f), bow.ArrowDragPerMetre, 10f * (i + 1), out float h));
                drops[i] = -h;
            }
            // Speed at 30 m with the drag: v = v0·exp(−k·s) along the path (nearly level).
            float v30 = bow.SpeedAt(1f) * MathF.Exp(-bow.ArrowDragPerMetre * 30f);
            double work = ArrowHarm.Work(bow.ArrowMassKg, v30);
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"| {name} | {drops[0] * 100:0.0} cm | {drops[1] * 100:0.0} cm | {drops[2] * 100:0.0} cm | {v30:0.0} m/s | {work:0.0} J ({harm.Dice(bow.ArrowMassKg, v30):0.00}) |"));
            float reach = Ballistics.FlatReach(bow.SpeedAt(1f), bow.ArrowDragPerMetre, out float best);
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"|   its flat reach at full draw | {reach:0} m at {best * DetMath.RadToDeg:0.0} degrees | | | | |"));
            if (ReferenceEquals(bow, items))
            {
                Assert.InRange(drops[0], 0.140f, 0.150f);   // g·t²/2 with t = 10 / 58.2: 0.145 m
                Assert.InRange(drops[2], 1.30f, 1.36f);
            }
        }
        // The faster arrow drops less at every distance.
        for (int d = 10; d <= 30; d += 10)
        {
            Assert.True(Ballistics.HeightAt(0f, items.SpeedAt(1f), items.ArrowDragPerMetre, d, out float a));
            Assert.True(Ballistics.HeightAt(0f, eh1.SpeedAt(1f), eh1.ArrowDragPerMetre, d, out float b));
            Assert.True(-a < -b);
        }
    }

    [Fact]
    public void A_bow_s_arrows_eh1_with_a_number_it_does_not_know_is_refused()
    {
        using var copy = new TestData.Copy();
        copy.Edit("combat/ranged.json", s => s.Replace("\"arrow_mass_kg\": 0.08748,", "\"arrow_mass_kg\": 0.08748, \"nock_seconds\": 2,"));
        var refused = Assert.Throws<ModuleRefusedException>(() => TestData.Sandbox(copy.Folder));
        Assert.Contains(refused.Reasons, r => r.Contains("weapons.war_bow.arrows_heavy.nock_seconds is not one of", StringComparison.Ordinal));
    }
}
