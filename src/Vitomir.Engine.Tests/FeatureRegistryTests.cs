// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Features;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The feature registry's rules (ours), pinned with **invented features** — a door, a lamp and
/// a lock — so that every rule is exercised whether or not one of the game's thirty-two declarations happens to
/// meet it (only three do: `FeatureDeclarationTests`). What is pinned: two features on one switch point conflict
/// unless one refines the other; a feature that changes what another reads is blocked unless either tolerates the
/// other; what a feature requires must be on and what it excludes off; a repair counts without being named; a set
/// is settled in the registry's order and settling twice changes nothing; the rules-or-presentation axis, the
/// profile and the names a save writes are computed; a mod that does not tolerate a rules feature is refused by
/// name; and a load's differences, with a new-game-only feature forced.
/// </summary>
public class FeatureRegistryTests
{
    static readonly SwitchPoint DoorOpens = new("Door.Opens", "how a door opens", Rules: true);
    static readonly SwitchPoint DoorLatch = new("Door.Latch", "whether a shut door latches", Rules: true);
    static readonly SwitchPoint LampColour = new("Lamp.Colour", "the colour a lamp is drawn in", Rules: false);
    static readonly SwitchPoint SaveOften = new("Save.Often", "how often the game may be saved", Rules: true);
    static readonly Datum DoorLock = new("door.lock", "whether a door is locked", RulesRead: true);
    static readonly Datum LampTint = new("lamp.tint", "a lamp's drawn colour", RulesRead: false);

    static readonly Feature OiledHinge = new(FeatureKind.Fix, "oiledhinge", "a door opens without a creak") { Takes = [DoorOpens] };
    static readonly Feature SelfClosing = new(FeatureKind.Fix, "selfclosing", "a door shuts behind you") { Takes = [DoorOpens] };
    static readonly Feature SoftClose = new(FeatureKind.Fix, "softclose", "and shuts quietly") { Takes = [DoorOpens], Refines = "fixes.selfclosing" };
    static readonly Feature WarmLamp = new(FeatureKind.Fix, "warmlamp", "lamps are amber") { Takes = [LampColour], Changes = [LampTint], Whose = FeatureWhose.Machine };
    static readonly Feature PickLock = new(FeatureKind.Fix, "picklock", "a lock can be picked") { Changes = [DoorLock] };
    static readonly Feature LockGauge = new(FeatureKind.Fix, "lockgauge", "a lock shows how hard it is") { Reads = [DoorLock] };
    static readonly Feature LockPatch = new(FeatureKind.Fix, "lockpatch", "a lock's difficulty is clamped") { Reads = [DoorLock], Tolerates = ["fixes.picklock"] };
    static readonly Feature Doorbell = new(FeatureKind.Qol, "doorbell", "a door rings when opened") { Default = true, Requires = ["fixes.oiledhinge"] };
    static readonly Feature Silence = new(FeatureKind.Qol, "silence", "nothing rings") { Excludes = ["qol.doorbell"] };
    static readonly Feature Latch = new(FeatureKind.Repair, "latch", "a shut door stays shut") { Takes = [DoorLatch] };
    static readonly Feature Ironman = new(FeatureKind.RulesetFlag, "ironman", "one save, written on quitting") { Takes = [SaveOften], When = FeatureWhen.NewGame };

    static readonly FeatureRegistry Registry = new(
        [OiledHinge, SelfClosing, SoftClose, WarmLamp, PickLock, LockGauge, LockPatch, Doorbell, Silence, Latch, Ironman]);

    [Fact]
    public void Two_features_on_one_switch_point_conflict_and_the_reason_names_both_and_what_is_decided_there()
    {
        Assert.Null(Registry.Blocks(SelfClosing, []));
        var why = Registry.Blocks(SelfClosing, [OiledHinge]);
        Assert.NotNull(why);
        Assert.Equal(FeatureRule.SwitchPoint, why!.Rule);
        Assert.Same(SelfClosing, why.Feature);
        Assert.Same(OiledHinge, why.Other);
        Assert.Equal("Door.Opens", why.Between);
        Assert.Equal("selfclosing and oiledhinge both decide how a door opens: only one can be on", why.Sentence);
        // And the other way about: the rule has no first comer.
        Assert.Equal(FeatureRule.SwitchPoint, Registry.Blocks(OiledHinge, [SelfClosing])!.Rule);
    }

    [Fact]
    public void A_feature_that_refines_another_needs_it_on_and_may_then_share_its_switch_point()
    {
        var alone = Registry.Blocks(SoftClose, []);
        Assert.Equal(FeatureRule.Refines, alone!.Rule);
        Assert.Same(SelfClosing, alone.Other);
        Assert.Equal("softclose needs selfclosing: it refines it", alone.Sentence);

        Assert.Null(Registry.Blocks(SoftClose, [SelfClosing]));
        Assert.Empty(Registry.Check([SelfClosing, SoftClose]));
        // Refining one feature is no leave to share the point with a third.
        Assert.Contains(Registry.Check([OiledHinge, SelfClosing, SoftClose]), c => c.Feature == SoftClose && c.Other == OiledHinge && c.Rule == FeatureRule.SwitchPoint);
    }

    [Fact]
    public void A_feature_that_changes_what_another_reads_is_blocked_unless_either_tolerates_the_other()
    {
        var changer = Registry.Blocks(PickLock, [LockGauge]);
        Assert.Equal(FeatureRule.Data, changer!.Rule);
        Assert.Equal("door.lock", changer.Between);
        Assert.Equal("picklock changes whether a door is locked, which lockgauge reads", changer.Sentence);
        // Switching the reader on beside the changer is refused for the same reason, said the same way round.
        var reader = Registry.Blocks(LockGauge, [PickLock]);
        Assert.Equal(FeatureRule.Data, reader!.Rule);
        Assert.Equal("picklock changes whether a door is locked, which lockgauge reads", reader.Sentence);

        // The reader that says it tolerates the changer stands beside it, whichever is switched on second.
        Assert.Null(Registry.Blocks(LockPatch, [PickLock]));
        Assert.Null(Registry.Blocks(PickLock, [LockPatch]));
        // Two readers of one datum are no conflict: neither changes it.
        Assert.Null(Registry.Blocks(LockGauge, [LockPatch]));
    }

    [Fact]
    public void What_a_feature_requires_must_be_on_and_what_it_excludes_off()
    {
        var needs = Registry.Blocks(Doorbell, []);
        Assert.Equal(FeatureRule.Requires, needs!.Rule);
        Assert.Equal("doorbell needs oiledhinge", needs.Sentence);
        Assert.Null(Registry.Blocks(Doorbell, [OiledHinge]));

        var excluded = Registry.Blocks(Silence, [OiledHinge, Doorbell]);
        Assert.Equal(FeatureRule.Excludes, excluded!.Rule);
        Assert.Equal("silence cannot be on with doorbell", excluded.Sentence);
        // Said by one of the two, it holds for both.
        Assert.Equal(FeatureRule.Excludes, Registry.Blocks(Doorbell, [OiledHinge, Silence])!.Rule);
    }

    [Fact]
    public void A_repair_is_always_on_and_counts_without_being_named()
    {
        var rival = new Feature(FeatureKind.Fix, "looselatch", "a shut door swings") { Takes = [DoorLatch] };
        var registry = new FeatureRegistry([rival, Latch]);
        var why = registry.Blocks(rival, []);
        Assert.Equal(FeatureRule.SwitchPoint, why!.Rule);
        Assert.Same(Latch, why.Other);
        // Settling switches the switch off, never the repair.
        var said = new List<Conflict>();
        Assert.Empty(registry.Settle([rival], said.Add));
        Assert.Single(said);
        Assert.False(Latch.Switchable);
        Assert.True(Latch.Default);
        Assert.Equal("repairs", Latch.Register);
    }

    [Fact]
    public void Check_names_every_conflict_in_a_set_once()
    {
        Assert.Empty(Registry.Check([]));
        Assert.Empty(Registry.Check([OiledHinge, Doorbell, WarmLamp, PickLock, LockPatch]));

        var all = Registry.Check([OiledHinge, SelfClosing, PickLock, LockGauge, Silence, Doorbell]);
        Assert.Equal(3, all.Count);
        Assert.Contains(all, c => c.Rule == FeatureRule.SwitchPoint && c.Feature == SelfClosing && c.Other == OiledHinge);
        Assert.Contains(all, c => c.Rule == FeatureRule.Data && c.Feature == LockGauge && c.Other == PickLock);
        Assert.Contains(all, c => c.Rule == FeatureRule.Excludes && c.Feature == Silence && c.Other == Doorbell);
    }

    [Fact]
    public void Settling_keeps_the_earlier_of_two_that_conflict_takes_what_needed_the_loser_and_says_each()
    {
        var said = new List<Conflict>();
        var kept = Registry.Settle([SoftClose, SelfClosing, OiledHinge, Doorbell, Silence, WarmLamp], said.Add);
        // oiledhinge is first in the registry and stays; selfclosing shares its point and goes; softclose needed
        // selfclosing and goes with it; doorbell has what it needs; silence is excluded by doorbell.
        Assert.Equal([OiledHinge, WarmLamp, Doorbell], kept);
        Assert.Equal(3, said.Count);
        Assert.Contains(said, c => c.Feature == SelfClosing && c.Rule == FeatureRule.SwitchPoint);
        Assert.Contains(said, c => c.Feature == SoftClose && c.Rule is FeatureRule.Refines or FeatureRule.SwitchPoint);
        Assert.Contains(said, c => c.Feature == Silence && c.Rule == FeatureRule.Excludes);

        // Settled is settled: nothing more goes and nothing is said.
        var again = new List<Conflict>();
        Assert.Equal(kept, Registry.Settle(kept, again.Add));
        Assert.Empty(again);
        Assert.Empty(Registry.Check(kept));
    }

    [Fact]
    public void A_need_for_a_feature_later_in_the_registry_is_met_by_it()
    {
        // softclose (third) needs selfclosing (second); doorbell (eighth) needs oiledhinge (first): the order a set
        // is given in, and the order the registry holds the two in, do not matter to a need.
        var early = new Feature(FeatureKind.Fix, "early", "needs a later one") { Requires = ["fixes.late"] };
        var late = new Feature(FeatureKind.Fix, "late", "is needed");
        var registry = new FeatureRegistry([early, late]);
        Assert.Equal([early, late], registry.Settle([late, early]));
        // And when the later one cannot stay, the earlier one that needed it goes on the next pass.
        var said = new List<Conflict>();
        Assert.Empty(registry.Settle([early], said.Add));
        Assert.Equal("early needs late", Assert.Single(said).Sentence);
    }

    [Fact]
    public void One_registers_reading_is_settled_without_judging_a_need_for_another_registers_switch()
    {
        // The quality-of-life switches alone: doorbell needs a Fix, which this reading does not speak for.
        var said = new List<Conflict>();
        var qol = Registry.Settle([Doorbell, Silence], said.Add, speaksFor: f => f.Kind == FeatureKind.Qol);
        Assert.Equal([Doorbell], qol);
        Assert.Equal(FeatureRule.Excludes, Assert.Single(said).Rule);
        // When the registers meet, the need is judged.
        said.Clear();
        Assert.Empty(Registry.Settle([Doorbell], said.Add));
        Assert.Equal(FeatureRule.Requires, Assert.Single(said).Rule);
    }

    [Fact]
    public void A_feature_a_save_holds_is_not_switched_off_and_what_conflicts_with_it_goes()
    {
        var said = new List<Conflict>();
        var kept = Registry.Settle([OiledHinge, SelfClosing], said.Add, held: f => f == SelfClosing);
        Assert.Equal([SelfClosing], kept);
        Assert.Same(OiledHinge, Assert.Single(said).Feature);
    }

    [Fact]
    public void The_axis_is_computed_from_what_a_feature_takes_and_changes()
    {
        Assert.Equal(FeatureAxis.Rules, OiledHinge.Axis);                  // it takes a rules switch point
        Assert.Equal(FeatureAxis.Rules, PickLock.Axis);                    // it takes none, and changes what the rules read
        Assert.Equal(FeatureAxis.Presentation, WarmLamp.Axis);             // a presentation point, data no rule reads
        Assert.Equal(FeatureAxis.Presentation, LockGauge.Axis);            // reading is not changing
        Assert.Equal(FeatureAxis.Presentation, Doorbell.Axis);
        Assert.Null(typeof(Feature).GetProperty(nameof(Feature.Axis))!.SetMethod);   // nobody writes it by hand
    }

    [Fact]
    public void A_game_is_faithful_until_a_switchable_rules_feature_is_on_and_a_save_writes_their_names()
    {
        Assert.Equal("faithful", Registry.Profile([]));
        Assert.Equal("faithful", Registry.Profile([WarmLamp, LockGauge, Latch]));   // presentation, and a repair: every game has it
        Assert.Empty(Registry.RulesOn([WarmLamp, LockGauge, Latch]));

        Assert.Equal("modded", Registry.Profile([WarmLamp, PickLock]));
        // By register and name, in the registry's order whatever order they were given in.
        Assert.Equal(["fixes.oiledhinge", "fixes.picklock", "rules.ironman"], Registry.RulesOn([Ironman, PickLock, WarmLamp, OiledHinge]));
    }

    /// <summary>
    /// **A quality-of-life switch never makes a game modded, whatever it takes** (<i>"i think
    /// the QoL things should not count towards a modded save. Yes they change how the player can interact with the
    /// game, but it won't materially change things like combat or other outcomes."</i>). Its axis is computed as
    /// any feature's is — one that takes a rules switch point is on the rules axis, and the host's — and it is left
    /// out of the count by its kind: not named by a save, not a difference at a load, not asked of a mod.
    /// </summary>
    [Fact]
    public void A_quality_of_life_switch_does_not_count_whatever_its_axis()
    {
        var knocker = new Feature(FeatureKind.Qol, "knocker", "a key knocks at a door") { Default = true, Takes = [new SwitchPoint("Door.Knock", "how a door is knocked at", Rules: true)] };
        var registry = new FeatureRegistry([OiledHinge, WarmLamp, knocker, Latch]);
        Assert.Equal(FeatureAxis.Rules, knocker.Axis);
        Assert.Equal(FeatureWhose.Host, knocker.Whose);
        Assert.False(knocker.Counts);
        Assert.True(OiledHinge.Counts);
        Assert.False(WarmLamp.Counts);                                     // a Fix that is presentation
        Assert.False(Latch.Counts);                                        // a repair: every game has it
        Assert.True(Ironman.Counts);                                       // a rule chosen at a new game

        Assert.Equal("faithful", registry.Profile([knocker, WarmLamp, Latch]));
        Assert.Empty(registry.RulesOn([knocker]));
        Assert.Equal(["fixes.oiledhinge"], registry.RulesOn([knocker, OiledHinge]));

        // A save of a build that counted it names it: the name is passed over, on whichever side it is on.
        Assert.Empty(registry.Differences("modded", ["qol.knocker"], []));
        Assert.Empty(registry.Differences("modded", ["qol.knocker"], [knocker]));
        Assert.Empty(registry.Differences("faithful", [], [knocker]));
        Assert.Equal("faithful", registry.ProfileOf("modded", ["qol.knocker"]));
        Assert.Equal("modded", registry.ProfileOf("modded", ["qol.knocker", "fixes.oiledhinge"]));
        Assert.Equal("modded", registry.ProfileOf("modded", ["mods.dragons"]));
        Assert.Equal("faithful", registry.ProfileOf("faithful", []));
        Assert.Equal("", registry.ProfileOf("", []));
        // And no mod is asked about it.
        Assert.Empty(registry.Refusals([new ModDeclaration("Plain Mod", [])], ["qol.knocker"]));
        Assert.Single(registry.Refusals([new ModDeclaration("Plain Mod", [])], ["qol.knocker", "fixes.oiledhinge"]));
    }

    [Fact]
    public void A_feature_is_found_by_register_and_name_whatever_the_case()
    {
        Assert.Same(PickLock, Registry.Find("fixes", "PickLock"));
        Assert.Same(Doorbell, Registry.Find("qol.doorbell"));
        Assert.Null(Registry.Find("qol", "picklock"));
        Assert.Null(Registry.Find("fixes", "all"));
        Assert.Equal([Latch], Registry.AlwaysOn);
        Assert.Equal([DoorOpens, LampColour, DoorLatch, SaveOften], Registry.Points);
        Assert.Equal([LampTint, DoorLock], Registry.Data);
    }

    [Fact]
    public void A_mod_that_does_not_tolerate_a_rules_feature_in_force_is_refused_by_name()
    {
        var careful = new ModDeclaration("Careful Mod", ["fixes.picklock", "fixes.oiledhinge"]);
        var plain = new ModDeclaration("Plain Mod", []);
        var half = new ModDeclaration("Half Mod", ["fixes.oiledhinge"]);

        // With the faithful rules every mod loads; a presentation feature refuses none.
        Assert.Empty(Registry.Refusals([careful, plain, half], Registry.RulesOn([WarmLamp])));
        Assert.Empty(Registry.Refusals([plain], ["fixes.warmlamp"]));

        var refused = Registry.Refusals([careful, plain, half], Registry.RulesOn([OiledHinge, PickLock]));
        Assert.Equal(2, refused.Count);
        Assert.Same(plain, refused[0].Mod);
        Assert.Equal(["fixes.oiledhinge", "fixes.picklock"], refused[0].NotTolerated);
        Assert.Same(half, refused[1].Mod);
        Assert.Equal(["fixes.picklock"], refused[1].NotTolerated);
        Assert.Equal("the mod Half Mod does not work with fixes.picklock, which this game is played with", refused[1].Sentence);

        // A rules feature of some other build's that no mod here names refuses them all.
        Assert.Equal(3, Registry.Refusals([careful, plain, half], ["fixes.unheardof"]).Count);
    }

    [Fact]
    public void A_load_finds_no_difference_when_the_save_agrees_or_does_not_say()
    {
        Assert.Empty(Registry.Differences("", [], [OiledHinge, PickLock]));          // a save from before: it does not say
        Assert.Empty(Registry.Differences(null, [], []));
        Assert.Empty(Registry.Differences("modded", ["fixes.oiledhinge"], [OiledHinge, WarmLamp]));
        Assert.Empty(Registry.Differences("faithful", [], [WarmLamp, LockGauge]));    // presentation is not the save's business
    }

    [Fact]
    public void A_load_names_each_rules_feature_the_save_and_the_run_differ_on()
    {
        var found = Registry.Differences("modded", ["fixes.picklock"], [OiledHinge]);
        Assert.Equal(2, found.Count);
        Assert.Equal(("fixes.oiledhinge", false, true, false), (found[0].Id, found[0].InSave, found[0].InForce, found[0].Forced));
        Assert.Equal("the save was played with oiledhinge off; it is on here", found[0].Sentence);
        Assert.Equal(("fixes.picklock", true, false, false), (found[1].Id, found[1].InSave, found[1].InForce, found[1].Forced));
        Assert.Equal("the save was played with picklock on; it is off here", found[1].Sentence);
        Assert.Same(PickLock, found[1].Feature);

        // A faithful save loaded into a modded run says so too.
        Assert.Single(Registry.Differences("faithful", [], [PickLock]));
    }

    [Fact]
    public void A_feature_chosen_at_a_new_game_only_is_forced_as_the_save_has_it()
    {
        var on = Assert.Single(Registry.Differences("modded", ["rules.ironman"], []));
        Assert.True(on.Forced);
        Assert.True(on.InSave);
        Assert.Equal("the save was played with ironman on, and it is chosen at a new game only: it is on for this game", on.Sentence);

        var off = Assert.Single(Registry.Differences("faithful", [], [Ironman]));
        Assert.True(off.Forced);
        Assert.False(off.InSave);
        Assert.Equal("the save was played with ironman off, and it is chosen at a new game only: it is off for this game", off.Sentence);
    }

    [Fact]
    public void A_feature_the_save_names_that_this_build_does_not_know_is_said_by_name()
    {
        var unknown = Assert.Single(Registry.Differences("modded", ["fixes.oiledhinge", "mods.dragons"], [OiledHinge]));
        Assert.Null(unknown.Feature);
        Assert.Equal("mods.dragons", unknown.Id);
        Assert.False(unknown.Forced);
        Assert.Equal("the save was played with mods.dragons on, which this build does not have", unknown.Sentence);
    }

    [Fact]
    public void A_declaration_that_cannot_be_right_is_refused_by_name_when_the_registry_is_made()
    {
        var twice = Assert.Throws<ArgumentException>(() => new FeatureRegistry([OiledHinge, new Feature(FeatureKind.Fix, "OiledHinge", "again")]));
        Assert.Contains("fixes.OiledHinge is declared twice", twice.Message);

        var stranger = Assert.Throws<ArgumentException>(() => new FeatureRegistry([new Feature(FeatureKind.Fix, "a", "") { Requires = ["fixes.nobody"] }]));
        Assert.Contains("fixes.a names fixes.nobody, which is not declared", stranger.Message);

        // Refining is sharing a switch point; with none shared it would only be a requirement under another name.
        var apart = Assert.Throws<ArgumentException>(() => new FeatureRegistry([OiledHinge, new Feature(FeatureKind.Fix, "b", "") { Takes = [LampColour], Refines = "fixes.oiledhinge" }]));
        Assert.Contains("shares no switch point", apart.Message);

        var words = Assert.Throws<ArgumentException>(() => new FeatureRegistry([OiledHinge, new Feature(FeatureKind.Fix, "c", "") { Takes = [new SwitchPoint("Door.Opens", "something else", false)] }]));
        Assert.Contains("the switch point Door.Opens is declared more than once", words.Message);

        var both = Assert.Throws<ArgumentException>(() => new FeatureRegistry([Latch, new Feature(FeatureKind.Repair, "bolt", "") { Takes = [DoorLatch] }]));
        Assert.Contains("always on conflict", both.Message);
    }

    // ---- the switches being edited: what the Fixes page's boxes stand on ----

    [Fact]
    public void A_tick_the_registry_refuses_does_not_take_and_the_reason_comes_back()
    {
        var ticks = new FeatureTicks(Registry);
        Assert.True(ticks.Tick(OiledHinge, true).Taken);
        var pressed = ticks.Tick(SelfClosing, true);
        Assert.False(pressed.Taken);
        Assert.Equal("selfclosing and oiledhinge both decide how a door opens: only one can be on", pressed.Refused!.Sentence);
        Assert.False(ticks.On(SelfClosing));
        Assert.True(ticks.On(OiledHinge));
        Assert.True(ticks.On(Latch));                                     // a repair is on, and does not move
        Assert.False(ticks.Tick(Latch, false).Taken);
        Assert.True(ticks.On(Latch));
    }

    [Fact]
    public void A_refining_feature_is_locked_while_what_it_refines_is_off_and_goes_off_with_it()
    {
        var ticks = new FeatureTicks(Registry);
        Assert.Same(SelfClosing, ticks.LockedUnder(SoftClose));
        Assert.False(ticks.Tick(SoftClose, true).Taken);

        Assert.True(ticks.Tick(SelfClosing, true).Taken);
        Assert.Null(ticks.LockedUnder(SoftClose));
        Assert.True(ticks.Tick(SoftClose, true).Taken);

        var off = ticks.Tick(SelfClosing, false);
        Assert.True(off.Taken);
        Assert.Same(SoftClose, Assert.Single(off.AlsoOff).Feature);
        Assert.False(ticks.On(SoftClose));
        Assert.Same(SelfClosing, ticks.LockedUnder(SoftClose));
        Assert.Null(ticks.LockedUnder(OiledHinge));
    }

    [Fact]
    public void The_switches_being_edited_start_from_an_allowed_set()
    {
        var ticks = new FeatureTicks(Registry);
        ticks.Put([SoftClose, WarmLamp]);                                  // softclose with nothing to refine
        Assert.Equal([WarmLamp], ticks.AllOn);
    }
}
