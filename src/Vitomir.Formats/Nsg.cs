// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The save game: `SaveGames\SP<n>\!main.nsg`.
//
// There are no lengths in this format. Between sections the writer drops a bare u32 tag
// and the reader asserts it is the tag it expected; everything
// between two tags is a struct written raw. So a reader cannot skip what it does not understand —
// the only way through is to follow the writer field by field, which is what this file does.
//
// Every field below follows the original game's save writer, and the whole grammar is checked
// against ten real saves from a full playthrough: each one parses to its last byte with nothing
// left over (`NsgCorpusTests`). Where the writer's meaning is known the field is named; where it is
// not, the field is named by its place in the writer's record (`Field1F4`), so nothing is silently
// dropped.
//
// The sections, in file order: the header, the chronicle and its quests, the teleports, the clock,
// the counters, the shops and their treasure, the effects, then the party, each thing, actor,
// player and item in it, and their disciplines (tag 0x28).
namespace Vitomir.Formats;

/// <summary>The section sentinels, by the number the writer puts in the file.</summary>
public static class NsgTag
{
    public const uint Chronicle = 2, ChronicleFlags = 3, Quest = 4, QuestRecord = 5, QuestStep = 6,
                      CodexList = 7, Teleports = 8, Clock = 9, ThingCounter = 10, CodexCounter = 11,
                      Party = 12, TreasureCounter = 14, Shops = 15, Treasure = 16, Effects = 17,
                      Thing = 0x1e, Model = 0x1f, ThingEnd = 0x20, Thing21 = 0x21, Thing22 = 0x22,
                      Links = 0x23, Actor = 0x24, ActorBody = 0x26, ActorEffects = 0x27,
                      Disciplines = 0x28, Inventory = 0x29, Player = 0x2b, Item = 0x2c,
                      Ammo = 0x2d, Armour = 0x2e, Prop = 0x2f, Region = 0x30, Container = 0x31,
                      CodexRecord = 0x1c, CodexEnd = 0x1d;
}

/// <summary>`ENUM_TYPE` — the first u32 of every thing record, asserted below THING_TYPE_NUMTYPES (11).</summary>
public enum NsgThingType : uint
{
    Actor = 0, Item = 1, Prop = 2, Region = 3, Projectile = 4, Weapon = 5,
    Armour = 6, Player = 7, Container = 8, None = 9, Explosion = 10,
}

/// <summary>
/// The script state a codex object wrote for itself. Saving one points the writer at the file
/// and calls the Java class's <c>save(int)</c>; the class writes through the `SaveInt`, `SaveBoolean`,
/// `SaveFloat` and `SaveString` natives, and <c>restore(int)</c> reads the same values back in the
/// same order. So the bytes are only meaningful to the script that wrote them, and the reader keeps
/// them whole rather than pretending to parse them.
/// </summary>
/// <remarks>
/// No chronicle class in the game declares <c>save</c>, so the chronicle's records are always
/// empty. But 89 other shipped classes do — doors, traps and the discipline effects among them — and
/// a party member with a discipline running carries its effect script's words here: Celerity's is
/// one word, the GUID of its trail. The reader steps over the shipped classes' state by their layouts
/// (<see cref="NsgScriptLayouts"/>); <see cref="NsgReadOptions.ReadScriptState"/> is the seam for
/// handing it to the script runtime instead.
/// </remarks>
public sealed class NsgCodexRecord
{
    /// <summary>The second argument of the writer: 1 in the chronicle's list, 0 everywhere else.</summary>
    public uint Kind { get; init; }
    public NsgCodexField[] Fields { get; init; } = [];
    /// <summary>The raw <c>save()</c> output, empty unless a script wrote some.</summary>
    public byte[] ScriptState { get; init; } = [];
}

/// <param name="Type">7 is a string; anything else is a 12-byte value.</param>
public readonly record struct NsgCodexField(uint Type, string? Text, byte[]? Bits);

/// <summary>One of the chronicle's locations, as the save left it.</summary>
public sealed class NsgLocationState
{
    /// <summary>See <see cref="NscLocationFlags"/>. The loader masks 0x100000 off on the way in.</summary>
    public uint Flags { get; init; }
    public string Name { get; init; } = "";
    public string Level { get; init; } = "";
    public string Scene { get; init; } = "";
    public string MapFile { get; init; } = "";
    public string MapMarker { get; init; } = "";
    /// <summary>A field of the location record whose meaning is not known, kept as read.</summary>
    public uint Field78 { get; init; }
    /// <summary>The exits, as the chronicle has them now — scripts open and close these.</summary>
    public NsgExit[] Exits { get; init; } = [];
}

/// <param name="To">The location this exit leads to.</param>
/// <param name="Entrance">Which entrance of that location you arrive at.</param>
/// <param name="Closed">Whether the exit is shut.</param>
public readonly record struct NsgExit(uint To, uint Entrance, uint Closed);

public sealed class NsgChronicle
{
    /// <summary>The chronicle file, as written (<c>prague.nsc</c>).</summary>
    public string Name { get; init; } = "";
    /// <summary>Fields of the chronicle record whose meaning is not known, kept as read.</summary>
    public uint Field0 { get; init; }
    public uint Field4 { get; init; }
    /// <summary>
    /// The current location, where Walk the Abyss leads (`SetPortalLocation`) and where a character
    /// revives (`SetReviveLocation`): each a location and an entrance.
    /// </summary>
    public uint[] Fields { get; init; } = [];
    /// <summary>The weather a script asked for (`SetWeatherEffect`), empty for none.</summary>
    public string Text1Af88 { get; init; } = "";
    public NsgLocationState[] Locations { get; init; } = [];
    /// <summary>The map file the chronicle is showing (<c>prague.nmf</c>).</summary>
    public string MapFile { get; init; } = "";
    /// <summary>The 1024 chronicle flags the scripts set with <c>SetChronicleFlag</c>, 32 words of them.</summary>
    public uint[] Flags { get; init; } = [];
    public NsgQuest[] Quests { get; init; } = [];
    /// <summary>The chronicle's codex objects, by class name.</summary>
    public (string Class, NsgCodexRecord Record)[] Codices { get; init; } = [];
}

/// <summary>A quest, at its index in the 0x40-entry table.</summary>
public sealed class NsgQuest
{
    public int Index { get; init; }
    public string Name { get; init; } = "";
    /// <summary>Fields of the quest record whose meaning is not known, kept as read.</summary>
    public string Text38 { get; init; } = "";
    public string Text58 { get; init; } = "";
    /// <summary>Fields of the quest record whose meaning is not known, kept as read.</summary>
    public uint[] Fields { get; init; } = [];
    /// <summary>A field of the quest record whose meaning is not known, kept as read.</summary>
    public string Text78 { get; init; } = "";
    /// <summary>A field of the quest record whose meaning is not known, kept as read.</summary>
    public uint Field30 { get; init; }
    public NsgQuestStep[] Steps { get; init; } = [];
}

/// <summary>One step of a quest, 0x80 bytes apart in the original's array.</summary>
public sealed class NsgQuestStep
{
    /// <summary>Fields of the step record whose meaning is not known, kept as read.</summary>
    public string Text10 { get; init; } = "";
    public string Text30 { get; init; } = "";
    /// <summary>Fields of the step record whose meaning is not known, kept as read.</summary>
    public uint Field0 { get; init; }
    public uint Field58 { get; init; }
    public string Text5C { get; init; } = "";
    public uint Field4 { get; init; }
    public uint Field8 { get; init; }
}

/// <summary>One of the sixteen teleport slots. Always sixteen, never counted differently.</summary>
public readonly record struct NsgTeleport(uint A, uint B, uint C, uint D, uint E, uint F, uint G);

/// <summary>
/// A shop, as the save writes it. Every field here is an argument the shop's own
/// script passed to `Codex.AddBuySell(nbsFile, ownerGuid, descriptionID, flags, buyRate, sellRate,
/// sellFlags)` — except the <c>.nbs</c> file name itself, which the format leaves out and which is
/// the whole reason shops re-roll on a load.
/// </summary>
public sealed class NsgShop
{
    /// <summary>The shopkeeper's GUID, and the only thing the lookup matches on.</summary>
    public uint OwnerGuid { get; init; }
    /// <summary>The shop's <c>DESCRIPTIONID</c> (<c>UNORNASHOP</c>, <c>SMITHY</c>).</summary>
    public string Name { get; init; } = "";
    /// <summary>The script's <c>flags</c> argument. 4 in every shop measured.</summary>
    public uint Flags { get; init; }
    /// <summary>The BUY rate the price formula multiplies by. 1.25 everywhere.</summary>
    public uint BuyRate { get; init; }
    /// <summary>The SELL rate. 0.8 everywhere.</summary>
    public uint SellRate { get; init; }
    /// <summary>
    /// The mask of <c>ITEM_SELLFLAGS</c> categories this shop deals in. Unorna's is
    /// 0x1af (magic armour, magic weapons and potions, but not plain armour or weapons); the
    /// smith's is 0x53 (armour and weapons, but nothing magical).
    /// </summary>
    public uint SellFlags { get; init; }
    public NsgStock[] Stock { get; init; } = [];
}

/// <summary>
/// One line of a shop's stock — one line of its `.nbs`, with whatever was rolled for it. The engine
/// keeps the line even when it has been bought out, so a sold-out line is a count of 0 and no
/// instance.
/// </summary>
public sealed class NsgStock
{
    /// <summary>How many are left. Meaningless while <see cref="Copies"/> is −1.</summary>
    public uint Remaining { get; init; }
    /// <summary>How many the `.nbs` line carried; **−1 means an infinite supply** (the file writes 0 for that).</summary>
    public int Copies { get; init; }
    /// <summary>The `.nbs` line's first column: 1 for `D`, a template by name; 2 for `R`, a treasure class to roll.</summary>
    public uint Kind { get; init; }
    /// <summary>The template, on a `D` line.</summary>
    public string? Template { get; init; }
    /// <summary>The treasure class (`game.ntt`), on an `R` line.</summary>
    public uint TreasureClass { get; init; }
    /// <summary>What was rolled for this line, or null once it has been bought out.</summary>
    public NsgTreasure? Treasure { get; init; }

    public bool Infinite => Copies < 0;
    public bool SoldOut => !Infinite && Remaining == 0;
}

/// <summary>
/// A treasure record (tag 16) — an item that exists but is not in the
/// world yet: a shop's rolled stock, or something waiting to drop. It is a flattened copy of the
/// template's own tags rather than a reference to it, which is why a shop's stock survives a save
/// without the templates and why re-rolling it produces a different record rather than a different
/// pointer. Every field below was matched against the shipped `.not` it came from.
/// </summary>
public sealed class NsgTreasure
{
    /// <summary>Which line of the shop's `.nbs` this was rolled for.</summary>
    public uint Slot { get; init; }
    /// <summary>`TYPE` (tag 16), the <see cref="NsgThingType"/> it will become.</summary>
    public NsgThingType Type { get; init; }
    /// <summary>`ITEM_AMOUNT` (22).</summary>
    public uint Amount { get; init; }
    /// <summary>`ITEM_FLAGS` (21).</summary>
    public uint Flags { get; init; }
    /// <summary>`ITEM_TYPE` (20).</summary>
    public uint ItemType { get; init; }
    /// <summary>`ITEM_BASECOST` (90), what the shop's price is computed from.</summary>
    public uint BaseCost { get; init; }
    /// <summary>`ITEM_SELLFLAGS` (120).</summary>
    public uint SellFlags { get; init; }
    /// <summary>`WEAPON_DAMAGE1` (43). 0 on anything that is not a weapon.</summary>
    public uint WeaponDamage { get; init; }
    /// <summary>`WEAPON_DAMAGETYPE1` (131): the stake's 1 LETHAL, holy water's 6 FAITH.</summary>
    public uint WeaponDamageType { get; init; }
    /// <summary>`WEAPON_STRIKETIME1` (45) in milliseconds (the template's 1.16667 s is 1166 here).</summary>
    public uint WeaponStrikeTime { get; init; }
    /// <summary>A float; 32.0 on the stake and 0 on everything else measured.</summary>
    public uint Field90 { get; init; }
    /// <summary>Two words whose meaning is not known. 0 in every record measured.</summary>
    public uint Field94 { get; init; }
    public uint Field98 { get; init; }
    /// <summary>`DESCRIPTIONID` (82), the name shown while it is unidentified (<c>AMULET</c>).</summary>
    public string DescriptionId { get; init; } = "";
    /// <summary>`ITEM_IDENTIFIEDDESC` (33), the name shown once it is (<c>AMULETCONTROL</c>).</summary>
    public string IdentifiedId { get; init; } = "";
    /// <summary>The effect's own description id (<c>ITEM_EFFECT_CONTROL</c>), or empty.</summary>
    public string EffectId { get; init; } = "";
    /// <summary>The `.not` template it was rolled from (by name).</summary>
    public string Template { get; init; } = "";
    /// <summary>The inventory `ICON` (tag 19), and the word saved beside it.</summary>
    public string? Icon { get; init; }
    public uint IconField { get; init; }
    /// <summary>The restrictions.</summary>
    public NsgRestriction[] Restrictions { get; init; } = [];
    /// <summary>The effects.</summary>
    public NsgItemEffect[] Effects { get; init; } = [];
}

/// <summary>
/// One of an item's effects, as the instance carries it. The four numbers are the template's
/// <c>ITEM_EFFECTLEVEL</c>, <c>ITEM_EFFECTVALUE</c> and <c>ITEM_EFFECTTIME</c>, then the id of the
/// live effect it is currently running on its wearer — 0 while the item is not equipped. Verified
/// against the shipped templates: the rags carry <c>ef_soak_lethal</c> at level −1 with the value
/// 5.0 (level −1 is `bByValue`), a health potion carries level 3 for 5000ms, and the ring of
/// manipulation's level 0 effect is 0 in the pack and an id once it is on a finger.
/// </summary>
public readonly record struct NsgItemEffect(string Name, int Level, uint Value, uint Time, uint InstanceId)
{
    /// <summary><see cref="Value"/> read as the float the by-value form stores.</summary>
    public float ValueAsFloat => BitConverter.UInt32BitsToSingle(Value);
    /// <summary>Whether the effect is running on a wearer right now.</summary>
    public bool Applied => InstanceId != 0;
}

/// <summary>
/// One of an item's restrictions — the template's <c>ITEM_RESTRTYPE</c> and <c>ITEM_RESTRVALUE</c>.
/// The instance keeps them in a list it builds by prepending, so a save holds them in the reverse
/// of the template's order (the broadsword's STRENGTH then DEXTERITY comes back the other way up).
/// </summary>
public readonly record struct NsgRestriction(uint Type, uint Value);

/// <summary>The party (tag 12) — the last section of the file.</summary>
public sealed class NsgParty
{
    public uint Gold { get; init; }
    /// <summary>
    /// Four words of uninitialised stack. The writer takes them from a local it never fills
    /// and the loader reads them into a local it never uses,
    /// so they mean nothing — but they are four bytes each and a reader has to step over them.
    /// </summary>
    public uint[] Junk { get; init; } = [];
    /// <summary>The four words the game keeps beside the party's array.</summary>
    public uint[] Slots { get; init; } = [];
    /// <summary>The GUID of the player the camera is on.</summary>
    public uint CurrentPlayerGuid { get; init; }
    public NsgThing[] Players { get; init; } = [];
}

/// <summary>A thing record (tag 0x1e), plus whatever its type adds.</summary>
public sealed class NsgThing
{
    public NsgThingType Type { get; init; }
    public uint Guid { get; init; }
    public string Name { get; init; } = "";
    /// <summary>The template's <c>DESCRIPTIONID</c> — the id the localised display name is looked up by.</summary>
    public string DescriptionId { get; init; } = "";
    /// <summary>The world it is in, or −1.</summary>
    public int World { get; init; }
    /// <summary>−1 for none, −2 for the world's own scene, otherwise the scene's id.</summary>
    public int Scene { get; init; }
    /// <summary>Its sector in that world, or −1.</summary>
    public int Sector { get; init; }
    /// <summary>Two placements of the thing, 0x30 bytes each.</summary>
    public byte[] Transform { get; init; } = [];
    public byte[] Transform2 { get; init; } = [];
    /// <summary>Three words: one not known, the flags word (0x200000 = equipped), and another not known.</summary>
    public uint Field90 { get; init; }
    public uint Flags { get; init; }
    public uint Field128 { get; init; }
    /// <summary>A field of the thing record whose meaning is not known, kept as read.</summary>
    public byte[] FieldFC { get; init; } = [];
    /// <summary>2 carries 0x90 more bytes, 3 carries 0x48, 0 and 1 carry none.</summary>
    public uint Shape { get; init; }
    public byte[] ShapeData { get; init; } = [];
    /// <summary>A list the thing carries, 0x18 bytes each.</summary>
    public byte[][] Records { get; init; } = [];
    /// <summary>The template it was made from, when it still has one.</summary>
    public string? Template { get; init; }
    /// <summary>A field of the thing record whose meaning is not known, kept as read.</summary>
    public uint Field28 { get; init; }
    public NsgModel Model { get; init; } = new();
    public NsgLinks Links { get; init; } = new();
    /// <summary>The script attached to this thing, when it has one.</summary>
    public NsgScript? Script { get; init; }

    /// <summary>Set on ACTOR and PLAYER.</summary>
    public NsgActor? Actor { get; init; }
    /// <summary>Set on PLAYER.</summary>
    public NsgPlayer? Player { get; init; }
    /// <summary>Set on ITEM, WEAPON and ARMOUR.</summary>
    public NsgItem? Item { get; init; }
    /// <summary>The fields the leaf type adds after its own tag.</summary>
    public uint[] TypeFields { get; init; } = [];
    /// <summary>WEAPON's ammunition counts (tag 0x2d), one per 0x40-byte slot.</summary>
    public uint[] Ammo { get; init; } = [];
    /// <summary>WEAPON's byte after the ammunition counts: which of its attacks a swing uses.</summary>
    public byte AmmoTrailer { get; init; }

    /// <summary>
    /// Where this record sits in the file: <see cref="Start"/> is the byte its type word begins at
    /// and <see cref="End"/> is one past its last byte, so <c>End - Start</c> is its whole length.
    ///
    /// The format has no lengths and no index, so these are the *only* way to say where a record
    /// begins and ends — and they come from the sequential walk itself rather than from a search,
    /// which is the distinction that matters. Anything that edits a save needs them: a record can
    /// then be lifted out or spliced in whole, without measuring offsets from a name, which
    /// produced true statements that did not generalise.
    /// </summary>
    public int Start { get; init; }
    /// <inheritdoc cref="Start"/>
    public int End { get; init; }
}

/// <summary>A thing's codex object: the class it runs, its GUID, and the state the script saved.</summary>
public sealed class NsgScript
{
    public string Class { get; init; } = "";
    public uint Guid { get; init; }
    public NsgCodexRecord Record { get; init; } = new();
}

/// <summary>The model and animation block (tag 0x1f).</summary>
public sealed class NsgModel
{
    /// <summary>0 = nothing saved, 1 = the short form, 2 = the long form.</summary>
    public uint Mode { get; init; }
    /// <summary>The render flags. 0x100000 is what picks the short form.</summary>
    public uint Flags { get; init; }
    /// <summary>The motion set, when the thing has one.</summary>
    public string? MotionSet { get; init; }
    /// <summary>Two words of the motion state, in the short form and in the long form's motion block alike.</summary>
    public uint[] MotionFields { get; init; } = [];
    /// <summary>Only the short form carries it.</summary>
    public uint Field14C { get; init; }
    /// <summary>The model file (long form only).</summary>
    public string? Model { get; init; }
    /// <summary>The playing animation and its flags (long form only).</summary>
    public string? Animation { get; init; }
    public uint AnimationFlags { get; init; }
}

/// <summary>What a thing is attached to and what is attached to it (tag 0x23).</summary>
public sealed class NsgLinks
{
    /// <summary>
    /// The GUID of the thing this one hangs off, or 0. An equipped item deliberately records none:
    /// the writer zeroes it when the type is ITEM, WEAPON or ARMOUR and the equipped bit is set, so
    /// the equip relationship is restored from the slot rather than from the link. The reader
    /// reads it back and then unconditionally clears the live field to 0 regardless — so
    /// on 1.1 the value in the file, whatever it is, is never actually used; here it is simply never
    /// read back out of <see cref="NsgLinks"/> either, which comes to the same thing.
    /// </summary>
    public uint ParentGuid { get; init; }
    public NsgAttachment[] Attachments { get; init; } = [];
}

/// <param name="Guid">The attached thing.</param>
/// <param name="Transform">0x30 bytes, the attachment's own placement.</param>
public readonly record struct NsgAttachment(uint Guid, uint A, uint B, byte[] Transform);

/// <summary>An actor's own fields (tag 0x24).</summary>
public sealed class NsgActor
{
    /// <summary>A field of the actor record whose meaning is not known, kept as read.</summary>
    public uint Field1F4 { get; init; }
    /// <summary>`THING_AF_*`. The writer masks 0x80 off in two action states.</summary>
    public uint Flags { get; init; }
    /// <summary>The `ACTOR_TYPE`.</summary>
    public byte ActorType { get; init; }
    /// <summary>A field of the actor record whose meaning is not known, kept as read.</summary>
    public byte Field1FE { get; init; }
    /// <summary>Fields of the actor record whose meaning is not known, kept as read.</summary>
    public uint Field22C { get; init; }
    public uint Field230 { get; init; }
    /// <summary>The `CASTID`, written with its length and no terminator.</summary>
    public string CastId { get; init; } = "";
    /// <summary>A name the actor holds, when it has one.</summary>
    public string? Text208 { get; init; }
    /// <summary>Present only when flags &amp; 0x800.</summary>
    public byte[] Extra { get; init; } = [];
    /// <summary>Fields of the actor record whose meaning is not known, kept as read.</summary>
    public byte Field1FF { get; init; }
    public byte Field200 { get; init; }
    public float Health { get; init; }
    /// <summary>The maximum, which carries the character's rank history — the loader takes it from here, not from the sheet.</summary>
    public float MaxHealth { get; init; }
    public float WalkSpeed { get; init; }
    public float RunSpeed { get; init; }
    /// <summary>The `MOVESPEED` and `ATTACKSPEED` multipliers.</summary>
    public float MoveSpeed { get; init; }
    public float AttackSpeed { get; init; }
    /// <summary>A field of the actor record whose meaning is not known, kept as read.</summary>
    public uint Field224 { get; init; }
    /// <summary>The nineteen base stats and ten base soaks.</summary>
    public float[] BaseStats { get; init; } = [];
    public float[] BaseSoaks { get; init; } = [];
    /// <summary>The nineteen current stats and ten current soaks — what the game reads.</summary>
    public float[] Stats { get; init; } = [];
    public float[] Soaks { get; init; } = [];
    /// <summary>When the corpse starts fading.</summary>
    public uint FadeAt { get; init; }
    public NsgEffect[] Effects { get; init; } = [];
    public NsgInventory Inventory { get; init; } = new();
    public NsgDiscipline[] Disciplines { get; init; } = [];
    /// <summary>The discipline the actor has selected, or null when the writer put its <c>"..."</c> placeholder.</summary>
    public string? SelectedDiscipline { get; init; }
    /// <summary>
    /// The actor's last word: **its shape change** when one is in force — the shape-change record's
    /// template's name and its flags, written and read back the same way as elsewhere. Named <c>Mind</c>
    /// before its meaning was known; it is not the mind.
    /// </summary>
    public string? Mind { get; init; }
    public uint MindField { get; init; }

    public const int StatCount = 19, SoakCount = 10;
}

/// <summary>
/// A live effect on an actor (tag 0x27) — ten words of the
/// effect instance. An effect an item put there names the item in <see cref="SourceGuid"/> and carries the
/// same <see cref="Id"/> the item's own copy of the effect records, so the whole chain from a worn
/// ring to a changed stat is in the file and can be walked either way.
/// </summary>
public sealed class NsgEffect
{
    /// <summary>The effect definition's name, from <c>effects.ned</c>.</summary>
    public string Name { get; init; } = "";
    /// <summary>The level the definition is read at.</summary>
    public int Level { get; init; }
    /// <summary>When the effect next acts, in the same milliseconds the clock counts. 0 on effects that never tick.</summary>
    public uint NextAt { get; init; }
    /// <summary>A field of the effect record whose meaning is not known, kept as read.</summary>
    public uint Field10 { get; init; }
    /// <summary>The instance's own id, which the item that applied it records too.</summary>
    public uint Id { get; init; }
    /// <summary>
    /// The thing that applied it, or 0: a worn item's GUID, or a caster's — Christof's
    /// own 5 on the Celerity he cast on himself (an autosave).
    /// </summary>
    public uint SourceGuid { get; init; }
    /// <summary>A flags word. The writer drops any effect with 0x2000000 set, so a saved effect never has it.</summary>
    public uint Flags { get; init; }
    /// <summary>−1 means the value below is used directly rather than read from the definition's levels.</summary>
    public int ValueLevel { get; init; }
    /// <summary>The by-value amount, as float bits.</summary>
    public uint Value { get; init; }
    /// <summary>Fields of the effect record whose meaning is not known, kept as read.</summary>
    public uint Field28 { get; init; }
    public uint Field2C { get; init; }
    /// <summary>The effect's own codex object, when it has one.</summary>
    public NsgScript? Script { get; init; }

    public float ValueAsFloat => BitConverter.UInt32BitsToSingle(Value);

    /// <summary>The bit that keeps an effect out of the file.</summary>
    public const uint NotSaved = 0x2000000;
}

/// <summary>What an actor is carrying and holding (tag 0x29).</summary>
public sealed class NsgInventory
{
    /// <summary>Everything in the pack and in the worn slots — the slot is in the item's own flags word.</summary>
    public NsgThing[] Items { get; init; } = [];
    /// <summary>Three GUIDs: one, the unarmed weapon (which every player is asserted to have), and one more.</summary>
    public NsgThing? Held360 { get; init; }
    public NsgThing? UnarmedWeapon { get; init; }
    public NsgThing? Held36C { get; init; }
    /// <summary>Six GUIDs: the current weapon, then five more of the actor's.</summary>
    public uint[] Guids { get; init; } = [];
}

/// <summary>A discipline the actor knows (tag 0x28).</summary>
public sealed class NsgDiscipline
{
    public string Name { get; init; } = "";
    /// <summary>A field of the effect record whose meaning is not known, kept as read.</summary>
    public uint Field8 { get; init; }
    /// <summary>The level, clamped at −1 on the way back in.</summary>
    public int Level { get; init; }
    /// <summary>Fields of the effect record whose meaning is not known, kept as read.</summary>
    public uint Field18 { get; init; }
    public uint FieldC { get; init; }
    /// <summary>The value handed back to the discipline's codex object.</summary>
    public uint ScriptValue { get; init; }
}

/// <summary>
/// An item's own fields (tag 0x2c). Five of the six words are the
/// template's tags carried onto the instance, which is how a save keeps an item that a mod has
/// since edited — and they are named here because every one of them was checked against the
/// shipped template it came from.
/// </summary>
public sealed class NsgItem
{
    /// <summary>
    /// <c>ITEM_FLAGS</c> (tag 21) plus the run-time bits: <b>0x2 IDENTIFIED</b>, set on
    /// purchase rather than on equip, and <b>0x200000 EQUIPPED</b>. The ring of manipulation's
    /// template says 0x3000 and its save entry says 0x203002.
    /// </summary>
    public uint Flags { get; init; }
    /// <summary><c>ITEM_AMOUNT</c> (tag 22), the charges. 1 on potions and holy water, 0 on everything else.</summary>
    public uint Amount { get; init; }
    /// <summary><c>ITEM_BASECOST</c> (tag 90), the number the price formula starts from.</summary>
    public uint BaseCost { get; init; }
    /// <summary><c>ITEM_SELLFLAGS</c> (tag 120), the category mask that decides which shop will trade it.</summary>
    public uint SellFlags { get; init; }
    /// <summary>A word whose meaning is not known. 0 in every save measured.</summary>
    public uint Field21C { get; init; }
    /// <summary>
    /// The slot the item is in right now, as a single bit of the slot mask:
    /// 0x400 inventory, 0x200 quick-use, 0x8 the weapon hand, 0x4 body,
    /// 0x2 neck, 0x80 and 0x100 the two ring fingers, and so on.
    /// </summary>
    public uint Location { get; init; }
    /// <summary>Two words of a record the item points to, which the loader reads straight back.</summary>
    public uint[] Fields2BC { get; init; } = [];
    /// <summary>The restrictions — <c>pNewRestrict</c> in the loader's own assert.</summary>
    public NsgRestriction[] Restrictions { get; init; } = [];
    /// <summary>The effects — <c>pNewEffect</c> in the loader's own assert.</summary>
    public NsgItemEffect[] Effects { get; init; } = [];

    /// <summary>The bit the writer tests to decide an equipped item records no parent.</summary>
    public const uint Equipped = 0x200000, Identified = 0x2;

    public bool IsEquipped => (Flags & Equipped) != 0;
}

/// <summary>A player's own fields (tag 0x2b).</summary>
public sealed class NsgPlayer
{
    /// <summary>A field of the player record whose meaning is not known, kept as read.</summary>
    public string Text3C4 { get; init; } = "";
    /// <summary>Fields of the player record whose meaning is not known, kept as read.</summary>
    public uint Field3E8 { get; init; }
    public uint Field3EC { get; init; }
    /// <summary>For a player it comes from the mind's own object instead.</summary>
    public string? Text3F0 { get; init; }
    /// <summary>A field of the player record whose meaning is not known, kept as read.</summary>
    public string Text494 { get; init; } = "";
    /// <summary>Fields of the player record whose meaning is not known, kept as read.</summary>
    public byte Field4B4 { get; init; }
    public byte Field4B5 { get; init; }
}

/// <summary>How to handle the parts of a save that only the script layer can read.</summary>
public sealed class NsgReadOptions
{
    /// <summary>
    /// Called where a codex object's <c>save()</c> output sits, with the reader parked on the first
    /// byte of it; it must leave the reader on the closing tag. Without it the reader steps over the
    /// shipped classes' state by their known layouts (<see cref="NsgScriptLayouts"/>).
    /// </summary>
    public Action<BinReader>? ReadScriptState { get; init; }

    public static readonly NsgReadOptions Default = new();
}

/// <summary>`!main.nsg` — one save slot's game state.</summary>
public sealed class NsgSave
{
    /// <summary>The 0x78-byte line the load screen shows: the scene and the elapsed time.</summary>
    public string Description { get; init; } = "";
    /// <summary>`NSG\0`.</summary>
    public string Magic { get; init; } = "";
    /// <summary>5 in every shipped build; the loader refuses anything else.</summary>
    public uint Version { get; init; }
    /// <summary>
    /// The location, entrance and scene of the next advance, as
    /// `SetupAdvance` left them — −1, −1 and empty until a script says, and again once the
    /// advance has gone.
    /// </summary>
    public uint FieldE4 { get; init; }
    public uint Field108 { get; init; }
    public string TextE8 { get; init; } = "";
    public NsgChronicle Chronicle { get; init; } = new();
    public NsgTeleport[] Teleports { get; init; } = [];
    /// <summary>The game clock, 18 words of it. Word 11 is the elapsed milliseconds the description is printed from.</summary>
    public uint[] Clock { get; init; } = [];
    /// <summary>The thing id counter (tag 10) and the codex counter (tag 11).</summary>
    public uint ThingCounter { get; init; }
    public uint CodexCounter { get; init; }
    /// <summary>The treasure counter (tag 14) and the effect counter (tag 17).</summary>
    public uint TreasureCounter { get; init; }
    public uint EffectCounter { get; init; }
    public NsgShop[] Shops { get; init; } = [];
    public NsgParty Party { get; init; } = new();
    /// <summary>
    /// Whether the file has a party section at all: a network game's save has none. The writer calls the party's
    /// only when no network game is up, and the load reads it only outside a network
    /// session: such a file ends on the effect counter, and <see cref="Party"/> is empty.
    /// </summary>
    public bool HasParty { get; init; } = true;

    /// <summary>Elapsed play time, from the clock word the description is printed from.</summary>
    public TimeSpan Elapsed => TimeSpan.FromMilliseconds(Clock.Length > 11 ? Clock[11] : 0);

    /// <summary>The party's first player, which is the one every single-player save has.</summary>
    public NsgThing? FirstPlayer => Party.Players.FirstOrDefault();

    public const int ClockWords = 18, TeleportSlots = 16, ChronicleFlagWords = 32, QuestSlots = 0x40;
    public const uint ExpectedVersion = 5;

    public static NsgSave Read(byte[] data, string name = "!main.nsg", NsgReadOptions? options = null) =>
        new NsgReader(new BinReader(data, name), options ?? NsgReadOptions.Default).ReadFile();

    public static NsgSave ReadFile(string path, NsgReadOptions? options = null) =>
        Read(File.ReadAllBytes(path), Path.GetFileName(path), options);
}
