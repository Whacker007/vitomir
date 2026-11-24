// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Modules.Enhanced.Rules;
using Vitomir.Modules.Enhanced.Sheets;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Modules.Enhanced.Tests;

/// <summary>
/// The guard's contest (SANDBOX): its numbers load with the module and its switch turns the guard off; each input alone
/// moves the contest the right way (Strength first for force and hold, technique second, a shield holding better, an incidental
/// interception holding worse); the combat rules' anchor sheets give the chances the data's notes say; the contest is deterministic and
/// draws from its own stream; the verdicts in a world, a clash judged once whichever contact comes first, and an overpowering
/// blade's budget left written after the harm.
/// </summary>
public sealed class GuardRulesTests
{
    static GuardRules Rules() => new(TestData.Sandbox().Guard);

    // The combat rules' sheets: the newly Embraced (Strength 15, Dexterity 23, Melee 0), the trained guard (21, 26, Melee 2), elite_stress's Strength 400.
    static GuardSide Embraced(int strength = 15, int melee = 0, float budget = 1f) => new(strength, 23, melee, 1.0, budget);
    static GuardSide Trained(int strength = 21, int dexterity = 26, int melee = 2) => new(strength, dexterity, melee, 1.0, 1f);

    [Fact]
    public void The_guard_s_numbers_load_with_the_module_and_its_switch_turns_the_guard_off()
    {
        var m = TestData.Sandbox();
        Assert.True(m.GuardOn);
        Assert.Equal((0.25f, 0.6f, 0.2f, 70f, 200f, 600f), (m.Guard.ParryWindowSeconds, m.Guard.ParryRepressSeconds, m.Guard.CancelReturnSeconds,
            m.Guard.GuardArcDegrees, m.Guard.GuardDegreesPerSecond, m.Guard.ParryDegreesPerSecond));
        Assert.False(TestData.Sandbox(null, EnhancedModule.GuardOffSwitch).GuardOn);
        // In a world: the platform's settings given the data's numbers, the rules deciding; off, the earlier build's guard.
        foreach (var off in new[] { false, true })
        {
            var module = off ? TestData.Sandbox(null, EnhancedModule.GuardOffSwitch) : TestData.Sandbox();
            var b = new WorldBuilder(1);
            CombatServices.Register(b);
            var g = b.Peek.Service<GuardSettings>();
            module.Register(b);
            Assert.Equal((!off, !off), (g.Enabled, g.RulesDecide));
            if (!off) Assert.Equal(0.25f, g.ParryWindowSeconds);
        }
    }

    [Fact]
    public void Each_input_alone_moves_the_contest_the_right_way()
    {
        var r = Rules();
        var d = r.Data;
        double Over(GuardSide a, GuardSide t, ContactMet met = ContactMet.Weapon, GuardMeet meet = GuardMeet.Blade) =>
            GuardRules.Odds(r.Force(a), d.OverpowerMargin * r.Hold(t, met, meet), d.OverpowerSharpness);
        double Parry(GuardSide a, GuardSide t) => GuardRules.Odds(r.Technique(t), d.ParryForceShare * r.Force(a), d.ParrySharpness);
        var a0 = Embraced();
        var t0 = Trained();
        double baseOver = Over(a0, t0), baseParry = Parry(a0, t0);
        Assert.True(Over(a0 with { Strength = 60 }, t0) > baseOver, "the attacker's Strength pushes through");
        Assert.True(Over(a0 with { WeaponForce = 2 }, t0) > baseOver, "a heavier weapon pushes through, a little");
        Assert.True(Over(a0 with { Budget = 0.5f }, t0) < baseOver, "a blade with less of its budget left pushes less");
        Assert.True(Over(a0, t0 with { Strength = 60 }) < baseOver, "the defender's Strength holds");
        Assert.True(Over(a0, t0 with { Dexterity = 60 }) < baseOver && Over(a0, t0 with { Melee = 5 }) < baseOver, "technique holds, second");
        Assert.True(Over(a0, t0, ContactMet.Shield, GuardMeet.Shield) < baseOver, "a shield holds better than a blade");
        Assert.True(Over(a0, t0, ContactMet.Shield, GuardMeet.Incidental) > Over(a0, t0, ContactMet.Shield, GuardMeet.Shield), "a shield not raised holds worse");
        // Strength first: the same raw rise in Strength moves the hold more than the same rise in Dexterity.
        Assert.True(Over(a0, t0 with { Strength = 41 }) < Over(a0, t0 with { Dexterity = 46 }));
        Assert.True(Parry(a0, t0 with { Melee = 4 }) > baseParry && Parry(a0, t0 with { Dexterity = 60 }) > baseParry, "technique parries");
        Assert.True(Parry(a0 with { Strength = 200 }, t0) < baseParry, "a stronger blow is harder to turn");
    }

    [Fact]
    public void The_combat_rules_anchor_sheets_give_the_chances_the_data_says()
    {
        var r = Rules();
        var d = r.Data;
        double Over(GuardSide a, GuardSide t, ContactMet met) =>
            GuardRules.Odds(r.Force(a), d.OverpowerMargin * r.Hold(t, met, met == ContactMet.Shield ? GuardMeet.Shield : GuardMeet.Blade), d.OverpowerSharpness);
        double Parry(GuardSide a, GuardSide t) => GuardRules.Odds(r.Technique(t), d.ParryForceShare * r.Force(a), d.ParrySharpness);
        Assert.Equal(0.05, Over(Embraced(), Trained(), ContactMet.Shield), 2);       // matched: a held guard usually holds
        Assert.Equal(0.10, Over(Embraced(), Trained(), ContactMet.Weapon), 2);
        Assert.Equal(0.15, Over(Embraced(42), Trained(), ContactMet.Shield), 2);
        Assert.Equal(0.68, Over(Embraced(200), Trained(), ContactMet.Shield), 2);
        Assert.Equal(0.89, Over(Embraced(400), Trained(), ContactMet.Shield), 2);    // 400 against 21: through a shield often
        Assert.Equal(0.94, Over(Embraced(400), Trained(), ContactMet.Weapon), 2);    // and through a blade nearly always
        Assert.Equal(0.56, Parry(Trained(), Trained(21, 23, 0)), 2);                 // the newly Embraced parrying the trained guard's blow
        Assert.Equal(0.94, Parry(Trained(), Trained(21, 23, 3)), 2);                 // at Melee 3
        Assert.Equal(0.91, Parry(Trained(), Trained()), 2);                          // a trained guard against his like
    }

    /// <summary>Draws given by the test, by stream: exact verdicts.</summary>
    sealed class Draws(params double[] units) : IDeterministicRandom
    {
        int next;
        public readonly List<string> Streams = [];
        public int NextD10(string purpose) => 1;
        public double NextUnitInterval(string purpose) { Streams.Add(purpose); return units[next++ % units.Length]; }
        public long Position(string purpose) => next;
    }

    [Fact]
    public void The_contest_is_deterministic_and_draws_from_its_own_stream_the_parry_draw_only_in_the_window()
    {
        var r = Rules();
        var held = new Draws(0.99, 0.99);
        Assert.Equal(GuardVerdict.Held, r.Judge(Embraced(), Trained(), ContactMet.Weapon, GuardMeet.Blade, held).Verdict);
        Assert.Equal(["enhanced.guard"], held.Streams);                              // one draw outside the window
        var parried = new Draws(0.99, 0.01);
        var p = r.Judge(Embraced(), Trained(), ContactMet.Weapon, GuardMeet.Parry, parried);
        Assert.Equal((GuardVerdict.Parried, 2), (p.Verdict, parried.Streams.Count));
        var over = r.Judge(Embraced(400), Trained(), ContactMet.Weapon, GuardMeet.Parry, new Draws(0.01, 0.01));
        Assert.Equal(GuardVerdict.Overpowered, over.Verdict);                       // Strength first: overpowering before the parry
        Assert.InRange(over.BudgetLeft, 0.25f, 1f);
        // A shield met in the window is no parry (a parry is the weapon's).
        Assert.Equal(GuardVerdict.Held, r.Judge(Embraced(), Trained(), ContactMet.Shield, GuardMeet.Parry, new Draws(0.99, 0.01)).Verdict);
        // The world's streams: the same seed, the same verdicts.
        var x = new WorldRandom(new RandomStreams(7));
        var y = new WorldRandom(new RandomStreams(7));
        for (int i = 0; i < 50; i++)
            Assert.Equal(r.Judge(Embraced(42), Trained(), ContactMet.Shield, GuardMeet.Shield, x), r.Judge(Embraced(42), Trained(), ContactMet.Shield, GuardMeet.Shield, y));
        // The clash: matched blows stop, a much stronger one dominates, an oblique meeting deflects.
        Assert.Equal((GuardVerdict.Clashed, GuardVerdict.Clashed), Verdicts(r.Clash(Trained(), Trained(), 1, 1, new Draws(0.5))));
        Assert.Equal((GuardVerdict.ClashDominates, GuardVerdict.Clashed), Verdicts(r.Clash(Embraced(400), Trained(), 1, 1, new Draws(0.5))));
        Assert.Equal((GuardVerdict.Clashed, GuardVerdict.ClashDominates), Verdicts(r.Clash(Trained(), Embraced(400), 1, 1, new Draws(0.5))));
        Assert.Equal((GuardVerdict.ClashDeflected, GuardVerdict.ClashDeflected), Verdicts(r.Clash(Trained(), Trained(), 0.2, 0.2, new Draws(0.5))));
    }

    static (GuardVerdict, GuardVerdict) Verdicts((GuardOutcome A, GuardOutcome B) x) => (x.A.Verdict, x.B.Verdict);

    /// <summary>A world with the module and the platform's fighting, and two people with the combat rules' sheets (the newly Embraced, the trained guard).</summary>
    static (World W, EntityId A, EntityId D, List<GuardResolved> Verdicts) Ring(Func<World, EntityId, EntityId, List<ContactMade>> contacts, int attackerStrength = 15, ulong seed = 3)
    {
        var m = TestData.Sandbox();
        var b = new WorldBuilder(seed);
        b.Service(new GuardSettings());     // the platform's (its fighting is not needed here: the contacts are given)
        b.Event<ContactMade>();
        var verdicts = new List<GuardResolved>();
        List<ContactMade>? feed = null;
        b.System(Phase.Contact, 0, "test.contacts", new DelegateSystem(w => { if (w.Tick == 2 && feed is not null) foreach (var c in feed) w.Events<ContactMade>().Publish(c); }));
        m.Register(b);
        b.System(Phase.Post, 0, "test.verdicts", new DelegateSystem(w => verdicts.AddRange(w.Events<GuardResolved>().ThisTick)));
        var world = b.Build();
        var people = world.Service<PeopleHooks>();
        var a = world.Create();
        people.Made(a, PersonRole.Townsperson);
        var d = world.Create();
        people.Made(d, PersonRole.Townsperson);
        var route = world.Service<IPersonSheets>();
        Assert.True(route.TryTemplate(a, "sandbox.newly_embraced", out var error), error);
        Assert.True(route.TryTemplate(d, "sandbox.guard_trained", out error), error);
        Assert.True(route.TrySet(a, "strength", attackerStrength, out error), error);
        feed = contacts(world, a, d);
        for (int i = 0; i < 4; i++) world.Step();
        return (world, a, d, verdicts);
    }

    static ContactMade Met(long id, EntityId attacker, EntityId target, ContactMet met, GuardMeet guard, int swing = 1) =>
        new(id, attacker, swing, target, met, null, ContactMaterial.Steel, 2, new Vector3(0, 0.6f, 1.3f), new Vector3(0, -1, 0), 10f, 14f,
            false, met == ContactMet.Shield, true, false, PointSpeed: 12f, TipSpeed: 20f, Guard: guard);

    [Fact]
    public void In_a_world_each_guard_contact_has_one_verdict_a_clash_is_judged_once_whichever_comes_first_and_an_overpowering_blade_keeps_its_budget()
    {
        var (w, a, d, v) = Ring((w, a, d) => [Met(2000, a, d, ContactMet.Weapon, GuardMeet.Blade), Met(2001, a, d, ContactMet.Level, GuardMeet.None)]);
        var one = Assert.Single(v);
        Assert.Equal((2000L, a, d, GuardMeet.Blade), (one.ContactId, one.Attacker, one.Defender, one.Meet));
        // A clash: the same verdicts by attacker whichever contact the list holds first.
        GuardVerdict[] ByAttacker(bool swap, int strength)
        {
            var (_, x, y, vs) = Ring((w, a, d) => swap
                ? [Met(2001, d, a, ContactMet.Weapon, GuardMeet.Clash), Met(2000, a, d, ContactMet.Weapon, GuardMeet.Clash)]
                : [Met(2000, a, d, ContactMet.Weapon, GuardMeet.Clash), Met(2001, d, a, ContactMet.Weapon, GuardMeet.Clash)], strength);
            Assert.Equal(2, vs.Count);
            return [vs.Single(z => z.Attacker == x).Verdict, vs.Single(z => z.Attacker == y).Verdict];
        }
        Assert.Equal(ByAttacker(false, 15), ByAttacker(true, 15));
        Assert.Equal(ByAttacker(false, 400), ByAttacker(true, 400));
        Assert.Equal(GuardVerdict.ClashDominates, ByAttacker(false, 1000)[0]);     // a thousand against 21: it goes on
        // Overpowered at Strength 1000 against a raised blade: its swing's budget is what the verdict left, after the harm.
        var (w2, a2, _, v2) = Ring((w, a, d) => [Met(2000, a, d, ContactMet.Weapon, GuardMeet.Blade)], 1000);
        var o = Assert.Single(v2);
        Assert.Equal(GuardVerdict.Overpowered, o.Verdict);
        Assert.True(o.BrokenSeconds > 0f && o.RiposteSeconds == 0f);
        Assert.Equal(o.BudgetLeft, w2.Get<SwingBudget>(a2).Left, 5);
    }
}
