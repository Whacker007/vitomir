// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Text.Json;
using Vitomir.Modules.Enhanced.Rules;
using Vitomir.Modules.Enhanced.Sheets;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Powers;

namespace Vitomir.Modules.Enhanced.Tests;

/// <summary>
/// Each body contact its own staged harm from the swing's attack budget, and severance (<i>"each hit gets
/// it's own calculation on damage and what it does. So if a swing is hard enough from a strong enough character, it could go
/// through the arm if it severs it, and keep going"</i>; the rules package's attack budget and injury rules). The
/// budget is set once, at the chain's first body contact, and never refilled; a later contact of the chain is resolved from the
/// share left. A lethal or aggravated blow of an edged class severs its part when its post-soak harm in Health reaches the
/// part's threshold (ENGINE_SANDBOX, shares of the victim's purchased maximum, hand
/// 45 %, forearm 60 %, upper arm 75 %, neck 90 %, trunk 200 %), never bashing; the blade goes on
/// with the share the cut did not need. Current Health is charged once; the effective maximum falls by the union of what is
/// missing (hand 5 %, forearm 15 %, upper arm 25 %); Blood Healing stops there; the bands keep the purchased maximum. The
/// neck and the trunk stop the body. A stand-in publishes the contacts (the platform's follow-through is the stop work's), and the
/// world tests roll every die a 10 (<see cref="MaxDice"/>) so a blow's harm is exact: Strength S with a broadsword is
/// ceil(S/20 + 2) wounds, 15 Health each, on the trained guard (a human: no soak for lethal).
/// </summary>
[Collection(AllocationCollection.Name)]   // it measures allocations (Allocations.cs)
public sealed class SeveranceTests
{
    sealed class Rig
    {
        public required World W;
        public required ActorSheets Sheets;
        public required IPersonSheets Route;
        public required EntityId Attacker, Target;
        public required List<(long Tick, ContactMade Contact)> Contacts;
        public required StringWriter Trace;
        public readonly List<HarmResolved> Harm = [];
        public readonly List<ActorDied> Died = [];
        public readonly List<PartSevered> Severed = [];
        long next = 1;

        public void Run(int ticks = 1)
        {
            for (int i = 0; i < ticks; i++)
            {
                W.Step();
                Harm.AddRange(W.Events<HarmResolved>().ThisTick);
                Died.AddRange(W.Events<ActorDied>().ThisTick);
                Severed.AddRange(W.Events<PartSevered>().ThisTick);
            }
        }

        /// <summary>A contact of the attacker's next tick on the target's <paramref name="site"/> (swing <paramref name="swing"/>, its place <paramref name="chain"/> in the swing's chain), then that tick run.</summary>
        public void Hit(string site, int swing, int chain = 0, ContactMet met = ContactMet.Body)
        {
            long id = next++;
            Contacts.Add((W.Tick, new ContactMade(id, Attacker, swing, met == ContactMet.Level ? EntityId.None : Target, met,
                met == ContactMet.Body ? site : null, ContactMaterial.Cloth, 0, new Vector3(1, 2, 1.3f), new Vector3(-1, 0, 0), 9.5f, 13f,
                false, met == ContactMet.Shield, true, false, Chain: chain)));
            Run();
        }

        public long Health(EntityId e) => Sheets.Resource(e, "health")!.Value.Current;

        public void SetHealth(EntityId e, long v) => Sheets.SetResource(e, Sheets.Resource(e, "health")!.Value with { Current = v });

        public List<JsonElement> Lines() => [.. Trace.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => JsonDocument.Parse(l).RootElement)
            .Where(l => l.GetProperty("schema").GetString() == HarmTrace.Schema)];
    }

    /// <summary>Every d10 a 10 and every unit draw 0 (a fractional pool rounds up): a blow's harm exact (the harm's dice seam, a world service).</summary>
    sealed class MaxDice : IDeterministicRandom
    {
        readonly Dictionary<string, long> drawn = [];
        public int NextD10(string purpose) { Count(purpose); return 10; }
        public double NextUnitInterval(string purpose) { Count(purpose); return 0; }
        public long Position(string purpose) => drawn.TryGetValue(purpose, out long n) ? n : 0;
        void Count(string purpose) => drawn[purpose] = Position(purpose) + 1;
    }

    /// <summary>
    /// The combat rules' raw-1000 stress fixture (or its Strength set to <paramref name="strength"/>) against their trained guard (a human: no soak
    /// for lethal) whose purchased Health is <paramref name="targetPool"/>, or the sheets named; every die a 10 unless
    /// <paramref name="maxDice"/> is false (Strength 1000: 52 wounds, 780 Health a blow; 760: 600; 580: 465).
    /// </summary>
    static Rig Make(ulong seed = 21, string attacker = "sandbox.raw1000_stress", string target = "sandbox.guard_trained", int targetPool = 1000,
        string weapon = "BroadSword", int? strength = null, bool maxDice = true, params string[] switches)
    {
        var m = TestData.Sandbox(null, switches);
        var b = new WorldBuilder(seed);
        if (maxDice) b.Service<IDeterministicRandom>(new MaxDice());
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
        people.Made(t, PersonRole.Townsperson, "rudolfGuard");
        var route = world.Service<IPersonSheets>();
        Assert.True(route.TryTemplate(a, attacker, out string error), error);
        Assert.True(route.TryTemplate(t, target, out error), error);
        Assert.True(route.TrySet(t, "health_pool", targetPool, out error), error);
        if (strength is { } st) Assert.True(route.TrySet(a, "strength", st, out error), error);
        return new Rig { W = world, Sheets = world.Service<ActorSheets>(), Route = route, Attacker = a, Target = t, Contacts = contacts, Trace = trace };
    }

    static SeveranceData Data => TestData.Sandbox().Severance;

    /// <summary>A blow resolved by hand for <see cref="SeveranceRules.Judge"/>: its wounds at one Health each, its class and kind.</summary>
    static HarmResult Blow(int health, string cls = "sword_one_hand", float budget = 1f)
    {
        var harm = TestData.Sandbox().Harm;
        var r = new HarmResult();
        r.Reset();
        r.Class = harm.Classes[cls];
        r.Kind = r.Class.Kind;
        r.Wounds = health;
        r.HealthPerWound = 1;
        r.BudgetIn = budget;
        r.Outcome = HarmOutcome.Applied;
        return r;
    }

    static ContactMade On(string site) => new(1, EntityId.None, 1, EntityId.None, ContactMet.Body, site, ContactMaterial.Cloth, 0,
        Vector3.Zero, Vector3.UnitX, 9f, 12f, false, false, true, false);

    static uint Bits(params BodyRegion[] rs) { uint b = 0; foreach (var r in rs) b |= 1u << (int)r; return b; }

    // ---- the data ----

    [Fact]
    public void The_data_names_every_region_with_the_leads_thresholds_and_the_packages_illustrative_shares()
    {
        var d = Data;
        Assert.Equal("ENGINE_SANDBOX", d.Status);
        foreach (var (r, threshold, share) in new[] { (BodyRegion.LeftHand, 0.45, 0.05), (BodyRegion.RightHand, 0.45, 0.05), (BodyRegion.LeftForearm, 0.60, 0.15),
                     (BodyRegion.RightForearm, 0.60, 0.15), (BodyRegion.LeftUpperArm, 0.75, 0.25), (BodyRegion.RightUpperArm, 0.75, 0.25) })
        {
            var p = d.Part(r)!;
            Assert.Equal((threshold, (double?)share, false), (p.ThresholdShare, p.Share, p.StopsBody));
            Assert.True(p.ThresholdShare > share);                                              // above its structural share: no HOLD case
        }
        // In Health: the least whole Health at or above the share of the purchased maximum.
        Assert.Equal((45L, 450L, 22L, 1L), (d.Part(BodyRegion.LeftHand)!.ThresholdOf(100), d.Part(BodyRegion.LeftHand)!.ThresholdOf(1000),
            d.Part(BodyRegion.LeftHand)!.ThresholdOf(47), d.Part(BodyRegion.LeftHand)!.ThresholdOf(1)));   // 21.15 -> 22; never below 1
        Assert.Equal([BodyRegion.RightHand], d.Part(BodyRegion.RightForearm)!.Takes);
        Assert.Equal([BodyRegion.LeftForearm, BodyRegion.LeftHand], d.Part(BodyRegion.LeftUpperArm)!.Takes);
        var neck = d.Part(BodyRegion.Neck)!;
        Assert.Equal((0.90, (double?)null, true, BodyRegion.Head), (neck.ThresholdShare, neck.Share, neck.StopsBody, Assert.Single(neck.Takes)));
        foreach (var r in new[] { BodyRegion.Chest, BodyRegion.Belly, BodyRegion.Hips })
            Assert.Equal((2.0, (double?)null, true, 200L), (d.Part(r)!.ThresholdShare, d.Part(r)!.Share, d.Part(r)!.StopsBody, d.Part(r)!.ThresholdOf(100)));
        foreach (var r in new[] { BodyRegion.Head, BodyRegion.LeftThigh, BodyRegion.RightThigh, BodyRegion.LeftShin, BodyRegion.RightShin, BodyRegion.LeftFoot, BodyRegion.RightFoot })
        {
            Assert.Null(d.Part(r));                                                              // legs not yet (crawling)
            Assert.Equal(r != BodyRegion.Head, d.DefaultBody.Unavailable(r));                    // the legs "unavailable", the head null
        }
        Assert.Equal((false, true, true), (d.Kinds[(int)HarmKind.Bashing], d.Kinds[(int)HarmKind.Lethal], d.Kinds[(int)HarmKind.Aggravated]));
        Assert.True(d.Edged("sword_one_hand") && d.Edged("axe_two_hand") && d.Edged("dagger") && d.Edged("claws"));   // the: claws cut (aggravated)
        Assert.False(d.Edged("stake") || d.Edged("blunt_one_hand") || d.Edged("fists") || d.Edged("fangs"));
        Assert.Equal("missing_anatomy", d.Condition);
    }

    [Fact]
    public void A_wrong_severance_file_is_refused_by_name()
    {
        foreach (var (from, to, says) in new[]
        {
            ("\"sword_one_hand\", \"sword_two_hand\"", "\"sword_one_hand\", \"blunt_one_hand\"", "bashing never severs"),
            ("\"kinds\": [\"lethal\", \"aggravated\"]", "\"kinds\": [\"lethal\", \"bashing\"]", "bashing never severs"),
            ("\"left hand\": { \"threshold_share\": 0.45", "\"left hand\": { \"threshold_share\": 0", "must be above 0"),
            ("\"left forearm\": { \"threshold_share\": 0.60", "\"left forearm\": { \"threshold_share\": 0.10", "above its structural_share"),
            ("\"right hand\": { \"threshold_share\": 0.45", "\"right hand\": { \"threshold_share\": 0.4505", "whole thousandths"),
            ("\"threshold_of\": \"purchased_maximum\"", "\"threshold_of\": \"current_health\"", "harm.threshold_of"),
            ("\"head\": null,", "\"nose\": null,", "no body region"),
            ("\"structural_share\": 0.15, \"limb\": \"left arm\"", "\"structural_share\": 0.01, \"limb\": \"left arm\"", "subsumes the distal"),
            ("\"rule\": \"residual_share\"", "\"rule\": \"full_share\"", "continuation.rule"),
        })
        {
            using var copy = new TestData.Copy();
            copy.Edit("rules/severance.json", s => { Assert.Contains(from, s); return s.Replace(from, to); });
            var x = Assert.Throws<ModuleRefusedException>(() => TestData.Sandbox(copy.Folder));
            Assert.Contains(x.Reasons, r => r.Contains("rules/severance") && r.Contains(says));
        }
    }

    // ---- the verdict ----

    [Fact]
    public void Each_part_is_severed_at_its_threshold_and_not_one_Health_below_it()
    {
        var rules = new SeveranceRules(Data, on: true);
        foreach (var (part, purchased, threshold) in new[] { ("left hand", 100, 45), ("right hand", 100, 45), ("left forearm", 100, 60), ("right forearm", 100, 60),
                     ("left upper arm", 100, 75), ("right upper arm", 100, 75), ("neck", 100, 90), ("chest", 100, 200), ("belly", 100, 200), ("hips", 100, 200),
                     ("left hand", 1000, 450), ("right forearm", 1000, 600), ("neck", 1000, 900), ("chest", 1000, 2000), ("left hand", 47, 22), ("left upper arm", 47, 36) })
        {
            Assert.Equal(threshold, Data.Part(SeveranceData.RegionOf(part))!.ThresholdOf(purchased));
            var below = Blow(threshold - 1);
            rules.Judge(On(part), below, 0, purchased);
            Assert.Equal((SeverVerdict.BelowThreshold, BodyRegion.None, false, 0f, 0L), (below.Verdict, below.Severed, below.Continues, below.BudgetOut, below.StructuralLoss));
            Assert.True(below.SeverEligible);
            var at = Blow(threshold);
            rules.Judge(On(part), at, 0, purchased);
            Assert.Equal(threshold, at.SeverThreshold);
            Assert.Equal((SeverVerdict.Severed, SeveranceData.RegionOf(part)), (at.Verdict, at.Severed));
            Assert.False(at.Continues);                                                          // exactly the threshold: nothing left to go on with
            Assert.Equal(0f, at.BudgetOut);
            Assert.Equal(part is "neck" or "chest" or "belly" or "hips", at.StopsBody);
        }
        // The measure is the blow's post-soak harm, wounds times the translator, before Health caps it.
        var capped = Blow(4);
        capped.HealthPerWound = 15;
        rules.Judge(On("right forearm"), capped, 0, 100);
        Assert.Equal((60L, SeverVerdict.Severed), (capped.PostSoakHealth, capped.Verdict));
        // Bought Health resists severance: the same 60 Health does not cut the forearm of a 1000-Health person.
        var bought = Blow(4);
        bought.HealthPerWound = 15;
        rules.Judge(On("right forearm"), bought, 0, 1000);
        Assert.Equal(SeverVerdict.BelowThreshold, bought.Verdict);
    }

    [Fact]
    public void Bashing_never_severs_nor_a_point_nor_a_blow_on_the_head_or_a_leg_nor_anything_with_severance_off()
    {
        var rules = new SeveranceRules(Data, on: true);
        foreach (var (cls, site, verdict) in new[]
        {
            ("blunt_two_hand", "right forearm", SeverVerdict.KindDoesNotCut), ("fists", "neck", SeverVerdict.KindDoesNotCut),
            ("stake", "right hand", SeverVerdict.NoEdge), ("fangs", "left hand", SeverVerdict.NoEdge),
            ("sword_two_hand", "head", SeverVerdict.NotSeverable), ("axe_two_hand", "left thigh", SeverVerdict.Unavailable),
            ("sword_two_hand", "right foot", SeverVerdict.Unavailable),                            // labelled, not merely not severable
        })
        {
            var r = Blow(999, cls);
            rules.Judge(On(site), r, 0, 100);
            Assert.Equal((verdict, BodyRegion.None, false), (r.Verdict, r.Severed, r.Continues));
        }
        var claws = Blow(999, "claws");                                                          // the claws cut (aggravated)
        rules.Judge(On("left hand"), claws, 0, 100);
        Assert.Equal((SeverVerdict.Severed, HarmKind.Aggravated), (claws.Verdict, claws.Kind));
        var off = Blow(999);
        new SeveranceRules(Data, on: false).Judge(On("neck"), off, 0, 100);
        Assert.Equal((SeverVerdict.Off, BodyRegion.None, false, false), (off.Verdict, off.Severed, off.StopsBody, off.Continues));
    }

    [Fact]
    public void A_cut_goes_on_with_the_share_of_its_budget_the_cut_did_not_need()
    {
        var rules = new SeveranceRules(Data, on: true);
        var r = Blow(80, budget: 0.8f);
        rules.Judge(On("left forearm"), r, 0, 100);
        Assert.True(r.Continues);
        Assert.Equal(0.8f * (80 - 60) / 80f, r.BudgetOut, 6);                                    // (harm - threshold) / harm of what it had
        var huge = Blow(1000);
        rules.Judge(On("chest"), huge, 0, 100);
        Assert.Equal((true, true), (huge.Continues, huge.StopsBody));                            // cut through the trunk and on
        Assert.Equal(0.8f, huge.BudgetOut, 6);
    }

    [Fact]
    public void The_structural_loss_is_the_union_by_limb_hand_then_forearm_adds_ten_and_two_arms_add()
    {
        var d = Data;
        var rules = new SeveranceRules(d, on: true);
        var hand = Blow(45);
        rules.Judge(On("right hand"), hand, 0, 100);
        Assert.Equal((5L, 5L), (hand.StructuralLoss, hand.DeficitAfter));
        var forearm = Blow(60);
        rules.Judge(On("right forearm"), forearm, hand.MissingAfter, 100);
        Assert.Equal((10L, 15L), (forearm.StructuralLoss, forearm.DeficitAfter));                // 15 in all, not 20
        Assert.Equal(Bits(BodyRegion.RightForearm, BodyRegion.RightHand), forearm.MissingAfter);
        // Forearm first: the hand goes with it — nothing more for the hand (a hit there is no contact: the world test).
        var first = Blow(60);
        rules.Judge(On("left forearm"), first, 0, 100);
        Assert.Equal((15L, Bits(BodyRegion.LeftForearm, BodyRegion.LeftHand)), (first.StructuralLoss, first.MissingAfter));
        var upper = Blow(75);
        rules.Judge(On("left upper arm"), upper, first.MissingAfter, 100);
        Assert.Equal((10L, 25L), (upper.StructuralLoss, upper.DeficitAfter));
        // Two limbs add; the order does not matter (by the union, never by events).
        Assert.Equal(40, d.Deficit(Bits(BodyRegion.RightForearm, BodyRegion.RightHand, BodyRegion.LeftUpperArm, BodyRegion.LeftForearm, BodyRegion.LeftHand), 100));
        Assert.Equal(400, d.Deficit(Bits(BodyRegion.RightForearm, BodyRegion.RightHand, BodyRegion.LeftUpperArm, BodyRegion.LeftForearm, BodyRegion.LeftHand), 1000));
        Assert.Equal(3, d.Deficit(Bits(BodyRegion.LeftHand), 50));                               // 2.5 rounded to the nearest, halves up
        Assert.Equal(0, d.Deficit(Bits(BodyRegion.Neck, BodyRegion.Head), 100));                 // the neck stops the body: no share
        Assert.Equal("right forearm,right hand", SeveranceData.Text(forearm.MissingAfter));
        Assert.Equal(forearm.MissingAfter, SeveranceData.Parse("right forearm, right hand"));
    }

    // ---- in the world ----

    [Fact]
    public void A_chain_of_two_body_contacts_resolves_the_second_from_the_share_left_and_never_refills_it()
    {
        var r = Make();
        r.Run(2);
        r.Hit("right forearm", swing: 1);
        var first = Assert.Single(r.Harm);
        Assert.Equal(BodyRegion.RightForearm, first.Severed);
        Assert.True(first.Continues);
        long harm = first.Wounds * 15L;
        Assert.Equal(780, harm);                                                                 // 52 wounds: every die a 10
        Assert.Equal((harm - 600) / (float)harm, first.BudgetLeft, 6);                           // the forearm's 60 % of 1000
        r.Hit("left thigh", swing: 1, chain: 1);                                                 // the blade goes on into the thigh (not severable: it stops)
        Assert.Equal(2, r.Harm.Count);
        var second = r.Harm[1];
        Assert.Equal((BodyRegion.None, false, 0f), (second.Severed, second.Continues, second.BudgetLeft));
        var lines = r.Lines();
        var budget = lines[1].GetProperty("events").EnumerateArray().Single(e => e.GetProperty("step").GetString() == "budget");
        Assert.Equal(first.BudgetLeft, budget.GetProperty("inputs").GetProperty("budget_in").GetSingle(), 6);
        double full = budget.GetProperty("inputs").GetProperty("full_potential").GetDouble();
        Assert.Equal(52.0, full, 6);                                                             // 1000/20 + 2
        Assert.Equal(full * first.BudgetLeft, budget.GetProperty("outputs").GetProperty("potential").GetDouble(), 4);
        Assert.InRange(second.DamageDice, (int)Math.Floor(full * first.BudgetLeft), (int)Math.Ceiling(full * first.BudgetLeft));
        Assert.Equal(1, lines[1].GetProperty("chain").GetInt32());
        // The blade stopped: a third contact of the chain has nothing to be resolved from (refused by name, no harm).
        long before = r.Health(r.Target);
        r.Hit("chest", swing: 1, chain: 2);
        Assert.Equal(2, r.Harm.Count);
        Assert.Equal(before, r.Health(r.Target));
        Assert.Contains("no attack budget left", r.Lines()[^1].GetProperty("reason").GetString());
        // Nor does a chain contact of a swing that never had one; a new swing's first contact is whole again.
        r.Hit("chest", swing: 7, chain: 1);
        Assert.Equal(2, r.Harm.Count);
        Assert.Equal(1, r.W.Service<HarmLedger>().ChainContacts);
    }

    [Fact]
    public void Severing_the_neck_or_the_trunk_stops_the_body_whatever_Health_is_left()
    {
        // 780 Health a blow: the neck's 90 % of 800 is 720 (20 Health left); the trunk's 200 % of 380 is 760, more than he has.
        foreach (var (site, pool) in new[] { ("neck", 800), ("chest", 380), ("belly", 380), ("hips", 380) })
        {
            var r = Make(targetPool: pool);
            r.Run(2);
            r.Hit(site, swing: 1);
            var h = Assert.Single(r.Harm);
            Assert.Equal(SeveranceData.RegionOf(site), h.Severed);
            Assert.Equal(site == "neck" ? 20 : 0, h.HealthAfter);                                // the neck: Health is left, and still the stop
            var died = Assert.Single(r.Died);
            Assert.Equal((ActorStop.SeveredStop, r.Target, h.ContactId), (died.Stop, died.Who, died.ContactId));
            var cut = Assert.Single(r.Severed);
            Assert.Equal((r.Target, r.Attacker, h.Severed, 0), (cut.Who, cut.By, cut.Region, cut.StructuralLoss));
            if (site == "neck") Assert.True(r.W.Get<MissingParts>(r.Target).Lacks(BodyRegion.Head));
            // A later blow on the stopped body is no harm.
            r.Hit("right hand", swing: 2);
            Assert.Single(r.Harm);
        }
    }

    [Fact]
    public void Current_Health_is_charged_once_the_effective_maximum_falls_by_the_union_and_the_bands_keep_the_purchased_maximum()
    {
        var r = Make(strength: 760);                                                             // 600 Health a blow
        r.Run(2);
        r.Hit("right hand", swing: 1);
        var hand = Assert.Single(r.Harm);
        Assert.Equal((BodyRegion.RightHand, 50), (hand.Severed, hand.StructuralLoss));          // 5 % of 1000
        Assert.Equal(hand.HealthBefore - hand.Wounds * 15, hand.HealthAfter);                    // once: never wounds + structural
        Assert.Equal(hand.HealthAfter, r.Health(r.Target));
        r.Run();
        var v = r.W.Get<Vitals>(r.Target);
        Assert.Equal((1000f, 950f), (v.HealthMax, v.HealthCap));
        r.SetHealth(r.Target, 950);                         // healed to the cap between the blows: cuts at 45 % and 60 % are more than one person's Health
        r.Hit("right forearm", swing: 2);
        var forearm = r.Harm[^1];
        Assert.Equal((BodyRegion.RightForearm, 100), (forearm.Severed, forearm.StructuralLoss));  // 15 % in all, 10 % more
        var cut = r.Severed[^1];
        Assert.Equal((600, 600, 850, 1000), (cut.Harm, cut.Threshold, cut.EffectiveMaximum, cut.PurchasedMaximum));
        Assert.Equal(forearm.HealthBefore - forearm.Wounds * 15, forearm.HealthAfter);
        r.Run();
        Assert.Equal(850f, r.W.Get<Vitals>(r.Target).HealthCap);
        Assert.Equal(Bits(BodyRegion.RightForearm, BodyRegion.RightHand), r.W.Get<MissingParts>(r.Target).Bits);
        Assert.Equal("right forearm,right hand", r.Sheets.Condition(r.Target, "missing_anatomy"));
        // A blow on the missing hand is no tissue: no harm, no structural loss again.
        int count = r.Harm.Count;
        long health = r.Health(r.Target);
        r.Hit("right hand", swing: 3);
        Assert.Equal((count, health), (r.Harm.Count, r.Health(r.Target)));
        Assert.Contains("missing: no tissue to strike", r.Lines()[^1].GetProperty("reason").GetString());
        // The bands divide by the purchased maximum: 240 of 1000 bought is Injured, though 28 % of the 850 cap.
        r.SetHealth(r.Target, 240);
        r.Run();
        Assert.Equal(HealthBand.Injured, r.W.Get<Vitals>(r.Target).Band);
        Assert.Equal((1000f, 850f), (r.W.Get<Vitals>(r.Target).HealthMax, r.W.Get<Vitals>(r.Target).HealthCap));
        // The trace's severance stage: the threshold, the verdict, the union and the effective maximum, with their rule ids.
        var sev = r.Lines().First(l => l.GetProperty("severed").GetString() == "right forearm").GetProperty("events").EnumerateArray()
            .Single(e => e.GetProperty("step").GetString() == "severance");
        Assert.Equal((600, 0.6), (sev.GetProperty("inputs").GetProperty("threshold").GetInt32(), sev.GetProperty("inputs").GetProperty("threshold_share").GetDouble()));
        Assert.Equal("right hand", sev.GetProperty("inputs").GetProperty("missing_before").GetString());
        var o = sev.GetProperty("outputs");
        Assert.Equal(("Severed", 50, 150, 100, 850), (o.GetProperty("verdict").GetString(), o.GetProperty("structural_deficit_before").GetInt32(),
            o.GetProperty("structural_deficit_after").GetInt32(), o.GetProperty("structural_loss").GetInt32(), o.GetProperty("effective_maximum").GetInt32()));
        Assert.StartsWith("severance", sev.GetProperty("inputs").GetProperty("rule").GetString());
    }

    [Fact]
    public void Blood_Healing_heals_no_further_than_the_effective_maximum_and_is_refused_there()
    {
        var r = Make(target: "sandbox.newly_embraced", strength: 760);                          // a vampire (Blood to heal with): 40 dice less 1 soaked, 585 Health
        r.Sheets.Learn(r.Target, "blood_healing", 1, "common.blood_healing");
        r.Run(2);
        r.Hit("left hand", swing: 1);
        Assert.Equal(BodyRegion.LeftHand, Assert.Single(r.Harm).Severed);
        r.SetHealth(r.Target, 940);                                                              // the cap is 950 of 1000
        var book = r.W.Service<PowerBook>();
        Assert.Equal(PowerDenial.None, book.Use(r.W, r.Target, "blood_healing"));
        Assert.Equal(950, r.Health(r.Target));                                                   // 10 of its 20
        r.Run(130);                                                                              // the recast (1 s) runs out
        Assert.Equal(PowerDenial.NoEffect, book.Use(r.W, r.Target, "blood_healing"));
        Assert.Contains("effective maximum (950 of 950, of 1000 bought: left hand missing)", book.LastReason);
        Assert.Equal(950, r.Health(r.Target));
    }

    [Fact]
    public void Severance_off_is_the_harm_of_before_every_body_contact_stopping_the_blade()
    {
        var on = Make(strength: 760);                                                            // 600 a blow: the forearm's 60 % of 1000 exactly
        var off = Make(strength: 760, switches: EnhancedModule.SeveranceOffSwitch);
        Assert.False(TestData.Sandbox(null, EnhancedModule.SeveranceOffSwitch).SeveranceOn);
        foreach (var rig in new[] { on, off })
        {
            rig.Run(2);
            rig.Hit("right forearm", swing: 1);
            rig.Hit("head", swing: 2);                                                           // 400 left: the zero-Health stop
            rig.Hit("left thigh", swing: 3);                                                     // on the stopped body: nothing
        }
        Assert.Equal(2, off.Harm.Count);
        Assert.Empty(off.Severed);
        Assert.DoesNotContain(off.Died, d => d.Stop == ActorStop.SeveredStop);
        Assert.Equal(on.Died.Count, off.Died.Count);
        Assert.All(off.Harm, h => Assert.Equal((BodyRegion.None, false, 0f, 0), (h.Severed, h.Continues, h.BudgetLeft, h.StructuralLoss)));
        Assert.False(off.W.Has<MissingParts>(off.Target));
        // The draws and the staged numbers are the same blow for blow; only the severance's fields differ.
        Assert.Equal(on.Harm.Select(h => h with { Severed = BodyRegion.None, Continues = false, BudgetLeft = 0f, StructuralLoss = 0, CutShareFrom = "off" }), off.Harm);   // no cut share counted with severance off
        Assert.Equal(BodyRegion.RightForearm, on.Harm[0].Severed);
        var offLine = off.Lines()[0].GetProperty("events").EnumerateArray().Single(e => e.GetProperty("step").GetString() == "severance");
        Assert.Equal("Off", offLine.GetProperty("outputs").GetProperty("verdict").GetString());
    }

    [Fact]
    public void The_flurry_stays_on_the_chains_first_contact_and_its_blows_carry_the_verdict_and_never_cut()
    {
        // The earlier build's compressed flurry (its blows at once on the contact: no bodies here for the geometric check).
        var r = Make(strength: 580, switches: EnhancedModule.FlurryCompressedSwitch);                                                             // 465 a blow: the hand's 450 and on; two blows leave 70
        Assert.True(r.Route.TryLearn(r.Attacker, "celerity", 3, ["celerity.active"], out string error), error);   // K = 2: one extra blow a swing
        r.Run(2);
        var book = r.W.Service<PowerBook>();
        Assert.True(book.Use(r.W, r.Attacker, "celerity") == PowerDenial.None, book.LastReason);
        r.Run();
        r.Hit("right hand", swing: 1);
        Assert.Equal(2, r.Harm.Count);
        var (own, extra) = (r.Harm[0], r.Harm[1]);
        Assert.Equal((0, 1, BodyRegion.RightHand, true), (own.Flurry, extra.Flurry, own.Severed, own.Continues));
        Assert.Equal((BodyRegion.None, 0, own.Continues, own.BudgetLeft), (extra.Severed, extra.StructuralLoss, extra.Continues, extra.BudgetLeft));
        Assert.Single(r.Severed);
        r.Hit("chest", swing: 1, chain: 1);
        var later = r.Harm[^1];
        Assert.Equal((1, 0, 0), (r.Harm.Count - 2, later.Flurry, later.FlurryBlows));           // a later contact carries no flurry
    }

    [Fact]
    public void The_same_seed_and_contacts_give_the_same_digests_and_events()
    {
        Rig Play()
        {
            var r = Make(seed: 33);
            r.Run(2);
            r.Hit("left forearm", swing: 1);
            r.Hit("chest", swing: 1, chain: 1);
            r.Run(5);
            return r;
        }
        var (a, b) = (Play(), Play());
        Assert.Equal(a.W.Digest(), b.W.Digest());
        Assert.Equal(a.Harm, b.Harm);
        Assert.Equal(a.Severed, b.Severed);
        Assert.Equal(a.Trace.ToString(), b.Trace.ToString());
        var other = Make(seed: 33, switches: EnhancedModule.SeveranceOffSwitch);
        other.Run(2);
        other.Hit("left forearm", swing: 1);
        other.Run(6);
        Assert.NotEqual(a.W.Digest(), other.W.Digest());
    }

    [Fact]
    public void A_sheet_with_parts_missing_saves_and_loads_and_the_cap_and_the_anatomy_follow_it()
    {
        var r = Make();
        r.Run(2);
        r.Hit("left forearm", swing: 1);
        r.Run();
        string save = SheetJson.Write(r.Sheets.Export(r.Target));
        Assert.Contains("missing_anatomy", save);
        var other = Make(seed: 5);
        other.Run(2);
        Assert.False(other.W.Has<MissingParts>(other.Target));
        other.Sheets.Import(other.Target, SheetJson.Read(save));
        other.Run();
        Assert.Equal(Bits(BodyRegion.LeftForearm, BodyRegion.LeftHand), other.W.Get<MissingParts>(other.Target).Bits);
        Assert.Equal(850f, other.W.Get<Vitals>(other.Target).HealthCap);
        Assert.Equal(r.Sheets.DigestOf(r.Target), other.Sheets.DigestOf(other.Target));
        // A whole sheet loaded over it: whole again (the sheet is the record; the cap follows the anatomy).
        other.Sheets.Import(other.Target, SheetJson.Read(SheetJson.Write(r.Sheets.Export(r.Attacker))));
        other.Run();
        Assert.False(other.W.Has<MissingParts>(other.Target));
        Assert.Equal(0f, other.W.Get<Vitals>(other.Target).HealthCap);
    }

    [Fact]
    public void Nothing_is_allocated_per_tick_with_contacts_and_a_body_missing_parts()
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
                x.Events<ContactMade>().Publish(new ContactMade(id++, attacker, (int)id, target, ContactMet.Body, (id & 1) == 0 ? "chest" : "left thigh",
                    ContactMaterial.Cloth, x.Tick, Vector3.Zero, Vector3.UnitX, 9f, 12f, false, false, true, false));
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
        world.Service<ActorSheets>().SetCondition(target, "missing_anatomy", "left hand");
        for (int i = 0; i < 200; i++) world.Step();
        Allocations.None("400 ticks of contacts on a body missing parts", () => { for (int i = 0; i < 400; i++) world.Step(); });
        Assert.True(world.Get<MissingParts>(target).Lacks(BodyRegion.LeftHand));
        Assert.True(world.Service<HarmLedger>().Resolved > 30);
        Assert.True(world.Get<Vitals>(target).Health > 0);
        Assert.Equal(950f, world.Get<Vitals>(target).HealthCap);
    }
}
