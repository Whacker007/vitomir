// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Text.Json;
using Vitomir.Modules.Enhanced.Rules;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Modules.Enhanced.Tests;

/// <summary>
/// The rules package's corrections to the enhanced module (their review of the module):
/// a blow the armour alone turned to nothing is resisted by <see cref="ResistedBy.Armor"/>, apart from soak and halving;
/// the legs' severance labelled UNAVAILABLE in the data and the trace, not merely null. The contest's Dodge
/// only in the dodge's burst is <see cref="DodgeDataTests"/>' and below. Every die a 10 (<see cref="MaxDice"/>) so a blow's
/// harm is exact.
/// </summary>
public sealed class RulesCorrectionsTests
{
    /// <summary>Every d10 a 10 and every unit draw 0 (a fractional pool rounds up): a blow's harm exact.</summary>
    sealed class MaxDice : IDeterministicRandom
    {
        readonly Dictionary<string, long> drawn = [];
        public int NextD10(string purpose) { Count(purpose); return 10; }
        public double NextUnitInterval(string purpose) { Count(purpose); return 0; }
        public long Position(string purpose) => drawn.TryGetValue(purpose, out long n) ? n : 0;
        void Count(string purpose) => drawn[purpose] = Position(purpose) + 1;
    }

    sealed record Rig(World W, EntityId Attacker, EntityId Target, List<(long Tick, ContactMade Contact)> Contacts, StringWriter Trace, List<HarmResolved> Harm)
    {
        long next = 1;

        public void Hit(string site, int swing)
        {
            Contacts.Add((W.Tick, new ContactMade(next++, Attacker, swing, Target, ContactMet.Body, site, ContactMaterial.Cloth, 0,
                new Vector3(1, 2, 1.3f), new Vector3(-1, 0, 0), 9.5f, 13f, false, false, true, false)));
            W.Step();
            Harm.AddRange(W.Events<HarmResolved>().ThisTick);
        }

        public List<JsonElement> Lines() => [.. Trace.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => JsonDocument.Parse(l).RootElement)
            .Where(l => l.GetProperty("schema").GetString() == HarmTrace.Schema)];
    }

    /// <summary>
    /// The combat rules' newly Embraced (Strength <paramref name="strength"/>, the <paramref name="weapon"/>) against <paramref name="target"/>'s
    /// sheet (Health 1000 bought: nothing is cut but by its own threshold) wearing <paramref name="wears"/>.
    /// </summary>
    static Rig Make(string[] wears, int strength, string weapon = "Rapier", string target = "sandbox.guard_trained", string character = "rudolfGuard", params string[] switches)
    {
        var m = TestData.Sandbox(null, switches);
        var b = new WorldBuilder(5);
        b.Service<IDeterministicRandom>(new MaxDice());
        var trace = new StringWriter();
        b.Service(new CombatTrace(trace));
        DeathServices.Register(b);
        b.Event<ContactMade>();
        var contacts = new List<(long, ContactMade)>();
        b.System(Phase.Contact, 0, "test.contacts", new DelegateSystem(w =>
        {
            foreach (var (tick, c) in contacts)
                if (tick == w.Tick) w.Events<ContactMade>().Publish(c with { Tick = w.Tick });
        }));
        m.Register(b);
        var world = b.Build();
        world.Service<HarmWeapons>().Of = (_, _) => weapon;
        var people = world.Service<PeopleHooks>();
        var (a, t) = (world.Create(), world.Create());
        people.Made(a, PersonRole.Player, "Christof");
        people.Made(t, PersonRole.Townsperson, character);
        var route = world.Service<IPersonSheets>();
        Assert.True(route.TryTemplate(a, "sandbox.newly_embraced", out string error), error);
        Assert.True(route.TryTemplate(t, target, out error), error);
        Assert.True(route.TrySet(t, "health_pool", 1000, out error), error);
        Assert.True(route.TrySet(a, "strength", strength, out error), error);
        if (wears.Length > 0) Assert.True(route.TryWear(t, wears, out error), error);
        return new Rig(world, a, t, contacts, trace, []);
    }

    static JsonElement Stage(JsonElement line, string step) => line.GetProperty("events").EnumerateArray().Single(e => e.GetProperty("step").GetString() == step);

    [Fact]
    public void A_blow_the_armour_alone_turned_to_nothing_is_resisted_by_the_armour_apart_from_soak()
    {
        // Strength 15 with the rapier: 0.75 + 2 = 2.75 dice, whole 3, three successes. The trained guard is a human: his Stamina
        // soaks no lethal harm, so his body alone would take 3 wounds. The mail shirt's 2.25 lethal dice (whole 3, three
        // successes) take all three: no wound, kept off by the armour alone — Armor, not Soak.
        var rig = Make(["mail_shirt"], strength: 15);
        rig.Hit("chest", 1);           // under the mail
        rig.Hit("left forearm", 2);    // a gap: three wounds
        var h = rig.Harm;
        Assert.Equal(2, h.Count);
        Assert.Equal((0, 3, ResistedBy.Armor, "mail_shirt"), (h[0].Wounds, h[0].ArmorKept, h[0].ResistedBy, h[0].Armor));
        Assert.Equal((3, ResistedBy.None), (h[1].Wounds, h[1].ResistedBy));
        Assert.Equal("Armor", rig.Lines()[0].GetProperty("resisted_by").GetString());

        // With armour off the same blow on the chest wounds: the armour made the difference.
        var off = Make(["mail_shirt"], strength: 15, switches: EnhancedModule.ArmorOffSwitch);
        off.Hit("chest", 1);
        Assert.Equal((3, ResistedBy.None), (off.Harm[0].Wounds, off.Harm[0].ResistedBy));
    }

    [Fact]
    public void A_blow_the_body_soak_alone_would_have_stopped_stays_soak_and_no_damage_stays_no_damage()
    {
        // The rule itself, stage by stage: Armor only when the body's soak (and halving) alone would have let a wound through.
        var r = new HarmResult { Outcome = HarmOutcome.Resisted, ResistedBy = ResistedBy.Soak, Wounds = 0, ArmorKept = 2, ArmorSuccesses = 2 };
        HarmSystem.ArmorResisted(r);
        Assert.Equal(ResistedBy.Armor, r.ResistedBy);
        r = new HarmResult { Outcome = HarmOutcome.Resisted, ResistedBy = ResistedBy.Soak, Wounds = 0, ArmorKept = 0, ArmorSuccesses = 2 };   // the body's soak would have done it
        HarmSystem.ArmorResisted(r);
        Assert.Equal(ResistedBy.Soak, r.ResistedBy);
        r = new HarmResult { Outcome = HarmOutcome.Resisted, ResistedBy = ResistedBy.NoDamage, Wounds = 0 };
        HarmSystem.ArmorResisted(r);
        Assert.Equal(ResistedBy.NoDamage, r.ResistedBy);
        r = new HarmResult { Outcome = HarmOutcome.Applied, ResistedBy = ResistedBy.None, Wounds = 1, ArmorKept = 2 };                       // a wound through the armour
        HarmSystem.ArmorResisted(r);
        Assert.Equal(ResistedBy.None, r.ResistedBy);

        // In the world: a vampire's Stamina soaks lethal harm. Christof (newly Embraced, Stamina 20: 1 die) struck by himself at
        // Strength 15 (3 successes) in a mail shirt: the body's die and the mail's three keep off all three — the armour kept off
        // two (3 − 1), so it is the armour's.
        var vamp = Make(["mail_shirt"], strength: 15, target: "sandbox.newly_embraced", character: "Christof");
        vamp.Hit("chest", 1);
        Assert.Equal((0, ResistedBy.Armor), (vamp.Harm[0].Wounds, vamp.Harm[0].ResistedBy));
        Assert.True(vamp.Harm[0].ArmorKept > 0);
    }

    [Fact]
    public void A_blow_on_a_leg_is_labelled_unavailable_for_severance_in_the_data_and_the_trace()
    {
        var d = TestData.Sandbox().Severance;
        foreach (var body in new[] { d.DefaultBody, d.Body("szlachta")! })
        {
            foreach (var part in new[] { BodyRegion.LeftThigh, BodyRegion.RightThigh, BodyRegion.LeftShin, BodyRegion.RightShin, BodyRegion.LeftFoot, BodyRegion.RightFoot })
                Assert.True(body.Unavailable(part) && body.Part(part) is null, $"{body.Id}: {part}");
            Assert.False(body.Unavailable(BodyRegion.Head));        // the head is taken at the neck: not severable, not "unavailable"
            Assert.False(body.Unavailable(BodyRegion.LeftHand));
        }

        // Strength 1000 (52 dice: 780 Health after soak) on the thigh of a 1000-Health guard: a forearm's 600 would go; the leg is not cut.
        var rig = Make([], strength: 1000);
        rig.Hit("left thigh", 1);
        rig.Hit("head", 2);
        Assert.Equal(BodyRegion.None, rig.Harm[0].Severed);
        var lines = rig.Lines();
        var leg = Stage(lines[0], "severance").GetProperty("outputs");
        Assert.Equal(("Unavailable", "UNAVAILABLE"), (leg.GetProperty("verdict").GetString(), leg.GetProperty("availability").GetString()));
        Assert.Contains("UNAVAILABLE", leg.GetProperty("reason").GetString());
        Assert.Contains("its movement is not built", leg.GetProperty("reason").GetString());
        // The counterfactual, trace only — the thigh read as the upper arm (75 % of 1000: 750); the
        // blow's 780 Health (52 successes, no soak, no angle measured: cut share 1) would have cut it. Nothing was cut.
        var cf = leg.GetProperty("counterfactual");
        Assert.Equal(("left upper arm", 0.75, 750, 780.0, true), (cf.GetProperty("read_as").GetString(), cf.GetProperty("threshold_share").GetDouble(),
            cf.GetProperty("threshold").GetInt32(), cf.GetProperty("cut_harm").GetDouble(), cf.GetProperty("would_cut").GetBoolean()));
        Assert.Equal((uint)0, SeveranceRules.Missing(rig.W, rig.Target));
        var head = Stage(lines[1], "severance").GetProperty("outputs");
        Assert.Equal(("NotSeverable", "NOT_SEVERABLE"), (head.GetProperty("verdict").GetString(), head.GetProperty("availability").GetString()));
        Assert.False(head.TryGetProperty("counterfactual", out _));          // the head has none: it is taken at the neck
    }

    [Fact]
    public void A_part_that_is_neither_an_object_null_nor_unavailable_is_refused_by_name()
    {
        string text = File.ReadAllText(Path.Combine(TestData.ModuleFolder, "Data", "rules", "severance.json"));
        Assert.Contains("\"left foot\": \"unavailable\"", text);
        var problems = new List<string>();
        Assert.Null(SeveranceData.Read(JsonDocument.Parse(text.Replace("\"left foot\": \"unavailable\"", "\"left foot\": \"later\"")).RootElement, "rules/severance", problems, TestData.Sandbox().Harm));
        Assert.Contains(problems, p => p.Contains("left foot") && p.Contains("unavailable"));
    }

    // ---- the guard's technique isolated, the contest's Dexterity only with a performed defence ----

    [Fact]
    public void The_guards_technique_and_Melee_in_it_can_each_be_switched_off()
    {
        var data = TestData.Sandbox().Guard;
        var d = new GuardSide(21, 26, 2, 0, 1f);
        var on = new GuardRules(data);
        var noMelee = new GuardRules(data, meleeOn: false);
        var none = new GuardRules(data, techniqueOn: false);
        double phiDex = 26 / 20.0;
        Assert.Equal(phiDex + 2, on.Technique(in d), 9);
        Assert.Equal(phiDex, noMelee.Technique(in d), 9);
        Assert.Equal(0, none.Technique(in d));
        double strength = none.Hold(in d, ContactMet.Shield, GuardMeet.Shield);
        Assert.Equal(strength + data.TechniqueWeight * (phiDex + 2), on.Hold(in d, ContactMet.Shield, GuardMeet.Shield), 9);
        Assert.Equal(strength + data.TechniqueWeight * phiDex, noMelee.Hold(in d, ContactMet.Shield, GuardMeet.Shield), 9);
    }

    [Fact]
    public void With_its_switch_the_contests_defence_counts_Dexterity_only_alongside_a_performed_defence()
    {
        var m = TestData.Sandbox();
        var contest = HarmRules.Build(m.Config, m.Harm, m.Skill).Contest!;
        var cls = m.Harm.Classes.Values.First(x => !m.Skill.IsBareHanded(x.Id));
        var a = new Striker(new EntityId(1, 0), "vampire", 60, 0, false, "Rapier", Dexterity: 23, Melee: 2);
        var still = new Struck(new EntityId(2, 0), "human", 25, 0, false, true, 100, 100, Dexterity: 40, Dodge: 3, Melee: 2, Armed: true);
        double Defence(Struck t, bool performedOnly)
        {
            contest.DexterityOnlyPerformed = performedOnly;
            var r = new HarmResult { DamagePool = 4 };
            contest.Roll(in a, in t, cls, new WorldRandom(new RandomStreams(5)), r);
            return r.DefencePotential;
        }
        Assert.Equal(2.0, Defence(still, false));                                  // The first contest: φ(40) always
        Assert.Equal(0.0, Defence(still, true));                                   // nothing performed: an empty defence
        Assert.Equal(4.0, Defence(still with { Guarding = true }, true));          // the guard raised: φ + Melee 2
        Assert.Equal(5.0, Defence(still with { Dodging = true }, true));           // a dodge in its burst: φ + Dodge 3
        contest.DexterityOnlyPerformed = false;
    }
}
