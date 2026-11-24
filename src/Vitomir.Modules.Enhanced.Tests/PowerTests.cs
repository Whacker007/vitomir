// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Modules.Enhanced.Rules;
using Vitomir.Modules.Enhanced.Sheets;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Powers;

namespace Vitomir.Modules.Enhanced.Tests;

/// <summary>
/// Blood and the first activated power (by design): the module's <c>rules/blood</c> and
/// <c>rules/powers</c> read strictly; Blood bought 0 to 1000 (1.1's units, 100 by default), its maximum following, a mortal
/// without; the ledger's reserve, commit and release, an action id spending once, the throughput's limits; every refusal named,
/// nothing spent and no effect; Celerity paid for a time by rank (1.1's 30 Blood, 15 to 90 s), its pace scale and its tempo,
/// its exact end, a renewal replacing the record and paying again, both curves and the attack's switch, a stop ending it;
/// learned and unpaid the world without it, digest for digest; the views (vitals, slots, the catalogue); the sheet saved and
/// loaded exactly; the same digests from the same presses; nothing allocated per tick; options learned by name.
/// </summary>
[Collection(AllocationCollection.Name)]   // it measures allocations (Allocations.cs)
public sealed class PowerTests
{
    sealed record Rig(World W, ActorSheets Sheets, PowerBook Book, IPersonSheets Route, EntityId Player, EntityId Guard)
    {
        public readonly List<PowerUsed> Used = [];
        public readonly List<PowerRefused> Refused = [];
        public readonly List<PowerEnded> Ended = [];

        /// <summary>Steps <paramref name="ticks"/> ticks, collecting the powers' events.</summary>
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
        public void Press(EntityId who, int slot = 1)
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
    }

    /// <summary>
    /// A world with the module (sandbox) and two people as the program makes them: the player (<c>sandbox.player</c>: Celerity 1,
    /// Potence 1, Blood 100 of 100) and a guard (<c>sandbox.guard_trained</c>, a mortal), both armed (the tempo's fighters).
    /// </summary>
    static Rig Make(string? moduleFolder = null, ulong seed = 5, params string[] switches)
    {
        var m = TestData.Sandbox(moduleFolder, [EnhancedModule.CombatTempoOffSwitch, .. switches]);   // Celerity's factors at the earlier build's pace (the global tempo: PaceTests)
        var b = new WorldBuilder(seed);
        DeathServices.Register(b);
        b.Component<Combatant>().Component<CombatIntent>();
        m.Register(b);
        var w = b.Build();
        w.Service<HarmWeapons>().Of = (_, _) => "BroadSword";
        var people = w.Service<PeopleHooks>();
        var (p, g) = (w.Create(), w.Create());
        people.Made(p, PersonRole.Player, "Christof");
        people.Made(g, PersonRole.Townsperson, "rudolfGuard");
        CombatServices.Arm(w, p);
        CombatServices.Arm(w, g);
        return new Rig(w, w.Service<ActorSheets>(), w.Service<PowerBook>(), w.Service<IPersonSheets>(), p, g);
    }

    static Rig With(params string[] switches) => Make(null, 5, switches);

    // ---- the data ----

    [Fact]
    public void The_powers_are_Celerity_cast_on_1_1_s_numbers_and_Potence_and_Fortitude_passive_and_Blood_is_1_1_s_pool()
    {
        var m = TestData.Sandbox();
        var p = m.Powers;
        Assert.Equal(["celerity", "potence", "fortitude", "blood_strength", "blood_dexterity", "blood_stamina", "blood_healing"], p.Powers.Select(x => x.Id));   // the Blood commands after
        var c = p.Find("celerity")!;
        Assert.Equal((PowerKind.Cast, "Celerity", "celerity.active", "celerity"), (c.Kind, c.Label, c.Option, c.Effect));
        Assert.Equal([30, 30, 30, 30, 30], c.Ranks.Select(r => r.Cost));                         // game.ndd:136
        Assert.Equal([15.0, 30, 45, 60, 90], c.Ranks.Select(r => r.Seconds));                    // CELERITY_DURATIONS
        Assert.All(c.Ranks, r => Assert.Equal(1.0, r.Recast));                                   // game.ndd:136, 1000 ms
        Assert.Null(c.At(6));                                                                    // ranks 6 to 10: no semantics (gate-29)
        Assert.Null(c.At(10));
        Assert.Equal("split", c.DefaultCurve);                                                  // speed plus flurry
        Assert.Equal([2.0, 3, 4, 5, 6], c.Curves["split"].Move);                                  // the actions, 1 + rank
        Assert.Equal([2.0, 3, 4, 5, 6], c.Curves["split"].Attack);
        Assert.Equal((0.5, 0.5), (c.Curves["split"].AttackExponent, c.Curves["split"].MoveExponent));
        Assert.Equal([2.0, 3, 4, 5, 6], c.Curves[PowerData.OnePlusRank].Move);                  // The default curve: 1 + rank
        Assert.Equal([2.0, 3, 4, 5, 6], c.Curves[PowerData.OnePlusRank].Attack);
        Assert.Equal((1.0, 1.0), (c.Curves[PowerData.OnePlusRank].AttackExponent, c.Curves[PowerData.OneOne].AttackExponent));   // all tempo
        Assert.Equal([1.5, 2.0, 2.5, 3.0, 3.5], c.Curves[PowerData.OneOne].Move);               // effects.ned:314
        Assert.Equal([1.1, 1.2, 1.3, 1.4, 1.5], c.Curves[PowerData.OneOne].Attack);             // effects.ned:315
        Assert.Equal((PowerKind.Passive, "potence.passive"), (p.Find("potence")!.Kind, p.Find("potence")!.Option));
        Assert.Equal((PowerKind.Passive, "fortitude.passive"), (p.Find("fortitude")!.Kind, p.Find("fortitude")!.Option));
        var b = m.Blood;
        Assert.Equal(("blood", "blood_pool", 0, 1000, 100), (b.Resource, b.PurchaseId, b.PurchaseMin, b.PurchaseMax, b.PurchaseDefault));   // Christof.NPC:29
        Assert.Equal(["vampire"], b.Species);
        Assert.Equal((0L, 0L), (b.PerActionLimit, b.PerSecondLimit));                           // no limits, as 1.1
        Assert.False(m.CelerityOneOne);
        Assert.True(m.CelerityAttackOn);
        Assert.True(TestData.Sandbox(null, EnhancedModule.CelerityCurveSwitch).CelerityOneOne);
        Assert.Equal("split", m.CelerityCurveName(c));                                          // the curve each switch chooses
        Assert.Equal(PowerData.OnePlusRank, TestData.Sandbox(null, EnhancedModule.CelerityTempoSwitch).CelerityCurveName(c));
        Assert.Equal(PowerData.OneOne, TestData.Sandbox(null, EnhancedModule.CelerityCurveSwitch).CelerityCurveName(c));
        var both = Assert.Throws<ModuleRefusedException>(() => TestData.Sandbox(null, EnhancedModule.CelerityCurveSwitch, EnhancedModule.CelerityTempoSwitch));
        Assert.Contains(both.Reasons, x => x.Contains("each choose Celerity's curve: give one"));
        Assert.False(TestData.Sandbox(null, EnhancedModule.CelerityAttackOffSwitch).CelerityAttackOn);

        // The catalogue, under the platform's interface.
        var r = Make();
        var cat = r.W.Service<IPowerCatalogue>();
        Assert.Equal(7, cat.Count);                                                       // and the four Blood commands
        Assert.Equal(("celerity.active", "Celerity"), (cat.Id(0), cat.Label(0)));
        Assert.Equal(("potence.passive", "Potence"), (cat.Id(1), cat.Label(1)));
    }

    [Fact]
    public void A_wrong_powers_or_blood_file_or_an_out_of_range_pool_on_a_sheet_is_refused_by_name()
    {
        using (var copy = new TestData.Copy())
        {
            copy.Edit("rules/blood.json", s => s.Replace("\"default\": 100", "\"default\": 1001"));
            var x = Assert.Throws<ModuleRefusedException>(() => TestData.Sandbox(copy.Folder));
            Assert.Contains(x.Reasons, r => r.Contains("rules/blood") && r.Contains("default 1001"));
        }
        using (var copy = new TestData.Copy())
        {
            copy.Edit("rules/powers.json", s => s.Replace("\"kind\": \"cast\"", "\"kind\": \"toggle\""));
            var x = Assert.Throws<ModuleRefusedException>(() => TestData.Sandbox(copy.Folder));
            Assert.Contains(x.Reasons, r => r.Contains("kind 'toggle' is neither passive nor cast"));
        }
        using (var copy = new TestData.Copy())
        {
            copy.Edit("rules/powers.json", s => s.Replace("\"attack\": [ 1.1, 1.2, 1.3, 1.4, 1.5 ]", "\"attack\": [ 1.1, 1.2, 1.3, 0.4, 1.5 ]"));
            var x = Assert.Throws<ModuleRefusedException>(() => TestData.Sandbox(copy.Folder));
            Assert.Contains(x.Reasons, r => r.Contains("factor 0.4 is below 1"));
        }
        using (var copy = new TestData.Copy())
        {
            copy.Edit("sheets/player.json", s => s.Replace("\"blood_pool\": 100", "\"blood_pool\": 1001"));
            var x = Assert.Throws<ModuleRefusedException>(() => TestData.Sandbox(copy.Folder));
            Assert.Contains(x.Reasons, r => r.Contains("purchased blood_pool = 1001 is outside 0..1000"));
        }
        using (var copy = new TestData.Copy())
        {
            copy.Edit("sheets/player.json", s => s.Replace("\"celerity.active\"", "\"celerity.dash\""));
            var x = Assert.Throws<ModuleRefusedException>(() => TestData.Sandbox(copy.Folder));
            Assert.Contains(x.Reasons, r => r.Contains("option celerity.dash is none of"));
        }
        // A save with a pool out of range, given whole.
        var rig = Make();
        string save = SheetJson.Write(rig.Sheets.Export(rig.Player)).Replace("\"blood_pool\": 100", "\"blood_pool\": -1");
        Assert.False(rig.Route.TryImport(rig.W.Create(), save, out string error));
        Assert.Contains("purchased blood_pool = -1 is outside 0..1000", error);
    }

    // ---- Blood and the ledger ----

    [Fact]
    public void The_ledger_reserves_commits_and_releases_whole_numbers_and_an_action_id_spends_once()
    {
        var r = Make();
        var ledger = r.W.Service<BloodLedger>();
        var p = r.Player;
        Assert.Equal(100, r.Blood(p));
        long a = ledger.NewActionId();
        Assert.True(ledger.TryReserve(p, 30, a, out _));
        Assert.Equal((100L, 30L), (r.Blood(p), ledger.Held(p)));                       // held, not yet spent
        Assert.True(ledger.Commit(a, out long before, out long after));
        Assert.Equal((100L, 70L, 70L, 0L), (before, after, r.Blood(p), ledger.Held(p)));
        Assert.False(ledger.Commit(a, out _, out _));                                  // a replayed commit spends nothing
        Assert.Contains("committed already or released", ledger.LastRefusal);
        Assert.False(ledger.TryReserve(p, 30, a, out string why));                     // nor does a replayed reservation
        Assert.Contains($"action id {a} was used already", why);
        Assert.Equal(70, r.Blood(p));

        long b = ledger.NewActionId();
        Assert.False(ledger.TryReserve(p, 71, b, out why));                             // never below nothing
        Assert.Equal("not enough Blood (70 of 71)", why);
        long c = ledger.NewActionId();
        Assert.True(ledger.TryReserve(p, 50, c, out _));
        long d = ledger.NewActionId();
        Assert.False(ledger.TryReserve(p, 30, d, out why));                            // 70 less the 50 held
        Assert.Equal("not enough Blood (20 of 30)", why);
        Assert.True(ledger.Release(c));
        Assert.False(ledger.Release(c));
        Assert.Equal((70L, 0L), (r.Blood(p), ledger.Held(p)));                         // released: nothing spent
        Assert.False(ledger.TryReserve(r.Guard, 1, ledger.NewActionId(), out why));    // a mortal has no Blood
        Assert.Contains("has no Blood", why);
        Assert.False(ledger.TryReserve(p, -1, ledger.NewActionId(), out why));
        Assert.Equal(30, ledger.Spent);                                                // conserved: 100 − 70
        Assert.Equal((2, 1, 1), (ledger.Reserves, ledger.Commits, ledger.Releases));
    }

    [Fact]
    public void Blood_pool_is_bought_from_0_to_1000_the_maximum_follows_and_a_mortal_has_none()
    {
        var r = Make();
        var (p, g) = (r.Player, r.Guard);
        Assert.True(r.Route.TrySet(p, "blood_pool", 500, out string error), error);
        Assert.Equal(new ResourceState("blood", 500, 500, 0, 0), r.Sheets.Resource(p, "blood"));   // a full pool stays full
        Assert.True(r.Route.TryRead(p, "blood_pool", out int pool));
        Assert.Equal(500, pool);
        r.Sheets.SetResource(p, r.Sheets.Resource(p, "blood")!.Value with { Current = 470 });
        Assert.True(r.Route.TrySet(p, "blood_pool", 300, out error), error);
        Assert.Equal((300L, 300L), (r.Sheets.Resource(p, "blood")!.Value.Current, r.Sheets.Resource(p, "blood")!.Value.Maximum));   // held to it
        r.Sheets.SetResource(p, r.Sheets.Resource(p, "blood")!.Value with { Current = 250 });
        Assert.True(r.Route.TrySet(p, "blood_pool", 400, out error), error);
        Assert.Equal((250L, 400L), (r.Sheets.Resource(p, "blood")!.Value.Current, r.Sheets.Resource(p, "blood")!.Value.Maximum));   // not full: kept
        Assert.True(r.Route.TryRead(p, "blood", out int now));
        Assert.Equal(250, now);
        Assert.False(r.Route.TrySet(p, "blood_pool", 1001, out error));
        Assert.Contains("purchased blood_pool = 1001 is refused: the domain is 0..1000", error);
        Assert.False(r.Route.TrySet(p, "blood_pool", -1, out error));
        Assert.Equal(400, r.Sheets.Bought(p, "blood_pool"));
        Assert.False(r.Route.TrySet(g, "blood_pool", 100, out error));               // a mortal has no Blood
        Assert.Contains("a human has no Blood", error);
        Assert.Null(r.Sheets.Bought(g, "blood_pool"));
        // The vitals: Health and Blood for everyone with a sheet; a mortal shows none to spend.
        r.Run();
        var v = r.W.Get<Vitals>(p);
        Assert.Equal((100f, 100f, 250f, 400f, true), (v.Health, v.HealthMax, v.Blood, v.BloodMax, v.HasBlood));
        var vg = r.W.Get<Vitals>(g);
        Assert.Equal((100f, 100f, 0f, 0f, false), (vg.Health, vg.HealthMax, vg.Blood, vg.BloodMax, vg.HasBlood));
        // A template given whole gets its Blood; a vampire's template without a pool gets 1.1's 100, full.
        var e = r.W.Create();
        Assert.True(r.Route.TryTemplate(e, "sandbox.newly_embraced", out error), error);
        Assert.Equal((100, new ResourceState("blood", 100, 100, 0, 0)), (r.Sheets.Bought(e, "blood_pool"), r.Sheets.Resource(e, "blood")));
    }

    [Fact]
    public void Blood_s_throughput_limits_refuse_by_name_and_are_none_by_default()
    {
        using var copy = new TestData.Copy();
        copy.Edit("rules/blood.json", s => s.Replace("\"per_action\": 0", "\"per_action\": 25"));
        var r = Make(copy.Folder);
        var ledger = r.W.Service<BloodLedger>();
        Assert.False(ledger.TryReserve(r.Player, 30, ledger.NewActionId(), out string why));
        Assert.StartsWith("30 is over the per-action limit of 25", why);
        Assert.Equal(PowerDenial.NoBlood, r.Book.Use(r.W, r.Player, "celerity"));      // Celerity's 30 is refused so: nothing spent
        Assert.Equal(100, r.Blood(r.Player));

        using var second = new TestData.Copy();
        second.Edit("rules/blood.json", s => s.Replace("\"per_second\": 0", "\"per_second\": 40"));
        var q = Make(second.Folder);
        var l2 = q.W.Service<BloodLedger>();
        long a = l2.NewActionId();
        Assert.True(l2.TryReserve(q.Player, 30, a, out _));
        Assert.True(l2.Commit(a, out _, out _));
        q.Run(World.TicksPerSecond - 1);
        Assert.False(l2.TryReserve(q.Player, 30, l2.NewActionId(), out why));          // 30 + 30 in one second > 40
        Assert.StartsWith("30 more would pass the per-second limit of 40 (30 spent or held in the last second", why);
        q.Run();
        long b = l2.NewActionId();
        Assert.True(l2.TryReserve(q.Player, 30, b, out why), why);                     // a second later
        Assert.True(l2.Commit(b, out _, out long after));
        Assert.Equal(40, after);
        Assert.Equal(new ResourceState("blood", 40, 100, 0, 40), q.Sheets.Resource(q.Player, "blood"));   // the D1 limits on the resource
    }

    // ---- the refusals ----

    [Fact]
    public void Each_refusal_names_its_reason_spends_nothing_and_has_no_effect()
    {
        var r = Make();
        var (p, g) = (r.Player, r.Guard);
        r.Run(2);
        void Refused(EntityId who, PowerDenial want, Action act, string? words = null)
        {
            ulong sheetBefore = r.Sheets.Has(who) ? r.Sheets.DigestOf(who) : 0;
            float paceBefore = r.W.Has<PaceScale>(who) ? r.W.Get<PaceScale>(who).Factor : 0f;
            long spent = r.W.Service<BloodLedger>().Spent;
            int n = r.Refused.Count;
            act();
            var x = Assert.Single(r.Refused.Skip(n));
            Assert.Equal((who, want), (x.Entity, x.Reason));
            if (words is not null) Assert.Equal(words, r.Book.LastReason);
            Assert.Equal(spent, r.W.Service<BloodLedger>().Spent);                     // nothing spent
            if (r.Sheets.Has(who)) Assert.Equal(sheetBefore, r.Sheets.DigestOf(who));  // no record, no Blood
            Assert.Equal(paceBefore, r.W.Has<PaceScale>(who) ? r.W.Get<PaceScale>(who).Factor : 0f);   // no effect
        }

        var nobody = r.W.Create();
        Refused(nobody, PowerDenial.NoSheet, () => r.Press(nobody));
        Assert.Null(r.Refused[^1].Power);
        Refused(p, PowerDenial.EmptySlot, () => r.Press(p, 6), "slot 6 holds nothing");          // slots 1 to 5 hold Celerity and the Blood commands
        Refused(g, PowerDenial.EmptySlot, () => r.Press(g, 1));                                   // a guard has no cast power
        r.Sheets.Learn(g, "celerity", 1);                                                          // learned without its option
        Refused(g, PowerDenial.Unlearned, () => r.Press(g, 1), "Celerity's option celerity.active is not learned");
        Assert.Equal("celerity.active", r.Refused[^1].Power);
        Refused(p, PowerDenial.Passive, () => r.UseNow(p, "potence"), "Potence is passive: always on, nothing to use");
        Refused(p, PowerDenial.Unlearned, () => r.UseNow(p, "fortitude"), "Fortitude is not learned");
        r.Sheets.Learn(p, "celerity", 6);
        Refused(p, PowerDenial.NoSemantics, () => r.Press(p), "Celerity 6 has no semantics here (ranks 1 to 5; gate-29)");
        Assert.Equal(SlotState.Unusable, r.W.Get<PowerSlots>(p).Slots[0].State);
        r.Sheets.Learn(p, "celerity", 1);
        r.Sheets.SetResource(p, r.Sheets.Resource(p, "blood")!.Value with { Current = 20 });
        r.Run();
        Assert.Equal(SlotState.Short, r.W.Get<PowerSlots>(p).Slots[0].State);
        Refused(p, PowerDenial.NoBlood, () => r.Press(p), "not enough Blood (20 of 30)");
        r.Sheets.SetResource(p, r.Sheets.Resource(p, "blood")!.Value with { Current = 100 });
        r.Press(p);                                                                                // paid
        Assert.Equal(70, r.Blood(p));
        Refused(p, PowerDenial.Recast, () => r.Press(p));                                         // within its 1 s recast
        Assert.StartsWith("Celerity's recast runs", r.Book.LastReason);
        Assert.Single(r.Used);
        r.W.Set(p, new Dead { Tick = r.W.Tick });
        r.Run(World.TicksPerSecond);
        Refused(p, PowerDenial.Dead, () => r.Press(p), $"{p} is dead");
        Assert.Equal(70, r.Blood(p));
    }

    // ---- Celerity ----

    [Fact]
    public void Celerity_paid_costs_30_Blood_scales_the_pace_and_the_tempo_and_ends_at_its_exact_tick()
    {
        var r = Make();
        var p = r.Player;
        Assert.True(r.Route.TrySet(p, "melee", 3, out string error), error);
        r.Run(3);
        var skill = r.W.Get<CombatTempo>(p);
        Assert.Equal(Math.Pow(1.3, 0.3), skill.Swing, 4);                             // Melee 3, unpaid Celerity adds nothing
        Assert.False(r.W.Has<PaceScale>(p));
        long t0 = r.W.Tick;
        r.Press(p);
        var u = Assert.Single(r.Used);
        Assert.Equal(new PowerUsed(p, "celerity.active", 1, 1, t0, u.ActionId, 30, 100, 70, 15f, false, t0 + 15 * World.TicksPerSecond), u);
        Assert.Equal(70, r.Blood(p));
        float t1 = CelerityEffect.Split(2, 0.5).Tempo;                                 // the split's tempo, (1 + 1)^0.5
        Assert.Equal(1.41421354f, t1, 6);
        Assert.Equal(t1, r.W.Get<PaceScale>(p).Factor);
        var fast = r.W.Get<CombatTempo>(p);
        Assert.Equal((skill.Swing * t1, skill.Recovery * t1, skill.Rise * t1), (fast.Swing, fast.Recovery, fast.Rise));   // skill × Celerity's tempo
        var live = Assert.Single(r.Sheets.Live(p));
        Assert.Equal(new LivePower("celerity", "celerity.active", 1, t0, t0 + 1800, t0 + 120, u.ActionId, false), live);
        r.Run((int)(t0 + 1800 - r.W.Tick));                                            // to the last live tick
        Assert.True(r.W.Has<PaceScale>(p));
        Assert.Empty(r.Ended);
        r.Run();                                                                       // the end tick
        var end = Assert.Single(r.Ended);
        Assert.Equal(new PowerEnded(p, "celerity.active", t0 + 1800, PowerEndCause.Expired, t0, u.ActionId, 1), end);
        Assert.False(r.W.Has<PaceScale>(p));
        Assert.Equal(skill, r.W.Get<CombatTempo>(p));                                  // back to the skill's alone
        Assert.Empty(r.Sheets.Live(p));                                                // its recast long over: the record goes
        Assert.Equal(70, r.Blood(p));                                                  // no regeneration
    }

    [Fact]
    public void A_press_while_live_renews_it_pays_again_and_keeps_one_record_at_the_rank_the_sheet_has_now()
    {
        var r = Make();
        var p = r.Player;
        r.Press(p);
        long t0 = r.Used[0].Tick;
        r.Run(300);
        r.Sheets.Learn(p, "celerity", 2);                                              // raised while live
        long t1 = r.W.Tick;
        r.Press(p);
        Assert.Equal(2, r.Used.Count);
        var end = Assert.Single(r.Ended);
        Assert.Equal((PowerEndCause.Renewed, t1, t0), (end.Cause, end.Tick, end.StartTick));
        var renewed = r.Used[1];
        Assert.True(renewed.Renewed);
        Assert.Equal((2, 70, 40, 30f, t1 + 30 * World.TicksPerSecond), (renewed.Rank, renewed.BloodBefore, renewed.BloodAfter, renewed.Seconds, renewed.EndTick));
        var live = Assert.Single(r.Sheets.Live(p));                                    // one record: no stacking
        Assert.Equal((2, t1, t1 + 3600), (live.Rank, live.StartTick, live.EndTick));   // now plus rank 2's 30 s (1.1 kept the old value: not copied)
        Assert.Equal(CelerityEffect.Split(3, 0.5).Tempo, r.W.Get<PaceScale>(p).Factor);   // rank 2's: (1 + 2)^0.5
        Assert.Equal(40, r.Blood(p));
    }

    [Fact]
    public void Celerity_runs_on_1_1_s_curve_with_its_switch_and_moves_alone_with_its_attack_off()
    {
        var one = With(EnhancedModule.CelerityCurveSwitch);
        one.Sheets.Learn(one.Player, "celerity", 2);
        one.Run(2);
        var skill = one.W.Get<CombatTempo>(one.Player);
        one.Press(one.Player);
        Assert.Equal(2.0f, one.W.Get<PaceScale>(one.Player).Factor);                   // effects.ned:314 v2
        Assert.Equal(skill.Swing * 1.2f, one.W.Get<CombatTempo>(one.Player).Swing);    // effects.ned:315 v2
        Assert.Equal(30f, one.Used[0].Seconds);

        var off = With(EnhancedModule.CelerityAttackOffSwitch);
        off.Sheets.Learn(off.Player, "celerity", 2);
        off.Run(2);
        var before = off.W.Get<CombatTempo>(off.Player);
        off.Press(off.Player);
        Assert.Equal(CelerityEffect.Split(3, 0.5).Tempo, off.W.Get<PaceScale>(off.Player).Factor);
        Assert.Equal(before, off.W.Get<CombatTempo>(off.Player));                      // the attack's part off

        // The skill's tempo off: Celerity's alone, only while it is live.
        var alone = With(EnhancedModule.TempoOffSwitch);
        alone.Run(2);
        Assert.False(alone.W.Has<CombatTempo>(alone.Player));
        alone.Press(alone.Player);
        float a1 = CelerityEffect.Split(2, 0.5).Tempo;
        Assert.Equal(new CombatTempo(a1, a1, a1), alone.W.Get<CombatTempo>(alone.Player));
        alone.Run(15 * World.TicksPerSecond);
        Assert.False(alone.W.Has<CombatTempo>(alone.Player));
    }

    [Fact]
    public void A_zero_Health_stop_ends_a_live_power()
    {
        var r = Make();
        var p = r.Player;
        r.Press(p);
        r.Run(10);
        r.W.Set(p, new Dead { Tick = r.W.Tick });
        long at = r.W.Tick;
        r.Run();
        var end = Assert.Single(r.Ended);
        Assert.Equal((PowerEndCause.Died, at), (end.Cause, end.Tick));
        Assert.False(r.W.Has<PaceScale>(p));
        Assert.Equal(SlotState.Unusable, r.W.Get<PowerSlots>(p).Slots[0].State);
    }

    [Fact]
    public void Celerity_learned_and_unpaid_is_the_same_world_without_it_digest_for_digest()
    {
        var a = Make();
        var b = Make();
        Assert.True(b.Sheets.Unlearn(b.Player, "celerity"));
        for (int i = 0; i < 600; i++)
        {
            a.Run(); b.Run();
            Assert.False(a.W.Has<PaceScale>(a.Player));
        }
        Assert.Empty(a.Used);
        a.Sheets.Unlearn(a.Player, "celerity");                                         // the sheets alike: the rest of the world is
        Assert.Equal(b.W.Digest(), a.W.Digest());
        Assert.Equal(b.W.Get<CombatTempo>(b.Player), a.W.Get<CombatTempo>(a.Player));
    }

    [Fact]
    public void The_slots_show_the_power_its_state_its_time_and_its_cost_and_only_who_has_a_cast_power_has_them()
    {
        var r = Make();
        var p = r.Player;
        r.Run();
        var s = r.W.Get<PowerSlots>(p).Slots;
        Assert.Equal((0, SlotState.Ready, 30), (s[0].Power, s[0].State, s[0].Cost));
        for (int k = 1; k <= 4; k++) Assert.Equal((2 + k, SlotState.Ready, 5), (s[k].Power, s[k].State, s[k].Cost));   // the Blood commands, 5 Blood each
        for (int k = 5; k < QuickSlots.Count; k++) Assert.Equal((-1, SlotState.Empty), (s[k].Power, s[k].State));
        Assert.False(r.W.Has<PowerSlots>(r.Guard));                                    // no cast power: no slots
        Assert.True(r.W.Has<Vitals>(r.Guard));
        r.Press(p);
        s = r.W.Get<PowerSlots>(p).Slots;
        Assert.Equal((SlotState.Live, 15f, 15f), (s[0].State, s[0].SecondsLeft, s[0].SecondsTotal));
        Assert.Equal(1f, s[0].RecastLeft, 3);
        r.Run(World.TicksPerSecond * 15);
        s = r.W.Get<PowerSlots>(p).Slots;
        Assert.Equal((SlotState.Ready, 0f), (s[0].State, s[0].SecondsLeft));
        Assert.Equal(new SlotView { Power = 0, State = SlotState.Ready, Cost = 30, Tempo = 1f }, s[0]);   // Tempo 1 when not live
    }

    // ---- save, load, determinism, allocation ----

    [Fact]
    public void A_sheet_with_Blood_and_a_live_power_saves_and_loads_exactly_and_one_without_saves_as_before()
    {
        var r = Make();
        r.Press(r.Player);
        var data = r.Sheets.Export(r.Player);
        string save = SheetJson.Write(data);
        Assert.Contains("\"live\"", save);
        Assert.Contains("\"blood_pool\": 100", save);
        Assert.Equal(data, SheetJson.Read(save));
        var other = Make(seed: 9);
        var e = other.W.Create();
        other.Sheets.Import(e, SheetJson.Read(save));
        Assert.Equal(r.Sheets.DigestOf(r.Player), other.Sheets.DigestOf(e));
        Assert.Equal(r.Sheets.Live(r.Player), other.Sheets.Live(e));
        Assert.DoesNotContain("\"live\"", SheetJson.Write(r.Sheets.Export(r.Guard)));  // a sheet with no record saves as before
        Assert.DoesNotContain("blood", SheetJson.Write(r.Sheets.Export(r.Guard)));
    }

    [Fact]
    public void The_same_presses_give_the_same_digests_and_a_tick_allocates_nothing()
    {
        var a = Make();
        var b = Make();
        for (int i = 0; i < 2400; i++)
        {
            if (i % 500 == 3) { a.W.Set(a.Player, new PowerIntent { Slot = 1 }); b.W.Set(b.Player, new PowerIntent { Slot = 1 }); }
            a.Run(); b.Run();
            Assert.Equal(a.W.Digest(), b.W.Digest());
        }
        Assert.Equal(3, a.Used.Count);                                                 // 100 → 70 → 40 → 10: the last two presses short
        Assert.Equal(2, a.Used.Count(u => u.Renewed));
        Assert.Equal(2, a.Refused.Count(x => x.Reason == PowerDenial.NoBlood));

        var r = Make();
        r.Press(r.Player);
        r.Run(60);
        Allocations.None("302 ticks of Celerity", () => { r.W.Step(); r.W.Step(); for (int i = 0; i < 300; i++) r.W.Step(); });
    }

    // ---- learning with options ----

    [Fact]
    public void Learning_with_options_refuses_an_unknown_option_another_power_s_and_a_power_the_module_lacks()
    {
        var r = Make();
        var g = r.Guard;
        Assert.False(r.Route.TryLearn(g, "celerity", 2, ["celerity.passive"], out string error));
        Assert.Contains("the module knows no option 'celerity.passive'", error);
        Assert.False(r.Route.TryLearn(g, "celerity", 2, ["potence.passive"], out error));
        Assert.Contains("option 'potence.passive' is Potence's, not celerity's", error);
        Assert.False(r.Route.TryLearn(g, "dominate", 1, ["dominate.command"], out error));
        Assert.Contains("the module has no power 'dominate'", error);
        Assert.Equal(0, PowerBook.RankOf(r.Sheets, g, "celerity"));
        Assert.True(r.Route.TryLearn(g, "celerity", 2, ["celerity.active"], out error), error);
        Assert.True(PowerBook.OptionLearned(r.Sheets, g, "celerity", "celerity.active"));
        Assert.True(r.Route.TryLearn(g, "fortitude", 3, ["fortitude.passive"], out error), error);
        Assert.True(r.Route.TryLearn(g, "fortitude", 0, ["fortitude.passive"], out error), error);   // 0 unlearns
        Assert.Equal(0, PowerBook.RankOf(r.Sheets, g, "fortitude"));
        Assert.Equal(["celerity.active"], r.Route.OptionsOf("celerity"));
        Assert.Empty(r.Route.OptionsOf("dominate"));

        // An option the data lists that no rule reads is refused by name: learned, it would do nothing.
        using var copy = new TestData.Copy();
        copy.Edit("rules/powers.json", s => s.Replace("\"options\": [ \"celerity.active\" ]", "\"options\": [ \"celerity.active\", \"celerity.dash\" ]"));
        var q = Make(copy.Folder);
        Assert.False(q.Route.TryLearn(q.Guard, "celerity", 1, ["celerity.dash"], out error));
        Assert.Contains("none of the options celerity.dash of Celerity is read by a rule", error);
    }
}
