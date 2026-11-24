// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Text.Json;
using Vitomir.Modules.Enhanced.Rules;
using Vitomir.Modules.Enhanced.Sheets;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Modules.Enhanced.Tests;

/// <summary>
/// Abilities in the fight: the module's <c>combat/skill</c> data and its two channels — the
/// <b>contest on contact</b> worked by hand with fixed dice (a positive net adds dice, a negative one takes at most half the
/// pool, never a miss, bare hands use Brawl and an unarmed defender only Dodge, its own streams leave the staged harm's
/// draws as they were) and in the world (<see cref="HarmResolved"/>'s new fields, the trace's stage); and the <b>tempo</b>
/// set on every fighter from its Melee or Brawl as the sheet changes. Each channel is switched off by its switch, and with
/// both off the module is the first one (no tempo store, no contest).
/// </summary>
[Collection(AllocationCollection.Name)]   // it measures allocations (Allocations.cs)
public sealed class SkillTests
{
    const string Damage = "enhanced.harm.damage", AttackStream = "enhanced.skill.attack", DefenceStream = "enhanced.skill.defence";

    /// <summary>Dice given in advance, by purpose; a purpose run dry fails the test.</summary>
    sealed class FixedDice : IDeterministicRandom
    {
        readonly Dictionary<string, Queue<int>> d10 = new(StringComparer.Ordinal);
        readonly Dictionary<string, Queue<double>> units = new(StringComparer.Ordinal);
        readonly Dictionary<string, long> positions = new(StringComparer.Ordinal);

        public FixedDice D10(string purpose, params int[] values) { d10[purpose] = new Queue<int>(values); return this; }
        public FixedDice Unit(string purpose, params double[] values) { units[purpose] = new Queue<double>(values); return this; }

        public int NextD10(string purpose) { positions[purpose] = Position(purpose) + 1; return d10[purpose].Dequeue(); }
        public double NextUnitInterval(string purpose) { positions[purpose] = Position(purpose) + 1; return units[purpose].Dequeue(); }
        public long Position(string purpose) => positions.TryGetValue(purpose, out var p) ? p : 0;
        public int Left(string purpose) => d10.TryGetValue(purpose, out var q) ? q.Count : 0;
    }

    static readonly EntityId A = new(1, 0), T = new(2, 0);

    static ContactMade Contact(long id = 1, EntityId? attacker = null, EntityId? target = null) =>
        new(id, attacker ?? A, 1, target ?? T, ContactMet.Body, "chest", ContactMaterial.Cloth, 10, new Vector3(1, 2, 1.3f), new Vector3(-1, 0, 0), 9.5f, 13.3f, false, false, true, false);

    static HarmRules Contested()
    {
        var m = TestData.Sandbox();
        var r = HarmRules.Build(m.Config, m.Harm, m.Skill);
        Assert.True(r.CanResolve, string.Join("; ", r.Refusals));
        Assert.NotNull(r.Contest);
        return r;
    }

    // ---- the data ----

    [Fact]
    public void The_skill_data_reads_both_channels_and_works_the_tempo_tables_from_the_power_curve()
    {
        var m = TestData.Sandbox();
        var k = m.Skill;
        Assert.True(m.TempoOn);                                                        // the tempo by default
        Assert.False(m.ContestOn);                                                     // the contest only when asked
        Assert.True(TestData.Sandbox(null, EnhancedModule.ContestSwitch).ContestOn);
        Assert.DoesNotContain("not a rule", m.Banner);
        Assert.Contains(EnhancedModule.DoubleCountWarning, TestData.Sandbox(null, EnhancedModule.ContestSwitch).Banner);   // both on: said plainly
        Assert.DoesNotContain("not a rule", TestData.Sandbox(null, EnhancedModule.ContestSwitch, EnhancedModule.TempoOffSwitch).Banner);
        Assert.Equal(("melee", "brawl", "dodge", "dexterity"), (k.ArmedAbility, k.BareAbility, k.DodgeAbility, k.DexterityTrait));
        Assert.Equal(["fists", "claws", "fangs"], k.BareClasses);
        Assert.Equal(SkillCurve.Power, k.Curve);
        Assert.Equal((1f, 1f, 1f), k.TempoAt(0));
        Assert.Equal((1.3f, 1.3f, 1.5f), k.TempoAt(10));
        Assert.Equal((1.3f, 1.3f, 1.5f), k.TempoAt(14));                              // clamped to the dots there are
        var (s3, _, r3) = k.TempoAt(3);
        Assert.Equal(Math.Pow(1.3, 0.3), s3, 4);                                       // 1.0819: Christof's and the guard's Melee 3
        Assert.Equal(Math.Pow(1.5, 0.3), r3, 4);
        Assert.Equal(1.15, SkillData.Rate(SkillCurve.Linear, 1.3, 5), 9);
        Assert.Equal("raw/20", k.DexterityDice.Text);
        Assert.Equal(15.0, k.DexterityDice.Of(300));
        Assert.Equal(("damage_d10_difficulty", (int?)null, 0.5), (k.DifficultyProfileValue, k.AddMost, k.TakeMostShare));
        Assert.Equal(("enhanced.skill.pool", AttackStream, DefenceStream), (k.PoolStream, k.AttackStream, k.DefenceStream));
    }

    [Theory]
    [InlineData("\"curve\": \"power\"", "\"curve\": \"cubic\"", "tempo.curve 'cubic' is not one this module knows")]
    [InlineData("\"swing\": 1.3", "\"swing\": 9", "tempo.at_ability_10.swing = 9")]
    [InlineData("\"enhanced.skill.pool\"", "\"enhanced.harm.pool\"", "contest stream 'enhanced.harm.pool' is also the staged harm's")]
    [InlineData("\"armed\": \"melee\"", "\"armed\": \"swordplay\"", "abilities.armed 'swordplay' is no trait of the registry")]
    [InlineData("\"take_most_share_of_pool\": 0.5", "\"take_most_share_of_pool\": 2", "contest.take_most_share_of_pool = 2")]
    [InlineData("[\"fists\", \"claws\", \"fangs\"]", "[\"claws\", \"fangs\"]", "must name enhanced:rules/harm's bare_hands class 'fists'")]
    public void A_wrong_number_in_the_skill_data_is_refused_by_name(string from, string to, string reason)
    {
        using var copy = new TestData.Copy();
        copy.Edit("combat/skill.json", t => t.Replace(from, to));
        var e = Assert.Throws<ModuleRefusedException>(() => TestData.Sandbox(copy.Folder));
        Assert.Contains(e.Reasons, x => x.Contains(reason));
    }

    // ---- the contest, by hand ----

    [Fact]
    public void A_positive_net_adds_its_dice_to_the_damage_pool_worked_by_hand()
    {
        var rules = Contested();
        var dice = new FixedDice()
            .D10(AttackStream, 6, 7, 8, 1, 2, 3, 10, 5, 9)          // 9 dice: five at 6 or more, one 1 → 4
            .D10(DefenceStream, 6, 1, 1, 2, 3, 4)                  // 6 dice: one success, two 1s → 0 (never below)
            .D10(Damage, 6, 6, 6, 6, 6, 2, 2, 2, 2, 2, 2);         // 7 + 4 = 11 dice → 5
        var a = new Striker(A, "vampire", 100, 0, false, "BroadSword", Dexterity: 100, Melee: 4, Brawl: 0);
        var t = new Struck(T, "human", 60, 0, false, true, 100, 100, Dexterity: 60, Dodge: 1, Melee: 3, Armed: true, Guarding: true);
        var r = new HarmResult();
        rules.Resolve(Contact(), in a, in t, dice, r);
        Assert.True(r.Contested);
        Assert.Equal(("melee", 5.0, 9.0, 9, 4), (r.AttackAbilityId, r.AttackDexterityDice, r.AttackPotential, r.AttackPool, r.AttackSuccesses));
        Assert.Equal(("melee", 3, 6.0, 6, 0), (r.DefenceAbilityId, r.DefenceAbility, r.DefencePotential, r.DefencePool, r.DefenceSuccesses));   // his Melee: he guards, armed; Dodge 1 never counts
        Assert.Equal((4, 7, 4, false), (r.ContestNet, r.PoolBeforeContest, r.ContestChange, r.ContestLimited));
        Assert.Equal(11, r.DamagePool);
        Assert.Equal(5, r.DamageSuccesses);
        Assert.Equal((0, 5, 25L, HarmOutcome.Applied), (r.SoakPool, r.Wounds, r.HealthAfter, r.Outcome));   // a human soaks no lethal with Stamina; 100 - 5 x 15
        Assert.Equal(0, dice.Left(AttackStream) + dice.Left(DefenceStream) + dice.Left(Damage));
        Assert.Equal([AttackStream, DefenceStream, Damage], r.Draws.Select(d => d.Purpose).Distinct());
    }

    [Fact]
    public void A_negative_net_takes_dice_but_never_more_than_half_the_pool()
    {
        var rules = Contested();
        var dice = new FixedDice()
            .D10(AttackStream, 2)                                   // Dexterity 20: one die, no Melee → 0
            .D10(DefenceStream, 10, 10, 10, 10, 10)                 // 100/20 = 5, Dodge 3 not counted (no dodge performed) → 5
            .D10(Damage, 6, 6, 6);                                  // 5 − floor(5/2) = 3 dice
        var a = new Striker(A, "vampire", 60, 0, false, "BroadSword", Dexterity: 20, Melee: 0);
        var t = new Struck(T, "human", 60, 0, false, true, 100, 100, Dexterity: 100, Dodge: 3, Melee: 5, Armed: true);   // armed, not guarding: Dexterity alone
        var r = new HarmResult();
        rules.Resolve(Contact(), in a, in t, dice, r);
        Assert.Equal(("", 0, 5, 5, 3), (r.DefenceAbilityId, r.DefenceAbility, r.DefencePool, r.DefenceSuccesses, r.DefenceDodge));
        Assert.Equal((-5, 5, -2, true), (r.ContestNet, r.PoolBeforeContest, r.ContestChange, r.ContestLimited));
        Assert.Equal((3, 3, 3), (r.DamagePool, r.DamageSuccesses, r.Wounds));
        Assert.Equal(HarmOutcome.Applied, r.Outcome);
    }

    [Fact]
    public void The_contest_never_makes_a_miss_a_one_die_blow_keeps_its_die_and_the_limits_hold()
    {
        var rules = Contested();
        var dice = new FixedDice().D10(AttackStream, 1).D10(DefenceStream, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10).D10(Damage, 7);
        var a = new Striker(A, "human", 0, 0, false, "Dagger", Dexterity: 20, Melee: 0);    // Strength 0 + a dagger's 1: one die
        var t = new Struck(T, "human", 25, 0, false, true, 100, 100, Dexterity: 100, Dodge: 5, Melee: 5, Armed: true, Guarding: true);
        var r = new HarmResult();
        rules.Resolve(Contact(), in a, in t, dice, r);
        Assert.Equal((-10, 0, 1), (r.ContestNet, r.ContestChange, r.DamagePool));            // floor(1/2) = 0: nothing taken
        Assert.Equal((HarmOutcome.Applied, 1), (r.Outcome, r.Wounds));                       // resolved, never rolled for a hit
        // The change's rule alone.
        Assert.Equal(0, SkillContest.Change(-5, 0, null, 0.5));
        Assert.Equal(-2, SkillContest.Change(-9, 5, null, 0.5));
        Assert.Equal(-3, SkillContest.Change(-3, 9, null, 0.5));
        Assert.Equal(7, SkillContest.Change(7, 3, null, 0.5));
        Assert.Equal(2, SkillContest.Change(7, 3, 2, 0.5));
        Assert.Equal(-5, SkillContest.Change(-9, 5, null, 1.0));                             // a share of 1 takes the whole pool, no more
    }

    [Fact]
    public void Bare_hands_strike_with_Brawl_and_a_fractional_pool_is_made_whole_from_the_contest_s_own_stream()
    {
        var rules = Contested();
        var dice = new FixedDice()
            .Unit("enhanced.skill.pool", 0.9, 0.1)                 // attack 2.5: 2 (0.9 ≥ 0.5); defence 2.5 + Brawl 1: 4 (0.1 < 0.5)
            .D10(AttackStream, 6, 6)
            .D10(DefenceStream, 2, 2, 2, 2)
            .D10(Damage, 2, 2, 2, 2)                               // 40/20 + 0 = 2, + 2 = 4 dice
            .D10("enhanced.harm.soak", 2, 2);                      // bashing: a human soaks it with Stamina 40, two dice
        var a = new Striker(A, "human", 40, 0, false, "", Dexterity: 50, Melee: 5, Brawl: 0);
        var t = new Struck(T, "human", 40, 0, false, true, 100, 100, Dexterity: 50, Dodge: 1, Melee: 4, Armed: false, Guarding: true, Brawl: 1);   // guarding bare-handed: Brawl
        var r = new HarmResult();
        rules.Resolve(Contact(), in a, in t, dice, r);
        Assert.Equal(("brawl", 0, 2, 2), (r.AttackAbilityId, r.AttackAbility, r.AttackPool, r.AttackSuccesses));   // Brawl 0, not Melee 5
        Assert.Equal(("brawl", 4, 0), (r.DefenceAbilityId, r.DefencePool, r.DefenceSuccesses));
        Assert.Equal((2, 4), (r.ContestChange, r.DamagePool));
        Assert.Equal(HarmKind.Bashing, r.Kind);
    }

    [Fact]
    public void The_contest_s_streams_leave_the_staged_harm_s_own_draws_as_they_were_without_it()
    {
        var m = TestData.Sandbox();
        var plain = HarmRules.Build(m.Config, m.Harm);
        var contested = HarmRules.Build(m.Config, m.Harm, m.Skill);
        Assert.Null(plain.Contest);
        var a = new Striker(A, "vampire", 50, 1, true, "BroadSword", Dexterity: 70, Melee: 2);     // 4.5 damage dice: a fraction drawn
        var t = new Struck(T, "vampire", 130, 0, false, true, 100, 100, Dexterity: 50, Dodge: 2, Melee: 1, Armed: true);
        var (r0, r1) = (new HarmResult(), new HarmResult());
        var (s0, s1) = (new WorldRandom(new RandomStreams(99)), new WorldRandom(new RandomStreams(99)));
        var (damage0, damage1) = (new Dictionary<long, int>(), new Dictionary<long, int>());
        for (int i = 0; i < 5; i++)
        {
            plain.Resolve(Contact(i), in a, in t, s0, r0);
            contested.Resolve(Contact(i), in a, in t, s1, r1);
            // The pool's and the soak's draws are the same lists; the damage dice differ in number (the contest changes the
            // pool) but every position of the damage stream gives the same die in both runs (common random numbers).
            static List<Draw> Of(HarmResult r, string p) => [.. r.Draws.Where(d => d.Purpose == p)];
            Assert.Equal(Of(r0, "enhanced.harm.pool"), Of(r1, "enhanced.harm.pool"));
            Assert.Equal(Of(r0, "enhanced.harm.soak"), Of(r1, "enhanced.harm.soak"));
            foreach (var d in Of(r1, Damage)) damage1[d.Position] = d.D10;
            foreach (var d in Of(r0, Damage)) damage0[d.Position] = d.D10;
            Assert.Contains(r1.Draws, d => d.Purpose == AttackStream);
            Assert.Contains(r1.Draws, d => d.Purpose == DefenceStream);
            Assert.Equal(r0.SoakSuccesses, r1.SoakSuccesses);
            Assert.Equal(r0.DamagePool + r1.ContestChange, r1.DamagePool);
        }
        foreach (var (at, die) in damage1) if (damage0.TryGetValue(at, out int d0)) Assert.Equal(d0, die);
        Assert.True(damage0.Keys.Intersect(damage1.Keys).Count() >= 10);
    }

    // ---- in the world ----

    static (World W, ActorSheets Sheets, StringWriter Trace, EntityId Christof, EntityId Guard) World(List<(long Tick, ContactMade Contact)> contacts, params string[] switches)
    {
        var m = TestData.Sandbox(null, [EnhancedModule.CombatTempoOffSwitch, .. switches]);   // the skill's channel at the earlier build's pace (the global tempo: PaceTests)
        var b = new WorldBuilder(11);
        var trace = new StringWriter();
        b.Service(new CombatTrace(trace));
        DeathServices.Register(b);
        b.Component<Combatant>().Component<CombatIntent>();     // the fighting's state alone (no animation here)
        b.Event<ContactMade>();
        b.System(Phase.Contact, 0, "test.contacts", new DelegateSystem(w =>
        {
            foreach (var (tick, c) in contacts)
                if (tick == w.Tick) w.Events<ContactMade>().Publish(c with { Tick = w.Tick });
        }));
        m.Register(b);
        var world = b.Build();
        world.Service<HarmWeapons>().Of = (_, _) => "BroadSword";
        var people = world.Service<PeopleHooks>();
        var (c, g) = (world.Create(), world.Create());
        people.Made(c, PersonRole.Player, "Christof");
        people.Made(g, PersonRole.Townsperson, "rudolfGuard");
        // The combat rules' fixtures dress them now; these tests are the stress fixtures' numbers (Melee 3 each), by name.
        var route = world.Service<IPersonSheets>();
        Assert.True(route.TryTemplate(c, "sandbox.christof", out string error), error);
        Assert.True(route.TryTemplate(g, "sandbox.guard", out error), error);
        CombatServices.Arm(world, c);
        CombatServices.Arm(world, g);
        return (world, world.Service<ActorSheets>(), trace, c, g);
    }

    [Fact]
    public void In_the_world_the_contest_is_published_with_the_harm_and_traced_as_its_own_stage()
    {
        var contacts = new List<(long, ContactMade)>();
        var (w, _, trace, christof, guard) = World(contacts, EnhancedModule.ContestSwitch);
        contacts.Add((2, Contact(7, guard, christof)));
        contacts.Add((4, Contact(8, christof, guard)));
        var harmed = new List<HarmResolved>();
        for (int i = 0; i < 6; i++) { w.Step(); harmed.AddRange(w.Events<HarmResolved>().ThisTick); }
        Assert.Equal(2, harmed.Count);
        var (onChristof, onGuard) = (harmed[0], harmed[1]);
        Assert.True(onChristof.Contested && onGuard.Contested);
        Assert.InRange(onChristof.AttackDice, 5, 6);                          // the guard: 50/20 + Melee 3 = 5.5
        Assert.Equal(15, onChristof.DefenceDice);                             // Christof: 300/20; not guarding, so no Melee; Dodge never
        Assert.Equal(5 + onChristof.ContestChange, onChristof.DamageDice);    // the guard's 3 + 2 dice, after the contest
        Assert.InRange(onChristof.ContestChange, -2, 0);                      // at most half of 5
        Assert.Equal(18, onGuard.AttackDice);
        Assert.Equal(17 + onGuard.ContestChange, onGuard.DamageDice);
        Assert.Equal(onGuard.AttackSuccesses - onGuard.DefenceSuccesses, onGuard.ContestChange);   // a positive net in full (or a negative within half)
        var line = JsonDocument.Parse(trace.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)[1]).RootElement;
        var steps = line.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("step").GetString()).ToList();
        Assert.Equal(["potential", "contest", "conversion", "auto_successes", "soak", "armor", "wounds", "health", "budget", "angle", "severance"], steps);   // the budget and severance after; the armour and the angle
        var contest = line.GetProperty("events")[1];
        Assert.Equal("melee", contest.GetProperty("inputs").GetProperty("attacker_ability").GetString());
        Assert.Equal(onGuard.ContestChange, contest.GetProperty("outputs").GetProperty("change").GetInt32());
        Assert.Equal("gate-11", contest.GetProperty("values")[0].GetProperty("gate").GetString());   // the damage difficulty's gate
        Assert.True(line.GetProperty("module").GetProperty("contest").GetBoolean());
        var purposes = line.GetProperty("random_draws").EnumerateArray().Select(d => d.GetProperty("purpose").GetString()).Distinct().ToList();
        Assert.Contains(AttackStream, purposes);
        Assert.Contains(DefenceStream, purposes);
    }

    [Fact]
    public void Each_fighter_s_tempo_follows_its_Melee_armed_and_its_Brawl_bare_handed_as_the_sheet_changes()
    {
        var (w, sheets, _, christof, guard) = World([]);
        Assert.True(CombatTempos.Registered(w));
        w.Step();
        Assert.Null(w.Service<HarmRules>().Contest);                          // the tempo alone by default
        var k = TestData.Sandbox().Skill;
        static (float, float, float) Of(World w, EntityId e) { var t = w.Get<CombatTempo>(e); return (t.Swing, t.Recovery, t.Rise); }
        Assert.Equal(k.TempoAt(3), Of(w, guard));                              // the guard's Melee 3
        Assert.Equal(k.TempoAt(3), Of(w, christof));
        sheets.SetPurchased(guard, "melee", 10);                              // the arena sets a value after he is made
        w.Step();
        Assert.Equal((1.3f, 1.3f, 1.5f), Of(w, guard));
        w.Service<HarmWeapons>().Of = (_, e) => e == christof ? "" : "BroadSword";   // Christof bare-handed: his Brawl 2
        w.Step();
        Assert.Equal(k.TempoAt(2), Of(w, christof));
        Assert.Equal(k.TempoAt(0), (1f, 1f, 1f));
    }

    [Fact]
    public void The_tempo_system_and_a_contested_harm_allocate_nothing_per_tick_once_warm()
    {
        var contacts = new List<(long, ContactMade)>();
        var (w, sheets, _, christof, guard) = World(contacts, EnhancedModule.ContestSwitch);
        var m = TestData.Sandbox(null, EnhancedModule.ContestSwitch);
        var tempo = new SkillTempoSystem(m.Skill, m.Harm);
        for (int i = 0; i < 10; i++) { tempo.Run(w); w.Step(); }
        Allocations.None("1000 runs of the tempo", () => { for (int i = 0; i < 1000; i++) tempo.Run(w); });
        // A contested harm, resolved again and again on the same result: nothing but the draws' list, already grown.
        var rules = HarmRules.Build(m.Config, m.Harm, m.Skill);
        var random = new WorldRandom(new RandomStreams(3));
        var r = new HarmResult();
        var a = new Striker(A, "vampire", 300, 1, true, "BroadSword", Dexterity: 300, Melee: 3);
        var t = new Struck(T, "human", 60, 0, false, true, 100, 100, Dexterity: 50, Melee: 3, Armed: true, Guarding: true);
        for (int i = 0; i < 20; i++) rules.Resolve(Contact(i), in a, in t, random, r);
        Allocations.None("200 contested resolutions", () => { for (int i = 0; i < 200; i++) rules.Resolve(Contact(i), in a, in t, random, r); });
    }

    [Fact]
    public void Each_channel_turns_off_by_its_switch_and_with_both_off_the_module_has_no_skill()
    {
        var contacts = new List<(long, ContactMade)>();
        // Celerity's attack factor shares the tempo's one writer, so the tempo's store goes only with celerity-attack-off too.
        var (tempoOff, _, _, _, _) = World(contacts, EnhancedModule.TempoOffSwitch, EnhancedModule.ContestSwitch, EnhancedModule.CelerityAttackOffSwitch);
        Assert.False(CombatTempos.Registered(tempoOff));
        Assert.DoesNotContain("Act/90/enhanced.skill-tempo", WorldBuilder.RunOrder(tempoOff));
        Assert.NotNull(tempoOff.Service<HarmRules>().Contest);
        // The skill's tempo off alone: the writer runs for Celerity, and with no Celerity live nobody has a tempo (1.1's timing).
        var (celerityOnly, _, _, cc, cg) = World(contacts, EnhancedModule.TempoOffSwitch);
        Assert.True(CombatTempos.Registered(celerityOnly));
        for (int i = 0; i < 3; i++) celerityOnly.Step();
        Assert.False(celerityOnly.Has<CombatTempo>(cc) || celerityOnly.Has<CombatTempo>(cg));

        var (both, _, trace, christof, guard) = World(contacts, EnhancedModule.TempoOffSwitch, EnhancedModule.CelerityAttackOffSwitch);   // the tempo off, the contest not asked for
        Assert.False(CombatTempos.Registered(both));
        Assert.Null(both.Service<HarmRules>().Contest);
        contacts.Add((2, Contact(9, christof, guard)));
        var harmed = new List<HarmResolved>();
        for (int i = 0; i < 4; i++) { both.Step(); harmed.AddRange(both.Events<HarmResolved>().ThisTick); }
        var h = Assert.Single(harmed);
        Assert.Equal((false, 0, 0, 0, 0, 0), (h.Contested, h.AttackDice, h.AttackSuccesses, h.DefenceDice, h.DefenceSuccesses, h.ContestChange));
        Assert.Equal(17, h.DamageDice);                                       // The first module's pool: 300/20 + 2
        var line = JsonDocument.Parse(trace.ToString().Trim()).RootElement;
        Assert.DoesNotContain(line.GetProperty("events").EnumerateArray(), e => e.GetProperty("step").GetString() == "contest");
        Assert.DoesNotContain(line.GetProperty("random_draws").EnumerateArray(), d => d.GetProperty("purpose").GetString()!.StartsWith("enhanced.skill.", StringComparison.Ordinal));
    }
}
