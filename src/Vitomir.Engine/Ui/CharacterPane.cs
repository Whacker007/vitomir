// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The character sheet, as state rather than as pixels — what the original pane would be drawing.
// `Vitomir.Render.CharacterPass` puts it up; everything here is testable without a GPU, which is
// the split `StatusBar` and `ConversationPane` already use.
//
// **Every binding in this file is 1.1's own rather than matched to a screenshot**,
// which matters because the last screenshot-derived binding on this pane's neighbour (the status
// bar's "kindred show blood") was wrong for two of the four actor types. Three functions carry all
// of it:
//
//   * `vuiChar::Init` caches one pointer per control and registers the damage-type icons;
//   * `vuiChar::SetActor` fills the boxes that are not stat-driven and then walks all 19
//     stats and all 10 soaks;
//   * `vuiChar::StatChanged` is the per-stat update, and its **switch** is
//     the binding table: 14 entries; stats 6, 8 and 10 point at the
//     function's own return because the bars above them have already been handled, and stats 14
//     and 15 are past the end (`if (0xd < index) return;`), so **FRENZYRATING and BLOODRATING are
//     never shown**.
//
// The two blocks the handlers read are uniform, and they agree exactly with the save format:
// **the base stats and the effective ones are two arrays of the actor**, and the distance between them is
// 0x4c, which is 19 floats, the same stride the save file shows.
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

/// <summary>One attribute row: the two boxes the pane shows side by side.</summary>
/// <param name="Stat">Which stat, and therefore which pair of `.nui` boxes.</param>
/// <param name="Base">What the template gave it (`charBase*`).</param>
/// <param name="Effective">What it is now (`char*`, drawn in the pane's yellow).</param>
public readonly record struct AttributeRow(ActorStat Stat, int Base, int Effective);

/// <summary>One of the three soak rows the pane spells out as numbers.</summary>
public readonly record struct SoakRow(DamageType Type, int Base, int Effective);

/// <summary>
/// One wedge of the soak pie. The engine picks the artwork out of the slice's own seven frames by
/// **the soak as a share of 100, in sevenths**: clamp to 0..100, times 0.01, times 7,
/// truncate, clamp to 0..6 — and writes the soak itself, truncated, into the paired textbox.
/// </summary>
public readonly record struct SoakSlice(DamageType Type, int Value, int Frame);

/// <summary>One of the four bars, with the numbers either side of it.</summary>
public readonly record struct BarRow(string Bar, int Value, int Max)
{
    /// <summary>How full it is, 0 to 1 — the fraction the bar is clamped to.</summary>
    public float Fraction => Max > 0 ? Math.Clamp((float)Value / Max, 0f, 1f) : 0f;
}

/// <summary>
/// The character pane: `ui/characterPane.nui` plus the state the engine would have put in it.
/// </summary>
public sealed class CharacterPane
{
    public const string PaneName = "characterPane";

    /// <summary>
    /// The nine attributes, **in the order the switch dispatches them** rather than in the
    /// order they sit on screen. Reading the pane top to bottom gives Strength, Dexterity, Stamina,
    /// then Perception, Intelligence, Wits, then Charisma, Manipulation, Appearance — because
    /// `.nui` space counts up from the bottom and their declared `y` runs 307 down to 159.
    /// </summary>
    public static readonly ActorStat[] Attributes =
    [
        ActorStat.Strength, ActorStat.Dexterity, ActorStat.Stamina,
        ActorStat.Perception, ActorStat.Intelligence, ActorStat.Wits,
        ActorStat.Charisma, ActorStat.Manipulation, ActorStat.Appearance,
    ];

    /// <summary>
    /// The three soaks the pane spells out as a labelled row with its own icon, in the order
    /// `vuiChar::SoakChanged` tests them: index 0, 1, 2.
    /// </summary>
    public static readonly DamageType[] SoakRows =
        [DamageType.Normal, DamageType.Lethal, DamageType.Aggravated];

    /// <summary>
    /// The five soaks drawn as pie wedges, in the order `vuiChar::Init` fills the slice array —
    /// which is by **damage-type index**, 3 to 7. `Init` zeroes ten slots and fills five, so
    /// indices 8 and 9 stay null and the update's `if (slice != 0)` skips them: **POISON and
    /// DISEASE appear nowhere on the character sheet**, and that is the engine's own doing rather
    /// than an omission of ours.
    /// </summary>
    public static readonly DamageType[] SoakSlices =
        [DamageType.Electric, DamageType.Fire, DamageType.Sun, DamageType.Faith, DamageType.Cold];

    /// <summary>
    /// The damage types that have an icon at all, by the index `Init` registers them under:
    /// 0 bash, 1 lethal, 2 aggravated, 3 electric, 4 fire, 5 sun, 6 holy, 7 ice. Eight of ten.
    /// </summary>
    public static readonly (DamageType Type, string Icon)[] DamageIcons =
    [
        (DamageType.Normal, "bashIcon"), (DamageType.Lethal, "lethalIcon"),
        (DamageType.Aggravated, "aggravatedIcon"), (DamageType.Electric, "electricIcon"),
        (DamageType.Fire, "fireIcon"), (DamageType.Sun, "sunIcon"),
        (DamageType.Faith, "holyIcon"), (DamageType.Cold, "iceIcon"),
    ];

    /// <summary>How many frames a soak wedge has, and therefore what its value is clamped to.</summary>
    public const int SliceFrames = 7;

    /// <summary>
    /// Humanity, frenzy and faith all run against a literal **100** in the update rather than
    /// against a stat — `push 0x64` for the number and `0x42c80000` (100.0f) for the bar.
    /// </summary>
    public const int FixedMax = 100;

    /// <summary>
    /// What the generation box says for a non-vampire. The engine puts a **localisation key**
    /// through its string table rather than a literal, so these are the keys and not the
    /// words; a monster gets the box cleared outright (the switch's default branch calls the
    /// control's own Clear).
    /// </summary>
    public const string GhoulKey = "UI_GENERAL_GHOUL", HumanKey = "UI_GENERAL_HUMAN";

    public NuiPane Pane { get; }

    public CharacterPane(NuiPane pane) => Pane = pane;

    /// <summary>Whose sheet this is. Null leaves every row empty, which is what a closed pane is.</summary>
    public Actor? Subject { get; set; }

    /// <summary>
    /// The weapon damage the pane reports: the amount and type of the **currently selected attack
    /// of the equipped weapon** (its selected attack's
    /// record). Null when nothing is held, which is the branch that clears the box
    /// and puts icon 0 up.
    ///
    /// Read off the subject's own weapon unless something sets it. Nothing did, so until
    /// The sheet's damage box was always blank and its icon always bashing — a
    /// base-game screenshot has *damage 30* beside the lethal crossed blades for the broadsword.
    /// </summary>
    public (float Amount, DamageType Type)? Weapon
    {
        get => weapon ?? (Subject?.Weapon is { } held ? (held.Current.Damage, held.Current.DamageType) : null);
        set => weapon = value;
    }
    (float Amount, DamageType Type)? weapon;

    /// <summary>
    /// How a localisation key becomes a word. Left alone it hands the key straight back, so
    /// nothing from the player's install is baked into this assembly — the resolver is wired to
    /// `strings/*.nls` by whoever owns the pane.
    /// </summary>
    public Func<string, string> Strings { get; set; } = key => key;

    /// <summary>
    /// **Every number on this pane is a truncation, not a rounding.** The boxes are `%d` of a float
    /// truncated toward zero, so 80.9 health shows as 80. Worth stating
    /// because a reimplementation that rounds is wrong by one for most of a character's life and
    /// the difference never shows up in a screenshot of a round number.
    /// </summary>
    public static int Truncate(float value) => (int)value;

    /// <summary>The nine attribute rows, base and effective.</summary>
    public List<AttributeRow> Rows()
    {
        var rows = new List<AttributeRow>();
        if (Subject is not { } actor) return rows;
        foreach (var stat in Attributes)
            rows.Add(new AttributeRow(stat,
                Truncate(actor.BaseStat[(int)stat]), Truncate(actor.Stat[(int)stat])));
        return rows;
    }

    /// <summary>The three spelled-out soak rows.</summary>
    public List<SoakRow> Soaks()
    {
        var rows = new List<SoakRow>();
        if (Subject is not { } actor) return rows;
        foreach (var type in SoakRows)
            rows.Add(new SoakRow(type,
                Truncate(actor.BaseSoak[(int)type]), Truncate(actor.Soak[(int)type])));
        return rows;
    }

    /// <summary>
    /// The five pie wedges. The number beside each is the effective soak, truncated. Which of the
    /// wedge's seven frames is drawn depends on <see cref="Qol.SoakPie"/>, which is **on** unless
    /// the player turns it off:
    ///
    /// - **on** — <see cref="EvenSliceFrame"/>: empty at 0, one step per 20 of the number shown,
    ///   full at 100, by design.
    /// - **off** — <see cref="SliceFrame"/>, 1.1's own rule: the soak clamped to 0..100
    ///   and scaled to **sevenths** of it, so a wedge is empty below 15 and full from 86.
    ///
    /// What play showed, with leather armour on: the circle pies for
    /// each of the soak types were fully highlighted as red, while in the base game the pie slice starts
    /// to 'fill' gradually starting from the center, depending on how much soak there is. The
    /// screenshot has fire and electrical at 10 and both wedges empty. This had used the soak **as**
    /// the frame, so 10 was clamped to the last frame, full — the two constants (0.01
    /// and 7.0) had been missed.
    /// </summary>
    public List<SoakSlice> Slices()
    {
        var slices = new List<SoakSlice>();
        if (Subject is not { } actor) return slices;
        bool even = (Rules ?? Qol.Current).SoakPie;
        foreach (var type in SoakSlices)
        {
            float soak = actor.Soak[(int)type];
            int shown = Truncate(soak);
            slices.Add(new SoakSlice(type, shown, even ? EvenSliceFrame(shown) : SliceFrame(soak)));
        }
        return slices;
    }

    /// <summary>
    /// Which quality-of-life switches are in force. Null takes <see cref="Qol.Current"/>, which is
    /// what the game does; a test passes its own rather than installing the global.
    /// </summary>
    public Qol? Rules { get; set; }

    /// <summary>The soak a wedge is full at, in effect: the clamp's ceiling (`_DAT_004eb420`).</summary>
    public const float SliceCeiling = 100f;

    /// <summary>Which of a wedge's seven frames a soak shows in 1.1.</summary>
    public static int SliceFrame(float soak)
    {
        float share = soak < 0f ? 0f : soak <= SliceCeiling ? soak : SliceCeiling;
        int frame = (int)(share * 0.01f * SliceFrames);
        return frame < 0 ? 0 : frame > SliceFrames - 1 ? SliceFrames - 1 : frame;
    }

    /// <summary>
    /// The soak a wedge climbs one step for under <see cref="Qol.SoakPie"/>: the five frames between
    /// empty and full share the hundred evenly.
    /// </summary>
    public const int EvenSliceStep = 20;

    /// <summary>
    /// Which frame the **number shown** draws under <see cref="Qol.SoakPie"/>: 0 draws empty, 100 or
    /// more draws full, and anything between climbs one frame per 20 — 1 to 20 the first, 81 to 99
    /// the fifth. It reads the truncated figure in the box rather than the soak underneath, so a
    /// box reading 0 is never beside a wedge with something in it.
    /// </summary>
    public static int EvenSliceFrame(int shown)
    {
        if (shown <= 0) return 0;
        if (shown >= (int)SliceCeiling) return SliceFrames - 1;
        int frame = (shown + EvenSliceStep - 1) / EvenSliceStep;
        return frame > SliceFrames - 2 ? SliceFrames - 2 : frame;
    }

    /// <summary>
    /// Whether this character's pools are the human pair. The engine's test is
    /// the actor's type `== ACTOR_TYPE_HUMAN` and nothing else — **not** "is kindred", which is the
    /// mistake the status bar made from a screenshot: a ghoul and a monster both take the blood
    /// and frenzy pair, and only a human swaps to mana and faith.
    /// </summary>
    public bool ShowsMana => Subject?.Type == ActorType.Human;

    /// <summary>Health, from the actor's own two fields rather than from a stat.</summary>
    public BarRow Health => Subject is { } a
        ? new BarRow("charHealthBar", Truncate(a.Health), Truncate(a.MaxHealth))
        : default;

    /// <summary>
    /// The pool bar: **blood** for everyone but a human, **mana** for a human. Stat 6 and stat 16
    /// both drive the blood one (current and maximum), 17 and 18 the mana one.
    /// </summary>
    public BarRow Pool => Subject is { } a
        ? ShowsMana
            ? new BarRow("charManaBar", Truncate(a.Stat[(int)ActorStat.Mana]),
                         Truncate(a.Stat[(int)ActorStat.ManaPool]))
            : new BarRow("charBloodBar", Truncate(a.Stat[(int)ActorStat.Blood]),
                         Truncate(a.Stat[(int)ActorStat.BloodPool]))
        : default;

    /// <summary>
    /// The bar under the pool: **frenzy** for everyone but a human, **faith** for a human. Both
    /// run against a literal 100 rather than against a stat of their own.
    /// </summary>
    public BarRow Secondary => Subject is { } a
        ? ShowsMana
            ? new BarRow("charFaithBar", Truncate(a.Stat[(int)ActorStat.Faith]), FixedMax)
            : new BarRow("charFrenzyBar", Truncate(a.Stat[(int)ActorStat.Frenzy]), FixedMax)
        : default;

    /// <summary>Humanity, also against a literal 100.</summary>
    public BarRow Humanity => Subject is { } a
        ? new BarRow("charHumanityBar", Truncate(a.Stat[(int)ActorStat.Humanity]), FixedMax)
        : default;

    /// <summary>
    /// The conditional controls that end up **shown**, everything else conditional being hidden.
    /// The `.nui` declares `charBloodBar` and `charManaBar` at the **same position**, and
    /// `charFrenzyBar` and `charFaithBar` likewise, so exactly one of each pair can be up.
    ///
    /// The mechanism is `SetActor`'s own, and it is hide-then-show rather than anything declared:
    /// `SetActor` opens by calling Hide over the advancement buttons and the two labels,
    /// and the update then clears the control's bit `0x200` on whichever ones apply. **The `.nui`'s
    /// own `flags=` is not what hides them** — the reader ORs that value straight into the same word,
    /// so `flags=0x08` on these controls is some other bit whose meaning has not been read. It
    /// correlates exactly with "conditional", which is suggestive and is not evidence.
    /// </summary>
    public IEnumerable<string> ShownControls()
    {
        yield return "charHealthBar";
        yield return "charHumanityBar";
        yield return ShowsMana ? "charManaBar" : "charBloodBar";
        yield return ShowsMana ? "charFaithBar" : "charFrenzyBar";
        if (ShowsMana) { yield return "charManaLabel"; yield return "charFaithLabel"; }
        if (ShowsClanBox) yield return "charClanBox";
        // Advancing: a raise button on every attribute that can be raised — shown whether or not
        // it can be afforded (lines 13–45).
        if (Advancing && Subject is { } a)
            foreach (var (stat, control) in RaiseButtons)
                if (Rpg.Advancement.Raisable(a, stat)) yield return control;
    }

    /// <summary>Whether the sheet is the advancement screen's left half (`vuiAdvancement_bOpen`).</summary>
    public bool Advancing { get; set; }

    /// <summary>Each raise button and the attribute it raises (`Init` writes the stat into the button).</summary>
    public static readonly (int Stat, string Control)[] RaiseButtons =
    [
        ((int)ActorStat.Strength, "advStrength"), ((int)ActorStat.Dexterity, "advDexterity"),
        ((int)ActorStat.Stamina, "advStamina"), ((int)ActorStat.Perception, "advPerception"),
        ((int)ActorStat.Intelligence, "advIntelligence"), ((int)ActorStat.Wits, "advWits"),
        ((int)ActorStat.Charisma, "advCharisma"), ((int)ActorStat.Manipulation, "advManipulation"),
        ((int)ActorStat.Appearance, "advAppearance"), ((int)ActorStat.Faith, "advFaith"),
    ];

    /// <summary>The attribute whose raise button is under a point in the pane's space, or −1.</summary>
    public int RaiseAt(float x, float y)
    {
        float px = Pane.Pos.Length > 1 ? Pane.Pos[0] : 0, py = Pane.Pos.Length > 1 ? Pane.Pos[1] : 0;
        foreach (var (stat, control) in RaiseButtons)
        {
            if (!Shown(control)) continue;
            if (Pane.Elements.FirstOrDefault(e => e.Name.Equals(control, StringComparison.OrdinalIgnoreCase))
                    is { Pos.Length: > 1, Size.Length: > 1 } e
                && x >= px + e.Pos[0] && x <= px + e.Pos[0] + e.Size[0] && y >= py + e.Pos[1] && y <= py + e.Pos[1] + e.Size[1])
                return stat;
        }
        return -1;
    }

    /// <summary>
    /// A raise button's rollover: nothing when it cannot be raised; else one line in the
    /// small box — "Cost to increase Strength: 120" — white when there is the experience for it and
    /// red when there is not.
    /// </summary>
    public RolloverLine? RaiseCost(int stat, Func<string, string?> find)
    {
        if (Subject is not { } a) return null;
        int cost = Rpg.Advancement.StatCost(a, stat);
        if (cost < 0) return null;
        string key = $"RPG_STAT_{((ActorStat)stat).ToString().ToUpperInvariant()}";
        string text = Vitomir.Engine.Ui.Strings.Format(find, "UI_ADVANCEMENT_STATCOST", $"%s{find(key) ?? key}%d{cost}");
        return new RolloverLine(text, a.SpendableExperience >= cost ? Rollover.White : DisciplinePane.Red);
    }

    /// <summary>Whether a conditional control is up this frame; anything unconditional is always up.</summary>
    public bool Shown(string name) =>
        !Conditional.Contains(name, StringComparer.OrdinalIgnoreCase)
        || ShownControls().Contains(name, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The controls `SetActor` hides before it decides. The advancement buttons are in the list
    /// because the engine hides all eleven of them on every refresh, and shows the ones that apply
    /// only while advancing.
    /// </summary>
    public static readonly string[] Conditional =
    [
        "charBloodBar", "charManaBar", "charFrenzyBar", "charFaithBar",
        "charManaLabel", "charFaithLabel", "charClanBox",
        "advStrength", "advDexterity", "advStamina", "advPerception", "advIntelligence",
        "advWits", "advCharisma", "advManipulation", "advAppearance",
        "advExperience", "advGeneration", "advFaith",
    ];

    /// <summary>
    /// Every text box on the pane and what it reads this frame, by the `.nui`'s own control names.
    /// A box the engine would have cleared is present with an empty string rather than absent, so a
    /// caller can tell "blank" from "not a box".
    /// </summary>
    public Dictionary<string, string> Texts()
    {
        var text = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["charName"] = Name,
            ["charClan"] = Clan,
            ["charStatus"] = Status,
            ["charGeneration"] = Generation,
            ["charExperience"] = Experience,
            ["charEarnedExp"] = EarnedExperience,
            ["charDamage"] = DamageRow.Text,
        };
        foreach (var row in Rows())
        {
            text[$"charBase{row.Stat}"] = row.Base.ToString();
            text[$"char{row.Stat}"] = row.Effective.ToString();
        }
        foreach (var row in Soaks())
        {
            // The three spelled-out rows are named for the *old* names of their damage types:
            // DAMAGE_TYPE_NORMAL is `charBash`, and that is the engine's own spelling.
            string name = row.Type == DamageType.Normal ? "Bash" : row.Type.ToString();
            text[$"charBase{name}"] = row.Base.ToString();
            text[$"char{name}"] = row.Effective.ToString();
        }
        foreach (var slice in Slices())
            text[$"charSoak{SliceName(slice.Type)}Value"] = slice.Value.ToString();

        foreach (var bar in new[] { ("Health", Health), ("Humanity", Humanity) })
        {
            text[$"char{bar.Item1}"] = bar.Item2.Value.ToString();
            text[$"charMax{bar.Item1}"] = bar.Item2.Max.ToString();
        }
        // The pool and the bar under it write into the *blood* and *frenzy* boxes whichever pair is
        // up: there is one set of numbers and two sets of bars, which is why a human's mana reads
        // out of `charBlood`. The engine does the same thing, into the same two cached pointers.
        text["charBlood"] = Pool.Value.ToString();
        text["charMaxBlood"] = Pool.Max.ToString();
        text["charFrenzy"] = Secondary.Value.ToString();
        text["charMaxFrenzy"] = Secondary.Max.ToString();
        if (Subject is null) foreach (var key in text.Keys.ToList()) text[key] = "";
        return text;
    }

    /// <summary>The name a soak wedge's controls are spelled with: `charSoakFire`, `pieFire`.</summary>
    public static string SliceName(DamageType type) => type switch
    {
        DamageType.Faith => "Holy",      // the pane calls it holy and the icon is `holyIcon`
        DamageType.Cold => "Ice",        // and this one ice, with `iceIcon`
        _ => type.ToString(),
    };

    /// <summary>Each bar control and how full it is, 0 to 1 — only the ones that are up.</summary>
    public Dictionary<string, float> Bars()
    {
        var bars = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        if (Subject is null) return bars;
        foreach (var bar in new[] { Health, Pool, Secondary, Humanity })
            if (bar.Bar.Length > 0) bars[bar.Bar] = bar.Fraction;
        return bars;
    }

    /// <summary>
    /// The picture each image control is showing: a wedge's frame out of its own `sliceprefix`,
    /// and the damage icon for whatever the equipped weapon deals. The clan crest is **not** here —
    /// the engine registers those from the clan list at startup (`Init` walks it), so the bitmap
    /// comes from the clan record rather than from this pane, and <see cref="ClanImage"/> is where
    /// whoever owns the pane puts it.
    /// </summary>
    /// <summary>
    /// The modern-day art (the pane's loader keeps its flag): the soak
    /// pie's frames are `%s%dMD` rather than `%s%d` — `pieFire3MD`.
    /// </summary>
    public bool ModernDay { get; set; }

    public Dictionary<string, string> Images()
    {
        var images = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (Subject is null) return images;
        foreach (var slice in Slices())
            if (Control($"charSoak{SliceName(slice.Type)}")?.Get("sliceprefix") is { } prefix)
                images[$"charSoak{SliceName(slice.Type)}"] = prefix + slice.Frame + (ModernDay ? "MD" : "");
        images["charDamageIcon"] = DamageIcons.First(i => i.Type == DamageRow.Type).Icon;
        if (ClanImage is { Length: > 0 } crest) images["charClanBox"] = crest;
        return images;
    }

    /// <summary>
    /// The clan crest's bitmap, out of the clan record; null draws nothing. Set it outright, or
    /// give the pane <see cref="ClanIcons"/> and it follows the subject's clan.
    /// </summary>
    public string? ClanImage
    {
        get => clanImage ?? (ShowsClanBox && ClanIcons is { } icons ? icons(Clan) : null);
        set => clanImage = value;
    }
    string? clanImage;

    /// <summary>
    /// Clan name to crest, which is the second column of `misc/game.ncd` — the clan list the engine
    /// reads at start-up and whose icons `vuiChar::Init` registers with
    /// the clan box, one per clan. Nothing supplied one until then, so the box was
    /// always empty; the base game's sheet has the Brujah crest in it.
    /// </summary>
    public Func<string, string?>? ClanIcons { get; set; }

    /// <summary>
    /// Reads the clan list's icon column: `name icon beckon shape flags …`, `;`, `#` and `//`
    /// comment lines skipped as the clan file's reader skips them, `none` meaning no icon.
    /// </summary>
    public static Dictionary<string, string> ClanIconsFrom(string ncd)
    {
        var icons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in ncd.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is ';' or '#' || line.StartsWith("//")) continue;
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts[1].Equals("none", StringComparison.OrdinalIgnoreCase)) continue;
            icons[parts[0]] = parts[1];
        }
        return icons;
    }

    /// <summary>
    /// What the generation box reads. A vampire gets the number; a ghoul and a human get a word
    /// through the string table; a monster gets nothing, because the dispatch falls through to the
    /// control's own Clear.
    /// </summary>
    public string Generation => Subject switch
    {
        null => "",
        { Type: ActorType.Vampire } a => Truncate(a.Stat[(int)ActorStat.Generation]).ToString(),
        { Type: ActorType.Ghoul } => Strings(GhoulKey),
        { Type: ActorType.Human } => Strings(HumanKey),
        _ => "",
    };

    /// <summary>
    /// The clan box. **A human never shows one**, and neither does anyone whose clan record is
    /// null — `SetActor` tests for type 2 or no clan record and clears the box for both.
    /// </summary>
    public string Clan => Subject is { Type: not ActorType.Human, Clan.Length: > 0 } a ? a.Clan : "";

    /// <summary>Whether the clan crest is up. Same test as <see cref="Clan"/>.</summary>
    public bool ShowsClanBox => Clan.Length > 0;

    /// <summary>The name, which is whatever the actor is called on screen.</summary>
    public string Name => Subject is { } a ? (a.Name.Length > 0 ? a.Name : a.CastId) : "";

    /// <summary>
    /// Status, experience and the unspent pool are filled **only for a `THING_TYPE_PLAYER`**; for
    /// anyone else all three boxes are cleared outright. That is why an ordinary townsperson's
    /// sheet has a blank top right corner. 1.1 refreshes these three boxes on an experience gain
    /// if the open sheet shows that player; Vitomir's read live every
    /// frame instead, to the same effect.
    /// </summary>
    public bool ShowsProgress => Subject?.IsPlayerThing == true;

    /// <summary>
    /// The rank title, through the string table. The rank itself is arithmetic on **lifetime**
    /// experience (the rank ladder walks its table reading the lifetime experience), so spending
    /// cannot demote anyone.
    /// </summary>
    public string Status => ShowsProgress && Subject is { } a ? Strings(RankKey(a)) : "";

    /// <summary>The lifetime figure, which is the one the rank is computed from.</summary>
    public string Experience => ShowsProgress && Subject is { } a ? a.Experience.ToString() : "";

    /// <summary>
    /// The **unspent** pool — and the box is called `charEarnedExp`, which reads
    /// like the opposite. The names are the engine's and they are misleading: `charExperience`
    /// takes the field the rank ladder walks, so that is the lifetime total, and
    /// `charEarnedExp` takes the other one. It is drawn in the pane's yellow with the `advExperience`
    /// button beside it, which is what a spendable pool wants and a lifetime total does not.
    /// </summary>
    public string EarnedExperience => ShowsProgress && Subject is { } a ? a.SpendableExperience.ToString() : "";

    /// <summary>
    /// The weapon damage row: the number, and which icon sits beside it. With nothing equipped the
    /// box is blank and the icon falls back to index 0. Written when the weapon is set (by the
    /// weapon's set).
    /// </summary>
    public (string Text, DamageType Type) DamageRow => Weapon is { } w
        ? (Truncate(w.Amount).ToString(), w.Type)
        : ("", DamageType.Normal);

    /// <summary>The lifetime experience each rank needs, from the rank table.</summary>
    public static readonly int[] RankThresholds = [0, 5_000, 10_000, 50_000, 200_000];

    /// <summary>`ACTOR_MAXRANKS`, asserted by the original.</summary>
    public const int MaxRanks = 5;

    /// <summary>
    /// Which rank a lifetime experience total earns: the highest threshold it has reached, capped
    /// at <see cref="MaxRanks"/> − 1.
    /// </summary>
    public static int Rank(long lifetime)
    {
        int rank = 0;
        var thresholds = Vitomir.Engine.Rules.GameRules.Current.Ranks.RankThresholds;
        for (int i = 1; i < thresholds.Length; i++)
            if (lifetime >= thresholds[i]) rank = i;
        return rank;
    }

    /// <summary>
    /// The rank titles, as the rank table spells them: a row per rank, a 44-byte cell per
    /// `ACTOR_TYPE` (name, then the rank's threshold and two more numbers). Ghouls and humans are
    /// numbered and **kindred are named** — which is why the sheet read `UI_STATUS_VAMPIRE0` until
    /// When this was a pattern and not the table: the key it built for a vampire does
    /// not exist. A base-game screenshot has Christof at 5,345 as a *Neonate*.
    /// The monster column is blank in every row, so a monster has no title at all.
    /// </summary>
    public static readonly string[,] RankKeys =
    {
        { "UI_STATUS_FLEDGELING", "UI_STATUS_GHOUL0", "UI_STATUS_HUMAN0", "" },
        { "UI_STATUS_NEONATE", "UI_STATUS_GHOUL1", "UI_STATUS_HUMAN1", "" },
        { "UI_STATUS_ANCILLAE", "UI_STATUS_GHOUL2", "UI_STATUS_HUMAN2", "" },
        { "UI_STATUS_ELDER", "UI_STATUS_GHOUL3", "UI_STATUS_HUMAN3", "" },
        { "UI_STATUS_METHUSELAH", "UI_STATUS_GHOUL4", "UI_STATUS_HUMAN4", "" },
    };

    /// <summary>The localisation key for a rank title (`table[(type + rank × 4) × 0x2c]`).</summary>
    public static string RankKey(Actor actor) =>
        (int)actor.Type is >= 0 and < 4 ? RankKeys[Rank(actor.Experience), (int)actor.Type] : "";

    /// <summary>A control by name, or null — the pane declares 60-odd and not all are used.</summary>
    public NuiElement? Control(string name) =>
        Pane.Elements.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
}
