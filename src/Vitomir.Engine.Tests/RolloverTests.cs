// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The description box, and the discipline target test — the two things that sit either side of the
// pointer gate. The rollover is what the gate's answer is *for*; the target test is the route past
// it that a caster gets.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

public class RolloverTests
{
    static Location Open(GameFiles files) =>
        Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");

    /// <summary>
    /// **A thing is named by its `DESCRIPTIONID`, through the string table.** Old Town's cast
    /// carries `OLDMAN`, `MERCHANT`, `LADY`, `MONK`, `KNIGHTOFSTJOHN` — keys, not words — and the
    /// engine resolves each one once when the template is applied (the thing keeps the key
    /// and the answer). A key nothing claims reads as itself, which is the
    /// `.nls` lookup's own miss behaviour.
    /// </summary>
    [Fact]
    public void A_character_is_named_by_its_description_id()
    {
        using var files = Install.Open();
        var place = Open(files);
        var words = new Strings(files);
        words.Add("prague");

        var monk = place.Things.First(t => t.DescriptionId == "MONK");
        string said = Rollover.NameOf(monk, words);

        Assert.NotEqual("", said);
        Assert.NotEqual(monk.Template, said);           // not the template name
        Assert.Equal(words["MONK"], said);
        // And with no table at hand the key itself is what shows, rather than nothing.
        Assert.Equal("MONK", Rollover.NameOf(monk));
    }

    /// <summary>
    /// **A character's name is tinted by its health**, which is the one place this tree draws a
    /// number the player can read without a bar. The description packs `0xffff<v><v>` with
    /// `v = health / (max + 1) x 255` truncated — red stays at full, so the ramp is white to red
    /// through pink, and a character at nothing is `0xffff0000`.
    /// </summary>
    [Fact]
    public void A_characters_name_is_tinted_by_its_health()
    {
        var who = new Actor { Health = 100, MaxHealth = 100 };
        // 100 / 101 x 255 truncates to 252, not 255: the engine's own +1 never quite gets there.
        Assert.Equal(0xFFFFFCFCu, Rollover.HealthTint(who));

        who.Health = 0;
        Assert.Equal(0xFFFF0000u, Rollover.HealthTint(who));

        who.Health = 50;
        Assert.Equal(0xFFFF7E7Eu, Rollover.HealthTint(who));
    }

    /// <summary>
    /// Which things say anything at all, by type — the engine's own type switch. A
    /// region and a projectile go straight to the exit and are named by nothing.
    /// </summary>
    [Fact]
    public void A_region_says_nothing_and_a_prop_says_its_name()
    {
        using var files = Install.Open();
        var place = Open(files);

        var prop = place.Things.First(t => t.Type == ThingType.Prop);
        Assert.NotNull(Rollover.Name(prop));
        Assert.Equal(Rollover.White, Rollover.Name(prop)!.Value.Colour);

        if (place.Things.FirstOrDefault(t => t.Type == ThingType.Region) is { } region)
            Assert.Null(Rollover.Name(region));
        Assert.Null(Rollover.Name(null));
    }

    /// <summary>
    /// **Identifying an item is partly about learning what it is called.** Until then the rollover
    /// shows the plain `DESCRIPTIONID`; afterwards it shows `ITEM_IDENTIFIEDDESC`, which is a
    /// different string. A **magic** item (`item_flags` `0x1000`) is named in blue either way.
    /// </summary>
    [Fact]
    public void An_identified_item_is_named_differently_from_an_unidentified_one()
    {
        using var files = Install.Open();
        var place = Open(files);
        // A template that actually carries both strings, rather than one chosen by hand.
        var named = place.Templates!.Names
            .Select(n => place.Make(n))
            .FirstOrDefault(t => t?.Item is { } i && i.IdentifiedDescriptionId.Length > 0
                                 && i.DescriptionId.Length > 0
                                 && !i.IdentifiedDescriptionId.Equals(i.DescriptionId, StringComparison.OrdinalIgnoreCase));
        Assert.SkipWhen(named is null, "no shipped item carries both descriptions");

        var item = named!.Item!;
        item.Flags &= ~ItemFlags.Identified;
        Assert.Equal(named.DescriptionId, Rollover.Name(named)!.Value.Text);

        item.Flags |= ItemFlags.Identified;
        Assert.Equal(item.IdentifiedDescriptionId, Rollover.Name(named)!.Value.Text);

        item.Flags |= ItemFlags.Magic;
        Assert.Equal(Rollover.Magic, Rollover.Name(named)!.Value.Colour);
    }

    /// <summary>
    /// **The weapon block, against the shipped game's own screen.** A base-game shot of a
    /// Fine Rapier reads `Damage: 30 (Lethal)` / `Accuracy: 25  Speed: 111  1H`, and this is that,
    /// line for line — which pins three things at once: **accuracy comes first** (it was once
    /// the wrong way round), **speed is derived from `WEAPON_RELOADTIME`**
    /// (<see cref="Rollover.Speed"/>) rather than from `STRIKETIME`, and the rollover reads the
    /// **first** attack block whatever the weapon is currently set to.
    ///
    /// And two lines that show nothing and are there all the same (the description counts both whatever they
    /// hold). The third, the effects line, is empty — the rapier has no effects. The fourth: the rapier
    /// inherits Strength and Dexterity restrictions from `_weapon_DA`, both with a value of **0**, and
    /// the engine's restriction renderer splits on the value rather than the type — a zero sends it to
    /// the creature-type switch, whose cases are only the six creature restrictions, so each stat row
    /// renders as "" — **and the join keeps the two spaces between them**. Both are rows of
    /// the large box (Vitomir once dropped both, and then kept only the two
    /// spaces).
    /// </summary>
    [Fact]
    public void A_weapons_numbers_match_the_shipped_games_own_screen()
    {
        using var files = Install.Open();
        var place = Open(files);
        var words = new Strings(files);
        var effects = new EffectDefinitions(files);
        Assert.SkipWhen(effects.File is null, "misc/effects.ned not in the install");
        var rapier = place.Make("rapierfine");
        Assert.SkipWhen(rapier?.Item is null, "rapierfine not in the template set");
        rapier!.Item!.Flags |= ItemFlags.Identified;

        var lines = Rollover.Detail(rapier, words, effects.File).Select(l => l.Text).ToList();

        Assert.Equal("Fine Rapier", Rollover.Name(rapier, words)!.Value.Text);
        Assert.Equal(["Damage: 30 (Lethal)", "Accuracy: 25  Speed: 111  1H   ", "", "  "], lines);
    }

    /// <summary>
    /// **The Broadsword in the pack, line for line** — the four strings the description hands the large box:
    /// the damage, the numbers with the not-silvered space after `1H  `, the empty effects line and the
    /// restrictions' two spaces. Four rows: the empty one is a line tall.
    /// </summary>
    [Fact]
    public void A_dark_ages_sword_is_four_rows_an_empty_one_and_one_of_two_spaces()
    {
        using var files = Install.Open();
        var place = Open(files);
        var words = new Strings(files);
        var effects = new EffectDefinitions(files);
        Assert.SkipWhen(effects.File is null, "misc/effects.ned not in the install");
        var sword = place.Make("BroadSword");
        Assert.SkipWhen(sword?.Item is null, "BroadSword not in the template set");
        sword!.Item!.Flags |= ItemFlags.Identified;

        Assert.Equal(["Damage: 30 (Lethal)", "Accuracy: 0  Speed: 90  1H   ", "", "  "],
                     Rollover.Detail(sword, words, effects.File).Select(l => l.Text));
    }

    /// <summary>
    /// **A torch, a letter: two blank rows, and the large box up all the same.** An identified item
    /// with no count, no effects and no restrictions still has its effects line and its restrictions
    /// line, both "" (two lines counted), each a kind-3 item a line tall in the box
    /// — and nothing in the status bar asks what the box holds before it puts the
    /// pane up (mode 2 in the pack, mode 3 on the belt; the bar tests the pane and
    /// the time). So the black backdrop covers the party portraits for as long as the pointer rests,
    /// which is what 1.1 does hovering the Torch in the pack (`descLargeSP`
    /// up the whole hover, no words in it). The shelf's rollover builds the
    /// same two lines, and the shop hands them to the box as they are.
    /// </summary>
    [Theory]
    [InlineData("torchU")]
    [InlineData("CuthbertLetter")]
    public void A_torch_or_a_letter_is_two_blank_rows_in_the_pack_and_on_the_shelf(string template)
    {
        using var files = Install.Open();
        var place = Open(files);
        var words = new Strings(files);
        var effects = new EffectDefinitions(files);
        Assert.SkipWhen(effects.File is null, "misc/effects.ned not in the install");
        var thing = place.Make(template);
        Assert.SkipWhen(thing?.Item is null, $"{template} not in the template set");
        var item = thing!.Item!;
        item.Flags |= ItemFlags.Identified;
        Assert.Equal(0, item.Amount);

        Assert.Equal(["", ""], Rollover.Detail(thing, words, effects.File).Select(l => l.Text));
        Assert.Equal(["", ""], Rollover.Treasure(new TreasureRecord { Template = template, Item = item }, null, words, effects.File)
                                       .Detail.Select(l => l.Text));
        // The pack's icon rollover hands the pass both boxes at once, the large one with its two rows.
        var (_, large, now, _) = IconRollover.Item(item, onStatusBar: false, user: null, words, effects.File)!.Value;
        Assert.True(now);
        Assert.Equal(2, large.Count);
    }

    /// <summary>
    /// **1.1's own words**: each case is a large
    /// box exactly as 1.1 shows it — the box's lines joined by " / ", an empty line left out
    /// (<see cref="AsOneLine"/>); the pack's weapons and the shelf's.
    /// Between them
    /// they pin:
    /// <list type="bullet">
    /// <item>a Dark Ages weapon's zero-valued restriction rows, "" each with the two spaces between
    /// them kept — the Broadsword's third line is two spaces, and the Warhammer's `  Min 55 Strength`
    /// (the Dexterity row's "" first, slot 1 before slot 0);</item>
    /// <item>a weapon with no restrictions at all — the Stake — has no third line;</item>
    /// <item><see cref="Rollover.Speed"/>: 99 for a 1.0-second reload, not 100;</item>
    /// <item>the pack's not-silvered filler is a space and the shelf's nothing (`1H   ` against `1H  `);</item>
    /// <item>the shelf's ranged bit is `WEAPON_FLAGS1`'s alone (<see cref="Weapon.Flags1"/>): the
    /// Short Bow's comes from `WEAPON_FLAGS`, so on the shelf it has no ammo line.</item>
    /// </list>
    /// The Longbow the log caught was empty (`Ammo: 0`, the weapon's own load); every
    /// weapon made here is emptied to match, which only a pack bow's ammo line reads.
    /// </summary>
    [Theory]
    [InlineData("BroadSword", false, "Damage: 30 (Lethal) / Accuracy: 0  Speed: 90  1H    /   ")]
    [InlineData("Scimitar", false, "Damage: 30 (Lethal) / Accuracy: 0  Speed: 99  1H    /   ")]
    [InlineData("Mace", false, "Damage: 30 (Bashing) / Accuracy: 10  Speed: 90  1H    /   ")]
    [InlineData("MoroDagger", false, "Damage: 20 (Lethal) / Accuracy: 0  Speed: 117  1H    /   ")]
    [InlineData("Rapier", false, "Damage: 25 (Lethal) / Accuracy: 25  Speed: 99  1H    /   ")]
    [InlineData("Stake", false, "Damage: 5 (Lethal) / Accuracy: 30  Speed: 85  1H   ")]
    [InlineData("longBow", false, "Damage: 30 (Lethal) / Accuracy: 15  Speed: 75  2H    / Ammo: 0 / Min 35 Dexterity  Min 35 Strength")]
    [InlineData("WarHammer", true, "Damage: 55 (Bashing) / Accuracy: 0  Speed: 76  2H   /   Min 55 Strength")]
    [InlineData("WarHammerFine", true, "Damage: 60 (Bashing) / Accuracy: 0  Speed: 76  2H   /   Min 55 Strength")]
    [InlineData("PointyMaceFine", true, "Damage: 55 (Bashing) / Accuracy: 0  Speed: 83  2H   /   Min 50 Strength")]
    [InlineData("Claymore", true, "Damage: 45 (Lethal) / Accuracy: 0  Speed: 83  2H   /   Min 45 Strength")]
    [InlineData("Quarterstaff", true, "Damage: 35 (Bashing) / Accuracy: 0  Speed: 90  2H   /   Min 35 Strength")]
    [InlineData("RapierFine", true, "Damage: 30 (Lethal) / Accuracy: 25  Speed: 111  1H   /   ")]
    [InlineData("Dagger", true, "Damage: 10 (Lethal) / Accuracy: 0  Speed: 117  1H   /   ")]
    [InlineData("shortBow", true, "Damage: 30 (Lethal) / Accuracy: 5  Speed: 75  2H   / Min 25 Dexterity  ")]
    public void A_weapons_large_box_reads_as_1_1_shows_it(string template, bool shelf, string shown)
    {
        using var files = Install.Open();
        var place = Open(files);
        var words = new Strings(files);
        var effects = new EffectDefinitions(files);
        Assert.SkipWhen(effects.File is null, "misc/effects.ned not in the install");
        var thing = place.Make(template);
        Assert.SkipWhen(thing?.Item is null, $"{template} not in the template set");
        var item = thing!.Item!;
        item.Flags |= ItemFlags.Identified;
        if (item.Weapon is { } w) w.Attacks[0].Ammo = 0;

        var lines = shelf
            ? Rollover.Treasure(new TreasureRecord { Template = template, Item = item }, null, words, effects.File).Detail
            : Rollover.Detail(thing, words, effects.File);

        Assert.Equal(shown, AsOneLine(lines));
    }

    /// <summary>A box's lines on one line: the empty ones left out, the rest joined by " / ".</summary>
    static string AsOneLine(IEnumerable<RolloverLine> lines) =>
        string.Join(" / ", lines.Select(l => l.Text).Where(t => t.Length > 0));

    /// <summary>
    /// **`Speed`, as 1.1 computes it**: the reload in milliseconds times the float 0.001,
    /// a hundred divided by that, stored to a float and truncated — at more than single precision until
    /// it is stored, so a 1.0-second reload is **99**. The reload times are the templates' own
    /// `WEAPON_RELOADTIME`; the speeds are the ones 1.1 shows (the Fine
    /// Rapier's among them). And the arithmetic alone: a zero reload is 0 (infinity's 64-bit conversion's
    /// low half), and 0.8 s, 2 s and 4 s fall just under 125, 50 and 25 as 1.0 s falls under 100.
    /// </summary>
    [Fact]
    public void A_weapons_speed_is_1_1_s_float_arithmetic_on_its_reload()
    {
        Assert.Equal(99, Rollover.Speed(1000));
        Assert.Equal(111, Rollover.Speed(900));
        Assert.Equal(124, Rollover.Speed(800));
        Assert.Equal(49, Rollover.Speed(2000));
        Assert.Equal(24, Rollover.Speed(4000));
        Assert.Equal(0, Rollover.Speed(0));

        using var files = Install.Open();
        var place = Open(files);
        foreach (var (template, speed) in new[] { ("Scimitar", 99), ("Rapier", 99), ("RapierFine", 111),
                                                  ("longBow", 75), ("Stake", 85), ("WarHammer", 76) })
        {
            var weapon = place.Make(template)?.Item?.Weapon;
            Assert.SkipWhen(weapon is null, $"{template} not in the template set");
            Assert.Equal(speed, Rollover.Speed(weapon!.Attacks[0].ReloadTime));
        }
    }

    /// <summary>
    /// **A discipline's requirements end in two spaces** (the requirements' text appends them after every check,
    /// the last included — not the item restrictions' rule, which puts them only between rows).
    /// The large boxes as 1.1 shows them, the empty second line left out:
    /// Dread Gaze at its second level and Feed at its fifth, which has no checks and so an empty
    /// requirements line. (The three spaces after `Enemy` are the level line's own two and the
    /// join's.)
    /// </summary>
    [Theory]
    [InlineData("DreadGaze", 1, "Causes Fear on the Target / Level: 2     Blood: 10     Cast on: Enemy   / Min 35 Charisma  ")]
    [InlineData("Feed", 4, "Drains Blood From Targeted Victim / Level: 5     Blood: 0     Cast on: Enemy  Friendly  ")]
    public void A_disciplines_large_box_reads_as_1_1_shows_it(string name, int level, string shown)
    {
        using var files = Install.Open();
        var words = new Strings(files);
        var powers = new DisciplineDefinitions(files);
        Assert.SkipWhen(powers[name] is null, "misc/game.ndd not in the install");
        var (_, large) = DisciplinePane.Describe(new Discipline { Definition = powers[name]!, Level = level }, words.Find);
        Assert.Equal(shown, AsOneLine(large));
    }

    /// <summary>
    /// **The item block, against the same source.** The quick-use rat reads `Rat` / `Charges: 1` /
    /// `Increase Blood +10` / `Non-Human only`, and every one of those comes from somewhere
    /// different: the name from `DESCRIPTIONID`, the count from the item's own amount with the
    /// engine's trailing space, the effect from `effects.ned`'s level-0 column through the
    /// effect's own `.nls` name, and the restriction from the creature-type switch.
    /// </summary>
    [Fact]
    public void An_items_detail_block_matches_the_shipped_games_own_screen()
    {
        using var files = Install.Open();
        var place = Open(files);
        var words = new Strings(files);
        var effects = new EffectDefinitions(files);
        Assert.SkipWhen(effects.File is null, "misc/effects.ned not in the install");
        var rat = place.Make("VitaeRat");
        Assert.SkipWhen(rat?.Item is null, "VitaeRat not in the template set");
        rat!.Item!.Flags |= ItemFlags.Identified;

        Assert.Equal("Rat", Rollover.Name(rat, words)!.Value.Text);
        Assert.Equal(["Charges: 1", "Increase Blood +10", "Non-Human only"],
                     Rollover.Detail(rat, words, effects.File).Select(l => l.Text.TrimEnd()));
    }

    /// <summary>
    /// **A thrown weapon's stack is its ammo, not its charges.** The description writes the count line in
    /// its item branch only (`thing+4 == 1`); a weapon's count shows on the ammo line, which for a
    /// thrown one is the stack (`WEAPON_FLAG` 0x20). Vitomir showed throwing knives as `Charges: 10`
    /// as well, until a comparison with 1.1.
    /// </summary>
    [Fact]
    public void A_thrown_weapons_stack_shows_as_ammo_and_not_as_charges()
    {
        using var files = Install.Open();
        var place = Open(files);
        var words = new Strings(files);
        var effects = new EffectDefinitions(files);
        var knives = place.Make("throwingKnives");
        Assert.SkipWhen(knives?.Item is null || effects.File is null, "throwingKnives or effects.ned missing");
        knives!.Item!.Flags |= ItemFlags.Identified;
        Assert.True(knives.Item.Amount > 0);

        var lines = Rollover.Detail(knives, words, effects.File).Select(l => l.Text.TrimEnd()).ToList();

        Assert.DoesNotContain(lines, l => l.StartsWith(words["RPG_CHARGES"]));
        Assert.Contains($"{words["RPG_WPNAMMO"]} {knives.Item.Amount}", lines);
    }

    /// <summary>
    /// **A character gets a name and nothing else.** The description's character branch writes the tinted
    /// name and `break`s with its line count still zero, so the large box is handed nothing — the
    /// `Health: … Blood: … Frenzy: …` line a party member shows comes from the status bar's own row
    /// rollover and goes in the *small* box. Worth pinning, because the obvious
    /// assumption is the other way.
    /// </summary>
    [Fact]
    public void A_character_has_no_detail_block_at_all()
    {
        using var files = Install.Open();
        var place = Open(files);
        var words = new Strings(files);
        var who = place.Things.First(t => t.Type == ThingType.Actor);

        Assert.NotNull(Rollover.Name(who, words));
        Assert.Empty(Rollover.Detail(who, words));
        // And nor does a prop, however interesting it looks.
        Assert.Empty(Rollover.Detail(place.Things.First(t => t.Type == ThingType.Prop), words));
    }

    /// <summary>
    /// **What each row under the lines does to the large box** (the textbox's add,
    /// and its centring): every row, blank or not, is a line tall and
    /// `itemSpacing` apart, and the block is centred down the 88-pixel box — so each row under a
    /// sword's two number lines lifts them by half a row, 6 pixels. In 1.1 a Dark Ages
    /// sword is four rows (`A_dark_ages_sword_is_four_rows_an_empty_one_and_one_of_two_spaces`), its
    /// first line 12 pixels above a two-row block's. The measure of a Fine Rapier screenshot
    /// gave about 6, which is three rows; the code is the stronger evidence, and
    /// Vitomir draws four.
    /// </summary>
    [Fact]
    public void Each_row_under_the_lines_lifts_the_large_box_s_block_by_half_a_row()
    {
        var box = NuiPane.Parse("element textbox name=descText pos=0,0 size=320,88 textboxflags=0x07 font=defaultFont itemSpacing=2",
                                "descLargeSP.nui").Elements[0];
        int FirstLine(int rows) => NuiPane.TextBoxLine(box, 320, 88, 100, 10, 0, rows, 2).Y;
        Assert.Equal(45, FirstLine(2));        // a torch's two blank rows (a Stake is four: two of them blank)
        Assert.Equal(51, FirstLine(3));        // three: the screenshot's measure
        Assert.Equal(57, FirstLine(4));        // a Dark Ages sword: the empty effects line and the restrictions' two spaces
    }

    /// <summary>
    /// Until an item is identified it says one thing and stops — its plain description, and then
    /// `[not identified]`. Nothing about what it does, which is the point of identifying it.
    /// </summary>
    [Fact]
    public void An_unidentified_item_says_only_that_it_is_unidentified()
    {
        using var files = Install.Open();
        var place = Open(files);
        var words = new Strings(files);
        var effects = new EffectDefinitions(files);
        var rat = place.Make("VitaeRat");
        Assert.SkipWhen(rat?.Item is null || effects.File is null, "VitaeRat or effects.ned missing");
        rat!.Item!.Flags &= ~ItemFlags.Identified;

        var lines = Rollover.Detail(rat, words, effects.File);
        Assert.Single(lines);
        Assert.Equal(words["RPG_NOTIDENTIFIED"], lines[0].Text);
    }

    // --- the discipline target test -------------------------------------------------------------

    /// <summary>
    /// **You cannot feed on a corpse, and this is the function that stops you.** `Feed` is
    /// `TARGET ENEMY | TARGET FRIENDLY | NOPARTYNOTIFY` and carries no `TARGET DEAD`, so the
    /// target test refuses a dead character outright — the feed order path itself never looks at
    /// `THING_AF_DEAD` at all.
    /// </summary>
    [Fact]
    public void Feed_is_refused_on_a_corpse_and_allowed_on_the_living()
    {
        using var files = Install.Open();
        var place = Open(files);
        var powers = new DisciplineDefinitions(files);
        Assert.SkipWhen(powers["Feed"] is null, "misc/game.ndd not in the install");
        var feed = powers["Feed"]!;
        var me = place.Player!.Thing;
        var victim = Beside(place, me);
        victim.Actor!.Flags &= ~Actor.Flag.Neutral;

        Assert.True(DisciplineTarget.MayTarget(feed, me, victim));

        victim.Actor.Flags |= Actor.Flag.Dead;
        victim.Actor.FadeAt = place.Milliseconds + 30_000;
        Assert.False(DisciplineTarget.MayTarget(feed, me, victim));
    }

    /// <summary>
    /// **The window on a corpse is the lying-still period, not the fade.** A `TARGET DEAD`
    /// discipline needs the body's fade timer still set, and the tick zeroes it the instant the
    /// fade *starts* — so for the last five seconds the corpse is on screen and already useless.
    /// </summary>
    [Fact]
    public void A_target_dead_discipline_wants_the_fade_timer_still_set()
    {
        using var files = Install.Open();
        var place = Open(files);
        var powers = new DisciplineDefinitions(files);
        var raise = powers.File?.All.FirstOrDefault(d => d.Has(DisciplineFlags.TargetDead));
        Assert.SkipWhen(raise is null, "no shipped discipline carries TARGET DEAD");
        var me = place.Player!.Thing;
        var body = Beside(place, me);
        body.Actor!.Flags = (body.Actor.Flags & ~Actor.Flag.Neutral) | Actor.Flag.Dead;

        body.Actor.FadeAt = place.Milliseconds + 30_000;
        Assert.True(DisciplineTarget.MayTarget(raise!, me, body));

        body.Actor.FadeAt = 0;                          // the fade has begun
        Assert.False(DisciplineTarget.MayTarget(raise!, me, body));
    }

    /// <summary>
    /// **An offensive discipline refuses a neutral target** — the same neutrality that keeps the
    /// pointer off a passer-by, one layer down. A discipline without the `OFFENSIVE` bit takes
    /// them, which is how you cast something helpful on somebody you cannot hit.
    /// </summary>
    [Fact]
    public void An_offensive_discipline_refuses_a_neutral_target()
    {
        using var files = Install.Open();
        var place = Open(files);
        var powers = new DisciplineDefinitions(files);
        Assert.SkipWhen(powers.File is null, "misc/game.ndd not in the install");
        var offensive = powers.File!.All.First(d => d.Has(DisciplineFlags.Offensive)
                                                    && d.Has(DisciplineFlags.TargetEnemy));
        var gentle = powers.File.All.First(d => !d.Has(DisciplineFlags.Offensive)
                                                && d.Has(DisciplineFlags.TargetEnemy));
        var me = place.Player!.Thing;
        var who = Beside(place, me);
        Assert.True(who.Actor!.Has(Actor.Flag.Neutral), "Old Town's cast is neutral");

        Assert.False(DisciplineTarget.MayTarget(offensive, me, who));
        Assert.True(DisciplineTarget.MayTarget(gentle, me, who));

        who.Actor.Flags &= ~Actor.Flag.Neutral;
        Assert.True(DisciplineTarget.MayTarget(offensive, me, who));
    }

    /// <summary>
    /// Range is the first test and it is the `.ndd`'s own `maxDist` — a column whose header calls
    /// it *"maximum distance to cast (for AI)"* and which this function enforces for the player
    /// too. And a person you can talk to is never a target of anything.
    /// </summary>
    [Fact]
    public void Range_is_the_first_test_and_a_talkable_person_is_never_a_target()
    {
        using var files = Install.Open();
        var place = Open(files);
        var powers = new DisciplineDefinitions(files);
        Assert.SkipWhen(powers.File is null, "misc/game.ndd not in the install");
        var power = powers.File!.All.First(d => d.Has(DisciplineFlags.TargetEnemy) && d.MaxDistance > 0);
        var me = place.Player!.Thing;
        var who = Beside(place, me);
        who.Actor!.Flags &= ~Actor.Flag.Neutral;
        Assert.True(DisciplineTarget.MayTarget(power, me, who));

        who.Position = me.Position + new System.Numerics.Vector3(power.MaxDistance + 10f, 0, 0);
        Assert.False(DisciplineTarget.MayTarget(power, me, who));

        who.Position = me.Position;
        who.Actor.Flags |= Actor.Flag.TalkTo;
        Assert.False(DisciplineTarget.MayTarget(power, me, who));
    }

    /// <summary>A character standing on the player, so range never decides anything by accident.</summary>
    static WorldThing Beside(Location place, WorldThing me)
    {
        var who = place.Things.First(t => t.Type == ThingType.Actor && t.Actor is not null);
        who.Position = me.Position;
        return who;
    }
}
