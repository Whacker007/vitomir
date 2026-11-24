// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Modules.Enhanced.Rules;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Perception;

namespace Vitomir.Modules.Enhanced.Tests;

/// <summary>
/// The guard by skill (guard.json's <c>skill</c>), the brain's parry timing (<c>brain_parry</c>), the
/// stagger when hit (<c>stagger</c>) read and given to the platform, and their switches.
/// </summary>
public sealed class GuardSkillTests
{
    static (GuardSettings Guard, HurtRule Hurt, bool Skills) World(params string[] switches)
    {
        var m = TestData.Sandbox(null, switches);
        var b = new WorldBuilder(1);
        PerceptionServices.Register(b);
        CombatServices.Register(b);
        ContactServices.Register(b);
        DeathServices.Register(b);
        m.Register(b);
        var w = b.Build();
        return (w.Service<GuardSettings>(), w.Service<HurtRule>(), w.TryStore<GuardSkill>(out _));
    }

    [Fact]
    public void The_curve_is_one_at_the_trained_guard_and_gives_Melee_0_2_and_5_their_numbers()
    {
        var g = TestData.Sandbox().Guard;
        var curve = Assert.IsType<GuardSkillCurve>(g.Skill);
        double reference = curve.ReferenceTechnique(g.AttributeDice);
        Assert.Equal(3.3, reference, 9);
        var at = curve.At(reference, reference);
        Assert.Equal((1f, 1f, 1f), (at.Hand, at.Read, at.React));   // exactly: the combat rules' trained guard keeps the earlier build's guard ...
        Assert.Equal((0.5f, 0.5f), (at.Guard, at.Parry));            // ... but for its responses, halved (response_scale: low, as in KCD)
        Assert.Equal((0.5, 0.5), (curve.GuardScale, curve.ParryScale));
        Assert.Equal(0.04f, at.Error, 5);
        // The combat rules' newly Embraced (Dexterity 23) at Melee 0, 2, 5; a Melee 2 fighter at Dexterity 15 and 60 — the platform's GuardSolverTests.SkillCases.
        foreach (var (dex, melee, hand, read, guard, parry) in new[]
        {
            (23, 0, 0.7663f, 1.5609f, 0.5793f, 0.3924f), (23, 2, 0.9837f, 1.0391f, 0.9707f, 0.9576f), (23, 5, 1.2575f, 0.7258f, 1.3594f, 1.6150f),
            (15, 2, 0.9402f, 1.1435f, 0.8924f, 0.8446f), (60, 2, 1.1574f, 0.8321f, 1.2204f, 1.3778f),
        })
        {
            var s = curve.At(curve.Technique(g.AttributeDice, dex, melee), reference);
            Assert.Equal(hand, s.Hand, 3);
            Assert.Equal(read, s.Read, 3);
            Assert.Equal(guard * 0.5f, s.Guard, 3);   // the response scale
            Assert.Equal(parry * 0.5f, s.Parry, 3);
        }
        // Celerity's attack factor speeds the hand only.
        var fast = curve.At(reference, reference, 3f);
        Assert.Equal((3f, 1f, 0.5f), (fast.Hand, fast.Read, fast.Parry));   // the parry's response scale
        // Held at the ends: Christof's sandbox sheet (Dexterity 300, Melee 3: T 18) gets the last point's.
        Assert.Equal(1.45f, curve.At(curve.Technique(g.AttributeDice, 300, 3), reference).Hand, 4);
    }

    [Fact]
    public void In_a_world_the_skill_the_parry_timing_the_stagger_and_the_parry_rebound_are_given_and_each_switch_turns_its_own_off()
    {
        var on = World();
        Assert.True(on.Skills);
        Assert.True(on.Guard.BrainParryByTravel);
        Assert.Equal((0.8f, 0.03f, 0.05f), (on.Guard.BrainParryTravelMetres, on.Guard.BrainParryMarginSeconds, on.Guard.BrainParryArriveSeconds));
        Assert.Equal(1.4f, on.Guard.ParryReboundScale);
        Assert.True(on.Hurt.Stagger);
        Assert.Equal((0.3f, 0.01f, 0.6f, 0.5f), (on.Hurt.StaggerLeastSeconds, on.Hurt.StaggerSecondsPerImpulse, on.Hurt.StaggerMostSeconds, on.Hurt.StaggerGuardSeconds));
        Assert.Equal((0.015f, 0.35f, 0.08f, 0.7f), (on.Hurt.PushRadiansPerImpulse, on.Hurt.PushMostRadians, on.Hurt.PushPeakSeconds, on.Hurt.PushSettleSeconds));
        Assert.True(on.Hurt.StaggerOverpowered);
        Assert.False(on.Hurt.OnContact);
        Assert.False(World(EnhancedModule.GuardSkillOffSwitch).Skills);
        Assert.False(World(EnhancedModule.BrainParryFixedSwitch).Guard.BrainParryByTravel);
        var flinch = World(EnhancedModule.StaggerOneOneSwitch).Hurt;
        Assert.False(flinch.Stagger);
        Assert.Equal(3.5f, flinch.FlinchSeconds);
        Assert.Equal(2f, World(EnhancedModule.ReboundTwoThirdsSwitch).Guard.ParryReboundScale);
        // 1.1's hurt clip played whole by default; the push alone, or the hurt cut short, by the switches.
        Assert.True(TestData.Sandbox().Guard.Stagger!.HurtWhole);
        Assert.Equal((true, true, true), (on.Hurt.Stagger, on.Hurt.StaggerHurtClip, on.Hurt.StaggerHurtWhole));
        var pushOnly = World(EnhancedModule.StaggerPushOnlySwitch).Hurt;
        Assert.Equal((true, false), (pushOnly.Stagger, pushOnly.StaggerHurtClip));
        foreach (var sw in new[] { EnhancedModule.StaggerHurtShortSwitch, EnhancedModule.StaggerHurtClipSwitch })   // The earlier name, an alias
        {
            var shortHurt = World(sw).Hurt;
            Assert.Equal((true, true, false), (shortHurt.Stagger, shortHurt.StaggerHurtClip, shortHurt.StaggerHurtWhole));
        }
    }

    [Fact]
    public void A_bad_skill_table_is_refused_by_name()
    {
        using var copy = new TestData.Copy();
        copy.Edit("combat/guard.json", s => s
            .Replace("\"points\": [0, 1, 3.3, 6, 10]", "\"points\": [0, 1, 3.3, 3, 10]")
            .Replace("\"hand\": [0.65, 0.75, 1.0, 1.25, 1.45]", "\"hand\": [0.65, 0.75, 1.0, 1.25]")
            .Replace("\"push_most_radians\": 0.35", "\"push_most_radians\": 3"));
        var refused = Assert.Throws<ModuleRefusedException>(() => TestData.Sandbox(copy.Folder));
        void Has(string text) => Assert.Contains(refused.Reasons, r => r.Contains(text, StringComparison.Ordinal));
        Has("skill.points must rise");
        Has("stagger.push_most_radians 3 is outside 0 to 1.5");
    }
}
