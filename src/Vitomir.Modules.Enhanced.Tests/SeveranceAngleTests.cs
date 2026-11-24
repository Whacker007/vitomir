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
/// Severance by the blow's angle, Melee's edge and each body's own parts (<i>"We'd
/// talked about the angle the attack lands at ... At roughly equal skills it should be fairly rare for armored body parts. For
/// things like the szlactha though ... they have long necks and are supposed to be fairly weak, so a well placed hit there should
/// generally have a better chance at severing the head. As the strength and combat stats go up, it should become increasingly
/// common"</i>; the rules package's angle bands). A contact's impact speed over its point speed is the sine of
/// the angle it met the surface at: normal from 33°, acute from 10°, glancing below. The share of the blow's post-soak harm that
/// counts toward cutting is the band's (normal 1, acute 0.6, glancing 0: a skim never cuts), moved by the attacker's Melee on the
/// Abilities' moderate curve (2.25^((Melee − 2)/10), at most 1); the harm to Health is not touched. Each body is judged by its
/// own part table: the man's and the szlachta's, whose long neck is cut at 30 % of its bought Health.
/// </summary>
[Collection(AllocationCollection.Name)]   // it measures allocations (Allocations.cs)
public sealed class SeveranceAngleTests
{
    static SeveranceData Data => TestData.Sandbox().Severance;

    static float Sin(double degrees) => DetMath.Sin((float)(degrees * DetMath.DegToRad));

    /// <summary>A body contact on <paramref name="site"/> whose striking point met the surface at the sine <paramref name="sine"/> (point speed 10 m/s); a negative sine for no measured speed.</summary>
    static ContactMade At(string site, float sine) => new(1, EntityId.None, 1, EntityId.None, ContactMet.Body, site, ContactMaterial.Cloth, 0,
        Vector3.Zero, Vector3.UnitX, sine < 0 ? 9f : sine * 10f, 12f, false, false, true, false, PointSpeed: sine < 0 ? 0f : 10f);

    /// <summary>A blow resolved by hand: its wounds at one Health each, a one-handed sword's lethal harm.</summary>
    static HarmResult Blow(int health, string cls = "sword_one_hand")
    {
        var harm = TestData.Sandbox().Harm;
        var r = new HarmResult();
        r.Reset();
        r.Class = harm.Classes[cls];
        r.Kind = r.Class.Kind;
        r.Wounds = health;
        r.HealthPerWound = 1;
        r.Outcome = HarmOutcome.Applied;
        return r;
    }

    // ---- the angle band ----

    [Fact]
    public void The_band_comes_from_the_sine_at_each_limit_a_tie_going_to_the_squarer_band()
    {
        var rules = new SeveranceRules(Data, on: true);
        Assert.Equal((33.0, 10.0), (Data.Angle.NormalFromDegrees, Data.Angle.AcuteFromDegrees));
        Assert.Equal(Sin(33), Data.Angle.NormalSine);
        Assert.Equal(Sin(10), Data.Angle.AcuteSine);
        foreach (var (sine, band) in new[]
        {
            (1f, AngleBand.Normal), (Sin(60), AngleBand.Normal), (Sin(33), AngleBand.Normal), (MathF.BitDecrement(Sin(33)), AngleBand.Acute),
            (Sin(20), AngleBand.Acute), (Sin(10), AngleBand.Acute), (MathF.BitDecrement(Sin(10)), AngleBand.Glancing), (Sin(3), AngleBand.Glancing), (0f, AngleBand.Glancing),
        })
        {
            Assert.Equal(band, Data.Angle.BandOf(sine));
            var c = new ContactMade(1, EntityId.None, 1, EntityId.None, ContactMet.Body, "chest", ContactMaterial.Cloth, 0, Vector3.Zero, Vector3.UnitX,
                sine * 7.5f, 1f, false, false, true, false, PointSpeed: 7.5f);
            Assert.Equal(Data.Angle.BandOf(Math.Clamp(sine * 7.5f / 7.5f, 0f, 1f)), rules.BandOf(in c, out float measured));
            Assert.Equal(Math.Clamp(sine * 7.5f / 7.5f, 0f, 1f), measured);
        }
        // No point speed: nothing to measure (a test's contact, a body walking onto a still blade).
        Assert.Equal(AngleBand.None, rules.BandOf(At("chest", -1), out float none));
        Assert.Equal(0f, none);
    }

    // ---- the cut share ----

    [Fact]
    public void The_cut_share_is_the_bands_moved_by_Melee_on_the_Abilities_curve_and_a_skim_stays_nothing()
    {
        var rules = new SeveranceRules(Data, on: true);
        Assert.Equal((1.0, 0.6, 0.0, 1.0), (Data.Angle.Normal, Data.Angle.Acute, Data.Angle.Glancing, Data.Angle.Unmeasured));
        Assert.Equal((2.25, 10.0, 2, 1.0), (Data.Edge.Base, Data.Edge.Ranks, Data.Edge.ReferenceRank, Data.Edge.Most));
        Assert.Equal(("melee", "brawl", "melee"), (Data.Edge.AbilityFor("sword_one_hand"), Data.Edge.AbilityFor("claws"), Data.Edge.AbilityFor(null)));
        // At the reference rank (2: the trained guard's, Christof the human's) the bands' own shares.
        Assert.Equal((1f, 0.6f, 0f, 1f), (rules.CutShare(AngleBand.Normal, 2, out _), rules.CutShare(AngleBand.Acute, 2, out _),
            rules.CutShare(AngleBand.Glancing, 2, out _), rules.CutShare(AngleBand.None, 2, out _)));
        foreach (var (band, rank, share) in new[]
        {
            (AngleBand.Normal, 0, 0.850), (AngleBand.Normal, 1, 0.922), (AngleBand.Normal, 3, 1.0), (AngleBand.Normal, 10, 1.0),
            (AngleBand.Acute, 0, 0.510), (AngleBand.Acute, 1, 0.553), (AngleBand.Acute, 3, 0.651), (AngleBand.Acute, 5, 0.765),
            (AngleBand.Acute, 8, 0.976), (AngleBand.Acute, 9, 1.0), (AngleBand.Acute, 10, 1.0),
            (AngleBand.Glancing, 0, 0.0), (AngleBand.Glancing, 10, 0.0),
            (AngleBand.None, 0, 1.0), (AngleBand.None, 10, 1.0),   // unmeasured: the earlier build's whole harm, Melee or not
        })
            Assert.Equal(share, rules.CutShare(band, rank, out _), 3);
        // Rising with Melee, never above 1.
        for (int m = 0; m < 10; m++) Assert.True(rules.CutShare(AngleBand.Acute, m + 1, out _) >= rules.CutShare(AngleBand.Acute, m, out _));
        // No Ability known (−1): the band's own share. Melee's edge off: the band's own share. The angle off: 1 whatever the band.
        Assert.Equal(0.6f, rules.CutShare(AngleBand.Acute, -1, out double factor));
        Assert.Equal(1.0, factor);
        Assert.Equal(0.6f, new SeveranceRules(Data, on: true, edgeOn: false).CutShare(AngleBand.Acute, 8, out _));
        Assert.Equal(1f, new SeveranceRules(Data, on: true, angleOn: false).CutShare(AngleBand.Glancing, 0, out _));
    }

    [Fact]
    public void A_glancing_blow_never_cuts_however_hard_and_an_acute_one_cuts_on_its_share()
    {
        var rules = new SeveranceRules(Data, on: true);
        foreach (string part in new[] { "left hand", "right forearm", "neck", "chest" })
        {
            var skim = Blow(999_999);
            rules.Judge(At(part, Sin(5)), skim, 0, 100, Data.DefaultBody, 10);
            Assert.Equal((SeverVerdict.Glancing, BodyRegion.None, false, 0f, AngleBand.Glancing), (skim.Verdict, skim.Severed, skim.Continues, skim.CutShare, skim.Angle));
            Assert.Equal((999_999L, 0.0), (skim.PostSoakHealth, skim.CutHarm));   // the harm is the blow's whole; none of it cuts
            Assert.Contains("glancing", SeveranceRules.Reason(skim));
        }
        // The right forearm at 100 bought: 60. Acute at Melee 2: 0.6 of the harm counts — 99 is 59.4, 100 is 60.
        var below = Blow(99);
        rules.Judge(At("right forearm", Sin(20)), below, 0, 100, Data.DefaultBody, 2);
        Assert.Equal((SeverVerdict.BelowThreshold, AngleBand.Acute, 0.6f), (below.Verdict, below.Angle, below.CutShare));
        Assert.Contains("of it cutting", SeveranceRules.Reason(below));
        var at = Blow(100);
        rules.Judge(At("right forearm", Sin(20)), at, 0, 100, Data.DefaultBody, 2);
        Assert.Equal((SeverVerdict.Severed, BodyRegion.RightForearm), (at.Verdict, at.Severed));
        Assert.False(at.Continues);                                                   // exactly the threshold: nothing left
        // The blade goes on with (cut − threshold) / cut of what it had: 150 acute is 90 cutting, (90 − 60) / 90.
        var on = Blow(150);
        on.BudgetIn = 0.9f;
        rules.Judge(At("right forearm", Sin(20)), on, 0, 100, Data.DefaultBody, 2);
        Assert.Equal((float)(0.9 * 30 / 90.0), on.BudgetOut, 5);
        // The same 99 square on cuts it (normal: the whole harm counts); at Melee 0 the normal band is 0.85: 99 is 84.1, 70 is 59.5.
        var square = Blow(99);
        rules.Judge(At("right forearm", Sin(70)), square, 0, 100, Data.DefaultBody, 2);
        Assert.Equal(SeverVerdict.Severed, square.Verdict);
        var green = Blow(70);
        rules.Judge(At("right forearm", Sin(70)), green, 0, 100, Data.DefaultBody, 0);
        Assert.Equal((SeverVerdict.BelowThreshold, 0.85f), (green.Verdict, MathF.Round(green.CutShare, 2)));
    }

    [Fact]
    public void With_the_angle_off_the_man_is_judged_by_the_whole_harm_whatever_the_band()
    {
        var off = new SeveranceRules(Data, on: true, angleOn: false);
        var then = new SeveranceRules(Data, on: true);
        foreach (var (part, threshold) in new[] { ("left hand", 45), ("right forearm", 60), ("left upper arm", 75), ("neck", 90), ("hips", 200) })
            foreach (float sine in new[] { Sin(80), Sin(20), Sin(2), -1f })
            {
                var below = Blow(threshold - 1);
                off.Judge(At(part, sine), below, 0, 100, Data.DefaultBody, 0);
                var at = Blow(threshold);
                off.Judge(At(part, sine), at, 0, 100, Data.DefaultBody, 0);
                Assert.Equal((SeverVerdict.BelowThreshold, SeverVerdict.Severed, 1f), (below.Verdict, at.Verdict, at.CutShare));
                // The earlier build's form (no body, no Ability: the default body and the band's share) on an unmeasured contact is the same.
                var old = Blow(threshold);
                then.Judge(At(part, -1), old, 0, 100);
                Assert.Equal((at.Verdict, at.Severed, at.BudgetOut, at.StructuralLoss), (old.Verdict, old.Severed, old.BudgetOut, old.StructuralLoss));
            }
        // The band is measured and kept whatever the switch.
        var r = Blow(10);
        off.Judge(At("chest", Sin(5)), r, 0, 100, Data.DefaultBody, 0);
        Assert.Equal((AngleBand.Glancing, false, 1f), (r.Angle, r.AngleOn, r.CutShare));
    }

    // ---- the bodies ----

    [Fact]
    public void The_man_and_the_woman_share_one_table_and_the_szlachtas_neck_is_a_third_of_a_mans()
    {
        var d = Data;
        Assert.Equal(["man", "szlachta"], d.Bodies.Select(b => b.Id));
        Assert.Same(d.Body("man"), d.DefaultBody);
        Assert.Same(d.Body("man"), d.ForAnatomy(AnatomyTables.OneOneMan.Name));
        Assert.Same(d.Body("man"), d.ForAnatomy(AnatomyTables.OneOneWoman.Name));
        Assert.Same(d.Body("szlachta"), d.ForAnatomy(AnatomyTables.OneOneSzlachta.Name));
        Assert.Null(d.ForAnatomy("1.1 horse (12 bones)"));
        var (man, szl) = (d.Body("man")!, d.Body("szlachta")!);
        foreach (var r in BodyRegions.All)
        {
            var (m, s) = (man.Part(r), szl.Part(r));
            Assert.Equal(m is null, s is null);
            if (m is null || s is null) continue;
            Assert.Equal((m.Share, m.Limb, m.StopsBody), (s.Share, s.Limb, s.StopsBody));   // the same structural shares: the effective maximum's union is a man's
            Assert.Equal(m.Takes, s.Takes);
            if (r != BodyRegion.Neck) Assert.Equal(m.ThresholdShare, s.ThresholdShare);
        }
        var neck = szl.Part(BodyRegion.Neck)!;
        Assert.Equal((0.30, 0.90, true, BodyRegion.Head), (neck.ThresholdShare, man.Part(BodyRegion.Neck)!.ThresholdShare, neck.StopsBody, Assert.Single(neck.Takes)));
        Assert.True(neck.ThresholdShare < man.Part(BodyRegion.LeftHand)!.ThresholdShare);   // below even a man's hand
        Assert.Equal((24L, 90L), (neck.ThresholdOf(80), man.Part(BodyRegion.Neck)!.ThresholdOf(100)));   // 1.1's 80 bought; a man's 100
        // Two wounds (30 Health) square on take the szlachta's head; acute, 18 of them cut, they do not; three acute (27 of 45) do.
        var rules = new SeveranceRules(d, on: true);
        foreach (var (wounds, sine, verdict) in new[] { (2, Sin(60), SeverVerdict.Severed), (2, Sin(20), SeverVerdict.BelowThreshold), (3, Sin(20), SeverVerdict.Severed),
                     (30, Sin(5), SeverVerdict.Glancing) })
        {
            var b = Blow(wounds);
            b.HealthPerWound = 15;
            rules.Judge(At("neck", sine), b, 0, 80, szl, 2);
            Assert.Equal((verdict, "szlachta"), (b.Verdict, b.Body));
            if (verdict == SeverVerdict.Severed) Assert.Equal((true, BodyRegion.Neck), (b.StopsBody, b.Severed));
        }
        // The same two wounds on a man's neck (100 bought: 90) do not.
        var manBlow = Blow(2);
        manBlow.HealthPerWound = 15;
        rules.Judge(At("neck", Sin(60)), manBlow, 0, 100, man, 2);
        Assert.Equal(SeverVerdict.BelowThreshold, manBlow.Verdict);
    }

    [Fact]
    public void A_body_with_no_part_table_is_never_cut_and_the_rules_alone_judge_by_the_default_body()
    {
        var d = Data;
        var rules = new SeveranceRules(d, on: true);
        foreach (string part in new[] { "left hand", "neck", "chest" })
        {
            var r = Blow(999_999);
            rules.Judge(At(part, 1f), r, 0, 100, body: null, 10);
            Assert.Equal((SeverVerdict.NoPartTable, BodyRegion.None, false, (string?)null), (r.Verdict, r.Severed, r.Continues, r.Body));
            Assert.Contains("no part table", SeveranceRules.Reason(r));
        }
        // A world that draws no bodies (no hit volumes, no characters): the default body, the man's.
        var world = new WorldBuilder(1).Build();
        Assert.Same(d.DefaultBody, rules.BodyOf(world, world.Create()));
    }

    // ---- the data ----

    [Fact]
    public void A_wrong_angle_edge_or_body_is_refused_by_name()
    {
        foreach (var (from, to, says) in new[]
        {
            ("\"glancing\": 0.0", "\"glancing\": 0.1", "a skim never cuts"),
            ("\"acute_from_degrees\": 10", "\"acute_from_degrees\": 40", "the limits must rise"),
            ("\"acute\": 0.6", "\"acute\": 1.6", "angle.cut_share.acute 1.6 must be a share"),
            ("\"measure\": \"impact_speed_over_point_speed\"", "\"measure\": \"degrees\"", "angle.measure"),
            ("\"bands\": [\"normal\", \"acute\"]", "\"bands\": [\"normal\", \"glancing\"]", "however good the edge"),
            ("\"reference_rank\": 2", "\"reference_rank\": 12", "reference_rank"),
            ("\"base\": 2.25", "\"base\": 0.5", "melee_edge.base"),
            ("\"claws\": \"brawl\"", "\"talons\": \"brawl\"", "ability_by_class names class 'talons'"),
            ("\"default_body\": \"man\"", "\"default_body\": \"horse\"", "default_body 'horse'"),
            ("\"anatomy\": [\"1.1 szlachta (34 bones)\"]", "\"anatomy\": [\"1.1 szlachta (35 bones)\"]", "no anatomy table the platform knows"),
            ("\"anatomy\": [\"1.1 szlachta (34 bones)\"]", "\"anatomy\": [\"1.1 man (40 bones)\"]", "another body's already"),
            ("\"neck\": { \"threshold_share\": 0.30", "\"neck\": { \"threshold_share\": 0", "bodies.szlachta.parts.neck.threshold_share must be above 0"),
        })
        {
            using var copy = new TestData.Copy();
            copy.Edit("rules/severance.json", s => { Assert.Contains(from, s); return s.Replace(from, to); });
            var x = Assert.Throws<ModuleRefusedException>(() => TestData.Sandbox(copy.Folder));
            Assert.Contains(x.Reasons, r => r.Contains("rules/severance") && r.Contains(says));
        }
    }

    [Fact]
    public void The_szlachtas_sheet_is_1_1s_on_the_combat_rules_scale_and_saves_and_loads()
    {
        var m = TestData.Sandbox();
        var b = new WorldBuilder(4);
        m.Register(b);
        var world = b.Build();
        var e = world.Create();
        world.Service<PeopleHooks>().Made(e, PersonRole.Townsperson, "Szlachta");   // the template dresses 1.1's Szlachta
        var sheets = world.Service<ActorSheets>();
        foreach (var (trait, value) in new[] { ("strength", 15), ("dexterity", 17), ("stamina", 30), ("perception", 13), ("intelligence", 13), ("wits", 11),
                     ("charisma", 0), ("manipulation", 0), ("appearance", 0), ("brawl", 2), ("melee", 0) })
            Assert.Equal((trait, value), (trait, sheets.Effective(e, trait)));
        Assert.Equal("human", sheets.Species(e));
        Assert.Equal(80, sheets.Resource(e, "health")!.Value.Maximum);
        string save = SheetJson.Write(sheets.Export(e));
        var f = world.Create();
        world.Service<PeopleHooks>().Made(f, PersonRole.Townsperson, "Civ12male");
        sheets.Import(f, SheetJson.Read(save));
        Assert.Equal(sheets.DigestOf(e), sheets.DigestOf(f));
    }

    // ---- in a world: the event, the trace, determinism, allocations ----

    sealed class Rig
    {
        public required World W;
        public required EntityId Attacker, Target;
        public required List<(long Tick, ContactMade Contact)> Contacts;
        public required StringWriter Trace;
        public readonly List<HarmResolved> Harm = [];
        long next = 1;

        public void Run(int ticks = 1)
        {
            for (int i = 0; i < ticks; i++) { W.Step(); Harm.AddRange(W.Events<HarmResolved>().ThisTick); }
        }

        public void Hit(string site, float sine)
        {
            long id = next++;
            Contacts.Add((W.Tick, new ContactMade(id, Attacker, (int)id, Target, ContactMet.Body, site, ContactMaterial.Cloth, 0, new Vector3(1, 2, 1.3f),
                new Vector3(-1, 0, 0), sine * 9.5f, 13f, false, false, true, false, PointSpeed: 9.5f)));
            Run();
        }

        public List<JsonElement> Lines() => [.. Trace.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => JsonDocument.Parse(l).RootElement)
            .Where(l => l.GetProperty("schema").GetString() == HarmTrace.Schema)];
    }

    static Rig Make(ulong seed = 21, string attacker = "sandbox.elite_stress", params string[] switches)
    {
        var m = TestData.Sandbox(null, switches);
        var b = new WorldBuilder(seed);
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
        world.Service<HarmWeapons>().Of = (_, _) => "BroadSword";
        var people = world.Service<PeopleHooks>();
        var (a, t) = (world.Create(), world.Create());
        people.Made(a, PersonRole.Player, "Christof");
        people.Made(t, PersonRole.Townsperson, "rudolfGuard");
        var route = world.Service<IPersonSheets>();
        Assert.True(route.TryTemplate(a, attacker, out string error), error);
        Assert.True(route.TryTemplate(t, "sandbox.guard_trained", out error), error);
        Assert.True(route.TrySet(t, "health_pool", 1000, out error), error);
        return new Rig { W = world, Attacker = a, Target = t, Contacts = contacts, Trace = trace };
    }

    [Fact]
    public void The_event_and_the_trace_carry_the_band_and_the_cut_share_and_the_harm_to_Health_is_the_same_whatever_the_angle()
    {
        Rig Play(params string[] switches)
        {
            var r = Make(seed: 7, switches: switches);
            r.Run(2);
            r.Hit("left upper arm", Sin(60));
            r.Hit("right forearm", Sin(20));
            r.Hit("chest", Sin(4));
            r.Hit("left hand", -0.0f);
            r.Run(2);
            return r;
        }
        var on = Play();
        var off = Play(EnhancedModule.SeveranceAngleOffSwitch);
        Assert.Equal([AngleBand.Normal, AngleBand.Acute, AngleBand.Glancing, AngleBand.Glancing], on.Harm.Select(h => h.Angle));
        Assert.Equal(on.Harm.Select(h => h.Angle), off.Harm.Select(h => h.Angle));        // measured whatever the switch
        // Elite stress: Melee 5. Normal 1, acute 0.6 × 2.25^0.3, glancing 0; with the angle off every share 1.
        var shares = on.Harm.Select(h => (double)h.CutShare).ToList();
        Assert.Equal(1.0, shares[0], 5);
        Assert.Equal(0.6 * Math.Pow(2.25, 0.3), shares[1], 4);
        Assert.Equal((0.0, 0.0), (shares[2], shares[3]));
        Assert.All(off.Harm, h => Assert.Equal(1f, h.CutShare));
        // The harm itself — dice, wounds, Health — is the same blow for blow: the angle moves only what cuts.
        Assert.Equal(on.Harm.Select(h => (h.DamageDice, h.DamageSuccesses, h.Wounds, h.HealthBefore - h.HealthAfter)),
            off.Harm.Select(h => (h.DamageDice, h.DamageSuccesses, h.Wounds, h.HealthBefore - h.HealthAfter)));
        // The trace: an "angle" stage before "severance", and the band and the share at the line's end.
        var line = on.Lines()[1];
        var stages = line.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("step").GetString()).ToList();
        Assert.Equal(stages.IndexOf("severance") - 1, stages.IndexOf("angle"));
        var angle = line.GetProperty("events").EnumerateArray().Single(e => e.GetProperty("step").GetString() == "angle");
        Assert.Equal(("Acute", 5, "melee"), (angle.GetProperty("outputs").GetProperty("band").GetString(), angle.GetProperty("inputs").GetProperty("edge_rank").GetInt32(),
            angle.GetProperty("inputs").GetProperty("edge_ability").GetString()));
        Assert.Equal(20.0, angle.GetProperty("outputs").GetProperty("degrees").GetDouble(), 1);
        Assert.Equal("Acute", line.GetProperty("angle").GetString());
        Assert.Equal(on.Harm[1].CutShare, line.GetProperty("cut_share").GetDouble(), 6);
    }

    [Fact]
    public void The_same_seed_and_contacts_give_the_same_digests_events_and_trace()
    {
        Rig Play()
        {
            var r = Make(seed: 41);
            r.Run(2);
            r.Hit("right forearm", Sin(25));
            r.Hit("neck", Sin(50));
            r.Run(3);
            return r;
        }
        var (a, b) = (Play(), Play());
        Assert.Equal(a.W.Digest(), b.W.Digest());
        Assert.Equal(a.Harm, b.Harm);
        Assert.Equal(a.Trace.ToString(), b.Trace.ToString());
    }

    [Fact]
    public void Nothing_is_allocated_per_tick_judging_bands_cut_shares_and_bodies()
    {
        var m = TestData.Sandbox();
        var b = new WorldBuilder(3);
        DeathServices.Register(b);
        b.Event<ContactMade>();
        EntityId attacker = EntityId.None, target = EntityId.None;
        long id = 1;
        b.System(Phase.Contact, 0, "test.contacts", new DelegateSystem(x =>
        {
            if (x.Tick % 16 == 0 && attacker != EntityId.None)
            {
                float sine = (id % 3) switch { 0 => 0.9f, 1 => 0.3f, _ => 0.05f };
                x.Events<ContactMade>().Publish(new ContactMade(id++, attacker, (int)id, target, ContactMet.Body, (id & 1) == 0 ? "neck" : "left forearm",
                    ContactMaterial.Cloth, x.Tick, Vector3.Zero, Vector3.UnitX, sine * 9f, 12f, false, false, true, false, PointSpeed: 9f));
            }
        }));
        m.Register(b);
        var world = b.Build();
        world.Service<HarmWeapons>().Of = (_, _) => "BroadSword";
        var people = world.Service<PeopleHooks>();
        (attacker, target) = (world.Create(), world.Create());
        people.Made(attacker, PersonRole.Player, "Christof");
        people.Made(target, PersonRole.Townsperson, "Civ12male");
        var route = world.Service<IPersonSheets>();
        Assert.True(route.TrySet(target, "health_pool", 1000, out string error), error);
        Assert.True(route.TryTemplate(attacker, "sandbox.newly_embraced", out error), error);   // a sword that cuts nothing: below every threshold
        for (int i = 0; i < 200; i++) world.Step();
        Allocations.None("400 ticks of contacts in every band", () => { for (int i = 0; i < 400; i++) world.Step(); });
        Assert.True(world.Service<HarmLedger>().Resolved > 30);
    }
}
