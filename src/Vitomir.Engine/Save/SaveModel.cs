// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Vitomir's own save, as data: one class per part of the file and one property per field a person
// might want to read or change. The shape follows what 1.1's save keeps, so a thing 1.1 forgets on a
// load (a character's AI mode, what it was in the middle of doing) is forgotten here too, and a thing
// 1.1 keeps (a script's `save()` output, its timers, what it has captured) is kept.
//
// Nothing in here is a runtime reference. Things are named by their stable identity
// (`ThingIds`), never by a pointer, so a file can be read, edited and written without the game.
using System.Numerics;

namespace Vitomir.Engine.Save;

/// <summary>A whole save: every part, as read from or written to one file.</summary>
public sealed class SaveGame
{
    public SaveHeader Header { get; set; } = new();
    public ChronicleSave? Chronicle { get; set; }
    public SessionSave Session { get; set; } = new();
    public PartySave Party { get; set; } = new();
    /// <summary>Every place the game has been and still remembers, by location name.</summary>
    public Dictionary<string, LocationSave> World { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>`save.jsonc`: what the load list shows, and what the rest of the file was written under.</summary>
public sealed class SaveHeader
{
    /// <summary>The version of this format. The engine reads every version it has ever written.</summary>
    public int Format { get; set; } = SaveArchive.FormatVersion;
    public string Engine { get; set; } = "Vitomir";
    /// <summary>
    /// The build that wrote it (the menu's version line); empty in a save from an
    /// older build. For the upgrades a later build runs by version: <see cref="Format"/>
    /// says how to read the file, this says which build's habits the game in it was played under.
    /// </summary>
    public string Build { get; set; } = "";
    /// <summary>`"The Convent (00:24:25)"` — the location and the game's clock, as 1.1 words it.</summary>
    public string Description { get; set; } = "";
    /// <summary>When it was written, as the machine's local time in ISO 8601.</summary>
    public string Saved { get; set; } = "";
    /// <summary>The game's clock in milliseconds (tag 9).</summary>
    public long ClockMs { get; set; }
    /// <summary>The chronicle being played (`Prague`).</summary>
    public string Chronicle { get; set; } = "";
    /// <summary>The location the party is in.</summary>
    public string Location { get; set; } = "";
    /// <summary>
    /// What the game was played under, in a word (ours): `faithful` when no feature that
    /// counts was on when it was saved (`Features.Feature.Counts`: a switch that changes the rules and is no
    /// quality-of-life switch), `modded` when one was — and **empty in a save from before saves recorded
    /// this**, which means the save does not say. A save says `changed` where this says `modded`, and
    /// is read as `modded` (`Features.FeatureRegistry.ProfileRead`). **The word is the writing build's**: two
    /// Earlier builds counted the quality-of-life switches, so their saves of a game played as it comes say
    /// `modded`; what a list of saves should show is `Features.FeatureRegistry.ProfileOf`, which reads the
    /// names in <see cref="Rules"/> by today's rule.
    /// </summary>
    public string Profile { get => profile; set => profile = Features.FeatureRegistry.ProfileRead(value); }
    string profile = "";
    /// <summary>
    /// The features that count and were on when it was saved, by name (`fixes.gunreach`), in the registry's
    /// order (`Features.FeatureRegistry.RulesOn`) — a save of such a build names quality-of-life switches
    /// too (`qol.driving`), which a load passes over; a ruleset flag — a
    /// rule chosen at a new game only — will be written here too. A load
    /// compares it with what is in force (`Features.LoadSwitches`). Empty in a save from before
    /// <see cref="Profile"/>.
    /// </summary>
    public List<string> Rules { get; set; } = [];
    /// <summary>
    /// The rules modules it was made with (the player's modules with world files: <c>id version hash</c>), in load order; empty for
    /// the base game and with only look modules on. A load compares them with the ones on (<see cref="LoadModules"/>).
    /// </summary>
    public List<string> Mods { get; set; } = [];
    /// <summary>A player's own note. Kept word for word; the game never writes it.</summary>
    public string Notes { get; set; } = "";
}

// ---- the chronicle -------------------------------------------------------------------------------

/// <summary>`chronicle.jsonc`: where the story is (1.1's tags 2, 3 and 7).</summary>
public sealed class ChronicleSave
{
    public string Name { get; set; } = "";
    /// <summary>The location the party is in, by name.</summary>
    public string Current { get; set; } = "";
    /// <summary>The chronicle flags that are set, by number (`PragueChronicle.JIRI_TALKEDONCE` is 120).</summary>
    public List<int> Flags { get; set; } = [];
    public PlaceRef? Portal { get; set; }
    public PlaceRef? Revive { get; set; }
    /// <summary>Where `SetupAdvance` said the next advance goes, when it has said.</summary>
    public AdvanceSave? Advance { get; set; }
    /// <summary>Every location of the chronicle, as the story has left it: its scene, flags and exits.</summary>
    public Dictionary<string, ChronicleLocationSave> Locations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The chronicle's own scripts (`PragueChronicle`), by class.</summary>
    public Dictionary<string, ScriptSave> Scripts { get; set; } = new(StringComparer.Ordinal);
    /// <summary>
    /// The weather a script asked for (`SetWeatherEffect`, the chronicle's 32-byte field at
    /// written and read back with the chronicle) — London's
    /// drizzle is set once, in a conversation's end, and `Weather.beginscene` reads it back. Absent: none.
    /// </summary>
    public string? Weather { get; set; }
}

/// <summary>A location and one of its entrances.</summary>
public sealed class PlaceRef
{
    public string? Location { get; set; }
    public int Entrance { get; set; }
}

public sealed class AdvanceSave
{
    public string? Location { get; set; }
    public string Scene { get; set; } = "";
    public int Entrance { get; set; } = -1;
}

public sealed class ChronicleLocationSave
{
    public string Scene { get; set; } = "";
    public uint Flags { get; set; }
    public List<ExitSave> Exits { get; set; } = [];
}

/// <summary>One exit, numbered by its place in the list. A null <see cref="To"/> leads nowhere.</summary>
public sealed class ExitSave
{
    public string? To { get; set; }
    public int Entrance { get; set; }
    public bool Closed { get; set; }
}

// ---- what belongs to no one place ----------------------------------------------------------------

/// <summary>`session.jsonc`: the purse, the quest log and the shops (1.1's tags 4–6, 12, 14–17).</summary>
public sealed class SessionSave
{
    public int Gold { get; set; }
    public List<QuestSave> Quests { get; set; } = [];
    /// <summary>
    /// Every shop record, newest first. **The `.nbs` file name is not kept**, because 1.1 does not
    /// keep it — which is why the first visit to each shop after a load stocks it afresh.
    /// </summary>
    public List<StoreSave> Stores { get; set; } = [];
    /// <summary>The treasure record counter (tag 14).</summary>
    public int TreasureCounter { get; set; }
    /// <summary>The effect instance counter.</summary>
    public int EffectCounter { get; set; }
    /// <summary>
    /// The game's GUID counter, `nextGUID` (1.1's tag 10) —
    /// the number the next thing made anywhere gets (<see cref="World.ThingGuids"/>). Format 2 on;
    /// a format-1 save kept one per place (<see cref="LocationSave.NextGuid"/>) and is converted
    /// (<see cref="SaveGuids"/>).
    /// </summary>
    public int ThingCounter { get; set; }
    /// <summary>
    /// The numbers set aside for places a chronicle loads at its start and the party has not yet been
    /// to (<see cref="World.Preloads"/>, ours), by level and scene: the first GUID each gets. Written by
    /// Which did not load those places; they are loaded on their numbers at the start
    /// (<see cref="World.Campaign.Preload"/>) and nothing is left set aside to write.
    /// </summary>
    public Dictionary<string, int>? ReservedGuids { get; set; }
    /// <summary>The teleport slots in use (1.1's tag 8, all sixteen; `TeleportSaves.cs`).</summary>
    public List<TeleportSave> Teleports { get; set; } = [];
}

public sealed class QuestSave
{
    public int Slot { get; set; }
    public string Name { get; set; } = "";
    public uint Flags { get; set; }
    public uint Xp { get; set; }
    public string ShortKey { get; set; } = "";
    public string LongKey { get; set; } = "";
    public string Icon { get; set; } = "";
    public List<QuestItemSave> Items { get; set; } = [];
}

public sealed class QuestItemSave
{
    public uint Flags { get; set; }
    public int Steps { get; set; }
    public int Step { get; set; }
    public string ShortKey { get; set; } = "";
    public string LongKey { get; set; } = "";
    public string Icon { get; set; } = "";
    public uint Xp { get; set; }
}

public sealed class StoreSave
{
    /// <summary>The shopkeeper, by stable identity; its GUID is kept alongside, as 1.1 keys by it.</summary>
    public string? Owner { get; set; }
    public int OwnerGuid { get; set; }
    public string DescriptionId { get; set; } = "";
    public uint Flags { get; set; }
    public float BuyRate { get; set; }
    public float SellRate { get; set; }
    public int SellMask { get; set; }
    public List<StockLineSave> Lines { get; set; } = [];
}

public sealed class StockLineSave
{
    public int Remaining { get; set; }
    /// <summary>−1 for a line that never runs out.</summary>
    public int Copies { get; set; }
    /// <summary>A `D` line's template, or null for an `R` line.</summary>
    public string? Template { get; set; }
    /// <summary>An `R` line's treasure class.</summary>
    public int Class { get; set; }
    /// <summary>What is on the shelf now, if anything.</summary>
    public TreasureSave? Stock { get; set; }
}

/// <summary>An item that exists but is in no world yet — a shelf's (1.1's TREASURE record).</summary>
public sealed class TreasureSave
{
    public int Id { get; set; }
    public string Template { get; set; } = "";
    public ItemSave Item { get; set; } = new();
}

// ---- the party -----------------------------------------------------------------------------------

/// <summary>`party.jsonc`: who travels with the player, leader first (1.1's tag 12).</summary>
public sealed class PartySave
{
    /// <summary>Which member the player is steering, by id.</summary>
    public string? Steered { get; set; }
    public List<ThingSave> Members { get; set; } = [];
}

// ---- a place -------------------------------------------------------------------------------------

/// <summary>`world/&lt;location&gt;.jsonc`: one place as it was left (1.1's `location_n.nsw`).</summary>
public sealed class LocationSave
{
    public string Level { get; set; } = "";
    /// <summary>The chapter scene laid over the level, or empty for the level's own.</summary>
    public string Scene { get; set; } = "";
    /// <summary>The entrance it was last entered by.</summary>
    public int Entrance { get; set; } = -1;
    /// <summary>The scripts' clock here, in milliseconds since the place was first entered.</summary>
    public long ScriptClockMs { get; set; }
    /// <summary>
    /// The game's clock when this record was taken (ours: 1.1's file keeps its times on the game's
    /// clock itself), so a place read back later is put forward by the time between (`AwayTimers`).
    /// </summary>
    public long AsOfMs { get; set; }
    /// <summary>
    /// The game's GUID counter when this place was taken. Format 1 numbered each place on its own and
    /// kept its counter here; from format 2 the one that counts is the session's
    /// (<see cref="SessionSave.ThingCounter"/>), and this is written for a reader's benefit.
    /// </summary>
    public int NextGuid { get; set; }
    /// <summary>How many things have been made here, which is where a made thing's id comes from.</summary>
    public int Made { get; set; }
    /// <summary>
    /// Everything in the world here, by id, the party excepted. A placed thing that is not here and
    /// not in anybody's pack was destroyed; see <see cref="Removed"/>.
    /// </summary>
    public Dictionary<string, ThingSave> Things { get; set; } = new(StringComparer.Ordinal);
    /// <summary>The placed things that are gone, for a reader's benefit — the loader works it out anyway.</summary>
    public List<string> Removed { get; set; } = [];
    /// <summary>
    /// The things in the world here, by id, in the world's list order — head first, newest first —
    /// as 1.1 writes them; the load links each back in at the head (`SaveOrder`). Absent
    /// in a save written before it was kept, which loads in the scene's order.
    /// </summary>
    public List<string>? ListOrder { get; set; }
    /// <summary>The scripts' timers that had not fired (1.1's tag 0x19).</summary>
    public List<TimerSave> Timers { get; set; } = [];
}

/// <summary>
/// One thing. The same record serves a thing in the world, a party member, and an item in a pack;
/// a field that does not apply is left out.
/// </summary>
public sealed class ThingSave
{
    public string Template { get; set; } = "";
    /// <summary>
    /// Its GUID while the game ran. Not its identity — the key it is filed under is — but kept,
    /// because a script's saved state may hold it, and 1.1 gives every thing its GUID back.
    /// </summary>
    public int Guid { get; set; }
    public Vector3 Position { get; set; }
    /// <summary>Degrees about x, y and z.</summary>
    public Vector3 Orientation { get; set; }
    public long Sector { get; set; }
    /// <summary>Where it was when it came into the world — the home an AI walks back to.</summary>
    public Vector3? Home { get; set; }
    public Vector3? HomeOrientation { get; set; }
    /// <summary>`THING_FLAG_*`.</summary>
    public uint Flags { get; set; }
    /// <summary>`THING_RENDERFLAG_*`.</summary>
    public uint RenderFlags { get; set; }
    public string? DescriptionId { get; set; }
    public string? Model { get; set; }
    public string? Foley { get; set; }
    public uint MoveType { get; set; }
    public uint PhysicsFlags { get; set; }
    public Vector3? Velocity { get; set; }
    public Vector3? Spin { get; set; }
    /// <summary>A container's health, when it can be broken — one of the three words tag 0x31 carries.</summary>
    public float? ContainerHealth { get; set; }
    /// <summary>The per-type state word — for a container, bit 0 is "opened".</summary>
    public uint State { get; set; }
    /// <summary>When it is thrown away, on the game's clock; absent for never.</summary>
    public int? ExpireAt { get; set; }
    public float Frame { get; set; }
    /// <summary>Its keyframes, when a script has given it others than the scene's.</summary>
    public List<KeyframeSave>? Keyframes { get; set; }
    public float? Alpha { get; set; }
    public float? Scale { get; set; }
    /// <summary>What rides on it: each child by id, with the bone, the flags and the offset it rides at (1.1's LINKS).</summary>
    public List<AttachmentSave>? Attachments { get; set; }
    /// <summary>Who owns it and who holds it, by id — an item's owner, a summoned thing's master.</summary>
    public string? Owner { get; set; }
    public string? Holder { get; set; }
    public int SpawnedBy { get; set; }
    public ItemSave? Item { get; set; }
    public ActorSave? Actor { get; set; }
    public List<EffectSave>? Effects { get; set; }
    public ScriptSave? Script { get; set; }
    /// <summary>The scripts that have captured it, and the id each gave (1.1's tag 0x1a).</summary>
    public List<CaptureSave>? Captures { get; set; }
    /// <summary>An animation it is holding at its end — a corpse's fall (1.1's model block, `Poses`).</summary>
    public PoseSave? Pose { get; set; }
    /// <summary>
    /// The body it presents to collision (`CollideShape`, the first word of the
    /// 0x28-byte block 1.1's thing record carries) — which a script's `SetCollideType` changes: a door taken away, a servant who has
    /// left. Absent in a save written before it was kept: the template's.
    /// </summary>
    public uint? Collide { get; set; }
    /// <summary>A region's own flag word (tag 0x30; `SetRegionFlags`).</summary>
    public uint? RegionFlags { get; set; }
    /// <summary>A prop's owner, by GUID (tag 0x2f's second word) — a teleporter end's maker.</summary>
    public int? PropOwner { get; set; }
    /// <summary>
    /// True for a thing never yet finalized — an item made straight into a pack that has never been in
    /// the world — so that when it first goes into the world it is finalized then, and its script told
    /// `created` (1.1 keeps the bit, 0x10000000, in the flag word it saves).
    /// </summary>
    public bool? Unfinalized { get; set; }
    /// <summary>
    /// A track mover's own state (move type 2: 1.1 writes the 0x90 bytes of the mover's state
    /// and reads them back): the tracker's angles and, when it was saved on its
    /// way, the move itself — so a door or a lift saved half-way carries on and arrives.
    /// </summary>
    public TrackSave? Track { get; set; }
    /// <summary>
    /// A party member standing in the place whose scene placed it: that placement (`P1_UNIV#12`), so a
    /// load gives the place's scripts that name it the member itself, as 1.1's saved parameters do.
    /// </summary>
    public string? Placed { get; set; }
}

/// <summary>A track mover's state (<see cref="ThingSave.Track"/>).</summary>
public sealed class TrackSave
{
    /// <summary>The tracker's angles, when the tracker has moved it from keyframe 0's.</summary>
    public Vector3? Angles { get; set; }
    /// <summary>The move under way, if it was saved moving.</summary>
    public MoveSave? Move { get; set; }
}

/// <summary>A track move under way (`World.ThingMotion`).</summary>
public sealed class MoveSave
{
    public int Frame { get; set; }
    public Vector3 FromPosition { get; set; }
    public Vector3 ToPosition { get; set; }
    public Vector3 FromOrientation { get; set; }
    public Vector3 ToOrientation { get; set; }
    /// <summary>The hinge, for a swing; absent for a straight move.</summary>
    public Vector3? Pivot { get; set; }
    public float Duration { get; set; }
    public float Elapsed { get; set; }
    /// <summary>A move asked at a speed of 0 or less: its share of the way a second.</summary>
    public float? Rate { get; set; }
}

/// <summary>
/// An animation held at its last frame, as 1.1's model block keeps it (the track's
/// animation and flags are written when they hold `0x1010`; the load plays it again with `0x800`, from its end).
/// </summary>
public sealed class PoseSave
{
    /// <summary>The animation file (`halfCorpse_death.nad`).</summary>
    public string Animation { get; set; } = "";
    /// <summary>The mode it was played as, when Vitomir knows it (`deathquick`).</summary>
    public string? Mode { get; set; }
    /// <summary>The channel's flags (`NmsFlags`; 1.1's file may carry its 0x1000 "held" bit too, which a load ignores).</summary>
    public uint Flags { get; set; }
}

public sealed class AttachmentSave
{
    public string Child { get; set; } = "";
    public uint Flags { get; set; }
    public int Bone { get; set; } = -1;
    /// <summary>The child's transform relative to its parent, as the 3 x 4 the engine keeps, row by row.</summary>
    public float[] Relative { get; set; } = [];
}

public sealed class KeyframeSave
{
    public Vector3 Position { get; set; }
    public Vector3 Orientation { get; set; }
}

public sealed class CaptureSave
{
    /// <summary>The capturing script, by the script key <see cref="ScriptKeys"/> gives.</summary>
    public string Script { get; set; } = "";
    public int Id { get; set; }
}

public sealed class ItemSave
{
    public uint Flags { get; set; }
    public int Amount { get; set; }
    /// <summary>Where it is: a worn slot, the grid, the belt or the vault (`Rpg.Slot`).</summary>
    public int Location { get; set; }
    /// <summary>Its cell in the grid.</summary>
    public int[]? Cell { get; set; }
    public int BaseCost { get; set; }
    public int SellFlags { get; set; }
    public int ItemId { get; set; }
    /// <summary>Its effects, when they are not the template's — a random modifier, a shop's roll.</summary>
    public List<ItemEffectSave>? Effects { get; set; }
    public WeaponSave? Weapon { get; set; }
}

public sealed class ItemEffectSave
{
    public string Name { get; set; } = "";
    public int Level { get; set; } = -1;
    public float? Value { get; set; }
    public int TimeMs { get; set; }
}

public sealed class WeaponSave
{
    public int Attack { get; set; }
    /// <summary>Rounds loaded, one per attack.</summary>
    public List<int> Ammo { get; set; } = [];
}

/// <summary>A character: the stats, the gear, the disciplines. 1.1's ACTOR and PLAYER records.</summary>
public sealed class ActorSave
{
    public string Name { get; set; } = "";
    public string? CastId { get; set; }
    /// <summary>`THING_AF_*`.</summary>
    public uint Flags { get; set; }
    /// <summary>`THING_AF2_*`.</summary>
    public uint Flags2 { get; set; }
    public string Type { get; set; } = "";
    public int Team { get; set; }
    public int OriginalTeam { get; set; }
    public int Perception { get; set; }
    public int Stealth { get; set; }
    public string Clan { get; set; } = "";
    public uint Aura { get; set; }
    public string? AuraName { get; set; }
    public float Health { get; set; }
    public float MaxHealth { get; set; }
    /// <summary>Everything ever earned — the rank, and the health and blood it brings.</summary>
    public long Experience { get; set; }
    /// <summary>What is left to spend.</summary>
    public long Unspent { get; set; }
    public int Cash { get; set; }
    public int Xp { get; set; }
    public int GibClass { get; set; }
    public int SkeletonId { get; set; }
    public float MoveSpeed { get; set; } = 1f;
    public float AttackSpeed { get; set; } = 1f;
    public float WalkSpeed { get; set; }
    public float RunSpeed { get; set; }
    public string Stance { get; set; } = "";
    public string? Head { get; set; }
    public bool WeaponHidden { get; set; }
    public bool OffhandHidden { get; set; }
    public string? NoItemArmor { get; set; }
    public bool PlayerThing { get; set; }
    public int DisciplineTab { get; set; }
    public int DisciplineTabOffset { get; set; }
    public int FadeAt { get; set; }
    public int FadingFrom { get; set; }
    public bool Dusted { get; set; }
    /// <summary>The nineteen stats by name: what the sheet shows, what it rests on, and the bounds.</summary>
    public Dictionary<string, StatSave> Stats { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The ten soaks, by damage type.</summary>
    public Dictionary<string, SoakSave> Soaks { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Everything carried and worn, by id.</summary>
    public Dictionary<string, ThingSave> Inventory { get; set; } = new(StringComparer.Ordinal);
    /// <summary>The item on the cursor, by id, when there is one.</summary>
    public string? Held { get; set; }
    public List<DisciplineSave> Disciplines { get; set; } = [];
    /// <summary>The discipline selected, by name.</summary>
    public string? Discipline { get; set; }
    /// <summary>A weapon swap in force (the claws, <c>THING_AF2_WEAPONOVERRIDE</c>), when there is one.</summary>
    public OverrideSave? Override { get; set; }
    /// <summary>A shape change in force (<see cref="Rpg.ShapeShift"/>), when there is one.</summary>
    public ShapeSave? Shape { get; set; }
}

/// <summary>
/// A weapon swap (<see cref="Rpg.WeaponOverride"/>): the weapon swapped in, by template, and what it
/// put aside — the hand's item and the off hand's, by id; a null hand is the character's own weapon.
/// 1.1 keeps the two as GUIDs on the actor and resolves them after a
/// load.
/// </summary>
public sealed class OverrideSave
{
    public string Weapon { get; set; } = "";
    public string? PrevWeapon { get; set; }
    public string? PrevOffhand { get; set; }
}

public sealed class StatSave
{
    public float Now { get; set; }
    public float Base { get; set; }
    public float Min { get; set; }
    public float Max { get; set; }
}

public sealed class SoakSave
{
    public float Now { get; set; }
    public float Base { get; set; }
}

public sealed class DisciplineSave
{
    public string Name { get; set; } = "";
    public int Level { get; set; }
    public int QuickSlot { get; set; } = -1;
    public uint Flags { get; set; }
    public int CastCount { get; set; }
    /// <summary>When it can be cast again, on the game's clock.</summary>
    public int RecastUntil { get; set; }
    public bool Active { get; set; }
    public ScriptSave? Script { get; set; }
}

public sealed class EffectSave
{
    public string Name { get; set; } = "";
    public int Level { get; set; }
    public float Value { get; set; }
    /// <summary>When it ends, on the game's clock; 0 for never.</summary>
    public int Until { get; set; }
    public int NextTick { get; set; }
    public float Fade { get; set; }
    public int Guid { get; set; }
    public int Creator { get; set; }
    public uint Flags { get; set; }
    public int IntParam { get; set; }
    public float FloatParam { get; set; }
    /// <summary>The worn item it hangs from, by id.</summary>
    public string? Source { get; set; }
    public ScriptSave? Script { get; set; }
    /// <summary>
    /// **Ours**: the things its script made that no save keeps and that are still there — a pad, a
    /// trail (<see cref="LoadedEffects"/>, the repair <c>loadedeffects</c>). 1.1's record has nothing like it.
    /// Written by every save and made again by a load. Absent when there is none, and in every older save.
    /// </summary>
    public List<EffectThingSave>? Made { get; set; }
    /// <summary>**Ours**: the shells its script set that are still on their models, as they stand. As <see cref="Made"/>.</summary>
    public List<EffectShellSave>? Shells { get; set; }
    /// <summary>
    /// **Ours**: the models its script froze that are still frozen (<c>FreezeAnimations</c>: Prison of Ice's
    /// target), each with the pose it is held in. As <see cref="Made"/>.
    /// </summary>
    public List<EffectFrozenSave>? Frozen { get; set; }
}

/// <summary>
/// One model an effect's script froze (<see cref="EffectSave.Frozen"/>, ours), and the pose it stands in:
/// `Anim.ThingAnimation.KeepPose`'s numbers. 1.1's save writes the model's flag word with the freeze bit
/// (0x20000) in it and its reader throws the word away, and keeps nothing of what a model is
/// playing but an animation held at its end (<see cref="PoseSave"/>).
/// </summary>
public sealed class EffectFrozenSave
{
    /// <summary>The thing whose model is frozen, by GUID.</summary>
    public int On { get; set; }
    /// <summary>Every bone's angles, three numbers a bone, in the model's bone order.</summary>
    public float[] Rotation { get; set; } = [];
    /// <summary>Every bone's offset, as <see cref="Rotation"/>.</summary>
    public float[] Translation { get; set; } = [];
}

/// <summary>
/// One thing an effect's script made that no save keeps (<see cref="EffectSave.Made"/>, ours): enough to make it
/// again where it was, under the number it had.
/// </summary>
public sealed class EffectThingSave
{
    public string Template { get; set; } = "";
    /// <summary>The GUID it had — the number the effect's script kept, and will remove it by.</summary>
    public int Guid { get; set; }
    public Vector3 Position { get; set; }
    public Vector3 Orientation { get; set; }
    public long Sector { get; set; }
    /// <summary>The thing it rides, by GUID (every saved thing has its own back by then); null for one that rides nothing.</summary>
    public int? Rides { get; set; }
    /// <summary>The bone it rides, or −1 for the thing's own transform; and the flags it was attached with (`ATTACH_FLAG_*`).</summary>
    public int Bone { get; set; } = -1;
    public uint AttachFlags { get; set; }
    /// <summary>Its transform relative to the attach point, as <see cref="AttachmentSave.Relative"/>.</summary>
    public float[]? Relative { get; set; }
    /// <summary>Its model's alpha and scale where the script had put them (True Faith's dome is all but clear), as <see cref="ThingSave.Alpha"/> and <see cref="ThingSave.Scale"/>; null for the template's own.</summary>
    public float? Alpha { get; set; }
    public float? Scale { get; set; }
    /// <summary>The keyframes the script gave it (a beam's two ends: <c>AllocateFrames</c>, <c>SetFramePosition</c>), as <see cref="ThingSave.Keyframes"/>; null for the template's own.</summary>
    public List<KeyframeSave>? Keyframes { get; set; }
}

/// <summary>One shell an effect's script set (<see cref="EffectSave.Shells"/>, ours), as it stood: `World.ModelFx.ShellState`'s words.</summary>
public sealed class EffectShellSave
{
    /// <summary>The thing whose model wears it, by GUID.</summary>
    public int On { get; set; }
    public string Material { get; set; } = "";
    public uint Flags { get; set; }
    public float Alpha { get; set; }
    /// <summary>Its ramp, when one was running: from, to, the rate (1 / seconds) and how far along (0 to 1).</summary>
    public bool Ramping { get; set; }
    public float From { get; set; }
    public float To { get; set; }
    public float Rate { get; set; }
    public float Progress { get; set; }
}

// ---- scripts -------------------------------------------------------------------------------------

/// <summary>
/// A script's own state: its class, and exactly what its `save()` wrote through `SaveInt`,
/// `SaveBoolean`, `SaveFloat` and `SaveString`, in order. `restore()` reads the same values back. A
/// class that declares no `save` keeps nothing, and on a load starts from its constructor — as in 1.1.
/// </summary>
public sealed class ScriptSave
{
    public string Class { get; set; } = "";
    public List<ScriptValue> Values { get; set; } = [];
}

/// <summary>One value a script saved. Exactly one of the four is set.</summary>
public sealed class ScriptValue
{
    public bool? Bool { get; set; }
    public int? Int { get; set; }
    public float? Float { get; set; }
    public string? String { get; set; }

    public static ScriptValue Of(bool v) => new() { Bool = v };
    public static ScriptValue Of(int v) => new() { Int = v };
    public static ScriptValue Of(float v) => new() { Float = v };
    public static ScriptValue Of(string v) => new() { String = v };
}

/// <summary>A timer a script set and that had not fired.</summary>
public sealed class TimerSave
{
    /// <summary>Whose timer, by script key.</summary>
    public string Script { get; set; } = "";
    public int Id { get; set; }
    /// <summary>How long it had still to run.</summary>
    public int InMs { get; set; }
    public float[] Args { get; set; } = new float[4];
}
