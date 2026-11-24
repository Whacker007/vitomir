// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// Spending experience: the attribute price, the discipline price,
/// buying either, and the revert. The discipline and
/// clan rows are the shipped `game.ndd`, `game.ndg` and `game.ncd`, so every figure below is one a
/// player would see on the learn button.
/// </summary>
public class AdvancementTests
{
    static WorldThing Someone(string? clan, GameFiles? files, ThingType type = ThingType.Actor,
                              ActorType kind = ActorType.Vampire, int guid = 1)
    {
        var actor = new Actor { Type = kind };
        actor.Health = actor.MaxHealth = 100;
        if (clan is not null && files is not null) actor.ClanRecord = Clans.For(files)[clan];
        return new WorldThing
        {
            Index = guid - 1, Guid = guid, Scene = new NsdThing(), Template = "christof", Type = type, Actor = actor,
        };
    }

    // ---- attributes ------------------------------------------------------------------------------

    /// <summary>The rate climbs with the value: 4, 5, 6, 8, 10, 15 at 50, 60, 70, 80 and 90, truncated.</summary>
    [Theory]
    [InlineData(30f, 120)]     // Christof's Strength at the start
    [InlineData(49f, 196)]
    [InlineData(50f, 250)]
    [InlineData(59f, 295)]
    [InlineData(60f, 360)]
    [InlineData(70f, 560)]
    [InlineData(80f, 800)]
    [InlineData(89f, 890)]
    [InlineData(90f, 1350)]
    [InlineData(99f, 1485)]
    public void An_attribute_point_costs_its_value_times_a_rate_that_climbs(float have, int cost)
    {
        var who = Someone(null, null).Actor!;
        who.BaseStat[(int)ActorStat.Strength] = have;
        Assert.Equal(cost, Advancement.StatCost(who, (int)ActorStat.Strength));
    }

    [Fact]
    public void An_attribute_at_its_own_maximum_cannot_be_raised_and_no_maximum_means_none_at_all()
    {
        var who = Someone(null, null).Actor!;
        who.StatMax[0] = 100f;                                    // Christof's `.NPC` max column
        who.BaseStat[0] = 100f;
        Assert.Equal(-1, Advancement.StatCost(who, 0));
        who.BaseStat[0] = 40f;
        who.StatMax[0] = 40f;
        Assert.Equal(-1, Advancement.StatCost(who, 0));
        // The price compares the maximum as it stands: the "no limit" −1 is below any value.
        who.StatMax[0] = Actor.NoStatLimit;
        Assert.Equal(-1, Advancement.StatCost(who, 0));
    }

    /// <summary>
    /// The Nosferatu's weakness bars **Appearance only** (`game.ncd` weakness 0x4, the price tests stat
    /// 13) — their Charisma and Manipulation go up like anyone's — and the Cappadocians' cap of 25 on
    /// Appearance stops the button there.
    /// </summary>
    [Fact]
    public void Clans_stop_appearance_by_weakness_or_by_cap_and_nothing_else()
    {
        using var files = Install.Open();
        var nos = Someone("nosferatu", files).Actor!;
        nos.BaseStat[(int)ActorStat.Appearance] = 0f;
        nos.BaseStat[(int)ActorStat.Charisma] = 20f;
        Assert.Equal(-1, Advancement.StatCost(nos, (int)ActorStat.Appearance));
        Assert.Equal(80, Advancement.StatCost(nos, (int)ActorStat.Charisma));

        var capp = Someone("cappadocian", files).Actor!;
        capp.BaseStat[(int)ActorStat.Appearance] = 24f;
        Assert.Equal(96, Advancement.StatCost(capp, (int)ActorStat.Appearance));
        capp.BaseStat[(int)ActorStat.Appearance] = 25f;
        Assert.Equal(-1, Advancement.StatCost(capp, (int)ActorStat.Appearance));
    }

    [Fact]
    public void The_sheet_puts_raise_buttons_on_nine_attributes_and_on_faith_for_a_human()
    {
        var vampire = Someone(null, null).Actor!;
        var human = Someone(null, null, kind: ActorType.Human).Actor!;
        int[] nine = [0, 1, 2, 3, 4, 5, 11, 12, 13];
        foreach (int s in nine) Assert.True(Advancement.Raisable(vampire, s));
        Assert.False(Advancement.Raisable(vampire, (int)ActorStat.Faith));
        Assert.True(Advancement.Raisable(human, (int)ActorStat.Faith));
        Assert.False(Advancement.Raisable(human, (int)ActorStat.Blood));
        Assert.False(Advancement.Raisable(human, (int)ActorStat.Humanity));
    }

    [Fact]
    public void Buying_a_point_spends_its_price_and_refuses_what_cannot_be_afforded()
    {
        using var files = Install.Open();
        var thing = Someone(null, null);
        var me = thing.Actor!;
        me.SetBaseStat(0, 30f);
        me.Stat[0] = 30f;
        me.SpendableExperience = 200;
        me.Experience = 5000;
        var adv = new Advancement(thing, [thing], new DisciplineDefinitions(files));

        Assert.True(adv.BuyStat(0));
        Assert.Equal(80, me.SpendableExperience);
        Assert.Equal(31f, me.BaseStat[0]);
        Assert.Equal(31f, me.Stat[0]);
        Assert.Equal(5000, me.Experience);                       // lifetime experience is untouched
        Assert.False(adv.BuyStat(0));                            // 124 > 80
        Assert.Equal(80, me.SpendableExperience);
    }

    // ---- disciplines -----------------------------------------------------------------------------

    /// <summary>
    /// Celerity for a Brujah — one of their three: learning it pays its own 500 and the group's
    /// 1,000; then 500 × 1.4, 2, 4 and 8. **The 1.4 step is 700**, not 699: the multiply is single
    /// precision, as 1.1 computes it.
    /// </summary>
    [Fact]
    public void An_in_clan_discipline_costs_the_column_times_the_level_multiplier()
    {
        using var files = Install.Open();
        var powers = new DisciplineDefinitions(files);
        var thing = Someone("brujah", files);
        var celerity = thing.Actor!.Disciplines.Add(powers["Celerity"]!, level: -1);

        int[] expected = [1500, 700, 1000, 2000, 4000];
        for (int level = -1; level <= 3; level++)
        {
            celerity.Level = level;
            Assert.Equal(expected[level + 1], Advancement.DisciplineCost(thing, powers, celerity.Definition));
        }
        celerity.Level = 4;
        Assert.Equal(-1, Advancement.DisciplineCost(thing, powers, celerity.Definition));
    }

    [Fact]
    public void Out_of_clan_is_a_quarter_dearer_and_so_is_everything_for_the_caitiff_and_the_clanless()
    {
        using var files = Install.Open();
        var powers = new DisciplineDefinitions(files);
        foreach (var clan in new[] { "nosferatu", "caitiff", null })
        {
            var thing = Someone(clan, clan is null ? null : files);
            var celerity = thing.Actor!.Disciplines.Add(powers["Celerity"]!, level: -1);
            Assert.Equal(1875, Advancement.DisciplineCost(thing, powers, celerity.Definition));
            celerity.Level = 0;
            Assert.Equal(875, Advancement.DisciplineCost(thing, powers, celerity.Definition));
        }
    }

    /// <summary>
    /// The group's price is paid on its **first** discipline only; the common disciplines' group costs
    /// nothing, and belongs to no clan — so Feed is 750 × 1.25 and Blood Healing 250 × 1.25, truncated.
    /// </summary>
    [Fact]
    public void Only_a_groups_first_discipline_carries_the_group_price()
    {
        using var files = Install.Open();
        var powers = new DisciplineDefinitions(files);
        var thing = Someone("brujah", files);
        var awe = thing.Actor!.Disciplines.Add(powers["Awe"]!, level: -1);
        var dread = thing.Actor!.Disciplines.Add(powers["DreadGaze"]!, level: -1);
        var feed = thing.Actor!.Disciplines.Add(powers["Feed"]!, level: -1);
        var heal = thing.Actor!.Disciplines.Add(powers["BloodHealing"]!, level: -1);
        Assert.Equal(1250, Advancement.DisciplineCost(thing, powers, awe.Definition));
        Assert.Equal(500, Advancement.DisciplineCost(thing, powers, dread.Definition));
        Assert.Equal(937, Advancement.DisciplineCost(thing, powers, feed.Definition));
        Assert.Equal(312, Advancement.DisciplineCost(thing, powers, heal.Definition));
    }

    [Fact]
    public void A_discipline_not_had_or_whose_requirements_are_not_met_has_no_price()
    {
        using var files = Install.Open();
        var powers = new DisciplineDefinitions(files);
        var thing = Someone("ventrue", files, type: ThingType.Player);
        Assert.Equal(-1, Advancement.DisciplineCost(thing, powers, powers["Command"]!));

        // Command asks for Manipulation 25 of a player-type character.
        var command = thing.Actor!.Disciplines.Add(powers["Command"]!, level: -1);
        thing.Actor.Stat[(int)ActorStat.Manipulation] = 20f;
        Assert.Equal(-1, Advancement.DisciplineCost(thing, powers, command.Definition));
        thing.Actor.Stat[(int)ActorStat.Manipulation] = 25f;
        Assert.Equal(1250, Advancement.DisciplineCost(thing, powers, command.Definition));
    }

    [Fact]
    public void Buying_a_level_opens_the_next_of_the_group_and_leaves_lifetime_experience_alone()
    {
        using var files = Install.Open();
        var powers = new DisciplineDefinitions(files);
        var thing = Someone("brujah", files);
        var me = thing.Actor!;
        var awe = me.Disciplines.Add(powers["Awe"]!, level: -1);
        me.SpendableExperience = 2000;
        me.Experience = 9000;
        var adv = new Advancement(thing, [thing], powers);

        Assert.True(adv.BuyDiscipline(awe.Definition));
        Assert.Equal(0, awe.Level);
        Assert.Equal(750, me.SpendableExperience);
        Assert.Equal(9000, me.Experience);
        Assert.Equal(-1, me.Disciplines["DreadGaze"]?.Level);    // "cannot use yet"
        Assert.Equal(350, Advancement.DisciplineCost(thing, powers, awe.Definition));
        me.SpendableExperience = 349;
        Assert.False(adv.BuyDiscipline(awe.Definition));
        Assert.Equal(0, awe.Level);
    }

    /// <summary>
    /// The revert puts both experience figures, both stat rows and every level back — and takes away a
    /// discipline the purchase opened — but installs the snapshot's copies, which were made in reverse,
    /// so **each revert reverses the discipline list**.
    /// </summary>
    [Fact]
    public void Revert_restores_everything_and_reverses_the_discipline_list()
    {
        using var files = Install.Open();
        var powers = new DisciplineDefinitions(files);
        var thing = Someone("brujah", files);
        var me = thing.Actor!;
        me.Disciplines.Add(powers["Celerity"]!, level: 1, quickSlot: 2);
        me.Disciplines.Add(powers["Awe"]!, level: -1);
        me.SetBaseStat(0, 30f);
        me.SpendableExperience = 5000;
        me.Experience = 12000;
        string[] before = [.. me.Disciplines.All.Select(d => d.Name)];

        var adv = new Advancement(thing, [thing], powers);
        Assert.True(adv.BuyStat(0));
        Assert.True(adv.BuyDiscipline(powers["Awe"]!));
        Assert.NotNull(me.Disciplines["DreadGaze"]);

        adv.Reset();
        Assert.Equal(5000, me.SpendableExperience);
        Assert.Equal(12000, me.Experience);
        Assert.Equal(30f, me.BaseStat[0]);
        Assert.Null(me.Disciplines["DreadGaze"]);
        Assert.Equal(-1, me.Disciplines["Awe"]!.Level);
        Assert.Equal(1, me.Disciplines["Celerity"]!.Level);
        Assert.Equal(2, me.Disciplines["Celerity"]!.QuickSlot);
        Assert.Equal(before.Reverse(), me.Disciplines.All.Select(d => d.Name));
        Assert.Null(me.Disciplines.Current);

        adv.Reset();
        Assert.Equal(before, me.Disciplines.All.Select(d => d.Name));
    }

    /// <summary>
    /// The session starts on the party's first member whoever asked, and "Finished" walks the slots.
    /// Anyone outside the party is advanced alone at slot 0 — and the engine's walk then still goes on
    /// to party[1], because it only counts.
    /// </summary>
    [Fact]
    public void The_session_walks_the_party_from_its_first_slot()
    {
        using var files = Install.Open();
        var powers = new DisciplineDefinitions(files);
        var a = Someone(null, null, guid: 1);
        var b = Someone(null, null, guid: 2);
        var outsider = Someone(null, null, guid: 3);

        var adv = new Advancement(b, [a, b], powers);
        Assert.Same(a, adv.Subject);
        Assert.True(adv.Next());
        Assert.Same(b, adv.Subject);
        Assert.False(adv.Next());

        var alone = new Advancement(outsider, [a, b], powers);
        Assert.Same(outsider, alone.Subject);
        Assert.True(alone.Next());
        Assert.Same(b, alone.Subject);
    }
}
