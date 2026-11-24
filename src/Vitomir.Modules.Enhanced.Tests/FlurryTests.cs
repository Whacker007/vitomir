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
/// Celerity as speed plus flurry (<i>"something approaching option c for attacks, combined with some level
/// of speeding up things"</i>): the actions A = 1 + rank split into a body tempo T = A^0.5 and a flurry K = A / T of blows
/// carried by each swing that lands on a body, T × K = A. Each blow of a flurry is a whole staged harm of the same contact,
/// with its own draws, its own <see cref="HarmResolved"/> (<c>Flurry</c> 1, 2, …) and trace line, Health in order; a shield,
/// a blade or the level stops them all; none after the stop; fractions carried swing to swing and taken away when Celerity
/// ends. The earlier build's pure tempo (<c>celerity-curve-tempo</c>), 1.1's curve and <c>celerity-attack-off</c> carry no flurry.
/// </summary>
[Collection(AllocationCollection.Name)]   // it measures allocations (Allocations.cs)
public sealed class FlurryTests
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
        long next = 1;

        public void Run(int ticks = 1)
        {
            for (int i = 0; i < ticks; i++)
            {
                W.Step();
                Harm.AddRange(W.Events<HarmResolved>().ThisTick);
                Died.AddRange(W.Events<ActorDied>().ThisTick);
            }
        }

        /// <summary>A contact of the attacker's on the target next tick (its own swing), then that tick run.</summary>
        public void Hit(ContactMet met = ContactMet.Body, bool protect = false)
        {
            long id = next++;
            Contacts.Add((W.Tick, new ContactMade(id, Attacker, (int)id, met == ContactMet.Level ? EntityId.None : Target, met,
                met == ContactMet.Body ? "chest" : null, met == ContactMet.Shield ? ContactMaterial.Wood : ContactMaterial.Cloth, 0,
                new Vector3(1, 2, 1.3f), new Vector3(-1, 0, 0), 9.5f, 13f, false, met == ContactMet.Shield, true, protect)));
            Run();
        }

        public void Press() { W.Set(Attacker, new PowerIntent { Slot = 1 }); Run(); }

        /// <summary>The harm trace's lines (the Blood ledger's lines share the file: left out).</summary>
        public List<JsonElement> Lines() => [.. Trace.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => JsonDocument.Parse(l).RootElement)
            .Where(l => l.GetProperty("schema").GetString() == HarmTrace.Schema)];
    }

    /// <summary>
    /// The player (<c>sandbox.player</c>: the combat rules' newly Embraced, Celerity 1, Potence 1, Blood 100; or, <paramref name="plain"/>,
    /// the combat rules' newly Embraced alone, no Potence) with a broadsword (or <paramref name="weapon"/>) against a townsman whose Health
    /// is the most a purchase gives (1000), or the high-stat stress fixtures (Christof's 300s against the guard).
    /// </summary>
    static Rig Make(ulong seed = 11, bool strong = false, int rank = 1, string weapon = "BroadSword", bool plain = false, params string[] switches)
    {
        // These are the earlier build's flurry, now the switch flurry-compressed (every extra blow at once on the contact; the
        // default checks each at its own moment against bodies these rigs do not have: FlurryModelTests).
        var m = TestData.Sandbox(null, [EnhancedModule.FlurryCompressedSwitch, .. switches]);
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
        world.Service<HarmWeapons>().Of = (_, _) => weapon;
        var people = world.Service<PeopleHooks>();
        var (a, t) = (world.Create(), world.Create());
        people.Made(a, PersonRole.Player, "Christof");
        people.Made(t, PersonRole.Townsperson, "Civ12male");
        var route = world.Service<IPersonSheets>();
        string error;
        if (strong)
        {
            Assert.True(route.TryTemplate(a, "sandbox.christof", out error), error);
            Assert.True(route.TryTemplate(t, "sandbox.guard", out error), error);
        }
        else Assert.True(route.TrySet(t, "health_pool", 1000, out error), error);
        if (plain) Assert.True(route.TryTemplate(a, "sandbox.newly_embraced", out error), error);
        Assert.True(route.TryLearn(a, "celerity", rank, ["celerity.active"], out error), error);
        return new Rig { W = world, Sheets = world.Service<ActorSheets>(), Route = route, Attacker = a, Target = t, Contacts = contacts, Trace = trace };
    }

    /// <summary>The blows each swing carries, its own among them, with the fraction carried: floor(carry + K), the rest kept.</summary>
    static List<int> Expected(double k, int swings)
    {
        var list = new List<int>();
        double carry = 0;
        for (int i = 0; i < swings; i++)
        {
            double total = carry + k;
            int blows = (int)Math.Floor(total);
            carry = total - blows;
            list.Add(blows);
        }
        return list;
    }

    [Fact]
    public void The_split_gives_each_rank_a_tempo_and_a_flurry_whose_product_is_one_plus_rank()
    {
        // The formula, ranks 1 to 10 (the data's ranks 1-5 are the usable ones: 6-10 have no semantics, gate-29).
        for (int rank = 1; rank <= 10; rank++)
        {
            var (t, k) = CelerityEffect.Split(1 + rank, 0.5);
            Assert.Equal(1 + rank, t * k, 5);
            Assert.Equal(Math.Sqrt(1 + rank), t, 5);
            Assert.Equal(Math.Sqrt(1 + rank), k, 5);
        }
        Assert.Equal((1.41421354f, 2f, 2.44948983f, 3.31662488f), (CelerityEffect.Split(2, 0.5).Tempo, CelerityEffect.Split(4, 0.5).Tempo,
            CelerityEffect.Split(6, 0.5).Tempo, CelerityEffect.Split(11, 0.5).Tempo));
        Assert.Equal((6f, 1.0), CelerityEffect.Split(6, 1.0));                           // all tempo: no flurry, exactly
        // From the data: the default curve split, ranks 1-5; the earlier build's under the tempo switch, 1.1's under its own, the attack off.
        var c = TestData.Sandbox().Powers.Find("celerity")!;
        var split = new CelerityEffect(c, c.Curves["split"], true);
        for (int r = 1; r <= 5; r++)
        {
            Assert.Equal(1 + r, split.AttackFactor(r) * split.FlurryFactor(r), 5);
            Assert.Equal(split.AttackFactor(r), split.MoveFactor(r));
            Assert.True(split.FlurryFactor(r) > 1);
        }
        Assert.Equal((1f, 1.0, 1f), (split.AttackFactor(6), split.FlurryFactor(6), split.MoveFactor(6)));
        var tempo = new CelerityEffect(c, c.Curves[PowerData.OnePlusRank], true);
        var oneone = new CelerityEffect(c, c.Curves[PowerData.OneOne], true);
        var off = new CelerityEffect(c, c.Curves["split"], false);
        for (int r = 1; r <= 5; r++)
        {
            Assert.Equal((1f + r, 1.0), (tempo.AttackFactor(r), tempo.FlurryFactor(r)));
            Assert.Equal(1.0, oneone.FlurryFactor(r));
            Assert.Equal((1f, 1.0, split.MoveFactor(r)), (off.AttackFactor(r), off.FlurryFactor(r), off.MoveFactor(r)));
        }
        Assert.Equal(1.5f, oneone.AttackFactor(5));
    }

    [Fact]
    public void Rank_1_carries_one_or_two_blows_a_swing_averaging_its_flurry_and_unpaid_it_carries_none()
    {
        var r = Make(weapon: "Punch", plain: true);                                     // fists, no Potence: he stands the forty
        for (int i = 0; i < 4; i++) r.Hit();                                            // learned, not paid: nothing
        Assert.Equal(4, r.Harm.Count);
        Assert.All(r.Harm, h => Assert.Equal((0, 0), (h.Flurry, h.FlurryBlows)));
        Assert.False(r.W.Has<FlurryCarry>(r.Attacker));
        r.Harm.Clear();
        r.Press();
        const int swings = 40;
        for (int i = 0; i < swings; i++) r.Hit();
        double k = 2 / (double)CelerityEffect.Split(2, 0.5).Tempo;
        var expected = Expected(k, swings);
        var got = r.Harm.GroupBy(h => h.ContactId).Select(g => g.Count()).ToList();
        Assert.Equal(expected, got);
        Assert.True(r.Harm[^1].HealthAfter > 0);
        Assert.Contains(1, got);
        Assert.Contains(2, got);
        Assert.InRange(got.Average(), 1.38, 1.44);                                      // 1.41 on average
        Assert.Equal(r.Harm.Count(h => h.Flurry > 0), r.W.Service<HarmLedger>().FlurryBlows);
        Assert.Equal(swings + r.W.Service<HarmLedger>().FlurryBlows + 4, r.W.Service<HarmLedger>().Resolved);
    }

    [Fact]
    public void Each_blow_of_a_flurry_is_its_own_staged_harm_with_its_own_draws_soak_and_line_and_Health_in_order()
    {
        var r = Make(rank: 3);                                                          // K = 2 exactly: two blows a swing
        r.Press();
        r.Hit();
        var blows = r.Harm;
        Assert.Equal(2, blows.Count);
        Assert.Equal((0, 1), (blows[0].Flurry, blows[1].Flurry));
        Assert.All(blows, h => Assert.Equal((blows[0].ContactId, 1), (h.ContactId, h.FlurryBlows)));
        Assert.Equal(blows[0].HealthAfter, blows[1].HealthBefore);                      // applied in order
        Assert.Equal(blows[1].HealthAfter, r.Sheets.Resource(r.Target, "health")!.Value.Current);
        Assert.Equal(1000 - 15 * (blows[0].Wounds + blows[1].Wounds), blows[1].HealthAfter);
        var lines = r.Lines();
        Assert.Equal(2, lines.Count);
        Assert.Equal(("contact:1", 0, "contact:1/flurry:1", 1), (lines[0].GetProperty("request_id").GetString(), lines[0].GetProperty("flurry").GetInt32(),
            lines[1].GetProperty("request_id").GetString(), lines[1].GetProperty("flurry").GetInt32()));
        Assert.All(lines, l => Assert.Equal(1, l.GetProperty("contact_id").GetInt64()));
        // Then the budget's stage, and the severance's for the swing's own blow only (the flurry's blows never cut).
        Assert.Equal(["potential", "conversion", "auto_successes", "soak", "armor", "wounds", "health", "budget", "angle", "severance"],   // the armour after the soak, the angle before severance
            lines[0].GetProperty("events").EnumerateArray().Select(e => e.GetProperty("step").GetString()));
        Assert.Equal(["potential", "conversion", "auto_successes", "soak", "armor", "wounds", "health", "budget"],
            lines[1].GetProperty("events").EnumerateArray().Select(e => e.GetProperty("step").GetString()));
        Assert.Equal("contact:1/flurry:1.potential", lines[1].GetProperty("events")[0].GetProperty("event_id").GetString());
        // Its own draws, after the first blow's, stream by stream: the second blow's positions follow on.
        static List<(string, long)> Draws(JsonElement l) => [.. l.GetProperty("random_draws").EnumerateArray().Select(d => (d.GetProperty("purpose").GetString()!, d.GetProperty("position").GetInt64()))];
        var first = Draws(lines[0]);
        var second = Draws(lines[1]);
        Assert.NotEmpty(second);
        foreach (var purpose in second.Select(d => d.Item1).Distinct())
        {
            long last = first.Where(d => d.Item1 == purpose).Select(d => d.Item2).DefaultIfEmpty(-1).Max();
            Assert.Equal(last + 1, second.Where(d => d.Item1 == purpose).Min(d => d.Item2));
        }
        Assert.Contains(second, d => d.Item1 == "enhanced.harm.damage");
        Assert.Equal(15, lines[1].GetProperty("injuries")[0].GetProperty("health_per_injury_unit").GetInt32());
        // Potence's automatic success on each blow; the townsman soaks no lethal harm with Stamina on any (a human).
        Assert.All(blows, h => Assert.Equal((1, 0), (h.AutoSuccesses, h.SoakDice)));
    }

    [Fact]
    public void A_shield_a_held_blade_or_the_level_stops_the_swing_and_every_blow_and_moves_no_carry()
    {
        var r = Make();
        r.Press();
        r.Hit();                                                                       // one body swing: the carry is K - 1 = 0.41
        double carry = r.W.Get<FlurryCarry>(r.Attacker).Carry;
        Assert.Equal(2 / (double)CelerityEffect.Split(2, 0.5).Tempo - 1, carry, 9);
        int harmed = r.Harm.Count;
        r.Hit(ContactMet.Shield);
        r.Hit(ContactMet.Weapon);
        r.Hit(ContactMet.Level);
        r.Hit(protect: true);                                                          // a protected person: no harm, no flurry
        Assert.Equal(harmed, r.Harm.Count);
        Assert.Equal(carry, r.W.Get<FlurryCarry>(r.Attacker).Carry);
        var outcomes = r.Lines().Skip(harmed).Select(l => (l.GetProperty("outcome").GetString(), l.GetProperty("flurry").GetInt32())).ToList();
        Assert.Equal([("Blocked", 0), ("Blocked", 0), ("Missed", 0), ("Denied", 0)], outcomes);   // one line each, no flurry
    }

    [Fact]
    public void No_blow_of_a_flurry_follows_the_stop_and_none_falls_on_the_dead()
    {
        // Christof's 300s and broadsword on the stress guard: about eight wounds a blow, so the swing's own blow stops him.
        var r = Make(strong: true, rank: 5);                                            // K = 2.45: two or three blows a swing
        r.Press();
        for (int i = 0; i < 6 && r.Died.Count == 0; i++) r.Hit();
        var d = Assert.Single(r.Died);
        Assert.Equal(0, r.Harm[^1].HealthAfter);
        Assert.Equal(HealthBand.Stopped, r.Harm[^1].Band);
        var last = r.Harm.Where(h => h.ContactId == r.Harm[^1].ContactId).ToList();
        Assert.True(last[0].FlurryBlows >= 1);                                          // the swing carried more blows ...
        Assert.True(last.Count < 1 + last[0].FlurryBlows, $"{last.Count} blows resolved of {1 + last[0].FlurryBlows}");   // ... not resolved past the stop
        Assert.All(last.SkipLast(1), h => Assert.True(h.HealthAfter > 0));
        int before = r.Harm.Count;
        r.Hit();                                                                       // a stopped body: denied, no flurry
        Assert.Equal(before, r.Harm.Count);
        var line = r.Lines()[^1];
        Assert.Equal(("Denied", 0), (line.GetProperty("outcome").GetString(), line.GetProperty("flurry").GetInt32()));
        Assert.Single(r.Died);
    }

    [Fact]
    public void With_the_contest_on_every_blow_of_a_flurry_runs_its_own_contest()
    {
        var r = Make(rank: 3, switches: EnhancedModule.ContestSwitch);                  // K = 2: two blows a swing
        r.Press();
        for (int i = 0; i < 3; i++) r.Hit();
        Assert.Equal(6, r.Harm.Count);
        Assert.All(r.Harm, h => Assert.True(h.Contested));
        var lines = r.Lines();
        Assert.All(lines, l => Assert.Contains("contest", l.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("step").GetString())));
        // Each blow's contest draws its own dice, after the blow before it, from the contest's streams.
        static List<long> Attack(JsonElement l) => [.. l.GetProperty("random_draws").EnumerateArray()
            .Where(d => d.GetProperty("purpose").GetString() == "enhanced.skill.attack").Select(d => d.GetProperty("position").GetInt64())];
        Assert.True(lines.Count(l => Attack(l).Count > 0) >= 4, "the contest's attack dice are in the lines");
        for (int i = 1; i < lines.Count; i++)
            if (Attack(lines[i]).Count > 0 && Attack(lines[i - 1]).Count > 0)
                Assert.Equal(Attack(lines[i - 1]).Max() + 1, Attack(lines[i]).Min());
    }

    [Fact]
    public void The_tempo_switch_1_1_s_curve_and_the_attack_off_carry_no_flurry()
    {
        foreach (var sw in new[] { EnhancedModule.CelerityTempoSwitch, EnhancedModule.CelerityCurveSwitch, EnhancedModule.CelerityAttackOffSwitch })
        {
            var r = Make(11, rank: 3, switches: sw);
            r.Press();
            for (int i = 0; i < 6; i++) r.Hit();
            Assert.Equal(6, r.Harm.Count);
            Assert.All(r.Harm, h => Assert.Equal((0, 0), (h.Flurry, h.FlurryBlows)));
            Assert.False(r.W.Has<FlurryCarry>(r.Attacker));
        }
    }

    [Fact]
    public void The_carry_is_kept_through_a_renewal_and_taken_away_when_Celerity_ends()
    {
        var r = Make();
        r.Press();
        r.Hit();
        Assert.True(r.W.Has<FlurryCarry>(r.Attacker));
        r.Run(2 * World.TicksPerSecond);                                                // past the recast
        r.Press();                                                                     // renewed: paid again, the carry kept
        Assert.True(r.W.Has<FlurryCarry>(r.Attacker));
        r.Run(16 * World.TicksPerSecond);                                               // rank 1's 15 s run out
        Assert.False(r.W.Has<FlurryCarry>(r.Attacker));
        int before = r.Harm.Count;
        r.Hit();                                                                       // after it: one blow, no carry made
        Assert.Equal(before + 1, r.Harm.Count);
        Assert.False(r.W.Has<FlurryCarry>(r.Attacker));
    }

    [Fact]
    public void The_same_seed_gives_the_same_flurry_trace_and_digest_and_the_world_allocates_nothing_per_tick()
    {
        static (string Trace, ulong Digest, List<HarmResolved> Harm) Go(ulong seed)
        {
            var r = Make(seed, rank: 2, weapon: "Punch", plain: true);
            r.Press();
            for (int i = 0; i < 12; i++) { r.Hit(); r.Run(3); }
            return (r.Trace.ToString(), r.W.Digest(), r.Harm);
        }
        var a = Go(5);
        var b = Go(5);
        var c = Go(6);
        Assert.Equal(a.Trace, b.Trace);
        Assert.Equal(a.Digest, b.Digest);
        Assert.Equal(a.Harm, b.Harm);
        Assert.NotEqual(a.Trace, c.Trace);
        Assert.Contains(a.Harm, h => h.Flurry > 0);

        // Warm, with flurry contacts every few ticks and no trace: nothing allocated in a tick.
        var m = TestData.Sandbox(null, EnhancedModule.FlurryCompressedSwitch);   // The earlier build's flurry, compressed
        var w = new WorldBuilder(9);
        w.Service(new CombatTrace(null));
        DeathServices.Register(w);
        w.Event<ContactMade>();
        EntityId attacker = EntityId.None, target = EntityId.None;
        long id = 1;
        w.System(Phase.Contact, 0, "test.contacts", new DelegateSystem(x =>
        {
            if (x.Tick % 8 == 0 && attacker != EntityId.None)
                x.Events<ContactMade>().Publish(new ContactMade(id++, attacker, (int)id, target, ContactMet.Body, "chest", ContactMaterial.Cloth, x.Tick,
                    Vector3.Zero, Vector3.UnitX, 9f, 12f, false, false, true, false));
        }));
        m.Register(w);
        var world = w.Build();
        world.Service<HarmWeapons>().Of = (_, _) => "Punch";                             // fists: little harm, a long life
        var people = world.Service<PeopleHooks>();
        (attacker, target) = (world.Create(), world.Create());
        people.Made(attacker, PersonRole.Player, "Christof");
        people.Made(target, PersonRole.Townsperson, "Civ12male");
        var route = world.Service<IPersonSheets>();
        Assert.True(route.TrySet(target, "health_pool", 1000, out string error), error);
        Assert.True(route.TryTemplate(attacker, "sandbox.newly_embraced", out error), error);   // no Potence: the townsman stands
        // (A contact on a stopped body builds its refusal's sentence, "is dead: no harm on the dead": he must stand.)
        Assert.True(route.TryLearn(attacker, "celerity", 5, ["celerity.active"], out error), error);
        world.Set(attacker, new PowerIntent { Slot = 1 });
        for (int i = 0; i < 200; i++) world.Step();
        Allocations.None("400 ticks of flurry contacts", () => { for (int i = 0; i < 400; i++) world.Step(); });
        Assert.True(world.Service<HarmLedger>().FlurryBlows > 50);
        Assert.True(world.Get<Vitals>(target).Health > 0);
    }
}
