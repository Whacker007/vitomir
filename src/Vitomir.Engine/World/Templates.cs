// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Vfs;
using Vitomir.Formats;

namespace Vitomir.Engine.World;

/// <summary>Engine tag ids (the engine's own tag table; ids match the SDK's noteditortags.txt).</summary>
public static class Tags
{
    public const uint BasedOn = 0, Orientation = 1, Position = 2, Sector = 3, Model = 4, ThingFlags = 7,
                      RenderFlags = 8, MotionSet = 9, ModelFlags = 10, Script = 11, Type = 16,
                      DescriptionId = 82, ModelScale = 235;

    /// <summary>
    /// `FOLEY` (tag 6), the thing's `.nag`, and `SOUNDTYPE` (tag 110), what a blow on it sounds like.
    /// </summary>
    public const uint Foley = 6, SoundType = 110;

    /// <summary>How wide a thing is (tag 13, RADIUS): what a walker is kept clear of, and what a
    /// path link's flags 1 and 2 shrink its edge by. 831 templates set one; the player's is 32 and
    /// the base <c>_actor</c>'s is 64.</summary>
    public const uint Radius = 13;

    /// <summary>
    /// The body a thing presents to collision (<see cref="CollideShape"/>) and the two numbers that
    /// size it. The engine takes <c>collide</c> only when it is 0..3, and <c>radius</c> and
    /// <c>height</c> only when they are not negative (cases 0x0c/0x0d/0x0e);
    /// a thing whose template sets none of them is built with all three zero — that is, with no
    /// body at all (the thing's constructor). <c>ProxRadius</c> is a separate thing: it is what
    /// a THING_FLAG_PROXIMITYTRIGGER notices people at, not what anyone bumps into.
    /// </summary>
    public const uint Collide = 12, Height = 14, MoveSpeed = 15, MoveType = 78, ProxRadius = 234;

    /// <summary>
    /// The five physics tags, and the guard that decides whether any of them means anything.
    /// `gameThing`'s physics setter tests the move type *first* and
    /// answers 0 to every one of these unless the thing is already <see cref="WorldThing.MovePhysics"/>
    /// — so a template that gives a gravity or an elasticity to anything else has it silently
    /// dropped. The shipped data lives inside that rule: **no item, weapon or armour template in the
    /// game sets `MOVETYPE` or any `PHYSICS_*` tag at all**, which is why a dropped sword does not
    /// fall.
    ///
    /// They go to five consecutive fields in that order, which is
    /// what confirms the offsets the physics step reads.
    /// </summary>
    /// <summary>
    /// `REGION_FLAGS` (tag 91), applied only to a region (the tag setter's case 0x5b tests the type) into
    /// the word an actor keeps its own flags in. The SDK names the three bits:
    /// 1 "does not track entry/exit", 2 "collides with cameras", 4 "intended to be rendered".
    /// </summary>
    public const uint RegionFlags = 91;

    public const uint PhysicsFlags = 87, PhysicsVelocity = 88, PhysicsRotVelocity = 89,
                      PhysicsElasticity = 232, PhysicsGravity = 243;

    /// <summary>
    /// `TIMER` — how long the thing lives, in milliseconds. The applier turns it into an **absolute**
    /// stamp on the spot (the tag setter's case 0x53: `expiry = value + now`), and that is the same field
    /// the corpse expiry writes, so the clock starts when the thing is *made* rather than when
    /// anything happens to it. A value of 0 is explicitly no timer.
    /// </summary>
    public const uint Timer = 83;

    /// <summary>
    /// The projectile tags. The applier is gated on thing type 4 exactly as the physics
    /// setter is gated on `MOVETYPE 3`, so a template that gives a `PROJECT_*` to anything else has
    /// it dropped. `EXPLOSION` (128) is a **template name**, resolved when the thing detonates
    /// rather than at load, and 114 of the 155 projectile templates leave it unset — a projectile
    /// with no explosion is an ordinary thing, and every gib chunk is one.
    /// </summary>
    public const uint ProjectFlags = 84, ProjectDamage = 85, ProjectDamageType = 86,
                      ProjectMaxDist = 136, Explosion = 128;

    /// <summary>
    /// The tags that describe a character (category 10 of the engine's table). Datatypes were
    /// checked against all 2,619 shipped templates: health, max health and XP are whole numbers,
    /// the speeds, stats and soaks are floats, the flags, type, team and aura are enums or bit
    /// fields, and the rest are strings.
    /// </summary>
    public const uint CastId = 81, ActorClan = 26, ActorAura = 27, ActorHealth = 52, ActorXp = 53,
                      ActorWalkSpeed = 54, ActorRunSpeed = 55, ActorAi = 56, ActorHeadModel = 57,
                      ActorHeadMotion = 58, ActorRpgStats = 59, ActorMaxHealth = 60,
                      TreasureClass = 92, ActorWeapon = 93, ActorFirePos = 94, ActorFlags = 114,
                      DropItem = 115, ActorType = 138, GibClass = 246, ActorTeam = 249,
                      ContainerHealth = 116, DestroyTemplate = 117;

    /// <summary>
    /// The item tags Vitomir reads. The names are the engine's own, from its tag table;
    /// which offset each one writes is **not** confirmed from the applier, so what justifies these
    /// three is the shipped data agreeing with the reload's use of them — every `WEAPON_AMMOTYPE` in
    /// the game resolves to an <see cref="ItemId"/>, and an ammo item's <see cref="ItemAmount"/>
    /// matches the magazine it fills. `item_flags` (tag 21) is deliberately **not** here: the field
    /// the reload tests is runtime state the inventory writes, not a template value.
    /// </summary>
    public const uint ItemType = 20, ItemAmount = 22, ItemId = 24;

    /// <summary>
    /// The rest of the item vocabulary, named by the SDK's own tag dictionary
    /// (<c>NodSDK/noteditortags.txt</c>) and checked against the shipped templates: every one of
    /// these ids carries the name the engine's compiled tag table gives it, and the
    /// values match 1.1's saves item for item.
    /// </summary>
    public const uint ItemFlags = 21, ItemBaseCost = 90, ItemSellFlags = 120;

    /// <summary>
    /// What an item looks like in a pane: <c>ICONSIZE</c> is `ENUM_ICONSIZE`, the width and height
    /// in grid cells (the broadsword is 2x3, a ring 1x1); <c>ICONTYPE</c> picks which icon set;
    /// <c>ICON</c> is a texture name with no extension — `swordBroadIcon`, `scrollIcon` — resolved
    /// through the same material search path every other UI texture uses.
    /// </summary>
    public const uint ItemIconSize = 17, ItemIconType = 18, ItemIcon = 19;

    /// <summary>
    /// <c>ITEM_TAG</c> (tag 25, `ENUM_ITEM_TAG`): the motion-set tag a **utility** item is carried
    /// on — `LFINGERS` (30) for every shipped one. The utility's wear reads it and bounds
    /// it below 0x2c, the size of the enum.
    /// </summary>
    public const uint ItemTag = 25;

    /// <summary>
    /// `ITEM_TEXT` — the `.ntx` entry a **BOOK** opens, and `DESCRIPTIONID` — the `.nls` key for
    /// its title. The two agree on every shipped document: `CuthbertLetter` names an entry in
    /// `prague.ntx` whose own `&lt;TITLE&gt;` is what `CUTHBERTLETTER` says in `game.nls`.
    /// </summary>
    public const uint ItemText = 30;
    /// <summary>A scroll's discipline, by name (`ITEM_SCROLLDISC`, 0x1c), and its level (`ITEM_SCROLLLEVEL`, 0x1d, held to 0..4).</summary>
    public const uint ItemScrollDiscipline = 28, ItemScrollLevel = 29;

    /// <summary>
    /// The two extra descriptions an item can carry, both `.nls` keys and both item-only (the
    /// SDK's dictionary gates each on <c>ITEM_TYPE</c> being 1, 5 or 6). The rollover shows
    /// <c>ITEM_IDENTIFIEDDESC</c> in place of the plain description once an item has been
    /// identified, and appends <c>ITEM_SPECIALDESC</c> to its detail block.
    /// </summary>
    public const uint ItemIdentifiedDesc = 33, ItemSpecialDesc = 34;

    /// <summary>
    /// The ten effect slots. Each is four consecutive tags — name, level, value, time — so slot
    /// <c>n</c> starts at <c>EffectName0 + n * 4</c>. An item names an effect from
    /// <c>misc/effects.ned</c> and then either sets a **level**, which indexes that effect's row of
    /// six values, or a **value**, which overrides it outright. The shipped data uses both: the
    /// buckler sets level 0, the rags set value 5.
    /// </summary>
    public const uint EffectName0 = 166, EffectStride = 4, EffectSlots = 10;

    /// <summary>
    /// The ten restrictions, two consecutive tags each — type then value — so restriction
    /// <c>n</c> starts at <c>RestrictionType0 + n * 2</c>. The type is
    /// <see cref="Rpg.RestrictionType"/>. Of the forty-nine shipped wearables that carry effects,
    /// fifteen gate on a creature type and **none** on a stat, so the stat half of the enum is
    /// authored capability the original never exercises — implemented anyway, because a mod can.
    /// </summary>
    public const uint RestrictionType0 = 206, RestrictionStride = 2, RestrictionSlots = 10;

    /// <summary>The stat tags, in the order <c>GetActorStat</c> indexes them (ACTOR_STAT_*).</summary>
    public static readonly uint[] Stat =
    [
        99, 100, 101, 102, 103, 104,        // strength, dexterity, stamina, perception, intelligence, wits
        105, 106, 107, 108, 109,            // blood, humanity, frenzy, generation, faith
        142, 143, 144, 145, 146, 147,       // charisma, manipulation, appearance, frenzy rating, blood rating, blood pool
        31, 32,                             // mana, mana pool
    ];

    /// <summary>The soak tags, in damage-type order (DAMAGE_TYPE_*).</summary>
    public static readonly uint[] Soak = [148, 149, 150, 151, 152, 153, 154, 155, 156, 157];

    /// <summary>
    /// Which `skeleton_&lt;n&gt;` template a vampire leaves behind when it dusts (tag 118). The
    /// engine formats the name and gives up quietly when there is no such template, which is why
    /// two of the shipped ids name nothing.
    /// </summary>
    public const uint SkeletonId = 118;

    /// <summary>
    /// `ARMOR_MODEL` (tag 79). What a shield or a suit of armour is called when it is on the
    /// character: a shield becomes `ARMOR_MODEL_SKELETONID` in the off hand and armour
    /// replaces the character's own model with `character_ARMOR_MODEL`. Eleven distinct
    /// values ship — `chain`, `plate`, `leather`, `upgrade`, and the shields. The tag applier
    /// sets this alongside `ARMOR_TYPE` (tag 80), which nothing ever
    /// reads, so it is not kept here (see).
    /// </summary>
    public const uint ArmorModel = 79;

    /// <summary>
    /// The weapon tags the per-attack reader does not handle by name. `WeaponMeleeType` and
    /// `WeaponMeleeType2` are the exception: they *are* per-attack (the first field of each record),
    /// but their names do not follow the `*1`/`*2` pattern the rest use.
    /// </summary>
    public const uint WeaponFlags = 0x27, WeaponType1 = 0x28, WeaponMeleeType = 0x29,
                      WeaponDefenseType = 0x2a, WeaponModel = 0x33, WeaponMeleeType2 = 0xe2,
                      WeaponFlags1 = 0xee, WeaponFlags2 = 0xef, WeaponThrowType = 0xf5,
                      WeaponThrowDist = 0xf7, WeaponThrowChance = 0xf8,
                      // `WEAPON_FIREPOS` (0x5f): where on the hand a shot leaves from.
                      WeaponFirePos = 0x5f;

    /// <summary>One attack's worth of weapon tags: `WEAPON_*1` or `WEAPON_*2`.</summary>
    public readonly record struct WeaponTags(uint Damage, uint DamageType, uint StrikeTime, uint Accuracy,
                                             uint ReloadTime, uint MaxAmmo, uint AmmoType, uint AmmoUse,
                                             uint MuzzleFlash, uint MaxDistance, uint MinDistance, uint Projectile);

    public static readonly WeaponTags WeaponFirst = new(
        Damage: 0x2b, DamageType: 0x83, StrikeTime: 0x2d, Accuracy: 0xec, ReloadTime: 0x2f,
        MaxAmmo: 0xa0, AmmoType: 0x9e, AmmoUse: 0xe3, MuzzleFlash: 0x85,
        MaxDistance: 0x31, MinDistance: 0x30, Projectile: 0x32);

    public static readonly WeaponTags WeaponSecond = new(
        Damage: 0x2c, DamageType: 0x84, StrikeTime: 0x2e, Accuracy: 0xed, ReloadTime: 0xa2,
        MaxAmmo: 0xa1, AmmoType: 0x9f, AmmoUse: 0xe4, MuzzleFlash: 0x86,
        MaxDistance: 0xa5, MinDistance: 0xa4, Projectile: 0xa3);
}

/// <summary>Thing types (tag 16).</summary>
public enum ThingType : uint
{
    Actor = 0, Item = 1, Prop = 2, Region = 3, Projectile = 4, Weapon = 5, Armor = 6, Player = 7,
    Container = 8, None = 9, Explosion = 10,
}

/// <summary>
/// The body a thing presents to collision (tag 12, THING_COLLIDE_*). Which one it is decides the
/// test, and each shape stands on the thing's own position: a sphere is centred there, a cylinder
/// and a box rise from it.
/// </summary>
public enum CollideShape : uint
{
    /// <summary>Nothing collides with it. This is what a thing with no <c>collide</c> tag gets.</summary>
    None = 0,
    /// <summary>A sphere centred on the thing, of its RADIUS.</summary>
    Sphere = 1,
    /// <summary>A cylinder of its RADIUS standing on it, HEIGHT tall.</summary>
    Cylinder = 2,
    /// <summary>Its model's own bounding box, turned and placed by the thing's transform.</summary>
    Box = 3,
}

/// <summary>
/// The template table. Like the engine, it loads global.not, darkAges.not, modernDay.not and
/// multiplayer.not, then every "_"-prefixed .not in misc/templates, into one namespace.
/// **The first loaded wins:** each file's templates go into one
/// hash table whose insert refuses a name it already holds, so of the two
/// names the four files share, global.not's copy is the one found. A scene
/// may name files of its own: a file already loaded is simply found again,
/// and a new one only adds the names nobody has yet — never replaces one (no shipped scene
/// names a file that exists, n1_utwn's `demo.not` being missing).
/// </summary>
public sealed class TemplateSet
{
    readonly List<NotFile> files = [];

    public TemplateSet(GameFiles game)
    {
        foreach (var name in new[] { "global", "darkAges", "modernDay", "multiplayer" })
            if (game.TryRead($"misc/templates/{name}.not", out var data)) files.Add(NotFile.Read(data, name + ".not"));
        foreach (var path in game.List("misc/templates/").Where(p => Path.GetFileName(p).StartsWith('_') && p.EndsWith(".not", StringComparison.OrdinalIgnoreCase)))
            files.Add(NotFile.Read(game.Read(path), path));
    }

    /// <summary>A scene's own file: after everything loaded, so it adds names and replaces none.</summary>
    public void AddFile(NotFile file) => files.Add(file);

    /// <summary>Every template name loaded, once each, in the order the files were read.</summary>
    public IEnumerable<string> Names
    {
        get
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in files)
                foreach (var name in f.ByName.Keys)
                    if (seen.Add(name)) yield return name;
        }
    }

    public NotTemplate? Find(string name)
    {
        foreach (var f in files)
            if (f.ByName.TryGetValue(name, out var t)) return t;
        return null;
    }

    /// <summary>A template's tags with inheritance flattened (child tags override the PARENT chain).</summary>
    /// <summary>Whether the table has a template of that name — what a spawn asks before building.</summary>
    public bool Has(string name) => Find(name) is not null;

    // A template's modifiers through its chain of base templates (the engine caps the walk at 128;
    // here `seen` stops a cycle instead), the same PARENT-chain flattening the engine does for a
    // treasure record (a later tag overrides, as it does here).
    public Dictionary<uint, NodTag> Resolve(string name)
    {
        var chain = new List<NotTemplate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var cur = name; !string.IsNullOrEmpty(cur) && seen.Add(cur);)
        {
            var t = Find(cur);
            if (t is null) break;
            chain.Add(t);
            cur = t.Parent;
        }
        var result = new Dictionary<uint, NodTag>();
        for (int i = chain.Count - 1; i >= 0; i--)
            foreach (var tag in chain[i].Tags) result[tag.Id] = tag;
        return result;
    }

    /// <summary>A placed thing's effective tags: its template's, overridden by the thing's own.</summary>
    public Dictionary<uint, NodTag> Resolve(NsdThing thing)
    {
        var tags = Resolve(thing.Template);
        foreach (var t in thing.Tags) if (t.Id != Tags.BasedOn) tags[t.Id] = t;
        return tags;
    }
}
