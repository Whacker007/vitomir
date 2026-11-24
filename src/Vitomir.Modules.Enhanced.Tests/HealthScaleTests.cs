// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Modules.Enhanced.Rules;
using Vitomir.Modules.Enhanced.Sheets;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Powers;

namespace Vitomir.Modules.Enhanced.Tests;

/// <summary>
/// Health on the 1–1000 scale (<i>"both blood and health were going to be a scale of 1 to 1000"</i>): a
/// purchased capacity <c>health_pool</c> (1 to 1000, 100 by default for every species, refused by name outside), its maximum
/// the purchase 1:1, a full pool full when it rises and the current held to it when it falls; a wound converted to 15 Health
/// by the translator (seven still stop the default person), a bigger pool enduring more of the same fixed harm;
/// the bands below 25 % and 10 % of the purchased maximum as labels only, on the vitals and as an event.
/// </summary>
public sealed class HealthScaleTests
{
    const string Damage = "enhanced.harm.damage", Soak = "enhanced.harm.soak";
    static readonly EntityId A = new(1, 1), T = new(2, 1);

    /// <summary>Dice given in advance, by purpose (as HarmTests' own).</summary>
    sealed class FixedDice : IDeterministicRandom
    {
        readonly Dictionary<string, Queue<int>> d10 = new(StringComparer.Ordinal);
        readonly Dictionary<string, long> positions = new(StringComparer.Ordinal);

        public FixedDice D10(string purpose, params int[] values) { d10[purpose] = new Queue<int>(values); return this; }
        public int NextD10(string purpose) { positions[purpose] = Position(purpose) + 1; return d10[purpose].Dequeue(); }
        public double NextUnitInterval(string purpose) => throw new InvalidOperationException($"no unit draw expected ({purpose})");
        public long Position(string purpose) => positions.TryGetValue(purpose, out var p) ? p : 0;
    }

    static ContactMade Contact(long id = 1) =>
        new(id, A, 1, T, ContactMet.Body, "chest", ContactMaterial.Cloth, 10, new Vector3(1, 2, 1.3f), new Vector3(-1, 0, 0), 9.5f, 13f, false, false, true, false);

    [Fact]
    public void Health_is_a_purchase_of_1_to_1000_100_by_default_for_every_species_with_a_wound_15_of_it()
    {
        var m = TestData.Sandbox();
        var h = m.Harm;
        Assert.Equal(("health_pool", 1, 1000, 100), (h.HealthPurchaseId, h.HealthPurchaseMin, h.HealthPurchaseMax, h.HealthMaximum));
        Assert.Equal(15, h.HealthPerWound);                                             // ENGINE_SANDBOX: 7 x 15 >= 100
        Assert.Equal((0.25, 0.10), (h.InjuredBelow, h.CrippledBelow));                  // the package's proposals
        Assert.Contains("a scale of 1 to 1000", h.HealthPurchaseSource);
        var (w, sheets, _) = TestData.World();
        var people = w.Service<PeopleHooks>();
        var (player, guard, townsman) = (w.Create(), w.Create(), w.Create());
        people.Made(player, PersonRole.Player, "Christof");
        people.Made(guard, PersonRole.Townsperson, "rudolfGuard");
        people.Made(townsman, PersonRole.Townsperson, "Civ12male");
        foreach (var e in new[] { player, guard, townsman })
        {
            Assert.Equal(100, sheets.Bought(e, "health_pool"));                           // a vampire and humans alike
            Assert.Equal(new ResourceState("health", 100, 100, 0, 0), sheets.Resource(e, "health"));
        }
        Assert.Equal("vampire", sheets.Species(player));
        var route = w.Service<IPersonSheets>();
        Assert.True(route.TryRead(guard, "health_pool", out int pool));
        Assert.True(route.TryRead(guard, "health", out int now));
        Assert.Equal((100, 100), (pool, now));
    }

    [Fact]
    public void Setting_health_pool_is_refused_by_name_outside_its_range_and_the_maximum_follows_it()
    {
        var (w, sheets, _) = TestData.World();
        var e = w.Create();
        w.Service<PeopleHooks>().Made(e, PersonRole.Townsperson, "rudolfGuard");
        var route = w.Service<IPersonSheets>();
        Assert.False(route.TrySet(e, "health_pool", 0, out string error));
        Assert.Contains("purchased health_pool", error);
        Assert.Contains("1..1000", error);
        Assert.False(route.TrySet(e, "health_pool", 1001, out error));
        Assert.Contains("1..1000", error);
        Assert.Equal(new ResourceState("health", 100, 100, 0, 0), sheets.Resource(e, "health"));   // a refusal changes nothing
        // A full pool stays full when it rises; a hurt one keeps what it has; the current is held to a lower maximum.
        Assert.True(route.TrySet(e, "health_pool", 250, out error), error);
        Assert.Equal(new ResourceState("health", 250, 250, 0, 0), sheets.Resource(e, "health"));
        sheets.SetResource(e, sheets.Resource(e, "health")!.Value with { Current = 50 });
        Assert.True(route.TrySet(e, "health_pool", 1000, out error), error);
        Assert.Equal(new ResourceState("health", 50, 1000, 0, 0), sheets.Resource(e, "health"));
        Assert.True(route.TrySet(e, "health_pool", 40, out error), error);
        Assert.Equal(new ResourceState("health", 40, 40, 0, 0), sheets.Resource(e, "health"));
        Assert.True(route.TrySet(e, "health_pool", 1, out error), error);                 // the least there is
        Assert.Equal(new ResourceState("health", 1, 1, 0, 0), sheets.Resource(e, "health"));
    }

    [Fact]
    public void A_sheet_s_purchased_health_pool_is_read_saved_and_given_back_exactly_and_refused_out_of_range()
    {
        var (w, sheets, _) = TestData.World();
        var route = w.Service<IPersonSheets>();
        string text = File.ReadAllText(TestData.File("sheets/guard_trained.json"));
        Assert.DoesNotContain("health_pool", text);
        string bought = text.Replace("\"purchased\": {", "\"purchased\": { \"health_pool\": 300,");
        Assert.NotEqual(text, bought);
        var e = w.Create();
        route.Give(e, bought, "bought.json");
        Assert.Equal(300, sheets.Bought(e, "health_pool"));
        Assert.Equal(new ResourceState("health", 300, 300, 0, 0), sheets.Resource(e, "health"));    // new: full
        sheets.SetResource(e, new ResourceState("health", 120, 300, 0, 0));
        string save = SheetJson.Write(sheets.Export(e));
        var f = w.Create();
        route.Give(f, save, "saved.json");
        Assert.Equal((300, new ResourceState("health", 120, 300, 0, 0)), (sheets.Bought(f, "health_pool"), sheets.Resource(f, "health")));
        Assert.Equal(sheets.DigestOf(e), sheets.DigestOf(f));
        var x = Assert.Throws<PersonSheetException>(() => route.Give(f, text.Replace("\"purchased\": {", "\"purchased\": { \"health_pool\": 1001,"), "bad.json"));
        Assert.Contains("purchased health_pool = 1001 is outside 1..1000", x.Message);
        // A sheet that carries Health 7 of 7 and buys none: full, so full at the default 100.
        var old = w.Create();
        Assert.True(route.TryTemplate(old, "sandbox.guard_trained", out string error), error);
        sheets.SetResource(old, new ResourceState("health", 7, 7, 0, 0));
        sheets.ClearBought(old, "health_pool");
        string carried = SheetJson.Write(sheets.Export(old));
        var g = w.Create();
        route.Give(g, carried, "fourteen-bw.json");
        Assert.Equal((100, new ResourceState("health", 100, 100, 0, 0)), (sheets.Bought(g, "health_pool"), sheets.Resource(g, "health")));
    }

    [Fact]
    public void The_translator_turns_each_wound_into_15_Health_so_seven_wounds_still_stop_the_default_person()
    {
        var m = TestData.Sandbox();
        var rules = HarmRules.Build(m.Config, m.Harm);
        var r = new HarmResult();
        // Strength 300 with a sword on a human (no soak): N wounds from N successes; 0 automatic.
        HarmResult Blow(int successes, long health, long maximum = 100)
        {
            var dice = new FixedDice().D10(Damage, [.. Enumerable.Repeat(10, successes), .. Enumerable.Repeat(2, 17 - successes)]);
            rules.Resolve(Contact(), new Striker(A, "vampire", 300, 0, false, "BroadSword"), new Struck(T, "human", 25, 0, false, true, health, maximum), dice, r);
            return r;
        }
        Assert.Equal((6, 90L, 10L, false), (Blow(6, 100).Wounds, r.HealthLost, r.HealthAfter, r.Died));
        Assert.Equal((7, 100L, 0L, true), (Blow(7, 100).Wounds, r.HealthLost, r.HealthAfter, r.Died));   // 105 >= 100: the stop
        Assert.Equal((1, 10L, 0L, true), (Blow(1, 10).Wounds, r.HealthLost, r.HealthAfter, r.Died));     // the seventh wound after six
        Assert.Equal((0, 0L, 100L), (Blow(0, 100).Wounds, r.HealthLost, r.HealthAfter));
        // As the earlier flat 7 did: a person of seven stops at the seventh wound, whatever order they come in.
        foreach (var split in new[] { new[] { 1, 1, 1, 1, 1, 1, 1 }, new[] { 3, 3, 1 }, new[] { 2, 4, 1 }, new[] { 6, 1 } })
        {
            long h = 100;
            for (int i = 0; i < split.Length; i++)
            {
                h = Blow(split[i], h).HealthAfter;
                Assert.Equal(i == split.Length - 1, r.Died);
                Assert.Equal(i == split.Length - 1 ? 0 : 100 - 15 * split.Take(i + 1).Sum(), h);
            }
        }
    }

    [Fact]
    public void A_bigger_pool_endures_more_of_the_same_fixed_harm()
    {
        // The same injury against different bought maxima — the absolute harm unchanged, more blows to the stop.
        var m = TestData.Sandbox();
        var rules = HarmRules.Build(m.Config, m.Harm);
        var r = new HarmResult();
        (int Blows, List<long> Lost) ToStop(long maximum)
        {
            long h = maximum;
            var lost = new List<long>();
            for (int n = 1; n < 1000; n++)
            {
                var dice = new FixedDice().D10(Damage, 10, 10, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2);   // two wounds a blow
                rules.Resolve(Contact(n), new Striker(A, "vampire", 300, 0, false, "BroadSword"), new Struck(T, "human", 25, 0, false, true, h, maximum), dice, r);
                lost.Add(r.HealthLost);
                h = r.HealthAfter;
                if (r.Died) return (n, lost);
            }
            return (-1, lost);
        }
        var (small, lostSmall) = ToStop(100);
        var (big, lostBig) = ToStop(300);
        var (most, _) = ToStop(1000);
        Assert.Equal((4, 10, 34), (small, big, most));                                  // 100/30, 300/30, 1000/30, rounded up
        Assert.All(lostSmall.SkipLast(1), x => Assert.Equal(30, x));                       // the same 30 Health a blow
        Assert.All(lostBig.SkipLast(1), x => Assert.Equal(30, x));
        Assert.Equal(lostSmall.Take(3), lostBig.Take(3));
    }

    [Fact]
    public void The_bands_are_labels_below_25_and_10_per_cent_of_the_purchased_maximum_and_change_no_harm()
    {
        var h = TestData.Sandbox().Harm;
        Assert.Equal(HealthBand.None, h.Band(100, 100));
        Assert.Equal(HealthBand.None, h.Band(25, 100));                                   // below, not at
        Assert.Equal(HealthBand.Injured, h.Band(24, 100));
        Assert.Equal(HealthBand.Injured, h.Band(10, 100));
        Assert.Equal(HealthBand.Crippled, h.Band(9, 100));
        Assert.Equal(HealthBand.Crippled, h.Band(1, 100));
        Assert.Equal(HealthBand.Stopped, h.Band(0, 100));
        Assert.Equal((HealthBand.None, HealthBand.Injured, HealthBand.Crippled), (h.Band(75, 300), h.Band(74, 300), h.Band(29, 300)));
        Assert.Equal(HealthBand.None, h.Band(0, 0));                                      // no Health, no band
        // With the default numbers: five wounds leave 25 (no band), six 10 (Injured), seven the stop — Crippled is never met.
        Assert.Equal((HealthBand.None, HealthBand.Injured, HealthBand.Stopped), (h.Band(100 - 75, 100), h.Band(100 - 90, 100), h.Band(0, 100)));

        // Labels only: the same blow on an injured body and on a whole one, the same dice, the same harm.
        var m = TestData.Sandbox();
        var rules = HarmRules.Build(m.Config, m.Harm);
        HarmResult On(long health)
        {
            var r = new HarmResult();
            var dice = new FixedDice().D10(Damage, 10, 6, 1, 2, 9).D10(Soak, 6, 1);
            rules.Resolve(Contact(), new Striker(A, "human", 60, 0, false, "Rapier"), new Struck(T, "vampire", 40, 0, false, true, health, 1000), dice, r);
            return r;
        }
        var whole = On(1000);
        var hurt = On(200);                                                               // 20 % of 1000: Injured
        Assert.Equal((HealthBand.None, HealthBand.Injured), (whole.BandBefore, hurt.BandBefore));
        Assert.Equal((whole.DamagePool, whole.DamageSuccesses, whole.SoakPool, whole.SoakSuccesses, whole.Wounds, whole.HealthLost),
                     (hurt.DamagePool, hurt.DamageSuccesses, hurt.SoakPool, hurt.SoakSuccesses, hurt.Wounds, hurt.HealthLost));
    }

    [Fact]
    public void The_vitals_carry_the_band_and_a_change_of_band_is_an_event_once()
    {
        var m = TestData.Sandbox();
        var b = new WorldBuilder(3);
        m.Register(b);
        var w = b.Build();
        var e = w.Create();
        w.Service<PeopleHooks>().Made(e, PersonRole.Townsperson, "rudolfGuard");
        var sheets = w.Service<ActorSheets>();
        var changes = new List<HealthBandChanged>();
        void Run(int n) { for (int i = 0; i < n; i++) { w.Step(); changes.AddRange(w.Events<HealthBandChanged>().ThisTick); } }
        Run(3);
        Assert.Equal((100f, 100f, HealthBand.None), (w.Get<Vitals>(e).Health, w.Get<Vitals>(e).HealthMax, w.Get<Vitals>(e).Band));
        Assert.Empty(changes);
        void Set(long current) => sheets.SetResource(e, sheets.Resource(e, "health")!.Value with { Current = current });
        Set(20); Run(3);
        Set(9); Run(3);
        Set(60); Run(3);                                                                  // healed: no band again
        Set(0); Run(3);
        Assert.Equal([(HealthBand.None, HealthBand.Injured, 20), (HealthBand.Injured, HealthBand.Crippled, 9), (HealthBand.Crippled, HealthBand.None, 60),
                      (HealthBand.None, HealthBand.Stopped, 0)], changes.Select(c => (c.From, c.To, c.Health)));
        Assert.All(changes, c => Assert.Equal((e, 100), (c.Who, c.HealthMax)));
        Assert.Equal(HealthBand.Stopped, w.Get<Vitals>(e).Band);
    }
}
