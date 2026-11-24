// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Modules.Enhanced.Rules;
using Vitomir.Modules.Enhanced.Sheets;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Modules.Enhanced.Tests;

/// <summary>
/// The pace of a fight between humans (<c>Data/combat/pace.json</c>; <i>"humans moving at human
/// speeds"</i>): the file's numbers and its refusals; the global combat tempo multiplied into every fighter's swing and
/// recovery, not its rise, under Melee's rate — 0.70 by default, the program's override in its place, 1 with <c>combat-tempo-off</c>,
/// written alone with the skill's channel off; the fighting pace given to the platform unless <c>fighting-pace-off</c>; the
/// dodge's pace built and off by default.
/// </summary>
public sealed class PaceTests
{
    [Fact]
    public void The_pace_file_gives_the_global_tempo_0_70_the_fighting_pace_1_8_m_s_and_a_slower_dodge_off_by_default()
    {
        var p = TestData.Sandbox().Pace;
        Assert.Equal(0.70f, p.Tempo);
        Assert.Equal(1.8f, p.FightingPace);
        Assert.False(p.DodgeUse);
        Assert.Equal((0.35f, 0.45f), (p.DodgeMoveSeconds, p.DodgeRecoverSeconds));
        Assert.True(TestData.Sandbox().FightingPaceOn);
        Assert.False(TestData.Sandbox().CombatTempoOff);
        Assert.True(TestData.Sandbox(null, EnhancedModule.CombatTempoOffSwitch).CombatTempoOff);
        Assert.False(TestData.Sandbox(null, EnhancedModule.FightingPaceOffSwitch).FightingPaceOn);
    }

    [Fact]
    public void A_bad_pace_file_is_refused_by_name()
    {
        using var copy = new TestData.Copy();
        copy.Edit("combat/pace.json", s => s
            .Replace("\"global\": 0.70", "\"global\": 5")
            .Replace("\"most_metres_per_second\": 1.8", "\"most_metres_per_second\": 0"));
        var refused = Assert.Throws<ModuleRefusedException>(() => TestData.Sandbox(copy.Folder));
        Assert.Contains(refused.Reasons, r => r.Contains("tempo.global 5", StringComparison.Ordinal));
        Assert.Contains(refused.Reasons, r => r.Contains("fighting_pace.most_metres_per_second 0", StringComparison.Ordinal));
        using var none = new TestData.Copy();
        File.Delete(none.Path_("combat/pace.json"));
        Assert.Contains(Assert.Throws<ModuleRefusedException>(() => TestData.Sandbox(none.Folder)).Reasons, r => r.Contains("combat/pace", StringComparison.Ordinal));
    }

    /// <summary>A world with the module and two armed fighters with the stress fixtures' sheets (Melee 3 each); <paramref name="tempo"/> the program's override.</summary>
    static (World W, EntityId Christof, EntityId Guard) World(float? tempo, params string[] switches)
    {
        var m = TestData.Sandbox(null, switches);
        var b = new WorldBuilder(11);
        b.Service(new CombatTrace(TextWriter.Null));
        DeathServices.Register(b);
        b.Component<Combatant>().Component<CombatIntent>();
        b.Event<ContactMade>();
        if (tempo is { } t) b.Service(new CombatTempoOverride(t));
        m.Register(b);
        var world = b.Build();
        world.Service<HarmWeapons>().Of = (_, _) => "BroadSword";
        var people = world.Service<PeopleHooks>();
        var (c, g) = (world.Create(), world.Create());
        people.Made(c, PersonRole.Player, "Christof");
        people.Made(g, PersonRole.Townsperson, "rudolfGuard");
        var route = world.Service<IPersonSheets>();
        Assert.True(route.TryTemplate(c, "sandbox.christof", out string error), error);
        Assert.True(route.TryTemplate(g, "sandbox.guard", out error), error);
        CombatServices.Arm(world, c);
        CombatServices.Arm(world, g);
        world.Step();
        return (world, c, g);
    }

    static (float, float, float) Of(World w, EntityId e) { var t = w.Get<CombatTempo>(e); return (t.Swing, t.Recovery, t.Rise); }

    [Fact]
    public void The_global_tempo_slows_every_fighter_s_swing_and_recovery_under_Melee_s_rate_and_never_the_block_s_rise()
    {
        var k = TestData.Sandbox().Skill;
        var (s, r, rise) = k.TempoAt(3);                                       // the fixtures' Melee 3: 1.082, 1.082, 1.129
        var (w, christof, guard) = World(null);
        Assert.Equal((s * 0.70f, r * 0.70f, rise), Of(w, guard));
        Assert.Equal((s * 0.70f, r * 0.70f, rise), Of(w, christof));
        // The program's override in place of the file's (--combat-tempo, an arena spec's combat_tempo, a recording's head).
        (w, _, guard) = World(0.6f);
        Assert.Equal((s * 0.6f, r * 0.6f, rise), Of(w, guard));
        // combat-tempo-off: the earlier build's pace exactly, the override too.
        (w, _, guard) = World(null, EnhancedModule.CombatTempoOffSwitch);
        Assert.Equal(k.TempoAt(3), Of(w, guard));
        (w, _, guard) = World(0.6f, EnhancedModule.CombatTempoOffSwitch);
        Assert.Equal(k.TempoAt(3), Of(w, guard));
        // The skill's channel and Celerity's off: the global tempo alone, for everyone with a sheet.
        (w, christof, guard) = World(null, EnhancedModule.TempoOffSwitch, EnhancedModule.CelerityAttackOffSwitch);
        Assert.True(CombatTempos.Registered(w));
        Assert.Equal((0.70f, 0.70f, 1f), Of(w, guard));
        Assert.Equal((0.70f, 0.70f, 1f), Of(w, christof));
        // All three off (with combat-tempo-off): no tempo store, as the first module.
        (w, _, _) = World(null, EnhancedModule.TempoOffSwitch, EnhancedModule.CelerityAttackOffSwitch, EnhancedModule.CombatTempoOffSwitch);
        Assert.False(CombatTempos.Registered(w));
    }

    [Fact]
    public void A_blow_s_busy_time_grows_by_the_tempo_s_inverse_and_its_clip_plays_slower_by_it()
    {
        // The platform's arithmetic on the module's tempo: a broadsword blow (1.1's 1.1 s, its Strike mark at 0.57 s) at Melee 0.
        var motion = new Platform.Animation.ActionMotion(0, 0.879f, 0.5f, 1.1f, -1f, false, "");
        var at = new CombatTempo(0.70f, 0.70f, 1f);
        Assert.Equal(1.1f / 0.70f, at.BusyFor(motion), 0.0001f);                // 1.571 s
        Assert.Equal(1.1f, CombatTempo.Normal.BusyFor(motion));
    }

    [Fact]
    public void The_fighting_pace_is_given_to_the_bodies_unless_switched_off()
    {
        var (w, _, _) = World(null);
        Assert.True(w.TryService<FightingPace>(out var fp));
        Assert.Equal(1.8f, fp!.MaxSpeed);
        Assert.True(fp.On);
        (w, _, _) = World(null, EnhancedModule.FightingPaceOffSwitch);
        Assert.False(w.TryService<FightingPace>(out _));
    }

    /// <summary>
    /// Pace.json's rushing creatures are the szlachta's sheet; with the switch <c>rush-unpaced</c> a fighter on that sheet
    /// is marked to keep its own pace (the platform's <see cref="RushUnpaced"/>) and one on another sheet is not; without the switch
    /// nothing is registered (the world before, digest for digest); with the fighting pace off neither.
    /// </summary>
    [Fact]
    public void Rushing_creatures_are_the_szlachta_and_are_let_through_only_with_rush_unpaced()
    {
        Assert.Equal(["sandbox.szlachta"], TestData.Sandbox().Pace.RushUnpaced);
        var (w, c, g) = World(null);
        Assert.False(w.TryStore<RushUnpaced>(out _));
        (w, c, g) = World(null, EnhancedModule.RushUnpacedSwitch, EnhancedModule.FightingPaceOffSwitch);
        Assert.False(w.TryStore<RushUnpaced>(out _));
        (w, c, g) = World(null, EnhancedModule.RushUnpacedSwitch);
        Assert.True(w.TryStore<RushUnpaced>(out var marks));
        Assert.False(marks!.Has(c) || marks.Has(g));                                    // the stress fixtures are no rushing creatures
        Assert.True(w.Service<IPersonSheets>().TryTemplate(g, "sandbox.szlachta", out string error), error);
        w.Step();
        Assert.True(marks.Has(g));                                                      // the szlachta's sheet: marked at once
        Assert.False(marks.Has(c));
        Assert.True(w.Service<IPersonSheets>().TryTemplate(g, "sandbox.guard", out error), error);
        w.Step();
        Assert.False(marks.Has(g));                                                     // its sheet changed away: the mark goes
    }

    [Fact]
    public void The_dodge_s_pace_is_built_and_off_by_default()
    {
        var m = TestData.Sandbox();
        var today = m.Skill.Dodge!.At(0, 23);
        Assert.Equal((0.30f, 0.40f, 0.15f, 1.0f), (today.Move, today.Recover, today.Ready, today.Distance));
        var paced = m.Skill.Dodge.WithTimes(m.Pace.DodgeMoveSeconds, m.Pace.DodgeRecoverSeconds).At(0, 23);
        Assert.Equal((0.35f, 0.45f, 0.15f, 1.0f), (paced.Move, paced.Recover, paced.Ready, paced.Distance));
        // The Dodge Ability still shortens the start and the recovery, Dexterity the burst.
        var trained = m.Skill.Dodge.WithTimes(0.35f, 0.45f).At(5, 23);
        Assert.Equal(0.45f / m.Skill.Dodge.AbilityRate(5), trained.Recover, 0.0001f);
        Assert.True(m.Skill.Dodge.WithTimes(0.35f, 0.45f).At(0, 100).Move < 0.35f);
    }
}
