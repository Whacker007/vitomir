// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Modules.Enhanced.Rules;
using Vitomir.Modules.Enhanced.Sheets;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Modules.Enhanced.Tests;

/// <summary>
/// The dodge's numbers (<c>skill.json</c>'s <c>dodge</c>): a human's dodge at Dexterity 23 and no Dodge; the Dodge
/// Ability alone shortening the start and the recovery (Dexterity held), Dexterity alone the burst (the Ability held), the
/// masquerade's burst held to Dexterity 100, the switch that turns Dexterity's link off; a wrong number refused by name; every
/// fighter's <see cref="DodgeTempo"/> following its sheet; and the contest counting Dodge only while a dodge is performed
/// (its start or burst), never in its recovery or without one.
/// </summary>
public sealed class DodgeDataTests
{
    [Fact]
    public void A_human_s_dodge_is_a_metre_in_0_3_s_after_0_15_s_then_0_4_s_to_recover()
    {
        var n = TestData.Sandbox().Skill.Dodge!;
        Assert.Equal((1f, 0.3f, 0.15f, 0.4f), (n.Distance, n.MoveSeconds, n.ReadySeconds, n.RecoverSeconds));
        var h = n.At(0, 23);
        Assert.Equal((0.15f, 0.3f, 0.4f, 1f, 0.3f), (h.Ready, h.Move, h.Recover, h.Distance, h.HumanMove));
    }

    [Fact]
    public void The_Dodge_Ability_alone_shortens_the_start_and_the_recovery_and_leaves_the_burst()
    {
        var n = TestData.Sandbox().Skill.Dodge!;
        float[] want = [1f, 1.0718f, 1.2311f, 1.4142f, 2f];
        int[] dots = [0, 1, 3, 5, 10];
        for (int i = 0; i < dots.Length; i++)
        {
            var t = n.At(dots[i], 23);
            Assert.Equal(want[i], n.AbilityRate(dots[i]), 3);
            Assert.Equal(0.15f / want[i], t.Ready, 3);
            Assert.Equal(0.4f / want[i], t.Recover, 3);
            Assert.Equal(0.3f, t.Move, 5);                      // Dexterity held: the burst is unchanged
            Assert.Equal(1f, t.Distance);
        }
        Assert.Equal(2f, n.AbilityRate(14));                     // clamped to the dots there are
    }

    [Fact]
    public void Dexterity_alone_sets_the_burst_s_speed_on_a_gentle_curve_about_the_human_mean()
    {
        var n = TestData.Sandbox().Skill.Dodge!;
        (int D, float S)[] cases = [(23, 1f), (26, 1.0311f), (43, 1.1693f), (100, 1.4440f), (330, 1.9463f), (1000, 2.5679f), (5, 0.7f)];
        foreach (var (d, s) in cases)
        {
            var t = n.At(0, d);
            Assert.Equal(s, n.DexterityScale(d), 3);
            Assert.Equal(0.3f / s, t.Move, 3);
            Assert.Equal((0.15f, 0.4f), (t.Ready, t.Recover));     // the Ability held: start and recovery unchanged
        }
        // The masquerade's burst: Dexterity counted to at most 100 (a human's five dots).
        Assert.Equal(n.At(0, 100).Move, n.At(0, 330).HumanMove, 5);
        Assert.Equal(n.At(0, 43).Move, n.At(0, 43).HumanMove, 5);
        // Dexterity's link off: the burst a human's whatever the Dexterity.
        Assert.Equal(0.3f, n.At(0, 1000, dexterityOn: false).Move, 5);
        Assert.Equal(0.3f, n.At(0, 1000, dexterityOn: false).HumanMove, 5);
    }

    [Fact]
    public void The_switch_turns_Dexterity_s_link_off_for_the_module()
    {
        Assert.True(TestData.Sandbox().DodgeDexterityOn);
        Assert.False(TestData.Sandbox(null, EnhancedModule.DodgeDexterityOffSwitch).DodgeDexterityOn);
    }

    [Theory]
    [InlineData("\"rate_at_ability_10\": 2.0", "\"rate_at_ability_10\": 9", "dodge.ability.rate_at_ability_10 = 9")]
    [InlineData("\"move_seconds\": 0.3", "\"move_seconds\": 0", "dodge.human.move_seconds = 0")]
    [InlineData("\"exponent\": 0.25", "\"exponent\": 7", "dodge.dexterity.exponent = 7")]
    public void A_wrong_number_in_the_dodge_s_data_is_refused_by_name(string from, string to, string reason)
    {
        using var copy = new TestData.Copy();
        copy.Edit("combat/skill.json", t => t.Replace(from, to));
        var e = Assert.Throws<ModuleRefusedException>(() => TestData.Sandbox(copy.Folder));
        Assert.Contains(e.Reasons, x => x.Contains(reason));
    }

    static (World W, ActorSheets Sheets, EntityId Christof, EntityId Guard) World(params string[] switches)
    {
        var m = TestData.Sandbox(null, switches);
        var b = new WorldBuilder(11);
        DeathServices.Register(b);
        b.Component<Combatant>().Component<CombatIntent>();
        b.Event<ContactMade>();
        Dodges.Register(b);
        m.Register(b);
        var world = b.Build();
        world.Service<HarmWeapons>().Of = (_, _) => "Rapier";
        var people = world.Service<PeopleHooks>();
        var (c, g) = (world.Create(), world.Create());
        people.Made(c, PersonRole.Player, "Christof");
        people.Made(g, PersonRole.Townsperson, "rudolfGuard");
        var route = world.Service<IPersonSheets>();
        Assert.True(route.TryTemplate(c, "sandbox.newly_embraced", out string error), error);
        Assert.True(route.TryTemplate(g, "sandbox.guard_trained", out error), error);
        CombatServices.Arm(world, c);
        CombatServices.Arm(world, g);
        return (world, world.Service<ActorSheets>(), c, g);
    }

    [Fact]
    public void Every_fighter_s_dodge_follows_its_Dodge_and_its_Dexterity_as_the_sheet_changes()
    {
        var (w, sheets, christof, guard) = World();
        var n = TestData.Sandbox().Skill.Dodge!;
        w.Step();
        Assert.Equal(n.At(0, 23), w.Get<DodgeTempo>(christof));          // the newly Embraced: Dodge 0, Dexterity 23
        Assert.Equal(n.At(1, 26), w.Get<DodgeTempo>(guard));             // the trained guard: Dodge 1, Dexterity 26
        sheets.SetPurchased(christof, "dodge", 5);                       // the arena sets a value after he is made
        w.Step();
        Assert.Equal(n.At(5, 23), w.Get<DodgeTempo>(christof));
        sheets.SetPurchased(christof, "dexterity", 100);
        w.Step();
        Assert.Equal(n.At(5, 100), w.Get<DodgeTempo>(christof));

        var (off, offSheets, c2, _) = World(EnhancedModule.DodgeDexterityOffSwitch);
        offSheets.SetPurchased(c2, "dexterity", 300);
        off.Step();
        Assert.Equal(0.3f, off.Get<DodgeTempo>(c2).Move, 5);
    }

    [Fact]
    public void The_contest_counts_Dodge_only_while_a_dodge_is_performed()
    {
        var m = TestData.Sandbox();
        var rules = HarmRules.Build(m.Config, m.Harm, m.Skill);
        var contest = rules.Contest!;
        var a = new Striker(new EntityId(1, 0), "vampire", 60, 0, false, "Rapier", Dexterity: 23, Melee: 2);
        var still = new Struck(new EntityId(2, 0), "human", 25, 0, false, true, 100, 100, Dexterity: 40, Dodge: 3, Melee: 2, Armed: true);
        var rng = new WorldRandom(new RandomStreams(5));
        var r = new HarmResult();
        r.DamagePool = 4;
        contest.Roll(in a, in still, m.Harm.Classes.Values.First(x => !m.Skill.IsBareHanded(x.Id)), rng, r);
        Assert.Equal(("", 0, 3), (r.DefenceAbilityId, r.DefenceAbility, r.DefenceDodge));        // traced, not counted
        Assert.Equal(2.0, r.DefencePotential);                                                     // 40/20

        r = new HarmResult();
        r.DamagePool = 4;
        contest.Roll(in a, still with { Dodging = true }, m.Harm.Classes.Values.First(x => !m.Skill.IsBareHanded(x.Id)), rng, r);
        Assert.Equal(("dodge", 3), (r.DefenceAbilityId, r.DefenceAbility));
        Assert.Equal(5.0, r.DefencePotential);                                                     // 40/20 + Dodge 3

        // The platform's dodge: performed in its burst only (by the rules — not its start, the windup; an earlier build counted it),
        // not in its recovery or between dodges.
        var b = new WorldBuilder(1);
        Dodges.Register(b);
        var w = b.Build();
        var e = w.Create();
        Assert.False(SkillContest.Dodging(w, e));
        foreach (var (phase, performed) in new[] { (DodgePhase.Start, false), (DodgePhase.Burst, true), (DodgePhase.Recover, false), (DodgePhase.None, false) })
        {
            w.Set(e, new Dodging { Phase = phase });
            Assert.Equal(performed, SkillContest.Dodging(w, e));
        }
        Assert.False(SkillContest.Dodging(new WorldBuilder(1).Build(), e));                         // a world without the dodge
    }
}
