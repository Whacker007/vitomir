// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What the game tells you about the thing under the pointer. The item rollover,
// which the UI update calls at the tail of its hover block with the hovered thing as `this` — so it
// runs once whenever the hover *changes*, not every frame.
//
// **It is not the status bar.** The status bar object owns the two pointers and the timing, and the
// text goes into two floating panes above it: `ui/descSmall.nui` at 160,93 — one line, `morpheus15`,
// three rows at most — and `ui/descLargeSP.nui` at 160,2, seven lines of detail in `defaultFont`.
// The small box carries **line 0, the name**, in a colour that means something; the large one
// carries lines 1..7 and only appears after a **1.5-second** hover, and only over an item.
//
// This file is both: line 0 (`Hint`, `Name`) and the detail block (`Detail`, `Weapon`, `Effects`,
// `Restrictions`). The `.nls` keys resolve through `Ui.Strings` and the use test is
// `Rpg.Equipment.Allows`; what is still missing is the **treasure modifier list** (the red
// "you cannot use this" tint on the name line is built).
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

/// <summary>One row of a rollover box, with the colour the engine writes it in (`0xAARRGGBB`).</summary>
public readonly record struct RolloverLine(string Text, uint Colour);

/// <summary>The rollover description, as far as it is written.</summary>
public static class Rollover
{
    /// <summary>The two panes and the textbox they both name.</summary>
    public const string SmallPane = "descSmall", LargePane = "descLargeSP", Box = "descText";

    /// <summary>
    /// How long a hover has to last before the large box joins the small one (the status
    /// bar arms its timer at `now + 1500`), and how long both take to fade (0.3).
    /// </summary>
    public const int LargeAfterMs = 1500;
    /// <inheritdoc cref="LargeAfterMs"/>
    public const float FadeSeconds = 0.3f;

    public const uint White = 0xFFFFFFFFu;
    /// <summary>The blue a **magic** item (`item_flags` `0x1000`) is named in: `0xff317ff4`.</summary>
    public const uint Magic = 0xFF317FF4u;

    /// <summary>
    /// **A control's own hint**, which is the other thing these boxes show and reaches them by a
    /// different door. `hintstring` is a `.nui` attribute the pane parser resolves through the
    /// string table **at parse time** (the parser stores the answer on the control), and the
    /// dispatcher copies it straight into the **small box** with no formatting: a
    /// control with a rollover callback goes to the rpg rollover, a control with only a hint does
    /// not. So `Toggle Inventory Pane` and `Increase Blood +10` arrive in the same box by
    /// completely different routes, and a hint never arms the large one.
    /// </summary>
    public static RolloverLine? Hint(NuiElement? control, Strings? text = null)
    {
        if (control?.Get("hintstring") is not { Length: > 0 } key) return null;
        string said = Say(key, key, text);
        return said.Length == 0 ? null : new RolloverLine(said, White);
    }

    /// <summary>
    /// **The name line.** Null means the pointer's thing has nothing to say and neither box
    /// appears — which the engine's own type switch decides by type: a region and a
    /// projectile go straight to the exit, and everything this tree can point at is covered here.
    ///
    /// <list type="bullet">
    /// <item>a **character** is named and **tinted by its health** — the engine packs
    /// `0xffff<v><v>` where <c>v</c> is <c>health / (maxHealth + 1) x 255</c> truncated, so a whole
    /// character is white and a nearly dead one is red, and the bar is not the only place you can
    /// read it;</item>
    /// <item>an **item** shows its `ITEM_IDENTIFIEDDESC` once identified and its plain description
    /// until then, and a **magic** one (`item_flags` `0x1000`) is named in blue;</item>
    /// <item>a **prop** and a **container** are named plainly.</item>
    /// </list>
    ///
    /// An item the character being steered (<paramref name="user"/>) **cannot use** is
    /// named in red, <see cref="Unusable"/> — the use test whole, which is <c>Rpg.Equipment.CanUse</c> —
    /// ahead of the magic blue. Without a user nobody is asked and nothing is red.
    ///
    /// A prop's owner is a second line of the small box, <see cref="Owner"/>.
    /// </summary>
    public static RolloverLine? Name(WorldThing? thing, Strings? text = null, WorldThing? user = null)
    {
        if (thing is null) return null;
        switch (thing.Type)
        {
            case ThingType.Actor:
            case ThingType.Player:
                return thing.Actor is { } who
                    ? new RolloverLine(NameOf(thing, text), HealthTint(who))
                    : null;

            case ThingType.Item:
            case ThingType.Weapon:
            case ThingType.Armor:
            {
                var item = thing.Item;
                string key = item is not null && (item.Flags & ItemFlags.Identified) != 0
                             && item.IdentifiedDescriptionId.Length > 0
                    ? item.IdentifiedDescriptionId : thing.DescriptionId;
                uint colour = item is not null && user is not null && !Rpg.Equipment.CanUse(user, item) ? Unusable
                            : item is not null && (item.Flags & ItemFlags.Magic) != 0 ? Magic : White;
                return new RolloverLine(Say(key, thing.Template, text), colour);
            }

            case ThingType.Prop:
            case ThingType.Container:
                return new RolloverLine(NameOf(thing, text), White);

            default:
                return null;
        }
    }

    /// <summary>
    /// **A prop's owner**, the small box's second line (the rollover's prop branch): after the
    /// prop's name, when its owner is set (<see cref="WorldThing.PropOwner"/>), **a network game is up**
    /// (<c>*</c>) and the GUID finds a thing (<see cref="PropOwners.Find"/>): that
    /// thing's name (<see cref="NameOf"/> — a player's character by its player's name) as `(%s)`,
    /// added in the box's own colour — `descText` sets no `tbtextcolor`, so
    /// white. The only props with an owner are a teleporter pair's ends, so it reads *(Anna)* under
    /// the portal Anna opened. Null for anything else, and in single player.
    /// </summary>
    public static RolloverLine? Owner(WorldThing? thing, Strings? text = null)
    {
        // 1.1's word is the game's; Vitomir's mode is a place's, and a host's place nobody has entered yet (a portal's
        // far end's) is the network game's through its teleporters (ours).
        if (thing is not { Type: ThingType.Prop, PropOwner: not 0 }
            || (Net.NetOrders.Mode(thing.Place) == Net.NetMode.Single && thing.Place?.Session?.Teleports.Network is null)) return null;
        return PropOwners.Find(thing) is { } owner ? new RolloverLine($"({NameOf(owner, text)})", White) : null;
    }

    /// <summary>
    /// **The detail block** — lines 1..7, which go in the large box. Empty for everything that is
    /// not an item, weapon or armour: the rollover's character branch writes the name and `break`s with
    /// its line count still zero, so **hovering a person in the world gets a name and nothing
    /// else**. (The `Health: … Blood: … Frenzy: …` line a party member shows is a different
    /// function altogether — the row hotspot's own rollover, and it
    /// goes in the *small* box.)
    ///
    /// The order is the engine's, and so is every format string:
    /// <list type="number">
    /// <item>**not identified** — `RPG_NOTIDENTIFIED`, and nothing after it;</item>
    /// <item>**a count**, when an *item* (type 1 — not a weapon or armour) has one: `RPG_CASH` for
    /// an `INSTANTCASH` item, `RPG_WPNAMMO` for ammo, `RPG_CHARGES` otherwise — with the engine's
    /// own trailing space. A thrown weapon's count is its ammo line, not a count line;</item>
    /// <item>**armour's soak**, on a line of its own ahead of the rest;</item>
    /// <item>**a weapon's numbers**: `Damage: n (Type)`, then `Accuracy: n  Speed: n  1H  Silvered`
    /// — accuracy first;</item>
    /// <item>**its ammo**, for a ranged one;</item>
    /// <item>**its effects**, with `ITEM_SPECIALDESC` ahead of them when it has one;</item>
    /// <item>**its restrictions**.</item>
    /// </list>
    /// **Every counted line is a row, empty or not.** The rollover writes armour's soak line, the effects
    /// line and the restrictions line **whatever they hold**, counts them (<c>lines + 2</c>)
    /// and hands every counted line to the large box (the textbox's add), and the
    /// box keeps an empty one as a row: the add gives "" no text and kind 3, the measure makes a kind-3
    /// item one line tall (the font's first field, <c>**</c>), the layout stacks it
    /// `itemSpacing` under the one before and centres the block on it, and only the draw
    /// finds nothing on it to write. So a Dark Ages sword is **four rows** — its two number
    /// lines, the empty effects line and the restrictions line of two spaces (<see cref="Restrictions"/>:
    /// `_weapon_DA`'s two zero-valued stat rows, "" each, with their separator kept) — and a torch, a
    /// key or a letter, whose effects and restrictions lines are both empty, is **two blank rows**, a
    /// large box of nothing on its black backdrop over the party portraits. That is what 1.1 shows of
    /// the Torch in the pack (`descLargeSP` up the whole hover,
    /// no words in it; the portraits hidden). Vitomir once left exactly-empty lines out.
    /// (For an item flagged `CANNOT BE IDENTIFIED` and identified all the same, the description answers NULL
    /// and those lines read `(null)`; no shipped template carries the flag.)
    /// </summary>
    public static IReadOnlyList<RolloverLine> Detail(WorldThing? thing, Strings? text = null,
                                                     NedFile? effects = null)
    {
        if (thing?.Item is not { } item) return [];
        if (thing.Type is not (ThingType.Item or ThingType.Weapon or ThingType.Armor)) return [];
        var lines = new List<RolloverLine>();

        if ((item.Flags & ItemFlags.Identified) == 0)
        {
            lines.Add(new RolloverLine(Say("RPG_NOTIDENTIFIED", "", text), White));
            return lines;
        }

        // `"%s %d "`, trailing space and all, three copies of the same format — in the item branch
        // only (thing type 1).
        if (thing.Type == ThingType.Item && item.Amount > 0)
        {
            string key = (item.Flags & ItemFlags.InstantCash) != 0 ? "RPG_CASH"
                       : item.Type == ItemType.Ammo ? "RPG_WPNAMMO" : "RPG_CHARGES";
            lines.Add(new RolloverLine($"{Say(key, key, text)} {item.Amount} ", White));
        }

        // Armour's soak comes out first and on its own, always a line; everything
        // else then leaves it out.
        bool armour = thing.Type == ThingType.Armor;
        if (armour) lines.Add(new RolloverLine(Effects(item, effects, text, SoakOnly), White));

        if (thing.Type == ThingType.Weapon && item.Weapon?.Attacks is { Length: > 0 } attacks)
            Weapon(attacks[0], item, text, lines);

        // The effects line and the restrictions line are lines whatever they hold — "" is a blank row.
        lines.Add(new RolloverLine(EffectsLine(item, effects, text, armour), White));
        lines.Add(new RolloverLine(Restrictions(item, text), White));
        return lines;
    }

    /// <summary>
    /// **The effects line**: with an `ITEM_SPECIALDESC`
    /// (the shelf record's special description) it is `"%s  %s"` — the special description, two
    /// spaces and the effects, the spaces there even when the effects are "" — else the
    /// effects alone (`"%s"`).
    /// </summary>
    static string EffectsLine(Item item, NedFile? effects, Strings? text, bool armour)
    {
        string rest = Effects(item, effects, text, armour ? ExceptSoak : Everything);
        return item.SpecialDescriptionId.Length > 0 ? $"{Say(item.SpecialDescriptionId, "", text)}  {rest}" : rest;
    }

    /// <summary>
    /// The two number lines a weapon shows, from its **first** attack block. The engine really does
    /// force that: it writes 0 into the live mode selector while it composes and puts
    /// the saved value back afterwards, so a rollover on a two-mode weapon mutates game state for
    /// the length of a `sprintf`. We simply read block 0 instead.
    ///
    /// **`Speed` is not `WEAPON_STRIKETIME`.** It is attacks per hundred seconds from
    /// `WEAPON_RELOADTIME`, <see cref="Speed"/>: a rapier with a 0.9-second reload shows **111**, and
    /// one with a 1.0-second reload **99**, not 100.
    /// </summary>
    static void Weapon(Rpg.WeaponAttack attack, Item item, Strings? text, List<RolloverLine> lines)
    {
        string type = Say("RPG_DAMAGE_" + attack.DamageType.ToString().ToUpperInvariant(), "", text);
        lines.Add(new RolloverLine($"{Say("RPG_WPNDAMAGE", "RPG_WPNDAMAGE", text)} {attack.Damage} ({type})", White));

        int speed = Speed(attack.ReloadTime);
        string hands = (item.Flags & ItemFlags.TwoHanded) != 0 ? "2H" : "1H";
        string silver = attack.Is(Rpg.WeaponFlag.Silver) ? Say("RPG_WPNSILVERED", "", text) : " ";
        lines.Add(new RolloverLine(
            $"{Say("RPG_WPNACCURACY", "RPG_WPNACCURACY", text)} {(int)attack.Accuracy}  " +
            $"{Say("RPG_WPNSPEED", "RPG_WPNSPEED", text)} {speed}  {hands}  {silver}", White));

        if (!attack.Is(Rpg.WeaponFlag.Ranged)) return;
        int ammo = attack.Is(Rpg.WeaponFlag.Thrown) ? item.Amount
                 : attack.MaxAmmo != 0 ? attack.Ammo : 0;
        lines.Add(new RolloverLine($"{Say("RPG_WPNAMMO", "RPG_WPNAMMO", text)} {ammo}", White));
    }

    /// <summary>
    /// **A weapon's `Speed`**, as both rollovers compute it (in the item's and on the
    /// shelf): the reload in milliseconds as a 64-bit integer, times the single-precision
    /// constant 0.001 (0x3a83126f — 0.0010000000475), 100 divided by that, the
    /// quotient stored to a **float**, and truncated. The product and the quotient are worked
    /// at more than single precision, as 1.1 does — the reload times the float 0.001 is exact there,
    /// a little over the whole seconds — so a 1000 ms reload gives 99.999995, which rounds to the
    /// float 99.99999237 and truncates to **99**: 1.1 shows the Scimitar and the Rapier at 99. A
    /// product rounded to a float first would be exactly 1.0 and give 100, which is what
    /// `100000 / ms` once gave. Every speed 1.1 shows fits this (99, 111, 117, 90,
    /// 85, 83, 76, 75). A zero reload divides to infinity, which converts to the 64-bit
    /// integer indefinite, whose low half — all the conversion returns — is 0.
    /// </summary>
    public static int Speed(int reloadMs)
    {
        if (reloadMs == 0) return 0;
        double quotient = 100.0 / ((uint)reloadMs * (double)0.001f);
        return (int)(float)quotient;
    }

    /// <summary>
    /// **A shop's shelf item** — the item rollover's item branch fed from the shop's
    /// treasure record rather than from a thing, with the same format strings and colours. Where it
    /// differs from <see cref="Name"/> and <see cref="Detail"/>, it differs on purpose-built fields
    /// of the record:
    /// <list type="bullet">
    /// <item>the name is red when the trader **could not use it** — by the record's restrictions
    /// alone, whose maximum test is the wrong way up (fails when the stat is *below*
    /// |v|), so on a shelf an item for the low of Humanity reads red to the humane;</item>
    /// <item>a weapon's ranged and silvered bits are the record's `WEAPON_FLAGS1` alone
    /// (<see cref="Rpg.Weapon.Flags1"/>), not the weapon's first attack's flags, so a
    /// bow ranged by `WEAPON_FLAGS` has no ammo line — the Short Bow on 1.1's shelf reads
    /// `Damage: 30 (Lethal)` / `Accuracy: 5  Speed: 75  2H  ` /
    /// `Min 25 Dexterity  `;</item>
    /// <item>a weapon's ammo line, when there is one, shows its capacity (`WEAPON_MAXAMMO1`), not a magazine;</item>
    /// <item>the not-silvered filler is empty, so the second weapon line ends in two spaces, not three;</item>
    /// <item>and **every line is always there** — the effects line and the restrictions line are
    /// added when they are empty, each a blank row of the large box, as the pack's are
    /// (<see cref="Detail"/>).</item>
    /// </list>
    /// </summary>
    public static (RolloverLine Name, IReadOnlyList<RolloverLine> Detail) Treasure(
        Rpg.TreasureRecord t, WorldThing? trader, Strings? text = null, NedFile? effects = null)
    {
        var item = t.Item;
        var lines = new List<RolloverLine>();
        bool identified = (item.Flags & ItemFlags.Identified) != 0;
        if (!identified)
        {
            // Unreachable from a shop, which identifies everything it stocks.
            lines.Add(new RolloverLine(Say("RPG_NOTIDENTIFIED", "", text), White));
            return (new RolloverLine(Say(item.DescriptionId, t.Template, text), White), lines);
        }
        string key = item.IdentifiedDescriptionId.Length > 0 ? item.IdentifiedDescriptionId : item.DescriptionId;
        uint colour = trader?.Actor is { } who && !ShelfAllows(who, item) ? Unusable
                    : (item.Flags & ItemFlags.Magic) != 0 ? Magic : White;
        var name = new RolloverLine(Say(key, t.Template, text), colour);

        var kind = item.ThingType;
        if (kind == ThingType.Item && item.Amount > 0)
        {
            string count = (item.Flags & ItemFlags.InstantCash) != 0 ? "RPG_CASH"
                         : item.Type == ItemType.Ammo ? "RPG_WPNAMMO" : "RPG_CHARGES";
            lines.Add(new RolloverLine($"{Say(count, count, text)} {item.Amount} ", White));
        }
        if (kind == ThingType.Weapon && item.Weapon?.Attacks is { Length: > 0 } attacks)
        {
            var a = attacks[0];
            string type = Say("RPG_DAMAGE_" + a.DamageType.ToString().ToUpperInvariant(), "", text);
            lines.Add(new RolloverLine($"{Say("RPG_WPNDAMAGE", "RPG_WPNDAMAGE", text)} {a.Damage} ({type})", White));
            int speed = Speed(a.ReloadTime);
            string hands = (item.Flags & ItemFlags.TwoHanded) != 0 ? "2H" : "1H";
            // The record's flags are `WEAPON_FLAGS1` alone.
            uint flags = item.Weapon.Flags1;
            string silver = (flags & Rpg.WeaponFlag.Silver) != 0 ? Say("RPG_WPNSILVERED", "", text) : "";
            lines.Add(new RolloverLine(
                $"{Say("RPG_WPNACCURACY", "RPG_WPNACCURACY", text)} {(int)a.Accuracy}  " +
                $"{Say("RPG_WPNSPEED", "RPG_WPNSPEED", text)} {speed}  {hands}  {silver}", White));
            if ((flags & Rpg.WeaponFlag.Ranged) != 0)
                lines.Add(new RolloverLine(
                    $"{Say("RPG_WPNAMMO", "RPG_WPNAMMO", text)} {((flags & Rpg.WeaponFlag.Thrown) != 0 ? item.Amount : a.MaxAmmo)}", White));
        }
        bool armour = kind == ThingType.Armor;
        if (armour) lines.Add(new RolloverLine(Effects(item, effects, text, SoakOnly), White));
        lines.Add(new RolloverLine(EffectsLine(item, effects, text, armour), White));
        lines.Add(new RolloverLine(Restrictions(item, text), White));
        return (name, lines);
    }

    /// <summary>The "cannot use it" red of a name line.</summary>
    public const uint Unusable = 0xFFFF0000u;

    /// <summary>
    /// The shelf's usability test: the restrictions and nothing else, with the maximum
    /// compare inverted against the owned item's (the shelf tests for "not below" where the
    /// owned item tests for "above"). A creature-type restriction is tested the ordinary way.
    /// </summary>
    public static bool ShelfAllows(Actor who, Item item)
    {
        foreach (var r in item.Restrictions)
        {
            bool ok = r.Type switch
            {
                RestrictionType.Vampire => who.Type == ActorType.Vampire,
                RestrictionType.Ghoul => who.Type == ActorType.Ghoul,
                RestrictionType.Human => who.Type == ActorType.Human,
                RestrictionType.NoVampire => who.Type != ActorType.Vampire,
                RestrictionType.NoGhoul => who.Type != ActorType.Ghoul,
                RestrictionType.NoHuman => who.Type != ActorType.Human,
                RestrictionType.Health => who.Health >= r.Value,
                _ => StatOf(who, r.Type) is not { } stat || (r.Value >= 0f ? stat >= r.Value : !(-stat > r.Value)),
            };
            if (!ok) return false;
        }
        return true;
    }

    static float? StatOf(Actor who, RestrictionType type) => type switch
    {
        >= RestrictionType.Strength and <= RestrictionType.BloodPool => who.Stat[(int)type - 1],
        RestrictionType.Mana => who.Stat[(int)ActorStat.Mana],
        RestrictionType.ManaPool => who.Stat[(int)ActorStat.ManaPool],
        _ => null,
    };

    /// <summary>Which of an item's effects a pass wants: all of them, only the armour soak, or the rest.</summary>
    public const int Everything = 0, SoakOnly = 1, ExceptSoak = 2;

    /// <summary>
    /// An item's effects, joined by **two spaces**, in the order the engine renders them — which is
    /// the **reverse** of the template's slot order, because the setter prepends each record. An
    /// effect flagged `NoRollover` renders as nothing **and still takes its separator**, so a hidden
    /// effect leaves a double gap; that is the engine's, not ours.
    ///
    /// The number is the engine's too: `mult x value`, where the multiplier is the record's own
    /// duration divided by the effect's tick interval when both are set — so an over-time effect
    /// shows what it will pay out **in total** — and the value is the item's own when the level is
    /// −1, or that level's column of `effects.ned` otherwise. `ShowValue` picks a decimal, and
    /// `Percentage` on top of it multiplies by a hundred.
    /// </summary>
    public static string Effects(Item item, NedFile? effects, Strings? text, int want)
    {
        if (effects is null) return "";
        var parts = new List<string>();
        foreach (var slot in item.Effects.AsEnumerable().Reverse())
        {
            if (effects[slot.Name] is not { } definition) continue;
            bool soak = definition.Kind == EffectKind.Soak && definition.Subtype < 2;
            if (want == SoakOnly && !soak) continue;
            if (want == ExceptSoak && soak) continue;
            parts.Add(definition.Has(EffectFlags.NoRollover) ? "" : One(slot, definition, text));
        }
        return string.Join("  ", parts);
    }

    /// <summary>One rendered effect: its name and its number, in whichever of the three shapes it takes.</summary>
    static string One(ItemEffect slot, NedEffect definition, Strings? text)
    {
        int mult = slot.TimeMs != 0 && definition.TickMs != 0 ? slot.TimeMs / definition.TickMs : 1;
        float value = mult * (slot.Level == -1 ? slot.Value ?? 0f : definition.At(slot.Level));
        string name = Say(definition.Name, definition.Name, text);
        if (!definition.Has(EffectFlags.ShowValue)) return $"{name} {(int)value:+#;-#;+0}";
        return definition.Has(EffectFlags.Percentage)
            ? $"{name} {(int)(value * 100f)}%"
            : $"{name} {value:0.00}";
    }

    /// <summary>
    /// An item's restrictions, joined the same way and reversed the same way (one line's own shaping
    /// is the restriction's format). A **creature-type** restriction — the ones that carry no value — is a string on
    /// its own (`Non-Human only`); a **stat** one is wrapped in `Min %V %S` or `Max %V %S` around the
    /// **absolute** value, so a −3 reads as `Max 3`, not `Max -3`. The join is the rollover's (and the
    /// shelf's): after each row but the last, two spaces — **whatever
    /// the row rendered as**, so a row that renders as nothing still keeps its separator.
    /// </summary>
    public static string Restrictions(Item item, Strings? text)
    {
        var parts = new List<string>();
        foreach (var bar in item.Restrictions.AsEnumerable().Reverse())
        {
            if (bar.Type == RestrictionType.None) continue;
            string key = "RPG_RESTRICT_" + bar.Type switch
            {
                RestrictionType.Vampire => "VAMPIRE", RestrictionType.Ghoul => "GHOUL",
                RestrictionType.Human => "HUMAN", RestrictionType.NoVampire => "NOVAMPIRE",
                RestrictionType.NoGhoul => "NOGHOUL", RestrictionType.NoHuman => "NOHUMAN",
                _ => bar.Type.ToString().ToUpperInvariant(),
            };
            // **The split is on the value, not the type**. A zero value takes the
            // creature-type switch, whose cases are only the six creature restrictions — so a *stat*
            // restriction carrying zero falls through its default and returns the buffer the function
            // cleared on entry: "", **with its two spaces kept** by the join. Every Dark
            // Ages weapon inherits two such rows from `_weapon_DA` (Strength and Dexterity, both 0), so
            // a Broadsword's restrictions are "" + "  " + "" — two spaces — and a Warhammer's, whose
            // Strength row is 55, `  Min 55 Strength`, as 1.1 shows them.
            if (bar.Value == 0f)
            {
                parts.Add(bar.Type is >= RestrictionType.Vampire and <= RestrictionType.NoHuman ? Say(key, key, text) : "");
                continue;
            }
            string wrapper = Say(bar.Value > 0 ? "RPG_RESTRICT_MINSTAT" : "RPG_RESTRICT_MAXSTAT", "", text);
            parts.Add(wrapper.Replace("%V", ((int)Math.Abs(bar.Value)).ToString())
                             .Replace("%S", Say(key, key, text)));
        }
        return string.Join("  ", parts);
    }

    /// <summary>
    /// What a thing is called: its **`DESCRIPTIONID` resolved through the string
    /// table** when it has one, and its template name when it does not. `DESCRIPTIONID` is
    /// template tag 82 and the engine resolves it **once, at template time** (the template keeps the
    /// key and the answer), which is why a thing carries both.
    /// **In a network game** (the session's flag or the lobby's set) **a player's
    /// character** (type 7) **goes by its player's name** when it has one — the name the
    /// player gave it on the create screen, which goes with it into the game (on
    /// the other machines) — so the world's
    /// rollover, the messages and the party rows all say *Anna*, not the model's description.
    /// </summary>
    public static string NameOf(WorldThing thing, Strings? text = null) =>
        thing is { Type: ThingType.Player, Actor.Name.Length: > 0 } && Net.NetOrders.Mode(thing.Place) != Net.NetMode.Single
            ? thing.Actor!.Name
            : Say(thing.DescriptionId, thing.Template, text);

    /// <summary>
    /// A key through the string table, or the key itself when nothing claims it — which is the
    /// `.nls` lookup's own miss behaviour, and is why an unlocalised thing
    /// reads as `OLDMAN` rather than as nothing at all.
    /// </summary>
    static string Say(string key, string fallback, Strings? text) =>
        key.Length == 0 ? fallback : text is null ? key : text[key];

    /// <summary>
    /// The health tint, exactly as the rollover packs it: `0xffff<v><v>`, `v = health / (max + 1) x
    /// 255` truncated. **Red stays at full** — only green and blue come down — so the ramp runs
    /// white to red through pink rather than through grey.
    /// </summary>
    public static uint HealthTint(Actor who)
    {
        float share = who.Health / (who.MaxHealth + 1f) * 255f;
        uint v = (uint)Math.Clamp((int)share, 0, 255);
        return 0xFFFF0000u | (v << 8) | v;
    }
}
