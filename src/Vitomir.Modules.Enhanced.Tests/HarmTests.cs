// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Text.Json;
using Vitomir.Modules.Enhanced.Rules;
using Vitomir.Modules.Enhanced.Sheets;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Modules.Enhanced.Tests;

/// <summary>
/// The staged harm on the research sandbox: each stage against numbers worked by hand with fixed dice, the
/// refusals by name, determinism, the trace's shape, and the world's path from a contact to wounds and a death. The
/// contacts here are synthetic <see cref="ContactMade"/> records, which only tests may make: in the game every one comes
/// from a swept blade meeting something.
/// </summary>
public sealed class HarmTests
{
    const string Damage = "enhanced.harm.damage", Soak = "enhanced.harm.soak", Pool = "enhanced.harm.pool";

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

    static ContactMade Contact(long id = 1, ContactMet met = ContactMet.Body, bool protect = false, EntityId? attacker = null, EntityId? target = null, long tick = 10) =>
        new(id, attacker ?? A, 1, target ?? T, met, met == ContactMet.Body ? "chest" : null, met == ContactMet.Shield ? ContactMaterial.Wood : ContactMaterial.Cloth,
            tick, new Vector3(1, 2, 1.3f), new Vector3(-1, 0, 0), 9.5f, 9.5f * 1.4f, false, met == ContactMet.Shield, true, protect);

    static HarmRules Rules(string? moduleFolder = null)
    {
        var m = TestData.Sandbox(moduleFolder);
        return HarmRules.Build(m.Config, m.Harm);
    }

    static Striker Striker(int strength, string? weapon = "BroadSword", int potence = 0, bool potenceLearned = false, string species = "vampire") =>
        new(A, species, strength, potence, potenceLearned, weapon);

    // Health on the 1-1000 scale, 100 by default (a wound is 15 of it).
    static Struck Struck(string species, int stamina, int fortitude = 0, bool fortitudeLearned = false, long health = 100, long maximum = 100) =>
        new(T, species, stamina, fortitude, fortitudeLearned, true, health, maximum);

    [Fact]
    public void The_sandbox_builds_every_stage_from_the_profile_and_refuses_nothing()
    {
        var r = Rules();
        Assert.True(r.CanResolve, string.Join("; ", r.Refusals));
        Assert.Equal("raw/20", r.DamageCurve!.Text);
        Assert.Equal(6, r.DamageDifficulty);
        Assert.Equal(6, r.SoakDifficulty);
        Assert.Equal(1.0, r.PotencePerRank);
        Assert.True(r.BashingHalves);
        Assert.True(r.EngineSandboxHealth);
        Assert.Equal((2.0, (string?)null), r.BonusOf(r.Data.Classes["sword_one_hand"]));   // weapon_damage_bonus_one_hand_sword
        Assert.Equal(HarmKind.Lethal, r.Data.ClassOf("BroadSword")!.Kind);
        Assert.Equal(HarmKind.Bashing, r.Data.ClassOf("Punch")!.Kind);
        Assert.Equal(HarmKind.Bashing, r.Data.ClassOf("")!.Kind);                              // bare hands: fists
        Assert.Equal(HarmKind.Aggravated, r.Data.ClassOf("Claw01")!.Kind);
        Assert.Equal("sword_one_hand", r.Data.ClassOf("weapTorchSword")!.Id);                  // case aside: 1.1's own spelling
    }

    [Fact]
    public void The_dice_curves_read_their_numbers_from_the_profile_text()
    {
        var damage = DiceCurve.Parse("raw_to_damage_dice", "raw/20");
        Assert.Equal(15.0, damage.Of(300));
        Assert.Equal(1.25, damage.Of(25));
        Assert.Equal(0.0, damage.Of(0));
        var soak = DiceCurve.Parse("raw_to_soak_dice", "raw/20 for raw<=100; 5+5*ln(1+(raw-100)/100) beyond");
        Assert.Equal(3.0, soak.Of(60));
        Assert.Equal(5.0, soak.Of(100));
        Assert.Equal(5 + 5 * Math.Log(3), soak.Of(300), 5);      // 10.4931: the combat rules' upper stress, not 15
        Assert.Equal(5 + 5 * Math.Log(10.9), soak.Of(1090), 4);
        var e = Assert.Throws<FormulaTextException>(() => DiceCurve.Parse("raw_to_damage_dice", "sqrt(raw)"));
        Assert.Contains("'raw_to_damage_dice'", e.Message);
    }

    [Fact]
    public void A_sword_on_a_human_is_lethal_and_bare_stamina_does_not_soak_it()
    {
        // Strength 300: 300/20 = 15 dice, + 2 for the sword = 17, whole (no draw for a fraction). Seventeen dice at 6:
        // 10 9 8 7 6 and 6 6 6 succeed (8), three ones take three away: 5. Potence 1 learned: 1 automatic success.
        // A human soaks lethal only with Fortitude: no soak dice. Wounds 5 + 1 - 0 = 6; Health 100 -> 10 (15 a wound).
        var dice = new FixedDice().D10(Damage, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 6, 6, 6, 1, 1, 2, 3);
        var r = new HarmResult();
        Rules().Resolve(Contact(), Striker(300, potence: 1, potenceLearned: true), Struck("human", 60), dice, r);
        Assert.Equal(HarmOutcome.Applied, r.Outcome);
        Assert.Equal(HarmKind.Lethal, r.Kind);
        Assert.Equal(15.0, r.StrengthDice);
        Assert.Equal(2.0, r.Bonus);
        Assert.Equal(17.0, r.Potential);
        Assert.Equal(17, r.DamagePool);
        Assert.Equal(5, r.DamageSuccesses);
        Assert.Equal(1, r.AutoSuccesses);
        Assert.False(r.StaminaSoaks);
        Assert.Equal(0, r.SoakPool);
        Assert.Equal(0, r.SoakSuccesses);
        Assert.Equal(6, r.Wounds);
        Assert.Equal((100L, 10L, false), (r.HealthBefore, r.HealthAfter, r.Died));
        Assert.Equal((15, 90L), (r.HealthPerWound, r.HealthLost));
        Assert.Equal(0, dice.Left(Damage));
        Assert.Equal(17, r.Draws.Count);
        Assert.All(r.Draws, d => Assert.Equal(Damage, d.Purpose));
        Assert.Equal(Enumerable.Range(0, 17).Select(i => (long)i), r.Draws.Select(d => d.Position));
    }

    [Fact]
    public void A_vampire_soaks_lethal_with_stamina_and_the_soak_pool_takes_its_fraction_by_one_draw()
    {
        // Stamina 300: 5 + 5 ln 3 = 10.493 soak dice; the draw 0.40 < 0.493 takes the 11. Strength 60 + sword: 3 + 2 = 5.
        var dice = new FixedDice()
            .D10(Damage, 6, 7, 8, 2, 3)                                  // 3 successes
            .Unit(Pool, 0.40)
            .D10(Soak, 6, 6, 1, 2, 3, 4, 5, 9, 10, 1, 7);                // 5 succeed, 2 ones: 3
        var r = new HarmResult();
        Rules().Resolve(Contact(), Striker(60, species: "human"), Struck("vampire", 300), dice, r);
        Assert.True(r.StaminaSoaks);
        Assert.Equal(5 + 5 * Math.Log(3), r.StaminaDice, 5);
        Assert.Equal(11, r.SoakPool);
        Assert.Equal(3, r.SoakSuccesses);
        Assert.Equal(0, r.Wounds);                                         // 3 + 0 - 3
        Assert.Equal(HarmOutcome.Resisted, r.Outcome);
        Assert.Equal(ResistedBy.Soak, r.ResistedBy);                       // soak took every success
        Assert.Equal(100, r.HealthAfter);

        // The same with the draw 0.60: ten soak dice, the seven not rolled: 4 succeed, 2 ones: 2; one wound gets through.
        dice = new FixedDice().D10(Damage, 6, 7, 8, 2, 3).Unit(Pool, 0.60).D10(Soak, 6, 6, 1, 2, 3, 4, 5, 9, 10, 1);
        Rules().Resolve(Contact(), Striker(60, species: "human"), Struck("vampire", 300), dice, r);
        Assert.Equal(10, r.SoakPool);
        Assert.Equal(2, r.SoakSuccesses);
        Assert.Equal((1, HarmOutcome.Applied, 85L), (r.Wounds, r.Outcome, r.HealthAfter));
    }

    [Fact]
    public void A_vampire_s_bashing_is_halved_after_soak_and_a_human_s_is_not()
    {
        // Fists, Strength 200: 10 dice + 0, all tens: 10 successes. Stamina 40: 2 soak dice, 2 and 3: none. Before
        // halving 10; the vampire takes floor(10 / 2) = 5 (75 Health), the human 10 (150: Health 100 -> 0, the human stops).
        var r = new HarmResult();
        Rules().Resolve(Contact(), Striker(200, weapon: "Punch", species: "human"), Struck("vampire", 40),
            new FixedDice().D10(Damage, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10).D10(Soak, 2, 3), r);
        Assert.Equal(HarmKind.Bashing, r.Kind);
        Assert.Equal((10, true, 5), (r.WoundsBeforeHalving, r.Halved, r.Wounds));
        Assert.Equal(25, r.HealthAfter);

        Rules().Resolve(Contact(), Striker(200, weapon: "Punch", species: "human"), Struck("human", 40),
            new FixedDice().D10(Damage, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10).D10(Soak, 2, 3), r);
        Assert.Equal((10, false, 10), (r.WoundsBeforeHalving, r.Halved, r.Wounds));
        Assert.True(r.StaminaSoaks);                                       // a human soaks bashing with Stamina
        Assert.Equal((0L, true), (r.HealthAfter, r.Died));
    }

    [Fact]
    public void Potence_gives_its_automatic_successes_only_where_its_passive_is_learned()
    {
        var r = new HarmResult();
        static FixedDice Dice() => new FixedDice().D10(Damage, 2, 2, 2, 2, 2, 2, 2);   // Strength 100: 5 + 2 = 7 dice, none succeeds
        Rules().Resolve(Contact(), Striker(100, potence: 3, potenceLearned: true), Struck("human", 25), Dice(), r);
        Assert.Equal((0, 3, 3), (r.DamageSuccesses, r.AutoSuccesses, r.Wounds));
        Rules().Resolve(Contact(), Striker(100, potence: 3, potenceLearned: false), Struck("human", 25), Dice(), r);
        Assert.Equal((0, 0), (r.AutoSuccesses, r.Wounds));               // a rank alone is not the option
    }

    [Fact]
    public void Fortitude_soaks_where_its_contract_allows_and_alone_soaks_claws()
    {
        // Claws (aggravated), Strength 100: 5 + 1 = 6 dice, 4 successes. A vampire's Stamina does not soak aggravated.
        var r = new HarmResult();
        Rules().Resolve(Contact(), Striker(100, weapon: "Claw01"), Struck("vampire", 300),
            new FixedDice().D10(Damage, 6, 7, 8, 9, 2, 3), r);
        Assert.Equal(HarmKind.Aggravated, r.Kind);
        Assert.False(r.StaminaSoaks);
        Assert.False(r.FortitudeSoaks);
        Assert.Equal((0, 4), (r.SoakPool, r.Wounds));

        // With Fortitude 2 learned: two soak dice (ENGINE_SANDBOX, one per rank), 6 and 1: none.
        Rules().Resolve(Contact(), Striker(100, weapon: "Claw01"), Struck("vampire", 300, fortitude: 2, fortitudeLearned: true),
            new FixedDice().D10(Damage, 6, 7, 8, 9, 2, 3).D10(Soak, 6, 7), r);
        Assert.True(r.FortitudeSoaks);
        Assert.Equal((2.0, 0.0, 2, 2, 2), (r.FortitudeDice, r.StaminaDice, r.SoakPool, r.SoakSuccesses, r.Wounds));

        // Fortitude's rank without its passive soaks nothing; with it, against a sword, it adds to Stamina's dice.
        Rules().Resolve(Contact(), Striker(100, weapon: "Claw01"), Struck("vampire", 300, fortitude: 2, fortitudeLearned: false),
            new FixedDice().D10(Damage, 6, 7, 8, 9, 2, 3), r);
        Assert.False(r.FortitudeSoaks);
        Rules().Resolve(Contact(), Striker(100), Struck("vampire", 100, fortitude: 2, fortitudeLearned: true),
            new FixedDice().D10(Damage, 2, 2, 2, 2, 2, 2, 2).D10(Soak, 2, 2, 2, 2, 2, 2, 2), r);
        Assert.Equal((5.0, 2.0, 7), (r.StaminaDice, r.FortitudeDice, r.SoakPool));
    }

    [Fact]
    public void Refusals_are_by_name_and_draw_no_dice()
    {
        var r = new HarmResult();
        var dice = new FixedDice();
        Rules().Resolve(Contact(), Striker(100, weapon: "FryingPan"), Struck("human", 25), dice, r);
        Assert.Equal(HarmOutcome.Unresolved, r.Outcome);
        Assert.Contains("weapon 'FryingPan' has no class", r.Refusal);
        Rules().Resolve(Contact(), Striker(100, weapon: null), Struck("human", 25), dice, r);
        Assert.Contains("is not known (it has no character)", r.Refusal);
        Rules().Resolve(Contact(), Striker(100), Struck("ghoul", 25), dice, r);
        Assert.Contains("species 'ghoul'", r.Refusal);
        Rules().Resolve(Contact(), Striker(100), new Struck(T, "human", 25, 0, false, false, 0, 0), dice, r);
        Assert.Contains("has no Health", r.Refusal);
        Assert.Contains("health_max_purchase_curve", r.Refusal);
        Assert.Empty(r.Draws);
    }

    [Fact]
    public void A_null_profile_value_refuses_every_contact_by_its_name_and_gate()
    {
        using var copy = new TestData.Copy();
        copy.Edit(TestData.SandboxFile, t => t.Replace("\"raw_to_damage_dice\": \"raw/20\"", "\"raw_to_damage_dice\": null"));
        var rules = Rules(copy.Folder);
        Assert.False(rules.CanResolve);
        var why = Assert.Single(rules.Refusals);
        Assert.Contains("value 'raw_to_damage_dice' is unresolved", why);
        Assert.Contains("gate-09", why);
        var r = new HarmResult();
        rules.Resolve(Contact(), Striker(300), Struck("human", 25), new FixedDice(), r);
        Assert.Equal(HarmOutcome.Unresolved, r.Outcome);
        Assert.Contains("raw_to_damage_dice", r.Refusal);

        // A sword class whose addend is a null profile value refuses that class only.
        using var copy2 = new TestData.Copy();
        copy2.Edit(TestData.SandboxFile, t => t.Replace("\"weapon_damage_bonus_one_hand_sword\": 2.0", "\"weapon_damage_bonus_one_hand_sword\": null"));
        var rules2 = Rules(copy2.Folder);
        Assert.True(rules2.CanResolve);
        rules2.Resolve(Contact(), Striker(300), Struck("human", 25), new FixedDice(), r);
        Assert.Contains("value 'weapon_damage_bonus_one_hand_sword' is unresolved", r.Refusal);
        rules2.Resolve(Contact(), Striker(20, weapon: "Mace"), Struck("human", 20), new FixedDice().D10(Damage, 2, 2, 2).D10(Soak, 2), r);
        Assert.Equal(HarmOutcome.Resisted, r.Outcome);
        Assert.Equal(ResistedBy.NoDamage, r.ResistedBy);                   // the damage made nothing
    }

    [Fact]
    public void Wrong_harm_data_is_refused_at_configuration_with_every_reason()
    {
        using var copy = new TestData.Copy();
        copy.Edit("rules/harm.json", t => t.Replace("\"hp_per_wound\": 15", "\"hp_per_wound\": 0").Replace("\"Mace\": \"blunt_one_hand\"", "\"Mace\": \"club\""));
        var e = Assert.Throws<ModuleRefusedException>(() => TestData.Sandbox(copy.Folder));
        Assert.Contains(e.Reasons, x => x.Contains("health.hp_per_wound must be at least 1"));
        Assert.Contains(e.Reasons, x => x.Contains("weapons.Mace names class 'club'"));
    }

    // ---- the world's path ----

    /// <summary>
    /// A sandbox world with the module, a test system publishing <paramref name="contacts"/> in Contact on the ticks given, a
    /// trace, and the platform's death; the Abilities' channels off (the module as first made) unless <paramref name="skill"/>.
    /// </summary>
    static (World W, ActorSheets Sheets, StringWriter Trace) World(ulong seed, List<(long Tick, ContactMade Contact)> contacts, bool death = true, bool skill = false)
    {
        var m = skill ? TestData.Sandbox() : TestData.SandboxTempoOff();
        var b = new WorldBuilder(seed);
        var trace = new StringWriter();
        b.Service(new CombatTrace(trace));
        if (death) DeathServices.Register(b);
        b.Event<ContactMade>();
        b.System(Phase.Contact, 0, "test.contacts", new DelegateSystem(w =>
        {
            foreach (var (tick, c) in contacts)
                if (tick == w.Tick) w.Events<ContactMade>().Publish(c with { Tick = w.Tick });
        }));
        m.Register(b);
        var world = b.Build();
        world.Service<HarmWeapons>().Of = (_, _) => "BroadSword";
        return (world, world.Service<ActorSheets>(), trace);
    }

    static (EntityId Christof, EntityId Guard) Fighters(World w)
    {
        var people = w.Service<PeopleHooks>();
        var (c, g) = (w.Create(), w.Create());
        people.Made(c, PersonRole.Player, "Christof");
        people.Made(g, PersonRole.Townsperson, "rudolfGuard");
        // The combat rules' fixtures dress them now; these tests are the high-stat stress fixtures' numbers, given by name.
        var route = w.Service<IPersonSheets>();
        Assert.True(route.TryTemplate(c, "sandbox.christof", out string error), error);
        Assert.True(route.TryTemplate(g, "sandbox.guard", out error), error);
        return (c, g);
    }

    static List<string> Lines(StringWriter s) => [.. s.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r'))];

    [Fact]
    public void A_sword_blow_on_the_guard_wounds_him_until_he_dies_once_and_the_blows_on_his_body_are_denied()
    {
        var contacts = new List<(long, ContactMade)>();
        var (w, sheets, trace) = World(7, contacts);
        var (christof, guard) = Fighters(w);
        for (int i = 0; i < 6; i++) contacts.Add((5 + i * 10, Contact(100 + i, attacker: christof, target: guard)));
        var harmed = new List<HarmResolved>();
        var died = new List<ActorDied>();
        for (int t = 0; t < 70; t++)
        {
            w.Step();
            harmed.AddRange(w.Events<HarmResolved>().ThisTick);
            died.AddRange(w.Events<ActorDied>().ThisTick);
        }
        // No harm on the dead — the blows after the killing one give no HarmResolved.
        Assert.InRange(harmed.Count, 1, 5);
        Assert.All(harmed, h => Assert.Equal(HarmKind.Lethal, h.Kind));
        Assert.All(harmed, h => Assert.False(h.Contested));               // the contest is off: the first module's numbers
        Assert.All(harmed, h => Assert.Equal(0, h.SoakDice));           // a human: no Stamina against lethal
        Assert.Equal(100, harmed[0].HealthBefore);                        // Health on the 1-1000 scale
        Assert.Equal(17, harmed[0].DamageDice);                           // 300/20 + 2
        Assert.Equal(1, harmed[0].AutoSuccesses);                         // Christof's Potence 1
        for (int i = 1; i < harmed.Count; i++) Assert.Equal(harmed[i - 1].HealthAfter, harmed[i].HealthBefore);
        Assert.Equal(0, harmed[^1].HealthAfter);
        var d = Assert.Single(died);
        Assert.Equal((guard, christof, HarmKind.Lethal), (d.Who, d.Killer, d.Kind));
        Assert.True(w.Has<Dead>(guard));
        Assert.Equal(0, sheets.Resource(guard, "health")!.Value.Current);
        Assert.Equal(100, sheets.Resource(christof, "health")!.Value.Current);
        var lines = Lines(trace).Select(l => JsonDocument.Parse(l).RootElement).ToList();
        Assert.Equal(6, lines.Count);                                    // every contact read is traced
        Assert.All(lines.Skip(harmed.Count), l =>
        {
            Assert.Equal("Denied", l.GetProperty("outcome").GetString());
            Assert.Equal("IllegalTarget", l.GetProperty("admission").GetProperty("reason").GetString());
            Assert.Contains("is dead: no harm on the dead", l.GetProperty("reason").GetString());
            Assert.Equal(0, l.GetProperty("random_draws").GetArrayLength());
        });
        var ledger = w.Service<HarmLedger>();
        Assert.Equal((6, harmed.Count, 1), (ledger.Read, ledger.Resolved, ledger.Deaths));
    }

    [Fact]
    public void A_protected_person_a_shield_and_the_level_take_no_harm_and_a_duplicate_is_rejected()
    {
        var contacts = new List<(long, ContactMade)>();
        var (w, sheets, trace) = World(7, contacts);
        var (christof, guard) = Fighters(w);
        contacts.Add((2, Contact(1, attacker: christof, target: guard, protect: true)));
        contacts.Add((3, Contact(2, ContactMet.Shield, attacker: christof, target: guard)));
        contacts.Add((4, Contact(3, ContactMet.Level, attacker: christof, target: EntityId.None)));
        contacts.Add((5, Contact(3, attacker: christof, target: guard)));     // the same contact again
        int harmed = 0;
        for (int t = 0; t < 8; t++) { w.Step(); harmed += w.Events<HarmResolved>().ThisTick.Count; }
        Assert.Equal(0, harmed);
        Assert.Equal(100, sheets.Resource(guard, "health")!.Value.Current);
        var outcomes = Lines(trace).Select(l => JsonDocument.Parse(l).RootElement.GetProperty("outcome").GetString()).ToList();
        Assert.Equal(["Denied", "Blocked", "Missed"], outcomes);
        Assert.Equal(1, w.Service<HarmLedger>().Duplicates);
    }

    [Fact]
    public void The_same_seed_and_contacts_give_the_same_dice_trace_and_digest_and_another_seed_other_dice()
    {
        static (string Trace, ulong Digest) Run(ulong seed)
        {
            var contacts = new List<(long, ContactMade)>();
            var (w, _, trace) = World(seed, contacts);
            var (christof, guard) = Fighters(w);
            for (int i = 0; i < 4; i++)
            {
                contacts.Add((3 + i * 7, Contact(10 + 2 * i, attacker: christof, target: guard)));
                contacts.Add((4 + i * 7, Contact(11 + 2 * i, attacker: guard, target: christof)));
            }
            for (int t = 0; t < 40; t++) w.Step();
            return (trace.ToString(), w.Digest());
        }
        var a = Run(11);
        var b = Run(11);
        var c = Run(12);
        Assert.Equal(a.Trace, b.Trace);
        Assert.Equal(a.Digest, b.Digest);
        Assert.NotEqual(a.Trace, c.Trace);
    }

    [Fact]
    public void The_trace_line_carries_the_contact_every_stage_the_dice_with_their_streams_the_profile_and_the_health()
    {
        var contacts = new List<(long, ContactMade)>();
        var (w, _, trace) = World(5, contacts);
        var (christof, guard) = Fighters(w);
        contacts.Add((2, Contact(42, attacker: guard, target: christof)));
        for (int t = 0; t < 4; t++) w.Step();
        var root = JsonDocument.Parse(Assert.Single(Lines(trace))).RootElement;
        Assert.Equal(HarmTrace.Schema, root.GetProperty("schema").GetString());
        Assert.Equal("contact:42", root.GetProperty("request_id").GetString());
        Assert.Equal(42, root.GetProperty("contact_id").GetInt64());
        var module = root.GetProperty("module");
        Assert.Equal(EnhancedModule.SandboxProfileId, module.GetProperty("profile_id").GetString());
        Assert.Equal(64, module.GetProperty("config_hash").GetString()!.Length);
        Assert.True(module.GetProperty("nonshipping_research").GetBoolean());
        Assert.Equal("chest", root.GetProperty("evidence").GetProperty("site").GetString());
        Assert.Equal(9.5, root.GetProperty("evidence").GetProperty("impact_speed").GetDouble());
        var steps = root.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("step").GetString()).ToList();
        Assert.Equal(["potential", "conversion", "auto_successes", "soak", "armor", "wounds", "health", "budget", "angle", "severance"], steps);   // the budget and severance after; the armour after the soak, the angle before severance
        var potential = root.GetProperty("events")[0];
        Assert.Equal(60, potential.GetProperty("inputs").GetProperty("strength_effective").GetInt32());   // the guard's
        Assert.Equal(5.0, potential.GetProperty("outputs").GetProperty("potential").GetDouble());           // 60/20 + 2
        var gates = potential.GetProperty("values").EnumerateArray().ToDictionary(v => v.GetProperty("key").GetString()!, v => v.GetProperty("gate").GetString());
        Assert.Equal("gate-09", gates["raw_to_damage_dice"]);
        Assert.Equal("gate-10", gates["weapon_damage_bonus_one_hand_sword"]);
        var draws = root.GetProperty("random_draws").EnumerateArray().ToList();
        Assert.Equal(5, draws.Count(d => d.GetProperty("purpose").GetString() == Damage));
        Assert.Contains(draws, d => d.GetProperty("purpose").GetString() == Pool && d.TryGetProperty("unit", out _));   // Stamina 300: 10.49 soak dice
        Assert.True(draws.Where(d => d.GetProperty("purpose").GetString() == Damage).Select(d => d.GetProperty("position").GetInt64()).SequenceEqual([0L, 1, 2, 3, 4]));
        Assert.Equal(100, root.GetProperty("health_before").GetInt64());
        var injury = Assert.Single(root.GetProperty("injuries").EnumerateArray().ToList());
        Assert.Equal(root.GetProperty("health_after").GetInt64() - 100, injury.GetProperty("actual_health_delta").GetInt64());
        // The injury units (wounds) apart from the Health they cost, and the translator in the health stage.
        Assert.Equal(-15 * injury.GetProperty("injury_units").GetInt64(), injury.GetProperty("actual_health_delta").GetInt64());
        Assert.Equal(15, injury.GetProperty("health_per_injury_unit").GetInt32());
        var health = root.GetProperty("events")[6];   // the armour stage before the wounds
        Assert.Equal(15, health.GetProperty("inputs").GetProperty("hp_per_wound").GetInt32());
        Assert.Equal(-injury.GetProperty("actual_health_delta").GetInt64(), health.GetProperty("outputs").GetProperty("health_lost").GetInt64());
        Assert.Equal(0, root.GetProperty("flurry").GetInt32());
    }

    [Fact]
    public void The_sheet_route_gives_a_template_or_a_save_and_refuses_a_bad_one_by_name()
    {
        var (w, sheets, _) = TestData.World();
        var e = w.Create();
        var route = w.Service<IPersonSheets>();
        string line = route.Give(e, File.ReadAllText(TestData.File("sheets/guard.json")), "guard.json");
        Assert.Equal("sandbox.guard", sheets.Template(e));
        Assert.Equal(100, sheets.Resource(e, "health")!.Value.Maximum);
        Assert.Contains("Health 100/100 (engine-sandbox)", line);
        // A save, with its own Health (its purchase health_pool 12, and 3 of the 12).
        Assert.True(route.TrySet(e, "health_pool", 12, out string why), why);
        sheets.SetResource(e, new ResourceState("health", 3, 12, 0, 0));
        string save = SheetJson.Write(sheets.Export(e));
        var f = w.Create();
        route.Give(f, save, "saved.json");
        Assert.Equal(new ResourceState("health", 3, 12, 0, 0), sheets.Resource(f, "health"));
        var x = Assert.Throws<PersonSheetException>(() => route.Give(f, File.ReadAllText(TestData.File("sheets/guard.json")).Replace("\"strength\": 60", "\"strength\": 1001"), "bad.json"));
        Assert.Contains("purchased strength = 1001 is outside 0..1000", x.Message);
        Assert.Throws<PersonSheetException>(() => route.Give(f, "{\"schema\":\"nope\"}", "nope.json"));
    }

    [Fact]
    public void The_route_offers_the_fighters_templates_by_id_and_takes_their_files_whole_with_the_sandbox_Health()
    {
        var (w, sheets, _) = TestData.World();
        var route = w.Service<IPersonSheets>();
        string error;
        Assert.Equal(["sandbox.townsperson", "sandbox.archer", "sandbox.christof", "sandbox.developed_ninth", "sandbox.elite_stress", "sandbox.guard",
                      "sandbox.guard_trained", "sandbox.human_reference", "sandbox.knight", "sandbox.newly_embraced", "sandbox.player",
                      "sandbox.raw1000_stress", "sandbox.szlachta", "sandbox.townsperson_reference", "sandbox.watchman"], route.Templates);   // the combat rules' eight fixtures too; the player's; the szlachta's; the archer's
        // The combat rules' fixtures dress the guards and the watch, the townsfolk; the stress fixtures nobody.
        var g = w.Create();
        w.Service<PeopleHooks>().Made(g, PersonRole.Townsperson, "rudolfGuard");
        Assert.Equal("sandbox.guard_trained", sheets.Template(g));
        Assert.True(route.TryTemplate(g, "sandbox.newly_embraced", out error), error);
        // The other six Attributes the rules package's Option B means (Wits 14), no longer a flat 19.
        Assert.Equal(("vampire", 15, 23, 20, 14, 0, 1), (sheets.Species(g), sheets.Purchased(g, "strength"), sheets.Purchased(g, "dexterity"),
            sheets.Purchased(g, "stamina"), sheets.Purchased(g, "wits"), sheets.Purchased(g, "melee"), sheets.Purchased(g, "alertness")));
        Assert.Empty(sheets.Powers(g));
        var e = w.Create();
        Assert.True(route.TryTemplate(e, "sandbox.christof", out error), error);
        Assert.True(route.TryRead(e, "strength", out int strength));
        Assert.Equal(300, strength);
        Assert.Equal(100, sheets.Resource(e, "health")!.Value.Maximum);                      // given with the template
        var f = w.Create();
        Assert.True(route.TryImport(f, File.ReadAllText(TestData.File("sheets/watchman.json")), out error), error);   // a template's file
        Assert.Equal("sandbox.watchman", sheets.Template(f));
        Assert.Equal(100, sheets.Resource(f, "health")!.Value.Current);
        Assert.False(route.TryImport(f, "{\"schema\":\"nope\"}", out error));
        Assert.Contains("schema is 'nope'", error);
        Assert.False(route.TryTemplate(f, "sandbox.dragon", out error));
        Assert.Contains("sandbox.guard", error);                                              // the refusal names the ones there are
    }

    [Fact]
    public void A_hit_that_deals_no_wound_says_why_no_damage_soak_or_halving()
    {
        // The "resisted" split: the outcome stays Resisted; why is HarmResult.ResistedBy (and HarmResolved's, at its end).
        var r = new HarmResult();
        // A mace (Strength 20: 1 die + 2) on a vampire (Stamina 20: 1 soak die, bashing).
        Rules().Resolve(Contact(), Striker(20, weapon: "Mace"), Struck("vampire", 20), new FixedDice().D10(Damage, 2, 2, 2).D10(Soak, 7), r);
        Assert.Equal((HarmOutcome.Resisted, ResistedBy.NoDamage, 0), (r.Outcome, r.ResistedBy, r.DamageSuccesses));
        Rules().Resolve(Contact(), Striker(20, weapon: "Mace"), Struck("vampire", 20), new FixedDice().D10(Damage, 7, 2, 2).D10(Soak, 7), r);
        Assert.Equal((HarmOutcome.Resisted, ResistedBy.Soak, 1, 1), (r.Outcome, r.ResistedBy, r.DamageSuccesses, r.SoakSuccesses));
        Rules().Resolve(Contact(), Striker(20, weapon: "Mace"), Struck("vampire", 20), new FixedDice().D10(Damage, 7, 2, 2).D10(Soak, 2), r);
        Assert.Equal((HarmOutcome.Resisted, ResistedBy.Halving, 1, 0, true), (r.Outcome, r.ResistedBy, r.WoundsBeforeHalving, r.Wounds, r.Halved));
        Rules().Resolve(Contact(), Striker(20, weapon: "Mace"), Struck("vampire", 20), new FixedDice().D10(Damage, 7, 8, 2).D10(Soak, 2), r);
        Assert.Equal((HarmOutcome.Applied, ResistedBy.None, 1), (r.Outcome, r.ResistedBy, r.Wounds));
    }

    [Fact]
    public void The_published_harm_and_its_trace_line_say_why_a_hit_dealt_no_wound()
    {
        // HarmResolved's ResistedBy (at its end) and the trace's "resisted_by" agree with the numbers, contact by contact.
        var contacts = new List<(long, ContactMade)>();
        var (w, _, trace) = World(3, contacts);
        var (christof, guard) = Fighters(w);
        for (int i = 0; i < 24; i++) contacts.Add((1 + i, Contact(1 + i, attacker: i % 2 == 0 ? christof : guard, target: i % 2 == 0 ? guard : christof)));
        var harms = new List<HarmResolved>();
        for (int t = 0; t < 30; t++) { w.Step(); harms.AddRange(w.Events<HarmResolved>().ThisTick); }
        Assert.True(harms.Count >= 4, $"{harms.Count} harms");
        var lines = Lines(trace).Select(l => JsonDocument.Parse(l).RootElement).ToDictionary(l => l.GetProperty("contact_id").GetInt64());
        foreach (var h in harms)
        {
            var want = h.Wounds > 0 ? ResistedBy.None
                : h.DamageSuccesses + h.AutoSuccesses == 0 ? ResistedBy.NoDamage
                : h.DamageSuccesses + h.AutoSuccesses <= h.SoakSuccesses ? ResistedBy.Soak : ResistedBy.Halving;
            Assert.Equal(want, h.ResistedBy);
            var line = lines[h.ContactId];
            if (h.Wounds > 0) Assert.False(line.TryGetProperty("resisted_by", out _));
            else Assert.Equal(want.ToString(), line.GetProperty("resisted_by").GetString());
        }
    }
}
