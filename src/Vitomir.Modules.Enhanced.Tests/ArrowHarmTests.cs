// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Text.Json;
using Vitomir.Modules.Enhanced.Rules;
using Vitomir.Modules.Enhanced.Sheets;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.OneOne;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Space;

namespace Vitomir.Modules.Enhanced.Tests;

/// <summary>
/// The bow's data and an arrow's harm (<c>Data/combat/ranged.json</c>, SANDBOX): the module fills the platform's
/// <see cref="BowConfig"/> only in a world with a bow, with the war bow's numbers as the file gives them; an arrow's contact on a body is
/// staged as a blow is, its potential the arrow's work (½ m v² over the data's joules a die), not the archer's Strength, with no contest
/// and no severance; on a shield it gives no harm and the trace says why. The world tests skip without the 1.1 install.
/// </summary>
public sealed class ArrowHarmTests
{
    static readonly Lazy<OneOneLibrary?> Library = new(() => GameInstall.TryOpen() is { } i ? new OneOneLibrary(i) : null);

    static LevelSpace Floor()
    {
        var v = new List<Vector3>();
        var ix = new List<int>();
        for (int x = -20; x < 20; x += 4)
            for (int y = -20; y < 20; y += 4)
            {
                int at = v.Count;
                v.Add(new(x, y, 0)); v.Add(new(x + 4, y, 0)); v.Add(new(x + 4, y + 4, 0)); v.Add(new(x, y + 4, 0));
                ix.AddRange([at, at + 1, at + 2, at, at + 2, at + 3]);
            }
        return new LevelSpace("floor", CollisionWorld.FromTriangles([.. v], [.. ix]));
    }

    [Fact]
    public void The_module_reads_the_war_bow_and_its_arrows_harm_as_the_file_gives_them()
    {
        var m = TestData.Sandbox();
        var bow = Assert.Single(m.Ranged.Bows);
        Assert.Equal(("war_bow", 58.2f, 1.0f, 0.0545f), (bow.Id, bow.SpeedAtFullDrawMetresPerSecond, bow.FullDrawSeconds, bow.ArrowMassKg));   // The items set's long bow's war arrow
        Assert.Equal(29.1f, bow.SpeedAt(0.5f), 3);
        var eh1 = Assert.Single(TestData.Sandbox(null, EnhancedModule.ArrowsHeavySwitch).Ranged.Bows);
        Assert.Equal(("war_bow", 53.34f, 1.0f, 0.08748f), (eh1.Id, eh1.SpeedAtFullDrawMetresPerSecond, eh1.FullDrawSeconds, eh1.ArrowMassKg));   // the heavy war arrow, 1,350 grains at 175 fps (arrows-heavy)
        Assert.Equal([1.3f, 1.2f, 1.1f, 1.0f, 0.9f, 0.8f], m.Ranged.DrawSecondsByRating);   // the draw's time by Range
        Assert.Equal(0.8f, m.Ranged.DrawSecondsAt(9));
        Assert.Equal("firearms", m.Ranged.SpreadTrait);   // the registry's Firearms, the module's "Range"
        Assert.Equal([2.0f, 1.4f, 1.0f, 0.7f, 0.5f, 0.35f], m.Ranged.SpreadByRating);
        Assert.Equal(0.35f, m.Ranged.SpreadAt(9));
        Assert.Equal(("arrow", 30f, 8f), (m.Ranged.Harm.Class, m.Ranged.Harm.JoulesPerDie, m.Ranged.Harm.DiceAtMost));
        // A 75 g arrow at 55 m/s: 113.4 J, 3.78 dice.
        Assert.Equal(113.4375, ArrowHarm.Work(0.075f, 55f), 3);
        Assert.Equal(3.78125, m.Ranged.Harm.Dice(0.075f, 55f), 3);
    }

    [Fact]
    public void A_level_full_draw_drops_as_the_war_arrow_table_says()
    {
        // A 1,350-grain war arrow at 175 fps fired level drops 5.7, 22.7, 51.1 and 90.8 in at 10, 20,
        // 30 and 40 yd (minimal drag). The module's war bow, flown a tick at a time as the arrow is, within 5 %. that arrow is the
        // switch arrows-heavy's (the items set's is ArrowsAndSpringTests').
        var bow = Assert.Single(TestData.Sandbox(null, EnhancedModule.ArrowsHeavySwitch).Ranged.Bows);
        double[] yards = [10, 20, 30, 40], inches = [5.7, 22.7, 51.1, 90.8];
        for (int i = 0; i < yards.Length; i++)
        {
            float metres = (float)(yards[i] * 0.9144), want = (float)(inches[i] * 0.0254);
            Assert.True(Ballistics.HeightAt(0f, bow.SpeedAt(1f), bow.ArrowDragPerMetre, metres, out float height));
            Assert.InRange(-height, want * 0.95f, want * 1.05f);
        }
        // How far a full draw reaches on the flat at its best angle (the stepping of PlayerBowTests' FlatReach): about 261 m at 44 degrees.
        Assert.InRange(Ballistics.FlatReach(bow.SpeedAt(1f), bow.ArrowDragPerMetre, out float best), 255f, 267f);
        Assert.InRange(best * DetMath.RadToDeg, 42f, 45.5f);
        // Its harm: the work at contact, 1/2 m v^2, at full draw at the muzzle: 124.4 J, 4.15 dice (the earlier build's 75 g at 55 m/s: 113.4 J, 3.78).
        var m = TestData.Sandbox(null, EnhancedModule.ArrowsHeavySwitch);
        Assert.Equal(124.45, ArrowHarm.Work(bow.ArrowMassKg, bow.SpeedAt(1f)), 1);
        Assert.Equal(4.148, m.Ranged.Harm.Dice(bow.ArrowMassKg, bow.SpeedAt(1f)), 2);
    }

    [Fact]
    public void The_module_reads_the_archer_brain_s_numbers_each_with_its_source()
    {
        // Ranged.json's archer_brain (ENGINE_SANDBOX).
        var m = TestData.Sandbox();
        Assert.Equal(new Vitomir.Platform.Combat.ArcherBrainSettings(20f, 4f, 0.5f, 1f), m.Ranged.Archer);
        // A number without its source, or out of its range, is refused.
        var problems = new List<string>();
        using var doc = System.Text.Json.JsonDocument.Parse("""
            { "schema": "VITOMIR_ENHANCED_RANGED", "status": "SANDBOX",
              "weapons": { "b": { "draw_weight_newtons": 1, "full_draw_seconds": 1, "speed_at_full_draw_metres_per_second": 50, "speed_exponent": 1,
                "least_share_to_loose": 0.2, "arrow_mass_kg": 0.07, "arrow_drag_per_metre": 0, "arrow_radius_metres": 0.005, "nock_seconds": 0.6,
                "raise_seconds": 0.25, "release_height_share": 0.8, "release_forward_metres": 0.45, "flight_seconds_at_most": 8, "fallen_below_metres": 30 } },
              "aim_spread": { "trait": "firearms", "degrees_by_rating": [1], "source": "x" },
              "archer_brain": { "keep_metres": 500, "sword_metres": 4, "hold_seconds": 0.5, "error_scale": 1,
                "sources": { "keep_metres": "x", "sword_metres": "x", "hold_seconds": "x" } } }
            """);
        Assert.Null(RangedData.Read(doc.RootElement, "test", problems));
        Assert.Contains(problems, p => p.Contains("archer_brain.keep_metres 500 is outside", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("archer_brain.sources.error_scale is missing", StringComparison.Ordinal));
    }

    [Fact]
    public void A_world_without_a_bow_is_given_no_bows_and_one_with_is_given_the_war_bow()
    {
        var m = TestData.Sandbox();
        var without = new WorldBuilder(3);
        MovementServices.Register(without, Floor());
        m.Register(without);
        Assert.False(without.Build().TryService<BowConfig>(out _));
        var with = new WorldBuilder(3);
        MovementServices.Register(with, Floor());
        BowServices.Register(with);
        m.Register(with);
        var w = with.Build();
        Assert.Equal("war_bow", Assert.Single(w.Service<BowConfig>().Bows).Id);
        Assert.NotNull(w.Service<BowConfig>().SpreadDegrees);
        Assert.True(w.TryService<ArrowHarmRules>(out _));
    }

    [Fact]
    public void An_arrow_on_the_body_is_staged_from_its_work_and_on_a_shield_gives_no_harm()
    {
        Assert.SkipUnless(Library.Value is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        var lib = Library.Value!;
        var assets = new CharacterAssets();
        int christofKind = OneOneLibrary.Register(assets, lib.Character("Christof", "BroadSword")!);
        int guardKind = OneOneLibrary.Register(assets, lib.Character("rudolfGuard")!);
        var contacts = new List<(long Tick, ContactMade Contact)>();
        var trace = new StringWriter();
        var m = TestData.Sandbox();
        var b = new WorldBuilder(21);
        MovementServices.Register(b, Floor());
        b.Animation(assets);
        BodyMotionSystem.Register(b);
        CombatServices.Register(b);
        DeathServices.Register(b);
        b.Service(new CombatTrace(trace));
        b.Event<ContactMade>();
        BowServices.Register(b);
        b.System(Phase.Contact, 0, "test.contacts", new DelegateSystem(w =>
        {
            foreach (var (tick, c) in contacts)
                if (tick == w.Tick) w.Events<ContactMade>().Publish(c with { Tick = w.Tick });
        }));
        m.Register(b);
        var world = b.Build();
        var people = world.Service<PeopleHooks>();
        var christof = MovementServices.SpawnBody(world, new(0, 0, 0.01f), DetMath.HalfPi, Locomotion.Sandbox);
        people.Made(christof, PersonRole.Townsperson, "Christof");
        world.Set(christof, CharacterAnimation.For(assets, christofKind));
        world.Set(christof, MotionInput.Standing);
        CombatServices.Arm(world, christof);
        Assert.True(BowServices.Give(world, christof, "war_bow", 6));
        var guard = MovementServices.SpawnBody(world, new(0, 10f, 0.01f), -DetMath.HalfPi, Locomotion.Sandbox);
        people.Made(guard, PersonRole.Townsperson, "rudolfGuard");
        world.Set(guard, CharacterAnimation.For(assets, guardKind));
        world.Set(guard, MotionInput.Standing);
        CombatServices.Arm(world, guard);
        var sheets = world.Service<ActorSheets>();
        int healthBefore = (int)sheets.Resource(guard, "health")!.Value.Current;

        // An arrow in his chest at 54 m/s (shot 1), then one on his shield (shot 2).
        contacts.Add((30, new ContactMade(30500, christof, 0, guard, ContactMet.Body, "chest", ContactMaterial.Flesh, 0, new(0, 9.8f, 1.4f), new(0, -1, 0),
            54f, 0.075f * 54f, false, false, true, false, PointSpeed: 54f, TipSpeed: 54f, PeakSpeed: 55f, Shot: 1, ProjectileMassKg: 0.075f)));
        contacts.Add((60, new ContactMade(60500, christof, 0, guard, ContactMet.Shield, null, ContactMaterial.Wood, 0, new(0, 9.6f, 1.3f), new(0, -1, 0),
            54f, 0.075f * 54f, false, true, true, false, PointSpeed: 54f, TipSpeed: 54f, PeakSpeed: 55f, Shot: 2, ProjectileMassKg: 0.075f)));
        var harmed = new List<HarmResolved>();
        for (int t = 0; t < 90; t++)
        {
            MovementServices.Step(world, ActionFrame.Idle);
            harmed.AddRange(world.Events<HarmResolved>().ThisTick);
        }
        var lines = trace.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => JsonDocument.Parse(l).RootElement).ToList();
        Assert.Equal(2, lines.Count);
        var potential = lines[0].GetProperty("events")[0];
        var inputs = potential.GetProperty("inputs");
        var outputs = potential.GetProperty("outputs");
        Assert.Equal("war_bow", inputs.GetProperty("weapon").GetString());
        Assert.Equal("arrow", inputs.GetProperty("weapon_class").GetString());
        Assert.Equal(0, inputs.GetProperty("strength_effective").GetInt32());
        Assert.Equal(1, inputs.GetProperty("arrow_shot").GetInt32());
        Assert.Equal(109.35, inputs.GetProperty("arrow_work_joules").GetDouble(), 2);
        Assert.Equal(0.0, outputs.GetProperty("strength_dice").GetDouble());
        Assert.Equal(3.645, outputs.GetProperty("arrow_work_dice").GetDouble(), 3);
        Assert.Equal(3.645, outputs.GetProperty("potential").GetDouble(), 3);
        Assert.Equal("Lethal", outputs.GetProperty("kind").GetString());
        var h = Assert.Single(harmed);
        Assert.Equal((30500L, christof, guard, HarmKind.Lethal), (h.ContactId, h.Attacker, h.Target, h.Kind));
        Assert.False(h.Contested);
        Assert.Equal(BodyRegion.None, h.Severed);
        Assert.Equal(0, h.FlurryBlows);
        Assert.Equal(healthBefore, h.HealthBefore);
        // On the shield: nothing, and why.
        Assert.Equal("Blocked", lines[1].GetProperty("outcome").GetString());
        Assert.Contains("shield", lines[1].ToString(), StringComparison.Ordinal);
    }
}
