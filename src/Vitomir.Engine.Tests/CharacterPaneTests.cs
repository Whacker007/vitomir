// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The character sheet. Every binding asserted here is the original pane's —
/// its set-up, its actor, its stat and soak updates —
/// rather than matched against a screenshot, which is the whole point:
/// the last screenshot-derived binding on the neighbouring pane was wrong for two of four actor
/// types and stayed wrong for two batches.
/// </summary>
public class CharacterPaneTests
{
    // A trimmed slice of the shipped pane, verbatim, so the geometry being modelled is visible.
    // Note the two pairs of bars declared at identical positions — that overlap is the reason
    // `ShownControls` exists at all.
    const string Nui = """
        name=characterPane
        pos=0,0
        visGroup=1
        quad mat=CharacterPane01 pos=0,224 size=256,256
        element textbox maxItems=1 name=charName pos=70,457 size=100,15 flags=0x02 textboxflags=0x01
        element textbox maxItems=1 name=charGeneration pos=231,457 size=60,15 flags=0x02 textboxflags=0x01
        element progressbar name=charBloodBar pos=144,374 size=81,8 fillbitmap=bloodPoolBar.tga flags=0x0A
        element progressbar name=charManaBar pos=144,374 size=81,8 fillbitmap=manaBar.tga flags=0x0A
        element progressbar name=charHealthBar pos=144,390 size=81,8 fillbitmap=healthBar.tga flags=0x02
        element progressbar name=charFrenzyBar pos=144,358 size=81,8 fillbitmap=frenzyBar.tga flags=0x0A
        element progressbar name=charFaithBar pos=144,358 size=81,8 fillbitmap=faithBar.tga flags=0x0A
        element progressbar name=charHumanityBar pos=144,342 size=81,8 fillbitmap=humanityBar.tga flags=0x02
        element textbox maxItems=1 name=charBaseStrength pos=108,307 size=25,15 flags=0x02 textboxflags=0x01
        element textbox maxItems=1 name=charStrength pos=134,307 size=25,15 flags=0x02 tbtextcolor=255,222,0
        element textbox maxItems=1 name=charBaseAppearance pos=108,159 size=25,15 flags=0x02 textboxflags=0x01
        element textbox maxItems=1 name=charAppearance pos=134,159 size=25,15 flags=0x02 tbtextcolor=255,222,0
        element soakslice name=charSoakFire pos=209,200 size=30,32 sliceprefix=pieFire
        element static name=charManaLabel bitmap=charManaLabel pos=35,370 size=99,15 flags=0x08
        """;

    static CharacterPane Pane(Actor? subject = null) =>
        new(NuiPane.Parse(Nui, "characterPane.nui")) { Subject = subject };

    static Actor Someone(ActorType type = ActorType.Vampire)
    {
        var a = new Actor { Type = type, Name = "Christof" };
        a.BaseStat[(int)ActorStat.Strength] = 30;
        a.Stat[(int)ActorStat.Strength] = 45;
        a.Stat[(int)ActorStat.Blood] = 12;
        a.Stat[(int)ActorStat.BloodPool] = 40;
        a.Stat[(int)ActorStat.Mana] = 7;
        a.Stat[(int)ActorStat.ManaPool] = 25;
        a.Stat[(int)ActorStat.Frenzy] = 13;
        a.Stat[(int)ActorStat.Faith] = 22;
        a.Stat[(int)ActorStat.Humanity] = 61;
        a.Stat[(int)ActorStat.Generation] = 12;
        return a;
    }

    [Fact]
    public void The_nine_attributes_are_the_nine_the_jump_table_dispatches()
    {
        // The stat update's switch has fourteen entries; six, eight and ten point at the function's own return
        // because the bars above have already handled them, and fourteen and fifteen are past the
        // end. So FRENZYRATING and BLOODRATING are on no character sheet in the game.
        Assert.Equal(9, CharacterPane.Attributes.Length);
        Assert.DoesNotContain(ActorStat.FrenzyRating, CharacterPane.Attributes);
        Assert.DoesNotContain(ActorStat.BloodRating, CharacterPane.Attributes);
        Assert.DoesNotContain(ActorStat.Blood, CharacterPane.Attributes);
        Assert.DoesNotContain(ActorStat.Generation, CharacterPane.Attributes);
    }

    [Fact]
    public void A_stat_shows_its_base_beside_its_effective_value()
    {
        var row = Pane(Someone()).Rows().Single(r => r.Stat == ActorStat.Strength);
        Assert.Equal(30, row.Base);
        Assert.Equal(45, row.Effective);
    }

    [Fact]
    public void Every_number_on_the_pane_truncates_rather_than_rounds()
    {
        // The boxes are `%d` of a float truncated toward zero, as in 1.1. A character
        // sitting on 80.9 health reads 80, and a reimplementation that rounds is wrong by one for
        // most of a playthrough without ever looking wrong in a screenshot of a round number.
        var a = Someone();
        a.Health = 80.9f;
        a.MaxHealth = 100f;
        Assert.Equal(80, Pane(a).Health.Value);
        Assert.Equal(-3, CharacterPane.Truncate(-3.9f));
    }

    [Fact]
    public void Only_a_human_swaps_blood_and_frenzy_for_mana_and_faith()
    {
        // The engine's test is the actor's type `== ACTOR_TYPE_HUMAN` and nothing else. "Is kindred" is
        // the mistake the status bar made from a screenshot: a ghoul and a monster both keep the
        // blood pair.
        foreach (var type in new[] { ActorType.Vampire, ActorType.Ghoul, ActorType.Monster })
        {
            var pane = Pane(Someone(type));
            Assert.False(pane.ShowsMana);
            Assert.Equal("charBloodBar", pane.Pool.Bar);
            Assert.Equal(12, pane.Pool.Value);
            Assert.Equal(40, pane.Pool.Max);
            Assert.Equal("charFrenzyBar", pane.Secondary.Bar);
            Assert.Contains("charBloodBar", pane.ShownControls());
            Assert.DoesNotContain("charManaLabel", pane.ShownControls());
        }

        var human = Pane(Someone(ActorType.Human));
        Assert.True(human.ShowsMana);
        Assert.Equal("charManaBar", human.Pool.Bar);
        Assert.Equal(7, human.Pool.Value);
        Assert.Equal(25, human.Pool.Max);
        Assert.Equal("charFaithBar", human.Secondary.Bar);
        Assert.Equal(22, human.Secondary.Value);
        Assert.Contains("charManaLabel", human.ShownControls());
    }

    [Fact]
    public void The_two_pairs_of_bars_are_declared_on_top_of_each_other()
    {
        // Which is why exactly one of each pair may be shown: it is not a style choice, it is the
        // only way the pane can be read.
        var pane = Pane(Someone());
        var blood = pane.Control("charBloodBar")!;
        var mana = pane.Control("charManaBar")!;
        Assert.Equal(blood.Pos, mana.Pos);
        Assert.Equal(pane.Control("charFrenzyBar")!.Pos, pane.Control("charFaithBar")!.Pos);

        var shown = pane.ShownControls().ToList();
        Assert.Single(shown, n => n is "charBloodBar" or "charManaBar");
        Assert.Single(shown, n => n is "charFrenzyBar" or "charFaithBar");
    }

    [Fact]
    public void Humanity_frenzy_and_faith_run_against_a_literal_hundred()
    {
        // `push 0x64` for the number and 0x42c80000 for the bar — not a stat, and not the pool.
        var pane = Pane(Someone());
        Assert.Equal(CharacterPane.FixedMax, pane.Humanity.Max);
        Assert.Equal(CharacterPane.FixedMax, pane.Secondary.Max);
        Assert.Equal(61, pane.Humanity.Value);
        Assert.Equal(0.61f, pane.Humanity.Fraction, 3);
    }

    [Fact]
    public void The_generation_box_says_a_number_a_word_or_nothing()
    {
        Assert.Equal("12", Pane(Someone()).Generation);
        Assert.Equal(CharacterPane.GhoulKey, Pane(Someone(ActorType.Ghoul)).Generation);
        Assert.Equal(CharacterPane.HumanKey, Pane(Someone(ActorType.Human)).Generation);
        Assert.Equal("", Pane(Someone(ActorType.Monster)).Generation);   // the dispatch clears it
    }

    [Fact]
    public void A_human_never_shows_a_clan_and_neither_does_anyone_without_one()
    {
        var vampire = Someone();
        vampire.Clan = "Brujah";
        Assert.Equal("Brujah", Pane(vampire).Clan);
        Assert.True(Pane(vampire).ShowsClanBox);

        var human = Someone(ActorType.Human);
        human.Clan = "Brujah";                      // the field can be set; the pane still hides it
        Assert.Equal("", Pane(human).Clan);
        Assert.False(Pane(human).ShowsClanBox);

        Assert.Equal("", Pane(Someone()).Clan);     // no clan record at all
    }

    [Fact]
    public void Poison_and_disease_appear_nowhere_on_the_sheet()
    {
        // `Init` zeroes ten slice slots and fills five; the update's `if (slice != 0)` skips the
        // rest, and neither type is one of the three spelled-out rows. Two of the ten damage types
        // are simply invisible here, and that is the engine's doing.
        Assert.DoesNotContain(DamageType.Poison, CharacterPane.SoakRows);
        Assert.DoesNotContain(DamageType.Disease, CharacterPane.SoakRows);
        Assert.DoesNotContain(DamageType.Poison, CharacterPane.SoakSlices);
        Assert.DoesNotContain(DamageType.Disease, CharacterPane.SoakSlices);
        Assert.DoesNotContain(DamageType.Poison, CharacterPane.DamageIcons.Select(i => i.Type));
        Assert.DoesNotContain(DamageType.Disease, CharacterPane.DamageIcons.Select(i => i.Type));

        // and between them the rows and the wedges cover the other eight exactly once
        var shown = CharacterPane.SoakRows.Concat(CharacterPane.SoakSlices).ToList();
        Assert.Equal(8, shown.Count);
        Assert.Equal(8, shown.Distinct().Count());
    }

    [Fact]
    public void Off_the_soakpie_switch_a_wedge_fills_in_sevenths_of_a_hundred_as_1_1_draws_it()
    {
        // Clamp(soak, 0, 100) × 0.01 × 7, truncated, clamped to 0..6. A screenshot
        // of the base game with leather armour on: fire and electrical at 10, both wedges empty.
        var a = Someone();
        a.Soak[(int)DamageType.Fire] = 10f;
        a.Soak[(int)DamageType.Electric] = 40.7f;
        a.Soak[(int)DamageType.Sun] = 250f;
        a.Soak[(int)DamageType.Cold] = -5f;
        var pane = Pane(a);
        pane.Rules = new Qol { SoakPie = false };
        var slices = pane.Slices();

        var fire = slices.Single(s => s.Type == DamageType.Fire);
        Assert.Equal(10, fire.Value);
        Assert.Equal(0, fire.Frame);                                            // 0.7 of a seventh
        var electric = slices.Single(s => s.Type == DamageType.Electric);
        Assert.Equal(40, electric.Value);                                       // the box reads the real figure
        Assert.Equal(2, electric.Frame);                                        // 2.849
        Assert.Equal(CharacterPane.SliceFrames - 1, slices.Single(s => s.Type == DamageType.Sun).Frame);
        Assert.Equal(0, slices.Single(s => s.Type == DamageType.Cold).Frame);  // clamped up from -5

        // Where each frame begins.
        Assert.Equal(0, CharacterPane.SliceFrame(14.28f));
        Assert.Equal(1, CharacterPane.SliceFrame(15f));
        Assert.Equal(5, CharacterPane.SliceFrame(85f));
        Assert.Equal(6, CharacterPane.SliceFrame(86f));
        Assert.Equal(6, CharacterPane.SliceFrame(100f));
    }

    [Fact]
    public void With_the_soakpie_switch_a_wedge_starts_filling_above_0_and_is_full_at_100()
    {
        // It fills up starting at 0, and is full at 100 or more soak per
        // value. On by default, so a pane with no rules of its own gets it.
        Assert.True(new Qol().SoakPie);

        var a = Someone();
        a.Soak[(int)DamageType.Fire] = 10f;       // leather armour: empty in 1.1, the first step here
        a.Soak[(int)DamageType.Electric] = 99.9f; // reads 99, so one short of full
        a.Soak[(int)DamageType.Sun] = 250f;
        a.Soak[(int)DamageType.Faith] = 0.6f;     // reads 0, so nothing drawn beside it
        a.Soak[(int)DamageType.Cold] = -5f;
        var pane = Pane(a);
        pane.Rules = new Qol();
        var slices = pane.Slices();

        Assert.Equal(1, slices.Single(s => s.Type == DamageType.Fire).Frame);
        Assert.Equal(5, slices.Single(s => s.Type == DamageType.Electric).Frame);
        Assert.Equal(99, slices.Single(s => s.Type == DamageType.Electric).Value);
        Assert.Equal(CharacterPane.SliceFrames - 1, slices.Single(s => s.Type == DamageType.Sun).Frame);
        Assert.Equal(0, slices.Single(s => s.Type == DamageType.Faith).Frame);
        Assert.Equal(0, slices.Single(s => s.Type == DamageType.Cold).Frame);

        // Five even steps of twenty between empty and full, and full is reached at 100, not before.
        int[] firstOf = [1, 21, 41, 61, 81];
        for (int step = 0; step < firstOf.Length; step++)
        {
            Assert.Equal(step + 1, CharacterPane.EvenSliceFrame(firstOf[step]));
            Assert.Equal(step + 1, CharacterPane.EvenSliceFrame(Math.Min(firstOf[step] + 19, 99)));
        }
        Assert.Equal(0, CharacterPane.EvenSliceFrame(0));
        Assert.Equal(5, CharacterPane.EvenSliceFrame(99));
        Assert.Equal(6, CharacterPane.EvenSliceFrame(100));
        Assert.Equal(6, CharacterPane.EvenSliceFrame(1000));

        // The frame never goes down as the soak goes up.
        for (int soak = 0; soak < 200; soak++)
            Assert.True(CharacterPane.EvenSliceFrame(soak) <= CharacterPane.EvenSliceFrame(soak + 1));
    }

    [Fact]
    public void Status_and_the_two_experience_boxes_are_only_filled_for_a_player()
    {
        var npc = Someone();
        npc.Experience = 12_000;
        npc.SpendableExperience = 300;
        Assert.False(Pane(npc).ShowsProgress);
        Assert.Equal("", Pane(npc).Experience);
        Assert.Equal("", Pane(npc).EarnedExperience);
        Assert.Equal("", Pane(npc).Status);

        npc.IsPlayerThing = true;
        var pane = Pane(npc);
        Assert.True(pane.ShowsProgress);
        Assert.Equal("12000", pane.Experience);          // `charExperience` <- the lifetime
        Assert.Equal("300", pane.EarnedExperience);      // `charEarnedExp`  <- the unspent
    }

    [Fact]
    public void Rank_comes_from_the_lifetime_figure_so_spending_cannot_demote_anyone()
    {
        Assert.Equal(0, CharacterPane.Rank(0));
        Assert.Equal(0, CharacterPane.Rank(4_999));
        Assert.Equal(1, CharacterPane.Rank(5_000));
        Assert.Equal(2, CharacterPane.Rank(10_000));
        Assert.Equal(3, CharacterPane.Rank(50_000));
        Assert.Equal(4, CharacterPane.Rank(200_000));
        Assert.Equal(4, CharacterPane.Rank(100_000_000));
        Assert.Equal(CharacterPane.MaxRanks, CharacterPane.RankThresholds.Length);

        // Christof at the smithy: human, 755 lifetime, rank 0 — which is what his sheet said.
        var christof = Someone(ActorType.Human);
        christof.Experience = 755;
        Assert.Equal(0, CharacterPane.Rank(christof.Experience));

        // and a monster has no title at all: its column in the table is blank in every row.
        Assert.Equal("", CharacterPane.RankKey(Someone(ActorType.Monster)));
        Assert.Equal("UI_STATUS_HUMAN0", CharacterPane.RankKey(christof));

        // Kindred are named, not numbered. The base game's sheet: 5,345 lifetime, "Neonate".
        var kindred = Someone(ActorType.Vampire);
        kindred.Experience = 5_345;
        Assert.Equal("UI_STATUS_NEONATE", CharacterPane.RankKey(kindred));
        kindred.Experience = 0;
        Assert.Equal("UI_STATUS_FLEDGELING", CharacterPane.RankKey(kindred));
        kindred.Experience = 200_000;
        Assert.Equal("UI_STATUS_METHUSELAH", CharacterPane.RankKey(kindred));
    }

    [Fact]
    public void The_damage_row_reports_the_equipped_weapon_and_clears_without_one()
    {
        var pane = Pane(Someone());
        Assert.Equal(("", DamageType.Normal), pane.DamageRow);

        pane.Weapon = (17.8f, DamageType.Aggravated);
        Assert.Equal(("17", DamageType.Aggravated), pane.DamageRow);
    }

    [Fact]
    public void Nothing_is_filled_in_without_a_subject()
    {
        var pane = Pane();
        Assert.Empty(pane.Rows());
        Assert.Empty(pane.Soaks());
        Assert.Empty(pane.Slices());
        Assert.Equal("", pane.Name);
        Assert.Equal("", pane.Generation);
        Assert.Equal(0, pane.Health.Max);
    }

    [Fact]
    public void Every_box_has_an_entry_even_when_the_engine_would_clear_it()
    {
        // Blank and "not a box" are different answers, and a caller needs to be able to tell them
        // apart — the engine clears a box rather than leaving the last character's number in it.
        var npc = Someone();
        var texts = Pane(npc).Texts();
        Assert.Equal("", texts["charStatus"]);          // cleared: not a player thing
        Assert.Equal("", texts["charExperience"]);
        Assert.Equal("30", texts["charBaseStrength"]);
        Assert.Equal("45", texts["charStrength"]);
        Assert.Equal("12", texts["charGeneration"]);
    }

    [Fact]
    public void There_is_one_set_of_pool_numbers_and_two_sets_of_pool_bars()
    {
        // `Init` caches one pointer for `charBlood` and one for `charMaxBlood` and the human branch
        // writes the mana figures through those same two. So a human's mana reads out of the boxes
        // named for blood, and only the *bars* come in pairs.
        var human = Pane(Someone(ActorType.Human)).Texts();
        Assert.Equal("7", human["charBlood"]);
        Assert.Equal("25", human["charMaxBlood"]);
        Assert.Equal("22", human["charFrenzy"]);        // faith, through the frenzy boxes
        Assert.Equal("100", human["charMaxFrenzy"]);

        var vampire = Pane(Someone()).Texts();
        Assert.Equal("12", vampire["charBlood"]);
        Assert.Equal("40", vampire["charMaxBlood"]);
    }

    [Fact]
    public void The_spelled_out_soak_rows_keep_the_engines_own_spelling()
    {
        // DAMAGE_TYPE_NORMAL's boxes are called `charBash` and `charBaseBash`, and its icon is
        // `bashIcon`. Following the enum name instead would miss every one of them.
        var a = Someone();
        a.BaseSoak[(int)DamageType.Normal] = 5;
        a.Soak[(int)DamageType.Normal] = 15;
        var texts = Pane(a).Texts();
        Assert.Equal("5", texts["charBaseBash"]);
        Assert.Equal("15", texts["charBash"]);
        Assert.False(texts.ContainsKey("charNormal"));

        Assert.Equal("Holy", CharacterPane.SliceName(DamageType.Faith));
        Assert.Equal("Ice", CharacterPane.SliceName(DamageType.Cold));
        Assert.Equal("Fire", CharacterPane.SliceName(DamageType.Fire));
    }

    [Fact]
    public void A_wedge_names_its_frame_out_of_its_own_sliceprefix()
    {
        // The prefix comes back lower-cased, because the engine lower-cases every `.nui` line
        // before parsing it and this reader does the same. Texture lookup is case-insensitive, so
        // it resolves either way — but a test that asserts the file's own spelling would fail.
        var a = Someone();
        a.Soak[(int)DamageType.Fire] = 64.9f;                                  // 4.54 sevenths
        Assert.Equal("piefire4", Pane(a).Images()["charSoakFire"]);

        a.Soak[(int)DamageType.Fire] = 0f;
        Assert.Equal("piefire0", Pane(a).Images()["charSoakFire"]);
    }

    [Fact]
    public void Only_one_of_each_overlapping_pair_of_bars_is_ever_drawn()
    {
        var vampire = Pane(Someone());
        Assert.True(vampire.Shown("charBloodBar"));
        Assert.False(vampire.Shown("charManaBar"));
        Assert.False(vampire.Shown("charManaLabel"));
        Assert.True(vampire.Shown("charHealthBar"));            // unconditional
        Assert.True(vampire.Shown("charStrength"));             // not conditional at all
        Assert.Equal(2, vampire.Bars().Count(b => b.Key is "charBloodBar" or "charManaBar"
                                                       or "charFrenzyBar" or "charFaithBar"));

        var human = Pane(Someone(ActorType.Human));
        Assert.False(human.Shown("charBloodBar"));
        Assert.True(human.Shown("charManaBar"));
        Assert.True(human.Shown("charManaLabel"));
    }

    [Fact]
    public void The_advancement_buttons_stay_down()
    {
        // `SetActor` hides all eleven on every refresh and shows one only when there is something
        // to spend. Nothing here spends experience, so none of them is up — and a button that is
        // drawn and does nothing is worse than one that is absent.
        var pane = Pane(Someone());
        foreach (var name in CharacterPane.Conditional.Where(n => n.StartsWith("adv")))
            Assert.False(pane.Shown(name));
    }

    [Fact]
    public void The_damage_icon_follows_the_weapon_and_the_clan_crest_is_not_this_panes_to_find()
    {
        var pane = Pane(Someone());
        Assert.Equal("bashIcon", pane.Images()["charDamageIcon"]);
        pane.Weapon = (20f, DamageType.Fire);
        Assert.Equal("fireIcon", pane.Images()["charDamageIcon"]);

        // The engine registers clan crests from the clan list at startup, so the bitmap comes from
        // the clan record rather than from the pane. Without one, nothing is drawn.
        Assert.False(pane.Images().ContainsKey("charClanBox"));
        pane.Subject!.Clan = "Brujah";
        pane.ClanImage = "clanBrujah";
        Assert.Equal("clanBrujah", pane.Images()["charClanBox"]);
        Assert.True(pane.Shown("charClanBox"));
    }

    [Fact]
    public void A_localisation_key_is_a_key_until_somebody_resolves_it()
    {
        // Nothing from the player's install is baked into the assembly: the pane hands the key back
        // unchanged until a resolver is wired to `strings/*.nls`.
        var pane = Pane(Someone(ActorType.Ghoul));
        Assert.Equal(CharacterPane.GhoulKey, pane.Generation);
        pane.Strings = key => key == CharacterPane.GhoulKey ? "Ghoul" : key;
        Assert.Equal("Ghoul", pane.Generation);
    }

    /// <summary>
    /// **`textboxflags`, read the second time.** A base-game screenshot: every
    /// value on the sheet is centred in its box. The first reading had the right bits and swapped
    /// the control's width and height (the hit test says which is the width): bit 0
    /// centres each line **across** the box, bit 3 decides whether items start at the top or the
    /// bottom, and bit 1 centres the block **down** the box. Positions count up from the bottom.
    /// </summary>
    [Fact]
    public void A_textbox_of_flags_one_is_centred_across_and_against_the_top()
    {
        var box = NuiPane.Parse(
            "element textbox maxItems=1 name=t pos=10,20 size=40,15 textboxflags=0x01",
            "t.nui").Elements[0];
        Assert.Equal(NuiPane.TextBoxFlags.Centre, NuiPane.TextBox(box));

        // A 12-wide word in a 40-wide box starts 14 in, a 24-wide one 8 in: the middles line up.
        // An 8-tall line in a 15-tall box has its bottom 7 up — against the top.
        Assert.Equal((14, 7), NuiPane.TextBoxOrigin(box, 40, 15, 12, 8));
        Assert.Equal((8, 7), NuiPane.TextBoxOrigin(box, 40, 15, 24, 8));
        // A line wider than its box is centred on it and starts left of it — a signed halving.
        Assert.Equal((-10, 7), NuiPane.TextBoxOrigin(box, 40, 15, 60, 8));
    }

    [Fact]
    public void Bit_one_centres_down_the_box_bit_three_starts_at_the_bottom_and_bit_two_wraps()
    {
        // 0x03, the small description box: centred both ways. The free 7 rows are halved with
        // truncation to 3, which the block moves down from the top — so 4 below and 3 above.
        var both = NuiPane.Parse("element textbox name=t size=40,15 textboxflags=0x03", "t.nui").Elements[0];
        Assert.Equal(NuiPane.TextBoxFlags.Centre | NuiPane.TextBoxFlags.VCentre, NuiPane.TextBox(both));
        Assert.Equal((14, 4), NuiPane.TextBoxOrigin(both, 40, 15, 12, 8));

        // 0x0C, the subtitle: left-aligned, against the bottom — a quarter of the line height up
        // from it.
        var sub = NuiPane.Parse("element textbox name=t size=40,15 textboxflags=0x0C", "t.nui").Elements[0];
        Assert.Equal((0, 2), NuiPane.TextBoxOrigin(sub, 40, 15, 12, 8));

        // No flags: plain left, against the top.
        var bare = NuiPane.Parse("element textbox name=t size=40,15", "t.nui").Elements[0];
        Assert.Equal(NuiPane.TextBoxFlags.None, NuiPane.TextBox(bare));
        Assert.Equal((0, 7), NuiPane.TextBoxOrigin(bare, 40, 15, 12, 8));

        // `%i` takes decimal too, which is why two shipped panes write a bare 7 and a bare 4 where
        // the rest write 0x07 and 0x04 — the same values, and the parser does not care. Bit 2 is
        // word wrap, which does not move a line that fits, so a 7 lays out like a 3.
        var seven = NuiPane.Parse("element textbox name=t size=40,15 textboxflags=7", "t.nui").Elements[0];
        var hex = NuiPane.Parse("element textbox name=t size=40,15 textboxflags=0x07", "t.nui").Elements[0];
        Assert.Equal(NuiPane.TextBox(hex), NuiPane.TextBox(seven));
        Assert.True(NuiPane.TextBox(seven).HasFlag(NuiPane.TextBoxFlags.WordWrap));
        Assert.Equal(NuiPane.TextBoxOrigin(both, 40, 15, 12, 8), NuiPane.TextBoxOrigin(seven, 40, 15, 12, 8));

        // Two lines of a centred block, 2 apart, in an 88-tall box: 18 tall, 70 free, 35 either side
        // — the first line on top.
        Assert.Equal((14, 35 + 10), NuiPane.TextBoxLine(seven, 40, 88, 12, 8, 0, 2, 2));
        Assert.Equal((14, 35), NuiPane.TextBoxLine(seven, 40, 88, 12, 8, 1, 2, 2));
    }

    /// <summary>Every textbox on the shipped pane, from the player's own install.</summary>
    [Fact]
    public void Every_textbox_on_the_shipped_character_pane_is_centred_across()
    {
        using var files = Install.Open();
        Assert.SkipUnless(files.TryRead("ui/characterPane.nui", out var data), "no ui/characterPane.nui");
        var pane = NuiPane.Parse(System.Text.Encoding.Latin1.GetString(data), "characterPane.nui");

        var boxes = pane.Elements.Where(e => e.Type == "textbox").ToList();
        Assert.Equal(44, boxes.Count);
        Assert.All(boxes, b => Assert.Equal(NuiPane.TextBoxFlags.Centre, NuiPane.TextBox(b)));
    }
}
