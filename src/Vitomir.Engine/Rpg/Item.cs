// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What a character is carrying.
//
// An inventory is a **doubly-linked list threaded through the items themselves**, and the engine's
// own assert strings name the fields: `pItem->pOwner`, `pItem->pNextInInventory`
// and `pItem->pPrevInInventory`, with the head on the character. The inventory
// adds one by pushing it on the **front** and its removal relinks both neighbours to remove
// one; the gameActor constructor zeroes the head. So **the list is in reverse order of
// acquisition** and nothing sorts it — which matters, because the weapon search below takes the
// first match. The prev pointer is why the script layer has a `GetPrevInventoryItem` as well as a
// `GetNextInventoryItem`.
//
// The field meanings come from how the reload uses them, which is unambiguous, plus the
// shipped data. Mapping each one to the template tag that fills it is **not** settled: the obvious
// name matches in the engine's tag table are not confirmed by the applier, and one of them was
// actively wrong — the slot mask is written by the inventory code with a caller-supplied value, not by
// `item_flags`, so it is runtime state rather than a template field.
using Vitomir.Formats;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// `item_type` (tag 20). The numbering is read off the shipped templates rather than
/// from any name table: every item inherits its type from one of a handful of base templates, so
/// only 98 of the 2,619 templates declare one at all and each value has an unambiguous family
/// behind it. <see cref="Ammo"/> is the one the weapon code tests for by number.
/// </summary>
public enum ItemType
{
    /// <summary>Potions, the cellphone, the flashlight — anything with no slot of its own.</summary>
    General = 0,
    Weapon = 1,
    Headgear = 2,
    Shield = 3,
    Rings = 4,
    Neckwear = 5,
    Handwear = 6,
    Quest = 7,
    Armor = 8,
    /// <summary>The only type the weapon code names: `_ammo_MD`, `_ammo_DA`, `_ammo_MP` and their children.</summary>
    Ammo = 9,
    /// <summary>Held rather than worn — `holyStatuette`, `martyrFire`.</summary>
    Utility = 10,
    Scroll = 11,
    Textbook = 12,
    Text = 13,
}

/// <summary>
/// Where an item is: the slot mask, which the inventory code sets and nothing loads
/// from a tag. The nine wearable bits are nine consecutive values because they are a **paperdoll**
/// — `ui/inventoryPane.nui` declares exactly nine <c>weargrid</c> slots in this order — and the
/// three containers above them are grids with a width and a height, which is why the engine
/// compares against those and never against a wearable slot.
/// </summary>
public static class Slot
{
    public const int None = 0;
    public const int Head = 0x1;          // invHead,        2 x 2 cells
    public const int Neck = 0x2;          // invNeck,        1 x 1
    public const int Body = 0x4;          // invBody,        2 x 3
    public const int RightWeapon = 0x8;   // invRightWeapon, 2 x 3
    public const int LeftWeapon = 0x10;   // invLeftWeapon,  2 x 3
    public const int RightArm = 0x20;     // invRightArm,    1 x 2
    public const int LeftArm = 0x40;      // invLeftArm,     1 x 2
    public const int RightHand = 0x80;    // invRightHand,   1 x 1
    public const int LeftHand = 0x100;    // invLeftHand,    1 x 1

    /// <summary>The five-slot strip along the bottom of the status bar (<c>sbQuickuse</c>, 5 x 1).</summary>
    public const int QuickUse = 0x200;
    /// <summary>The backpack grid (<c>invInventory</c>, 10 x 4).</summary>
    public const int Inventory = 0x400;
    /// <summary>The haven's storage (<c>vaultGrid</c>, 10 x 9).</summary>
    public const int Vault = 0x4000;

    /// <summary>Every wearable slot, which is every bit below <see cref="QuickUse"/>.</summary>
    public const int Worn = Head | Neck | Body | RightWeapon | LeftWeapon
                          | RightArm | LeftArm | RightHand | LeftHand;

    /// <summary>The containers, each of which holds many items rather than one.</summary>
    public const int Containers = QuickUse | Inventory | Vault;

    /// <summary>How big each container is, in cells (and the UI agrees).</summary>
    public static (int Width, int Height) Size(int container) => container switch
    {
        QuickUse => (5, 1),
        Inventory => (10, 4),
        Vault => (10, 9),
        _ => (0, 0),
    };

    /// <summary>The name the game's own UI gives a slot, for diagnostics.</summary>
    public static string Name(int slot) => slot switch
    {
        None => "none",
        Head => "head", Neck => "neck", Body => "body",
        RightWeapon => "right weapon", LeftWeapon => "left weapon",
        RightArm => "right arm", LeftArm => "left arm",
        RightHand => "right hand", LeftHand => "left hand",
        QuickUse => "quick use", Inventory => "inventory", Vault => "vault",
        _ => $"0x{slot:x}",
    };
}

/// <summary>
/// `item_flags` (tag 21), named by the SDK's <c>FLAG_ITEM_FLAGS</c>. The value a save
/// records is the template's **plus runtime bits**: the inventory sets
/// <see cref="Identified"/> when an item is bought, and <c>0x200000</c> — not in this enum, because
/// it is never authored — when one is equipped.
/// </summary>
[Flags]
public enum ItemFlags
{
    None = 0,
    DestroyedAtZeroCharges = 0x1,
    /// <summary>What the sell price tests: an unidentified item is worth a **quarter**.</summary>
    Identified = 0x2,
    PrecomputedCost = 0x4,
    UniqueCost = 0x8,
    NoSell = 0x10,
    NoRandomMod = 0x20,
    CannotBeIdentified = 0x40,
    InstantCash = 0x80,
    NoSmall = 0x100,
    NoMedium = 0x200,
    NoLarge = 0x400,
    /// <summary>What turns on the combat utility stance; only the torch carries it.</summary>
    CarrySpecial = 0x800,
    Magic = 0x1000,
    CarryToModernDay = 0x2000,
    Cursed = 0x4000,
    /// <summary>Occupies **both** slots of its pair. Every glove and gauntlet has it; no bracelet does.</summary>
    TwoHanded = 0x8000,
    NoPartyAiUse = 0x10000,
    /// <summary>
    /// Used up. The item's use sets it when the last charge goes and the item is **not**
    /// <see cref="DestroyedAtZeroCharges"/>, and the same function refuses to use anything carrying
    /// it — so a spent item stays in the pack for ever, doing nothing.
    /// </summary>
    Spent = 0x100000,
}

/// <summary>
/// `ITEM_RESTRTYPE` (<c>ENUM_ITEM_RESTRTYPE</c>): what a character must be, or must have, to use an
/// item. The first seventeen are the stats offset by one, so <see cref="Strength"/> is
/// <see cref="ActorStat.Strength"/>; the four creature types are the only ones the shipped game
/// uses.
/// </summary>
public enum RestrictionType
{
    None = 0,
    Strength = 1, Dexterity = 2, Stamina = 3, Perception = 4, Intelligence = 5, Wits = 6,
    Blood = 7, Humanity = 8, Frenzy = 9, Generation = 10, Faith = 11,
    Charisma = 12, Manipulation = 13, Appearance = 14,
    FrenzyRating = 15, BloodRating = 16, BloodPool = 17,
    Health = 18,
    Vampire = 19, Ghoul = 20, Human = 21,
    NoVampire = 22, NoGhoul = 23, NoHuman = 24,
    Mana = 25, ManaPool = 26,
}

/// <summary>
/// One of an item's ten effect slots. The item names an effect from <c>misc/effects.ned</c> and then
/// gives **either** a level, which indexes that effect's row of six values, **or** a value, which
/// overrides it. Both appear in the shipped data.
/// </summary>
/// <param name="Name">The `ef_*` the item applies.</param>
/// <param name="Level">
/// Which of the effect's six values to take, or **−1 when the template gives no level** — the
/// record starts at −1 (case 0xa6) and a −1 is applied by value. A level of 0
/// is a real level, the first of the six.
/// </param>
/// <param name="Value">The magnitude applied when there is no level; an unset one is 0.</param>
/// <param name="TimeMs">How long it lasts; zero is for as long as the item is worn.</param>
public readonly record struct ItemEffect(string Name, int Level, float? Value, int TimeMs);

/// <summary>One of an item's ten restrictions.</summary>
public readonly record struct ItemRestriction(RestrictionType Type, float Value);

/// <summary>
/// One thing in somebody's inventory.
/// </summary>
public sealed class Item
{
    public string Name { get; init; } = "";

    /// <summary>`item_type`.</summary>
    public ItemType Type { get; set; }

    /// <summary>The thing type it is made as — item, weapon or armour (tag 16), which the rollovers switch on.</summary>
    public World.ThingType ThingType { get; init; } = World.ThingType.Item;

    /// <summary>
    /// The thing this item **is**. In the original there is no distinction at all — `rpgItem`
    /// derives from `simThing` — and the two are separate classes here only because the world side
    /// grew first. <see cref="World.WorldThing.Item"/> is the same link read the other way, and
    /// <see cref="Ground"/> is the only thing that sets either.
    ///
    /// Null for an item with no world behind it: a test fixture, or a kit given to a character who
    /// is not in a location. Everything that needs the thing — dropping it, its GUID, its position
    /// — checks first and says so rather than assuming.
    /// </summary>
    public World.WorldThing? Thing { get; set; }

    /// <summary>
    /// Its foley file and what it sounds like when struck (tags 6 and 110, as for any thing) — kept
    /// on the item because an item in a pack has no thing in the world to carry them.
    /// </summary>
    public string Foley { get; init; } = "";
    /// <inheritdoc cref="Foley"/>
    public int SoundType { get; init; } = 1;

    /// <summary>
    /// `ITEM_ID`. For ammo this is what a weapon's `WEAPON_AMMOTYPE` names: the
    /// crossbow asks for 2 and `bolts_01` is 2. It is an arbitrary id, not an index into anything.
    /// </summary>
    public int ItemId { get; set; }

    /// <summary>
    /// `item_amount`. For ammo this is how many rounds the stack holds —
    /// `pistol_clip` is 11, `shotgun_shells_10` is 10, a single bolt is 1.
    /// </summary>
    public int Amount { get; set; }

    /// <summary>
    /// Where it is — one value of <see cref="Slot"/>. **Runtime state, not a template
    /// field.** The inventory sets it from a value its caller passes in, and nothing
    /// loads it from a tag.
    /// </summary>
    public int Location { get; set; }

    /// <summary>
    /// Which cell of <see cref="Location"/> its top-left corner sits in — the pair a save writes at
    /// `Fields2BC` (settled by moving a bracelet from an arm into
    /// the pack). **The engine takes it on trust on load**: nothing checks it, so two records
    /// sharing a cell are drawn on top of each other. Meaningless for a worn slot, which holds one
    /// item and draws it at its own corner.
    /// </summary>
    public (int X, int Y) Cell { get; set; }

    /// <summary>
    /// The one value of <see cref="Location"/> the ammo path tests (the reload: `!= 0x4000`).
    /// An item in the vault is passed over when looking for ammo to load.
    /// </summary>
    public const int NotCarried = Slot.Vault;

    /// <summary>`item_flags` (tag 21), plus the runtime bits the inventory sets.</summary>
    public ItemFlags Flags { get; set; }

    /// <summary>
    /// `ITEM_BASECOST` (tag 90) — what the shop arithmetic starts from before the store's own
    /// multiplier and the trader's Manipulation.
    /// </summary>
    public int BaseCost { get; set; }

    /// <summary>`ITEM_SELLFLAGS` (tag 120): which kind of merchant will take it.</summary>
    public int SellFlags { get; set; }

    /// <summary>
    /// Spent to nothing and thrown away. The engine deletes the object outright (the use's other
    /// branch); Vitomir marks it so whoever holds the inventory can drop it on its own terms.
    /// </summary>
    public bool Destroyed { get; set; }

    /// <summary>
    /// The texture that stands for it in a pane (`ICON`, tag 19) — a name with no extension, such
    /// as `swordBroadIcon`. Resolved through the material search path like any other UI texture.
    /// </summary>
    public string Icon { get; init; } = "";

    /// <summary>
    /// `ITEM_TEXT` (tag 30): the `.ntx` entry a **BOOK** opens. Empty for everything that is not
    /// one — 13 is `ITEM_TYPE` BOOK, which is what `CuthbertLetter` is.
    /// </summary>
    public string TextId { get; init; } = "";

    /// <summary>
    /// The discipline a scroll casts (from `ITEM_SCROLLDISC`), by name; empty for
    /// anything that is not one. The engine resolves the row when the template is applied;
    /// here it is resolved at the cast.
    /// </summary>
    public string ScrollDiscipline { get; init; } = "";
    /// <summary>The level it casts at (`ITEM_SCROLLLEVEL`, held to 0..4).</summary>
    public int ScrollLevel { get; init; }

    /// <summary>`DESCRIPTIONID` (tag 82): the `.nls` key for what this is called.</summary>
    public string DescriptionId { get; init; } = "";

    /// <summary>
    /// `ITEM_IDENTIFIEDDESC` (tag 33) and `ITEM_SPECIALDESC` (tag 34), both `.nls` keys. The
    /// rollover shows the first **instead of** <see cref="DescriptionId"/> once the item is
    /// <see cref="ItemFlags.Identified"/>, and the second as a line of its own — so an unidentified
    /// ring says what any ring says, and naming it is part of what identifying buys.
    /// </summary>
    public string IdentifiedDescriptionId { get; init; } = "";
    /// <inheritdoc cref="IdentifiedDescriptionId"/>
    public string SpecialDescriptionId { get; init; } = "";

    /// <summary>
    /// How many grid cells it takes (`ICONSIZE`, tag 17 — `ENUM_ICONSIZE`) and which icon set it
    /// comes from (`ICONTYPE`, tag 18). The size is why the backpack is a grid rather than a list:
    /// a broadsword is 2x3 cells and a ring is 1x1.
    /// </summary>
    public int IconSize { get; init; }
    /// <inheritdoc cref="IconSize"/>
    public int IconType { get; init; }

    /// <summary>
    /// `ITEM_TAG` (tag 25): the character's motion-set tag a utility item rides
    /// — `LFINGERS` for the torch, the flashlight and the rest (<see cref="Utility"/>). 0 for none.
    /// </summary>
    public int Tag { get; init; }

    /// <summary>
    /// <see cref="IconSize"/> as cells across and down. The numbers are the SDK's own
    /// `ENUM_ICONSIZE` and are **not** an arithmetic encoding — 4 is 2x2 and 7 is 2x1, which no
    /// width-times-height scheme produces — so they are a table. Six of the eight values appear in
    /// the shipped templates; 3 and 6 do not, and the pane's switch gives them 1x4 and 2x4 (read in
    /// A comparison with 1.1; until then they were left 1x1). A value past 7 falls out of the switch and
    /// leaves the icon as it was — 1x1 here (ours; no shipped template has one).
    /// </summary>
    public (int Across, int Down) IconCells => IconSize switch
    {
        1 => (1, 2),
        2 => (1, 3),
        3 => (1, 4),
        4 => (2, 2),
        5 => (2, 3),
        6 => (2, 4),
        7 => (2, 1),
        _ => (1, 1),
    };

    /// <summary>What it does to whoever wears it. Up to ten, and most wearables have one.</summary>
    public IReadOnlyList<ItemEffect> Effects { get; set; } = [];

    /// <summary>What a character must be, or have, to use it. Up to ten.</summary>
    public IReadOnlyList<ItemRestriction> Restrictions { get; init; } = [];

    /// <summary>Does it fill both slots of its pair? Every glove does; no bracelet does.</summary>
    public bool TwoHanded => (Flags & ItemFlags.TwoHanded) != 0;

    /// <summary>
    /// The slots this item may be worn in, from its <see cref="Type"/>. There is **no slot tag** in
    /// the template dictionary, so the engine derives this — which is why four item types map onto
    /// pairs of adjacent bits and the rest onto one each. Anything not listed is carried only.
    /// </summary>
    public static int SlotsFor(ItemType type) => type switch
    {
        ItemType.Headgear => Slot.Head,
        ItemType.Neckwear => Slot.Neck,
        ItemType.Armor => Slot.Body,
        ItemType.Weapon => Slot.RightWeapon,
        ItemType.Shield or ItemType.Utility => Slot.LeftWeapon,
        ItemType.Handwear => Slot.RightArm | Slot.LeftArm,
        ItemType.Rings => Slot.RightHand | Slot.LeftHand,
        _ => Slot.None,
    };

    /// <summary>The slots this particular item may be worn in.</summary>
    public int Slots => SlotsFor(Type);

    /// <summary>The other half of a paired slot, or <see cref="Slot.None"/>.</summary>
    public static int Partner(int slot) => slot switch
    {
        Slot.RightArm => Slot.LeftArm,
        Slot.LeftArm => Slot.RightArm,
        Slot.RightHand => Slot.LeftHand,
        Slot.LeftHand => Slot.RightHand,
        _ => Slot.None,
    };

    /// <summary>
    /// The weapon this item *is*, when <see cref="Type"/> is <see cref="ItemType.Weapon"/>. In the
    /// engine an inventory entry is a thing and a weapon thing carries its `gameWeapon` inline, so
    /// the two are one object; keeping the weapon on the item preserves that rather than making
    /// somebody look it up again.
    /// </summary>
    public Weapon? Weapon { get; set; }

    /// <summary>
    /// `ARMOR_MODEL` — what this is called when it is **on** the character rather than in the
    /// paperdoll. A shield in the off hand is `ARMOR_MODEL_SKELETONID` and a suit of
    /// armour replaces the character's own model with `character_ARMOR_MODEL`. See
    /// <see cref="World.HeldModel"/>.
    /// </summary>
    public string ArmorModel { get; set; } = "";

    /// <summary>Whether this item can be loaded into a weapon asking for <paramref name="ammoType"/>.</summary>
    public bool IsAmmoFor(int ammoType) =>
        Location != NotCarried && Type == ItemType.Ammo && ItemId == ammoType;

    public static Item? From(string name, IReadOnlyDictionary<uint, NodTag> tags)
    {
        if (!tags.TryGetValue(World.Tags.Type, out var type)) return null;
        uint kind = type.AsUInt();
        if (kind != (uint)World.ThingType.Item && kind != (uint)World.ThingType.Weapon
            && kind != (uint)World.ThingType.Armor) return null;

        return new Item
        {
            // A weapon item **is** a weapon: in the engine an inventory entry is a thing and a
            // weapon thing carries its `gameWeapon` inline, so the two are made together here
            // rather than bolted on by whoever happens to make the item.
            ThingType = (World.ThingType)kind,
            Weapon = kind == (uint)World.ThingType.Weapon ? Weapon.From(name, tags) : null,
            ArmorModel = Str(tags, World.Tags.ArmorModel),
            Foley = Str(tags, World.Tags.Foley),
            SoundType = tags.ContainsKey(World.Tags.SoundType) ? Int(tags, World.Tags.SoundType) : 1,
            Name = name,
            Type = (ItemType)Int(tags, World.Tags.ItemType),
            ItemId = Int(tags, World.Tags.ItemId),
            Amount = Int(tags, World.Tags.ItemAmount),
            Flags = (ItemFlags)Int(tags, World.Tags.ItemFlags),
            BaseCost = Int(tags, World.Tags.ItemBaseCost),
            SellFlags = Int(tags, World.Tags.ItemSellFlags),
            Icon = Str(tags, World.Tags.ItemIcon),
            TextId = Str(tags, World.Tags.ItemText),
            ScrollDiscipline = Str(tags, World.Tags.ItemScrollDiscipline),
            ScrollLevel = Math.Clamp(Int(tags, World.Tags.ItemScrollLevel), 0, 4),
            DescriptionId = Str(tags, World.Tags.DescriptionId),
            IdentifiedDescriptionId = Str(tags, World.Tags.ItemIdentifiedDesc),
            SpecialDescriptionId = Str(tags, World.Tags.ItemSpecialDesc),
            IconSize = Int(tags, World.Tags.ItemIconSize),
            IconType = Int(tags, World.Tags.ItemIconType),
            Tag = Int(tags, World.Tags.ItemTag),
            Effects = ReadEffects(tags),
            Restrictions = ReadRestrictions(tags),
            // Location is deliberately not loaded: it is runtime state the inventory sets.
        };
    }

    /// <summary>The ten effect slots, skipping the ones with no name.</summary>
    static List<ItemEffect> ReadEffects(IReadOnlyDictionary<uint, NodTag> tags)
    {
        var found = new List<ItemEffect>();
        for (uint n = 0; n < World.Tags.EffectSlots; n++)
        {
            uint at = World.Tags.EffectName0 + n * World.Tags.EffectStride;
            if (!tags.TryGetValue(at, out var name)) continue;
            string what = name.AsString();
            if (string.IsNullOrEmpty(what)) continue;
            // The tag reader's record starts at level −1 and value 0 (case 0xa6):
            // an item that gives a value and no level is applied **by value**, which is how the
            // rags' ITEM_EFFECTVALUE 5 is five and not the effect's first level.
            found.Add(new ItemEffect(
                what,
                tags.ContainsKey(at + 1) ? Int(tags, at + 1) : -1,
                tags.TryGetValue(at + 2, out var value) ? value.AsFloat() : null,
                Int(tags, at + 3)));
        }
        return found;
    }

    /// <summary>The ten restrictions, skipping <see cref="RestrictionType.None"/>.</summary>
    static List<ItemRestriction> ReadRestrictions(IReadOnlyDictionary<uint, NodTag> tags)
    {
        var found = new List<ItemRestriction>();
        for (uint n = 0; n < World.Tags.RestrictionSlots; n++)
        {
            uint at = World.Tags.RestrictionType0 + n * World.Tags.RestrictionStride;
            if (!tags.TryGetValue(at, out var type)) continue;
            var kind = (RestrictionType)type.AsUInt();
            if (kind == RestrictionType.None) continue;
            found.Add(new ItemRestriction(
                kind, tags.TryGetValue(at + 1, out var value) ? value.AsFloat() : 0f));
        }
        return found;
    }

    static int Int(IReadOnlyDictionary<uint, NodTag> tags, uint id) =>
        tags.TryGetValue(id, out var t) ? (int)t.AsUInt() : 0;

    static string Str(IReadOnlyDictionary<uint, NodTag> tags, uint id) =>
        tags.TryGetValue(id, out var t) ? t.AsString() : "";

    public override string ToString() =>
        Type == ItemType.Ammo ? $"{Name} x{Amount} (ammo {ItemId})" : $"{Name} ({Type})";
}

/// <summary>
/// The character's item list. Vitomir keeps a <see cref="List{T}"/> rather than threading pointers
/// through the items, which is the same list in a different shape — but the **order matters**, so
/// <see cref="Add"/> pushes on the front exactly as 1.1 does.
/// </summary>
public sealed class Inventory
{
    readonly List<Item> items = [];

    public IReadOnlyList<Item> Items => items;
    public int Count => items.Count;

    /// <summary>Push on the front, as 1.1's inventory does.</summary>
    public void Add(Item item) => items.Insert(0, item);

    public bool Remove(Item item) => items.Remove(item);

    /// <summary>
    /// The ammo the reload would pick: of everything that matches the type, **the smallest stack**
    /// (the reload keeps the item with the fewest rounds). That is deliberate rather than
    /// incidental — spending part-used stacks first is what stops a full clip being wasted on a
    /// half-empty magazine. Ties go to the one found first, which is the most recently acquired.
    /// </summary>
    public Item? SmallestAmmo(int ammoType)
    {
        Item? best = null;
        foreach (var item in items)
            if (item.IsAmmoFor(ammoType) && (best is null || item.Amount < best.Amount))
                best = item;
        return best;
    }

    /// <summary>
    /// The first weapon in the list that could be swung or fired right now: not ranged, or ranged
    /// with no ammo requirement, or ranged with ammo in it. The caller
    /// decides whether to actually switch to it.
    /// </summary>
    public IEnumerable<Item> UsableWeapons()
    {
        foreach (var item in items)
        {
            if (item.Type != ItemType.Weapon || item.Weapon is not { } w) continue;
            var a = w.Current;
            if (!a.Is(WeaponFlag.Ranged) || a.MaxAmmo == 0 || a.Ammo > 0) yield return item;
        }
    }
}
