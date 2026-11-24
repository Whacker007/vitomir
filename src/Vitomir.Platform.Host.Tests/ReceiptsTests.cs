// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Host.Arena;
using Vitomir.Platform.Host.Scenario;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Powers;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// The receipts against stand-ins for the records contact, the guard, harm and the powers publish (no game files):
/// the built-in check <c>blockedBodyNoWound</c> keyed by the physical action (A1: an independent arrow's wound is not an
/// Earlier blocked arrow's; the same action blocked then wounding still fails), a check with no case not applicable.
/// </summary>
public sealed class ReceiptsTests
{
    /// <summary>A world whose stand-in publishes <paramref name="script"/>'s events at their ticks; the log and the tally over it, run to <paramref name="ticks"/>.</summary>
    internal static (World World, ScenarioEventLog Log, ScenarioTally Tally, EntityId A, EntityId T, ScenarioReceipts Receipts) Run(int ticks, Action<World, EntityId, EntityId> script,
        Action<WorldBuilder>? more = null)
    {
        var b = new WorldBuilder(1);
        b.Event<AttackBegun>().Event<BlockRaised>().Event<BlockLowered>().Event<ContactMade>().Event<HarmResolved>().Event<GuardResolved>()
         .Event<SwingEnded>().Event<FlurryStrike>().Event<ActorDied>();
        more?.Invoke(b);
        EntityId a = EntityId.None, t = EntityId.None;
        b.System(Phase.Contact, 1, "test.stand-in", w => script(w, a, t));
        var world = b.Build();
        a = world.Create();
        t = world.Create();
        var names = new Dictionary<EntityId, string> { [a] = "A", [t] = "T" };
        var log = new ScenarioEventLog(world, names);
        var result = new BoutResult { Point = 0, Values = [], Bout = 0, Seed = 1, Fighters = [new FighterTally(), new FighterTally()] };
        var tally = new ScenarioTally(world, [a, t], ["A", "T"], result, log, new ScenarioMeasuresModel([], [], new ScenarioStop(), new ScenarioOutputs()));
        var receipts = new ScenarioReceipts(world, [a, t], ["A", "T"], log);
        for (int i = 0; i < ticks; i++) { world.Step(); log.Collect(); receipts.Collect(); tally.Collect(); }
        return (world, log, tally, a, t, receipts);
    }

    internal static ContactMade Contact(long id, EntityId by, int swing, EntityId on, ContactMet met, long tick, int shot = 0, GuardMeet guard = GuardMeet.None, string? site = null, bool fixedContact = false) =>
        new(id, by, swing, on, met, site ?? (met == ContactMet.Body ? "chest" : null), ContactMaterial.Flesh, tick, new Vector3(1, 2, 1.3f), Vector3.UnitX, 8f, 9.6f,
            false, met == ContactMet.Shield, true, false, Guard: guard, Shot: shot, Fixed: fixedContact);

    internal static HarmResolved Harm(long contact, EntityId by, EntityId on, long tick, int wounds, int flurry = 0) =>
        new(contact, by, on, tick, HarmKind.Lethal, 6, 3, 0, 2, 1, wounds, 100, 100 - wounds * 10, Flurry: flurry);

    [Fact]
    public void An_independent_arrow_that_wounds_after_two_were_stopped_at_the_shield_breaks_no_check()
    {
        // (seed 73000, example 11's kind): arrows 1 and 2 met the shield; an independent arrow 3 met the chest and wounded.
        var (_, _, tally, _, _, _) = Run(40, (w, a, t) =>
        {
            if (w.Tick == 10) w.Events<ContactMade>().Publish(Contact(1, a, 0, t, ContactMet.Shield, w.Tick, shot: 1));
            if (w.Tick == 20) w.Events<ContactMade>().Publish(Contact(2, a, 0, t, ContactMet.Shield, w.Tick, shot: 2));
            if (w.Tick == 30)
            {
                w.Events<ContactMade>().Publish(Contact(3, a, 0, t, ContactMet.Body, w.Tick, shot: 3));
                w.Events<HarmResolved>().Publish(Harm(3, a, t, w.Tick, 2));
            }
        });
        var check = tally.Check(BuiltInChecks.BlockedBodyNoWound);
        Assert.Equal((1.0, 0.0, 2.0), (check.Value, check.Numerator, check.Denominator));   // held; two blocked arrows looked at, none wounded through
    }

    [Fact]
    public void The_same_action_stopped_then_wounding_still_breaks_the_check_for_a_shot_a_swing_and_a_flurry_strike()
    {
        // The same arrow (shot 1) stopped at the shield, then a body contact of that shot that wounds: a violation.
        var (_, _, shot, _, _, _) = Run(30, (w, a, t) =>
        {
            if (w.Tick == 10) w.Events<ContactMade>().Publish(Contact(1, a, 0, t, ContactMet.Shield, w.Tick, shot: 1));
            if (w.Tick == 20)
            {
                w.Events<ContactMade>().Publish(Contact(2, a, 0, t, ContactMet.Body, w.Tick, shot: 1));
                w.Events<HarmResolved>().Publish(Harm(2, a, t, w.Tick, 1));
            }
        });
        Assert.Equal((0.0, 1.0), (shot.Check(BuiltInChecks.BlockedBodyNoWound).Value, shot.Check(BuiltInChecks.BlockedBodyNoWound).Numerator));
        // A blow held at a guard (swing 1), then a later contact of swing 1 that wounds: a violation; swing 2's wound is not.
        var (_, _, swing, _, _, _) = Run(40, (w, a, t) =>
        {
            if (w.Tick == 10)
            {
                w.Events<ContactMade>().Publish(Contact(1, a, 1, t, ContactMet.Weapon, w.Tick, guard: GuardMeet.Blade));
                w.Events<GuardResolved>().Publish(new GuardResolved(1, a, t, w.Tick, ContactMet.Weapon, GuardMeet.Blade, GuardVerdict.Held, 1, 1, 1, 0, 0, 0.5, 0.5, 1, 0, 0, 0));
            }
            if (w.Tick == 20) { w.Events<ContactMade>().Publish(Contact(2, a, 1, t, ContactMet.Body, w.Tick)); w.Events<HarmResolved>().Publish(Harm(2, a, t, w.Tick, 1)); }
            if (w.Tick == 30) { w.Events<ContactMade>().Publish(Contact(3, a, 2, t, ContactMet.Body, w.Tick)); w.Events<HarmResolved>().Publish(Harm(3, a, t, w.Tick, 1)); }
        });
        Assert.Equal((0.0, 1.0, 1.0), (swing.Check(BuiltInChecks.BlockedBodyNoWound).Value, swing.Check(BuiltInChecks.BlockedBodyNoWound).Numerator, swing.Check(BuiltInChecks.BlockedBodyNoWound).Denominator));
        // A flurry strike belongs to its swing: swing 1 landed (contact 1), its chain then met a held guard (contact 2); an extra blow of
        // swing 1 that wounds after that is a violation; one before it is not.
        var (_, _, flurry, _, _, _) = Run(40, (w, a, t) =>
        {
            if (w.Tick == 10) { w.Events<ContactMade>().Publish(Contact(1, a, 1, t, ContactMet.Body, w.Tick)); w.Events<HarmResolved>().Publish(Harm(1, a, t, w.Tick, 1)); }
            if (w.Tick == 12) w.Events<HarmResolved>().Publish(Harm(1, a, t, w.Tick, 1, flurry: 1));
            if (w.Tick == 15)
            {
                w.Events<ContactMade>().Publish(Contact(2, a, 1, t, ContactMet.Weapon, w.Tick, guard: GuardMeet.Blade));
                w.Events<GuardResolved>().Publish(new GuardResolved(2, a, t, w.Tick, ContactMet.Weapon, GuardMeet.Blade, GuardVerdict.Held, 1, 1, 1, 0, 0, 0.5, 0.5, 1, 0, 0, 0));
            }
            if (w.Tick == 20) w.Events<HarmResolved>().Publish(Harm(1, a, t, w.Tick, 1, flurry: 2));
        });
        Assert.Equal((0.0, 1.0), (flurry.Check(BuiltInChecks.BlockedBodyNoWound).Value, flurry.Check(BuiltInChecks.BlockedBodyNoWound).Numerator));
    }

    [Fact]
    public void A_check_with_no_case_in_any_run_is_not_applicable_and_fails_nothing()
    {
        var spec = new AssertionSpec("no wound through a guard", new CheckForm(BuiltInChecks.BlockedBodyNoWound), new Comparison(CompareOp.IsTrue), AssertionScope.EachRun);
        var none = new RunValue(1, 0, 0);
        var (v, res) = ScenarioVerdicts.AtPoint(spec, MeasureKind.Check, [none, none, none], [1UL, 2, 3], false);
        Assert.Equal((Verdict.NotApplicable, "notApplicable", 0, 3), (v, res.Result, res.N, res.Runs));
        Assert.Contains("0 cases", res.Note);
        // One run with a case: judged as before (a pass here).
        var (v2, _) = ScenarioVerdicts.AtPoint(spec, MeasureKind.Check, [none, new RunValue(1, 0, 2), none], [1UL, 2, 3], false);
        Assert.Equal(Verdict.Pass, v2);
    }

    static AttackBegun Begun(EntityId a, long tick, int swing) => new(a, tick, swing, AttackStrength.Quick, Animation.ActionRole.AttackQuick, 0, 0f);

    [Fact]
    public void Each_defence_is_told_apart_and_a_blade_stopped_at_a_guard_is_never_a_miss()
    {
        var (_, log, _, a, t, receipts) = Run(130, (w, a, t) =>
        {
            void C(long id, int swing, ContactMet met, GuardMeet g) => w.Events<ContactMade>().Publish(Contact(id, a, swing, t, met, w.Tick, guard: g));
            switch (w.Tick)
            {
                case 5: w.Events<BlockRaised>().Publish(new BlockRaised(t, w.Tick)); break;                       // up before the blow
                case 10: w.Events<AttackBegun>().Publish(Begun(a, w.Tick, 1)); break;
                case 20: C(1, 1, ContactMet.Weapon, GuardMeet.Blade); break;                                         // guardHeld
                case 22: w.Events<SwingEnded>().Publish(new SwingEnded(a, 1, w.Tick, 1, 0, true)); break;
                case 25: w.Events<BlockLowered>().Publish(new BlockLowered(t, w.Tick, 0.2f)); break;
                case 30: w.Events<AttackBegun>().Publish(Begun(a, w.Tick, 2)); break;
                case 33: w.Events<BlockRaised>().Publish(new BlockRaised(t, w.Tick)); break;                       // raised against it
                case 40: C(2, 2, ContactMet.Shield, GuardMeet.Shield); break;                                        // reactiveBlock
                case 41: w.Events<BlockLowered>().Publish(new BlockLowered(t, w.Tick, 0.1f)); break;
                case 45: w.Events<AttackBegun>().Publish(Begun(a, w.Tick, 3)); break;
                case 50: C(3, 3, ContactMet.Weapon, GuardMeet.Parry); break;                                         // timedParry
                case 55: w.Events<AttackBegun>().Publish(Begun(a, w.Tick, 4)); break;
                case 60: C(4, 4, ContactMet.Shield, GuardMeet.Incidental); break;                                    // shieldInterception
                case 65: w.Events<AttackBegun>().Publish(Begun(a, w.Tick, 5)); break;
                case 70: C(5, 5, ContactMet.Weapon, GuardMeet.Clash); break;                                         // clash
                case 75: w.Events<AttackBegun>().Publish(Begun(a, w.Tick, 6)); break;
                case 76: w.Events<Bodies.DodgeBegun>().Publish(new Bodies.DodgeBegun(t, w.Tick, Vector2.UnitX, Vector3.Zero, 0.1f, 0.2f, 0.1f, 1f, 5f, false)); break;
                case 85: w.Events<SwingEnded>().Publish(new SwingEnded(a, 6, w.Tick, 0, 0, false)); break;          // met nothing, dodged through
                case 90: w.Events<Bodies.DodgeEnded>().Publish(new Bodies.DodgeEnded(t, w.Tick, Vector3.UnitX, 1.1f, 1f, false)); break;
                case 95: w.Events<AttackBegun>().Publish(Begun(a, w.Tick, 7)); break;
                case 105: w.Events<SwingEnded>().Publish(new SwingEnded(a, 7, w.Tick, 0, 0, false)); break;         // met nothing, no dodge: missed
                case 110: w.Events<AttackBegun>().Publish(Begun(a, w.Tick, 8)); break;
                case 112: C(8, 8, ContactMet.Body, GuardMeet.None); break;                                           // none: a body
                case 115: w.Events<SwingEnded>().Publish(new SwingEnded(a, 8, w.Tick, 1, 0, false)); break;
            }
        }, b => b.Event<Bodies.DodgeBegun>().Event<Bodies.DodgeEnded>());
        string? D(long contact) => log.Events.First(e => e.Raw is ContactMade c && c.ContactId == contact).Field("defence") as string;
        Assert.Equal(["guardHeld", "reactiveBlock", "timedParry", "shieldInterception", "clash", "none"], new long[] { 1, 2, 3, 4, 5, 8 }.Select(D));
        var ends = log.Events.Where(e => e.Kind == "SwingEnded").ToList();
        Assert.Equal(["stopped", "dodged", "missed", "landed"], ends.Select(e => e.Field("outcome") as string));
        Assert.Equal(("dodge", "T"), (ends[1].Field("defence") as string, ends[1].Field("defender") as string));
        Assert.Equal(1.1, (double)ends[1].Field("dodgeMovedMetres")!, 3);
        Assert.Contains("Inferred from the motion", ends[1].Field("defenceEvidence") as string);
        Assert.Equal("none", ends[2].Field("defence"));
        var r = receipts.Finish("p000-r000", 1);
        Assert.Equal(new DefenceCounts(1, 1, 1, 1, 1, 0, 1), r.Actors[1].Defences);
        Assert.Equal(new BlowOutcomes(4, 1, 1, 1, 1, 0), r.Actors[0].Blows);   // the stopped blade is not a miss
        Assert.Null(r.Actors[0].Decisions);   // no decisions asked for
    }

    [Fact]
    public void A_brain_s_decision_is_the_parent_of_what_it_caused_and_one_that_came_to_nothing_says_so()
    {
        var (_, log, _, a, t, receipts) = Run(60, (w, a, t) =>
        {
            if (!BrainDecisions.On(w)) BrainDecisions.Enable(w);
            var q = w.Events<BrainDecided>();
            BrainDecided Decide(BrainChoice c, int foeSwing = 0) => new(a, w.Tick, c, BrainRule.FoeBlowInReach, t, 1.2f, 1.6f, 1.6f, foeSwing, BrainFoePhase.WindUp,
                0, false, false, 0f, 0f, Draw: 0.1f, Threshold: 0.5f, FormerTarget: EntityId.None);
            switch (w.Tick)
            {
                case 8: w.Events<AttackBegun>().Publish(Begun(t, w.Tick, 1)); break;     // the foe's blow
                case 10: q.Publish(Decide(BrainChoice.Block, foeSwing: 1)); break;
                case 30: w.Events<BlockRaised>().Publish(new BlockRaised(a, w.Tick)); break;
                case 40: q.Publish(Decide(BrainChoice.Attack)); break;                // muted: nothing begins
                case 50: q.Publish(Decide(BrainChoice.Attack)); w.Events<AttackBegun>().Publish(Begun(a, w.Tick, 1)); break;
            }
        });
        var decided = log.Events.Where(e => e.Kind == "BrainDecided").ToList();
        Assert.Equal(3, decided.Count);
        var block = log.Events.Single(e => e.Kind == "BlockRaised");
        Assert.Equal(decided[0].Id, block.Parent);
        Assert.Equal(((double)block.Id, true), ((double)decided[0].Field("caused")!, (bool)decided[0].Field("executed")!));
        Assert.Equal(log.Events.First(e => e.Kind == "AttackBegun" && e.By == "T").Id, decided[0].Parent);   // the foe's blow it answered
        Assert.Equal(false, decided[1].Field("executed"));                                                 // chosen, never executed
        var begun = log.Events.Single(e => e.Kind == "AttackBegun" && e.By == "A");
        Assert.Equal(decided[2].Id, begun.Parent);
        Assert.Equal(true, decided[2].Field("executed"));
        var r = receipts.Finish("p000-r000", 1);
        Assert.Equal((3, 1), (r.Actors[0].Decisions, r.Actors[0].DecisionsNotExecuted));
    }

    [Fact]
    public void Health_s_books_keep_the_purchased_and_effective_maximum_the_wounds_by_region_and_a_stop_that_is_no_death()
    {
        var (_, log, _, a, t, receipts) = Run(40, (w, a, t) =>
        {
            switch (w.Tick)
            {
                case 0: w.Set(t, new Vitals { Health = 100, HealthMax = 100, Blood = 10, BloodMax = 10, HasBlood = true }); break;
                case 20:
                    w.Events<ContactMade>().Publish(Contact(1, a, 1, t, ContactMet.Body, w.Tick, site: "left forearm"));
                    w.Events<HarmResolved>().Publish(Harm(1, a, t, w.Tick, 3) with { Severed = BodyRegion.LeftHand });
                    w.Events<PartSevered>().Publish(new PartSevered(t, a, BodyRegion.LeftHand, w.Tick, 1, 5, 40, 30, 95, 100));
                    w.Set(t, new Vitals { Health = 55, HealthMax = 100, HealthCap = 95, Blood = 10, BloodMax = 10, HasBlood = true, Band = HealthBand.None });
                    break;
                case 30:
                    w.Events<ActorDied>().Publish(new ActorDied(t, w.Tick, 1, a));
                    w.Set(t, new Vitals { Health = 0, HealthMax = 100, HealthCap = 95, Blood = 10, BloodMax = 10, HasBlood = true, Band = HealthBand.Stopped });
                    break;
            }
        }, b => { PowerContract.Register(b); b.Event<PartSevered>(); });
        var harm = log.Events.Single(e => e.Kind == "HarmResolved");
        Assert.Equal(("forearm", "left", 100.0, 95.0), (harm.Field("harmRegion"), harm.Field("harmSide"), harm.Field("healthMaxPurchased"), harm.Field("healthMaxEffective")));
        var died = log.Events.Single(e => e.Kind == "ActorDied");
        Assert.Equal(("ZERO_HEALTH_STOP", false), (died.Field("harnessStop"), died.Field("finalDeath")));
        var r = receipts.Finish("p000-r000", 1).Actors[1];
        Assert.Equal((100.0, 100.0, 100.0), (r.Start.PurchasedMax, r.Start.EffectiveMax, r.Start.Current));
        Assert.Equal((100.0, 95.0, 0.0, "Stopped"), (r.End.PurchasedMax, r.End.EffectiveMax, r.End.Current, r.End.Band));
        Assert.Equal(3, r.End.WoundsByRegion["forearm left"]);
        Assert.Equal(["left hand"], r.End.Severed);
        Assert.Equal(("ZERO_HEALTH_STOP", false), (r.End.HarnessStop, r.End.FinalDeath));
    }

    [Fact]
    public void Celerity_s_ledger_counts_each_landed_swing_s_extra_blows_used_and_unused_and_a_denial_with_its_reason()
    {
        var (_, log, _, a, t, receipts) = Run(60, (w, a, t) =>
        {
            var c1 = Contact(1, a, 1, t, ContactMet.Body, w.Tick);
            switch (w.Tick)
            {
                case 10: w.Events<PowerUsed>().Publish(new PowerUsed(a, "celerity.active", 1, 1, w.Tick, 7, 30, 100, 70, 15f, false, EndTick: w.Tick + 1800)); break;
                case 20: w.Events<AttackBegun>().Publish(Begun(a, w.Tick, 1)); break;
                case 30:
                    w.Events<ContactMade>().Publish(c1);
                    w.Events<HarmResolved>().Publish(Harm(1, a, t, w.Tick, 1) with { FlurryBlows = 2, Strike = FlurryBlows.Id(1, 0) });
                    break;
                case 35:
                    w.Events<FlurryStrike>().Publish(new FlurryStrike(FlurryBlows.Id(1, 1), 1, a, 1, t, 1, 2, w.Tick, FlurryCheck.Met, ContactMet.Body, GuardMeet.None, BodyRegion.Chest, Vector3.Zero, 0f, c1));
                    w.Events<HarmResolved>().Publish(Harm(1, a, t, w.Tick, 1, flurry: 1) with { FlurryBlows = 2, Strike = FlurryBlows.Id(1, 1) });
                    break;
                case 40:
                    w.Events<FlurryStrike>().Publish(new FlurryStrike(FlurryBlows.Id(1, 2), 1, a, 1, t, 2, 2, w.Tick, FlurryCheck.OutOfReach, ContactMet.Body, GuardMeet.None, BodyRegion.None, Vector3.Zero, 0.4f, c1));
                    break;
                case 50: w.Events<PowerRefused>().Publish(new PowerRefused(a, "celerity.active", 1, PowerDenial.NoBlood, w.Tick)); break;
            }
        }, b => PowerContract.Register(b));
        var strikes = log.Events.Where(e => e.Kind == "FlurryStrike").ToList();
        Assert.All(strikes, s => Assert.Equal((1.0, true), ((double)s.Field("parentContact")!, (bool)s.Field("recheckedAtOwnMoment")!)));
        var extraHarm = log.Events.Single(e => e.Raw is HarmResolved h && h.Flurry == 1);
        Assert.Equal((double)strikes[0].Id, extraHarm.Field("strikeEventId"));   // the extra blow's harm names its strike, not a second swing
        Assert.Equal(log.Events.First(e => e.Kind == "ContactMade").Id, extraHarm.Parent);   // and follows from the one contact it re-met
        var r = receipts.Finish("p000-r000", 1);
        var acts = r.Actors[0].Activations;
        Assert.Equal(["paid", "denied"], acts.Select(x => x.Result));
        var paid = acts[0];
        Assert.Equal(("celerity", "activated", 1, 2, 1, 1, "OutOfReach:1", 1, 1), (paid.Power, paid.Source, paid.SwingsLive, paid.RightsGranted, paid.RightsUsed, paid.RightsUnused, paid.UnusedWhy, paid.PhysicalContacts, paid.StrikesWounding));
        Assert.Equal([(FlurryBlows.Id(1, 1), 1L, true, 1), (FlurryBlows.Id(1, 2), 1L, false, 0)], paid.Rights!.Select(g => (g.Strike, g.ParentContact, g.Used, g.Wounds)));
        // Each extra blow its own row: the met one a hit on the chest, the out-of-reach one never a hit; both swept at their own moment, under the activation.
        Assert.Equal([("Met", "chest", true, 7L), ("OutOfReach", null, false, 7L)], paid.Rights!.Select(g => (g.Check, g.Region, g.Hit, g.ActionId ?? 0)));
        Assert.All(paid.Rights!, g => Assert.Equal(ExtraBlowResolution.Sweep, g.Resolution));
        Assert.Equal(2, r.ExtraBlows!.Count);
        Assert.Equal(("landed", "missed"), (strikes[0].Field("outcome") as string, strikes[1].Field("outcome") as string));
        Assert.StartsWith("NoBlood", acts[1].Why);
        Assert.Contains(r.Actors[0].Effects, e => e.Source == "activated" && e.ActionId == 7);
    }

    /// <summary>
    /// Examples 13 to 15 (the receipts' fixtures, a positive and a negative point each): they check, run three runs a point and pass;
    /// <c>--describe events</c> lists <c>BrainDecided</c> and the receipts' derived fields; the receipts are in the tables and results.json.
    /// </summary>
    [Theory]
    [InlineData("13-receipts-defences-and-decisions.scenario.json")]
    [InlineData("14-receipts-celerity-rights.scenario.json")]
    [InlineData("15-receipts-health-books.scenario.json")]
    public void The_receipts_examples_check_run_and_pass(string file)
    {
        var course = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        var game = GameInstall.Locate();
        Assert.SkipUnless(course is not null && File.Exists(Path.Combine(course, "level.json")), "no proving ground (VITOMIR_COURSE_LEVEL)");
        Assert.SkipUnless(game is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        Environment.SetEnvironmentVariable("VITOMIR_USERDATA", Path.Combine(Path.GetTempPath(), "vitomir-receipts-tests-userdata"));
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Vitomir.slnx"))) dir = dir.Parent;
        Assert.SkipUnless(dir is not null, "not in the repository");
        string path = Path.Combine(dir!.FullName, "tools", "platform", "scenarios", "examples", file);
        var baseline = new SessionOptions { Level = course, Game = game, Switches = new HashSet<string>(StringComparer.Ordinal) { "sandbox" } };
        using var scenarioGame = ScenarioGame.Load(baseline, course!);
        var load = ScenarioSpec.Load(path, scenarioGame, course!);
        Assert.True(load.Ok, string.Join("\n", load.Errors.Select(e => e.Line())));
        var plan = ScenarioPlan.From(load.Scenario!);
        string out_ = Path.Combine(Path.GetTempPath(), $"vitomir-ck-receipts-{Path.GetFileNameWithoutExtension(file)}-{Environment.ProcessId}");
        if (Directory.Exists(out_)) Directory.Delete(out_, recursive: true);
        var (results, exit) = ScenarioRunner.Execute(plan, baseline, out_, _ => { }, new ScenarioRunOptions { Runs = 3 });
        Assert.True(exit == 0, $"exit {exit}: " + string.Join("; ", results.Assertions.Select(a => $"{a.Name} {a.Result}")));
        Assert.NotNull(results.Receipts);
        Assert.True(File.Exists(Path.Combine(out_, "activations.csv")));
        Assert.Contains("def_reactive_block", File.ReadLines(Path.Combine(out_, "actors.csv")).First());
        var events = ScenarioWords.Events();
        Assert.Contains(events, e => e.Name == "BrainDecided" && e.Derived.Contains("executed"));
        Assert.Contains(events, e => e.Name == "SwingEnded" && e.Derived.Contains("defence"));
        Directory.Delete(out_, recursive: true);
    }
}

