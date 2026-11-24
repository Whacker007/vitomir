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
using Vitomir.Platform.Powers;

namespace Vitomir.Modules.Enhanced.Tests;

/// <summary>
/// The Blood commands (by design): Blood Strength,
/// Dexterity and Stamina cast on 1.1's numbers (5 Blood, 15 to 90 s by rank, a 1 s recast, +5 to +25 raw points by rank), a
/// temporary modifier on the <b>effective</b> Attribute that may pass 1000 (the package's scenario 8) with the purchased value
/// unchanged, one a power (a renewal replaces it, paid again), its expiry exact; Blood Healing (5 Blood, 20 to 80 Health by
/// rank at once, never past the maximum, refused at full Health with nothing spent); every refusal spending nothing; the staged
/// harm reading the risen Strength and Stamina; the slots' use hint; save and load exact, the same digests from the same presses,
/// nothing allocated per tick. Health here is a stand-in of 100 (an earlier build's scale) set on the sheet.
/// </summary>
[Collection(AllocationCollection.Name)]   // it measures allocations (Allocations.cs)
public sealed class CommonCommandTests
{
    sealed record Rig(World W, ActorSheets Sheets, PowerBook Book, IPersonSheets Route, EntityId Player, EntityId Guard, StringWriter Trace)
    {
        public readonly List<PowerUsed> Used = [];
        public readonly List<PowerRefused> Refused = [];
        public readonly List<PowerEnded> Ended = [];

        public void Run(int ticks = 1)
        {
            for (int i = 0; i < ticks; i++)
            {
                W.Step();
                Used.AddRange(W.Events<PowerUsed>().ThisTick);
                Refused.AddRange(W.Events<PowerRefused>().ThisTick);
                Ended.AddRange(W.Events<PowerEnded>().ThisTick);
            }
        }

        /// <summary>Presses a quick slot for the next tick and steps it.</summary>
        public void Press(EntityId who, int slot)
        {
            W.Set(who, new PowerIntent { Slot = slot });
            Run();
        }

        /// <summary>Uses a power by id between ticks (slot 0), collecting what it published.</summary>
        public PowerDenial UseNow(EntityId who, string power)
        {
            int u = W.Events<PowerUsed>().ThisTick.Count, f = W.Events<PowerRefused>().ThisTick.Count;
            var d = Book.Use(W, who, power);
            Used.AddRange(W.Events<PowerUsed>().ThisTick.Skip(u));
            Refused.AddRange(W.Events<PowerRefused>().ThisTick.Skip(f));
            return d;
        }

        public long Blood(EntityId e) => Sheets.Resource(e, "blood")!.Value.Current;
        public long Spent => W.Service<BloodLedger>().Spent;

        /// <summary>The stand-in for the earlier build's Health (its scale, 100 by default): the sheet's Health resource set to <paramref name="current"/> of <paramref name="maximum"/>.</summary>
        public void Health(EntityId e, long current, long maximum = 100)
        {
            var h = Sheets.Resource(e, Book.HealthResource!)!.Value;
            Sheets.SetResource(e, h with { Current = current, Maximum = maximum });
        }

        public long HealthNow(EntityId e) => Sheets.Resource(e, Book.HealthResource!)!.Value.Current;
    }

    /// <summary>
    /// The module (sandbox) with the player (<c>sandbox.player</c>: Celerity 1, Potence 1 and the four Blood commands at 1,
    /// Blood 100 of 100) and a trained guard (a mortal), both armed; <paramref name="contacts"/> published at their ticks
    /// (the staged harm reads them), the combat trace kept.
    /// </summary>
    static Rig Make(ulong seed = 5, List<(long Tick, ContactMade C)>? contacts = null)
    {
        var m = TestData.Sandbox();
        var b = new WorldBuilder(seed);
        DeathServices.Register(b);
        var trace = new StringWriter();
        b.Service(new CombatTrace(trace));
        b.Component<Combatant>().Component<CombatIntent>();
        b.Event<ContactMade>();
        if (contacts is not null)
            b.System(Phase.Contact, 0, "test.contacts", new DelegateSystem(w =>
            {
                foreach (var (tick, c) in contacts)
                    if (tick == w.Tick) w.Events<ContactMade>().Publish(c with { Tick = w.Tick });
            }));
        m.Register(b);
        var w = b.Build();
        w.Service<HarmWeapons>().Of = (_, _) => "BroadSword";
        var people = w.Service<PeopleHooks>();
        var (p, g) = (w.Create(), w.Create());
        people.Made(p, PersonRole.Player, "Christof");
        people.Made(g, PersonRole.Townsperson, "rudolfGuard");
        CombatServices.Arm(w, p);
        CombatServices.Arm(w, g);
        var r = new Rig(w, w.Service<ActorSheets>(), w.Service<PowerBook>(), w.Service<IPersonSheets>(), p, g, trace);
        r.Run();
        return r;
    }

    // The player's slots (rules/powers' order: Celerity first).
    const int Strength = 2, Dexterity = 3, Stamina = 4, Healing = 5;

    // ---- the data ----

    [Fact]
    public void The_Blood_commands_are_1_1_s_numbers_with_the_package_s_option_names_and_two_readings()
    {
        var d = TestData.Sandbox().Powers;
        foreach (var (id, trait) in new[] { ("blood_strength", "strength"), ("blood_dexterity", "dexterity"), ("blood_stamina", "stamina") })
        {
            var p = d.Find(id)!;
            Assert.Equal((PowerKind.Cast, PowerData.EffectTempAttribute, "common." + id, trait), (p.Kind, p.Effect, p.Option, p.Trait));
            Assert.Equal([5, 5, 5, 5, 5], p.Ranks.Select(r => r.Cost));                          // game.ndd:109-111
            Assert.Equal([15.0, 30, 45, 60, 90], p.Ranks.Select(r => r.Seconds));                // BLOOD*_DURATIONS
            Assert.All(p.Ranks, r => Assert.Equal(1.0, r.Recast));                               // game.ndd: 1000 ms
            Assert.Equal([5, 10, 15, 20, 25], p.Amounts!);                                       // effects.ned:309-311 v1-v5
            Assert.Equal(PowerData.ReadingRaw, p.DefaultReading);
            Assert.Equal([5, 10, 15, 20, 25], Enumerable.Range(1, 5).Select(k => p.AmountAt(k)));
            Assert.Equal([50, 100, 150, 200, 250], Enumerable.Range(1, 5).Select(k => p.AmountAt(k, PowerData.ReadingProportional)));
            Assert.Null(p.At(6));                                                                // 1.1's levels stop at 5
        }
        var h = d.Find("blood_healing")!;
        Assert.Equal((PowerKind.Cast, PowerData.EffectHeal, "common.blood_healing", ""), (h.Kind, h.Effect, h.Option, h.Trait));
        Assert.Equal([5, 5, 5, 5, 5], h.Ranks.Select(r => r.Cost));                              // game.ndd:108
        Assert.All(h.Ranks, r => Assert.Equal((0.0, 1.0), (r.Seconds, r.Recast)));               // instant; 1000 ms
        Assert.Equal([20, 30, 40, 60, 80], Enumerable.Range(1, 5).Select(k => h.AmountAt(k)));   // effects.ned:324 v1-v5
        Assert.Null(h.Readings);
        Assert.Equal(("Blood Healing", "Blood Heal"), (h.Label, h.Short));                      // the full name in words, the short one on a slot
        Assert.Equal("Celerity", d.Find("celerity")!.Short);
    }

    [Fact]
    public void A_file_that_says_otherwise_is_refused_by_name()
    {
        foreach (var (from, to, words) in new[]
        {
            ("\"effect\": \"heal\"", "\"effect\": \"regenerate\"", "effect 'regenerate' is not one this module has"),
            ("{ \"rank\": 1, \"cost\": 5, \"seconds\": 0, \"recast\": 1.0, \"amount\": 20 }", "{ \"rank\": 1, \"cost\": 5, \"seconds\": 4, \"recast\": 1.0, \"amount\": 20 }", "a heal is instant"),
            ("{ \"rank\": 1, \"cost\": 5, \"seconds\": 15, \"recast\": 1.0, \"amount\": 5 }", "{ \"rank\": 1, \"cost\": 5, \"seconds\": 15, \"recast\": 1.0, \"amount\": 0 }", "amount 0 must be above nothing"),
            ("\"trait\": \"dexterity\"", "\"trait\": \"agility\"", "power blood_dexterity raises 'agility', which is no trait of the registry"),
            ("\"default\": \"raw\",\n        \"raw\":", "\"default\": \"halved\",\n        \"raw\":", "the default reading 'halved' is none of its readings"),
            ("\"slot_label\": \"Blood Str\"", "\"slot_label\": \"Blood Strength\"", "slot_label 'Blood Strength' is not 1 to 10 characters"),
        })
        {
            using var copy = new TestData.Copy();
            copy.Edit("rules/powers.json", s => { Assert.Contains(from, s); int i = s.IndexOf(from, StringComparison.Ordinal); return s[..i] + to + s[(i + from.Length)..]; });
            var x = Assert.Throws<ModuleRefusedException>(() => TestData.Sandbox(copy.Folder));
            Assert.Contains(x.Reasons, why => why.Contains(words));
        }
    }

    [Fact]
    public void The_player_learns_the_four_at_rank_1_and_its_slots_show_them_in_the_file_s_order_with_their_use()
    {
        var r = Make();
        var s = r.W.Get<PowerSlots>(r.Player).Slots;
        var cat = r.W.Service<IPowerCatalogue>();
        string[] labels = ["Celerity", "Blood Str", "Blood Dex", "Blood Sta", "Blood Heal"];           // the slots' short names (slot_label)
        for (int k = 0; k < 5; k++) Assert.Equal(labels[k], cat.Label(s[k].Power));
        Assert.Equal(["Celerity", "Blood Strength", "Blood Dexterity", "Blood Stamina", "Blood Healing"], Enumerable.Range(0, 5).Select(k => cat.Name(s[k].Power)));
        Assert.Equal([SlotUse.Engage, SlotUse.Engage, SlotUse.Engage, SlotUse.Engage, SlotUse.Hurt], Enumerable.Range(0, 5).Select(k => s[k].Use));
        Assert.Equal([30, 5, 5, 5, 5], Enumerable.Range(0, 5).Select(k => s[k].Cost));
        Assert.Equal(-1, s[5].Power);
        Assert.All(new[] { "blood_strength", "blood_dexterity", "blood_stamina", "blood_healing" },
            id => Assert.Equal(1, PowerBook.RankOf(r.Sheets, r.Player, id)));
        Assert.Equal(["common.blood_strength"], r.Route.OptionsOf("blood_strength"));
    }

    // ---- Blood Strength, Dexterity, Stamina ----

    [Theory]
    [InlineData("blood_strength", "strength", Strength)]
    [InlineData("blood_dexterity", "dexterity", Dexterity)]
    [InlineData("blood_stamina", "stamina", Stamina)]
    public void A_rise_is_paid_once_raises_the_effective_trait_by_rank_and_ends_exactly(string power, string trait, int slot)
    {
        int[] amounts = [5, 10, 15, 20, 25];
        double[] seconds = [15, 30, 45, 60, 90];
        for (int rank = 1; rank <= 5; rank++)
        {
            var r = Make();
            var p = r.Player;
            r.Sheets.Learn(p, power, rank, "common." + power);
            int purchased = r.Sheets.Purchased(p, trait);
            Assert.Equal(purchased, r.Sheets.Effective(p, trait));
            r.Press(p, slot);
            var u = Assert.Single(r.Used);
            Assert.Equal(("common." + power, rank, 5, 100, 95, amounts[rank - 1]), (u.Power, u.Rank, u.Cost, u.BloodBefore, u.BloodAfter, u.Amount));
            Assert.Equal((95L, 5L), (r.Blood(p), r.Spent));                                      // paid once
            Assert.Equal(purchased + amounts[rank - 1], r.Sheets.Effective(p, trait));
            Assert.Equal(purchased, r.Sheets.Purchased(p, trait));                               // the purchase never changes
            var m = Assert.Single(r.Sheets.Modifiers(p));
            Assert.Equal((trait, amounts[rank - 1], "common." + power, u.EndTick), (m.TraitId, m.Delta, m.SourceId, m.ExpiresAtTick));
            long end = u.Tick + (long)(seconds[rank - 1] * World.TicksPerSecond);
            Assert.Equal(end, u.EndTick);
            r.Run((int)(end - r.W.Tick - 1));
            Assert.Equal(purchased + amounts[rank - 1], r.Sheets.Effective(p, trait));          // still live the tick before
            r.Run();
            Assert.Equal(end, r.W.Tick);
            Assert.Equal(purchased, r.Sheets.Effective(p, trait));                               // from its end tick, the purchase again
            Assert.Empty(r.Ended);
            r.Run();                                                                             // the powers' step at its end tick tells it
            var x = Assert.Single(r.Ended);
            Assert.Equal((PowerEndCause.Expired, end, rank), (x.Cause, x.Tick, x.Rank));
            Assert.Equal(purchased, r.Sheets.Effective(p, trait));                               // restored exactly
            Assert.Empty(r.Sheets.Modifiers(p));
            Assert.Equal(95, r.Blood(p));
        }
    }

    [Fact]
    public void A_renewal_replaces_the_rise_at_the_rank_the_sheet_has_now_and_pays_again()
    {
        var r = Make();
        var p = r.Player;
        int purchased = r.Sheets.Purchased(p, "strength");
        r.Press(p, Strength);
        r.Run(2 * World.TicksPerSecond);
        r.Sheets.Learn(p, "blood_strength", 3, "common.blood_strength");
        r.Press(p, Strength);
        Assert.Equal(2, r.Used.Count);
        Assert.True(r.Used[1].Renewed);
        var renewed = Assert.Single(r.Ended);
        Assert.Equal((PowerEndCause.Renewed, r.Used[0].ActionId), (renewed.Cause, renewed.ActionId));
        var m = Assert.Single(r.Sheets.Modifiers(p));                                            // one record, one modifier: no stacking
        Assert.Equal((15, r.Used[1].EndTick), (m.Delta, m.ExpiresAtTick));
        Assert.Equal(purchased + 15, r.Sheets.Effective(p, "strength"));
        Assert.Equal(r.Used[1].Tick + 45 * World.TicksPerSecond, r.Used[1].EndTick);           // rank 3's 45 s from now
        Assert.Equal((90L, 10L), (r.Blood(p), r.Spent));
        Assert.Single(r.Sheets.Live(p));
    }

    [Fact]
    public void The_three_rises_are_three_records_on_three_traits_and_a_stop_ends_them_all()
    {
        var r = Make();
        var p = r.Player;
        int s0 = r.Sheets.Purchased(p, "strength"), d0 = r.Sheets.Purchased(p, "dexterity"), t0 = r.Sheets.Purchased(p, "stamina");
        r.Press(p, Strength);
        r.Run(World.TicksPerSecond / 2);
        r.Press(p, Dexterity);                                                                   // another power: its own recast
        r.Press(p, Stamina);
        Assert.Equal((s0 + 5, d0 + 5, t0 + 5), (r.Sheets.Effective(p, "strength"), r.Sheets.Effective(p, "dexterity"), r.Sheets.Effective(p, "stamina")));
        Assert.Equal(3, r.Sheets.Modifiers(p).Count);
        r.W.Set(p, new Dead { Tick = r.W.Tick });
        r.Run();
        Assert.Equal(3, r.Ended.Count(x => x.Cause == PowerEndCause.Died));
        Assert.Equal((s0, d0, t0), (r.Sheets.Effective(p, "strength"), r.Sheets.Effective(p, "dexterity"), r.Sheets.Effective(p, "stamina")));
        Assert.Empty(r.Sheets.Modifiers(p));
    }

    [Fact]
    public void At_purchased_1000_the_rise_passes_1000_visibly_and_for_its_time_only_and_the_purchase_stays_1000()
    {
        // The rules package's acceptance scenario: legitimate Blood Strength at permanent 1000.
        var r = Make();
        var p = r.Player;
        Assert.True(r.Route.TrySet(p, "strength", 1000, out string error), error);
        r.Sheets.Learn(p, "blood_strength", 5, "common.blood_strength");
        r.Press(p, Strength);
        Assert.Equal(1025, r.Sheets.Effective(p, "strength"));                                   // overcap, temporary
        Assert.True(r.Route.TryRead(p, "strength", out int seen));
        Assert.Equal(1025, seen);                                                                // what the program sees is the effective
        Assert.Equal(1000, r.Sheets.Purchased(p, "strength"));
        Assert.Equal((95L, 5L), (r.Blood(p), r.Spent));                                          // spent once
        r.Run(90 * World.TicksPerSecond);
        Assert.Equal(1000, r.Sheets.Effective(p, "strength"));                                   // expired: baseline
        Assert.Equal(PowerEndCause.Expired, Assert.Single(r.Ended).Cause);
    }

    [Fact]
    public void The_staged_harm_reads_the_risen_Strength_for_damage_and_the_risen_Stamina_for_soak()
    {
        var contacts = new List<(long, ContactMade)>();
        var r = Make(contacts: contacts);
        var (p, g) = (r.Player, r.Guard);
        int str = r.Sheets.Purchased(p, "strength"), sta = r.Sheets.Purchased(p, "stamina");
        r.Sheets.Learn(p, "blood_strength", 5, "common.blood_strength");
        r.Sheets.Learn(p, "blood_stamina", 4, "common.blood_stamina");
        // The player's blade on the guard before and after Blood Strength; the guard's on the player before and after Blood Stamina.
        ContactMade Hit(long id, EntityId a, EntityId t) => new(id, a, (int)id, t, ContactMet.Body, "chest", ContactMaterial.Cloth, 0, new Vector3(0, 1, 1.3f), new Vector3(0, 1, 0), 10f, 10f, false, false, true, false);
        long t0 = r.W.Tick;
        contacts.Add((t0 + 2, Hit(1, p, g)));
        contacts.Add((t0 + 3, Hit(2, g, p)));
        r.Run(5);
        r.Press(p, Strength);
        r.Run(World.TicksPerSecond);
        r.Press(p, Stamina);
        long t1 = r.W.Tick;
        contacts.Add((t1 + 2, Hit(3, p, g)));
        contacts.Add((t1 + 3, Hit(4, g, p)));
        r.Run(5);
        var harm = r.Trace.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.Contains("\"strength_effective\"") || l.Contains("\"stamina_effective\"")).Select(l => JsonDocument.Parse(l).RootElement).ToList();
        Assert.Equal(4, harm.Count);
        static int Input(JsonElement line, string key)
        {
            foreach (var ev in line.GetProperty("events").EnumerateArray())
                if (ev.TryGetProperty("inputs", out var i) && i.TryGetProperty(key, out var v)) return v.GetInt32();
            throw new InvalidOperationException($"no {key}");
        }
        Assert.Equal((str, str + 25), (Input(harm[0], "strength_effective"), Input(harm[2], "strength_effective")));
        Assert.Equal((sta, sta + 20), (Input(harm[1], "stamina_effective"), Input(harm[3], "stamina_effective")));
    }

    // ---- Blood Healing ----

    [Fact]
    public void Blood_Healing_heals_by_rank_at_once_never_past_the_maximum_and_holds_only_its_recast()
    {
        int[] amounts = [20, 30, 40, 60, 80];
        for (int rank = 1; rank <= 5; rank++)
        {
            var r = Make();
            var p = r.Player;
            r.Sheets.Learn(p, "blood_healing", rank, "common.blood_healing");
            r.Health(p, 10);
            r.Press(p, Healing);
            var u = Assert.Single(r.Used);
            Assert.Equal(("common.blood_healing", rank, 5, 95, 0f, amounts[rank - 1]), (u.Power, u.Rank, u.Cost, u.BloodAfter, u.Seconds, u.Amount));
            Assert.Equal(u.Tick, u.EndTick);                                                     // instant: no window
            Assert.Equal(10 + amounts[rank - 1], r.HealthNow(p));
            var s = r.W.Get<PowerSlots>(p).Slots[Healing - 1];
            Assert.Equal(SlotState.Recast, s.State);
            r.Run(World.TicksPerSecond);
            Assert.Empty(r.Ended);                                                               // an instant power ends nothing
            Assert.Empty(r.Sheets.Live(p));                                                      // its record gone with its recast
            Assert.Equal(SlotState.Ready, r.W.Get<PowerSlots>(p).Slots[Healing - 1].State);
        }
        var q = Make();
        q.Sheets.Learn(q.Player, "blood_healing", 5, "common.blood_healing");
        q.Health(q.Player, 90);
        q.Press(q.Player, Healing);
        Assert.Equal((100L, 10), (q.HealthNow(q.Player), Assert.Single(q.Used).Amount));        // never past the maximum: 10 of 80 gained
        Assert.Equal(5L, q.Spent);
    }

    // ---- refusals ----

    [Fact]
    public void Each_refusal_spends_nothing_and_has_no_effect_unlearned_short_recast_and_a_heal_at_full_Health()
    {
        var r = Make();
        var (p, g) = (r.Player, r.Guard);
        void Refused(EntityId who, PowerDenial want, Action act, string? words = null)
        {
            ulong sheet = r.Sheets.DigestOf(who);
            long spent = r.Spent;
            int n = r.Refused.Count;
            act();
            var x = Assert.Single(r.Refused.Skip(n));
            Assert.Equal((who, want), (x.Entity, x.Reason));
            if (words is not null) Assert.Equal(words, r.Book.LastReason);
            Assert.Equal(spent, r.Spent);
            Assert.Equal(sheet, r.Sheets.DigestOf(who));                                       // no record, no modifier, no Health, no Blood
        }

        // Unlearned: the option without the power's rank, and the power without its option.
        r.Sheets.Learn(g, "blood_strength", 1);
        Refused(g, PowerDenial.Unlearned, () => r.UseNow(g, "blood_strength"));
        Assert.Equal("Blood Strength's option common.blood_strength is not learned", r.Book.LastReason);
        r.Sheets.Unlearn(p, "blood_healing");
        Refused(p, PowerDenial.EmptySlot, () => r.Press(p, Healing));                            // the slot is gone with it
        Refused(p, PowerDenial.Unlearned, () => r.UseNow(p, "blood_healing"), "Blood Healing is not learned");
        r.Sheets.Learn(p, "blood_healing", 1, "common.blood_healing");

        // A heal at full Health does nothing: refused, nothing spent (1.1's RPG_DISC_NOBLOODHEALING).
        r.Health(p, 100);
        Refused(p, PowerDenial.NoEffect, () => r.Press(p, Healing), "Blood Healing: Health is full (100 of 100)");
        Assert.Equal(PowerDenial.NoEffect, r.Refused[^1].Reason);

        // Short of Blood: 4 of 5.
        var b = r.Sheets.Resource(p, "blood")!.Value;
        r.Sheets.SetResource(p, b with { Current = 4 });
        Refused(p, PowerDenial.NoBlood, () => r.Press(p, Strength));
        r.Sheets.SetResource(p, b);

        // The recast: a second press within the second.
        r.Press(p, Strength);
        Refused(p, PowerDenial.Recast, () => r.Press(p, Strength));
        r.Health(p, 50);
        r.Press(p, Healing);
        Refused(p, PowerDenial.Recast, () => r.Press(p, Healing));
        Assert.Equal(70, r.HealthNow(p));

        // Dead.
        r.W.Set(p, new Dead { Tick = r.W.Tick });
        r.Run();
        Refused(p, PowerDenial.Dead, () => r.Press(p, Dexterity));
    }

    // ---- save, load, determinism, allocation ----

    [Fact]
    public void A_sheet_with_a_rise_live_saves_and_loads_exactly_and_the_rise_ends_at_its_tick_after_the_load()
    {
        var r = Make();
        var p = r.Player;
        r.Press(p, Strength);
        r.Run(60);
        var data = r.Sheets.Export(p);
        string save = SheetJson.Write(data);
        Assert.Equal(data, SheetJson.Read(save));
        var other = Make(seed: 9);
        other.Run((int)(r.W.Tick - other.W.Tick));                                              // the same tick
        var e = other.Player;
        other.Sheets.Import(e, SheetJson.Read(save));
        Assert.Equal(r.Sheets.DigestOf(p), other.Sheets.DigestOf(e));
        Assert.Equal(r.Sheets.Effective(p, "strength"), other.Sheets.Effective(e, "strength"));
        long end = Assert.Single(r.Used).EndTick;
        other.Run((int)(end - other.W.Tick) + 1);
        Assert.Equal(PowerEndCause.Expired, Assert.Single(other.Ended).Cause);
        Assert.Equal(other.Sheets.Purchased(e, "strength"), other.Sheets.Effective(e, "strength"));
        Assert.Empty(other.Sheets.Modifiers(e));
    }

    [Fact]
    public void The_same_presses_give_the_same_digests_and_a_tick_with_a_rise_live_allocates_nothing()
    {
        var a = Make();
        var b = Make();
        int[] slots = [Strength, Dexterity, Stamina, Healing];
        for (int i = 0; i < 1800; i++)
        {
            if (i % 150 == 7)
                foreach (var r in new[] { a, b })
                {
                    if (i == 307) r.Health(r.Player, 40);
                    r.W.Set(r.Player, new PowerIntent { Slot = slots[i / 150 % 4] });
                }
            a.Run(); b.Run();
            Assert.Equal(a.W.Digest(), b.W.Digest());
        }
        Assert.True(a.Used.Count >= 8);
        Assert.Contains(a.Used, u => u.Power == "common.blood_healing" && u.Amount == 20);

        var q = Make();
        q.Press(q.Player, Strength);
        q.Press(q.Player, Stamina);
        q.Run(60);
        Allocations.None("300 ticks of the Blood commands", () => { for (int i = 0; i < 300; i++) q.W.Step(); });
    }
}
